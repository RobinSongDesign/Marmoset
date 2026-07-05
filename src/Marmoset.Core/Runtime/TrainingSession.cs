using System;

namespace Marmoset.Core
{
    /// <summary>
    /// 环境运行时会话：持有一个 Agent，驱动 reset/step 循环，维护回合统计。
    /// 线程模型：Reset/Step 可从任意线程（通常是 TCP 服务线程）调用，内部以
    /// <see cref="SyncRoot"/> 加锁；可视化等外部读取 Agent 状态时必须 lock(SyncRoot)。
    /// </summary>
    public sealed class TrainingSession
    {
        private readonly VectorSensor _sensor;

        public TrainingSession(Agent agent)
        {
            Agent = agent ?? throw new ArgumentNullException(nameof(agent));
            _sensor = new VectorSensor(agent.ObservationSpec.VectorLength);
        }

        public Agent Agent { get; }

        /// <summary>读取 Agent/环境状态（如绘制可视化）时锁此对象，与仿真线程互斥。</summary>
        public object SyncRoot { get; } = new object();

        public long TotalSteps { get; private set; }

        public int EpisodeCount { get; private set; }

        public int CurrentEpisodeSteps { get; private set; }

        public float CurrentEpisodeReward { get; private set; }

        /// <summary>每步完成后在仿真线程触发。订阅者必须快速返回（如仅置脏标记）。</summary>
        public event Action? StepCompleted;

        /// <summary>回合结束（terminated 或 truncated）后在仿真线程触发。</summary>
        public event Action? EpisodeCompleted;

        public float[] Reset(int? seed = null)
        {
            float[] observation;
            lock (SyncRoot)
            {
                Agent.ResetStepState();
                Agent.OnEpisodeBegin(seed);
                CurrentEpisodeSteps = 0;
                CurrentEpisodeReward = 0f;
                observation = CollectObservations();
            }

            StepCompleted?.Invoke();
            return observation;
        }

        public StepResult Step(ActionBuffer actions)
        {
            if (actions == null)
                throw new ArgumentNullException(nameof(actions));
            actions.Validate(Agent.ActionSpec);

            StepResult result;
            lock (SyncRoot)
            {
                Agent.ResetStepState();
                Agent.OnActionReceived(actions);

                CurrentEpisodeSteps++;
                TotalSteps++;

                bool terminated = Agent.StepTerminated;
                bool truncated = !terminated && Agent.MaxSteps > 0 && CurrentEpisodeSteps >= Agent.MaxSteps;
                float reward = Agent.StepReward;
                CurrentEpisodeReward += reward;

                if (terminated || truncated)
                    EpisodeCount++;

                result = new StepResult(CollectObservations(), reward, terminated, truncated);
            }

            StepCompleted?.Invoke();
            if (result.Terminated || result.Truncated)
                EpisodeCompleted?.Invoke();
            return result;
        }

        private float[] CollectObservations()
        {
            _sensor.Clear();
            Agent.CollectObservations(_sensor);
            return _sensor.ToArrayChecked();
        }
    }
}
