using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet.Model;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Planet.Tests
{
    public sealed class ChunkMeshTests
    {
        private const int Quads = 32;
        private static readonly PlanetConfig Planet640 = new PlanetConfig(640f, float3.zero);
        private static readonly PlanetHeight Height = PlanetHeight.ForPlanet(Planet640, 42UL);

        [BurstCompile(CompileSynchronously = true)]
        private struct VerticesJob : IJob
        {
            public ChunkGeometry Geometry;
            public NativeArray<ChunkVertex> Output;
            public NativeArray<int> RanManaged;

            public void Execute()
            {
                Geometry.Build(Output, out _, out _);
                MarkManaged();
            }

            [BurstDiscard]
            private void MarkManaged() => RanManaged[0] = 1;
        }

        // Seam probe from the CubeSphere FENCE: every face edge at 2049 dyadic parameters, both sides, in Burst.
        [BurstCompile(CompileSynchronously = true)]
        private struct SeamJob : IJob
        {
            [ReadOnly] public NativeArray<int4> Links; // face, edge, linkFace, linkEdge | reversed in w bit 8
            public NativeArray<int> Mismatches;
            public NativeArray<int> RanManaged;

            public void Execute()
            {
                int bad = 0;
                for (int l = 0; l < Links.Length; l++)
                {
                    int4 link = Links[l];
                    bool reversed = (link.w & 0x100) != 0;
                    int otherEdge = link.w & 0xFF;
                    for (int k = 0; k <= 2048; k++)
                    {
                        float s = -1f + k / 1024f;
                        float2 a = EdgeParams(link.y, s);
                        float2 b = EdgeParams(otherEdge, reversed ? -s : s);
                        float3 da = CubeSphere.FaceToDirectionCore(link.x, a.x, a.y);
                        float3 db = CubeSphere.FaceToDirectionCore(link.z, b.x, b.y);
                        if (!math.all(math.asuint(da) == math.asuint(db)))
                        {
                            bad++;
                        }
                    }
                }

                Mismatches[0] = bad;
                MarkManaged();
            }

            private static float2 EdgeParams(int edge, float along)
            {
                switch (edge)
                {
                    case 0: return new float2(-1f, along);
                    case 1: return new float2(1f, along);
                    case 2: return new float2(along, -1f);
                    default: return new float2(along, 1f);
                }
            }

            [BurstDiscard]
            private void MarkManaged() => RanManaged[0] = 1;
        }

        private static ChunkVertex[] BuildManaged(ChunkId id)
        {
            var g = ChunkGeometry.Create(id, Planet640, Height, Quads);
            using (var v = new NativeArray<ChunkVertex>((Quads + 1) * (Quads + 1), Allocator.Temp))
            {
                g.Build(v, out _, out _);
                return v.ToArray();
            }
        }

        private static ChunkVertex[] BuildBurst(ChunkId id)
        {
            Assume.That(BurstCompiler.IsEnabled, "Burst is disabled in this Editor; the Burst path cannot be verified");
            var output = new NativeArray<ChunkVertex>((Quads + 1) * (Quads + 1), Allocator.TempJob);
            var flag = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                new VerticesJob { Geometry = ChunkGeometry.Create(id, Planet640, Height, Quads), Output = output, RanManaged = flag }
                    .Schedule().Complete();
                Assert.AreEqual(0, flag[0], "VerticesJob ran as managed code, not Burst");
                return output.ToArray();
            }
            finally
            {
                output.Dispose();
                flag.Dispose();
            }
        }

        private static uint3 Bits(float3 p) => math.asuint(p);

        private static HashSet<uint3> EdgeVertexBits(ChunkVertex[] v, CubeEdge edge, int stride)
        {
            var set = new HashSet<uint3>();
            for (int k = 0; k <= Quads; k += stride)
            {
                int i, j;
                switch (edge)
                {
                    case CubeEdge.UMin: i = 0; j = k; break;
                    case CubeEdge.UMax: i = Quads; j = k; break;
                    case CubeEdge.VMin: i = k; j = 0; break;
                    default: i = k; j = Quads; break;
                }

                set.Add(Bits(v[j * (Quads + 1) + i].Position));
            }

            return set;
        }

        private static IEnumerable<ChunkId> BorderChunks(int level)
        {
            int n = 1 << level;
            for (int f = 0; f < CubeSphere.FaceCount; f++)
            {
                for (int k = 0; k < n; k++)
                {
                    yield return new ChunkId((CubeFace)f, level, 0, k);
                    yield return new ChunkId((CubeFace)f, level, n - 1, k);
                    yield return new ChunkId((CubeFace)f, level, k, 0);
                    yield return new ChunkId((CubeFace)f, level, k, n - 1);
                }
            }
        }

        private static void AssertSeams(System.Func<ChunkId, ChunkVertex[]> build, int level)
        {
            var cache = new Dictionary<ChunkId, ChunkVertex[]>();
            ChunkVertex[] Get(ChunkId id)
            {
                if (!cache.TryGetValue(id, out ChunkVertex[] v))
                {
                    v = build(id);
                    cache.Add(id, v);
                }

                return v;
            }

            foreach (ChunkId id in BorderChunks(level))
            {
                for (int e = 0; e < 4; e++)
                {
                    var edge = (CubeEdge)e;
                    ChunkId other = id.Neighbor(edge, out CubeEdge back);
                    // Same level: the whole edge matches bit for bit (crosses cube faces for border chunks).
                    CollectionAssert.AreEquivalent(EdgeVertexBits(Get(id), edge, 1), EdgeVertexBits(Get(other), back, 1),
                        $"{id} {edge} ↔ {other}");

                    // One level finer on this side (2:1 case): its even edge vertices lie on the coarse edge exactly.
                    id.ChildrenOnEdge(edge, out ChunkId c0, out ChunkId c1);
                    var coarse = EdgeVertexBits(Get(other), back, 1);
                    foreach (ChunkId child in new[] { c0, c1 })
                    {
                        Assert.IsTrue(coarse.IsSupersetOf(EdgeVertexBits(Get(child), edge, 2)), $"{child} {edge} vs coarse {other}");
                    }
                }
            }
        }

        [Test]
        public void Shared_vertices_are_bit_identical_across_faces_and_levels_managed()
        {
            AssertSeams(BuildManaged, 2);
        }

        [Test]
        public void Shared_vertices_are_bit_identical_across_faces_and_levels_burst()
        {
            AssertSeams(BuildBurst, 2);
        }

        [Test]
        public void Face_seams_are_bit_identical_in_burst()
        {
            Assume.That(BurstCompiler.IsEnabled, "Burst is disabled in this Editor");
            var links = new NativeArray<int4>(CubeSphere.FaceCount * 4, Allocator.TempJob);
            var bad = new NativeArray<int>(1, Allocator.TempJob);
            var flag = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                for (int f = 0; f < CubeSphere.FaceCount; f++)
                {
                    for (int e = 0; e < 4; e++)
                    {
                        CubeEdgeLink l = CubeSphere.Adjacent((CubeFace)f, (CubeEdge)e);
                        links[f * 4 + e] = new int4(f, e, (int)l.Face, (int)l.Edge | (l.Reversed ? 0x100 : 0));
                    }
                }

                new SeamJob { Links = links, Mismatches = bad, RanManaged = flag }.Schedule().Complete();
                Assert.AreEqual(0, flag[0], "SeamJob ran as managed code, not Burst");
                Assert.AreEqual(0, bad[0]);
            }
            finally
            {
                links.Dispose();
                bad.Dispose();
                flag.Dispose();
            }
        }

        [Test]
        public void Burst_and_managed_vertices_agree_to_sub_millimetre()
        {
            var id = new ChunkId(CubeFace.PosY, 3, 5, 2);
            ChunkVertex[] m = BuildManaged(id);
            ChunkVertex[] b = BuildBurst(id);
            float maxPos = 0f;
            float maxNormal = 0f;
            for (int k = 0; k < m.Length; k++)
            {
                maxPos = math.max(maxPos, math.distance(m[k].Position, b[k].Position));
                maxNormal = math.max(maxNormal, math.distance(m[k].Normal, b[k].Normal));
            }

            // ADR-0006: bit-equality across backends is not required, only within one backend.
            // Measured 2026-11-06: 0.53 mm (Burst may contract a*b+c into FMA inside snoise).
            Assert.Less(maxPos, 1e-3f);
            // Normals are central differences over a 0.5 m baseline, which amplifies that ~1000× (≈0.1° at 2e-3).
            Assert.Less(maxNormal, 1e-2f);
        }

        [Test]
        public void FaceToDirectionCore_matches_the_axis_tables()
        {
            for (int f = 0; f < CubeSphere.FaceCount; f++)
            {
                var face = (CubeFace)f;
                for (int k = 0; k <= 16; k++)
                {
                    float u = -1f + k / 8f;
                    float v = 1f - k / 16f;
                    float3 expected = math.normalize((float3)CubeSphere.Normal(face)
                        + (float3)CubeSphere.AxisU(face) * CubeSphere.Warp(u) + (float3)CubeSphere.AxisV(face) * CubeSphere.Warp(v));
                    Assert.AreEqual(Bits(expected), Bits(CubeSphere.FaceToDirection(face, u, v)), $"{face} {u},{v}");
                }
            }
        }

        [Test]
        public void Vertices_lie_on_the_displaced_surface_with_outward_normals_and_front_faces()
        {
            var id = new ChunkId(CubeFace.NegZ, 4, 3, 11);
            ChunkVertex[] v = BuildManaged(id);
            foreach (ChunkVertex vert in v)
            {
                float3 dir = math.normalize(vert.Position);
                Assert.AreEqual(Planet640.Radius + Height.Sample(dir), math.length(vert.Position), 1e-3f);
                Assert.AreEqual(1f, math.length(vert.Normal), 1e-4f);
                Assert.Greater(math.dot(vert.Normal, dir), 0.5f);
            }

            ushort[] idx = new ChunkStitching(Quads).Indices(0b1011);
            for (int t = 0; t < idx.Length; t += 3)
            {
                float3 a = v[idx[t]].Position;
                float3 n = math.cross(v[idx[t + 1]].Position - a, v[idx[t + 2]].Position - a);
                Assert.Greater(math.dot(n, math.normalize(a)), 0f, $"tri {t / 3} faces inward");
            }
        }

        [Test]
        public void Mesh_job_fills_mesh_data_with_vertices_indices_and_bounds()
        {
            var ids = new[] { new ChunkId(CubeFace.PosX, 5, 16, 16), new ChunkId(CubeFace.NegY, 2, 0, 3) };
            var masks = new[] { 0b0101, 0 };
            var stitching = new ChunkStitching(Quads);
            int len0 = stitching.Indices(masks[0]).Length;
            int len1 = stitching.Indices(masks[1]).Length;
            var all = new NativeArray<ushort>(len0 + len1, Allocator.TempJob);
            NativeArray<ushort>.Copy(stitching.Indices(masks[0]), 0, all, 0, len0);
            NativeArray<ushort>.Copy(stitching.Indices(masks[1]), 0, all, len0, len1);
            var ranges = new NativeArray<int2>(new[] { new int2(0, len0), new int2(len0, len1) }, Allocator.TempJob);
            var geometries = new NativeArray<ChunkGeometry>(2, Allocator.TempJob);
            var bounds = new NativeArray<float3x2>(2, Allocator.TempJob);
            Mesh.MeshDataArray data = Mesh.AllocateWritableMeshData(2);
            var meshes = new[] { new Mesh(), new Mesh() };
            try
            {
                for (int k = 0; k < 2; k++)
                {
                    geometries[k] = ChunkGeometry.Create(ids[k], Planet640, Height, Quads);
                    ChunkMeshJob.PrepareOutput(data[k], Quads, ranges[k].y);
                }

                new ChunkMeshJob { Geometries = geometries, IndexRanges = ranges, AllIndices = all, Output = data, Bounds = bounds }
                    .Schedule(2, 1).Complete();
                float3x2[] box = bounds.ToArray();
                Mesh.ApplyAndDisposeWritableMeshData(data, meshes);
                for (int k = 0; k < 2; k++)
                {
                    Assert.AreEqual(stitching.VertexCount, meshes[k].vertexCount);
                    CollectionAssert.AreEqual(stitching.Indices(masks[k]), System.Array.ConvertAll(meshes[k].GetIndices(0), x => (ushort)x));
                    foreach (Vector3 p in meshes[k].vertices)
                    {
                        Assert.IsTrue(math.all((float3)p >= box[k].c0) && math.all((float3)p <= box[k].c1));
                    }
                }
            }
            finally
            {
                all.Dispose();
                ranges.Dispose();
                geometries.Dispose();
                bounds.Dispose();
                Object.DestroyImmediate(meshes[0]);
                Object.DestroyImmediate(meshes[1]);
            }
        }

        [Test]
        public void Geometry_preconditions_are_enforced()
        {
            var id = ChunkId.Root(CubeFace.PosX);
            Assert.Throws<ContractException>(() => ChunkGeometry.Create(id, default, Height, Quads));
            Assert.Throws<ContractException>(() => ChunkGeometry.Create(id, Planet640, default, Quads));
            Assert.Throws<ContractException>(() => ChunkGeometry.Create(id, Planet640, Height, 24));
            Assert.Throws<ContractException>(() => CubeSphere.FaceToDirection(CubeFace.PosX, 1.5f, 0f));
        }
    }
}
