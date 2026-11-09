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

        private ScatterField Field(float radius, float3 centre, float3 up)
        {
            var root = new GameObject("scatter-test");
            _roots.Add(root);
            ScatterField field = root.AddComponent<ScatterField>();
            field.enabled = false;
            var config = new PlanetConfig(radius, centre);
            Shader shader = Shader.Find("Cosmimera/ToonOutlined");
            Assert.That(shader, Is.Not.Null);
            field.Configure(config, 12345, "verdant_planet", _ => 0f, null, () => config.ToWorld(up, 0f), shader);
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
