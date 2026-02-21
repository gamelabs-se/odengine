namespace Odengine.Tests.Shared
{
    /// <summary>
    /// Deterministic xorshift32 PRNG.
    /// Same seed → identical sequence every time. No System.Random.
    /// </summary>
    public sealed class DeterministicRng
    {
        private uint _state;

        public DeterministicRng(uint seed) { _state = seed == 0 ? 1u : seed; }

        public uint NextU()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return _state = x;
        }

        public int NextInt(int min, int max)
        {
            if (max <= min) return min;
            return (int)(NextU() % (uint)(max - min)) + min;
        }

        public float NextFloat(float min, float max)
        {
            float t = NextU() / (float)uint.MaxValue;
            return min + (max - min) * t;
        }

        public bool NextBool(float trueProb = 0.5f) => NextFloat(0f, 1f) < trueProb;
    }
}
