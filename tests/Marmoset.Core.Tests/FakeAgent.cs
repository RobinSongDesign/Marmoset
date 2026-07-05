using System;
using Marmoset.Core;

namespace Marmoset.Core.Tests
{
    /// <summary>
    /// 测试用可配置 Agent。观测布局（长度 >= 2 时）：
    /// [0] 本回合已执行步数，[1] 最近一次离散动作（回合开始为 -1），其余填 42。
    /// </summary>
    internal sealed class FakeAgent : Agent
    {
        private readonly ObservationSpec _observationSpec;
        private readonly ActionSpec _actionSpec;
        private readonly int _maxSteps;

        public FakeAgent(int obsLength = 3, ActionSpec? actionSpec = null, int maxSteps = 0)
        {
            _observationSpec = new ObservationSpec(obsLength);
            _actionSpec = actionSpec ?? ActionSpec.Discrete(4);
            _maxSteps = maxSteps;
        }

        public float RewardPerStep { get; set; } = 0.5f;

        /// <summary>离散动作等于该值时结束回合（terminated）。</summary>
        public int? EndOnDiscreteAction { get; set; }

        /// <summary>为 true 时 OnActionReceived 抛异常，测试服务端错误路径。</summary>
        public bool ThrowOnAction { get; set; }

        public int EpisodeBeginCount { get; private set; }

        public int? LastSeed { get; private set; }

        public int StepsThisEpisode { get; private set; }

        public int LastDiscreteAction { get; private set; } = -1;

        public float[] LastContinuousActions { get; private set; } = Array.Empty<float>();

        public override ObservationSpec ObservationSpec => _observationSpec;

        public override ActionSpec ActionSpec => _actionSpec;

        public override int MaxSteps => _maxSteps;

        public override void OnEpisodeBegin(int? seed)
        {
            EpisodeBeginCount++;
            LastSeed = seed;
            StepsThisEpisode = 0;
            LastDiscreteAction = -1;
            LastContinuousActions = Array.Empty<float>();
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            int length = _observationSpec.VectorLength;
            if (length > 0)
                sensor.AddObservation(StepsThisEpisode);
            if (length > 1)
                sensor.AddObservation(LastDiscreteAction);
            for (int i = 2; i < length; i++)
                sensor.AddObservation(42f);
        }

        public override void OnActionReceived(ActionBuffer actions)
        {
            if (ThrowOnAction)
                throw new InvalidOperationException("FakeAgent deliberate failure.");

            StepsThisEpisode++;
            if (actions.Discrete.Count > 0)
                LastDiscreteAction = actions.Discrete[0];
            if (actions.Continuous.Count > 0)
            {
                var copy = new float[actions.Continuous.Count];
                for (int i = 0; i < copy.Length; i++)
                    copy[i] = actions.Continuous[i];
                LastContinuousActions = copy;
            }

            AddReward(RewardPerStep);

            if (EndOnDiscreteAction is int end && actions.Discrete.Count > 0 && actions.Discrete[0] == end)
                EndEpisode();
        }
    }
}
