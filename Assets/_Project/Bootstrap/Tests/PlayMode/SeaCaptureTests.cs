using System.Collections;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    // Visual proof for planet-world.md#海浪: the same beach and sea cliff under a calm breeze and under a gale.
    public sealed class SeaCaptureTests
    {
        private const float Radius = 8000f;
        private readonly List<Object> _owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _owned) Object.Destroy(o);
            _owned.Clear();
            SeaWaves.Clear();
            Shader.SetGlobalVector("_CosmiPlanet", Vector4.zero);
            Shader.SetGlobalColor("_CosmiSkyHorizon", Color.clear);
            Shader.SetGlobalColor("_CosmiSkyZenith", Color.clear);
        }

        // Beach rising toward −x out of a sea at +x, and a sea cliff wall along z ≈ 25.
        private static float Ground(float3 local)
        {
            if (local.z > 22f && local.z < 28f && local.x < 16f) return 6f;
            return -0.12f - local.x * 0.08f;
        }

        [UnityTest]
        public IEnumerator Capture_calm_and_gale_seas()
        {
            if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") != "1")
            {
                Assert.Ignore("visual proof only: set COSMIMERA_CAPTURE=1 and run without -nographics");
            }

            var planet = new PlanetConfig(Radius, new float3(0f, -Radius, 0f));
            var stage = new GameObject("sea-stage");
            _owned.Add(stage);
            Material sand = PlanetLook.SceneryMaterial(Shader.Find("Cosmimera/ToonScenery"), new Color(0.86f, 0.8f, 0.6f), Color.white, 0f, 0f, 0f, 0f);
            Material rock = PlanetLook.SceneryMaterial(Shader.Find("Cosmimera/ToonScenery"), new Color(0.45f, 0.42f, 0.4f), Color.white, 0f, 0f, 0f, 0f);
            _owned.Add(sand);
            _owned.Add(rock);
            GameObject beach = GameObject.CreatePrimitive(PrimitiveType.Plane);
            beach.transform.SetParent(stage.transform, false);
            beach.transform.localScale = new Vector3(12f, 1f, 12f);
            beach.transform.position = new Vector3(0f, -0.12f, 0f);
            beach.transform.rotation = Quaternion.Euler(0f, 0f, -math.degrees(math.atan(0.08f)));
            beach.GetComponent<Renderer>().sharedMaterial = sand;
            GameObject cliff = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cliff.transform.SetParent(stage.transform, false);
            cliff.transform.position = new Vector3(-12f, 1f, 25f);
            cliff.transform.localScale = new Vector3(56f, 14f, 6f);
            cliff.GetComponent<Renderer>().sharedMaterial = rock;

            var sun = new GameObject("sun").AddComponent<Light>();
            sun.transform.SetParent(stage.transform, false);
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(35f, 60f, 0f);
            RenderSettings.ambientLight = new Color(0.5f, 0.6f, 0.7f);
            Shader.SetGlobalVector("_CosmiPlanet", new Vector4(0f, -Radius, 0f, Radius));
            Shader.SetGlobalColor("_CosmiSkyHorizon", new Color(0.72f, 0.84f, 0.93f, 1f));
            Shader.SetGlobalColor("_CosmiSkyZenith", new Color(0.25f, 0.45f, 0.8f, 1f));

            var water = stage.AddComponent<WaterSurface>();
            water.Configure(planet, PlanetLook.WaterMaterial(Shader.Find("Cosmimera/ToonWater"), "verdant_planet", planet, 30f), 400f);
            _owned.Add(water.Root);
            var sea = new SeaWaves();
            var spray = new SeaSprayView(stage.transform);
            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.transform.SetParent(stage.transform, false);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.72f, 0.84f, 0.93f);
            camera.fieldOfView = 55f;
            camera.farClipPlane = 1000f;
            camera.transform.position = new Vector3(-16f, 8f, -14f);
            camera.transform.LookAt(new Vector3(14f, 0f, 12f));
            float3 player = new float3(-8f, 0f, -2f);
            float3 downwind = math.normalize(new float3(-1f, 0f, 0.35f));
            float3 up = planet.ToDirection(player);
            System.Func<float3, float> ground = d => Ground(planet.ToWorld(d, 0f));
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
            try
            {
                yield return Run(sea, spray, planet, player, up, downwind, ground, 0.15f, 0f, 40);
                ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "sea-calm.png"));
                yield return Run(sea, spray, planet, player, up, downwind, ground, 0.92f, 0.6f, 90);
                ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "sea-gale.png"));
                camera.transform.position = new Vector3(-6f, 4f, 4f);
                camera.transform.LookAt(new Vector3(14f, 1f, 22f));
                yield return Run(sea, spray, planet, player, up, downwind, ground, 0.92f, 0.6f, 30);
                ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "sea-gale-cliff.png"));
                Assert.That(spray.SpotCount, Is.GreaterThan(0), "the cliff at the waterline should be found as a spray spot");
            }
            finally
            {
                spray.Dispose();
            }
        }

        private static IEnumerator Run(SeaWaves sea, SeaSprayView spray, PlanetConfig planet, float3 player, float3 up,
            float3 downwind, System.Func<float3, float> ground, float wind, float gust, int frames)
        {
            float3 seaPoint = planet.ToWorld(up, PlanetLook.SeaLevelM);
            // Batchmode frames run unthrottled: wait real time so the swells and spray actually move.
            for (int i = 0; i < frames; i++)
            {
                sea.Step(seaPoint, up, downwind, wind, gust, 0.05f);
                sea.Publish(gust);
                spray.Tick(0.05f, planet, player, downwind, sea, ground, PlanetLook.SeaLevelM);
                yield return new WaitForSeconds(0.05f);
            }
        }
    }
}
