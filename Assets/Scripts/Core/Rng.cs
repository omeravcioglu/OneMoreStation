using System.Collections.Generic;
using UnityEngine;

namespace Metro
{
    /// <summary>Small deterministic RNG (xorshift64*) so a city regenerates identically from its seed.</summary>
    public sealed class Rng
    {
        ulong state;

        public Rng(int seed)
        {
            state = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0xD1B54A32D192ED03UL;
            if (state == 0) state = 0x2545F4914F6CDD1DUL;
            for (int i = 0; i < 4; i++) Next();
        }

        public ulong Next()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return state * 2685821657736338717UL;
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float Value() => (Next() >> 40) / 16777216f;

        public float Range(float min, float max) => min + (max - min) * Value();

        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            int v = min + (int)(Value() * (maxExclusive - min));
            return v >= maxExclusive ? maxExclusive - 1 : v;
        }

        public bool Chance(float p) => Value() < p;

        public float Gaussian()
        {
            float u1 = Mathf.Max(Value(), 1e-7f), u2 = Value();
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }

        public Vector2 InsideUnitCircle()
        {
            float a = Value() * Mathf.PI * 2f;
            float r = Mathf.Sqrt(Value());
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }

        public T Pick<T>(IList<T> list) => list[Range(0, list.Count)];

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
