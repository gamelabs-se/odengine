using System;

namespace OdengineDebugger.Editor
{
    /// <summary>
    /// Fixed-capacity circular buffer for recording time series data (logAmp per tick).
    /// Thread-safe for single-writer / single-reader usage in the editor.
    /// </summary>
    internal sealed class TimeSeriesBuffer
    {
        private readonly float[] _data;
        private int _head;   // next write position
        private int _count;  // number of values written so far (capped at Capacity)

        public int Capacity => _data.Length;
        public int Count    => _count;

        public TimeSeriesBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _data = new float[capacity];
        }

        /// <summary>Push a new value, overwriting the oldest when full.</summary>
        public void Push(float value)
        {
            _data[_head] = value;
            _head        = (_head + 1) % _data.Length;
            if (_count < _data.Length) _count++;
        }

        /// <summary>
        /// Copy all values into <paramref name="dest"/> in chronological order (oldest first).
        /// Unwritten slots are filled with 0. <paramref name="dest"/> must have length == Capacity.
        /// </summary>
        public void CopyTo(float[] dest)
        {
            if (dest == null || dest.Length != _data.Length)
                throw new ArgumentException("dest length must equal Capacity", nameof(dest));

            if (_count == 0)
            {
                Array.Clear(dest, 0, dest.Length);
                return;
            }

            int oldest = _count < _data.Length ? 0 : _head;
            for (int i = 0; i < _data.Length; i++)
                dest[i] = _data[(oldest + i) % _data.Length];
        }

        /// <summary>Latest pushed value, or 0 if empty.</summary>
        public float Latest => _count == 0 ? 0f : _data[(_head - 1 + _data.Length) % _data.Length];

        public void Clear()
        {
            _head  = 0;
            _count = 0;
            Array.Clear(_data, 0, _data.Length);
        }
    }
}
