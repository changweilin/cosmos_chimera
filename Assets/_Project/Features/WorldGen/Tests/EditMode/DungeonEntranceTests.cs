using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.WorldGen.Tests
{
    // REQ: docs/design/planet-world.md#進階地形地城 (Q-W41, Q-W43)
    public sealed class DungeonEntranceTests
    {
        private static PlanetElements Elements(params (Element e, int pct)[] shares)
        {
            var list = new PlanetElementShare[shares.Length];
            int total = 0;
            for (int i = 0; i < shares.Length; i++)
            {
                list[i] = new PlanetElementShare(shares[i].e, shares[i].pct, i == 0);
                total += shares[i].pct;
            }

            Assert.That(total, Is.EqualTo(100), "fixture shares must sum to 100");
            return new PlanetElements(list, 1, shares.Length - 1, shares[0].pct, 100 - shares[0].pct);
        }

        [Test]
        public void Every_terrain_has_its_own_entrance_landmark()
        {
            var kinds = new HashSet<LandmarkKind>();
            foreach (AdvancedTerrainSpec spec in AdvancedTerrains.All)
            {
                LandmarkKind kind = DungeonEntrance.KindFor(spec.Id);
                Assert.That(kinds.Add(kind), Is.True, spec.Id);
                Assert.That(kind, Is.Not.EqualTo(LandmarkKind.SpaceStation).And.Not.EqualTo(LandmarkKind.SkyIsland));
                foreach (string alias in spec.Aliases)
                {
                    Assert.That(DungeonEntrance.KindFor(alias), Is.EqualTo(kind));
                }
            }

            Assert.That(DungeonEntrance.KindFor("volcano"), Is.EqualTo(LandmarkKind.Crater));
            Assert.That(DungeonEntrance.KindFor("cave"), Is.EqualTo(LandmarkKind.MineShaft));
            Assert.That(DungeonEntrance.KindFor("space"), Is.EqualTo(LandmarkKind.SpaceElevator));
        }

        [Test]
        public void Asteroids_have_no_entrance()
        {
            Assert.That(DungeonEntrance.TryDraw(1, PlanetSize.Asteroid, Elements((Element.Fire, 100)), out _), Is.False);
        }

        [Test]
        public void Draw_follows_the_element_shares_among_eligible_terrains()
        {
            PlanetElements fireWater = Elements((Element.Fire, 70), (Element.Water, 30));
            var count = new Dictionary<string, int>();
            for (ulong seed = 0; seed < 2000; seed++)
            {
                Assert.That(DungeonEntrance.TryDraw(seed, PlanetSize.Moon, fireWater, out AdvancedTerrainSpec t), Is.True);
                count[t.Id] = count.TryGetValue(t.Id, out int c) ? c + 1 : 1;
            }

            CollectionAssert.AreEquivalent(new[] { "volcano", "deep_sea" }, count.Keys);
            Assert.That(count["volcano"], Is.InRange(1250, 1550), "≈ 70 %");
        }

        [Test]
        public void Ineligible_shares_fall_back_to_a_uniform_draw()
        {
            // Ice terrain needs a planet; on a dwarf planet the ice share counts for nothing.
            PlanetElements ice = Elements((Element.Ice, 100));
            var seen = new HashSet<string>();
            for (ulong seed = 0; seed < 400; seed++)
            {
                DungeonEntrance.TryDraw(seed, PlanetSize.Moon, ice, out AdvancedTerrainSpec t);
                Assert.That(AdvancedTerrains.AppearsOn(t, PlanetSize.Moon), Is.True);
                seen.Add(t.Id);
                DungeonEntrance.TryDraw(seed, PlanetSize.Supergiant, null, out AdvancedTerrainSpec any);
                Assert.That(AdvancedTerrains.AppearsOn(any, PlanetSize.Supergiant), Is.True);
            }

            Assert.That(seen.Count, Is.EqualTo(5));
        }

        [Test]
        public void Same_seed_same_terrain()
        {
            PlanetElements e = Elements((Element.Light, 60), (Element.Shadow, 40));
            for (ulong seed = 0; seed < 50; seed++)
            {
                DungeonEntrance.TryDraw(seed, PlanetSize.Supergiant, e, out AdvancedTerrainSpec a);
                DungeonEntrance.TryDraw(seed, PlanetSize.Supergiant, e, out AdvancedTerrainSpec b);
                Assert.That(b.Id, Is.EqualTo(a.Id));
            }
        }

        [Test]
        public void Contract_violations_throw()
        {
            Assert.Throws<ContractException>(() => DungeonEntrance.KindFor("forest"));
            Assert.Throws<ContractException>(() => DungeonEntrance.KindFor(null));
            Assert.Throws<ContractException>(() => DungeonEntrance.TryDraw(1, (PlanetSize)9, null, out _));
        }
    }
}
