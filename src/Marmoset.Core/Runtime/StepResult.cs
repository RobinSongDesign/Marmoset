namespace Marmoset.Core
{
    /// <summary>一步仿真的结果，语义与 Gymnasium 的 step 返回值对齐。</summary>
    public readonly record struct StepResult(
        float[] Observation,
        float Reward,
        bool Terminated,
        bool Truncated);
}
