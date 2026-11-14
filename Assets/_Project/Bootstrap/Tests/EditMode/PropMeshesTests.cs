using System;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: vision.md#畫風與沉浸感; destructibles.md#破壞與掉落, #地上的素材; planet-world.md#地表地牢 — debris, drops,
    // chests and the arena seal are built in code, identically on every client.
    public sealed class PropMeshesTests
    {
        private readonly List<Mesh> _meshes = new List<Mesh>();

        private static IEnumerable<TestCaseData> Every()
        {
            foreach (PropShape shape in Enum.GetValues(typeof(PropShape)))
            {
                for (int v = 0; v < PropMeshes.VariantCount(shape); v++)
                {
                    yield return new TestCaseData(shape, v).SetName($"{shape}_{v}");
                }
            }
        }

        private static IEnumerable<TestCaseData> Loose()
        {
            foreach (PropShape shape in new[] { PropShape.Shard, PropShape.Nugget, PropShape.Gem, PropShape.Wisp })
            {
                for (int v = 0; v < PropMeshes.VariantCount(shape); v++)
                {
                    yield return new TestCaseData(shape, v).SetName($"{shape}_{v}");
                }
            }
        }

        private Mesh Build(PropShape shape, int variant)
        {
            Mesh mesh = PropMeshes.Build(shape, variant);
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
        public void Same_arguments_build_the_same_mesh(PropShape shape, int variant)
        {
            Mesh a = Build(shape, variant);
            Mesh b = Build(shape, variant);
            Assert.That(b.vertices, Is.EqualTo(a.vertices));
            Assert.That(b.normals, Is.EqualTo(a.normals));
            Assert.That(b.colors, Is.EqualTo(a.colors));
            Assert.That(b.triangles, Is.EqualTo(a.triangles));
        }

        [TestCaseSource(nameof(Every))]
        public void Mesh_is_well_formed_and_fills_the_shader_channels(PropShape shape, int variant)
        {
            Mesh mesh = Build(shape, variant);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Color[] colors = mesh.colors;
            var wind = new List<Vector2>();
            mesh.GetUVs(1, wind);
            Assert.That(vertices.Length, Is.GreaterThan(0).And.LessThan(65536), "fits a 16-bit index buffer");
            Assert.That(normals.Length, Is.EqualTo(vertices.Length));
            Assert.That(colors.Length, Is.EqualTo(vertices.Length));
            Assert.That(wind.Count, Is.EqualTo(vertices.Length));
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            Assert.That(mesh.triangles.Length, Is.GreaterThanOrEqualTo(20 * 3));
            for (int i = 0; i < vertices.Length; i++)
            {
                Assert.That(float.IsFinite(vertices[i].x + vertices[i].y + vertices[i].z), Is.True, $"vertex {i}");
                Assert.That(normals[i].magnitude, Is.EqualTo(1f).Within(1e-3f), $"normal {i}");
                Assert.That(colors[i].a, Is.InRange(0f, 1f), $"accent mask {i}");
                Assert.That(wind[i], Is.EqualTo(Vector2.zero), "nothing here sways: a chest must not lean in the wind");
            }
        }

        [TestCaseSource(nameof(Loose))]
        public void Loose_pieces_fill_the_unit_cube_their_collider_assumes(PropShape shape, int variant)
        {
            // ObjectPhysics gives each a unit BoxCollider scaled by the piece's edge; mass is edge³ × density.
            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            foreach (Vector3 v in Build(shape, variant).vertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }

            Vector3 size = max - min;
            Assert.That(Mathf.Max(size.x, size.y, size.z), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Mathf.Min(size.x, size.y, size.z), Is.GreaterThan(0.25f), "not a sliver");
            Assert.That(((min + max) * 0.5f).magnitude, Is.LessThan(1e-4f), "centred on the origin");
        }

        [TestCaseSource(nameof(Loose))]
        public void Faces_of_loose_pieces_point_away_from_the_centre(PropShape shape, int variant)
        {
            // Each is star-shaped around the origin, so a face looking inward is a culled hole.
            Mesh mesh = Build(shape, variant);
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                Vector3 face = Vector3.Cross(b - a, c - a);
                if (face.sqrMagnitude < 1e-12f) continue;
                Assert.That(Vector3.Dot(face.normalized, (a + b + c) / 3f), Is.GreaterThan(-1e-4f), $"triangle {t / 3}");
            }
        }

        [Test]
        public void Chest_stands_on_the_ground_and_is_about_one_unit_wide()
        {
            for (int v = 0; v < PropMeshes.VariantCount(PropShape.Chest); v++)
            {
                Bounds bounds = TightBounds(Build(PropShape.Chest, v));
                Assert.That(bounds.min.y, Is.InRange(-0.2f, -0.01f), "a skirt below y = 0 hides the base on slopes");
                Assert.That(bounds.max.y, Is.InRange(0.55f, 0.95f));
                Assert.That(bounds.size.x, Is.InRange(0.95f, 1.1f));
                Assert.That(bounds.size.z, Is.InRange(0.5f, 0.85f), "longer than deep");
            }
        }

        [Test]
        public void Seal_fills_the_cylinder_its_collider_blocks()
        {
            for (int v = 0; v < PropMeshes.VariantCount(PropShape.Seal); v++)
            {
                float reach = 0f, top = 0f, bottom = 0f;
                foreach (Vector3 p in Build(PropShape.Seal, v).vertices)
                {
                    reach = Mathf.Max(reach, new Vector2(p.x, p.z).magnitude);
                    top = Mathf.Max(top, p.y);
                    bottom = Mathf.Min(bottom, p.y);
                }

                // Past the rim a spire would look solid where the character can walk; far short of it, the reverse.
                Assert.That(reach, Is.InRange(0.85f, 1.1f));
                Assert.That(top, Is.EqualTo(1f).Within(0.02f), "as tall as the walls it plugs");
                Assert.That(bottom, Is.GreaterThan(-0.1f));
            }
        }

        [Test]
        public void Variants_of_a_shape_differ()
        {
            foreach (PropShape shape in Enum.GetValues(typeof(PropShape)))
            {
                if (PropMeshes.VariantCount(shape) < 2) continue;
                Assert.That(Build(shape, 1).vertices, Is.Not.EqualTo(Build(shape, 0).vertices), shape.ToString());
            }
        }

        [Test]
        public void Out_of_range_variant_violates_the_contract()
        {
            Assert.Throws<ContractException>(() => PropMeshes.Build(PropShape.Gem, -1));
            Assert.Throws<ContractException>(() => PropMeshes.Build(PropShape.Gem, PropMeshes.VariantCount(PropShape.Gem)));
            Assert.Throws<ArgumentOutOfRangeException>(() => PropMeshes.VariantCount((PropShape)99));
        }

        // mesh.bounds is padded for wind sway (SceneryMeshes.MeshData.ToMesh); measure the vertices themselves.
        private static Bounds TightBounds(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 v in vertices) bounds.Encapsulate(v);
            return bounds;
        }
    }
}
