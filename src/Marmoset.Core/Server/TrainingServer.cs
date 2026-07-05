using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MessagePack;

namespace Marmoset.Core
{
    /// <summary>
    /// TCP 训练服务器：监听 Python 客户端连接，按 docs/PROTOCOL.md 驱动 TrainingSession。
    /// 生命周期：Start() 后在后台线程监听/服务，Stop()/Dispose() 幂等关闭。
    /// 本类型的公共表面是稳定契约（GH 组件依赖它）；内部实现见 Server 工作包。
    ///
    /// 线程模型：Start() 同步打开监听 socket（端口冲突立即在调用线程抛出），随后单个后台线程
    /// 负责 Accept 与逐帧服务（v1 单客户端、严格请求-响应，单线程即可且天然串行）。
    /// Stop() 通过关闭 listener/client socket 中断阻塞的 Accept/Read，并 Join 后台线程。
    /// 安全：只绑定 127.0.0.1，不接受外部连接。
    /// </summary>
    public sealed class TrainingServer : IDisposable
    {
        public const int DefaultPort = 5555;

        private const int ApiVersion = 1;
        private const uint MaxFrameLength = 64 * 1024 * 1024; // 帧长上限，防御损坏的长度前缀

        private readonly object _gate = new();
        private TcpListener? _listener;
        private Thread? _thread;
        private TcpClient? _client;
        private volatile bool _stopRequested;
        private volatile bool _isRunning;
        private string _statusText = "Stopped";

        public TrainingServer(TrainingSession session, int port = DefaultPort)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            if (port is < 1 or > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));
            Port = port;
        }

        public TrainingSession Session { get; }

        public int Port { get; }

        public bool IsRunning => _isRunning;

        /// <summary>人类可读状态，如 "Listening on 5555" / "Client connected" / "Stopped"。</summary>
        public string StatusText
        {
            get
            {
                lock (_gate)
                    return _statusText;
            }
        }

        /// <summary>IsRunning/StatusText 变化时触发（可能来自后台线程）。</summary>
        public event Action? StatusChanged;

        public void Start()
        {
            Thread thread;
            lock (_gate)
            {
                if (_thread != null)
                    return; // 已在运行，幂等

                var listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start(); // 端口被占用时在调用线程直接抛出
                _listener = listener;
                _stopRequested = false;
                _isRunning = true;
                thread = new Thread(() => ServerLoop(listener))
                {
                    IsBackground = true,
                    Name = $"Marmoset.TrainingServer:{Port}",
                };
                _thread = thread;
            }

            SetStatus($"Listening on {Port}");
            thread.Start();
        }

        public void Stop()
        {
            Thread? thread;
            lock (_gate)
            {
                _stopRequested = true;
                try { _listener?.Stop(); } catch { /* 忽略关闭竞态 */ }
                _listener = null;
                try { _client?.Close(); } catch { /* 中断阻塞 Read，忽略竞态 */ }
                _client = null;
                thread = _thread;
                _thread = null;
            }

            if (thread != null && thread != Thread.CurrentThread)
            {
                try { thread.Join(TimeSpan.FromSeconds(5)); }
                catch (ThreadStateException) { /* 尚未 Start 的极端竞态 */ }
            }

            _isRunning = false;
            SetStatus("Stopped");
        }

        public void Dispose() => Stop();

        // ---------------------------------------------------------------- 服务循环

        private void ServerLoop(TcpListener listener)
        {
            try
            {
                while (!_stopRequested)
                {
                    SetStatus($"Listening on {Port}");

                    TcpClient client;
                    try
                    {
                        client = listener.AcceptTcpClient();
                    }
                    catch (SocketException)
                    {
                        break; // Stop() 关闭了 listener
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        break; // Stop() 与循环迭代竞态：listener 已停止时 Accept 抛此异常
                    }

                    if (_stopRequested)
                    {
                        try { client.Close(); } catch { }
                        break;
                    }

                    lock (_gate)
                        _client = client;
                    SetStatus("Client connected");

                    try
                    {
                        ServeClient(client);
                    }
                    catch
                    {
                        // 连接级异常（对端断开、Stop() 关 socket 等）：丢弃该连接，回到监听
                    }
                    finally
                    {
                        lock (_gate)
                            _client = null;
                        try { client.Close(); } catch { }
                    }
                }
            }
            finally
            {
                _isRunning = false;
                SetStatus("Stopped");
            }
        }

        /// <summary>服务单个客户端直至其断开/close/致命协议错误。协议状态机在此维护。</summary>
        private void ServeClient(TcpClient client)
        {
            client.NoDelay = true;
            using NetworkStream stream = client.GetStream();

            bool handshaked = false;  // 规则 1：handshake 前置
            bool hasReset = false;    // 规则 2：首次 step 前必须 reset
            bool needsReset = false;  // 规则 3：terminated/truncated 后必须 reset

            while (!_stopRequested)
            {
                byte[]? payload = TryReadFrame(stream);
                if (payload == null)
                    return; // 对端断开：会话结束，回到监听

                Request request;
                try
                {
                    request = ParseRequest(payload);
                }
                catch
                {
                    WriteFrame(stream, BuildError("Malformed message: expected a MessagePack string-key map."));
                    continue;
                }

                if (!handshaked && request.Type != "handshake")
                {
                    WriteFrame(stream, BuildError("Protocol violation: 'handshake' must be the first message."));
                    continue;
                }

                switch (request.Type)
                {
                    case "handshake":
                        if (handshaked)
                        {
                            WriteFrame(stream, BuildError("Protocol violation: handshake already completed."));
                            break;
                        }
                        if (request.ApiVersion != ApiVersion)
                        {
                            WriteFrame(stream, BuildError(
                                $"Unsupported api_version {(request.ApiVersion?.ToString() ?? "nil")}; this server implements version {ApiVersion}."));
                            return; // 协议规定：版本不匹配回 error 并断开
                        }
                        handshaked = true;
                        WriteFrame(stream, BuildHandshakeAck());
                        break;

                    case "reset":
                    {
                        if (!ValidateEnvId(request, stream))
                            break;

                        float[] observation;
                        try
                        {
                            observation = Session.Reset(request.Seed);
                        }
                        catch (Exception ex)
                        {
                            WriteFrame(stream, BuildError($"Agent reset failed: {ex.Message}"));
                            break;
                        }
                        hasReset = true;
                        needsReset = false;
                        WriteFrame(stream, BuildResetResult(observation));
                        break;
                    }

                    case "step":
                    {
                        if (!ValidateEnvId(request, stream))
                            break;
                        if (!hasReset)
                        {
                            WriteFrame(stream, BuildError("Protocol violation: 'step' received before the first 'reset'."));
                            break;
                        }
                        if (needsReset)
                        {
                            WriteFrame(stream, BuildError(
                                "Protocol violation: episode has ended (terminated or truncated); 'reset' is required before the next 'step'."));
                            break;
                        }

                        ActionBuffer actions;
                        ActionSpec spec = Session.Agent.ActionSpec;
                        if (spec.IsDiscrete)
                        {
                            if (request.Discrete is not { Length: > 0 })
                            {
                                WriteFrame(stream, BuildError("Action space is discrete: 'discrete' must be a non-empty int array."));
                                break;
                            }
                            actions = ActionBuffer.FromDiscrete(request.Discrete);
                        }
                        else
                        {
                            if (request.Continuous is not { Length: > 0 })
                            {
                                WriteFrame(stream, BuildError("Action space is continuous: 'continuous' must be a non-empty float array."));
                                break;
                            }
                            actions = ActionBuffer.FromContinuous(request.Continuous);
                        }

                        StepResult result;
                        try
                        {
                            result = Session.Step(actions);
                        }
                        catch (ArgumentException ex)
                        {
                            WriteFrame(stream, BuildError(ex.Message)); // 动作与规格不匹配
                            break;
                        }
                        catch (Exception ex)
                        {
                            WriteFrame(stream, BuildError($"Agent step failed: {ex.Message}"));
                            break;
                        }

                        if (result.Terminated || result.Truncated)
                            needsReset = true;
                        WriteFrame(stream, BuildStepResult(result));
                        break;
                    }

                    case "close":
                        WriteFrame(stream, BuildCloseAck());
                        return; // 回 close_ack 后断开

                    default:
                        WriteFrame(stream, BuildError($"Unknown message type '{request.Type ?? "nil"}'."));
                        break;
                }
            }
        }

        private static bool ValidateEnvId(Request request, NetworkStream stream)
        {
            int envId = request.EnvId ?? 0;
            if (envId == 0)
                return true;
            WriteFrame(stream, BuildError($"env_id {envId} is not available: v1 supports a single environment with env_id 0."));
            return false;
        }

        // ---------------------------------------------------------------- 帧收发

        private static byte[]? TryReadFrame(NetworkStream stream)
        {
            Span<byte> header = stackalloc byte[4];
            if (!ReadExactly(stream, header))
                return null;

            uint length = BinaryPrimitives.ReadUInt32LittleEndian(header);
            if (length == 0 || length > MaxFrameLength)
                throw new IOException($"Invalid frame length {length}.");

            var payload = new byte[length];
            if (!ReadExactly(stream, payload))
                return null; // 帧中途断开，视作断开
            return payload;
        }

        private static bool ReadExactly(NetworkStream stream, Span<byte> buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer.Slice(offset));
                if (read == 0)
                    return false;
                offset += read;
            }
            return true;
        }

        private static void WriteFrame(NetworkStream stream, byte[] payload)
        {
            Span<byte> header = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);
            stream.Write(header);
            stream.Write(payload);
            stream.Flush();
        }

        // ---------------------------------------------------------------- 消息解析
        // 手写 MessagePackReader/Writer：完全掌控线上格式（string-key map、无多余 nil 键），
        // 与 Python msgpack 直接互通，不依赖运行时动态生成的对象映射。

        private sealed class Request
        {
            public string? Type;
            public int? ApiVersion;
            public int? EnvId;
            public int? Seed;
            public int[]? Discrete;
            public float[]? Continuous;
        }

        private static Request ParseRequest(byte[] payload)
        {
            var reader = new MessagePackReader(payload);
            var request = new Request();

            int count = reader.ReadMapHeader();
            for (int i = 0; i < count; i++)
            {
                string? key = reader.ReadString();
                switch (key)
                {
                    case "type":
                        request.Type = reader.ReadString();
                        break;
                    case "api_version":
                        request.ApiVersion = reader.TryReadNil() ? null : reader.ReadInt32();
                        break;
                    case "env_id":
                        request.EnvId = reader.TryReadNil() ? null : reader.ReadInt32();
                        break;
                    case "seed":
                        request.Seed = reader.TryReadNil() ? null : reader.ReadInt32();
                        break;
                    case "discrete":
                        if (reader.TryReadNil())
                        {
                            request.Discrete = null;
                        }
                        else
                        {
                            int n = reader.ReadArrayHeader();
                            var values = new int[n];
                            for (int j = 0; j < n; j++)
                                values[j] = reader.ReadInt32();
                            request.Discrete = values;
                        }
                        break;
                    case "continuous":
                        if (reader.TryReadNil())
                        {
                            request.Continuous = null;
                        }
                        else
                        {
                            int n = reader.ReadArrayHeader();
                            var values = new float[n];
                            for (int j = 0; j < n; j++)
                                values[j] = reader.ReadSingle(); // 接受 float32/float64/int
                            request.Continuous = values;
                        }
                        break;
                    default:
                        reader.Skip(); // 未知字段：跳过（向前兼容）
                        break;
                }
            }
            return request;
        }

        // ---------------------------------------------------------------- 消息构造

        private byte[] BuildHandshakeAck()
        {
            ObservationSpec obsSpec = Session.Agent.ObservationSpec;
            ActionSpec actSpec = Session.Agent.ActionSpec;

            var buffer = new ArrayBufferWriter<byte>();
            var writer = new MessagePackWriter(buffer);

            writer.WriteMapHeader(4);
            writer.Write("type");
            writer.Write("handshake_ack");
            writer.Write("api_version");
            writer.Write(ApiVersion);

            // 观测空间：{ "type": "box", "shape": [len] }
            writer.Write("observation_space");
            writer.WriteMapHeader(2);
            writer.Write("type");
            writer.Write("box");
            writer.Write("shape");
            writer.WriteArrayHeader(1);
            writer.Write(obsSpec.VectorLength);

            // 动作空间三选一（见 PROTOCOL.md「Space 描述」）
            writer.Write("action_space");
            if (actSpec.IsDiscrete)
            {
                if (actSpec.DiscreteBranchSizes.Count == 1)
                {
                    writer.WriteMapHeader(2);
                    writer.Write("type");
                    writer.Write("discrete");
                    writer.Write("n");
                    writer.Write(actSpec.DiscreteBranchSizes[0]);
                }
                else
                {
                    writer.WriteMapHeader(2);
                    writer.Write("type");
                    writer.Write("multi_discrete");
                    writer.Write("nvec");
                    writer.WriteArrayHeader(actSpec.DiscreteBranchSizes.Count);
                    for (int i = 0; i < actSpec.DiscreteBranchSizes.Count; i++)
                        writer.Write(actSpec.DiscreteBranchSizes[i]);
                }
            }
            else
            {
                writer.WriteMapHeader(4);
                writer.Write("type");
                writer.Write("box");
                writer.Write("shape");
                writer.WriteArrayHeader(1);
                writer.Write(actSpec.ContinuousSize);
                writer.Write("low");
                writer.Write(-1.0);
                writer.Write("high");
                writer.Write(1.0);
            }

            writer.Flush();
            return buffer.WrittenSpan.ToArray();
        }

        private static byte[] BuildResetResult(float[] observation)
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new MessagePackWriter(buffer);
            writer.WriteMapHeader(2);
            writer.Write("type");
            writer.Write("reset_result");
            writer.Write("observation");
            WriteFloatArray(ref writer, observation);
            writer.Flush();
            return buffer.WrittenSpan.ToArray();
        }

        private static byte[] BuildStepResult(in StepResult result)
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new MessagePackWriter(buffer);
            writer.WriteMapHeader(5);
            writer.Write("type");
            writer.Write("step_result");
            writer.Write("observation");
            WriteFloatArray(ref writer, result.Observation);
            writer.Write("reward");
            writer.Write(result.Reward);
            writer.Write("terminated");
            writer.Write(result.Terminated);
            writer.Write("truncated");
            writer.Write(result.Truncated);
            writer.Flush();
            return buffer.WrittenSpan.ToArray();
        }

        private static byte[] BuildCloseAck()
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new MessagePackWriter(buffer);
            writer.WriteMapHeader(1);
            writer.Write("type");
            writer.Write("close_ack");
            writer.Flush();
            return buffer.WrittenSpan.ToArray();
        }

        private static byte[] BuildError(string message)
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new MessagePackWriter(buffer);
            writer.WriteMapHeader(2);
            writer.Write("type");
            writer.Write("error");
            writer.Write("message");
            writer.Write(message);
            writer.Flush();
            return buffer.WrittenSpan.ToArray();
        }

        private static void WriteFloatArray(ref MessagePackWriter writer, float[] values)
        {
            writer.WriteArrayHeader(values.Length);
            for (int i = 0; i < values.Length; i++)
                writer.Write(values[i]);
        }

        // ---------------------------------------------------------------- 状态

        private void SetStatus(string text)
        {
            lock (_gate)
            {
                if (_statusText == text)
                    return;
                _statusText = text;
            }
            StatusChanged?.Invoke();
        }
    }
}
