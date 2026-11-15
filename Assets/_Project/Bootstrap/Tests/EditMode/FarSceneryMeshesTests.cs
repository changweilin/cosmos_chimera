using System;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: vision.md#美術投資 第 5 項 (P1-15) — far silhouettes are cheap, closed toward the viewer and sized like the item.
    public sealed class FarSceneryMeshesTests
    {
        private static IEnumerable<FarShape> Shapes()
        {
            foreach (FarShape shape in Enum.GetValues(typeof(FarShape)))
            {
                if (shape != FarShape.None) yield return shape;
            }
        }

        private static void Append(FarShape shape, List<Vector3> v, List<Vector3> n, List<Color32> c, List<Vector3> a,
            List<int> t, Vector3 origin, Quaternion rotation, float size) =>
            FarSceneryMeshes.Append(shape, v, n, c, a, t, origin, rotation, Vector3.one * size, Color.green, Color.red, Color.blue);

        [TestCaseSource(nameof(Shapes))]
        public void A_shape_is_a_small_unit_sized_outward_facing_mesh(FarShape shape)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var c = new List<Color32>();
            var a = new List<Vector3>();
            var t = new List<int>();
            Append(shape, v, n, c, a, t, Vector3.zero, Quaternion.identity, 1f);
            Assert.That(v.Count, Is.EqualTo(FarSceneryMeshes.VertexCount(shape)));
            Assert.That(n.Count, Is.EqualTo(v.Count));
            Assert.That(c.Count, Is.EqualTo(v.Count));
            Assert.That(a.Count, Is.EqualTo(v.Count));
            Assert.That(t.Count % 3, Is.EqualTo(0));
            Assert.That(t.Count / 3, Is.InRange(5, 60), "a silhouette, not a model");
            float top = float.MinValue, bottom = float.MaxValue, reach = 0f;
            for (int i = 0; i < v.Count; i++)
            {
                top = Mathf.Max(top, v[i].y);
                bottom = Mathf.Min(bottom, v[i].y);
                reach = Mathf.Max(reach, new Vector2(v[i].x, v[i].z).magnitude);
                Assert.That(n[i].magnitude, Is.EqualTo(1f).Within(1e-3f));
                Assert.That(c[i].a, Is.EqualTo(255), "alpha 255 = no accent mask in ToonScenery");
            }

            Assert.That(bottom, Is.LessThanOrEqualTo(0.001f), "stands on (or in) the ground");
            Assert.That(top, Is.InRange(0.5f, 1.001f));
            Assert.That(reach, Is.InRange(0.05f, 0.55f));
            for (int k = 0; k < t.Count; k += 3)
            {
                Assert.That(t[k], Is.InRange(0, v.Count - 1));
                Vector3 p0 = v[t[k]], p1 = v[t[k + 1]], p2 = v[t[k + 2]];
                Vector3 face = Vector3.Cross(p1 - p0, p2 - p0);
                Assert.That(face.magnitude, Is.GreaterThan(1e-6f), "no degenerate triangle");
                // Unity's front face is clockwise: its normal must point away from the shape's own axis.
                Vector3 centre = (p0 + p1 + p2) / 3f;
                Vector3 outward = new Vector3(centre.x, 0f, centre.z);
                if (outward.magnitude > 0.02f)
                {
                    Assert.That(Vector3.Dot(face.normalized, outward.normalized), Is.GreaterThan(-0.2f), shape + " triangle " + k / 3);
                }
            }
        }

        [Test]
        public void Append_places_scales_and_colours_the_item()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var c = new List<Color32>();
            var a = new List<Vector3>();
            var t = new List<int>();
            var origin = new Vector3(100f, -20f, 7f);
            Append(FarShape.Boulder, v, n, c, a, t, Vector3.zero, Quaternion.identity, 1f);
            int first = v.Count;
            int firstTriangle = t.Count;
            Append(FarShape.Broadleaf, v, n, c, a, t, origin, Quaternion.Euler(0f, 0f, 90f), 6f);
            Assert.That(v.Count, Is.EqualTo(first + FarSceneryMeshes.VertexCount(FarShape.Broadleaf)));
            bool trunk = false, crown = false, sunlit = false;
            for (int i = first; i < v.Count; i++)
            {
                Assert.That(a[i], Is.EqualTo(origin));
                // Rotated 90° about Z: the tree's height now runs along −X.
                Vector3 local = v[i] - origin;
                Assert.That(local.x, Is.InRange(-6.01f, 0.01f));
                Assert.That(Mathf.Abs(local.y), Is.LessThan(6f * 0.35f));
                trunk |= c[i].r == 255 && c[i].g == 0;
                crown |= c[i].g > 0;
                sunlit |= c[i].b > 100;
            }

            Assert.That(trunk, Is.True, "trunk takes the accent colour");
            Assert.That(crown, Is.True, "crown takes the body colour");
            Assert.That(sunlit, Is.True, "upward faces shade toward the top colour");
            for (int k = firstTriangle; k < t.Count; k++)
            {
                Assert.That(t[k], Is.InRange(first, v.Count - 1), "indices are offset past the earlier item");
            }
        }

        [Test]
        public void Contract_violations_throw()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var c = new List<Color32>();
            var a = new List<Vector3>();
            var t = new List<int>();
            Assert.Throws<ContractException>(() => FarSceneryMeshes.VertexCount(FarShape.None));
            Assert.Throws<ContractException>(() => Append(FarShape.None, v, n, c, a, t, Vector3.zero, Quaternion.identity, 1f));
            Assert.Throws<ContractException>(() => Append(FarShape.Spire, v, n, c, a, null, Vector3.zero, Quaternion.identity, 1f));
            Assert.Throws<ContractException>(() => Append(FarShape.Spire, v, n, c, a, t, Vector3.zero, Quaternion.identity, 0f));
            n.Add(Vector3.up);
            Assert.Throws<ContractException>(() => Append(FarShape.Spire, v, n, c, a, t, Vector3.zero, Quaternion.identity, 1f));
        }
    }
}
