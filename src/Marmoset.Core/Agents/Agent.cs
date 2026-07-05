using System;

namespace Marmoset.Core
{
    /// <summary>
    /// 环境/智能体基类。仿 ML-Agents 的编程模型：
    /// 子类实现回合初始化、观测采集、动作执行，并通过 AddReward / EndEpisode 报告结果。
    /// 生命周期由 <see cref="TrainingSession"/> 驱动，子类不要自行调用 internal 成员。
    /// </summary>
    public abstract class Agent
    {
        private float _reward;
        private bool _terminated;

        /// <summary>观测向量的规格。整个训练会话期间必须保持不变。</summary>
        public abstract ObservationSpec ObservationSpec { get; }

        /// <summary>动作空间的规格。整个训练会话期间必须保持不变。</summary>
        public abstract ActionSpec ActionSpec { get; }

        /// <summary>单回合最大步数，超过后回合被截断（truncated）。0 表示无上限。</summary>
        public virtual int MaxSteps => 0;

        /// <summary>回合开始时调用，重置环境状态。seed 为 null 时应使用非确定性随机源。</summary>
        public abstract void OnEpisodeBegin(int? seed);

        /// <summary>
        /// 采集当前观测。写入 sensor 的浮点数个数必须恰好等于 ObservationSpec.VectorLength。
        /// </summary>
        public abstract void CollectObservations(VectorSensor sensor);

        /// <summary>
        /// 执行一步动作。在此方法内调用 AddReward / SetReward 报告本步奖励，
        /// 回合结束时调用 EndEpisode。
        /// </summary>
        public abstract void OnActionReceived(ActionBuffer actions);

        /// <summary>累加本步奖励。仅在 OnActionReceived 内调用。</summary>
        protected void AddReward(float increment) => _reward += increment;

        /// <summary>覆写本步奖励。仅在 OnActionReceived 内调用。</summary>
        protected void SetReward(float reward) => _reward = reward;

        /// <summary>声明回合终止（terminated，区别于超步数截断）。仅在 OnActionReceived 内调用。</summary>
        protected void EndEpisode() => _terminated = true;

        internal void ResetStepState()
        {
            _reward = 0f;
            _terminated = false;
        }

        internal float StepReward => _reward;

        internal bool StepTerminated => _terminated;
    }
}
