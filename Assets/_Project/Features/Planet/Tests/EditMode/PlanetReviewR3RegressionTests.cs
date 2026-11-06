using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet.Model;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Planet.Tests
{
    // Counter-examples and surviving mutations from docs/reviews/P1-review-r3-sol.md (A1-R3, N1, N2, N03).
    public sealed class PlanetReviewR3RegressionTests
    {
        private static readonly PlanetConfig Planet640 = new PlanetConfig(640f, float3.zero);

        // ---- A1-R3: retained refinement is bounded like fresh refinement.

        [Test]
        public void A1R3_unbounded_merge_hysteresis_is_rejected()
        {
            Assert.Throws<ContractException>(() => new ChunkLodSettings(5, 32, 2f, 1e6f, 0f));
            Assert.Throws<ContractException>(() => new ChunkLodSettings(5, 32, 2f, ChunkLodSettings.MaxMergeHysteresis * 1.01f, 0f));
            Assert.Throws<ContractException>(() => new ChunkLodSettings(5, 32, 2f, float.PositiveInfinity, 0f));
        }

        [Test]
        public void A1R3_sweeping_the_camera_over_the_planet_does_not_accumulate_leaves()
        {
            const int depth = 5;
            var tree = new ChunkQuadtree(Planet640, new ChunkLodSettings(depth, 32, 2f, ChunkLodSettings.MaxMergeHysteresis, 0f));
            var added = new List<ChunkId>();
            var removed = new List<ChunkId>();
            var mask = new List<ChunkId>();
            int n = 1 << (depth - 3);
            int peak = 0;
            // The review's sweep: one camera per cell centre of every face, 1.7 m above the sphere.
            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                for (int x = 0; x < n; x++)
                {
                    for (int y = 0; y < n; y++)
                    {
                        float u = -1f + (x + 0.5f) * 2f / n;
                        float v = -1f + (y + 0.5f) * 2f / n;
                        tree.Update(641.7f * CubeSphere.FaceToDirection((CubeFace)face, u, v), added, removed, mask);
                        peak = math.max(peak, tree.LeafCount);
                    }
                }
            }

            // 6·4^5 = 6,144 if nothing ever merges; a single camera position needs ≈ 1,250.
            Assert.LessOrEqual(peak, 3000);
            tree.Update(new float3(10000f, 0f, 0f), added, removed, mask);
            Assert.LessOrEqual(tree.LeafCount, 200, "leaving the planet must release the retained refinement");
        }

        // N03: the margin cap is an absolute number of finest edges, not just whatever the constant says.
        [Test]
        public void N03_margin_of_twenty_finest_edges_is_rejected()
        {
            float edge = ChunkLodSettings.NodeEdgeLength(Planet640, 9);
            Assert.Throws<ContractException>(() => new ChunkQuadtree(Planet640, new ChunkLodSettings(9, 32, 2f, 0.25f, 20f * edge)));
            Assert.DoesNotThrow(() => new ChunkQuadtree(Planet640, new ChunkLodSettings(9, 32, 2f, 0.25f, 16f * edge)));
        }

        // ---- N1: PlanetTerrain.Configure's domain matches the quadtree's.

        [TestCase(320f, 32, true)]
        [TestCase(640f, 32, true)]
        [TestCase(1280f, 32, true)]
        [TestCase(640f, 2, false)]
        [TestCase(1280f, 2, false)]
        [TestCase(1280f, 4, false)]
        [TestCase(8000f, 32, false)]
        public void N1_supported_terrain_configurations(float radius, int quadsPerEdge, bool expected)
        {
            var config = new PlanetConfig(radius, float3.zero);
            PlanetHeight height = PlanetHeight.ForPlanet(config, 42UL);
            ChunkLodSettings lod = ChunkLodSettings.ForPlanet(config, 1f, height.MaxAbsHeight, quadsPerEdge);
            Assert.AreEqual(expected, ChunkQuadtree.Supports(config, lod));
        }

        [Test]
        public void N1_unsupported_Configure_is_rejected_before_any_state_changes()
        {
            var go = new GameObject("PlanetTerrainN1");
            var viewer = new GameObject("Viewer");
            try
            {
                var terrain = go.AddComponent<PlanetTerrain>();
                Vector3 before = go.transform.position;
                PlanetHeight height = PlanetHeight.ForPlanet(Planet640, 42UL);
                Assert.IsTrue(terrain.IsUntouchedByConfigure);
                var e = Assert.Throws<ContractException>(() =>
                    terrain.Configure(new PlanetConfig(640f, new float3(5f, 0f, 0f)), height, viewer.transform, null, 128f, 2));
                Assert.AreEqual(before, go.transform.position, "Configure moved the transform before rejecting");
                // sol r4 N3: deleting Configure's own Supports check (the quadtree constructor then throws) or moving it
                // below `_config = config` both left the transform alone; field state and the message tell them apart.
                Assert.IsTrue(terrain.IsUntouchedByConfigure, "Configure wrote fields before rejecting");
                StringAssert.Contains("ChunkQuadtree.Supports", e.Message, "rejected by Configure's own check");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(viewer);
            }
        }

        // ---- N2: the √3 diagonal frame needs the 2× slack on LocalToWorld.

        [Test]
        public void N2_diagonal_frame_round_trips_at_the_component_limit()
        {
            PlanetFrame f = PlanetFrame.FromUp(math.normalize(new float3(1f, 1f, 1f)));
            float m = PlanetFrame.MaxVectorComponent;
            foreach (float3 world in new[] { new float3(m, m, m), new float3(-m, -m, -m) })
            {
                float3 local = f.WorldToLocal(world);
                Assert.Greater(math.cmax(math.abs(local)), 1.7f * m, "this frame must exercise the √3 case");
                float3 back = f.LocalToWorld(local);
                Assert.LessOrEqual(math.cmax(math.abs(back - world)), 1e-6f * m);
            }

            Assert.IsTrue(math.all(math.isfinite(f.LocalToWorld(new float3(2f * m, -2f * m, 2f * m)))));
        }
    }
}
