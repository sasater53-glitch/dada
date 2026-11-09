using System.Collections.Generic;

namespace CityBiz.Core
{
    /// <summary>
    /// xorshift128+ генератор. Даёт одинаковую последовательность на сервере и клиентах
    /// независимо от платформы, поэтому косметические NPC можно не синхронизировать по сети.
    /// </summary>
    public sealed class DeterministicRandom
    {
        ulong _s0, _s1;

        public DeterministicRandom(ulong seed) { Reseed(seed); }

        public void Reseed(ulong seed)
        {
            _s0 = SplitMix(ref seed);
            _s1 = SplitMix(ref seed);
            if (_s0 == 0 && _s1 == 0) _s1 = 1;
        }

        static ulong SplitMix(ref ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            ulong s1 = _s0, s0 = _s1;
            _s0 = s0;
            s1 ^= s1 << 23;
            _s1 = s1 ^ s0 ^ (s1 >> 17) ^ (s0 >> 26);
            return _s1 + s0;
        }

        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));
        public float NextFloat() => (float)NextDouble();
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextULong() % (ulong)(maxExclusive - minInclusive));
        }

        public bool Chance(double p) => NextDouble() < p;
        public T Pick<T>(IReadOnlyList<T> list) => list[Range(0, list.Count)];

        public static ulong Hash(ulong a, ulong b = 0)
        {
            ulong x = a * 0x9E3779B97F4A7C15UL ^ (b + 0x632BE59BD9B4E019UL);
            return SplitMix(ref x);
        }

        /// <summary>Детерминированное число 0..1 из ключа — для «случайных» но стабильных деталей.</summary>
        public static float Hash01(ulong a, ulong b = 0) => (Hash(a, b) >> 40) / (float)(1UL << 24);
    }
}
