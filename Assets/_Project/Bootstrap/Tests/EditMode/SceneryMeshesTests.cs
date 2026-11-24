using System;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: vision.md#畫風與沉浸感; planet-world.md (7. 散佈) — props are built in code, identically on every client.
    public sealed class SceneryMeshesTests
    {
        private readonly List<Mesh> _meshes = new List<Mesh>();

        private static IEnumerable<TestCaseData> Every()
        {
            foreach (SceneryArchetype archetype in Enum.GetValues(typeof(SceneryArchetype)))
            {
                for (int v = 0; v < SceneryMeshes.VariantCount(archetype); v++)
                {
                    yield return new TestCaseData(archetype, v).SetName($"{archetype}_{v}");
                }
            }
        }

        private Mesh Build(SceneryArchetype archetype, int variant)
        {
            Mesh mesh = SceneryMeshes.Build(archetype, variant);
            _meshes.Add(mesh);
            return mesh;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Mesh mesh in _meshes) UnityEngine.Object.DestroyImmediate(mesh);
            _meshes.Clear();
        }

        [TestCaseSource(nameof(Every))]
        public void Every_variant_builds_deterministically_and_keeps_every_mesh_contract(SceneryArchetype archetype, int variant)
        {
            Mesh a = Build(archetype, variant);
            Mesh b = Build(archetype, variant);
            Vector3[] vertices = a.vertices;
            Vector3[] normals = a.normals;
            Color[] colors = a.colors;
            Assert.That(b.vertices, Is.EqualTo(vertices));
            Assert.That(b.normals, Is.EqualTo(normals));
            Assert.That(b.colors, Is.EqualTo(colors));
            Assert.That(b.triangles, Is.EqualTo(a.triangles));

            var wind = new List<Vector2>();
            a.GetUVs(1, wind);
            Assert.That(vertices.Length, Is.GreaterThan(0).And.LessThan(65536), "fits a 16-bit index buffer");
            Assert.That(normals.Length, Is.EqualTo(vertices.Length));
            Assert.That(colors.Length, Is.EqualTo(vertices.Length));
            Assert.That(wind.Count, Is.EqualTo(vertices.Length));
            bool plant = Sways(archetype);
            int subMeshCount = a.subMeshCount;
            Assert.That(subMeshCount, Is.EqualTo(SubMeshes(archetype)), "see SceneryMeshes.Build");
            float maxSway = 0f;
            float minY = float.MaxValue, maxY = float.MinValue, reach = 0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                Assert.That(float.IsFinite(v.x + v.y + v.z), Is.True, $"vertex {i}");
                Assert.That(normals[i].magnitude, Is.EqualTo(1f).Within(1e-3f), $"normal {i}");
                Assert.That(colors[i].a, Is.InRange(0f, 1f), $"accent mask {i}");
                Assert.That(wind[i].x, Is.GreaterThanOrEqualTo(0f), $"sway weight {i}");
                Assert.That(wind[i].y, Is.InRange(0f, 1f), $"flutter phase {i}");
                maxSway = Mathf.Max(maxSway, wind[i].x);
                minY = Mathf.Min(minY, v.y);
                maxY = Mathf.Max(maxY, v.y);
                reach = Mathf.Max(reach, new Vector2(v.x, v.z).magnitude);
            }

            Assert.That(maxSway > 0f, Is.EqualTo(plant), "only plants move in the wind");

            // ScatterItem.BlockRadiusM / VolumeL assume a prop of size 1 is about 1 m: the mesh must not outgrow that.
            Assert.That(minY, Is.LessThan(0f), "a skirt below y = 0 hides the base on slopes");
            Assert.That(minY, Is.GreaterThan(-0.4f));
            Assert.That(maxY, Is.InRange(0.35f, 1.25f));
            Assert.That(reach, Is.InRange(0.15f, 0.95f));

            // A back-facing triangle is culled and leaves a hole. Every piece is convex-ish around its own centre, so
            // measured against the whole mesh's centre nearly all faces must still look outward.
            int agree = 0, total = 0;
            for (int s = 0; s < subMeshCount; s++)
            {
                int[] triangles = a.GetTriangles(s);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Vector3 v0 = vertices[triangles[t]], v1 = vertices[triangles[t + 1]], v2 = vertices[triangles[t + 2]];
                    Vector3 face = Vector3.Cross(v1 - v0, v2 - v0);
                    if (face.sqrMagnitude < 1e-12f)
                    {
                        continue;
                    }

                    total++;
                    if (Vector3.Dot(face, normals[triangles[t]]) > 0f)
                    {
                        agree++;
                    }
                }
            }

            Assert.That(total, Is.GreaterThan(20));
            Assert.That(agree, Is.GreaterThanOrEqualTo(total * 0.98f), "winding agrees with the shading normal");
        }

        // Rocks, crystals and stiff-bodied plants (log, cactus, mushroom) stand still in the wind.
        private static bool Sways(SceneryArchetype archetype) => archetype switch
        {
            SceneryArchetype.Boulder or SceneryArchetype.CrystalCluster or SceneryArchetype.Log or SceneryArchetype.Cactus or
                SceneryArchetype.Mushroom or SceneryArchetype.Spire or SceneryArchetype.Columns or SceneryArchetype.Outcrop or
                SceneryArchetype.Tor or SceneryArchetype.Slabs or SceneryArchetype.Ledge or SceneryArchetype.Cobbles or
                SceneryArchetype.Fluted or SceneryArchetype.Cushion => false,
            _ => true,
        };

        private static int SubMeshes(SceneryArchetype archetype) => archetype switch
        {
            SceneryArchetype.Boulder or SceneryArchetype.CrystalCluster or SceneryArchetype.Snag or SceneryArchetype.Log or
                SceneryArchetype.Spire or SceneryArchetype.Columns or SceneryArchetype.Fern or SceneryArchetype.Tor or
                SceneryArchetype.Slabs or SceneryArchetype.Ledge or SceneryArchetype.Cobbles or SceneryArchetype.Fluted => 1,
            _ => 2,
        };

        [Test]
        public void Variants_of_an_archetype_differ()
        {
            foreach (SceneryArchetype archetype in Enum.GetValues(typeof(SceneryArchetype)))
            {
                Mesh a = Build(archetype, 0);
                Mesh b = Build(archetype, 1);
                Assert.That(b.vertices, Is.Not.EqualTo(a.vertices), archetype.ToString());
            }
        }

        [Test]
        public void Out_of_range_variant_violates_the_contract()
        {
            Assert.Throws<ContractException>(() => SceneryMeshes.Build(SceneryArchetype.Boulder, -1));
            Assert.Throws<ContractException>(() =>
                SceneryMeshes.Build(SceneryArchetype.Boulder, SceneryMeshes.VariantCount(SceneryArchetype.Boulder)));
        }
    }
}
