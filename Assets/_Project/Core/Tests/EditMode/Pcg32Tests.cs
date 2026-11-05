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
