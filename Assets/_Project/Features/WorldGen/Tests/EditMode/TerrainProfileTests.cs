using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class TerrainProfileTests
    {
        private static readonly PlanetConfig Planet = new PlanetConfig(640f, float3.zero);

        [Test]
        public void Only_the_ten_advanced_terrains_are_dungeons()
        {
            Assert.That(TerrainProfiles.AdvancedTerrainIds.Count, Is.EqualTo(10));
            foreach (string id in TerrainProfiles.AdvancedTerrainIds)
            {
                TerrainProfile p = TerrainProfiles.For(id);
                Assert.That(p.IsAdvancedDungeon, Is.True, id);
                Assert.That(p.Dungeon, Is.Not.EqualTo(DungeonKind.None), id);
            }

            string[] general =
            {
                "grassland", "ice_field", "desert", "swamp", "mountain", "forest",
                "river_valley", "plateau", "archipelago", "fjord", "canyon", "rift_valley",
                "volcano_foothills", "cavern_outer", "eternal_day", "eternal_night",
            };
            foreach (string id in general)
            {
                TerrainProfile p = TerrainProfiles.For(id);
                Assert.That(p.IsAdvancedDungeon, Is.False, id);
                Assert.That(p.Dungeon, Is.EqualTo(DungeonKind.None), id);
            }
        }

        [Test]
        public void Open_landforms_have_lower_scatter_density_than_forest_and_mountain()
        {
            float grass = TerrainProfiles.For("grassland").ScatterDensity;
            float ice = TerrainProfiles.For("ice_field").ScatterDensity;
            float desert = TerrainProfiles.For("desert").ScatterDensity;
            float forest = TerrainProfiles.For("forest").ScatterDensity;
            float mountain = TerrainProfiles.For("mountain").ScatterDensity;

            Assert.That(grass, Is.LessThan(forest));
            Assert.That(ice, Is.LessThan(forest));
            Assert.That(desert, Is.LessThan(forest));
            Assert.That(grass, Is.LessThan(mountain));
            Assert.That(ice, Is.LessThan(mountain));
            Assert.That(desert, Is.LessThan(mountain));
        }

        [Test]
        public void Landform_action_modifiers_differentiate_swamp_ice_and_plains()
        {
            TerrainProfile grass = TerrainProfiles.For("grassland");
            TerrainProfile swamp = TerrainProfiles.For("swamp");
            TerrainProfile ice = TerrainProfiles.For("ice_field");

            Assert.That(swamp.MoveSpeedMultiplier, Is.LessThan(grass.MoveSpeedMultiplier));
            Assert.That(swamp.SprintSpeedMultiplier, Is.LessThan(grass.SprintSpeedMultiplier));
            Assert.That(ice.GroundAccelerationMultiplier, Is.LessThan(grass.GroundAccelerationMultiplier));
        }

        [Test]
        public void ScatterPlanner_respects_landform_density()
        {
            var open = new ScatterPlanner(Planet, 12345, "ice_crystal_planet", _ => 0f, null, null,
                TerrainProfiles.For("ice_field").ScatterDensity);
            var dense = new ScatterPlanner(Planet, 12345, "verdant_planet", _ => 0f, null, null,
                TerrainProfiles.For("forest").ScatterDensity);

            var cells = new HashSet<long>();
            open.CollectCells(new float3(0f, 1f, 0f), 120f, cells);
            int openCount = 0;
            int denseCount = 0;
            foreach (long k in cells)
            {
                if (open.TryGetItem(k, out _)) openCount++;
                if (dense.TryGetItem(k, out _)) denseCount++;
            }

            Assert.That(openCount, Is.LessThan(denseCount));
        }

        [Test]
        public void ScatterPlanner_rejects_out_of_range_place_chance()
        {
            Assert.Throws<ContractException>(() =>
                new ScatterPlanner(Planet, 1, "verdant_planet", _ => 0f, null, null, -0.1f));
            Assert.Throws<ContractException>(() =>
                new ScatterPlanner(Planet, 1, "verdant_planet", _ => 0f, null, null, 1.1f));
        }
    }
}
