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
    /// - Determinism: the result depends only on the camera position and the previous leaf set, never on timing,
    ///   so every client in a session converges on the same chunk ids for the same camera path.
    /// Not thread-safe; owned by the main thread (mesh building is the job system's part, P1-03).
    /// REQ: docs/architecture/planet-tech.md#幾何：Cube-sphere 四叉樹
    /// </remarks>
    internal sealed class ChunkQuadtree
    {
        private readonly PlanetConfig _config;
        private readonly ChunkLodSettings _settings;

        // Leaf key → coarser-neighbour mask (bit (int)CubeEdge set when the leaf across that edge is one level up).
        private Dictionary<ulong, byte> _leaves = new Dictionary<ulong, byte>();
        private Dictionary<ulong, byte> _target = new Dictionary<ulong, byte>();
        private readonly List<ulong> _work = new List<ulong>();
        private readonly List<ulong> _scratch = new List<ulong>();

        private static readonly IComparer<ChunkId> ByKey = new KeyComparer();

        /// <summary>Creates an empty tree; the first <see cref="Update"/> populates it.</summary>
        /// <remarks>
        /// Pre: config.IsValid; settings.IsValid.
        /// Post: LeafCount == 0.
        /// Side effects: allocates the leaf buffers.
        /// </remarks>
        public ChunkQuadtree(in PlanetConfig config, in ChunkLodSettings settings)
        {
            Contract.Requires(config.IsValid, "PlanetConfig is default/uninitialised");
            Contract.Requires(settings.IsValid, "ChunkLodSettings is default/uninitialised");
            _config = config;
            _settings = settings;
        }

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

        private sealed class KeyComparer : IComparer<ChunkId>
        {
            public int Compare(ChunkId a, ChunkId b) => a.Key.CompareTo(b.Key);
        }
    }
}
