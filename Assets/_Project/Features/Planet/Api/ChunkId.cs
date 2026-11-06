using System;
using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.Planet
{
    /// <summary>Address of one cube-sphere quadtree node: face, level and integer cell (X along u, Y along v).</summary>
    /// <remarks>
    /// Pre: construct through the constructor or navigation methods; <c>default(ChunkId)</c> is the PosX root.
    /// Post: 0 ≤ Level ≤ <see cref="MaxLevel"/>; 0 ≤ X, Y &lt; 2^Level.
    /// Side effects: none (immutable value).
    /// <see cref="Key"/> packs the id into 64 bits (stable across sessions/clients: safe for save data and packets).
    /// Layout: face[63..61] level[60..56] x[55..28] y[27..0].
    /// </remarks>
    public readonly struct ChunkId : IEquatable<ChunkId>
    {
        /// <summary>Deepest supported level.</summary>
        /// <remarks>
        /// Pre: none. Post: constant. Side effects: none.
        /// At level 20 a node spans 2/2^20 ≈ 1.9e-6 in u, about 16 float ulps near |u| = 1; deeper levels would
        /// make neighbouring vertices collapse. R = 1280 m at 1 m vertex spacing needs only level 6.
        /// </remarks>
        public const int MaxLevel = 20;

        /// <summary>Packed 64-bit form.</summary>
        /// <remarks>Pre: none. Post: two ids are equal iff their keys are equal. Side effects: none.</remarks>
        public readonly ulong Key;

        /// <summary>Creates an id.</summary>
        /// <remarks>
        /// Pre: face defined; 0 ≤ level ≤ MaxLevel; 0 ≤ x, y &lt; 2^level.
        /// Post: Face/Level/X/Y equal the arguments.
        /// Side effects: none.
        /// </remarks>
        public ChunkId(CubeFace face, int level, int x, int y)
        {
            Contract.Requires((uint)face < CubeSphere.FaceCount, "face must be a defined CubeFace");
            Contract.Requires(level >= 0 && level <= MaxLevel, "level must be within [0, MaxLevel]");
            Contract.Requires(x >= 0 && y >= 0 && x < (1 << level) && y < (1 << level), "x, y must be within [0, 2^level)");
            Key = ((ulong)face << 61) | ((ulong)level << 56) | ((ulong)(uint)x << 28) | (ulong)(uint)y;
        }

        private ChunkId(ulong key)
        {
            Key = key;
        }

        /// <summary>Rebuilds an id from <see cref="Key"/>.</summary>
        /// <remarks>
        /// Pre: key was produced by <see cref="Key"/> (fields in range).
        /// Post: FromKey(id.Key) == id.
        /// Side effects: none.
        /// </remarks>
        public static ChunkId FromKey(ulong key)
        {
            var id = new ChunkId(key);
            Contract.Requires((uint)id.Face < CubeSphere.FaceCount && id.Level <= MaxLevel
                && id.X < (1 << id.Level) && id.Y < (1 << id.Level), "key fields out of range");
            return id;
        }

        /// <summary>Root node (level 0) of <paramref name="face"/>.</summary>
        /// <remarks>Pre: face defined. Post: Level == 0. Side effects: none.</remarks>
        public static ChunkId Root(CubeFace face) => new ChunkId(face, 0, 0, 0);

        /// <summary>Cube face.</summary>
        /// <remarks>Pre: none. Post: defined CubeFace. Side effects: none.</remarks>
        public CubeFace Face => (CubeFace)(Key >> 61);

        /// <summary>Depth in the face quadtree; 0 is the whole face.</summary>
        /// <remarks>Pre: none. Post: within [0, MaxLevel]. Side effects: none.</remarks>
        public int Level => (int)((Key >> 56) & 0x1FUL);

        /// <summary>Cell index along u.</summary>
        /// <remarks>Pre: none. Post: within [0, 2^Level). Side effects: none.</remarks>
        public int X => (int)((Key >> 28) & 0x0FFFFFFFUL);

        /// <summary>Cell index along v.</summary>
        /// <remarks>Pre: none. Post: within [0, 2^Level). Side effects: none.</remarks>
        public int Y => (int)(Key & 0x0FFFFFFFUL);

        /// <summary>Number of cells per face side at this level (2^Level).</summary>
        /// <remarks>Pre: none. Post: ≥ 1. Side effects: none.</remarks>
        public int CellsPerSide => 1 << Level;

        /// <summary>Parent node.</summary>
        /// <remarks>Pre: Level &gt; 0. Post: result.Level == Level − 1 and result contains this node. Side effects: none.</remarks>
        public ChunkId Parent()
        {
            Contract.Requires(Level > 0, "a root has no parent");
            return new ChunkId(Face, Level - 1, X >> 1, Y >> 1);
        }

        /// <summary>Child quadrant: bit 0 of <paramref name="quadrant"/> selects +u, bit 1 selects +v.</summary>
        /// <remarks>
        /// Pre: Level &lt; MaxLevel; 0 ≤ quadrant ≤ 3.
        /// Post: result.Parent() == this.
        /// Side effects: none.
        /// </remarks>
        public ChunkId Child(int quadrant)
        {
            Contract.Requires(Level < MaxLevel, "already at MaxLevel");
            Contract.Requires(quadrant >= 0 && quadrant <= 3, "quadrant must be within [0, 3]");
            return new ChunkId(Face, Level + 1, (X << 1) | (quadrant & 1), (Y << 1) | (quadrant >> 1));
        }

        /// <summary>True if <paramref name="descendant"/> is this node or lies inside it.</summary>
        /// <remarks>Pre: none. Post: Contains(this) is true. Side effects: none.</remarks>
        public bool Contains(ChunkId descendant)
        {
            int shift = descendant.Level - Level;
            return descendant.Face == Face && shift >= 0
                && (descendant.X >> shift) == X && (descendant.Y >> shift) == Y;
        }

        /// <summary>Lower and upper face parameters (u, v) covered by this node.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: −1 ≤ min &lt; max ≤ 1 componentwise; values are exact dyadic rationals (so shared corners of
        ///       neighbouring nodes are bit-identical, which the seam contract in <see cref="CubeSphere"/> relies on).
        /// Side effects: none.
        /// </remarks>
        public void GetBounds(out float2 min, out float2 max)
        {
            float size = 2f / CellsPerSide;
            min = new float2(-1f + X * size, -1f + Y * size);
            max = new float2(-1f + (X + 1) * size, -1f + (Y + 1) * size);
        }

        /// <summary>Same-level node across <paramref name="edge"/>, crossing onto the adjacent face if needed.</summary>
        /// <remarks>
        /// Pre: edge defined.
        /// Post: result.Level == Level; result.Neighbor(back) == this for exactly one edge `back`
        ///       (see <see cref="NeighborBackEdge"/>).
        /// Side effects: none.
        /// </remarks>
        public ChunkId Neighbor(CubeEdge edge)
        {
            return Neighbor(edge, out _);
        }

        /// <summary>Same as <see cref="Neighbor(CubeEdge)"/> and also returns the edge of the result that faces back.</summary>
        /// <remarks>Pre: edge defined. Post: result.Neighbor(backEdge) == this. Side effects: none.</remarks>
        public ChunkId Neighbor(CubeEdge edge, out CubeEdge backEdge)
        {
            Contract.Requires((uint)edge < 4u, "edge must be a defined CubeEdge");
            int n = CellsPerSide;
            int x = X;
            int y = Y;
            switch (edge)
            {
                case CubeEdge.UMin when x > 0:
                    backEdge = CubeEdge.UMax;
                    return new ChunkId(Face, Level, x - 1, y);
                case CubeEdge.UMax when x < n - 1:
                    backEdge = CubeEdge.UMin;
                    return new ChunkId(Face, Level, x + 1, y);
                case CubeEdge.VMin when y > 0:
                    backEdge = CubeEdge.VMax;
                    return new ChunkId(Face, Level, x, y - 1);
                case CubeEdge.VMax when y < n - 1:
                    backEdge = CubeEdge.VMin;
                    return new ChunkId(Face, Level, x, y + 1);
            }

            CubeEdgeLink link = CubeSphere.Adjacent(Face, edge);
            int along = CubeSphere.RunsAlongU(edge) ? x : y;
            if (link.Reversed)
            {
                along = n - 1 - along;
            }

            backEdge = link.Edge;
            switch (link.Edge)
            {
                case CubeEdge.UMin: return new ChunkId(link.Face, Level, 0, along);
                case CubeEdge.UMax: return new ChunkId(link.Face, Level, n - 1, along);
                case CubeEdge.VMin: return new ChunkId(link.Face, Level, along, 0);
                default: return new ChunkId(link.Face, Level, along, n - 1);
            }
        }

        /// <summary>The edge of <c>Neighbor(edge)</c> that touches this node.</summary>
        /// <remarks>Pre: edge defined. Post: Neighbor(edge).Neighbor(result) == this. Side effects: none.</remarks>
        public CubeEdge NeighborBackEdge(CubeEdge edge)
        {
            Neighbor(edge, out CubeEdge back);
            return back;
        }

        /// <summary>The two children of this node that touch <paramref name="edge"/>, in along-edge order.</summary>
        /// <remarks>
        /// Pre: Level &lt; MaxLevel; edge defined.
        /// Post: both results are children of this node lying on that edge.
        /// Side effects: none.
        /// </remarks>
        public void ChildrenOnEdge(CubeEdge edge, out ChunkId first, out ChunkId second)
        {
            Contract.Requires((uint)edge < 4u, "edge must be a defined CubeEdge");
            switch (edge)
            {
                case CubeEdge.UMin:
                    first = Child(0);
                    second = Child(2);
                    break;
                case CubeEdge.UMax:
                    first = Child(1);
                    second = Child(3);
                    break;
                case CubeEdge.VMin:
                    first = Child(0);
                    second = Child(1);
                    break;
                default:
                    first = Child(2);
                    second = Child(3);
                    break;
            }
        }

        /// <inheritdoc/>
        public bool Equals(ChunkId other) => Key == other.Key;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ChunkId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Key.GetHashCode();

        /// <summary>Value equality on <see cref="Key"/>.</summary>
        /// <remarks>Pre: none. Post: a == b iff a.Key == b.Key. Side effects: none.</remarks>
        public static bool operator ==(ChunkId a, ChunkId b) => a.Key == b.Key;

        /// <summary>Value inequality on <see cref="Key"/>.</summary>
        /// <remarks>Pre: none. Post: a != b iff a.Key != b.Key. Side effects: none.</remarks>
        public static bool operator !=(ChunkId a, ChunkId b) => a.Key != b.Key;

        /// <inheritdoc/>
        public override string ToString() => $"{Face}/L{Level}/{X},{Y}";
    }
}
