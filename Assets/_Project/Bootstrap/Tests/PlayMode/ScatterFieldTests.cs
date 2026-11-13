using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class ScatterFieldTests
    {
        private readonly List<GameObject> _roots = new List<GameObject>();
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private ScatterField Field(float radius, float3 centre, float3 up, System.Func<float3, bool> isClear = null)
        {
            var root = new GameObject("scatter-test");
            _roots.Add(root);
            ScatterField field = root.AddComponent<ScatterField>();
            field.enabled = false;
            var config = new PlanetConfig(radius, centre);
            Shader shader = Shader.Find("Cosmimera/ToonOutlined");
            Assert.That(shader, Is.Not.Null);
            field.Configure(config, 12345, "verdant_planet", _ => 0f, null, () => config.ToWorld(up, 0f), shader, isClear);
            return field;
        }

        private static T Get<T>(ScatterField field, string name) => (T)typeof(ScatterField).GetField(name, Private).GetValue(field);

        private static void Call(ScatterField field, string method, object argument) =>
            typeof(ScatterField).GetMethod(method, Private).Invoke(field, new[] { argument });

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject root in _roots) Object.Destroy(root);
            _roots.Clear();
            yield return null;
        }

        [TestCase(320f)]
        [TestCase(640f)]
        [TestCase(1280f)]
        public void All_cell_centres_in_cap_and_legacy_sampled_cells_are_enumerated_at_every_seam_and_corner(float radius)
        {
            ScatterField field = Field(radius, float3.zero, new float3(1, 0, 0));
            int n = Get<int>(field, "_cellsPerFace");
            for (int f = 0; f < 6; f++)
            {
                for (int edge = 0; edge < 4; edge++)
                {
                    foreach (float along in new[] { -1f, -0.37f, 0f, 0.63f, 1f })
                    {
                        float u = edge < 2 ? (edge == 0 ? -1f : 1f) : along;
                        float v = edge < 2 ? along : (edge == 2 ? -1f : 1f);
                        float3 up = CubeSphere.FaceToDirection((CubeFace)f, u, v);
                        Call(field, "CollectCells", up);
                        HashSet<long> wanted = Get<HashSet<long>>(field, "_wanted");
                        // Exhaustive six-face oracle is independent of the production projection/range bounds.
                        double limit = System.Math.Cos(90.0 / radius);
                        for (int g = 0; g < 6; g++)
                        {
                            for (int j = 0; j < n; j++)
                            {
                                for (int i = 0; i < n; i++)
                                {
                                    float3 d = CubeSphere.FaceToDirection((CubeFace)g, -1f + 2f * (i + 0.5f) / n,
                                        -1f + 2f * (j + 0.5f) / n);
                                    if (math.dot(up, d) >= limit)
                                        Assert.That(wanted.Contains(((long)g * n + j) * n + i), Is.True,
                                            $"R={radius} face={f} edge={edge} along={along}: missing {g}/{i}/{j}");
                                }
                            }
                        }

                        foreach (long key in LegacyKeys(up, radius, n))
                            Assert.That(wanted.Contains(key), Is.True, "previously visible cell must stay visible");
                        Assert.That(wanted.Count, Is.LessThan(6 * n * n), "must remain a local stream");
                    }
                }
            }
        }

        [Test]
        public void Blocked_ground_only_removes_items_and_leaves_the_rest_unchanged()
        {
            float3 up = math.normalize(new float3(0f, 1f, 0.2f));
            ScatterField open = Field(640f, float3.zero, up);
            ScatterField blocked = Field(640f, float3.zero, up, d => d.x > 0f);
            float3 focus = new PlanetConfig(640f, float3.zero).ToWorld(up, 0f);
            Call(open, "Refresh", focus);
            Call(blocked, "Refresh", focus);
            SortedDictionary<long, string> all = Snapshot(open);
            SortedDictionary<long, string> kept = Snapshot(blocked);
            int removed = 0;
            int survived = 0;
            foreach (var pair in Get<Dictionary<long, List<GameObject>>>(open, "_live"))
            {
                foreach (GameObject root in pair.Value)
                {
                    if (root.transform.position.x > 0f) survived++;
                    else removed++;
                }
            }

            Assert.That(removed, Is.GreaterThan(0), "fixture must straddle the blocked half");
            Assert.That(survived, Is.GreaterThan(0), "fixture must straddle the blocked half");
            Assert.That(blocked.LiveCount, Is.EqualTo(survived));
            foreach (var pair in kept)
            {
                if (pair.Value.Length > 0) Assert.That(all[pair.Key], Is.EqualTo(pair.Value), $"cell {pair.Key}");
            }
        }

        [UnityTest]
        public IEnumerator A_removed_item_is_gone_and_never_streams_back()
        {
            float3 up = math.normalize(new float3(0f, 1f, 0.2f));
            var config = new PlanetConfig(640f, float3.zero);
            var planner = new Cosmimera.WorldGen.ScatterPlanner(config, 12345, "verdant_planet", _ => 0f, null, null);
            var destroyed = new HashSet<long>();
            var root = new GameObject("scatter-remove-test");
            _roots.Add(root);
            ScatterField field = root.AddComponent<ScatterField>();
            field.enabled = false;
            field.Configure(config, planner, "verdant_planet", () => config.ToWorld(up, 0f),
                Shader.Find("Cosmimera/ToonOutlined"), destroyed.Contains);
            Call(field, "Refresh", config.ToWorld(up, 0f));
            yield return null;
            long key = -1;
            foreach (var pair in Get<Dictionary<long, List<GameObject>>>(field, "_live"))
            {
                if (pair.Value.Count > 0) { key = pair.Key; break; }
            }

            Assert.That(key, Is.GreaterThanOrEqualTo(0));
            GameObject item = Get<Dictionary<long, List<GameObject>>>(field, "_live")[key][0];
            Assert.That(item.GetComponentsInChildren<Collider>().Length, Is.EqualTo(1), "only the trunk/body collides");
            int before = field.LiveCount;
            destroyed.Add(key);
            field.Remove(key);
            yield return null;
            Assert.That(item == null, Is.True);
            Assert.That(field.LiveCount, Is.EqualTo(before - 1));
            Call(field, "Refresh", config.ToWorld(-up, 0f));
            Call(field, "Refresh", config.ToWorld(up, 0f));
            Assert.That(field.LiveCount, Is.EqualTo(before - 1), "a destroyed item never streams back");
        }

        [UnityTest]
        public IEnumerator Scenery_shader_draws_wind_ready_meshes_and_keeps_one_collider_per_item()
        {
            float3 up = math.normalize(new float3(0f, 1f, 0.2f));
            var config = new PlanetConfig(640f, float3.zero);
            var root = new GameObject("scatter-scenery-test");
            _roots.Add(root);
            ScatterField field = root.AddComponent<ScatterField>();
            field.enabled = false;
            Shader scenery = Shader.Find("Cosmimera/ToonScenery");
            Assert.That(scenery, Is.Not.Null);
            Assert.That(scenery.isSupported, Is.True, "ToonScenery must compile");
            field.Configure(config, 12345, "verdant_planet", _ => 0f, null, () => config.ToWorld(up, 0f),
                Shader.Find("Cosmimera/ToonOutlined"), null, scenery);
            Call(field, "Refresh", config.ToWorld(up, 0f));
            yield return null;
            int trees = 0, rocks = 0;
            foreach (var pair in Get<Dictionary<long, List<GameObject>>>(field, "_live"))
            {
                foreach (GameObject item in pair.Value)
                {
                    Assert.That(item.GetComponentsInChildren<Collider>().Length, Is.EqualTo(1), "only the trunk/body collides");
                    MeshRenderer renderer = item.GetComponentInChildren<MeshRenderer>();
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(mesh.subMeshCount), "one material per sub-mesh");
                    foreach (Material m in renderer.sharedMaterials) Assert.That(m.shader, Is.SameAs(scenery));
                    bool tree = mesh.subMeshCount == 2;
                    if (tree) trees++; else rocks++;
                    Assert.That(renderer.sharedMaterials[0].GetFloat("_Sway") > 0f, Is.EqualTo(tree), "trees sway, rocks do not");
                }
            }

            Assert.That(trees, Is.GreaterThan(0));
            Assert.That(rocks, Is.GreaterThan(0));
            Assert.That(Get<Dictionary<int, Mesh>>(field, "_meshes").Count, Is.LessThan(trees + rocks), "meshes are shared");
        }

        [UnityTest]
        public IEnumerator Capture_scenery_lineup_when_requested()
        {
            if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") != "1")
            {
                Assert.Ignore("visual proof only: set COSMIMERA_CAPTURE=1 and run without -nographics");
            }

            var stage = new GameObject("scenery-lineup");
            _roots.Add(stage);
            Shader scenery = Shader.Find("Cosmimera/ToonScenery");
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(stage.transform, false);
            ground.transform.localPosition = new Vector3(0f, 0f, 80f);
            ground.transform.localScale = Vector3.one * 40f;
            ground.GetComponent<Renderer>().sharedMaterial =
                PlanetLook.SceneryMaterial(scenery, new Color(0.43f, 0.69f, 0.29f), Color.white, 0f, 0f, 0f, 0f);
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.transform.SetParent(stage.transform, false);
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            RenderSettings.ambientLight = new Color(0.55f, 0.78f, 0.94f) * 0.6f;

            (SceneryArchetype archetype, float size, Color color, Color accent, Color top, float glow)[] rows =
            {
                (SceneryArchetype.Broadleaf, 6f, Hex(0x3F7F36), Hex(0x6B4A2B), Hex(0x9CBF4A), 0f),
                (SceneryArchetype.Conifer, 6f, Hex(0x3F7F36), Hex(0x6B4A2B), Hex(0x9CBF4A), 0f),
                (SceneryArchetype.Umbrella, 6f, Hex(0x8A6AC8), Hex(0x3E3050), Hex(0xD0A8F0), 0f),
                (SceneryArchetype.Boulder, 2.2f, Hex(0x6E6A62), Hex(0x6E6A62), Hex(0x5E8A3A), 0f),
                (SceneryArchetype.CrystalCluster, 3.5f, Hex(0xFF8A1E), Hex(0xFF8A1E), Hex(0xFFE08A), 0.9f),
            };
            for (int r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                bool tree = row.archetype <= SceneryArchetype.Conifer;
                for (int v = 0; v < 3; v++)
                {
                    var go = new GameObject(row.archetype + "_" + v);
                    go.transform.SetParent(stage.transform, false);
                    go.transform.localPosition = new Vector3((v - 1) * 8f, 0f, r * 40f);
                    go.transform.localScale = Vector3.one * row.size;
                    go.AddComponent<MeshFilter>().sharedMesh = SceneryMeshes.Build(row.archetype, v);
                    go.AddComponent<MeshRenderer>().sharedMaterials = tree
                        ? new[]
                        {
                            PlanetLook.SceneryMaterial(scenery, row.accent, row.accent * 0.72f, 0.8f, 0.06f, 0f, 0f),
                            PlanetLook.SceneryMaterial(scenery, row.color, row.top, 0.85f, 0.06f, 0.55f, 0f),
                        }
                        : new[] { PlanetLook.SceneryMaterial(scenery, row.color, row.top, 0.8f, 0f, 0f, row.glow) };
                }
            }

            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.transform.SetParent(stage.transform, false);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.78f, 0.94f);
            camera.fieldOfView = 40f;
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
            yield return null;
            for (int r = 0; r < rows.Length; r++)
            {
                float back = rows[r].size * 3.2f + 6f;
                camera.transform.position = new Vector3(2f, rows[r].size * 0.9f, r * 40f - back);
                camera.transform.LookAt(new Vector3(0f, rows[r].size * 0.45f, r * 40f));
                Shoot(camera, System.IO.Path.Combine(dir, "scenery-" + rows[r].archetype + ".png"));
            }
        }

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        private static void Shoot(Camera camera, string path)
        {
            var rt = new RenderTexture(1280, 720, 24);
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                texture.Apply();
                System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.Destroy(texture);
                Object.Destroy(rt);
            }
        }

        private static HashSet<long> LegacyKeys(float3 up, float radius, int n)
        {
            var keys = new HashSet<long>();
            PlanetFrame frame = PlanetFrame.FromUp(up);
            for (int a = -18; a <= 18; a++)
            {
                for (int b = -18; b <= 18; b++)
                {
                    if ((a * a + b * b) * 25 > 90 * 90) continue;
                    float3 d = math.normalize(up * radius + frame.East * (a * 5f) + frame.North * (b * 5f));
                    CubeFace face = CubeSphere.DirectionToFace(d, out float u, out float v);
                    int i = math.clamp((int)((u + 1f) * 0.5f * n), 0, n - 1);
                    int j = math.clamp((int)((v + 1f) * 0.5f * n), 0, n - 1);
                    keys.Add(((long)(int)face * n + j) * n + i);
                }
            }

            return keys;
        }

        [UnityTest]
        public IEnumerator Same_seed_repeats_objects_after_streaming_and_preserves_legacy_cell_results()
        {
            float3 up = math.normalize(new float3(1, -1, 1));
            var config = new PlanetConfig(640f, new float3(23, -57, 109));
            ScatterField first = Field(config.Radius, config.Center, up);
            ScatterField second = Field(config.Radius, config.Center, up);
            Call(first, "Refresh", config.ToWorld(up, 0f));
            // Build only the formerly found keys on a separate instance, in reverse order. Enumeration order
            // must not perturb per-cell RNG, and added cells must not alter any previously visible object.
            int n = Get<int>(first, "_cellsPerFace");
            var legacy = new List<long>(LegacyKeys(up, config.Radius, n));
            legacy.Sort((a, b) => b.CompareTo(a));
            foreach (long key in legacy) Call(second, "BuildCell", key);
            AssertSame(Get<Dictionary<long, List<GameObject>>>(second, "_live"), Get<Dictionary<long, List<GameObject>>>(first, "_live"));
            Call(second, "Refresh", config.ToWorld(up, 0f));
            Assert.That(first.LiveCount, Is.GreaterThan(0));
            var before = Snapshot(first);
            Assert.That(Snapshot(second), Is.EqualTo(before));
            Call(second, "Refresh", config.ToWorld(-up, 0f));
            yield return null;
            Call(second, "Refresh", config.ToWorld(up, 0f));
            Assert.That(Snapshot(second), Is.EqualTo(before));
        }

        private static void AssertSame(Dictionary<long, List<GameObject>> expected, Dictionary<long, List<GameObject>> actual)
        {
            foreach (var pair in expected)
            {
                Assert.That(actual.ContainsKey(pair.Key), Is.True);
                Assert.That(Describe(actual[pair.Key]), Is.EqualTo(Describe(pair.Value)), $"cell {pair.Key}");
            }
        }

        private static SortedDictionary<long, string> Snapshot(ScatterField field)
        {
            var result = new SortedDictionary<long, string>();
            foreach (var pair in Get<Dictionary<long, List<GameObject>>>(field, "_live")) result[pair.Key] = Describe(pair.Value);
            return result;
        }

        private static string Describe(List<GameObject> roots)
        {
            var values = new List<string>();
            foreach (GameObject root in roots)
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>())
                {
                    values.Add(t.name + t.localPosition.ToString("R") + t.localRotation.ToString("R") + t.localScale.ToString("R"));
                    if (t.TryGetComponent(out Renderer renderer)) values.Add(renderer.sharedMaterial.GetColor("_BaseColor").ToString("R"));
                }
            }

            return string.Join(";", values);
        }
    }
}
