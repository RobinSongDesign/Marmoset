using System;
using System.IO;
using Marmoset.Core;
using Xunit;

namespace Marmoset.Core.Tests
{
    public class OnnxPolicyTests
    {
        private static void CleanUp(string path)
        {
            try { File.Delete(path); } catch { }
        }

        [Fact]
        public void Predict_ContinuousModel_ReturnsFloatActions()
        {
            string path = OnnxModelFactory.WriteContinuousIdentityModel(dim: 2);
            try
            {
                using var policy = new OnnxPolicy(path);
                ActionBuffer actions = policy.Predict(new[] { 0.5f, -0.25f });

                Assert.Empty(actions.Discrete);
                Assert.Equal(new[] { 0.5f, -0.25f }, actions.Continuous);
            }
            finally
            {
                CleanUp(path);
            }
        }

        [Fact]
        public void Predict_DiscreteModel_ReturnsIntActions()
        {
            // Cast float->int64（截断），输出 int64 [1, 2] → FromDiscrete
            string path = OnnxModelFactory.WriteDiscreteCastModel(dim: 2);
            try
            {
                using var policy = new OnnxPolicy(path);
                ActionBuffer actions = policy.Predict(new[] { 2.0f, 1.0f });

                Assert.Empty(actions.Continuous);
                Assert.Equal(new[] { 2, 1 }, actions.Discrete);
            }
            finally
            {
                CleanUp(path);
            }
        }

        [Fact]
        public void Predict_SingleBranchDiscreteModel_Works()
        {
            string path = OnnxModelFactory.WriteDiscreteCastModel(dim: 1);
            try
            {
                using var policy = new OnnxPolicy(path);
                ActionBuffer actions = policy.Predict(new[] { 3.0f });

                Assert.Equal(new[] { 3 }, actions.Discrete);
            }
            finally
            {
                CleanUp(path);
            }
        }

        [Fact]
        public void NonStandardTensorNames_AutoDetectedWhenUnique()
        {
            // SB3 风格命名：模型只有一个输入/一个输出时自动采用
            string path = OnnxModelFactory.WriteContinuousIdentityModel(dim: 2, inputName: "obs_0", outputName: "continuous_actions");
            try
            {
                using var policy = new OnnxPolicy(path);
                ActionBuffer actions = policy.Predict(new[] { 0.1f, 0.2f });

                Assert.Equal(new[] { 0.1f, 0.2f }, actions.Continuous);
            }
            finally
            {
                CleanUp(path);
            }
        }

        [Fact]
        public void Predict_CanDriveTrainingSession()
        {
            // 推理结果可直接喂给 TrainingSession.Step（契约要求）
            string path = OnnxModelFactory.WriteDiscreteCastModel(dim: 1);
            try
            {
                using var policy = new OnnxPolicy(path);
                var agent = new FakeAgent(obsLength: 3, actionSpec: ActionSpec.Discrete(4));
                var session = new TrainingSession(agent);

                float[] observation = session.Reset();
                // 模型输入维度为 1：喂第一个观测分量，得到离散动作
                ActionBuffer action = policy.Predict(new[] { observation[0] });
                StepResult result = session.Step(action);

                Assert.Equal(1, session.TotalSteps);
                Assert.False(result.Terminated);
            }
            finally
            {
                CleanUp(path);
            }
        }

        // ---------------------------------------------------------------- 错误路径

        [Fact]
        public void MissingFile_ThrowsFileNotFound()
        {
            string path = Path.Combine(Path.GetTempPath(), "marmoset-tests", "does-not-exist.onnx");
            Assert.Throws<FileNotFoundException>(() => new OnnxPolicy(path));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyPath_ThrowsArgumentException(string path)
        {
            Assert.Throws<ArgumentException>(() => new OnnxPolicy(path));
        }

        [Fact]
        public void CorruptModelFile_Throws()
        {
            string dir = Path.Combine(Path.GetTempPath(), "marmoset-tests");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".onnx");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
            try
            {
                Assert.ThrowsAny<Exception>(() => new OnnxPolicy(path));
            }
            finally
            {
                CleanUp(path);
            }
        }

        [Fact]
        public void Predict_NullOrEmptyObservation_Throws()
        {
            string path = OnnxModelFactory.WriteContinuousIdentityModel(dim: 2);
            try
            {
                using var policy = new OnnxPolicy(path);
                Assert.Throws<ArgumentNullException>(() => policy.Predict(null!));
                Assert.Throws<ArgumentException>(() => policy.Predict(Array.Empty<float>()));
            }
            finally
            {
                CleanUp(path);
            }
        }

        [Fact]
        public void Predict_AfterDispose_ThrowsObjectDisposed()
        {
            string path = OnnxModelFactory.WriteContinuousIdentityModel(dim: 2);
            try
            {
                var policy = new OnnxPolicy(path);
                policy.Dispose();
                policy.Dispose(); // 幂等
                Assert.Throws<ObjectDisposedException>(() => policy.Predict(new[] { 0.1f, 0.2f }));
            }
            finally
            {
                CleanUp(path);
            }
        }

        [Fact]
        public void ModelPath_IsExposed()
        {
            string path = OnnxModelFactory.WriteContinuousIdentityModel(dim: 2);
            try
            {
                using var policy = new OnnxPolicy(path);
                Assert.Equal(path, policy.ModelPath);
            }
            finally
            {
                CleanUp(path);
            }
        }
    }
}
