using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class PlanetElementDrawTests
    {
        private static readonly Element[] VerdantBias = { Element.Wood, Element.Water, Element.Wind };

        [Test]
        public void Draw_is_deterministic()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                PlanetElements a = PlanetElementDraw.Draw(seed, PlanetSize.Supergiant, VerdantBias);
                PlanetElements b = PlanetElementDraw.Draw(seed, PlanetSize.Supergiant, VerdantBias);
                Assert.That(a.MainCount, Is.EqualTo(b.MainCount));
                Assert.That(a.SubCount, Is.EqualTo(b.SubCount));
                Assert.That(a.Shares.Count, Is.EqualTo(b.Shares.Count));
                for (int i = 0; i < a.Shares.Count; i++)
                {
                    Assert.That(a.Shares[i], Is.EqualTo(b.Shares[i]));
                }
            }
        }

        [Test]
        public void Asteroid_has_one_main_and_zero_sub()
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                PlanetElements e = PlanetElementDraw.Draw(seed, PlanetSize.Asteroid, VerdantBias);
                Assert.That(e.MainCount, Is.EqualTo(1));
                Assert.That(e.SubCount, Is.EqualTo(0));
                Assert.That(e.MainPercentTotal, Is.EqualTo(100));
                Assert.That(e.SubPercentTotal, Is.EqualTo(0));
                Assert.That(e.Shares.Count, Is.EqualTo(1));
                Assert.That(e.Shares[0].IsMain, Is.True);
                Assert.That(e.Shares[0].Percent, Is.EqualTo(100));
                CollectionAssert.Contains(VerdantBias, e.Shares[0].Element);
            }
        }

        [Test]
        public void Moon_has_one_main_and_zero_or_one_sub()
        {
            var seenSubCounts = new HashSet<int>();
            for (ulong seed = 0; seed < 200; seed++)
            {
                PlanetElements e = PlanetElementDraw.Draw(seed, PlanetSize.Moon, VerdantBias);
                Assert.That(e.MainCount, Is.EqualTo(1));
                Assert.That(e.SubCount, Is.InRange(0, 1));
                seenSubCounts.Add(e.SubCount);
                AssertInvariants(e);
            }

            CollectionAssert.AreEquivalent(new[] { 0, 1 }, seenSubCounts);
        }

        [Test]
        public void Planet_has_one_main_and_zero_to_two_subs()
        {
            var seenSubCounts = new HashSet<int>();
            for (ulong seed = 0; seed < 200; seed++)
            {
                PlanetElements e = PlanetElementDraw.Draw(seed, PlanetSize.Planet, VerdantBias);
                Assert.That(e.MainCount, Is.EqualTo(1));
                Assert.That(e.SubCount, Is.InRange(0, 2));
                seenSubCounts.Add(e.SubCount);
                AssertInvariants(e);
            }

            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, seenSubCounts);
        }

        [Test]
        public void Supergiant_has_one_to_two_main_and_zero_to_two_subs()
        {
            var seenMainCounts = new HashSet<int>();
            var seenSubCounts = new HashSet<int>();
            for (ulong seed = 0; seed < 300; seed++)
            {
                PlanetElements e = PlanetElementDraw.Draw(seed, PlanetSize.Supergiant, VerdantBias);
                Assert.That(e.MainCount, Is.InRange(1, 2));
                Assert.That(e.SubCount, Is.InRange(0, 2));
                seenMainCounts.Add(e.MainCount);
                seenSubCounts.Add(e.SubCount);
                AssertInvariants(e);
            }

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, seenMainCounts);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, seenSubCounts);
        }

        [Test]
        public void Single_or_empty_bias_falls_back_to_unique_elements()
        {
            var singleBias = new[] { Element.Fire };
            var emptyBias = System.Array.Empty<Element>();
            for (ulong seed = 0; seed < 50; seed++)
            {
                PlanetElements s = PlanetElementDraw.Draw(seed, PlanetSize.Supergiant, singleBias);
                AssertInvariants(s);
                Assert.That(s.PercentOf(Element.Fire), Is.GreaterThan(0));

                PlanetElements z = PlanetElementDraw.Draw(seed, PlanetSize.Supergiant, emptyBias);
                AssertInvariants(z);
            }
        }

        [Test]
        public void Preconditions_throw_contract_exception()
        {
            Assert.Throws<ContractException>(() => PlanetElementDraw.Draw(1, (PlanetSize)99, VerdantBias));
            Assert.Throws<ContractException>(() => PlanetElementDraw.Draw(1, PlanetSize.Moon, null));
            Assert.Throws<ContractException>(() =>
                PlanetElementDraw.Draw(1, PlanetSize.Moon, new[] { Element.Fire, (Element)99 }));

            PlanetElements e = PlanetElementDraw.Draw(1, PlanetSize.Asteroid, VerdantBias);
            Assert.Throws<ContractException>(() => e.PercentOf((Element)99));
        }

        private static void AssertInvariants(PlanetElements e)
        {
            Assert.That(e.Shares.Count, Is.EqualTo(e.MainCount + e.SubCount));
            Assert.That(e.Elements.Count, Is.EqualTo(e.Shares.Count));
            Assert.That(e.Percents.Count, Is.EqualTo(e.Shares.Count));
            Assert.That(e.MainPercentTotal + e.SubPercentTotal, Is.EqualTo(100));

            if (e.SubCount == 0)
            {
                Assert.That(e.SubPercentTotal, Is.EqualTo(0));
            }
            else
            {
                Assert.That(e.SubPercentTotal, Is.InRange(e.SubCount, 35));
            }

            var unique = new HashSet<Element>();
            int mainSum = 0;
            int subSum = 0;
            for (int i = 0; i < e.Shares.Count; i++)
            {
                PlanetElementShare s = e.Shares[i];
                Assert.That(unique.Add(s.Element), Is.True, "duplicate element");
                Assert.That(s.Percent, Is.GreaterThanOrEqualTo(1));
                Assert.That(e.Elements[i], Is.EqualTo(s.Element));
                Assert.That(e.Percents[i], Is.EqualTo(s.Percent));
                Assert.That(e.PercentOf(s.Element), Is.EqualTo(s.Percent));
                if (s.IsMain)
                {
                    mainSum += s.Percent;
                }
                else
                {
                    subSum += s.Percent;
                }
            }

            Assert.That(mainSum, Is.EqualTo(e.MainPercentTotal));
            Assert.That(subSum, Is.EqualTo(e.SubPercentTotal));
        }
    }
}
