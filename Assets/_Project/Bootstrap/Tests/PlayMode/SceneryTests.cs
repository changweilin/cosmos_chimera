using System.Collections;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: vision.md#畫風與沉浸感; roadmap P1-08 (sky), P1-09 (grass), P3-04 (liquid surface).
    public sealed class SceneryTests
    {
        private const float Radius = 320f;
        private readonly List<GameObject> _roots = new List<GameObject>();

        private GrassField Grass(System.Func<float3, float> altitude, System.Func<float3, bool> isClear, float density,
            float3 up, ulong seed = 12345)
        {
            var root = new GameObject("grass-test");
            _roots.Add(root);
            var config = new PlanetConfig(Radius, float3.zero);
            Shader shader = Shader.Find("Cosmimera/ToonGrass");
            Assert.That(shader, Is.Not.Null);
            GrassField field = root.AddComponent<GrassField>();
            field.Configure(config, seed, altitude, isClear, () => config.ToWorld(up, 0f),
                PlanetLook.GrassMaterial(shader, "verdant_planet", config, 30f, GrassField.RadiusM), density, 20f, 0.9f);
            return field;
        }

        private static IEnumerator Settle()
        {
            // The disc holds ~250 cells and streams 10 per frame.
            for (int i = 0; i < 60; i++)
            {
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject root in _roots) Object.Destroy(root);
            _roots.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Grass_grows_on_open_meadow_and_regrows_identically_for_the_same_seed()
        {
            var up = math.normalize(new float3(0.3f, 0.2f, 0.9f));
            GrassField a = Grass(_ => 5f, null, 1f, up);
            GrassField b = Grass(_ => 5f, null, 1f, up);
            GrassField other = Grass(_ => 5f, null, 0.5f, up, 999);
            yield return Settle();
            Assert.That(a.InstanceCount, Is.GreaterThan(1000), "a 30 m disc of meadow is thousands of clumps");
            Assert.That(b.InstanceCount, Is.EqualTo(a.InstanceCount), "same seed, same place: same grass");
            Assert.That(other.InstanceCount, Is.LessThan(a.InstanceCount), "density thins the field");
            Assert.That(other.InstanceCount, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator Grass_keeps_off_sea_sand_snow_cliffs_poles_and_cleared_ground()
        {
            var up = math.normalize(new float3(0.3f, 0.2f, 0.9f));
            GrassField sea = Grass(_ => -3f, null, 1f, up);
            GrassField sand = Grass(_ => 0.5f, null, 1f, up);
            GrassField snow = Grass(_ => 25f, null, 1f, up);                    // above maxAltitudeM = 20
            GrassField cliff = Grass(d => d.x * Radius * 0.6f + 100f, null, 1f, up); // rise/run ≈ 0.6 everywhere near `up`
            GrassField road = Grass(_ => 5f, _ => false, 1f, up);
            GrassField pole = Grass(_ => 5f, null, 1f, new float3(0f, 1f, 0f)); // |sin lat| > 0.9 within 30 m of the pole
            GrassField barren = Grass(_ => 5f, null, 0f, up);
            yield return Settle();
            Assert.That(sea.InstanceCount, Is.Zero);
            Assert.That(sand.InstanceCount, Is.Zero);
            Assert.That(snow.InstanceCount, Is.Zero);
            Assert.That(cliff.InstanceCount, Is.Zero);
            Assert.That(road.InstanceCount, Is.Zero);
            Assert.That(pole.InstanceCount, Is.Zero);
            Assert.That(barren.InstanceCount, Is.Zero);
        }

        [Test]
        public void Grass_configure_rejects_missing_inputs_and_out_of_range_density()
        {
            var config = new PlanetConfig(Radius, float3.zero);
            var root = new GameObject("grass-contract");
            _roots.Add(root);
            GrassField field = root.AddComponent<GrassField>();
            Material material = PlanetLook.GrassMaterial(Shader.Find("Cosmimera/ToonGrass"), null, config, 30f, 30f);
            try
            {
                Assert.Throws<ContractException>(() => field.Configure(config, 1, null, null, () => float3.zero, material, 1f, 20f, 0.9f));
                Assert.Throws<ContractException>(() => field.Configure(config, 1, _ => 0f, null, null, material, 1f, 20f, 0.9f));
                Assert.Throws<ContractException>(() => field.Configure(config, 1, _ => 0f, null, () => float3.zero, null, 1f, 20f, 0.9f));
                Assert.Throws<ContractException>(() => field.Configure(config, 1, _ => 0f, null, () => float3.zero, material, 1.5f, 20f, 0.9f));
                Assert.Throws<ContractException>(() => field.Configure(config, 1, _ => 0f, null, () => float3.zero, material, 1f, 0f, 0.9f));
                Assert.Throws<ContractException>(() => field.Configure(config, 1, _ => 0f, null, () => float3.zero, material, 1f, 20f, 0f));
            }
            finally
            {
                Object.Destroy(material);
            }
        }

        [UnityTest]
        public IEnumerator Sky_and_water_build_one_renderer_each_and_remove_it_with_themselves()
        {
            var config = new PlanetConfig(Radius, float3.zero);
            var root = new GameObject("scenery-test");
            _roots.Add(root);
            SkyDome sky = root.AddComponent<SkyDome>();
            sky.Configure(config, Shader.Find("Cosmimera/ToonSky"));
            WaterSurface water = root.AddComponent<WaterSurface>();
            water.Configure(config, PlanetLook.WaterMaterial(Shader.Find("Cosmimera/ToonWater"), "lava_planet", config, 30f), 300f);
            Assert.Throws<ContractException>(() => sky.Configure(config, Shader.Find("Cosmimera/ToonSky")), "Configure is once-only");
            GameObject skyRoot = sky.Root;
            GameObject waterRoot = water.Root;
            Assert.That(skyRoot.GetComponent<MeshRenderer>().sharedMaterial.shader.name, Is.EqualTo("Cosmimera/ToonSky"));
            Material waterMaterial = waterRoot.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(waterMaterial.GetFloat("_WaterGlow"), Is.EqualTo(1f), "lava worlds get a molten surface");
            Assert.That(waterMaterial.GetFloat("_SeaLevel"), Is.EqualTo(PlanetLook.SeaLevelM));
            // Both are placed by their vertex shaders, so their bounds must cover the whole planet or they get culled.
            Assert.That(waterRoot.GetComponent<MeshRenderer>().bounds.Contains(new Vector3(0f, -Radius, 0f)));
            Assert.That(skyRoot.GetComponent<MeshRenderer>().bounds.Contains(new Vector3(Radius, 0f, 0f)));

            Object.Destroy(root);
            _roots.Clear();
            yield return null;
            Assert.That(skyRoot == null && waterRoot == null, "roots live outside the component's hierarchy and must not leak");
        }

        [Test]
        public void Scenery_rules_are_in_range_for_every_planet_type_and_barren_worlds_grow_no_grass()
        {
            foreach (string id in new[] { "verdant_planet", "lava_planet", "ice_crystal_planet", "storm_planet",
                         "mineral_planet", "twilight_planet", null, "unknown" })
            {
                (float grass, float snowLine, float polarLat, float cloud, float wind) = PlanetLook.Scenery(id);
                Assert.That(grass, Is.InRange(0f, 1f), id);
                Assert.That(cloud, Is.InRange(0f, 1f), id);
                Assert.That(wind, Is.GreaterThanOrEqualTo(0f), id);
                Assert.That(snowLine, Is.GreaterThan(0f), id);
                Assert.That(polarLat, Is.InRange(0f, 1f), id);
            }

            Assert.That(PlanetLook.Scenery("lava_planet").grassDensity, Is.Zero);
            Assert.That(PlanetLook.Scenery("ice_crystal_planet").grassDensity, Is.Zero);
            Assert.That(PlanetLook.Scenery(null), Is.EqualTo(PlanetLook.Scenery("verdant_planet")), "unknown ids read as verdant");
        }

        [Test]
        public void Weather_rejects_out_of_range_values()
        {
            var root = new GameObject("weather-test");
            _roots.Add(root);
            AtmosphereDriver driver = root.AddComponent<AtmosphereDriver>();
            Assert.DoesNotThrow(() => driver.SetWeather(0f, 0f));
            Assert.DoesNotThrow(() => driver.SetWeather(1f, 3f));
            Assert.Throws<ContractException>(() => driver.SetWeather(1.1f, 1f));
            Assert.Throws<ContractException>(() => driver.SetWeather(-0.1f, 1f));
            Assert.Throws<ContractException>(() => driver.SetWeather(0.5f, -1f));
        }
    }
}
