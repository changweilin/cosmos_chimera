using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using NUnit.Framework;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: planet-world.md#進階地形地城 (地城文化).
    public sealed class DungeonCultureTests
    {
        private static readonly string[] PlanetTypes =
        {
            "verdant_planet", "lava_planet", "ice_crystal_planet", "storm_planet", "mineral_planet", "twilight_planet",
        };

        private static readonly string[] Terrains =
        {
            "laboratory", "rainforest", "deep_sea", "volcano", "cave", "ice_cap", "tower", "space", "labyrinth", "temple",
        };

        [Test]
        public void Same_arguments_give_the_same_culture()
        {
            for (ulong seed = 1; seed < 200; seed++)
            {
                ArchPlanetStyle planet = ArchitectureMeshes.PlanetStyleFor("storm_planet", seed);
                DungeonCulture a = DungeonCulture.For(planet, "temple", seed);
                DungeonCulture b = DungeonCulture.For(planet, "temple", seed);
                Assert.That(b.Region, Is.EqualTo(a.Region));
                Assert.That(b.Era, Is.EqualTo(a.Era));
                Assert.That(b.AccentRegion, Is.EqualTo(a.AccentRegion));
                Assert.That(b.AccentEra, Is.EqualTo(a.AccentEra));
                Assert.That(b.AccentShare, Is.EqualTo(a.AccentShare));
                Assert.That(b.Look, Is.EqualTo(a.Look));
                Assert.That(b.Age, Is.EqualTo(a.Age));
                Assert.That(b.Damage, Is.EqualTo(a.Damage));
                Assert.That(b.Seed, Is.EqualTo(a.Seed));
            }
        }

        [Test]
        public void Every_culture_keeps_its_bounds()
        {
            foreach (string type in PlanetTypes)
            {
                for (ulong seed = 1; seed < 300; seed++)
                {
                    ArchPlanetStyle planet = ArchitectureMeshes.PlanetStyleFor(type, seed);
                    foreach (string terrain in Terrains)
                    {
                        DungeonCulture c = DungeonCulture.For(planet, terrain, seed);
                        Assert.That(c.AccentRegion, Is.Not.EqualTo(c.Region));
                        Assert.That(c.AccentEra, Is.Not.EqualTo(c.Era));
                        Assert.That(c.AccentShare, Is.InRange(0.10f, 0.35f));
                        Assert.That(c.Look, Is.InRange(0, DungeonCulture.Looks - 1));
                        Assert.That(c.Age, Is.InRange(0f, 1f));
                        Assert.That(c.Damage, Is.InRange(0f, 1f));
                    }
                }
            }
        }

        [Test]
        public void Dungeons_lean_on_the_planet_culture_and_are_mostly_ancient()
        {
            int n = 0, planetRegion = 0, ancient = 0, planetEra = 0;
            var looks = new int[DungeonCulture.Looks];
            for (ulong seed = 1; seed <= 4000; seed++)
            {
                ArchPlanetStyle planet = ArchitectureMeshes.PlanetStyleFor(PlanetTypes[seed % 6], seed);
                DungeonCulture c = DungeonCulture.For(planet, Terrains[seed % 10], seed);
                n++;
                planetRegion += c.Region == planet.Region ? 1 : 0;
                ancient += c.Era == ArchEra.Ancient ? 1 : 0;
                planetEra += c.Era == planet.Era ? 1 : 0;
                looks[c.Look]++;
            }

            // Invariants of the draw, loose enough not to pin the 暫定 shares.
            Assert.That(planetRegion / (float)n, Is.InRange(0.45f, 0.65f));
            Assert.That(ancient / (float)n, Is.InRange(0.3f, 0.5f));
            Assert.That(planetEra / (float)n, Is.InRange(0.2f, 0.4f));
            foreach (int count in looks)
            {
                Assert.That(count, Is.GreaterThan(n / 6));
            }
        }

        [Test]
        public void Terrains_on_one_planet_draw_independently()
        {
            int differ = 0;
            for (ulong seed = 1; seed <= 100; seed++)
            {
                ArchPlanetStyle planet = ArchitectureMeshes.PlanetStyleFor("verdant_planet", seed);
                DungeonCulture a = DungeonCulture.For(planet, "temple", seed);
                DungeonCulture b = DungeonCulture.For(planet, "volcano", seed);
                differ += a.Seed != b.Seed ? 1 : 0;
            }

            Assert.That(differ, Is.EqualTo(100));
        }

        [Test]
        public void Null_terrain_is_rejected()
        {
            ArchPlanetStyle planet = ArchitectureMeshes.PlanetStyleFor("verdant_planet", 1);
            Assert.Throws<ContractException>(() => DungeonCulture.For(planet, null, 1));
        }
    }
}
