using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Marmoset.Core.Tests.Wire
{
    /// <summary>
    /// 逐字节手工构造 MessagePack 载荷（不经过 MessagePack-CSharp 序列化器），
    /// 模拟 Python msgpack 客户端的线上格式，验证服务端的真实互通性。
    /// </summary>
    internal static class RawMsgPack
    {
        public static byte[] Map(params (string Key, byte[] Value)[] entries)
        {
            if (entries.Length > 15)
                throw new ArgumentException("fixmap supports at most 15 entries.");
            var bytes = new List<byte> { (byte)(0x80 | entries.Length) }; // fixmap
            foreach ((string key, byte[] value) in entries)
            {
                AppendString(bytes, key);
                bytes.AddRange(value);
            }
            return bytes.ToArray();
        }

        public static byte[] Str(string value)
        {
            var bytes = new List<byte>();
            AppendString(bytes, value);
            return bytes.ToArray();
        }

        public static byte[] Int(int value)
        {
            if (value >= 0 && value <= 127)
                return new[] { (byte)value }; // positive fixint
            var bytes = new byte[5];
            bytes[0] = 0xD2; // int32 big-endian
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(1), value);
            return bytes;
        }

        public static byte[] Nil() => new byte[] { 0xC0 };

        public static byte[] IntArray(params int[] values)
        {
            if (values.Length > 15)
                throw new ArgumentException("fixarray supports at most 15 entries.");
            var bytes = new List<byte> { (byte)(0x90 | values.Length) }; // fixarray
            foreach (int v in values)
                bytes.AddRange(Int(v));
            return bytes.ToArray();
        }

        public static byte[] FloatArray(params float[] values)
        {
            if (values.Length > 15)
                throw new ArgumentException("fixarray supports at most 15 entries.");
            var bytes = new List<byte> { (byte)(0x90 | values.Length) };
            foreach (float v in values)
            {
                var item = new byte[5];
                item[0] = 0xCA; // float32 big-endian
                BinaryPrimitives.WriteSingleBigEndian(item.AsSpan(1), v);
                bytes.AddRange(item);
            }
            return bytes.ToArray();
        }

        private static void AppendString(List<byte> target, string value)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            if (utf8.Length <= 31)
            {
                target.Add((byte)(0xA0 | utf8.Length)); // fixstr
            }
            else if (utf8.Length <= 255)
            {
                target.Add(0xD9); // str8
                target.Add((byte)utf8.Length);
            }
            else
            {
                throw new ArgumentException("Test helper only supports short strings.");
            }
            target.AddRange(utf8);
        }
    }

    /// <summary>协议客户端消息的便捷构造。</summary>
    internal static class Requests
    {
        public static byte[] Handshake(int apiVersion = 1) => RawMsgPack.Map(
            ("type", RawMsgPack.Str("handshake")),
            ("api_version", RawMsgPack.Int(apiVersion)));

        public static byte[] Reset(int envId = 0, int? seed = null) => RawMsgPack.Map(
            ("type", RawMsgPack.Str("reset")),
            ("env_id", RawMsgPack.Int(envId)),
            ("seed", seed.HasValue ? RawMsgPack.Int(seed.Value) : RawMsgPack.Nil()));

        public static byte[] StepDiscrete(int[] actions, int envId = 0) => RawMsgPack.Map(
            ("type", RawMsgPack.Str("step")),
            ("env_id", RawMsgPack.Int(envId)),
            ("discrete", RawMsgPack.IntArray(actions)),
            ("continuous", RawMsgPack.Nil()));

        public static byte[] StepContinuous(float[] actions, int envId = 0) => RawMsgPack.Map(
            ("type", RawMsgPack.Str("step")),
            ("env_id", RawMsgPack.Int(envId)),
            ("discrete", RawMsgPack.Nil()),
            ("continuous", RawMsgPack.FloatArray(actions)));

        public static byte[] Close() => RawMsgPack.Map(
            ("type", RawMsgPack.Str("close")));
    }
}
