using System.Collections.Generic;
using System.Diagnostics;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet.Model;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Planet
{
    /// <summary>Renders the planet surface as LOD leaf meshes around a viewer and keeps colliders only near it.</summary>
    /// <remarks>
    /// Pre: call <see cref="Configure"/> once before the first LateUpdate (composition root injects it; no scene lookup).
    /// Post: after each LateUpdate either the shown leaves equal the quadtree's leaves of some recent frame, or a
    ///       build batch is in flight and the previous complete set is still shown (never a partial set → no holes).
    /// Side effects: owns chunk GameObjects (children), meshes, native buffers and worker jobs.
    /// Frame model: quadtree diff → one batch of Burst jobs → next frame(s): apply the whole batch atomically.
    /// Colliders: baked off-thread (Physics.BakeMesh) for leaves whose bounds are within ColliderRange.
    /// REQ: docs/roadmap/phase-1.md#P1-03 (主執行緒每幀 &lt; 2 ms；碰撞只存在 ~128 m 內)
    /// </remarks>
    public sealed class PlanetTerrain : MonoBehaviour
    {
        private sealed class Chunk
        {
            public ChunkId Id;
            public GameObject Go;
            public Mesh Mesh;
            public MeshCollider Collider;
            public float3 BoundsMin;
            public float3 BoundsMax;
            public bool ColliderReady;
        }

        private struct BakeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<EntityId> MeshIds;

            public void Execute(int index) => Physics.BakeMesh(MeshIds[index], false);
        }

        private PlanetConfig _config;
        private PlanetHeight _height;
        private TemplateView _templates;
        private Transform _viewer;
        private Material _material;
        private float _colliderRange;
        private ChunkQuadtree _tree;
        private ChunkStitching _stitching;
        private NativeArray<ushort> _allIndices;
        private readonly int2[] _variantRanges = new int2[ChunkStitching.VariantCount];

        private readonly Dictionary<ulong, Chunk> _shown = new Dictionary<ulong, Chunk>();
        private readonly Stack<Chunk> _pool = new Stack<Chunk>();
        private readonly List<ChunkId> _added = new List<ChunkId>();
        private readonly List<ChunkId> _removed = new List<ChunkId>();
        private readonly List<ChunkId> _maskChanged = new List<ChunkId>();

        // In-flight build batch.
        private bool _building;
        private JobHandle _buildHandle;
        private Mesh.MeshDataArray _meshData;
        private NativeArray<float3x2> _bounds;
        private NativeArray<ChunkGeometry> _geometries;
        private NativeArray<int2> _indexRanges;
        private readonly List<ChunkId> _batchBuild = new List<ChunkId>();
        private readonly List<ChunkId> _batchRemove = new List<ChunkId>();
        private readonly List<Chunk> _batchChunks = new List<Chunk>();
        private readonly List<Mesh> _batchMeshes = new List<Mesh>();

        // In-flight collider bake.
        private bool _baking;
        private JobHandle _bakeHandle;
        private NativeArray<EntityId> _bakeIds;
        private readonly List<Chunk> _bakeChunks = new List<Chunk>();

        private readonly Stopwatch _stopwatch = new Stopwatch();

        /// <summary>Main-thread milliseconds spent in the last LateUpdate (excludes worker time).</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public double LastFrameMs { get; private set; }

        /// <summary>True when no build batch is in flight and the last tree update produced no changes.</summary>
        /// <remarks>Pre: none. Post: see summary. Side effects: none.</remarks>
        public bool IsSettled { get; private set; }

        /// <summary>Number of leaf meshes currently shown.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public int ShownCount => _shown.Count;

        /// <summary>Range (metres, from the viewer to a leaf's bounds) inside which leaves get colliders.</summary>
        /// <remarks>Pre: none. Post: &gt; 0 after Configure. Side effects: none.</remarks>
        public float ColliderRange => _colliderRange;

        /// <summary>True while no field set by Configure has been written (test hook for "rejects before any state changes").</summary>
        /// <remarks>
        /// Pre: none. Post: true from construction until Configure passes its preconditions. Side effects: none.
        /// REQ: docs/reviews/P1-review-r4-sol.md#N3 — the transform alone is written last, so it could not show a
        /// precondition that was deleted or moved after the first field assignment.
        /// </remarks>
        internal bool IsUntouchedByConfigure =>
            !_config.IsValid && !_height.IsValid && _viewer == null && _material == null && _colliderRange == 0f
            && _stitching == null && _tree == null;

        /// <summary>Injects the planet and viewer; must be called once.</summary>
        /// <remarks>
        /// Pre: not configured yet; config.IsValid; height.IsValid; viewer non-null; colliderRange finite and &gt; 0;
        ///      quadsPerEdge a power of two within [2, 128]; the derived LOD settings (1 m finest spacing, margin =
        ///      height.MaxAbsHeight) satisfy ChunkQuadtree.Supports — false for small quadsPerEdge on large or tall
        ///      planets (e.g. R = 640 with q = 2, R = 8000 with q = 32), which would need per-node height bounds.
        /// Post: this transform sits at config.Center with identity rotation/scale (leaf vertices are planet-local).
        /// Side effects: moves this transform; allocates the persistent index buffer holding all 16 variants.
        /// </remarks>
        public void Configure(in PlanetConfig config, in PlanetHeight height, Transform viewer, Material material,
            float colliderRange = 128f, int quadsPerEdge = 32) =>
            Configure(config, height, viewer, material, default, 0f, colliderRange, quadsPerEdge);

        /// <summary>As the other overload, with geo-templates stamped over the noise (P3-03).</summary>
        /// <remarks>
        /// Pre: as the other overload; templates default or the View of a live PlanetTemplates that outlives this
        ///      terrain; templateMarginM == that PlanetTemplates.MaxAbsContribution (0 for none), added to the LOD
        ///      height margin so culling/splitting stay conservative.
        /// Post/Side effects: as the other overload.
        /// </remarks>
        public void Configure(in PlanetConfig config, in PlanetHeight height, Transform viewer, Material material,
            TemplateView templates, float templateMarginM, float colliderRange = 128f, int quadsPerEdge = 32)
        {
            Contract.Requires(math.isfinite(templateMarginM) && templateMarginM >= 0f, "templateMarginM must be >= 0");
            Contract.Requires(_tree == null, "PlanetTerrain is already configured");
            Contract.Requires(config.IsValid, "PlanetConfig is default/uninitialised");
            Contract.Requires(height.IsValid, "PlanetHeight is default/uninitialised");
            Contract.Requires(viewer != null, "viewer must be non-null");
            Contract.Requires(math.isfinite(colliderRange) && colliderRange > 0f, "colliderRange must be > 0");
            // Finest vertex spacing 1 m: planet-tech.md#幾何 (R=320/640/1280 → depth 4/5/6).
            ChunkLodSettings lod = ChunkLodSettings.ForPlanet(config, 1f, height.MaxAbsHeight + templateMarginM, quadsPerEdge);
            // Checked before any state changes (sol r3 N1): the quadtree would otherwise throw half-way through Configure.
            Contract.Requires(ChunkQuadtree.Supports(config, lod),
                "terrain is too tall for this radius/quadsPerEdge without per-node height bounds (ChunkQuadtree.Supports)");
            _config = config;
            _height = height;
            _templates = templates;
            _viewer = viewer;
            _material = material;
            _colliderRange = colliderRange;
            _stitching = new ChunkStitching(quadsPerEdge);
            _tree = new ChunkQuadtree(config, lod);
            int total = 0;
            for (int m = 0; m < ChunkStitching.VariantCount; m++)
            {
                _variantRanges[m] = new int2(total, _stitching.Indices(m).Length);
                total += _variantRanges[m].y;
            }

            _allIndices = new NativeArray<ushort>(total, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int m = 0; m < ChunkStitching.VariantCount; m++)
            {
                NativeArray<ushort>.Copy(_stitching.Indices(m), 0, _allIndices, _variantRanges[m].x, _variantRanges[m].y);
            }

            transform.SetPositionAndRotation(config.Center, Quaternion.identity);
            transform.localScale = Vector3.one;
        }

        private void LateUpdate()
        {
            if (_tree == null)
            {
                return;
            }

            _stopwatch.Restart();
            bool changed = false;
            if (_building && _buildHandle.IsCompleted)
            {
                FinishBuild();
            }

            if (!_building)
            {
                _tree.Update(_viewer.position, _added, _removed, _maskChanged);
                changed = _added.Count + _removed.Count + _maskChanged.Count > 0;
                if (changed)
                {
                    StartBuild();
                }
            }

            UpdateColliders();
            IsSettled = !_building && !changed && !_baking;
            _stopwatch.Stop();
            LastFrameMs = _stopwatch.Elapsed.TotalMilliseconds;
        }

        private void StartBuild()
        {
            _batchBuild.Clear();
            _batchBuild.AddRange(_added);
            _batchBuild.AddRange(_maskChanged);
            _batchRemove.Clear();
            _batchRemove.AddRange(_removed);
            int count = _batchBuild.Count;
            if (count == 0)
            {
                ApplyRemovals();
                return;
            }

            _meshData = Mesh.AllocateWritableMeshData(count);
            _bounds = new NativeArray<float3x2>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _geometries = new NativeArray<ChunkGeometry>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _indexRanges = new NativeArray<int2>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int k = 0; k < count; k++)
            {
                ChunkId id = _batchBuild[k];
                _tree.TryGetLeaf(id, out byte mask);
                int2 range = _variantRanges[mask];
                ChunkMeshJob.PrepareOutput(_meshData[k], _stitching.QuadsPerEdge, range.y);
                _geometries[k] = ChunkGeometry.Create(id, _config, _height, _stitching.QuadsPerEdge);
                _indexRanges[k] = range;
            }

            _buildHandle = new ChunkMeshJob
            {
                Geometries = _geometries,
                IndexRanges = _indexRanges,
                AllIndices = _allIndices,
                Output = _meshData,
                Bounds = _bounds,
                Templates = _templates,
            }.Schedule(count, 1);
            JobHandle.ScheduleBatchedJobs();
            _building = true;
        }

        private void FinishBuild()
        {
            _buildHandle.Complete();
            // A bake in flight may read a mesh this batch rewrites.
            CompleteBake();
            _batchChunks.Clear();
            _batchMeshes.Clear();
            for (int k = 0; k < _batchBuild.Count; k++)
            {
                ChunkId id = _batchBuild[k];
                if (!_shown.TryGetValue(id.Key, out Chunk chunk))
                {
                    chunk = Rent(id);
                }

                _batchChunks.Add(chunk);
                _batchMeshes.Add(chunk.Mesh);
            }

            Mesh.ApplyAndDisposeWritableMeshData(_meshData, _batchMeshes,
                UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds | UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices);
            for (int k = 0; k < _batchChunks.Count; k++)
            {
                Chunk chunk = _batchChunks[k];
                chunk.BoundsMin = _bounds[k].c0;
                chunk.BoundsMax = _bounds[k].c1;
                chunk.Mesh.bounds = new Bounds((chunk.BoundsMin + chunk.BoundsMax) * 0.5f, chunk.BoundsMax - chunk.BoundsMin);
                // Rewritten mesh: the old baked collision data is stale.
                DropCollider(chunk);
                if (!_shown.ContainsKey(chunk.Id.Key))
                {
                    _shown.Add(chunk.Id.Key, chunk);
                    chunk.Go.SetActive(true);
                }
            }

            DisposeBatchBuffers();
            ApplyRemovals();
            _building = false;
        }

        private void DisposeBatchBuffers()
        {
            _bounds.Dispose();
            _geometries.Dispose();
            _indexRanges.Dispose();
        }

        private void ApplyRemovals()
        {
            for (int k = 0; k < _batchRemove.Count; k++)
            {
                if (_shown.Remove(_batchRemove[k].Key, out Chunk chunk))
                {
                    DropCollider(chunk);
                    chunk.Go.SetActive(false);
                    _pool.Push(chunk);
                }
            }

            _batchRemove.Clear();
        }

        private Chunk Rent(ChunkId id)
        {
            Chunk chunk;
            if (_pool.Count > 0)
            {
                chunk = _pool.Pop();
            }
            else
            {
                var go = new GameObject("Chunk");
                go.SetActive(false);
                go.transform.SetParent(transform, false);
                var mesh = new Mesh();
                mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = _material;
                var collider = go.AddComponent<MeshCollider>();
                collider.enabled = false;
                chunk = new Chunk { Go = go, Mesh = mesh, Collider = collider };
            }

            chunk.Id = id;
#if UNITY_EDITOR
            // Editor-only: naming allocates, and is only useful in the hierarchy.
            chunk.Go.name = id.ToString();
#endif
            return chunk;
        }

        private static void DropCollider(Chunk chunk)
        {
            chunk.ColliderReady = false;
            chunk.Collider.enabled = false;
            chunk.Collider.sharedMesh = null;
        }

        private void UpdateColliders()
        {
            if (_baking && _bakeHandle.IsCompleted)
            {
                CompleteBake();
            }

            // The drop pass runs even while a bake is in flight, so colliders never linger beyond range.
            bool canBake = !_baking;
            if (canBake)
            {
                _bakeChunks.Clear();
            }

            float3 viewer = (float3)_viewer.position - _config.Center;
            float rangeSq = _colliderRange * _colliderRange;
            foreach (KeyValuePair<ulong, Chunk> pair in _shown)
            {
                Chunk chunk = pair.Value;
                float3 nearest = math.clamp(viewer, chunk.BoundsMin, chunk.BoundsMax);
                bool inRange = math.distancesq(viewer, nearest) <= rangeSq;
                if (inRange && !chunk.ColliderReady && canBake)
                {
                    _bakeChunks.Add(chunk);
                }
                else if (!inRange && chunk.ColliderReady)
                {
                    DropCollider(chunk);
                }
            }

            if (!canBake || _bakeChunks.Count == 0)
            {
                return;
            }

            _bakeIds = new NativeArray<EntityId>(_bakeChunks.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int k = 0; k < _bakeChunks.Count; k++)
            {
                _bakeIds[k] = _bakeChunks[k].Mesh.GetEntityId();
            }

            _bakeHandle = new BakeJob { MeshIds = _bakeIds }.Schedule(_bakeIds.Length, 1);
            JobHandle.ScheduleBatchedJobs();
            _baking = true;
        }

        private void CompleteBake()
        {
            if (!_baking)
            {
                return;
            }

            _bakeHandle.Complete();
            for (int k = 0; k < _bakeChunks.Count; k++)
            {
                Chunk chunk = _bakeChunks[k];
                // Skip leaves removed while baking; assigning sharedMesh reuses the baked data (no main-thread cook).
                if (_shown.TryGetValue(chunk.Id.Key, out Chunk current) && ReferenceEquals(current, chunk))
                {
                    chunk.Collider.sharedMesh = chunk.Mesh;
                    chunk.Collider.enabled = true;
                    chunk.ColliderReady = true;
                }
            }

            _bakeChunks.Clear();
            _bakeIds.Dispose();
            _baking = false;
        }

        /// <summary>Number of leaves that currently have an enabled collider.</summary>
        /// <remarks>Pre: none. Post: ≤ ShownCount. Side effects: none (iterates shown leaves).</remarks>
        internal int CountColliders(out float farthestColliderDistance)
        {
            int count = 0;
            farthestColliderDistance = 0f;
            float3 viewer = (float3)_viewer.position - _config.Center;
            foreach (KeyValuePair<ulong, Chunk> pair in _shown)
            {
                Chunk chunk = pair.Value;
                if (chunk.Collider.enabled)
                {
                    count++;
                    float d = math.distance(viewer, math.clamp(viewer, chunk.BoundsMin, chunk.BoundsMax));
                    farthestColliderDistance = math.max(farthestColliderDistance, d);
                }
            }

            return count;
        }

        private void OnDestroy()
        {
            if (_building)
            {
                _buildHandle.Complete();
                _meshData.Dispose();
                DisposeBatchBuffers();
                _building = false;
            }

            CompleteBake();
            if (_allIndices.IsCreated)
            {
                _allIndices.Dispose();
            }

            foreach (KeyValuePair<ulong, Chunk> pair in _shown)
            {
                Destroy(pair.Value.Mesh);
            }

            foreach (Chunk chunk in _pool)
            {
                Destroy(chunk.Mesh);
            }
        }
    }
}
