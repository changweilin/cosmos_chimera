using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.Planet.Model
{
    /// <summary>Tunables for <see cref="ChunkQuadtree"/> split/merge decisions.</summary>
    /// <remarks>
    /// Pre: construct through the constructor or <see cref="ForPlanet"/>; <c>default</c> is invalid.
    /// Post: 0 ≤ MaxLevel ≤ ChunkId.MaxLevel; QuadsPerEdge ≥ 1; SplitDistanceFactor &gt; 0; MergeHysteresis ≥ 0; HeightMargin ≥ 0.
    /// Side effects: none (immutable value).
    /// </remarks>
    internal readonly struct ChunkLodSettings
    {
        /// <summary>Deepest level the tree may reach.</summary>
        public readonly int MaxLevel;

        /// <summary>Quads along one side of every leaf mesh (vertices = QuadsPerEdge + 1).</summary>
        public readonly int QuadsPerEdge;

        /// <summary>A node splits when the camera is closer than this × its edge length.</summary>
        public readonly float SplitDistanceFactor;

        /// <summary>A split node merges only beyond (1 + MergeHysteresis) × the split distance.</summary>
        public readonly float MergeHysteresis;

        /// <summary>Largest |terrain height| in metres; inflates node bounds so displaced terrain is never under-refined.</summary>
        public readonly float HeightMargin;

        /// <summary>Creates settings.</summary>
        /// <remarks>
        /// Pre: 0 ≤ maxLevel ≤ ChunkId.MaxLevel; quadsPerEdge ≥ 1; splitDistanceFactor finite and &gt; 0;
        ///      mergeHysteresis finite and ≥ 0; heightMargin finite and ≥ 0.
        /// Post: fields equal the arguments.
        /// Side effects: none.
        /// </remarks>
        public ChunkLodSettings(int maxLevel, int quadsPerEdge, float splitDistanceFactor, float mergeHysteresis, float heightMargin)
        {
            Contract.Requires(maxLevel >= 0 && maxLevel <= ChunkId.MaxLevel, "maxLevel must be within [0, ChunkId.MaxLevel]");
            Contract.Requires(quadsPerEdge >= 1, "quadsPerEdge must be >= 1");
            Contract.Requires(math.isfinite(splitDistanceFactor) && splitDistanceFactor > 0f, "splitDistanceFactor must be > 0");
            Contract.Requires(math.isfinite(mergeHysteresis) && mergeHysteresis >= 0f, "mergeHysteresis must be >= 0");
            Contract.Requires(math.isfinite(heightMargin) && heightMargin >= 0f, "heightMargin must be >= 0");
            MaxLevel = maxLevel;
            QuadsPerEdge = quadsPerEdge;
            SplitDistanceFactor = splitDistanceFactor;
            MergeHysteresis = mergeHysteresis;
            HeightMargin = heightMargin;
        }

        /// <summary>True for a constructed value.</summary>
        public bool IsValid => QuadsPerEdge >= 1;

        /// <summary>Settings whose finest leaves have vertex spacing ≤ <paramref name="finestVertexSpacing"/> on this planet.</summary>
        /// <remarks>
        /// Pre: config.IsValid; finestVertexSpacing finite and &gt; 0; other arguments as in the constructor.
        /// Post: NodeEdgeLength(config, MaxLevel) / QuadsPerEdge ≤ finestVertexSpacing, unless MaxLevel was clamped
        ///       to ChunkId.MaxLevel; MaxLevel is the smallest level meeting that.
        /// Side effects: none.
        /// REQ: CLAUDE.md#硬性規則 — depth is derived from PlanetConfig.Radius, so R=320/640/1280 need no retuning (P1-10).
        /// </remarks>
        public static ChunkLodSettings ForPlanet(in PlanetConfig config, float finestVertexSpacing, float heightMargin,
            int quadsPerEdge = 32, float splitDistanceFactor = 2f, float mergeHysteresis = 0.25f)
        {
            Contract.Requires(config.IsValid, "PlanetConfig is default/uninitialised");
            Contract.Requires(math.isfinite(finestVertexSpacing) && finestVertexSpacing > 0f, "finestVertexSpacing must be > 0");
            Contract.Requires(quadsPerEdge >= 1, "quadsPerEdge must be >= 1");
            int level = 0;
            while (level < ChunkId.MaxLevel && NodeEdgeLength(config, level) / quadsPerEdge > finestVertexSpacing)
            {
                level++;
            }

            return new ChunkLodSettings(level, quadsPerEdge, splitDistanceFactor, mergeHysteresis, heightMargin);
        }

        /// <summary>Upper bound on the ground length of one side of a level-<paramref name="level"/> node.</summary>
        /// <remarks>
        /// Pre: config.IsValid; 0 ≤ level ≤ ChunkId.MaxLevel.
        /// Post: result &gt; 0; halves with each level.
        /// Side effects: none.
        /// A face spans π/2 radians along u at v = 0 and less elsewhere, so R·(π/2)/2^level is the longest side.
        /// </remarks>
        public static float NodeEdgeLength(in PlanetConfig config, int level)
        {
            Contract.Requires(config.IsValid, "PlanetConfig is default/uninitialised");
            Contract.Requires(level >= 0 && level <= ChunkId.MaxLevel, "level must be within [0, ChunkId.MaxLevel]");
            return config.Radius * (math.PI * 0.5f) / (1 << level);
        }
    }
}
