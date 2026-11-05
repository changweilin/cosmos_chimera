using Cosmimera.Core.Contracts;

namespace Cosmimera.Core.Random
{
    /// <summary>Reproducible PCG-XSH-RR 64/32 generator: same (seed, stream) yields the same sequence on every platform.</summary>
    /// <remarks>
    /// Pre: none.
    /// Post: output is bit-identical to the reference pcg32 (pcg-random.org, pcg_basic.c) for the same seed/stream.
    /// Side effects: none outside the instance.
    /// It is a mutable struct so WorldGen jobs (Burst) can hold it without managed references.
    /// Copying it forks the sequence: pass by <c>ref</c>, never by value, unless two identical streams are intended.
    /// REQ: CLAUDE.md 硬性規則（程序生成可重現）; docs/design/combat.md（戰鬥與生成分離的 stream）.
    /// </remarks>
    public struct Pcg32
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private ulong _increment;

        /// <summary>Seeds the generator; <paramref name="stream"/> selects one of 2^63 independent sequences.</summary>
        /// <remarks>
        /// Pre: none (any 64-bit seed/stream is valid; the stream's top bit is discarded by the algorithm).
        /// Post: the next <see cref="NextUInt()"/> equals the reference pcg32_srandom_r(seed, stream) output.
        /// Side effects: none.
        /// </remarks>
        public Pcg32(ulong seed, ulong stream)
        {
            _state = 0UL;
            // Increment must be odd for the LCG to reach full period 2^64.
            _increment = (stream << 1) | 1UL;
            Step();
            _state += seed;
            Step();
        }

        /// <summary>Returns the next uniformly distributed 32-bit value.</summary>
        /// <remarks>
        /// Pre: instance was created with the seeding constructor (default(Pcg32) is a valid but fixed sequence).
        /// Post: advances the sequence by exactly one step.
        /// Side effects: mutates this instance.
        /// </remarks>
        public uint NextUInt()
        {
            ulong old = _state;
            Step();
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rotation = (int)(old >> 59);
            // C# masks shift counts to 5 bits for uint, so (32 - 0) == 0 shift is safe here (unlike C UB).
            return (xorShifted >> rotation) | (xorShifted << (32 - rotation));
        }

        /// <summary>Returns a uniformly distributed value in [0, <paramref name="bound"/>) without modulo bias.</summary>
        /// <remarks>
        /// Pre: bound > 0.
        /// Post: 0 &lt;= result &lt; bound; consumes one or more steps (rejection sampling, matches pcg32_boundedrand_r).
        /// Side effects: mutates this instance.
        /// </remarks>
        public uint NextUInt(uint bound)
        {
            Contract.Requires(bound > 0u, "Pcg32.NextUInt(bound): bound > 0");

            // 2^32 mod bound, computed in 32-bit arithmetic: values below it would over-represent low results.
            uint threshold = unchecked(0u - bound) % bound;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold)
                {
                    return r % bound;
                }
            }
        }

        /// <summary>Returns a uniformly distributed integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
        /// <remarks>
        /// Pre: minInclusive &lt; maxExclusive.
        /// Post: minInclusive &lt;= result &lt; maxExclusive.
        /// Side effects: mutates this instance.
        /// </remarks>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            Contract.Requires(minInclusive < maxExclusive, "Pcg32.NextInt: minInclusive < maxExclusive");

            // Range computed in uint so int.MinValue..int.MaxValue does not overflow.
            uint range = unchecked((uint)(maxExclusive - minInclusive));
            return unchecked(minInclusive + (int)NextUInt(range));
        }

        /// <summary>Returns a uniformly distributed float in [0, 1).</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: 0 &lt;= result &lt; 1; result is an exact multiple of 2^-24.
        /// Side effects: mutates this instance.
        /// Uses the top 24 bits only: they fit the float mantissa exactly, so the int→float conversion is
        /// lossless and identical on every platform (a full 32-bit value could round up to 1.0f).
        /// </remarks>
        public float NextFloat()
        {
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        private void Step()
        {
            _state = unchecked(_state * Multiplier + _increment);
        }
    }
}
