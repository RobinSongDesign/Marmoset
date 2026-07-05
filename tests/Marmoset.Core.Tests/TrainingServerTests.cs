using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Marmoset.Core;
using Marmoset.Core.Tests.Wire;
using Xunit;

namespace Marmoset.Core.Tests
{
    public class TrainingServerTests
    {
        // ---------------------------------------------------------------- 基础设施

        private static int GetFreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private sealed class ServerFixture : IDisposable
        {
            public ServerFixture(FakeAgent? agent = null)
            {
                Agent = agent ?? new FakeAgent();
                Session = new TrainingSession(Agent);
                Port = GetFreePort();
                Server = new TrainingServer(Session, Port);
                Server.Start();
            }

            public FakeAgent Agent { get; }
            public TrainingSession Session { get; }
            public TrainingServer Server { get; }
            public int Port { get; }

            public WireClient Connect() => new(Port);

            public void Dispose() => Server.Dispose();
        }

        private static void AssertError(Dictionary<string, object> message)
        {
            Assert.Equal("error", Msg.TypeOf(message));
            Assert.False(string.IsNullOrWhiteSpace((string)message["message"]));
        }

        // ---------------------------------------------------------------- 全流程

        [Fact]
        public void FullLoop_Handshake_Reset_Step_Close()
        {
            using var fixture = new ServerFixture(new FakeAgent(obsLength: 3) { RewardPerStep = 0.5f, EndOnDiscreteAction = 3 });
            using WireClient client = fixture.Connect();

            // handshake
            var ack = client.Request(Requests.Handshake());
            Assert.Equal("handshake_ack", Msg.TypeOf(ack));
            Assert.Equal(1, Msg.Int(ack["api_version"]));

            var obsSpace = Msg.Map(ack["observation_space"]);
            Assert.Equal("box", (string)obsSpace["type"]);
            Assert.Equal(new[] { 3 }, Msg.IntArray(obsSpace["shape"]));

            var actSpace = Msg.Map(ack["action_space"]);
            Assert.Equal("discrete", (string)actSpace["type"]);
            Assert.Equal(4, Msg.Int(actSpace["n"]));

            // reset（带 seed）
            var resetResult = client.Request(Requests.Reset(seed: 123));
            Assert.Equal("reset_result", Msg.TypeOf(resetResult));
            Assert.Equal(new[] { 0f, -1f, 42f }, Msg.FloatArray(resetResult["observation"]));
            Assert.Equal(123, fixture.Agent.LastSeed);

            // step（非终止）
            var step = client.Request(Requests.StepDiscrete(new[] { 1 }));
            Assert.Equal("step_result", Msg.TypeOf(step));
            Assert.Equal(new[] { 1f, 1f, 42f }, Msg.FloatArray(step["observation"]));
            Assert.Equal(0.5f, Msg.Float(step["reward"]));
            Assert.False(Msg.Bool(step["terminated"]));
            Assert.False(Msg.Bool(step["truncated"]));

            // step（触发 terminated）
            var terminal = client.Request(Requests.StepDiscrete(new[] { 3 }));
            Assert.True(Msg.Bool(terminal["terminated"]));
            Assert.False(Msg.Bool(terminal["truncated"]));

            // terminated 后 reset，再 step 正常
            var secondReset = client.Request(Requests.Reset());
            Assert.Equal("reset_result", Msg.TypeOf(secondReset));
            var afterReset = client.Request(Requests.StepDiscrete(new[] { 2 }));
            Assert.Equal("step_result", Msg.TypeOf(afterReset));

            // close：回 close_ack 后服务端断开
            var closeAck = client.Request(Requests.Close());
            Assert.Equal("close_ack", Msg.TypeOf(closeAck));
            Assert.True(client.WaitForServerDisconnect());
        }

        [Fact]
        public void MaxSteps_Truncation_ReportedOverWire()
        {
            using var fixture = new ServerFixture(new FakeAgent(maxSteps: 2));
            using WireClient client = fixture.Connect();
            client.Request(Requests.Handshake());
            client.Request(Requests.Reset());

            var s1 = client.Request(Requests.StepDiscrete(new[] { 0 }));
            var s2 = client.Request(Requests.StepDiscrete(new[] { 0 }));

            Assert.False(Msg.Bool(s1["truncated"]));
            Assert.True(Msg.Bool(s2["truncated"]));
            Assert.False(Msg.Bool(s2["terminated"]));

            // 截断后未 reset 直接 step → error
            AssertError(client.Request(Requests.StepDiscrete(new[] { 0 })));
        }

        // ---------------------------------------------------------------- 状态机违规

        [Fact]
        public void MessageBeforeHandshake_ReturnsError_ConnectionSurvives()
        {
            using var fixture = new ServerFixture();
            using WireClient client = fixture.Connect();

            AssertError(client.Request(Requests.Reset()));
            AssertError(client.Request(Requests.StepDiscrete(new[] { 0 })));

            // 之后正常握手仍可用
            Assert.Equal("handshake_ack", Msg.TypeOf(client.Request(Requests.Handshake())));
        }

        [Fact]
        public void StepBeforeReset_ReturnsError()
        {
            using var fixture = new ServerFixture();
            using WireClient client = fixture.Connect();
            client.Request(Requests.Handshake());

            AssertError(client.Request(Requests.StepDiscrete(new[] { 1 })));

            // reset 之后 step 正常
            client.Request(Requests.Reset());
            Assert.Equal("step_result", Msg.TypeOf(client.Request(Requests.StepDiscrete(new[] { 1 }))));
        }

        [Fact]
        public void StepAfterTerminated_WithoutReset_ReturnsError()
        {
            using var fixture = new ServerFixture(new FakeAgent { EndOnDiscreteAction = 3 });
            using WireClient client = fixture.Connect();
            client.Request(Requests.Handshake());
            client.Request(Requests.Reset());

            var terminal = client.Request(Requests.StepDiscrete(new[] { 3 }));
            Assert.True(Msg.Bool(terminal["terminated"]));

            AssertError(client.Request(Requests.StepDiscrete(new[] { 1 })));

            // reset 恢复
            Assert.Equal("reset_result", Msg.TypeOf(client.Request(Requests.Reset())));
            Assert.Equal("step_result", Msg.TypeOf(client.Request(Requests.StepDiscrete(new[] { 1 }))));
        }

        [Fact]
        public void DuplicateHandshake_ReturnsError()
        {
            using var fixture = new ServerFixture();
            using WireClient client = fixture.Connect();
            client.Request(Requests.Handshake());

            AssertError(client.Request(Requests.Handshake()));
        }

        [Fact]
        public void WrongApiVersion_ErrorThenDisconnect_ServerAcceptsNewClient()
        {
            using var fixture = new ServerFixture();

            using (WireClient bad = fixture.Connect())
            {
                AssertError(bad.Request(Requests.Handshake(apiVersion: 99)));
                Assert.True(bad.WaitForServerDisconnect()); // 版本不匹配：error 后断开
            }

            // 服务端回到监听，新客户端可用
            using WireClient good = fixture.Connect();
            Assert.Equal("handshake_ack", Msg.TypeOf(good.Request(Requests.Handshake())));
        }

        [Fact]
        public void UnknownMessageType_ReturnsError()
        {
            using var fixture = new ServerFixture();
            using WireClient client = fixture.Connect();
            client.Request(Requests.Handshake());

            var unknown = RawMsgPack.Map(("type", RawMsgPack.Str("teleport")));
            AssertError(client.Request(unknown));
        }

        [Fact]
        public void MalformedPayload_ReturnsError_ConnectionSurvives()
        {
            using var fixture = new ServerFixture();
            using WireClient client = fixture.Connect();
            client.Request(Requests.Handshake());

            // 合法帧长度前缀 + 非 map 的 msgpack（数组）
            AssertError(client.Request(new byte[] { 0x93, 0x01, 0x02, 0x03 }));

            // 连接仍可用
            Assert.Equal("reset_result", Msg.TypeOf(client.Request(Requests.Reset())));
        }

        [Fact]
        public void NonZeroEnvId_ReturnsError()
        {
            using var fixture = new ServerFixture();
            using WireClient client = fixture.Connect();
            client.Request(Requests.Handshake());

            AssertError(client.Request(Requests.Reset(envId: 1)));
        }

        [Fact]
        public void WrongActionKind_ReturnsError_ConnectionSurvives()
        {
            using var fixture = new ServerFixture(); // 离散空间
            using WireClient client = fixture.Connect();
            client.Request(Requests.Handshake());
            client.Request(Requests.Reset());

            // 离散空间发连续动作
            AssertError(client.Request(Requests.StepContinuous(new[] { 0.5f })));
            // 分支数不匹配（1 分支发 2 个）
            AssertError(client.Request(Requests.StepDiscrete(new[] { 1, 2 })));
            // 动作值越界
            AssertError(client.Request(Requests.StepDiscrete(new[] { 9 })));

            // 会话未被破坏
            Assert.Equal("step_result", Msg.TypeOf(client.Request(Requests.StepDiscrete(new[] { 1 }))));
        }

        [Fact]
        public void AgentException_ReturnsError_ConnectionSurvives()
        {
            var agent = new FakeAgent();
            using var fixture = new ServerFixture(agent);
            using WireClient client = fixture.Connect();
            client.Request(Requests.Handshake());
            client.Request(Requests.Reset());

            agent.ThrowOnAction = true;
            AssertError(client.Request(Requests.StepDiscrete(new[] { 1 })));

            agent.ThrowOnAction = false;
            Assert.Equal("step_result", Msg.TypeOf(client.Request(Requests.StepDiscrete(new[] { 1 }))));
        }

        // ---------------------------------------------------------------- 空间描述

        [Fact]
        public void MultiDiscreteSpace_SerializedAsNvec()
        {
            using var fixture = new ServerFixture(new FakeAgent(actionSpec: ActionSpec.Discrete(3, 2)));
            using WireClient client = fixture.Connect();

            var ack = client.Request(Requests.Handshake());
            var actSpace = Msg.Map(ack["action_space"]);
            Assert.Equal("multi_discrete", (string)actSpace["type"]);
            Assert.Equal(new[] { 3, 2 }, Msg.IntArray(actSpace["nvec"]));

            // 多分支 step
            client.Request(Requests.Reset());
            var step = client.Request(Requests.StepDiscrete(new[] { 2, 1 }));
            Assert.Equal("step_result", Msg.TypeOf(step));
        }

        [Fact]
        public void ContinuousSpace_SerializedAsBoxWithBounds()
        {
            var agent = new FakeAgent(actionSpec: ActionSpec.Continuous(2));
            using var fixture = new ServerFixture(agent);
            using WireClient client = fixture.Connect();

            var ack = client.Request(Requests.Handshake());
            var actSpace = Msg.Map(ack["action_space"]);
            Assert.Equal("box", (string)actSpace["type"]);
            Assert.Equal(new[] { 2 }, Msg.IntArray(actSpace["shape"]));
            Assert.Equal(-1f, Msg.Float(actSpace["low"]));
            Assert.Equal(1f, Msg.Float(actSpace["high"]));

            client.Request(Requests.Reset());
            var step = client.Request(Requests.StepContinuous(new[] { 0.25f, -0.75f }));
            Assert.Equal("step_result", Msg.TypeOf(step));
            Assert.Equal(new[] { 0.25f, -0.75f }, agent.LastContinuousActions);
        }

        // ---------------------------------------------------------------- 断开重连与生命周期

        [Fact]
        public void AbruptDisconnect_ServerReturnsToListening_NewClientMustHandshake()
        {
            using var fixture = new ServerFixture();

            using (WireClient first = fixture.Connect())
            {
                first.Request(Requests.Handshake());
                first.Request(Requests.Reset());
            } // 不发 close，直接断开

            // 服务端回到监听；新连接是全新协议状态（必须重新握手）
            using WireClient second = fixture.Connect();
            AssertError(second.Request(Requests.Reset())); // 未握手
            Assert.Equal("handshake_ack", Msg.TypeOf(second.Request(Requests.Handshake())));
            Assert.Equal("reset_result", Msg.TypeOf(second.Request(Requests.Reset())));
        }

        [Fact]
        public void StopAndDispose_AreIdempotent_AndInterruptBlockedAccept()
        {
            var fixture = new ServerFixture();
            Assert.True(fixture.Server.IsRunning);
            Assert.Equal($"Listening on {fixture.Port}", fixture.Server.StatusText);

            fixture.Server.Stop();
            Assert.False(fixture.Server.IsRunning);
            Assert.Equal("Stopped", fixture.Server.StatusText);

            // 幂等：重复 Stop/Dispose 不抛
            fixture.Server.Stop();
            fixture.Server.Dispose();
            fixture.Server.Dispose();

            // 停止后端口已释放，连接被拒
            Assert.ThrowsAny<SocketException>(() =>
            {
                using var tcp = new TcpClient();
                tcp.Connect(IPAddress.Loopback, fixture.Port);
            });
        }

        [Fact]
        public void Stop_InterruptsBlockedRead_WhileClientConnected()
        {
            var fixture = new ServerFixture();
            using WireClient client = fixture.Connect();
            client.Request(Requests.Handshake()); // 确认服务线程已进入读循环

            fixture.Server.Stop(); // 必须能中断阻塞的 Read 并及时返回
            Assert.False(fixture.Server.IsRunning);
            Assert.True(client.WaitForServerDisconnect());
        }

        [Fact]
        public void StatusChanged_FiresOnTransitions()
        {
            var agent = new FakeAgent();
            var session = new TrainingSession(agent);
            int port = GetFreePort();
            using var server = new TrainingServer(session, port);

            var statuses = new List<string>();
            var connected = new ManualResetEventSlim();
            server.StatusChanged += () =>
            {
                string status = server.StatusText;
                lock (statuses)
                    statuses.Add(status);
                if (status == "Client connected")
                    connected.Set();
            };

            server.Start();
            Assert.True(server.IsRunning);

            using (var client = new WireClient(port))
            {
                Assert.True(connected.Wait(TimeSpan.FromSeconds(5)));
                Assert.Equal("Client connected", server.StatusText);
            }

            server.Stop();
            lock (statuses)
            {
                Assert.Contains($"Listening on {port}", statuses);
                Assert.Contains("Client connected", statuses);
                Assert.Contains("Stopped", statuses);
            }
        }

        [Fact]
        public void Restart_AfterStop_Works()
        {
            var agent = new FakeAgent();
            var session = new TrainingSession(agent);
            int port = GetFreePort();
            using var server = new TrainingServer(session, port);

            server.Start();
            server.Stop();
            server.Start(); // 重新启动

            using var client = new WireClient(port);
            Assert.Equal("handshake_ack", Msg.TypeOf(client.Request(Requests.Handshake())));
            server.Stop();
        }
    }
}
