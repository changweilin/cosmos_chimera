using System.Collections;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Combat;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    // Visual proof for destructibles.md#延燒 and weather.md#落雷: the six fire looks, a blast, scorch marks and a bolt.
    public sealed class FireCaptureTests
    {
        private readonly List<GameObject> _roots = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _roots) Object.Destroy(go);
            _roots.Clear();
        }

        [UnityTest]
        public IEnumerator Capture_fire_kinds_and_a_lightning_bolt()
        {
            if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") != "1")
            {
                Assert.Ignore("visual proof only: set COSMIMERA_CAPTURE=1 and run without -nographics");
            }

            // A huge planet centred far below makes the stage locally flat with +Y as up.
            const float radius = 8000f;
            var planet = new PlanetConfig(radius, new float3(0f, -radius, 0f));
            var stage = new GameObject("fire-lineup");
            _roots.Add(stage);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(stage.transform, false);
            ground.transform.localScale = Vector3.one * 20f;
            ground.GetComponent<Renderer>().sharedMaterial =
                PlanetLook.SceneryMaterial(Shader.Find("Cosmimera/ToonScenery"), new Color(0.43f, 0.6f, 0.29f), Color.white, 0f, 0f, 0f, 0f);
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.transform.SetParent(stage.transform, false);
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

            var fires = new FireView(stage.transform);
            var lightning = new LightningView(stage.transform, 3);
            try
            {
                FireKind[] kinds = { FireKind.Grass, FireKind.Crown, FireKind.Structure, FireKind.Electrical, FireKind.Lava };
                var burning = new List<FireView.Burning>();
                for (int i = 0; i < kinds.Length; i++)
                {
                    float height = kinds[i] == FireKind.Grass ? 0.3f : kinds[i] == FireKind.Crown ? 3f : 1.5f;
                    burning.Add(new FireView.Burning
                    {
                        Id = i, Center = new float3((i - 2) * 9f, height, 0f), RadiusM = kinds[i] == FireKind.Grass ? 1.2f : 1.5f,
                        Kind = kinds[i],
                    });
                }

                float3 up = new float3(0f, 1f, 0f);
                fires.Scorch(new float3(-18f, 0f, 8f), up, 1.5f, FireKind.Crown);
                fires.Scorch(new float3(-12f, 0f, 8f), up, 1.2f, FireKind.Grass);
                var camera = new GameObject("camera").AddComponent<Camera>();
                camera.transform.SetParent(stage.transform, false);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.fieldOfView = 45f;
                camera.transform.position = new Vector3(0f, 9f, -30f);
                camera.transform.LookAt(new Vector3(0f, 2f, 0f));
                string dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs");

                // Batchmode frames run unthrottled: wait real time so the particles actually build up.
                for (int frame = 0; frame < 60; frame++)
                {
                    fires.Sync(burning, planet, new float3(1f, 0f, 0f), 0.4f, 0f, 0.05f);
                    if (frame == 56) fires.Blast(new float3(20f, 0.5f, 6f), up);
                    yield return new WaitForSeconds(0.05f);
                }

                sun.intensity = 1.1f;
                RenderSettings.ambientLight = new Color(0.5f, 0.6f, 0.7f);
                camera.backgroundColor = new Color(0.55f, 0.75f, 0.92f);
                ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "fire-kinds-day.png"));
                fires.Sync(burning, planet, new float3(1f, 0f, 0f), 0.4f, 1f, 0.05f);
                sun.intensity = 0.05f;
                RenderSettings.ambientLight = new Color(0.05f, 0.06f, 0.1f);
                camera.backgroundColor = new Color(0.03f, 0.04f, 0.08f);
                ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "fire-kinds-night.png"));

                // A bolt 60 m out, seen from the same spot at night.
                float3 playerDirection = planet.ToDirection(new float3(0f, 0f, -30f));
                float3 strike = planet.ToDirection(new float3(10f, 0f, 30f));
                lightning.StrikeAt(planet, strike, playerDirection, d => 0f);
                lightning.Tick(0.01f, planet, playerDirection, 0f, d => 0f);
                camera.transform.LookAt(new Vector3(10f, 40f, 30f));
                yield return null;
                ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "lightning-strike-night.png"));
            }
            finally
            {
                fires.Dispose();
                lightning.Dispose();
            }
        }
    }
}
