using System.Collections;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Combat;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    // Visual proof for planet-world.md#地震噴發: an eruption by day and night, the flow cooling, and the black rock left.
    public sealed class QuakeCaptureTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;

            public bool IsSea(float3 direction) => false;

            public bool IsBlocked(float3 direction, float clearanceM) => false;
        }

        private const float Radius = 8000f;
        private const int Dt = 50;
        private const ulong Seed = 21;
        private readonly List<Object> _owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _owned) Object.Destroy(o);
            _owned.Clear();
        }

        [UnityTest]
        public IEnumerator Capture_eruption_cooling_and_black_rock()
        {
            if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") != "1")
            {
                Assert.Ignore("visual proof only: set COSMIMERA_CAPTURE=1 and run without -nographics");
            }

            var planet = new PlanetConfig(Radius, new float3(0f, -Radius, 0f));
            var stage = new GameObject("quake-stage");
            _owned.Add(stage);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(stage.transform, false);
            ground.transform.localScale = Vector3.one * 20f;
            Material ash = PlanetLook.SceneryMaterial(Shader.Find("Cosmimera/ToonScenery"), new Color(0.32f, 0.27f, 0.25f), Color.white, 0f, 0f, 0f, 0f);
            _owned.Add(ash);
            ground.GetComponent<Renderer>().sharedMaterial = ash;
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.transform.SetParent(stage.transform, false);
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

            string balance = File.ReadAllText(Path.Combine(Application.dataPath, "..", "Data", "balance.json"));
            var encounter = new Encounter(planet, CombatBalance.FromJson(balance), new FlatSurface(), 7);
            float3 up = new float3(0f, 1f, 0f);
            encounter.AttachQuakes(Seed, _ => 0, _ => up, 30f, true, _ => true);
            QuakeSchedule.Timing(Seed, 0, 0, true, out long start, out _, out _);

            var view = new QuakeView(stage.transform);
            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.transform.SetParent(stage.transform, false);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.fieldOfView = 50f;
            camera.transform.position = new Vector3(0f, 16f, -42f);
            camera.transform.LookAt(new Vector3(0f, 2f, 0f));
            float3 player = new float3(0f, 0f, -60f);
            string dir = Path.Combine(Application.dataPath, "..", "Logs");
            long now = start - 200;
            System.Func<float3, float> flat = d => 0f;
            try
            {
                encounter.Tick(now, Dt, player, new AiCommand(AiCommandKind.Free, CombatAi.NoTarget), CombatAi.NoTarget);
                Quake q = encounter.Quakes[0];
                Day(sun, camera);
                // Batchmode frames run unthrottled: wait real time so fountains and smoke build up.
                now = q.CrackMs - 100;
                for (int i = 0; i < 70; i++)
                {
                    now += Dt;
                    encounter.Tick(now, Dt, player, new AiCommand(AiCommandKind.Free, CombatAi.NoTarget), CombatAi.NoTarget);
                    view.Sync(encounter.Quakes, encounter.PendingBombs, planet, flat, now, player, 0, 0f, 0.05f);
                    yield return new WaitForSeconds(0.05f);
                }

                ScatterFieldTests.Shoot(camera, Path.Combine(dir, "quake-eruption-day.png"));
                Night(sun, camera);
                view.Sync(encounter.Quakes, encounter.PendingBombs, planet, flat, now, player, 0, 1f, 0f);
                ScatterFieldTests.Shoot(camera, Path.Combine(dir, "quake-eruption-night.png"));

                Day(sun, camera);
                yield return Jump(encounter, view, planet, flat, player, q.EruptEndMs + 25_000);
                ScatterFieldTests.Shoot(camera, Path.Combine(dir, "quake-cooling.png"));
                yield return Jump(encounter, view, planet, flat, player, q.CoolEndMs + 5_000);
                Assert.That(view.ScarCount, Is.EqualTo(1), "black rock stays while the player is in the region");
                ScatterFieldTests.Shoot(camera, Path.Combine(dir, "quake-black-rock.png"));
            }
            finally
            {
                view.Dispose();
            }
        }

        private static IEnumerator Jump(Encounter encounter, QuakeView view, PlanetConfig planet, System.Func<float3, float> flat,
            float3 player, long at)
        {
            for (int i = 0; i < 20; i++)
            {
                long now = at - (19 - i) * Dt;
                encounter.Tick(now, Dt, player, new AiCommand(AiCommandKind.Free, CombatAi.NoTarget), CombatAi.NoTarget);
                view.Sync(encounter.Quakes, encounter.PendingBombs, planet, flat, now, player, 0, 0f, 0.05f);
                yield return new WaitForSeconds(0.05f);
            }
        }

        private static void Day(Light sun, Camera camera)
        {
            sun.intensity = 1.1f;
            RenderSettings.ambientLight = new Color(0.5f, 0.55f, 0.6f);
            camera.backgroundColor = new Color(0.62f, 0.66f, 0.72f);
        }

        private static void Night(Light sun, Camera camera)
        {
            sun.intensity = 0.05f;
            RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.08f);
            camera.backgroundColor = new Color(0.03f, 0.03f, 0.06f);
        }
    }
}
