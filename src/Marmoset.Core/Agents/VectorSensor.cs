using System;
using System.Collections.Generic;

namespace Marmoset.Core
{
    /// <summary>观测写入器。容量固定为 ObservationSpec.VectorLength，写满即为一帧完整观测。</summary>
    public sealed class VectorSensor
    {
        private readonly float[] _buffer;
        private int _count;

        public VectorSensor(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new float[capacity];
        }

        public int Capacity => _buffer.Length;

        public int Count => _count;

        public void AddObservation(float value)
        {
            if (_count >= _buffer.Length)
                throw new InvalidOperationException(
                    $"Observation overflow: sensor capacity is {_buffer.Length}. Check ObservationSpec.VectorLength.");
            _buffer[_count++] = value;
        }

        public void AddObservation(int value) => AddObservation((float)value);

        public void AddObservation(bool value) => AddObservation(value ? 1f : 0f);

        public void AddObservation(IEnumerable<float> values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            foreach (var v in values)
                AddObservation(v);
        }

        internal void Clear() => _count = 0;

        internal float[] ToArrayChecked()
        {
            if (_count != _buffer.Length)
                throw new InvalidOperationException(
                    $"Observation underflow: expected {_buffer.Length} values, agent wrote {_count}. Check CollectObservations.");
            return (float[])_buffer.Clone();
        }
    }
}
