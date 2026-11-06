using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Planet.Model;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Planet.Tests
{
    // Counter-examples and surviving mutations from docs/reviews/P1-review-r2-sol.md (A1–A3, B2–B4).
    public sealed class PlanetReviewR2RegressionTests
    {
        private static readonly PlanetConfig Planet640 = new PlanetConfig(640f, float3.zero);

        // ---- A1: the leaf count at the most permissive legal margin no longer grows with depth.

        [Test]
        public void A1_review_settings_that_grew_4_to_the_D_are_rejected()
        {
            Assert.Throws<ContractException>(() => new ChunkQuadtree(Planet640, new ChunkLodSettings(9, 32, 2f, 0.25f, 313.6f)));
            Assert.Throws<ContractException>(() => new ChunkQuadtree(Planet640, new ChunkLodSettings(7, 32, 2f, 0.25f, 313.6f)));
        }

        [Test]
        public void A1_leaf_count_at_the_margin_limit_is_bounded_independent_of_depth()
        {
            var added = new List<ChunkId>();
            var removed = new List<ChunkId>();
            var mask = new List<ChunkId>();
            var camera = new float3(641.7f, 0f, 0f);
            foreach (int depth in new[] { 5, 7, 9, 11 })
            {
                float margin = math.min(
                    ChunkQuadtree.MaxMarginInFinestEdges * ChunkLodSettings.NodeEdgeLength(Planet640, depth),
                    0.49f * Planet640.Radius);
                var tree = new ChunkQuadtree(Planet640, new ChunkLodSettings(depth, 32, 2f, 0.25f, margin));
                tree.Update(camera, added, removed, mask);
                // ≈ π·(16 + split radius)² finest leaves plus the coarser rings and 2:1 balance.
                Assert.LessOrEqual(tree.LeafCount, 6000, $"depth {depth}");
            }
        }

        // ---- A2: WorldToLocal's domain makes the round-trip Post achievable.

        [Test]
        public void A2_overflowing_world_vectors_are_rejected_and_the_limit_round_trips()
        {
            PlanetFrame f = PlanetFrame.FromUp(math.normalize(new float3(1f, 1f, 0f)));
            Assert.Throws<ContractException>(() => f.WorldToLocal(new float3(float.MaxValue, float.MaxValue, 0f)));
            Assert.Throws<ContractException>(() => f.WorldToLocal(new float3(float.NaN, 0f, 0f)));
            Assert.Throws<ContractException>(() => f.LocalToWorld(new float3(0f, 0f, 3f * PlanetFrame.MaxVectorComponent)));

            float m = PlanetFrame.MaxVectorComponent;
            foreach (float3 world in new[] { new float3(m, m, -m), new float3(-m, m, m), new float3(m, 0f, 0f) })
            {
                float3 local = f.WorldToLocal(world);
                Assert.IsTrue(math.all(math.isfinite(local)));
                float3 back = f.LocalToWorld(local);
                Assert.LessOrEqual(math.cmax(math.abs(back - world)), 1e-6f * m);
            }
        }

        // ---- A3: negative margin and the dictionaries' own comparers.

        [Test]
        public void A3_negative_height_margin_is_rejected()
        {
            Assert.Throws<ContractException>(() => new ChunkLodSettings(1, 32, 2f, 0.25f, -1f));
            Assert.Throws<ContractException>(() => new ChunkLodSettings(1, 32, 2f, 0.25f, -float.Epsilon));
        }

        [Test]
        public void A3_both_quadtree_dictionaries_spread_packed_keys()
        {
            var tree = new ChunkQuadtree(Planet640, ChunkLodSettings.ForPlanet(Planet640, 1f, 0f));
            var l1 = new List<ChunkId>();
            var l2 = new List<ChunkId>();
            var l3 = new List<ChunkId>();
            tree.Update(new float3(641.7f, 0f, 0f), l1, l2, l3);
            foreach (IEqualityComparer<ulong> comparer in new[] { tree.LeafKeyComparer, tree.TargetKeyComparer })
            {
                const int level = 7;
                int n = 1 << level;
                var hashes = new HashSet<int>();
                int total = 0;
                for (int face = 0; face < CubeSphere.FaceCount; face++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        for (int y = 0; y < n; y++)
                        {
                            hashes.Add(comparer.GetHashCode(new ChunkId((CubeFace)face, level, x, y).Key));
                            total++;
                        }
                    }
                }

                // The default ulong comparer yields 2,048 distinct values here.
                Assert.GreaterOrEqual(hashes.Count, total - 16);
            }
        }

        // ---- B2: amplitude and frequency domains keep the bound and every intermediate finite.

        [Test]
        public void B2_unbounded_amplitude_and_frequency_are_rejected()
        {
            Assert.Throws<ContractException>(() => new PlanetHeight(42UL, float.MaxValue, 2f, 8));
            Assert.Throws<ContractException>(() => new PlanetHeight(42UL, PlanetHeight.MaxAmplitude * 1.01f, 2f, 1));
            Assert.Throws<ContractException>(() => new PlanetHeight(42UL, 1f, float.MaxValue, 1));
            Assert.Throws<ContractException>(() => new PlanetHeight(42UL, 1f, float.NaN, 1));
            Assert.Throws<ContractException>(() => new PlanetHeight(42UL, 1f, 8.01f, 8));
            Assert.Throws<ContractException>(() => new PlanetHeight(42UL, 1f, PlanetHeight.MaxTopFrequency * 1.01f, 1));
        }

        [Test]
        public void B2_extreme_legal_parameters_give_finite_bounded_heights()
        {
            var cases = new[]
            {
                new PlanetHeight(42UL, PlanetHeight.MaxAmplitude, PlanetHeight.MaxTopFrequency / 128f, 8),
                new PlanetHeight(42UL, PlanetHeight.MaxAmplitude, PlanetHeight.MaxTopFrequency, 1),
                new PlanetHeight(42UL, PlanetHeight.MaxAmplitude, 1e-30f, 8),
            };
            foreach (PlanetHeight h in cases)
            {
                Assert.IsTrue(math.isfinite(h.MaxAbsHeight));
                var rng = new Pcg32(8UL, 9UL);
                for (int k = 0; k < 2000; k++)
                {
                    float s = h.Sample(PlanetHeightTests.RandomDirection(ref rng));
                    Assert.IsTrue(math.isfinite(s));
                    Assert.LessOrEqual(math.abs(s), h.MaxAbsHeight);
                }
            }
        }

        // ---- B3: the clamp is what makes MaxAbsHeight a bound; raw snoise exceeds 1.

        [Test]
        public void B3_clamp_engages_where_raw_simplex_exceeds_one()
        {
            // Rebuild the first octave offset the constructor derives from seed 42 (stream 'H').
            var rng = new Pcg32(42UL, 0x48UL);
            float ox = rng.NextFloat() * 512f - 256f;
            float oy = rng.NextFloat() * 512f - 256f;
            float oz = rng.NextFloat() * 512f - 256f;
            var offset = new float3(ox, oy, oz);
            float3 rel = new float3(-81.06958f, 205.9524f, 1.4006042f) - offset;
            var h = new PlanetHeight(42UL, 10f, math.length(rel), 1);
            float3 dir = math.normalize(rel);
            Assert.Greater(noise.snoise(math.normalize(dir) * h.BaseFrequency + offset), 1f, "raw peak moved; pick a new witness");
            Assert.AreEqual(10f, h.Sample(dir));
        }

        [Test]
        public void B3_zero_amplitude_is_flat_and_one_and_eight_octaves_stay_bounded()
        {
            var flat = new PlanetHeight(5UL, 0f, 2f, 8);
            var one = new PlanetHeight(5UL, 10f, 2f, 1);
            var eight = new PlanetHeight(5UL, 10f, 2f, 8);
            var rng = new Pcg32(3UL, 4UL);
            for (int k = 0; k < 5000; k++)
            {
                float3 d = PlanetHeightTests.RandomDirection(ref rng);
                Assert.AreEqual(0f, flat.Sample(d));
                Assert.LessOrEqual(math.abs(one.Sample(d)), one.MaxAbsHeight);
                Assert.LessOrEqual(math.abs(eight.Sample(d)), eight.MaxAbsHeight);
            }
        }

        // ---- B4: public query and mesh entry evaluate the same graph.

        [Test]
        public void B4_Sample_and_SampleCore_are_bit_identical()
        {
            PlanetHeight h = PlanetHeight.ForPlanet(Planet640, 42UL);
            var rng = new Pcg32(2026UL, 11UL);
            for (int k = 0; k < 4096; k++)
            {
                float3 d = PlanetHeightTests.RandomDirection(ref rng);
                Assert.AreEqual(math.asuint(h.SampleCore(d)), math.asuint(h.Sample(d)), $"unit #{k}");
                float3 loose = d * 1.0004f;
                Assert.AreEqual(math.asuint(h.SampleCore(loose)), math.asuint(h.Sample(loose)), $"loose #{k}");
            }
        }
    }
}
