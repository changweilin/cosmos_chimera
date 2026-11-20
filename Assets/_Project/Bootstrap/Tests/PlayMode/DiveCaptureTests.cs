using System.Collections;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    // Visual proof for planet-world.md 潛水視覺: the surface from below, shafts and motes, caustics on the seabed,
    // and the same seabed seen from above.
    public sealed class DiveCaptureTests
    {
        private const float Radius = 8000f;
        private readonly List<Object> _owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _owned) Object.Destroy(o);
            _owned.Clear();
            SeaWaves.Clear();
            Shader.SetGlobalVector("_CosmiUnderwater", Vector4.zero);
        }

        [UnityTest]
        public IEnumerator Capture_under_and_over_the_sea()
        {
            if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") != "1")
            {
                Assert.Ignore("visual proof only: set COSMIMERA_CAPTURE=1 and run without -nographics");
            }

            // Planet centre at −Z so local up is +Z: AtmosphereDriver's sun circles the Y pole, so only here can it
            // stand high overhead. The stage is built Y-up under a root rotated Y → Z.
            var planet = new PlanetConfig(Radius, new float3(0f, 0f, -Radius));
            var stage = new GameObject("dive-stage");
            _owned.Add(stage);
            stage.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Transform root = stage.transform;
            Shader scenery = Shader.Find("Cosmimera/ToonScenery");
            Material sand = PlanetLook.SceneryMaterial(scenery, new Color(0.86f, 0.8f, 0.6f), Color.white, 0f, 0f, 0f, 0f);
            Material rock = PlanetLook.SceneryMaterial(scenery, new Color(0.5f, 0.47f, 0.44f), Color.white, 0f, 0f, 0f, 0f);
            _owned.Add(sand);
            _owned.Add(rock);
            Prop(PrimitiveType.Plane, root, new Vector3(0f, -7f, 0f), new Vector3(12f, 1f, 12f), sand);
            Prop(PrimitiveType.Plane, root, new Vector3(-50f, -1.2f, 30f), new Vector3(6f, 1f, 6f), sand);
            for (int i = 0; i < 7; i++)
            {
                float a = i * 0.9f;
                Prop(PrimitiveType.Sphere, root, new Vector3(math.cos(a) * (6f + i * 2f), -6.5f, 8f + math.sin(a) * 6f),
                    Vector3.one * (1.5f + (i % 3)), rock);
            }

            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.transform.SetParent(root, false);
            camera.fieldOfView = 60f;
            camera.farClipPlane = 1000f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            _owned.Add(sun.gameObject);
            var atmosphere = stage.AddComponent<AtmosphereDriver>();
            atmosphere.Configure(camera, sun, planet, new float3(0f, 1f, 0f), PlanetLook.Sky("verdant_planet"), 360f, 1.7f);
            atmosphere.SetSimulationTime(70_000);
            var water = stage.AddComponent<WaterSurface>();
            Material waterMat = PlanetLook.WaterMaterial(Shader.Find("Cosmimera/ToonWater"), "verdant_planet", planet, 30f);
            water.Configure(planet, waterMat, 400f);
            _owned.Add(water.Root);
            var sea = new SeaWaves();
            var view = new UnderwaterView(stage.transform);
            Color shallow = waterMat.GetColor("_ShallowWater");
            Color deep = waterMat.GetColor("_DeepWater");
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
            try
            {
                Place(camera, root, new Vector3(0f, 1.2f, -14f), new Vector3(0f, -4f, 0f));
                yield return Run(sea, view, atmosphere, planet, camera, sun, shallow, deep, 10);
                Place(camera, root, new Vector3(0f, -3f, -14f), new Vector3(0f, -4f, 0f));
                yield return Run(sea, view, atmosphere, planet, camera, sun, shallow, deep, 50);
                Assert.That(view.Submerged, Is.True);
                ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "dive-seabed.png"));
                Place(camera, root, new Vector3(0f, -4f, -6f), new Vector3(2f, 3f, 4f));
                yield return Run(sea, view, atmosphere, planet, camera, sun, shallow, deep, 20);
                ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "dive-looking-up.png"));
                Place(camera, root, new Vector3(-40f, 4f, 18f), new Vector3(-50f, -1.2f, 30f));
                yield return Run(sea, view, atmosphere, planet, camera, sun, shallow, deep, 20);
                Assert.That(view.Submerged, Is.False);
                ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "dive-shallows-from-above.png"));
            }
            finally
            {
                view.Dispose();
            }
        }

        private static void Prop(PrimitiveType type, Transform root, Vector3 at, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(root, false);
            go.transform.localPosition = at;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void Place(Camera camera, Transform root, Vector3 at, Vector3 lookAt)
        {
            camera.transform.localPosition = at;
            camera.transform.LookAt(root.TransformPoint(lookAt), root.up);
        }

        private static IEnumerator Run(SeaWaves sea, UnderwaterView view, AtmosphereDriver atmosphere, PlanetConfig planet,
            Camera camera, Light sun, Color shallow, Color deep, int frames)
        {
            // Batchmode frames run unthrottled: wait real time so particles build up and the screen grade eases in.
            for (int i = 0; i < frames; i++)
            {
                float3 eye = camera.transform.position;
                float3 up = planet.ToDirection(eye);
                float3 seaPoint = planet.ToWorld(up, PlanetLook.SeaLevelM);
                float3 east = math.normalize(math.cross(up, new float3(0f, 1f, 0f)));
                sea.Step(seaPoint, up, east, 0.3f, 0.1f, 0.05f);
                sea.Publish(0.1f);
                float depth = PlanetLook.SeaLevelM + sea.HeightAt(seaPoint) - planet.Altitude(eye);
                atmosphere.SetUnderwater(depth, shallow, deep);
                view.Tick(0.05f, planet, camera.transform, eye + (float3)camera.transform.forward * 4f - up * 1.6f, depth, Radius + PlanetLook.SeaLevelM, -(float3)sun.transform.forward, 1f);
                yield return new WaitForSeconds(0.05f);
            }
        }
    }
}
