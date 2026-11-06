using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using NUnit.Framework;

namespace Cosmimera.Core.Tests
{
    public sealed class Pcg32Tests
    {
        // Official pcg32-demo (pcg-c-basic, --deterministic): pcg32_srandom_r(&rng, 42u, 54u), round 1.
        private static readonly uint[] ReferenceOutputs =
        {
            0xa15c02b7u, 0x7b47f409u, 0xba1d3330u, 0x83d2f293u, 0xbfa4784bu, 0xcbed606eu,
        };

        // Same demo, continuing after the 6 values above: first 10 coin flips, pcg32_boundedrand_r(&rng, 2) ("HHTTTHTHHH").
        private static readonly uint[] ReferenceCoins = { 1, 1, 0, 0, 0, 1, 0, 1, 1, 1 };

        [Test]
        public void NextUInt_MatchesOfficialTestVector()
        {
            var rng = new Pcg32(42UL, 54UL);
            for (int i = 0; i < ReferenceOutputs.Length; i++)
            {
                Assert.AreEqual(ReferenceOutputs[i], rng.NextUInt(), $"output #{i}");
            }
        }

        [Test]
        public void NextUIntBounded_MatchesOfficialCoinFlips()
        {
            var rng = new Pcg32(42UL, 54UL);
            for (int i = 0; i < ReferenceOutputs.Length; i++)
            {
                rng.NextUInt();
            }

            for (int i = 0; i < ReferenceCoins.Length; i++)
            {
                Assert.AreEqual(ReferenceCoins[i], rng.NextUInt(2u), $"coin #{i}");
            }
        }

        // Independent BigInt model (docs/reviews/P0-04-sol.md #3): threshold = 0x7fffffff, so the 2nd raw
        // output 0x7b47f409 must be rejected. A plain `NextUInt() % bound` would return 0x7b47f409 % bound here.
        private static readonly uint[] ReferenceRejectionBounded = { 559678134u, 974992175u, 64156306u };

        [Test]
        public void NextUIntBounded_RejectsBelowThreshold_MatchesReferenceModel()
        {
            var rng = new Pcg32(42UL, 54UL);
            for (int i = 0; i < ReferenceRejectionBounded.Length; i++)
            {
                Assert.AreEqual(ReferenceRejectionBounded[i], rng.NextUInt(0x80000001u), $"bounded #{i}");
            }

            // Three results consumed four raw outputs; the 5th raw output must follow.
            Assert.AreEqual(ReferenceOutputs[4], rng.NextUInt());
        }

        // Seed addition must wrap mod 2^64 (docs/reviews/P0-04-sol.md #1); values from the same BigInt model.
        [TestCase(ulong.MaxValue, ulong.MaxValue, 0x2675c047u, 0x7779a837u, 0xa145aa13u)]
        [TestCase(0UL, 0UL, 0xe4c14788u, 0x379c6516u, 0x5c4ab3bbu)]
        public void ExtremeSeeds_MatchReferenceModel(ulong seed, ulong stream, uint r0, uint r1, uint r2)
        {
            var rng = new Pcg32(seed, stream);
            Assert.AreEqual(r0, rng.NextUInt());
            Assert.AreEqual(r1, rng.NextUInt());
            Assert.AreEqual(r2, rng.NextUInt());
        }

        [Test]
        public void UnseededInstance_ThrowsContractExceptionInsteadOfLooping()
        {
            // default(Pcg32).NextUInt(3) would spin forever (state stays 0, threshold 1 rejects every 0).
            var zero = default(Pcg32);
            Assert.Throws<ContractException>(() => zero.NextUInt());
            Assert.Throws<ContractException>(() => zero.NextUInt(3u));
            Assert.Throws<ContractException>(() => zero.NextInt(0, 3));
            Assert.Throws<ContractException>(() => zero.NextFloat());
            var slots = new Pcg32[1];
            Assert.Throws<ContractException>(() => slots[0].NextUInt());
        }

        [Test]
        public void SameSeedAndStream_ProduceSameSequence()
        {
            var a = new Pcg32(123UL, 7UL);
            var b = new Pcg32(123UL, 7UL);
            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(a.NextUInt(), b.NextUInt());
            }
        }

        [Test]
        public void DifferentStreams_Diverge()
        {
            var a = new Pcg32(123UL, 7UL);
            var b = new Pcg32(123UL, 8UL);
            int equal = 0;
            for (int i = 0; i < 64; i++)
            {
                if (a.NextUInt() == b.NextUInt())
                {
                    equal++;
                }
            }

            Assert.Less(equal, 2);
        }

        [Test]
        public void NextUIntBounded_ZeroBound_ThrowsContractException()
        {
            var rng = new Pcg32(1UL, 1UL);
            Assert.Throws<ContractException>(() => rng.NextUInt(0u));
        }

        [TestCase(1u)]
        [TestCase(3u)]
        [TestCase(0x80000001u)]
        [TestCase(uint.MaxValue)]
        public void NextUIntBounded_StaysBelowBound(uint bound)
        {
            var rng = new Pcg32(99UL, 3UL);
            for (int i = 0; i < 2000; i++)
            {
                Assert.Less(rng.NextUInt(bound), bound);
            }
        }

        [Test]
        public void NextInt_EmptyRange_ThrowsContractException()
        {
            var rng = new Pcg32(1UL, 1UL);
            Assert.Throws<ContractException>(() => rng.NextInt(5, 5));
        }

        [Test]
        public void NextInt_ReversedRange_ThrowsContractException()
        {
            var rng = new Pcg32(1UL, 1UL);
            Assert.Throws<ContractException>(() => rng.NextInt(5, 4));
        }

        [TestCase(-3, 4)]
        [TestCase(0, 1)]
        [TestCase(int.MinValue, int.MaxValue)]
        public void NextInt_StaysInRange(int min, int max)
        {
            var rng = new Pcg32(5UL, 11UL);
            for (int i = 0; i < 2000; i++)
            {
                int v = rng.NextInt(min, max);
                Assert.GreaterOrEqual(v, min);
                Assert.Less(v, max);
            }
        }

        [Test]
        public void NextFloat_InHalfOpenUnitInterval()
        {
            var rng = new Pcg32(2024UL, 1UL);
            for (int i = 0; i < 10000; i++)
            {
                float f = rng.NextFloat();
                Assert.GreaterOrEqual(f, 0f);
                Assert.Less(f, 1f);
            }
        }

        [Test]
        public void CopyingStruct_ForksSequence()
        {
            // Documents the value-type hazard called out in Pcg32's remarks.
            var original = new Pcg32(8UL, 8UL);
            var copy = original;
            Assert.AreEqual(original.NextUInt(), copy.NextUInt());
        }
    }
}
