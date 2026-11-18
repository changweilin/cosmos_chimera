using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.WorldGen;
using NUnit.Framework;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class PlanetNameDrawTests
    {
        [Test]
        public void Same_planet_type_and_seed_draw_identical_indices()
        {
            for (ulong seed = 0; seed < 50; seed++)
            {
                PlanetNameIndices a = PlanetNameDraw.Draw("verdant_planet", seed, 28, 28);
                PlanetNameIndices b = PlanetNameDraw.Draw("verdant_planet", seed, 28, 28);
                Assert.That(b, Is.EqualTo(a));
                Assert.That(a.WeatherIndex, Is.InRange(0, 27));
                Assert.That(a.TerrainIndex, Is.InRange(0, 27));
            }
        }

        [Test]
        public void Invalid_counts_throw_contract_exception()
        {
            Assert.Throws<ContractException>(() => PlanetNameDraw.Draw("verdant_planet", 1, 0, 10));
            Assert.Throws<ContractException>(() => PlanetNameDraw.Draw("verdant_planet", 1, -3, 10));
            Assert.Throws<ContractException>(() => PlanetNameDraw.Draw("verdant_planet", 1, 10, 0));
            Assert.Throws<ContractException>(() => PlanetNameDraw.Draw("verdant_planet", 1, 10, -1));
        }

        [Test]
        public void Different_seeds_cover_many_weather_and_terrain_affixes()
        {
            var weathers = new HashSet<int>();
            var terrains = new HashSet<int>();
            var pairs = new HashSet<(int, int)>();
            for (ulong seed = 0; seed < 300; seed++)
            {
                PlanetNameIndices pick = PlanetNameDraw.Draw("storm_planet", seed, 28, 28);
                weathers.Add(pick.WeatherIndex);
                terrains.Add(pick.TerrainIndex);
                pairs.Add((pick.WeatherIndex, pick.TerrainIndex));
            }

            Assert.That(weathers.Count, Is.GreaterThanOrEqualTo(25));
            Assert.That(terrains.Count, Is.GreaterThanOrEqualTo(25));
            Assert.That(pairs.Count, Is.GreaterThan(200));
        }

        [Test]
        public void Different_planet_types_with_same_seed_draw_independently()
        {
            var draws = new HashSet<(int, int)>();
            string[] types =
            {
                "verdant_planet", "lava_planet", "ice_crystal_planet",
                "storm_planet", "mineral_planet", "twilight_planet",
            };
            foreach (string t in types)
            {
                PlanetNameIndices pick = PlanetNameDraw.Draw(t, 42, 28, 28);
                draws.Add((pick.WeatherIndex, pick.TerrainIndex));
            }

            Assert.That(draws.Count, Is.GreaterThanOrEqualTo(5));
        }
    }
}
