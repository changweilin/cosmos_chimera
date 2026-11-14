using System.Collections;
using Cosmimera.Bootstrap.Demo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class PropLineupTests
    {
        private GameObject _stage;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_stage);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Capture_prop_lineup_when_requested()
        {
            if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") != "1")
            {
                Assert.Ignore("visual proof only: set COSMIMERA_CAPTURE=1 and run without -nographics");
            }

            _stage = new GameObject("prop-lineup");
            Shader scenery = Shader.Find("Cosmimera/ToonScenery");
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(_stage.transform, false);
            ground.transform.localPosition = new Vector3(0f, 0f, 100f);
            ground.transform.localScale = Vector3.one * 50f;
            ground.GetComponent<Renderer>().sharedMaterial =
                PlanetLook.SceneryMaterial(scenery, new Color(0.43f, 0.69f, 0.29f), Color.white, 0f, 0f, 0f, 0f);
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.transform.SetParent(_stage.transform, false);
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            RenderSettings.ambientLight = new Color(0.55f, 0.78f, 0.94f) * 0.6f;

            // Sizes and materials as ObjectPhysics and DungeonView use them; loose pieces are lifted to rest on the plane.
            (PropShape shape, Vector3 scale, float lift, float spacing, Color color, Color accent, float accentStrength, float glow)[] rows =
            {
                (PropShape.Shard, Vector3.one * 0.9f, 0.3f, 1.4f, new Color(0.43f, 0.29f, 0.17f), new Color(0.31f, 0.21f, 0.12f), 0.8f, 0f),
                (PropShape.Nugget, Vector3.one * 0.35f, 0.14f, 0.6f, new Color(0.9f, 0.35f, 0.2f), new Color(0.95f, 0.68f, 0.6f), 0.85f, 0.15f),
                (PropShape.Gem, Vector3.one * 0.25f, 0.12f, 0.45f, new Color(0.6f, 0.85f, 1f), Color.white, 0.7f, 0.7f),
                (PropShape.Wisp, Vector3.one * 0.4f, 1f, 0.8f, new Color(1f, 0.93f, 0.6f), Color.white, 0.9f, 1.6f),
                (PropShape.Chest, Vector3.one * 1.2f, 0f, 2.2f, Color.white, new Color(0.75f, 1f, 0.95f), 1f, 0.08f),
                (PropShape.Seal, new Vector3(13f, 16f, 13f), -4f, 30f, new Color(0.36f, 0.22f, 0.52f), new Color(0.82f, 0.62f, 1f), 0.9f, 0.8f),
            };
            for (int r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                int count = Mathf.Min(3, PropMeshes.VariantCount(row.shape));
                for (int v = 0; v < count; v++)
                {
                    var go = new GameObject(row.shape + "_" + v);
                    go.transform.SetParent(_stage.transform, false);
                    go.transform.localPosition = new Vector3((v - (count - 1) * 0.5f) * row.spacing, row.lift, r * 40f);
                    go.transform.localRotation = Quaternion.Euler(0f, 25f + v * 40f, 0f);
                    go.transform.localScale = row.scale;
                    go.AddComponent<MeshFilter>().sharedMesh = PropMeshes.Build(row.shape, v);
                    go.AddComponent<MeshRenderer>().sharedMaterial =
                        PlanetLook.SceneryMaterial(scenery, row.color, row.accent, row.accentStrength, 0f, 0f, row.glow);
                }
            }

            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.transform.SetParent(_stage.transform, false);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.78f, 0.94f);
            camera.fieldOfView = 40f;
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
            yield return null;
            for (int r = 0; r < rows.Length; r++)
            {
                float span = rows[r].spacing * 2f + rows[r].scale.x;
                float height = rows[r].shape == PropShape.Seal ? 12f : rows[r].scale.y;
                float back = Mathf.Max(span * 1.1f, height * 2.5f) + 0.6f;
                camera.transform.position = new Vector3(back * 0.2f, rows[r].lift + height * 0.9f + back * 0.25f, r * 40f - back);
                camera.transform.LookAt(new Vector3(0f, Mathf.Max(0f, rows[r].lift) + height * 0.4f, r * 40f));
                ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "prop-" + rows[r].shape + ".png"));
            }
        }
    }
}
