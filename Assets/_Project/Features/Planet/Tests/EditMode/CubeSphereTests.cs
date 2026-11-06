using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Planet.Tests
{
    public sealed class CubeSphereTests
    {
        private static IEnumerable<CubeFace> Faces()
        {
            for (int f = 0; f < CubeSphere.FaceCount; f++)
            {
                yield return (CubeFace)f;
            }
        }

        internal static float2 EdgeParams(CubeEdge edge, float along)
        {
            switch (edge)
            {
                case CubeEdge.UMin: return new float2(-1f, along);
                case CubeEdge.UMax: return new float2(1f, along);
                case CubeEdge.VMin: return new float2(along, -1f);
                default: return new float2(along, 1f);
            }
        }

        [Test]
        public void Face_axes_are_right_handed()
        {
            foreach (CubeFace f in Faces())
            {
                Assert.AreEqual((float3)CubeSphere.Normal(f), math.cross((float3)CubeSphere.AxisU(f), (float3)CubeSphere.AxisV(f)), f.ToString());
            }
        }

        [Test]
        public void Adjacency_is_symmetric_and_touches_four_distinct_side_faces()
        {
            foreach (CubeFace f in Faces())
            {
                var seen = new HashSet<CubeFace>();
                for (int e = 0; e < 4; e++)
                {
                    CubeEdgeLink link = CubeSphere.Adjacent(f, (CubeEdge)e);
                    CubeEdgeLink back = CubeSphere.Adjacent(link.Face, link.Edge);
                    Assert.AreEqual(f, back.Face, $"{f}/{(CubeEdge)e}");
                    Assert.AreEqual((CubeEdge)e, back.Edge, $"{f}/{(CubeEdge)e}");
                    Assert.AreEqual(link.Reversed, back.Reversed, $"{f}/{(CubeEdge)e}");
                    Assert.AreNotEqual(f, link.Face);
                    Assert.AreNotEqual(CubeSphere.Normal(f), -CubeSphere.Normal(link.Face), "opposite face is never adjacent");
                    seen.Add(link.Face);
                }

                Assert.AreEqual(4, seen.Count, f.ToString());
            }
        }

        [Test]
        public void Shared_edges_are_bit_identical()
        {
            // Dyadic grid at the finest level LOD can reach on a 1280 m planet with 32-quad leaves (2^6 · 32).
            const int samples = 2048;
            foreach (CubeFace f in Faces())
            {
                for (int e = 0; e < 4; e++)
                {
                    var edge = (CubeEdge)e;
                    CubeEdgeLink link = CubeSphere.Adjacent(f, edge);
                    for (int k = 0; k <= samples; k++)
                    {
                        float s = -1f + 2f * k / samples;
                        float2 a = EdgeParams(edge, s);
                        float2 b = EdgeParams(link.Edge, link.Reversed ? -s : s);
                        float3 da = CubeSphere.FaceToDirection(f, a.x, a.y);
                        float3 db = CubeSphere.FaceToDirection(link.Face, b.x, b.y);
                        // Bit equality on purpose: any 1-ulp difference is a visible crack in the mesh, and `==`
                        // would also accept +0 vs −0 (sol review F6).
                        Assert.IsTrue(math.all(math.asuint(da) == math.asuint(db)), $"{f}/{edge} s={s}: {da} vs {db}");
                    }
                }
            }
        }

        [Test]
        public void Warp_endpoints_and_symmetry_are_exact()
        {
            Assert.AreEqual(1f, CubeSphere.Warp(1f));
            Assert.AreEqual(-1f, CubeSphere.Warp(-1f));
            Assert.AreEqual(0f, CubeSphere.Warp(0f));
            for (int k = 1; k < 64; k++)
            {
                float s = k / 64f;
                Assert.AreEqual(-CubeSphere.Warp(s), CubeSphere.Warp(-s));
                Assert.Greater(CubeSphere.Warp(s), CubeSphere.Warp((k - 1) / 64f), "strictly increasing");
                Assert.AreEqual(s, CubeSphere.Unwarp(CubeSphere.Warp(s)), 1e-6f);
            }
        }

        [Test]
        public void Direction_face_round_trip()
        {
            var rng = new Pcg32(21UL, 4UL);
            for (int i = 0; i < 3000; i++)
            {
                float3 dir = PlanetFrameTests.RandomDirection(ref rng);
                CubeFace face = CubeSphere.DirectionToFace(dir, out float u, out float v);
                Assert.LessOrEqual(math.abs(u), 1f);
                Assert.LessOrEqual(math.abs(v), 1f);
                Assert.Less(math.distance(CubeSphere.FaceToDirection(face, u, v), dir), 2e-6f);
            }

            foreach (CubeFace f in Faces())
            {
                for (int i = 0; i < 50; i++)
                {
                    float u = rng.NextFloat() * 1.98f - 0.99f;
                    float v = rng.NextFloat() * 1.98f - 0.99f;
                    float3 dir = CubeSphere.FaceToDirection(f, u, v);
                    Assert.AreEqual(1f, math.length(dir), 1e-6f);
                    Assert.AreEqual(f, CubeSphere.DirectionToFace(dir, out float u2, out float v2));
                    Assert.AreEqual(u, u2, 1e-5f);
                    Assert.AreEqual(v, v2, 1e-5f);
                }
            }
        }

        [Test]
        public void Cell_area_spread_is_within_equi_angular_bound()
        {
            // REQ: planet-tech.md 「等面積修正映射」 — equi-angular theory gives max/min = √2 ≈ 1.414 (plain cube: 5.2).
            const int n = 32;
            double min = double.MaxValue;
            double max = 0.0;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    float u0 = -1f + 2f * i / n, u1 = -1f + 2f * (i + 1) / n;
                    float v0 = -1f + 2f * j / n, v1 = -1f + 2f * (j + 1) / n;
                    double3 a = CubeSphere.FaceToDirection(CubeFace.PosZ, u0, v0);
                    double3 b = CubeSphere.FaceToDirection(CubeFace.PosZ, u1, v0);
                    double3 c = CubeSphere.FaceToDirection(CubeFace.PosZ, u1, v1);
                    double3 d = CubeSphere.FaceToDirection(CubeFace.PosZ, u0, v1);
                    double area = SphericalTriangle(a, b, c) + SphericalTriangle(a, c, d);
                    min = math.min(min, area);
                    max = math.max(max, area);
                }
            }

            double ratio = max / min;
            TestContext.WriteLine($"cell area max/min = {ratio:F4}");
            Assert.Less(ratio, 1.45);
            Assert.Greater(ratio, 1.3, "sanity: the bound is tight, a much smaller ratio means the test is broken");
        }

        private static double SphericalTriangle(double3 a, double3 b, double3 c)
        {
            // Van Oosterom–Strackee solid angle.
            double num = math.abs(math.dot(a, math.cross(b, c)));
            double den = 1.0 + math.dot(a, b) + math.dot(b, c) + math.dot(c, a);
            return 2.0 * math.atan2(num, den);
        }

        [Test]
        public void Contract_violations_throw()
        {
            Assert.Throws<ContractException>(() => CubeSphere.Warp(1.01f));
            Assert.Throws<ContractException>(() => CubeSphere.Warp(float.NaN));
            Assert.Throws<ContractException>(() => CubeSphere.Unwarp(float.PositiveInfinity));
            Assert.Throws<ContractException>(() => CubeSphere.FaceToDirection((CubeFace)6, 0f, 0f));
            Assert.Throws<ContractException>(() => CubeSphere.FaceToDirection(CubeFace.PosX, 2f, 0f));
            Assert.Throws<ContractException>(() => CubeSphere.DirectionToFace(float3.zero, out _, out _));
            Assert.Throws<ContractException>(() => CubeSphere.Adjacent(CubeFace.PosX, (CubeEdge)4));
            Assert.Throws<ContractException>(() => CubeSphere.Normal((CubeFace)7));
        }
    }

    public sealed class ChunkIdTests
    {
        [Test]
        public void Key_round_trip_and_field_access()
        {
            var rng = new Pcg32(1UL, 77UL);
            for (int i = 0; i < 2000; i++)
            {
                int level = rng.NextInt(0, ChunkId.MaxLevel + 1);
                int n = 1 << level;
                var id = new ChunkId((CubeFace)rng.NextInt(0, 6), level, rng.NextInt(0, n), rng.NextInt(0, n));
                ChunkId back = ChunkId.FromKey(id.Key);
                Assert.AreEqual(id, back);
                Assert.AreEqual(id.Face, back.Face);
                Assert.AreEqual(level, back.Level);
                Assert.AreEqual(id.X, back.X);
                Assert.AreEqual(id.Y, back.Y);
            }

            var max = new ChunkId(CubeFace.NegZ, ChunkId.MaxLevel, (1 << ChunkId.MaxLevel) - 1, (1 << ChunkId.MaxLevel) - 1);
            Assert.AreEqual(CubeFace.NegZ, ChunkId.FromKey(max.Key).Face);
        }

        [Test]
        public void Parent_child_and_contains()
        {
            var id = new ChunkId(CubeFace.PosY, 3, 5, 2);
            for (int q = 0; q < 4; q++)
            {
                ChunkId c = id.Child(q);
                Assert.AreEqual(id, c.Parent());
                Assert.IsTrue(id.Contains(c));
                Assert.IsFalse(c.Contains(id));
            }

            Assert.IsTrue(id.Contains(id));
            Assert.IsFalse(id.Contains(new ChunkId(CubeFace.NegY, 4, 10, 4)));
            id.GetBounds(out float2 min, out float2 max);
            Assert.AreEqual(new float2(0.25f, -0.5f), min);
            Assert.AreEqual(new float2(0.5f, -0.25f), max);
        }

        [Test]
        public void Neighbors_are_symmetric_and_share_their_edge_geometrically()
        {
            for (int level = 0; level <= 4; level++)
            {
                int n = 1 << level;
                for (int f = 0; f < CubeSphere.FaceCount; f++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        for (int y = 0; y < n; y++)
                        {
                            var id = new ChunkId((CubeFace)f, level, x, y);
                            for (int e = 0; e < 4; e++)
                            {
                                var edge = (CubeEdge)e;
                                ChunkId nb = id.Neighbor(edge, out CubeEdge back);
                                Assert.AreEqual(level, nb.Level);
                                Assert.AreNotEqual(id, nb);
                                Assert.AreEqual(id, nb.Neighbor(back), $"{id} {edge} → {nb} {back}");
                                AssertSameEdge(id, edge, nb, back);
                            }
                        }
                    }
                }
            }
        }

        private static void AssertSameEdge(ChunkId a, CubeEdge ea, ChunkId b, CubeEdge eb)
        {
            EdgeEnds(a, ea, out float3 a0, out float3 a1);
            EdgeEnds(b, eb, out float3 b0, out float3 b1);
            bool same = math.distance(a0, b0) < 1e-6f && math.distance(a1, b1) < 1e-6f;
            bool swapped = math.distance(a0, b1) < 1e-6f && math.distance(a1, b0) < 1e-6f;
            Assert.IsTrue(same || swapped, $"{a}/{ea} and {b}/{eb} do not share an edge");
        }

        private static void EdgeEnds(ChunkId id, CubeEdge edge, out float3 p0, out float3 p1)
        {
            id.GetBounds(out float2 min, out float2 max);
            float2 q0, q1;
            switch (edge)
            {
                case CubeEdge.UMin: q0 = min; q1 = new float2(min.x, max.y); break;
                case CubeEdge.UMax: q0 = new float2(max.x, min.y); q1 = max; break;
                case CubeEdge.VMin: q0 = min; q1 = new float2(max.x, min.y); break;
                default: q0 = new float2(min.x, max.y); q1 = max; break;
            }

            p0 = CubeSphere.FaceToDirection(id.Face, q0.x, q0.y);
            p1 = CubeSphere.FaceToDirection(id.Face, q1.x, q1.y);
        }

        [Test]
        public void Children_on_edge_touch_that_edge()
        {
            var id = new ChunkId(CubeFace.NegX, 2, 1, 3);
            for (int e = 0; e < 4; e++)
            {
                var edge = (CubeEdge)e;
                id.ChildrenOnEdge(edge, out ChunkId c0, out ChunkId c1);
                Assert.AreEqual(id, c0.Parent());
                Assert.AreEqual(id, c1.Parent());
                Assert.AreNotEqual(id, c0.Neighbor(edge).Parent(), "c0 must sit on the edge");
                Assert.AreNotEqual(id, c1.Neighbor(edge).Parent(), "c1 must sit on the edge");
            }
        }

        [Test]
        public void Contract_violations_throw()
        {
            Assert.Throws<ContractException>(() => new ChunkId(CubeFace.PosX, ChunkId.MaxLevel + 1, 0, 0));
            Assert.Throws<ContractException>(() => new ChunkId(CubeFace.PosX, 2, 4, 0));
            Assert.Throws<ContractException>(() => new ChunkId(CubeFace.PosX, 2, 0, -1));
            Assert.Throws<ContractException>(() => new ChunkId((CubeFace)6, 0, 0, 0));
            Assert.Throws<ContractException>(() => ChunkId.Root(CubeFace.PosX).Parent());
            Assert.Throws<ContractException>(() => new ChunkId(CubeFace.PosX, ChunkId.MaxLevel, 0, 0).Child(0));
            Assert.Throws<ContractException>(() => ChunkId.Root(CubeFace.PosX).Child(4));
            Assert.Throws<ContractException>(() => ChunkId.Root(CubeFace.PosX).Neighbor((CubeEdge)9));
            Assert.Throws<ContractException>(() => ChunkId.FromKey(ulong.MaxValue));
        }
    }
}
