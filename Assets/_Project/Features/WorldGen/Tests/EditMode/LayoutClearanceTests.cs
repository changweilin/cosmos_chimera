using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class LayoutClearanceTests
    {
        private const float Radius = 100f;
        private const float Step = 0.07f; // 7 m per segment at R = 100
        private static readonly PlanetConfig Planet = new PlanetConfig(Radius, float3.zero);

        // One 4 m main road along the x–z great circle from θ = 0 to 3·Step, and one 6 × 4 m house at +y facing +x.
        private static RunLayout Layout()
        {
            var points = new float3[4];
            for (int k = 0; k < points.Length; k++)
            {
                points[k] = new float3(math.cos(k * Step), 0f, math.sin(k * Step));
            }

            var road = new RunRoad(RoadClass.Main, 0, 1, 4f, points, new bool[points.Length]);
            var house = new RunBuilding(0, BuildingKind.House, new float3(0, 1, 0), new float3(1, 0, 0), 6f, 4f, 1);
            var village = new RunSettlement(0, 0, SettlementKind.Village, new float3(0, 1, 0), 0, 1);
            return new RunLayout(new[] { road }, new[] { village }, new[] { house });
        }

        private static float3 OnRoad(float theta, float lateralM) =>
            math.normalize(new float3(math.cos(theta), 0f, math.sin(theta)) + new float3(0f, lateralM / Radius, 0f));

        private static float3 NearHouse(float3 tangentM) => math.normalize(new float3(0, 1, 0) + tangentM / Radius);

        [TestCase(0f)]
        [TestCase(1.5f)]
        public void Road_band_is_half_width_plus_margin(float margin)
        {
            var c = new LayoutClearance(Planet, Layout(), margin);
            float edge = 2f + margin;
            foreach (float theta in new[] { 0f, 0.5f * Step, 1.5f * Step, 3f * Step })
            {
                Assert.That(c.IsClear(OnRoad(theta, 0f)), Is.False, $"centre line θ={theta}");
                Assert.That(c.IsClear(OnRoad(theta, edge - 0.05f)), Is.False, $"inside band θ={theta}");
                Assert.That(c.IsClear(OnRoad(theta, -(edge - 0.05f))), Is.False, $"inside band (other side) θ={theta}");
                Assert.That(c.IsClear(OnRoad(theta, edge + 0.05f)), Is.True, $"outside band θ={theta}");
            }

            // Beyond either end the band is a round cap.
            float endM = 3f * Step * Radius;
            Assert.That(c.IsClear(OnRoad((endM + edge - 0.05f) / Radius, 0f)), Is.False);
            Assert.That(c.IsClear(OnRoad((endM + edge + 0.05f) / Radius, 0f)), Is.True);
            Assert.That(c.IsClear(OnRoad(-(edge + 0.05f) / Radius, 0f)), Is.True);
        }

        [TestCase(0f)]
        [TestCase(1.5f)]
        public void Building_footprint_grows_by_margin_on_every_side(float margin)
        {
            var c = new LayoutClearance(Planet, Layout(), margin);
            float3 facing = new float3(1, 0, 0);
            float3 across = math.cross(new float3(0, 1, 0), facing);
            Assert.That(c.IsClear(new float3(0, 1, 0)), Is.False);
            foreach (float sign in new[] { 1f, -1f })
            {
                Assert.That(c.IsClear(NearHouse(sign * facing * (2f + margin - 0.05f))), Is.False);
                Assert.That(c.IsClear(NearHouse(sign * facing * (2f + margin + 0.05f))), Is.True);
                Assert.That(c.IsClear(NearHouse(sign * across * (3f + margin - 0.05f))), Is.False);
                Assert.That(c.IsClear(NearHouse(sign * across * (3f + margin + 0.05f))), Is.True);
            }
        }

        [Test]
        public void Antipode_of_a_building_is_clear()
        {
            var c = new LayoutClearance(Planet, Layout(), 1f);
            Assert.That(c.IsClear(new float3(0, -1, 0)), Is.True);
        }

        [Test]
        public void Unnormalized_direction_is_normalized()
        {
            var c = new LayoutClearance(Planet, Layout(), 0f);
            Assert.That(c.IsClear(new float3(0, 7, 0)), Is.False);
            Assert.That(c.IsClear(OnRoad(Step, 0f) * 3f), Is.False);
        }

        [Test]
        public void Empty_layout_is_clear_everywhere()
        {
            var c = new LayoutClearance(Planet, new RunLayout(new RunRoad[0], new RunSettlement[0], new RunBuilding[0]), 5f);
            Assert.That(c.IsClear(new float3(1, 0, 0)), Is.True);
        }

        [Test]
        public void Inside_building_ignores_roads_and_the_constructor_margin()
        {
            var c = new LayoutClearance(Planet, Layout(), 5f);
            float3 facing = new float3(1, 0, 0);
            Assert.That(c.IsInsideBuilding(OnRoad(Step, 0f), 0f), Is.False, "roads are not buildings");
            Assert.That(c.IsInsideBuilding(new float3(0, 1, 0), 0f), Is.True);
            Assert.That(c.IsInsideBuilding(NearHouse(facing * 2.3f), 0f), Is.False);
            Assert.That(c.IsInsideBuilding(NearHouse(facing * 2.3f), 0.5f), Is.True);
            Assert.That(c.IsInsideBuilding(new float3(0, -1, 0), 0.5f), Is.False, "antipode");
        }

        [Test]
        public void Removed_building_stops_blocking_but_still_keeps_scatter_clear()
        {
            var c = new LayoutClearance(Planet, Layout(), 0f);
            c.RemoveBuilding(0);
            c.RemoveBuilding(0);
            Assert.That(c.IsInsideBuilding(new float3(0, 1, 0), 0f), Is.False);
            Assert.That(c.IsClear(new float3(0, 1, 0)), Is.False, "placement must not depend on destruction");
            Assert.Throws<ContractException>(() => c.RemoveBuilding(1));
            Assert.Throws<ContractException>(() => c.RemoveBuilding(-1));
        }

        [Test]
        public void Contract_violations_throw()
        {
            RunLayout layout = Layout();
            Assert.Throws<ContractException>(() => new LayoutClearance(default, layout, 0f));
            Assert.Throws<ContractException>(() => new LayoutClearance(Planet, null, 0f));
            Assert.Throws<ContractException>(() => new LayoutClearance(Planet, layout, -0.1f));
            Assert.Throws<ContractException>(() => new LayoutClearance(Planet, layout, float.NaN));
            var c = new LayoutClearance(Planet, layout, 0f);
            Assert.Throws<ContractException>(() => c.IsClear(float3.zero));
            Assert.Throws<ContractException>(() => c.IsClear(new float3(float.NaN, 0f, 1f)));
            Assert.Throws<ContractException>(() => c.IsInsideBuilding(float3.zero, 0f));
            Assert.Throws<ContractException>(() => c.IsInsideBuilding(new float3(0, 1, 0), -1f));
            Assert.Throws<ContractException>(() => c.IsInsideBuilding(new float3(0, 1, 0), float.PositiveInfinity));
        }
    }
}
