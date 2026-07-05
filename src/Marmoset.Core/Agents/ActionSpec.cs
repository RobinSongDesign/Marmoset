using System;
using System.Collections.Generic;
using System.Linq;

namespace Marmoset.Core
{
    /// <summary>
    /// 动作空间规格。v1 不支持离散/连续混合空间：二者只能取其一。
    /// 离散空间支持多分支（对应 gym 的 MultiDiscrete）；
    /// 连续动作约定取值范围为 [-1, 1]，环境作者自行映射到实际量纲。
    /// </summary>
    public sealed class ActionSpec
    {
        private ActionSpec(int[] discreteBranchSizes, int continuousSize)
        {
            DiscreteBranchSizes = discreteBranchSizes;
            ContinuousSize = continuousSize;
        }

        public IReadOnlyList<int> DiscreteBranchSizes { get; }

        public int ContinuousSize { get; }

        public bool IsDiscrete => DiscreteBranchSizes.Count > 0;

        public bool IsContinuous => ContinuousSize > 0;

        public static ActionSpec Discrete(params int[] branchSizes)
        {
            if (branchSizes == null || branchSizes.Length == 0)
                throw new ArgumentException("At least one discrete branch is required.", nameof(branchSizes));
            if (branchSizes.Any(s => s < 2))
                throw new ArgumentException("Each discrete branch must have at least 2 actions.", nameof(branchSizes));
            return new ActionSpec((int[])branchSizes.Clone(), 0);
        }

        public static ActionSpec Continuous(int size)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            return new ActionSpec(Array.Empty<int>(), size);
        }
    }
}
