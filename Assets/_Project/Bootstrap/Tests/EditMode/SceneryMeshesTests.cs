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
        public void Same_arguments_build_the_same_mesh(SceneryArchetype archetype, int variant)
        {
            Mesh a = Build(archetype, variant);
            Mesh b = Build(archetype, variant);
            Assert.That(b.vertices, Is.EqualTo(a.vertices));
            Assert.That(b.normals, Is.EqualTo(a.normals));
            Assert.That(b.colors, Is.EqualTo(a.colors));
            Assert.That(b.triangles, Is.EqualTo(a.triangles));
        }

        [TestCaseSource(nameof(Every))]
        public void Mesh_is_well_formed_and_fills_the_shader_channels(SceneryArchetype archetype, int variant)
        {
            Mesh mesh = Build(archetype, variant);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Color[] colors = mesh.colors;
            var wind = new List<Vector2>();
            mesh.GetUVs(1, wind);
            Assert.That(vertices.Length, Is.GreaterThan(0).And.LessThan(65536), "fits a 16-bit index buffer");
            Assert.That(normals.Length, Is.EqualTo(vertices.Length));
            Assert.That(colors.Length, Is.EqualTo(vertices.Length));
            Assert.That(wind.Count, Is.EqualTo(vertices.Length));
            bool tree = archetype <= SceneryArchetype.Conifer;
            Assert.That(mesh.subMeshCount, Is.EqualTo(tree ? 2 : 1), "trees: wood + foliage");
            float maxSway = 0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                Assert.That(float.IsFinite(vertices[i].x + vertices[i].y + vertices[i].z), Is.True, $"vertex {i}");
                Assert.That(normals[i].magnitude, Is.EqualTo(1f).Within(1e-3f), $"normal {i}");
                Assert.That(colors[i].a, Is.InRange(0f, 1f), $"accent mask {i}");
                Assert.That(wind[i].x, Is.GreaterThanOrEqualTo(0f), $"sway weight {i}");
                Assert.That(wind[i].y, Is.InRange(0f, 1f), $"flutter phase {i}");
                maxSway = Mathf.Max(maxSway, wind[i].x);
            }

            Assert.That(maxSway > 0f, Is.EqualTo(tree), "only trees move in the wind");
        }

        [TestCaseSource(nameof(Every))]
        public void Mesh_is_unit_sized_and_reaches_below_the_ground(SceneryArchetype archetype, int variant)
        {
            // ScatterItem.BlockRadiusM / VolumeL assume a prop of size 1 is about 1 m: the mesh must not outgrow that.
            Mesh mesh = Build(archetype, variant);
            float minY = float.MaxValue, maxY = float.MinValue, reach = 0f;
            foreach (Vector3 v in mesh.vertices)
            {
                minY = Mathf.Min(minY, v.y);
                maxY = Mathf.Max(maxY, v.y);
                reach = Mathf.Max(reach, new Vector2(v.x, v.z).magnitude);
            }

            Assert.That(minY, Is.LessThan(0f), "a skirt below y = 0 hides the base on slopes");
            Assert.That(minY, Is.GreaterThan(-0.4f));
            Assert.That(maxY, Is.InRange(0.35f, 1.25f));
            Assert.That(reach, Is.InRange(0.15f, 0.95f));
        }

        [TestCaseSource(nameof(Every))]
        public void Faces_point_away_from_the_inside(SceneryArchetype archetype, int variant)
        {
            // A back-facing triangle is culled and leaves a hole. Every piece is convex-ish around its own centre, so
            // measured against the whole mesh's centre nearly all faces must still look outward.
            Mesh mesh = Build(archetype, variant);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int agree = 0, total = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] triangles = mesh.GetTriangles(s);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                    Vector3 face = Vector3.Cross(b - a, c - a);
                    if (face.sqrMagnitude < 1e-12f) continue;
                    total++;
                    if (Vector3.Dot(face, normals[triangles[t]]) > 0f) agree++;
                }
            }

            Assert.That(total, Is.GreaterThan(20));
            Assert.That(agree, Is.GreaterThanOrEqualTo(total * 0.98f), "winding agrees with the shading normal");
        }

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
