using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Marmoset.Core.Tests
{
    /// <summary>
    /// 用纯 C# 手工编码 protobuf，生成能被 ONNX Runtime 加载的最小模型文件：
    /// - 连续策略：observation(float[1,d]) --Identity--> action(float[1,d])
    /// - 离散策略：observation(float[1,d]) --Cast(INT64)--> action(int64[1,d])
    /// 字段号对照 onnx.proto（ModelProto/GraphProto/NodeProto/...）。
    /// </summary>
    internal static class OnnxModelFactory
    {
        private const int ElemFloat = 1; // TensorProto.DataType.FLOAT
        private const int ElemInt64 = 7; // TensorProto.DataType.INT64

        public static string WriteContinuousIdentityModel(int dim, string? inputName = null, string? outputName = null)
        {
            byte[] model = BuildModel(inputName ?? "observation", outputName ?? "action", dim, castToInt64: false);
            return WriteTempFile(model);
        }

        public static string WriteDiscreteCastModel(int dim, string? inputName = null, string? outputName = null)
        {
            byte[] model = BuildModel(inputName ?? "observation", outputName ?? "action", dim, castToInt64: true);
            return WriteTempFile(model);
        }

        private static string WriteTempFile(byte[] model)
        {
            string dir = Path.Combine(Path.GetTempPath(), "marmoset-tests");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".onnx");
            File.WriteAllBytes(path, model);
            return path;
        }

        private static byte[] BuildModel(string inputName, string outputName, int dim, bool castToInt64)
        {
            // NodeProto
            var node = new List<byte>();
            AddString(node, 1, inputName);                       // input
            AddString(node, 2, outputName);                      // output
            AddString(node, 4, castToInt64 ? "Cast" : "Identity"); // op_type
            if (castToInt64)
            {
                // AttributeProto { name="to", i=INT64(7), type=INT(2) }
                var attr = new List<byte>();
                AddString(attr, 1, "to");
                AddVarint(attr, 3, ElemInt64);
                AddVarint(attr, 20, 2);
                AddBytes(node, 5, attr);                         // attribute
            }

            // GraphProto
            var graph = new List<byte>();
            AddBytes(graph, 1, node);                            // node
            AddString(graph, 2, "marmoset_test_graph");          // name
            AddBytes(graph, 11, BuildValueInfo(inputName, ElemFloat, dim));               // input
            AddBytes(graph, 12, BuildValueInfo(outputName, castToInt64 ? ElemInt64 : ElemFloat, dim)); // output

            // OperatorSetIdProto { version=13 }（domain 缺省 = ""，即标准 opset）
            var opset = new List<byte>();
            AddVarint(opset, 2, 13);

            // ModelProto
            var model = new List<byte>();
            AddVarint(model, 1, 8);                              // ir_version
            AddString(model, 2, "marmoset-tests");               // producer_name
            AddBytes(model, 7, graph);                           // graph
            AddBytes(model, 8, opset);                           // opset_import
            return model.ToArray();
        }

        /// <summary>ValueInfoProto：名字 + float/int64 张量类型 + 形状 [1, dim]。</summary>
        private static List<byte> BuildValueInfo(string name, int elemType, int dim)
        {
            var dim0 = new List<byte>();
            AddVarint(dim0, 1, 1);       // Dimension.dim_value = 1（batch）
            var dim1 = new List<byte>();
            AddVarint(dim1, 1, dim);

            var shape = new List<byte>(); // TensorShapeProto
            AddBytes(shape, 1, dim0);
            AddBytes(shape, 1, dim1);

            var tensor = new List<byte>(); // TypeProto.Tensor
            AddVarint(tensor, 1, elemType);
            AddBytes(tensor, 2, shape);

            var typeProto = new List<byte>();
            AddBytes(typeProto, 1, tensor); // TypeProto.tensor_type

            var valueInfo = new List<byte>();
            AddString(valueInfo, 1, name);
            AddBytes(valueInfo, 2, typeProto);
            return valueInfo;
        }

        // ---------------------------------------------------------------- protobuf 基元

        private static void AddVarint(List<byte> target, int field, long value)
        {
            WriteVarint(target, (ulong)((field << 3) | 0)); // wire type 0
            WriteVarint(target, (ulong)value);
        }

        private static void AddString(List<byte> target, int field, string value)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            WriteVarint(target, (ulong)((field << 3) | 2)); // wire type 2
            WriteVarint(target, (ulong)utf8.Length);
            target.AddRange(utf8);
        }

        private static void AddBytes(List<byte> target, int field, List<byte> payload)
        {
            WriteVarint(target, (ulong)((field << 3) | 2));
            WriteVarint(target, (ulong)payload.Count);
            target.AddRange(payload);
        }

        private static void WriteVarint(List<byte> target, ulong value)
        {
            do
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value != 0)
                    b |= 0x80;
                target.Add(b);
            }
            while (value != 0);
        }
    }
}
