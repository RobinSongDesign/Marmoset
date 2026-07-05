using System;
using System.Collections.Generic;

namespace Marmoset.Core
{
    /// <summary>一步动作的载体。离散动作与 ActionSpec.DiscreteBranchSizes 一一对应。</summary>
    public sealed class ActionBuffer
    {
        private static readonly int[] EmptyInts = Array.Empty<int>();
        private static readonly float[] EmptyFloats = Array.Empty<float>();

        private ActionBuffer(int[] discrete, float[] continuous)
        {
            Discrete = discrete;
            Continuous = continuous;
        }

        public IReadOnlyList<int> Discrete { get; }

        public IReadOnlyList<float> Continuous { get; }

        public static ActionBuffer FromDiscrete(params int[] actions)
        {
            if (actions == null || actions.Length == 0)
                throw new ArgumentException("Discrete actions must not be empty.", nameof(actions));
            return new ActionBuffer(actions, EmptyFloats);
        }

        public static ActionBuffer FromContinuous(params float[] actions)
        {
            if (actions == null || actions.Length == 0)
                throw new ArgumentException("Continuous actions must not be empty.", nameof(actions));
            return new ActionBuffer(EmptyInts, actions);
        }

        /// <summary>校验动作与规格匹配，不匹配时抛出说明性异常。</summary>
        public void Validate(ActionSpec spec)
        {
            if (spec.IsDiscrete)
            {
                if (Discrete.Count != spec.DiscreteBranchSizes.Count)
                    throw new ArgumentException(
                        $"Expected {spec.DiscreteBranchSizes.Count} discrete action branch(es), got {Discrete.Count}.");
                for (int i = 0; i < Discrete.Count; i++)
                {
                    if (Discrete[i] < 0 || Discrete[i] >= spec.DiscreteBranchSizes[i])
                        throw new ArgumentException(
                            $"Discrete action {Discrete[i]} out of range [0, {spec.DiscreteBranchSizes[i]}) for branch {i}.");
                }
            }
            else if (Continuous.Count != spec.ContinuousSize)
            {
                throw new ArgumentException(
                    $"Expected {spec.ContinuousSize} continuous action value(s), got {Continuous.Count}.");
            }
        }
    }
}
