using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet.Model;
using NUnit.Framework;

namespace Cosmimera.Planet.Tests
{
    public sealed class ChunkStitchingTests
    {
        private static IEnumerable<int> Sizes() => new[] { 2, 4, 32 };

        private static (int i, int j) Coord(int index, int n) => (index % (n + 1), index / (n + 1));

        private static bool OnEdge(int i, int j, int n, CubeEdge e)
        {
            switch (e)
            {
                case CubeEdge.UMin: return i == 0;
                case CubeEdge.UMax: return i == n;
                case CubeEdge.VMin: return j == 0;
                default: return j == n;
            }
        }

        [Test]
        public void Every_variant_is_a_front_facing_cover_of_the_grid([ValueSource(nameof(Sizes))] int n)
        {
            var s = new ChunkStitching(n);
            for (int mask = 0; mask < ChunkStitching.VariantCount; mask++)
            {
                ushort[] idx = s.Indices(mask);
                Assert.AreEqual(0, idx.Length % 3);
                long twiceArea = 0;
                for (int t = 0; t < idx.Length; t += 3)
                {
                    Assert.Less(idx[t], s.VertexCount);
                    Assert.Less(idx[t + 1], s.VertexCount);
                    Assert.Less(idx[t + 2], s.VertexCount);
                    (int ai, int aj) = Coord(idx[t], n);
                    (int bi, int bj) = Coord(idx[t + 1], n);
                    (int ci, int cj) = Coord(idx[t + 2], n);
                    long cross = (long)(bi - ai) * (cj - aj) - (long)(bj - aj) * (ci - ai);
                    Assert.Greater(cross, 0, $"n={n} mask={mask} tri {t / 3} is degenerate or back-facing");
                    twiceArea += cross;
                }

                // All triangles counter-clockwise + total area == grid area ⇒ no overlap and no gap.
                Assert.AreEqual(2L * n * n, twiceArea, $"n={n} mask={mask}");
            }
        }

        [Test]
        public void Coarse_edges_have_only_even_vertices_and_two_quad_segments([ValueSource(nameof(Sizes))] int n)
        {
            var s = new ChunkStitching(n);
            for (int mask = 0; mask < ChunkStitching.VariantCount; mask++)
            {
                ushort[] idx = s.Indices(mask);
                var edgeUse = new Dictionary<(int, int), int>();
                for (int t = 0; t < idx.Length; t += 3)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int a = idx[t + k];
                        int b = idx[t + (k + 1) % 3];
                        var key = a < b ? (a, b) : (b, a);
                        edgeUse[key] = edgeUse.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                }

                foreach (KeyValuePair<(int, int), int> pair in edgeUse)
                {
                    (int ai, int aj) = Coord(pair.Key.Item1, n);
                    (int bi, int bj) = Coord(pair.Key.Item2, n);
                    bool boundary = false;
                    for (int e = 0; e < 4; e++)
                    {
                        boundary |= OnEdge(ai, aj, n, (CubeEdge)e) && OnEdge(bi, bj, n, (CubeEdge)e);
                    }

                    // Manifold: with all-CCW triangles and the area check above, this rules out overlaps.
                    Assert.AreEqual(boundary ? 1 : 2, pair.Value, $"n={n} mask={mask} edge {pair.Key}");
                }

                for (int e = 0; e < 4; e++)
                {
                    var edge = (CubeEdge)e;
                    bool coarse = (mask & (1 << e)) != 0;
                    int segments = 0;
                    foreach (KeyValuePair<(int, int), int> pair in edgeUse)
                    {
                        (int ai, int aj) = Coord(pair.Key.Item1, n);
                        (int bi, int bj) = Coord(pair.Key.Item2, n);
                        if (!OnEdge(ai, aj, n, edge) || !OnEdge(bi, bj, n, edge))
                        {
                            continue;
                        }

                        Assert.AreEqual(1, pair.Value, "a boundary segment must belong to exactly one triangle");
                        int along0 = CubeSphere.RunsAlongU(edge) ? ai : aj;
                        int along1 = CubeSphere.RunsAlongU(edge) ? bi : bj;
                        int length = System.Math.Abs(along1 - along0);
                        if (coarse)
                        {
                            Assert.AreEqual(2, length, $"n={n} mask={mask} {edge}");
                            Assert.AreEqual(0, along0 % 2, $"n={n} mask={mask} {edge}: odd vertex used");
                        }
                        else
                        {
                            Assert.AreEqual(1, length, $"n={n} mask={mask} {edge}");
                        }

                        segments++;
                    }

                    Assert.AreEqual(coarse ? n / 2 : n, segments, $"n={n} mask={mask} {edge}");
                }
            }
        }

        [Test]
        public void Preconditions_are_enforced()
        {
            Assert.Throws<ContractException>(() => new ChunkStitching(0));
            Assert.Throws<ContractException>(() => new ChunkStitching(1));
            Assert.Throws<ContractException>(() => new ChunkStitching(6));
            Assert.Throws<ContractException>(() => new ChunkStitching(256));
            var s = new ChunkStitching(4);
            Assert.Throws<ContractException>(() => s.Indices(-1));
            Assert.Throws<ContractException>(() => s.Indices(16));
        }
    }
}
