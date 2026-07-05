using System;

namespace Marmoset.Core
{
    /// <summary>观测规格。v1 仅支持定长浮点向量观测（见 docs/DESIGN.md 第 2 条）。</summary>
    public sealed record ObservationSpec
    {
        public ObservationSpec(int vectorLength)
        {
            if (vectorLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(vectorLength), "Observation vector length must be positive.");
            VectorLength = vectorLength;
        }

        public int VectorLength { get; }
    }
}
