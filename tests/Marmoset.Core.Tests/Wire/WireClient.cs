using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using MessagePack;

namespace Marmoset.Core.Tests.Wire
{
    /// <summary>
    /// 原始 TCP 协议客户端：按 PROTOCOL.md 收发 4 字节小端长度前缀 + MessagePack 帧。
    /// 发送侧使用 RawMsgPack 手工编码；接收侧用 MessagePackSerializer 解为通用字典校验。
    /// </summary>
    internal sealed class WireClient : IDisposable
    {
        private readonly TcpClient _tcp;
        private readonly NetworkStream _stream;

        public WireClient(int port)
        {
            _tcp = new TcpClient { NoDelay = true };
            _tcp.Connect(IPAddress.Loopback, port);
            _stream = _tcp.GetStream();
            _stream.ReadTimeout = 10_000; // 服务器无响应时测试快速失败而非挂死
        }

        public void SendPayload(byte[] payload)
        {
            var header = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);
            _stream.Write(header, 0, header.Length);
            _stream.Write(payload, 0, payload.Length);
            _stream.Flush();
        }

        public Dictionary<string, object> Receive()
        {
            byte[] header = ReadExactly(4) ?? throw new IOException("Server closed the connection.");
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(header);
            byte[] payload = ReadExactly((int)length) ?? throw new IOException("Connection dropped mid-frame.");
            return MessagePackSerializer.Deserialize<Dictionary<string, object>>(payload);
        }

        public Dictionary<string, object> Request(byte[] payload)
        {
            SendPayload(payload);
            return Receive();
        }

        /// <summary>等待服务端关闭连接（读到 EOF 返回 true）。</summary>
        public bool WaitForServerDisconnect(int timeoutMs = 5000)
        {
            _stream.ReadTimeout = timeoutMs;
            try
            {
                var buffer = new byte[1];
                return _stream.Read(buffer, 0, 1) == 0;
            }
            catch (IOException)
            {
                return false; // 超时：连接仍然存活
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }

        public void Dispose()
        {
            try { _stream.Dispose(); } catch { }
            try { _tcp.Dispose(); } catch { }
        }

        private byte[]? ReadExactly(int count)
        {
            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = _stream.Read(buffer, offset, count - offset);
                if (read == 0)
                    return null;
                offset += read;
            }
            return buffer;
        }
    }

    /// <summary>响应字典字段的类型无关取值（msgpack 整数可能解码为多种 CLR 数值类型）。</summary>
    internal static class Msg
    {
        public static string TypeOf(Dictionary<string, object> message) => (string)message["type"];

        public static int Int(object value) => Convert.ToInt32(value, CultureInfo.InvariantCulture);

        public static float Float(object value) => Convert.ToSingle(value, CultureInfo.InvariantCulture);

        public static bool Bool(object value) => (bool)value;

        public static object[] Array(object value) => (object[])value;

        public static Dictionary<object, object> Map(object value) => (Dictionary<object, object>)value;

        public static float[] FloatArray(object value)
        {
            object[] items = Array(value);
            var result = new float[items.Length];
            for (int i = 0; i < items.Length; i++)
                result[i] = Float(items[i]);
            return result;
        }

        public static int[] IntArray(object value)
        {
            object[] items = Array(value);
            var result = new int[items.Length];
            for (int i = 0; i < items.Length; i++)
                result[i] = Int(items[i]);
            return result;
        }
    }
}
