using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.Planet.Model
{
    /// <summary>The 16 triangle index buffers of a leaf grid, one per coarser-neighbour mask.</summary>
    /// <remarks>
    /// Vertex (i, j) — i along u, j along v — has index j·(QuadsPerEdge + 1) + i, matching ChunkMeshJob.
    /// Mask bit (int)CubeEdge set = the neighbour across that edge is one level coarser (ChunkQuadtree, 2:1).
    /// Construction: triangulate the full grid, then on every coarse edge collapse each odd edge vertex onto its
    /// lower even neighbour and drop the degenerate triangles. The edge then consists only of segments between
    /// even vertices, which are exactly the coarse neighbour's edge vertices, so there are no T-junctions (an
    /// index-only fix, unlike vertex snapping, which leaves T-junctions that sparkle under rasterisation).
    /// Odd edge vertices stay in the vertex buffer but are unreferenced.
    /// Winding: (v00, v10, v11) etc. are counter-clockwise in (u, v); since cross(AxisU, AxisV) == outward
    /// normal (CubeSphere), cross(b − a, c − a) points away from the planet, which is Unity's front face.
    /// REQ: docs/architecture/planet-tech.md#幾何：Cube-sphere 四叉樹 (16 種縫合索引)
    /// </remarks>
    internal sealed class ChunkStitching
    {
        /// <summary>Number of mask variants.</summary>
        public const int VariantCount = 16;

        private readonly ushort[][] _variants = new ushort[VariantCount][];

        /// <summary>Quads along one side of the leaf grid.</summary>
        public int QuadsPerEdge { get; }

        /// <summary>Vertices per leaf mesh, (QuadsPerEdge + 1)².</summary>
        public int VertexCount => (QuadsPerEdge + 1) * (QuadsPerEdge + 1);

        /// <summary>Builds all 16 variants for a grid of <paramref name="quadsPerEdge"/> quads per side.</summary>
        /// <remarks>
        /// Pre: quadsPerEdge is a power of two within [2, 128] (power of two keeps vertex parameters exact dyadic
        ///      rationals, so shared vertices of different-level neighbours are bit-identical; ≤ 128 keeps 16-bit indices).
        /// Post: Indices(m) is non-empty for every m in [0, 16); see type remarks.
        /// Side effects: allocates the 16 buffers once.
        /// </remarks>
        public ChunkStitching(int quadsPerEdge)
        {
            Contract.Requires(quadsPerEdge >= 2 && quadsPerEdge <= 128 && math.ispow2(quadsPerEdge),
                "quadsPerEdge must be a power of two within [2, 128]");
            QuadsPerEdge = quadsPerEdge;
            for (int mask = 0; mask < VariantCount; mask++)
            {
                _variants[mask] = Build(quadsPerEdge, mask);
            }
        }

        /// <summary>Triangle list for <paramref name="coarserNeighbourMask"/>.</summary>
        /// <remarks>
        /// Pre: 0 ≤ coarserNeighbourMask &lt; 16.
        /// Post: length is a multiple of 3; every index &lt; VertexCount; no degenerate triangles. The array is
        ///       shared — callers must not modify it.
        /// Side effects: none.
        /// </remarks>
        public ushort[] Indices(int coarserNeighbourMask)
        {
            Contract.Requires(coarserNeighbourMask >= 0 && coarserNeighbourMask < VariantCount, "mask must be within [0, 16)");
            return _variants[coarserNeighbourMask];
        }

        private static ushort[] Build(int n, int mask)
        {
            int row = n + 1;
            var remap = new int[row * row];
            for (int k = 0; k < remap.Length; k++)
            {
                remap[k] = k;
            }

            for (int k = 1; k < n; k += 2)
            {
                if ((mask & (1 << (int)CubeEdge.UMin)) != 0)
                {
                    remap[k * row] = (k - 1) * row;
                }

                if ((mask & (1 << (int)CubeEdge.UMax)) != 0)
                {
                    remap[k * row + n] = (k - 1) * row + n;
                }

                if ((mask & (1 << (int)CubeEdge.VMin)) != 0)
                {
                    remap[k] = k - 1;
                }

                if ((mask & (1 << (int)CubeEdge.VMax)) != 0)
                {
                    remap[n * row + k] = n * row + k - 1;
                }
            }

            var tris = new System.Collections.Generic.List<ushort>(n * n * 6);
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int v00 = j * row + i;
                    int v10 = v00 + 1;
                    int v01 = v00 + row;
                    int v11 = v01 + 1;
                    Emit(tris, remap[v00], remap[v10], remap[v11]);
                    Emit(tris, remap[v00], remap[v11], remap[v01]);
                }
            }

            return tris.ToArray();
        }

        private static void Emit(System.Collections.Generic.List<ushort> tris, int a, int b, int c)
        {
            if (a == b || b == c || a == c)
            {
                return;
            }

            tris.Add((ushort)a);
            tris.Add((ushort)b);
            tris.Add((ushort)c);
        }
    }
}
