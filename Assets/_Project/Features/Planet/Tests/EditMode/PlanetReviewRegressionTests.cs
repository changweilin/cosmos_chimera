using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Planet.Model;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Planet.Tests
{
    // Counter-examples and surviving mutations from docs/reviews/P1-01-P1-02-sol.md, one test per finding.
    public sealed class PlanetReviewRegressionTests
    {
        private static readonly PlanetConfig Planet640 = new PlanetConfig(640f, new float3(3f, -4f, 5f));

        // ---- F1: ProjectOnTangent is scale invariant and never returns a near-radial vector.

        [Test]
        public void F1_projection_of_scaled_up_falls_back_to_north_at_every_scale()
        {
            PlanetFrame f = PlanetFrame.FromUp(math.normalize(new float3(1, 2, 3)));
            foreach (float scale in new[] { 1e-30f, 1e-6f, 1f, 4f, 100f, 1000f, 1e20f, -100f })
            {
                Assert.AreEqual(f.North, f.ProjectOnTangent(f.Up * scale), $"scale {scale}");
            }

            Assert.AreEqual(f.North, f.ProjectOnTangent(float3.zero));
            // Inside the documented 1e-3 rad cone the heading counts as lost, even though a tangent is computable.
            Assert.AreEqual(f.North, f.ProjectOnTangent(math.normalize(f.Up + f.East * 1e-4f)));
            Assert.AreNotEqual(f.North, f.ProjectOnTangent(math.normalize(f.Up + f.East * 2e-3f)));
        }

        [Test]
        public void F1_projection_is_tangent_unit_and_independent_of_length()
        {
            var rng = new Pcg32(17UL, 3UL);
            for (int k = 0; k < 2000; k++)
            {
                PlanetFrame f = PlanetFrame.FromUp(PlanetFrameTests.RandomDirection(ref rng));
                float3 d = PlanetFrameTests.RandomDirection(ref rng);
                // Near-parallel but past the 1e-3 rad cut-off: the hardest legal case.
                if (k % 4 == 0)
                {
                    d = math.normalize(f.Up + f.East * 2e-3f);
                }

                float3 t = f.ProjectOnTangent(d);
                Assert.AreEqual(1f, math.length(t), 1e-6f);
                Assert.LessOrEqual(math.abs(math.dot(t, f.Up)), 1e-6f, $"k={k}");
                foreach (float scale in new[] { 1e-25f, 1000f, 1e25f })
                {
                    // Rescaling perturbs d by ~1 ulp; near Up the projection amplifies that by 1/sin (500× at 2e-3).
                    Assert.Less(math.distance(t, f.ProjectOnTangent(d * scale)), 2e-4f, $"k={k} scale {scale}");
                }
            }
        }

        // ---- F2: the ±1e-3 input tolerance never leaks into outputs.

        [Test]
        public void F2_near_unit_inputs_give_exact_unit_basis_and_radius()
        {
            foreach (float3 up in new[] { new float3(0f, 0.999f, 0f), new float3(0.6f, 0.8005f, 0f) })
            {
                PlanetFrame f = PlanetFrame.FromUp(up);
                Assert.AreEqual(1f, math.length(f.Up), 1e-6f);
                Assert.AreEqual(1f, math.length(f.North), 1e-6f);
                Assert.AreEqual(1f, math.length(f.East), 1e-6f);
                Assert.AreEqual(1f, f.WorldToLocal(f.LocalToWorld(new float3(0f, 1f, 0f))).y, 1e-6f);
                Assert.LessOrEqual(math.abs(math.dot(f.ProjectOnTangent(f.Up * 4f + f.East), f.Up)), 1e-6f);
            }

            var c = new PlanetConfig(640f, float3.zero);
            Assert.AreEqual(640f, math.length(c.ToWorld(new float3(0f, 0.999f, 0f), 0f)), 1e-3f);
            var h = PlanetHeight.ForPlanet(c, 5UL);
            float3 d = math.normalize(new float3(0.3f, 0.4f, 0.5f));
            Assert.AreEqual(h.Sample(d), h.Sample(d * 1.0009f), 1e-4f);
        }

        // ---- F3: finite inputs at the extremes either work or are rejected by a precondition.

        [Test]
        public void F3_extreme_magnitudes_normalise_correctly()
        {
            var c = new PlanetConfig(640f, float3.zero);
            Assert.AreEqual(new float3(1f, 0f, 0f), c.ToDirection(new float3(1e20f, 0f, 0f)));
            Assert.AreEqual(CubeFace.PosX, CubeSphere.DirectionToFace(new float3(1e-30f, 0f, 0f), out float u, out float v));
            Assert.AreEqual(0f, u);
            Assert.AreEqual(0f, v);
            Assert.AreEqual(CubeFace.NegZ, CubeSphere.DirectionToFace(new float3(1e30f, -2e30f, -3e30f), out _, out _));
        }

        [Test]
        public void F3_out_of_domain_configs_are_rejected()
        {
            Assert.Throws<ContractException>(() => new PlanetConfig(float.Epsilon, float3.zero));
            Assert.Throws<ContractException>(() => new PlanetConfig(0.5f, float3.zero));
            Assert.Throws<ContractException>(() => new PlanetConfig(640f, new float3(34359738368f, 0f, 0f)));
            Assert.Throws<ContractException>(() => new PlanetConfig(640f, new float3(0f, 0f, -PlanetConfig.MaxCenterComponent * 1.01f)));
            Assert.DoesNotThrow(() => new PlanetConfig(PlanetConfig.MinSupportedRadius, new float3(PlanetConfig.MaxCenterComponent)));
            var c = new PlanetConfig(640f, float3.zero);
            Assert.Throws<ContractException>(() => c.ToWorld(new float3(1f, 0f, 0f), PlanetConfig.MaxSupportedRadius * 2f));
            Assert.Throws<ContractException>(() => c.HorizonDistance(PlanetConfig.MaxSupportedRadius * 2f));
            Assert.Throws<ContractException>(() => c.ToDirection(new float3(5e-4f, 5e-4f, 5e-4f)));
            var tiny = new PlanetConfig(PlanetConfig.MinSupportedRadius, float3.zero);
            Assert.Greater(ChunkLodSettings.NodeEdgeLength(tiny, ChunkId.MaxLevel), 0f);
            Assert.Greater(tiny.HorizonDistance(float.Epsilon), 0f);
        }

        // ---- F4: hashing spreads packed keys; tree settings that would refine the whole sphere are rejected.

        [Test]
        public void F4_chunk_key_hashes_do_not_collide_systematically()
        {
            const int level = 7;
            int n = 1 << level;
            var hashes = new HashSet<int>();
            int total = 0;
            for (int f = 0; f < CubeSphere.FaceCount; f++)
            {
                for (int x = 0; x < n; x++)
                {
                    for (int y = 0; y < n; y++)
                    {
                        hashes.Add(new ChunkId((CubeFace)f, level, x, y).GetHashCode());
                        total++;
                    }
                }
            }

            // 98,304 keys: the old hi^lo hash produced only 2,048 distinct values. A random 32-bit hash expects
            // ~1 collision here.
            Assert.GreaterOrEqual(hashes.Count, total - 16);
        }

        [Test]
        public void F4_runaway_refinement_settings_are_rejected()
        {
            Assert.Throws<ContractException>(() =>
                new ChunkQuadtree(Planet640, ChunkLodSettings.ForPlanet(Planet640, 1f, Planet640.Radius)));
            Assert.Throws<ContractException>(() =>
                new ChunkQuadtree(Planet640, ChunkLodSettings.ForPlanet(Planet640, 1f, 0f, 32, ChunkQuadtree.MaxSplitDistanceFactor * 2f)));
            Assert.DoesNotThrow(() =>
                new ChunkQuadtree(Planet640, ChunkLodSettings.ForPlanet(Planet640, 1f, 0.49f * Planet640.Radius)));
        }

        // ---- F5: hysteresis has a positive floor, so adjacent-float camera jitter cannot flap the tree.

        [Test]
        public void F5_zero_hysteresis_is_rejected_and_float_jitter_does_not_flap()
        {
            Assert.Throws<ContractException>(() => new ChunkLodSettings(1, 32, 2f, 0f, 0f));
            var c = new PlanetConfig(640f, float3.zero);
            var tree = new ChunkQuadtree(c, new ChunkLodSettings(1, 32, 2f, ChunkLodSettings.MinMergeHysteresis, 0f));
            var added = new List<ChunkId>();
            var removed = new List<ChunkId>();
            var mask = new List<ChunkId>();
            // The review's split-boundary camera and its float predecessor.
            var a = new float3(3239.0364f, 0f, 0f);
            var b = new float3(3239.0361f, 0f, 0f);
            tree.Update(b, added, removed, mask);
            for (int k = 0; k < 6; k++)
            {
                tree.Update(k % 2 == 0 ? a : b, added, removed, mask);
                Assert.AreEqual(0, added.Count + removed.Count + mask.Count, $"step {k}");
            }
        }

        // ---- F6: preconditions that had no test.

        [Test]
        public void F6_missing_precondition_checks_are_enforced()
        {
            Assert.Throws<ContractException>(() => CubeSphere.RunsAlongU((CubeEdge)255));
            PlanetConfig none = default;
            Assert.Throws<ContractException>(() => none.ToWorld(new float3(1f, 0f, 0f), 0f));
            Assert.Throws<ContractException>(() => none.ToDirection(new float3(1f, 0f, 0f)));
            Assert.Throws<ContractException>(() => none.Altitude(new float3(1f, 0f, 0f)));
            Assert.Throws<ContractException>(() => Planet640.ToWorld(new float3(1f, 0f, 0f), float.NaN));
            Assert.Throws<ContractException>(() => Planet640.HorizonDistance(float.PositiveInfinity));
            Assert.Throws<ContractException>(() => CubeSphere.FaceToDirection(CubeFace.PosX, 0f, -1.01f));
            Assert.Throws<ContractException>(() => CubeSphere.FaceToDirection(CubeFace.PosX, 1.01f, 0f));
            Assert.Throws<ContractException>(() => new ChunkId(CubeFace.PosX, -1, 0, 0));
            // Valid face and level, X out of range: the review's surviving FromKey mutation.
            ulong badX = ((ulong)CubeFace.PosY << 61) | (3UL << 56) | (8UL << 28);
            Assert.Throws<ContractException>(() => ChunkId.FromKey(badX));
            ulong badY = ((ulong)CubeFace.PosY << 61) | (3UL << 56) | 9UL;
            Assert.Throws<ContractException>(() => ChunkId.FromKey(badY));
            PlanetFrame frame = PlanetFrame.FromUp(new float3(0f, 0f, 1f));
            Assert.Throws<ContractException>(() => frame.LocalToWorld(new float3(float.NaN, 0f, 0f)));
            Assert.Throws<ContractException>(() => frame.WorldToLocal(new float3(0f, float.PositiveInfinity, 0f)));
            Assert.Throws<ContractException>(() => new ChunkLodSettings(1, 32, float.PositiveInfinity, 0.25f, 0f));
            Assert.Throws<ContractException>(() => new ChunkLodSettings(1, 32, 2f, float.NaN, 0f));
            var tree = new ChunkQuadtree(Planet640, ChunkLodSettings.ForPlanet(Planet640, 1f, 0f));
            var l1 = new List<ChunkId>();
            var l2 = new List<ChunkId>();
            Assert.Throws<ContractException>(() => tree.Update(float3.zero, l1, l1, l2));
            Assert.Throws<ContractException>(() => tree.Update(float3.zero, l1, l2, l1));
            Assert.Throws<ContractException>(() => tree.Update(float3.zero, l2, l1, l1));
        }

        // ---- F7: contract texts match float behaviour.

        [Test]
        public void F7_longitude_range_holds_on_the_antimeridian_for_both_zero_signs()
        {
            Assert.AreEqual(math.PI, LatLon.FromDirection(new float3(-1f, 0f, 0f)).Longitude);
            Assert.AreEqual(math.PI, LatLon.FromDirection(new float3(-1f, 0f, -0f)).Longitude);
        }

        [Test]
        public void F7_warp_is_non_decreasing_and_strictly_increasing_on_the_tested_grid()
        {
            const int steps = 1 << 20;
            float previous = CubeSphere.Warp(-1f);
            for (int k = 1; k <= steps; k++)
            {
                float w = CubeSphere.Warp(-1f + k / (float)(steps / 2));
                Assert.IsTrue(w > previous, $"k={k}");
                previous = w;
            }

            // The review's adjacent-float pair: equal results are allowed (non-decreasing), inversions are not.
            Assert.LessOrEqual(CubeSphere.Warp(0.983702f), CubeSphere.Warp(0.98370206f));
        }

        // ---- Surviving mutations listed in the review.

        [Test]
        public void Mutation_latitude_uses_atan2_near_the_poles()
        {
            // asin(y) loses ~1e-4 here because y rounds to 1; atan2 keeps it.
            float3 d = math.normalize(new float3(1e-4f, 1f, 0f));
            Assert.AreEqual(math.PI * 0.5 - 1e-4, LatLon.FromDirection(d).Latitude, 1e-6);
            float3 d2 = math.normalize(new float3(1e-3f, 1f, 0f));
            Assert.AreEqual(math.PI * 0.5 - 1e-3, LatLon.FromDirection(d2).Latitude, 1e-6);
        }

        [Test]
        public void Mutation_update_lists_are_sorted_by_key()
        {
            var tree = new ChunkQuadtree(Planet640, ChunkLodSettings.ForPlanet(Planet640, 1f, 20f));
            var added = new List<ChunkId>();
            var removed = new List<ChunkId>();
            var mask = new List<ChunkId>();
            int sawRemoved = 0;
            int sawMask = 0;
            for (int i = 0; i < 40; i++)
            {
                float3 dir = math.normalize(new float3(1f, 0.02f * i, 0.5f));
                tree.Update(Planet640.ToWorld(dir, 1.7f), added, removed, mask);
                foreach (List<ChunkId> list in new[] { added, removed, mask })
                {
                    for (int k = 1; k < list.Count; k++)
                    {
                        Assert.Less(list[k - 1].Key, list[k].Key, $"step {i}");
                    }
                }

                sawRemoved += removed.Count > 1 ? 1 : 0;
                sawMask += mask.Count > 1 ? 1 : 0;
            }

            Assert.Greater(sawRemoved, 0, "path never produced a multi-item removed list");
            Assert.Greater(sawMask, 0, "path never produced a multi-item mask list");
        }

        [Test]
        public void Mutation_ForPlanet_starts_from_level_zero()
        {
            Assert.AreEqual(0, ChunkLodSettings.ForPlanet(new PlanetConfig(1f, float3.zero), 1f, 0f).MaxLevel);
            Assert.AreEqual(0, ChunkLodSettings.ForPlanet(new PlanetConfig(10f, float3.zero), 1f, 0f).MaxLevel);
            Assert.AreEqual(1, ChunkLodSettings.ForPlanet(new PlanetConfig(10f, float3.zero), 0.4f, 0f).MaxLevel);
        }

        [Test]
        public void Mutation_children_on_edge_are_in_along_edge_order()
        {
            var id = new ChunkId(CubeFace.NegZ, 3, 2, 5);
            for (int e = 0; e < 4; e++)
            {
                var edge = (CubeEdge)e;
                id.ChildrenOnEdge(edge, out ChunkId first, out ChunkId second);
                bool alongU = CubeSphere.RunsAlongU(edge);
                int a0 = alongU ? first.X : first.Y;
                int a1 = alongU ? second.X : second.Y;
                Assert.AreEqual(a0 + 1, a1, edge.ToString());
                int across = alongU ? first.Y : first.X;
                bool max = edge == CubeEdge.UMax || edge == CubeEdge.VMax;
                Assert.AreEqual(max ? 1 : 0, across & 1, edge.ToString());
            }
        }

        [Test]
        public void Mutation_face_ties_prefer_x_then_y()
        {
            Assert.AreEqual(CubeFace.PosX, CubeSphere.DirectionToFace(new float3(1f, 1f, 0f), out _, out _));
            Assert.AreEqual(CubeFace.NegX, CubeSphere.DirectionToFace(new float3(-1f, 1f, 1f), out _, out _));
            Assert.AreEqual(CubeFace.PosY, CubeSphere.DirectionToFace(new float3(0f, 1f, -1f), out _, out _));
            Assert.AreEqual(CubeFace.PosX, CubeSphere.DirectionToFace(new float3(1f, -1f, -1f), out _, out _));
        }
    }
}
