using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Marmoset.Core
{
    /// <summary>
    /// 加载训练导出的 ONNX 策略并在进程内推理（无需 Python）。
    /// 模型 IO 约定见 docs/PROTOCOL.md「ONNX 模型约定」：
    /// 输入 "observation" float32 [1, obs_dim]；输出 "action"：
    /// int64 [1, branches] → 离散动作，float32 [1, act_dim] → 连续动作。
    /// 若模型没有按约定命名但只有单个输入/输出，则自动采用该张量（容错）。
    /// 本类型的公共表面是稳定契约（GH 组件依赖它）；内部实现见 Server 工作包。
    /// </summary>
    public sealed class OnnxPolicy : IDisposable
    {
        private const string PreferredInputName = "observation";
        private const string PreferredOutputName = "action";

        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly string _outputName;
        private readonly bool _outputIsInt64;
        private bool _disposed;

        public OnnxPolicy(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentException("Model path must not be empty.", nameof(modelPath));
            ModelPath = modelPath;

            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"ONNX model file not found: {modelPath}", modelPath);

            _session = new InferenceSession(modelPath);
            try
            {
                _inputName = ResolveTensorName(_session.InputMetadata.Keys, PreferredInputName, "input");
                _outputName = ResolveTensorName(_session.OutputMetadata.Keys, PreferredOutputName, "output");

                Type elementType = _session.OutputMetadata[_outputName].ElementType;
                if (elementType == typeof(long))
                {
                    _outputIsInt64 = true;  // 离散动作（已 argmax）
                }
                else if (elementType == typeof(float))
                {
                    _outputIsInt64 = false; // 连续动作（确定性均值，已 clip 到 [-1,1]）
                }
                else
                {
                    throw new NotSupportedException(
                        $"Output tensor '{_outputName}' has element type '{elementType?.Name ?? "unknown"}'; " +
                        "expected int64 (discrete) or float32 (continuous). See docs/PROTOCOL.md.");
                }
            }
            catch
            {
                _session.Dispose();
                throw;
            }
        }

        public string ModelPath { get; }

        /// <summary>对单帧观测做确定性推理，返回可直接喂给 TrainingSession.Step 的动作。</summary>
        public ActionBuffer Predict(float[] observation)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OnnxPolicy));
            if (observation == null)
                throw new ArgumentNullException(nameof(observation));
            if (observation.Length == 0)
                throw new ArgumentException("Observation must not be empty.", nameof(observation));

            var input = new DenseTensor<float>(observation, new[] { 1, observation.Length });
            var inputs = new[] { NamedOnnxValue.CreateFromTensor(_inputName, input) };

            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
                _session.Run(inputs, new[] { _outputName });
            DisposableNamedOnnxValue output = results[0];

            if (_outputIsInt64)
            {
                Tensor<long> tensor = output.AsTensor<long>();
                var discrete = new int[(int)tensor.Length];
                int i = 0;
                foreach (long value in tensor)
                    discrete[i++] = checked((int)value);
                return ActionBuffer.FromDiscrete(discrete);
            }

            Tensor<float> floats = output.AsTensor<float>();
            return ActionBuffer.FromContinuous(floats.ToArray());
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _session.Dispose();
        }

        private static string ResolveTensorName(IEnumerable<string> names, string preferred, string kind)
        {
            var list = names.ToArray();
            if (list.Contains(preferred))
                return preferred;
            if (list.Length == 1)
                return list[0]; // 单个张量：容错采用
            throw new NotSupportedException(
                $"Model has {list.Length} {kind} tensors ({string.Join(", ", list)}) and none is named '{preferred}'. " +
                "See docs/PROTOCOL.md for the ONNX model contract.");
        }
    }
}
