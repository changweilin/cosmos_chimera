using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.Planet.Model
{
    /// <summary>Camera-driven cube-sphere LOD: the current set of leaf chunks and how it changes each update.</summary>
    /// <remarks>
    /// Invariants after every <see cref="Update"/> (all checked by ChunkQuadtreeTests):
    /// - Coverage: the leaves tile all six faces exactly once (no gaps, no overlaps).
    /// - 2:1 balance: two leaves sharing an edge — including across cube faces — differ by at most one level, so a
    ///   leaf's edge needs at most one stitching pattern (<see cref="TryGetLeaf"/> mask) to close T-junction cracks.
    /// - Determinism: the result depends only on the camera position and the previous leaf set, never on timing or
    ///   hash/iteration order. That is a same-process, same-backend guarantee only: split tests compare floats
    ///   (tan, sqrt), and a camera one float ulp away can flip a split (sol review F5). LOD is per-client
    ///   presentation — each client follows its own camera — and is NOT part of the world-generation
    ///   reproducibility contract (seed + GeneratorVersion); never derive gameplay or network state from leaf ids.
    /// - Cost: every Update rebuilds the leaf set from the six roots, O(leaves · MaxLevel). The constructor's
    ///   preconditions keep leaves local to the camera (≈ 450 at R = 640 m with defaults).
    /// Not thread-safe; owned by the main thread (mesh building is the job system's part, P1-03).
    /// REQ: docs/architecture/planet-tech.md#幾何：Cube-sphere 四叉樹
    /// </remarks>
    internal sealed class ChunkQuadtree
    {
        private readonly PlanetConfig _config;
        private readonly ChunkLodSettings _settings;

        // Leaf key → coarser-neighbour mask (bit (int)CubeEdge set when the leaf across that edge is one level up).
        // Explicit comparer: the default ulong hash (hi ^ lo) collides systematically for packed keys (sol review F4).
        private Dictionary<ulong, byte> _leaves = new Dictionary<ulong, byte>(KeyEquality.Instance);
        private Dictionary<ulong, byte> _target = new Dictionary<ulong, byte>(KeyEquality.Instance);
        private readonly List<ulong> _work = new List<ulong>();
        private readonly List<ulong> _scratch = new List<ulong>();

        private static readonly IComparer<ChunkId> ByKey = new KeyComparer();

        /// <summary>Creates an empty tree; the first <see cref="Update"/> populates it.</summary>
        /// <remarks>
        /// Pre: config.IsValid; settings.IsValid; settings.HeightMargin &lt; 0.5 · config.Radius;
        ///      settings.HeightMargin ≤ MaxMarginInFinestEdges · NodeEdgeLength(config, settings.MaxLevel);
        ///      settings.SplitDistanceFactor ≤ MaxSplitDistanceFactor.
        /// Post: LeafCount == 0.
        /// Side effects: allocates the leaf buffers.
        /// REQ: sol review F4 — HeightMargin ≥ Radius makes every node's distance bound 0, so the whole sphere splits
        /// to MaxLevel (6·4^MaxLevel leaves). With the margin below R/2, a node can only be near the camera if it is
        /// near the camera on the sphere, which keeps refinement local; the factor cap bounds the refined radius.
        /// REQ: docs/reviews/P1-review-r2-sol.md#A1 — R/2 alone still allowed Θ(4^MaxLevel) leaves: every node within
        /// HeightMargin of the camera has distance bound 0 and splits to MaxLevel, so the finest leaves fill a disc
        /// of radius ≈ HeightMargin. Measuring the margin in finest edges caps that disc independently of depth.
        /// </remarks>
        public ChunkQuadtree(in PlanetConfig config, in ChunkLodSettings settings)
        {
            Contract.Requires(config.IsValid, "PlanetConfig is default/uninitialised");
            Contract.Requires(settings.IsValid, "ChunkLodSettings is default/uninitialised");
            Contract.Requires(Supports(config, settings),
                "settings outside the supported workload domain (HeightMargin, SplitDistanceFactor); see Supports");
            _config = config;
            _settings = settings;
        }

        /// <summary>True when a tree with these settings keeps its leaf count bounded independently of MaxLevel.</summary>
        /// <remarks>
        /// Pre: config.IsValid; settings.IsValid.
        /// Post: result == HeightMargin &lt; 0.5·Radius ∧ HeightMargin ≤ MaxMarginInFinestEdges·NodeEdgeLength(MaxLevel)
        ///       ∧ SplitDistanceFactor ≤ MaxSplitDistanceFactor. The constructor requires it; owners that derive settings
        ///       (PlanetTerrain.Configure) check it before mutating state.
        /// Side effects: none.
        /// </remarks>
        public static bool Supports(in PlanetConfig config, in ChunkLodSettings settings)
        {
            Contract.Requires(config.IsValid, "PlanetConfig is default/uninitialised");
            Contract.Requires(settings.IsValid, "ChunkLodSettings is default/uninitialised");
            return settings.HeightMargin < 0.5f * config.Radius
                && settings.HeightMargin <= MaxMarginInFinestEdges * ChunkLodSettings.NodeEdgeLength(config, settings.MaxLevel)
                && settings.SplitDistanceFactor <= MaxSplitDistanceFactor;
        }

        /// <summary>Largest accepted <see cref="ChunkLodSettings.SplitDistanceFactor"/>.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none. Leaves per level grow ≈ with factor², so 8 is 16× the default 2.</remarks>
        public const float MaxSplitDistanceFactor = 8f;

        /// <summary>Largest accepted HeightMargin, in edges of a MaxLevel node.</summary>
        /// <remarks>
        /// Pre: none. Post: constant. Side effects: none.
        /// The always-split disc then holds ≈ π·16² ≈ 800 finest leaves whatever MaxLevel is. Defaults at R = 640 m
        /// use ≈ 1.2 edges (37.8 m margin, 31.4 m edges). A deeper tree with tall terrain needs per-node height
        /// bounds instead of one global margin (not built yet).
        /// </remarks>
        public const float MaxMarginInFinestEdges = 16f;

        /// <summary>Comparer used by the leaf dictionaries; exposed for the hash-distribution regression test.</summary>
        /// <remarks>Pre: none. Post: both internal dictionaries use exactly this comparer. Side effects: none.</remarks>
        internal IEqualityComparer<ulong> LeafKeyComparer => _leaves.Comparer;

        /// <summary>Comparer of the scratch dictionary that becomes the leaf set on the next Update.</summary>
        /// <remarks>Pre: none. Post: see <see cref="LeafKeyComparer"/>. Side effects: none.</remarks>
        internal IEqualityComparer<ulong> TargetKeyComparer => _target.Comparer;

        /// <summary>Number of current leaves.</summary>
        /// <remarks>Pre: none. Post: 0 before the first Update, ≥ 6 after. Side effects: none.</remarks>
        public int LeafCount => _leaves.Count;

        /// <summary>Settings this tree was built with.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public ChunkLodSettings Settings => _settings;

        /// <summary>Recomputes the leaf set for <paramref name="cameraWorldPosition"/> and reports the difference.</summary>
        /// <remarks>
        /// Pre: cameraWorldPosition is finite; the three lists are non-null and distinct.
        /// Post: the lists are cleared, then hold (sorted by Key) the leaves that appeared, disappeared, and stayed
        ///       but changed coarser-neighbour mask. Old set − removed + added == new set. Type invariants hold.
        ///       Calling again with the same position reports no changes.
        /// Side effects: replaces the leaf set; writes the three lists. Allocation-free once buffers have grown.
        /// </remarks>
        public void Update(float3 cameraWorldPosition, List<ChunkId> added, List<ChunkId> removed, List<ChunkId> maskChanged)
        {
            Contract.Requires(math.all(math.isfinite(cameraWorldPosition)), "camera position must be finite");
            Contract.Requires(added != null && removed != null && maskChanged != null, "output lists must be non-null");
            Contract.Requires(!ReferenceEquals(added, removed) && !ReferenceEquals(added, maskChanged)
                && !ReferenceEquals(removed, maskChanged), "output lists must be distinct");

            added.Clear();
            removed.Clear();
            maskChanged.Clear();

            _target.Clear();
            for (int f = 0; f < CubeSphere.FaceCount; f++)
            {
                Refine(ChunkId.Root((CubeFace)f), cameraWorldPosition);
            }

            Balance();
            ComputeMasks();

            foreach (KeyValuePair<ulong, byte> old in _leaves)
            {
                if (!_target.TryGetValue(old.Key, out byte mask))
                {
                    removed.Add(ChunkId.FromKey(old.Key));
                }
                else if (mask != old.Value)
                {
                    maskChanged.Add(ChunkId.FromKey(old.Key));
                }
            }

            foreach (KeyValuePair<ulong, byte> now in _target)
            {
                if (!_leaves.ContainsKey(now.Key))
                {
                    added.Add(ChunkId.FromKey(now.Key));
                }
            }

            // Sorted so downstream mesh scheduling sees the same order on every client and every run.
            added.Sort(ByKey);
            removed.Sort(ByKey);
            maskChanged.Sort(ByKey);

            Dictionary<ulong, byte> swap = _leaves;
            _leaves = _target;
            _target = swap;
        }

        /// <summary>Looks up a current leaf.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: true iff id is a current leaf; then coarserNeighbourMask has bit (int)edge set for each edge whose
        ///       neighbour leaf is exactly one level coarser (never more, by 2:1 balance).
        /// Side effects: none.
        /// </remarks>
        public bool TryGetLeaf(ChunkId id, out byte coarserNeighbourMask)
        {
            return _leaves.TryGetValue(id.Key, out coarserNeighbourMask);
        }

        /// <summary>The current leaf containing <paramref name="direction"/>.</summary>
        /// <remarks>
        /// Pre: LeafCount &gt; 0; direction finite and non-zero.
        /// Post: result is a current leaf whose (u, v) bounds contain the direction's face parameters.
        /// Side effects: none.
        /// </remarks>
        public ChunkId FindLeaf(float3 direction)
        {
            Contract.Requires(_leaves.Count > 0, "Update has not run yet");
            CubeFace face = CubeSphere.DirectionToFace(direction, out float u, out float v);
            for (int level = 0; level <= _settings.MaxLevel; level++)
            {
                int n = 1 << level;
                int x = math.clamp((int)math.floor((u + 1f) * 0.5f * n), 0, n - 1);
                int y = math.clamp((int)math.floor((v + 1f) * 0.5f * n), 0, n - 1);
                var id = new ChunkId(face, level, x, y);
                if (_leaves.ContainsKey(id.Key))
                {
                    return id;
                }
            }

            throw new ContractException("Postcondition failed: leaves do not cover the sphere");
        }

        /// <summary>Copies the current leaves into <paramref name="destination"/>, sorted by Key.</summary>
        /// <remarks>
        /// Pre: destination non-null.
        /// Post: destination.Count == LeafCount.
        /// Side effects: clears and fills destination.
        /// </remarks>
        public void CopyLeaves(List<ChunkId> destination)
        {
            Contract.Requires(destination != null, "destination must be non-null");
            destination.Clear();
            foreach (ulong key in _leaves.Keys)
            {
                destination.Add(ChunkId.FromKey(key));
            }

            destination.Sort(ByKey);
        }

        private void Refine(ChunkId node, float3 camera)
        {
            if (node.Level < _settings.MaxLevel && WantsSplit(node, camera))
            {
                for (int q = 0; q < 4; q++)
                {
                    Refine(node.Child(q), camera);
                }

                return;
            }

            _target[node.Key] = 0;
        }

        private bool WantsSplit(ChunkId node, float3 camera)
        {
            float distance = DistanceToNode(node, camera);
            float threshold = _settings.SplitDistanceFactor * ChunkLodSettings.NodeEdgeLength(_config, node.Level);
            // Hysteresis: a node that is split now only merges once the camera is clearly past the split distance,
            // so hovering on the boundary does not rebuild meshes every frame.
            if (IsCurrentlySplit(node))
            {
                threshold *= 1f + _settings.MergeHysteresis;
            }

            return distance < threshold;
        }

        private bool IsCurrentlySplit(ChunkId node)
        {
            if (_leaves.Count == 0 || _leaves.ContainsKey(node.Key))
            {
                return false;
            }

            ChunkId cursor = node;
            while (cursor.Level > 0)
            {
                cursor = cursor.Parent();
                if (_leaves.ContainsKey(cursor.Key))
                {
                    return false;
                }
            }

            return true;
        }

        private float DistanceToNode(ChunkId node, float3 camera)
        {
            node.GetBounds(out float2 min, out float2 max);
            float2 mid = (min + max) * 0.5f;
            CubeFace face = node.Face;
            float r = _config.Radius;
            float3 center = CubeSphere.FaceToDirection(face, mid.x, mid.y) * r;
            float bound = math.max(
                math.max(math.distance(center, CubeSphere.FaceToDirection(face, min.x, min.y) * r),
                         math.distance(center, CubeSphere.FaceToDirection(face, max.x, min.y) * r)),
                math.max(math.distance(center, CubeSphere.FaceToDirection(face, min.x, max.y) * r),
                         math.distance(center, CubeSphere.FaceToDirection(face, max.x, max.y) * r)));
            // Corner distance underestimates the bulge of the curved patch by at most the sagitta, which is below
            // the height margin at every level that can split near the camera; the margin covers both.
            bound += _settings.HeightMargin;
            return math.max(0f, math.distance(camera - _config.Center, center) - bound);
        }

        // Splits coarse leaves until no two edge-adjacent leaves differ by more than one level. Only ever splits,
        // so it cannot undo a refinement the camera asked for.
        private void Balance()
        {
            _work.Clear();
            foreach (ulong key in _target.Keys)
            {
                _work.Add(key);
            }

            while (_work.Count > 0)
            {
                ulong key = _work[_work.Count - 1];
                _work.RemoveAt(_work.Count - 1);
                if (!_target.ContainsKey(key))
                {
                    continue; // Already split by an earlier step.
                }

                ChunkId leaf = ChunkId.FromKey(key);
                if (leaf.Level < 2)
                {
                    continue; // Any neighbour is at level ≥ 0, so a level-0/1 leaf can never be 2+ levels finer.
                }

                for (int e = 0; e < 4; e++)
                {
                    ChunkId cover = CoveringLeaf(leaf.Neighbor((CubeEdge)e), out bool found);
                    if (!found || cover.Level >= leaf.Level - 1)
                    {
                        continue;
                    }

                    _target.Remove(cover.Key);
                    for (int q = 0; q < 4; q++)
                    {
                        ulong child = cover.Child(q).Key;
                        _target[child] = 0;
                        _work.Add(child);
                    }

                    // The split child may still be too coarse for `leaf`; re-check it after the children.
                    _work.Add(key);
                    break;
                }
            }
        }

        // The target leaf that is `node` or an ancestor of it; found == false when `node` is subdivided further.
        private ChunkId CoveringLeaf(ChunkId node, out bool found)
        {
            ChunkId cursor = node;
            while (true)
            {
                if (_target.ContainsKey(cursor.Key))
                {
                    found = true;
                    return cursor;
                }

                if (cursor.Level == 0)
                {
                    found = false;
                    return node;
                }

                cursor = cursor.Parent();
            }
        }

        private void ComputeMasks()
        {
            _scratch.Clear();
            foreach (ulong key in _target.Keys)
            {
                _scratch.Add(key);
            }

            foreach (ulong key in _scratch)
            {
                ChunkId leaf = ChunkId.FromKey(key);
                byte mask = 0;
                if (leaf.Level > 0)
                {
                    for (int e = 0; e < 4; e++)
                    {
                        ChunkId neighbour = leaf.Neighbor((CubeEdge)e);
                        if (!_target.ContainsKey(neighbour.Key) && _target.ContainsKey(neighbour.Parent().Key))
                        {
                            mask |= (byte)(1 << e);
                        }
                    }
                }

                _target[key] = mask;
            }
        }

        private sealed class KeyEquality : IEqualityComparer<ulong>
        {
            public static readonly KeyEquality Instance = new KeyEquality();

            public bool Equals(ulong a, ulong b) => a == b;

            public int GetHashCode(ulong key) => ChunkId.MixHash(key);
        }

        private sealed class KeyComparer : IComparer<ChunkId>
        {
            public int Compare(ChunkId a, ChunkId b) => a.Key.CompareTo(b.Key);
        }
    }
}
