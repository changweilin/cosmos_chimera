using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet.Model;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Planet.Tests
{
    public sealed class ChunkQuadtreeTests
    {
        private static readonly PlanetConfig Planet640 = new PlanetConfig(640f, new float3(3f, -4f, 5f));

        private readonly List<ChunkId> _added = new List<ChunkId>();
        private readonly List<ChunkId> _removed = new List<ChunkId>();
        private readonly List<ChunkId> _maskChanged = new List<ChunkId>();
        private readonly List<ChunkId> _leaves = new List<ChunkId>();

        private static ChunkQuadtree NewTree(in PlanetConfig config) =>
            new ChunkQuadtree(config, ChunkLodSettings.ForPlanet(config, 1f, 20f));

        private void Update(ChunkQuadtree tree, float3 camera) => tree.Update(camera, _added, _removed, _maskChanged);

        private static int TotalChanges(ChunkQuadtreeTests t) => t._added.Count + t._removed.Count + t._maskChanged.Count;

        // A great circle through two cube corners, both poles (face centres) and two face-edge midpoints:
        // the worst case for cross-face neighbour lookup.
        private static float3 CirclePoint(int step, int steps)
        {
            float t = 2f * math.PI * step / steps;
            float3 a = math.normalize(new float3(1f, 1f, 1f));
            float3 b = math.normalize(new float3(-1f, 2f, -1f) - a * math.dot(new float3(-1f, 2f, -1f), a));
            return math.normalize(a * math.cos(t) + b * math.sin(t));
        }

        private void AssertInvariants(ChunkQuadtree tree, string label)
        {
            tree.CopyLeaves(_leaves);
            var set = new HashSet<ChunkId>(_leaves);
            int maxLevel = tree.Settings.MaxLevel;
            long area = 0;
            foreach (ChunkId leaf in _leaves)
            {
                area += 1L << (2 * (maxLevel - leaf.Level));
                for (ChunkId a = leaf; a.Level > 0;)
                {
                    a = a.Parent();
                    Assert.IsFalse(set.Contains(a), $"{label}: {leaf} overlaps ancestor {a}");
                }

                Assert.IsTrue(tree.TryGetLeaf(leaf, out byte mask));
                for (int e = 0; e < 4; e++)
                {
                    ChunkId nb = leaf.Neighbor((CubeEdge)e);
                    ChunkId cover = nb;
                    while (!set.Contains(cover) && cover.Level > 0)
                    {
                        cover = cover.Parent();
                    }

                    bool coarser = false;
                    if (set.Contains(cover))
                    {
                        Assert.GreaterOrEqual(cover.Level, leaf.Level - 1, $"{label}: 2:1 violated between {leaf} and {cover}");
                        coarser = cover.Level == leaf.Level - 1;
                    }

                    Assert.AreEqual(coarser, (mask & (1 << e)) != 0, $"{label}: mask of {leaf} edge {(CubeEdge)e}");
                }
            }

            // Leaves have no overlaps (above) and their areas add up to the six faces, so they tile the sphere.
            Assert.AreEqual(6L << (2 * maxLevel), area, $"{label}: coverage");
        }

        [Test]
        public void Settings_derive_depth_from_radius()
        {
            foreach (float r in new[] { 320f, 640f, 1280f })
            {
                var config = new PlanetConfig(r, float3.zero);
                ChunkLodSettings s = ChunkLodSettings.ForPlanet(config, 1f, 0f);
                float spacing = ChunkLodSettings.NodeEdgeLength(config, s.MaxLevel) / s.QuadsPerEdge;
                Assert.LessOrEqual(spacing, 1f, $"R={r}");
                Assert.Greater(spacing * 2f, 1f, $"R={r}: MaxLevel is the smallest sufficient level");
                TestContext.WriteLine($"R={r}: MaxLevel={s.MaxLevel}, finest spacing={spacing:F3} m");
            }

            Assert.AreEqual(5, ChunkLodSettings.ForPlanet(Planet640, 1f, 0f).MaxLevel);
        }

        [Test]
        public void Distant_camera_sees_only_the_six_roots()
        {
            ChunkQuadtree tree = NewTree(Planet640);
            Update(tree, Planet640.Center + new float3(0f, 100f * Planet640.Radius, 0f));
            Assert.AreEqual(6, tree.LeafCount);
            Assert.AreEqual(6, _added.Count);
            Assert.AreEqual(0, _removed.Count);
            AssertInvariants(tree, "far");
        }

        [Test]
        public void Walking_a_great_circle_keeps_every_invariant()
        {
            ChunkQuadtree tree = NewTree(Planet640);
            var mirror = new HashSet<ChunkId>();
            const int steps = 720; // ~5.6 m per step at R=640 m.
            int maxLeaves = 0;
            for (int i = 0; i <= steps; i++)
            {
                float3 dir = CirclePoint(i, steps);
                Update(tree, Planet640.ToWorld(dir, 1.7f));

                foreach (ChunkId r in _removed)
                {
                    Assert.IsTrue(mirror.Remove(r), $"step {i}: removed {r} was not a leaf");
                }

                foreach (ChunkId a in _added)
                {
                    Assert.IsTrue(mirror.Add(a), $"step {i}: added {a} was already a leaf");
                }

                tree.CopyLeaves(_leaves);
                Assert.AreEqual(mirror.Count, _leaves.Count, $"step {i}: diff does not reproduce the leaf set");
                Assert.AreEqual(tree.Settings.MaxLevel, tree.FindLeaf(dir).Level, $"step {i}: ground under camera is not finest");
                AssertInvariants(tree, $"step {i}");
                maxLeaves = math.max(maxLeaves, tree.LeafCount);
            }

            TestContext.WriteLine($"max leaves during walk: {maxLeaves}");
        }

        [Test]
        public void Same_position_reports_no_changes()
        {
            ChunkQuadtree tree = NewTree(Planet640);
            float3 p = Planet640.ToWorld(math.normalize(new float3(0.3f, 0.9f, -0.2f)), 1.7f);
            Update(tree, p);
            Update(tree, p);
            Assert.AreEqual(0, TotalChanges(this));
        }

        [Test]
        public void Hysteresis_stops_flapping_on_small_back_and_forth_moves()
        {
            ChunkQuadtree tree = NewTree(Planet640);
            float3 dir = math.normalize(new float3(1f, 0.2f, 0.7f));
            PlanetFrame frame = PlanetFrame.FromUp(dir);
            float3 a = Planet640.ToWorld(dir, 1.7f);
            for (int k = 0; k < 400; k++)
            {
                // March in 0.25 m steps; at each spot jiggle ±5 cm and require the jiggle to change nothing.
                float3 p = a + frame.East * (0.25f * k);
                Update(tree, p);
                Update(tree, p + frame.North * 0.05f);
                Update(tree, p);
                Update(tree, p + frame.North * 0.05f);
                Assert.AreEqual(0, TotalChanges(this), $"spot {k}");
                Update(tree, p);
                Assert.AreEqual(0, TotalChanges(this), $"spot {k} (return)");
            }
        }

        [Test]
        public void Same_camera_path_gives_identical_leaves()
        {
            ChunkQuadtree t1 = NewTree(Planet640);
            ChunkQuadtree t2 = NewTree(Planet640);
            var l1 = new List<ChunkId>();
            var l2 = new List<ChunkId>();
            for (int i = 0; i < 100; i++)
            {
                float3 p = Planet640.ToWorld(CirclePoint(i, 300), 30f);
                t1.Update(p, _added, _removed, _maskChanged);
                t2.Update(p, _added, _removed, _maskChanged);
                t1.CopyLeaves(l1);
                t2.CopyLeaves(l2);
                CollectionAssert.AreEqual(l1, l2, $"step {i}");
            }
        }

        [Test]
        public void All_radii_from_P1_10_keep_invariants()
        {
            foreach (float r in new[] { 320f, 1280f })
            {
                var config = new PlanetConfig(r, float3.zero);
                ChunkQuadtree tree = NewTree(config);
                for (int i = 0; i < 60; i++)
                {
                    float3 dir = CirclePoint(i * 7, 420);
                    Update(tree, config.ToWorld(dir, 1.7f));
                    AssertInvariants(tree, $"R={r} step {i}");
                    Assert.AreEqual(tree.Settings.MaxLevel, tree.FindLeaf(dir).Level);
                }
            }
        }

        [Test]
        public void Full_lap_at_every_size_tier_keeps_invariants()
        {
            // REQ: docs/roadmap/phase-1.md P1-10 — each tier must be walkable all the way round; steps scale with
            // circumference (~8 m) so every tier crosses the same number of LOD transitions per metre.
            foreach (float r in new[] { 320f, 960f, 1280f })
            {
                var config = new PlanetConfig(r, float3.zero);
                ChunkQuadtree tree = NewTree(config);
                int steps = (int)math.ceil(2f * math.PI * r / 8f);
                for (int i = 0; i <= steps; i++)
                {
                    float3 dir = CirclePoint(i, steps);
                    Update(tree, config.ToWorld(dir, 1.7f));
                    Assert.AreEqual(tree.Settings.MaxLevel, tree.FindLeaf(dir).Level, $"R={r} step {i}: ground not finest");
                    if (i % 4 == 0 || i == steps)
                    {
                        AssertInvariants(tree, $"R={r} step {i}");
                    }
                }
            }
        }

        [Test]
        public void Contract_violations_throw()
        {
            Assert.Throws<ContractException>(() => new ChunkQuadtree(default, ChunkLodSettings.ForPlanet(Planet640, 1f, 0f)));
            Assert.Throws<ContractException>(() => new ChunkQuadtree(Planet640, default));
            ChunkQuadtree tree = NewTree(Planet640);
            Assert.Throws<ContractException>(() => tree.FindLeaf(new float3(1, 0, 0)));
            Assert.Throws<ContractException>(() => tree.Update(new float3(float.NaN, 0, 0), _added, _removed, _maskChanged));
            Assert.Throws<ContractException>(() => tree.Update(float3.zero, null, _removed, _maskChanged));
            Assert.Throws<ContractException>(() => tree.Update(float3.zero, _added, _added, _maskChanged));
            Assert.Throws<ContractException>(() => tree.CopyLeaves(null));
            Assert.Throws<ContractException>(() => ChunkLodSettings.ForPlanet(Planet640, 0f, 0f));
            Assert.Throws<ContractException>(() => new ChunkLodSettings(ChunkId.MaxLevel + 1, 32, 2f, 0.25f, 0f));
            Assert.Throws<ContractException>(() => new ChunkLodSettings(5, 0, 2f, 0.25f, 0f));
            Assert.Throws<ContractException>(() => new ChunkLodSettings(5, 32, 0f, 0.25f, 0f));
            Assert.Throws<ContractException>(() => new ChunkLodSettings(5, 32, 2f, -1f, 0f));
            Assert.Throws<ContractException>(() => new ChunkLodSettings(5, 32, 2f, 0.25f, float.NaN));
            Assert.Throws<ContractException>(() => ChunkLodSettings.NodeEdgeLength(Planet640, -1));
        }
    }
}
