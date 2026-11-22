using System.Runtime.InteropServices;
using Cosmimera.Core.Contracts;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cosmimera.Planet.Model
{
    /// <summary>
    /// Interleaved leaf-mesh vertex: planet-local position (relative to PlanetConfig.Center), normal, and the river mask
    /// (TemplateView.River, UV0.x) the terrain shader paints riverbeds with.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ChunkVertex
    {
        public float3 Position;
        public float3 Normal;
        public float River;

        /// <summary>Vertex layout matching the field order above.</summary>
        public static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 1),
        };
    }

    /// <summary>Everything needed to compute one leaf's vertices; validated on the main thread, consumed in Burst.</summary>
    /// <remarks>
    /// Pre: build through <see cref="Create"/>.
    /// Post: Min/Step are exact dyadic rationals, so a vertex shared with any neighbour (same or adjacent level,
    ///       same or adjacent face) gets bit-identical parameters and therefore a bit-identical position.
    /// Side effects: none (blittable value).
    /// </remarks>
    internal struct ChunkGeometry
    {
        public int Face;
        public float2 Min;
        public float Step;
        public int QuadsPerEdge;
        public float Radius;
        public float NormalEpsilon;
        public PlanetHeight Height;

        /// <summary>Geometry for leaf <paramref name="id"/>.</summary>
        /// <remarks>
        /// Pre: config.IsValid; height.IsValid; quadsPerEdge is a power of two within [2, 128].
        /// Post: Step · QuadsPerEdge == node extent in (u, v) exactly.
        /// Side effects: none.
        /// </remarks>
        public static ChunkGeometry Create(ChunkId id, in PlanetConfig config, in PlanetHeight height, int quadsPerEdge)
        {
            Contract.Requires(config.IsValid, "PlanetConfig is default/uninitialised");
            Contract.Requires(height.IsValid, "PlanetHeight is default/uninitialised");
            Contract.Requires(quadsPerEdge >= 2 && quadsPerEdge <= 128 && math.ispow2(quadsPerEdge),
                "quadsPerEdge must be a power of two within [2, 128]");
            id.GetBounds(out float2 min, out float2 max);
            return new ChunkGeometry
            {
                Face = (int)id.Face,
                Min = min,
                Step = (max.x - min.x) / quadsPerEdge,
                QuadsPerEdge = quadsPerEdge,
                Radius = config.Radius,
                // Fixed 0.25 m ground step (not per-level) so neighbouring leaves of different levels shade
                // their shared vertices with the same normal; finer than the finest 1 m vertex spacing.
                NormalEpsilon = 0.25f / config.Radius,
                Height = height,
            };
        }

        /// <summary>Fills <paramref name="vertices"/> and returns the planet-local AABB.</summary>
        /// <remarks>
        /// Pre: vertices.Length == (QuadsPerEdge + 1)²; this value came from <see cref="Create"/>.
        /// Post: vertex (i, j) at index j·(QuadsPerEdge + 1) + i; normals unit length and outward-facing; River 0.
        /// Side effects: writes vertices. Burst-compatible (no managed calls).
        /// </remarks>
        public void Build(NativeArray<ChunkVertex> vertices, out float3 boundsMin, out float3 boundsMax) =>
            Build(vertices, default, out boundsMin, out boundsMax);

        /// <summary>As <see cref="Build(NativeArray{ChunkVertex}, out float3, out float3)"/> with geo-templates over the noise.</summary>
        /// <remarks>
        /// Pre: as the other overload; templates default (none) or a live PlanetTemplates.View.
        /// Post: positions use templates.Apply(dir, noise); with default templates identical to the other overload.
        /// Side effects: writes vertices. Burst-compatible.
        /// </remarks>
        public void Build(NativeArray<ChunkVertex> vertices, in TemplateView templates, out float3 boundsMin,
            out float3 boundsMax) => Build(vertices, templates, default, out boundsMin, out boundsMax);

        /// <summary>As the template overload, with cell walls raised on top of the terrain.</summary>
        /// <remarks>
        /// Pre: as the other overloads; walls default (none) or a live PlanetWalls.View.
        /// Post: positions use templates.Apply(dir, noise) + walls.Height(dir); with default walls identical to the
        ///       template overload. River = templates.River(dir) (0 without templates).
        /// Side effects: writes vertices. Burst-compatible.
        /// </remarks>
        public void Build(NativeArray<ChunkVertex> vertices, in TemplateView templates, in WallView walls,
            out float3 boundsMin, out float3 boundsMax)
        {
            int row = QuadsPerEdge + 1;
            boundsMin = new float3(float.MaxValue);
            boundsMax = new float3(float.MinValue);

            float extent = QuadsPerEdge * Step;
            float3 chunkCenter = CubeSphere.FaceToDirectionCore(Face, Min.x + extent * 0.5f, Min.y + extent * 0.5f);
            float minDot = math.min(
                math.min(math.dot(chunkCenter, CubeSphere.FaceToDirectionCore(Face, Min.x, Min.y)),
                         math.dot(chunkCenter, CubeSphere.FaceToDirectionCore(Face, Min.x + extent, Min.y))),
                math.min(math.dot(chunkCenter, CubeSphere.FaceToDirectionCore(Face, Min.x, Min.y + extent)),
                         math.dot(chunkCenter, CubeSphere.FaceToDirectionCore(Face, Min.x + extent, Min.y + extent))));
            float chunkHalfAngle = math.acos(math.clamp(minDot, -1f, 1f)) + NormalEpsilon + 2e-3f;
            float chunkChordBound = 2f * math.sin(math.min(chunkHalfAngle, math.PI) * 0.5f) + 1e-3f;

            FixedList128Bytes<int> tplIndices = default;
            bool useTplFilter = templates.Count > 0 && templates.CollectIntersecting(chunkCenter, chunkHalfAngle, out tplIndices);
            bool checkRing = false;
            FixedList512Bytes<int> corridorIndices = default;
            bool useWallFilter = walls.Count > 0 &&
                                 walls.TryBuildChunkFilter(chunkCenter, chunkChordBound, out checkRing, out corridorIndices);

            for (int j = 0; j < row; j++)
            {
                float v = Min.y + j * Step;
                for (int i = 0; i < row; i++)
                {
                    float u = Min.x + i * Step;
                    float3 dir = CubeSphere.FaceToDirectionCore(Face, u, v);
                    float3 p = Surface(dir, templates, walls, useTplFilter, tplIndices, useWallFilter, checkRing, corridorIndices, out float river);
                    float3 normal = SurfaceNormal(dir, templates, walls, useTplFilter, tplIndices, useWallFilter, checkRing, corridorIndices);
                    vertices[j * row + i] = new ChunkVertex
                    {
                        Position = p, Normal = normal, River = river,
                    };
                    boundsMin = math.min(boundsMin, p);
                    boundsMax = math.max(boundsMax, p);
                }
            }
        }

        private float3 Surface(float3 dir, in TemplateView templates, in WallView walls, bool useTplFilter,
            in FixedList128Bytes<int> tplIndices, bool useWallFilter, bool checkRing,
            in FixedList512Bytes<int> corridorIndices, out float river)
        {
            float h = Height.SampleCore(dir);
            river = 0f;
            if (templates.Count > 0)
            {
                float3 ndir = math.normalize(dir);
                h = useTplFilter
                    ? templates.ApplySubset(ndir, h, tplIndices, out river)
                    : templates.ApplyWithRiver(ndir, h, out river);
            }

            if (walls.Count > 0)
            {
                float3 ndir = math.normalize(dir);
                h += useWallFilter
                    ? walls.HeightSubset(ndir, checkRing, corridorIndices)
                    : walls.Height(ndir);
            }

            return dir * (Radius + h);
        }

        // Central differences of the displaced surface in an arbitrary tangent basis. The result is
        // basis-independent (up to rounding), so it agrees across cube faces without using PlanetFrame.
        private float3 SurfaceNormal(float3 dir, in TemplateView templates, in WallView walls, bool useTplFilter,
            in FixedList128Bytes<int> tplIndices, bool useWallFilter, bool checkRing,
            in FixedList512Bytes<int> corridorIndices)
        {
            // Reference axis only for building *a* tangent; it is not treated as "up" (CLAUDE.md#硬性規則).
            float3 reference = math.abs(dir.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f);
            float3 t1 = math.normalize(math.cross(reference, dir));
            float3 t2 = math.cross(dir, t1);
            float e = NormalEpsilon;
            float3 du = Surface(math.normalize(dir + t1 * e), templates, walls, useTplFilter, tplIndices, useWallFilter, checkRing, corridorIndices, out _) -
                        Surface(math.normalize(dir - t1 * e), templates, walls, useTplFilter, tplIndices, useWallFilter, checkRing, corridorIndices, out _);
            float3 dv = Surface(math.normalize(dir + t2 * e), templates, walls, useTplFilter, tplIndices, useWallFilter, checkRing, corridorIndices, out _) -
                        Surface(math.normalize(dir - t2 * e), templates, walls, useTplFilter, tplIndices, useWallFilter, checkRing, corridorIndices, out _);
            // cross(t1, t2) == dir, so this orientation points away from the planet.
            return math.normalize(math.cross(du, dv));
        }
    }

    /// <summary>Burst job that writes a batch of leaves' vertices and stitched indices into writable mesh data.</summary>
    /// <remarks>
    /// Pre: Output was allocated by Mesh.AllocateWritableMeshData(Geometries.Length) and each element prepared with
    ///      <see cref="PrepareOutput"/> for its index count; IndexRanges[k] = (offset, length) of leaf k's
    ///      ChunkStitching variant inside AllIndices; Bounds.Length == Geometries.Length.
    /// Post: Output[k] has one sub-mesh with all its indices; Bounds[k] = planet-local AABB (c0 = min, c1 = max).
    /// Side effects: writes Output and Bounds.
    /// One parallel job per batch rather than one IJob per leaf: sub-arrays of one NativeArray share a safety
    /// handle, so per-leaf jobs writing disjoint slices are rejected by the job-safety system.
    /// </remarks>
    [BurstCompile(CompileSynchronously = true)]
    internal struct ChunkMeshJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ChunkGeometry> Geometries;
        [ReadOnly] public NativeArray<int2> IndexRanges;
        [ReadOnly] public NativeArray<ushort> AllIndices;
        public Mesh.MeshDataArray Output;
        [WriteOnly] public NativeArray<float3x2> Bounds;
        public TemplateView Templates;
        public WallView Walls;

        /// <summary>Sizes one leaf's vertex/index buffers on the main thread (the layout is a managed array, so not in Burst).</summary>
        /// <remarks>Pre: as the job. Post: buffers sized for the job. Side effects: allocates mesh-data buffers.</remarks>
        public static void PrepareOutput(Mesh.MeshData output, int quadsPerEdge, int indexCount)
        {
            int row = quadsPerEdge + 1;
            output.SetVertexBufferParams(row * row, ChunkVertex.Layout);
            output.SetIndexBufferParams(indexCount, IndexFormat.UInt16);
        }

        public void Execute(int index)
        {
            ChunkGeometry geometry = Geometries[index];
            int2 range = IndexRanges[index];
            Mesh.MeshData output = Output[index];
            int row = geometry.QuadsPerEdge + 1;
            NativeArray<ChunkVertex> vertices = output.GetVertexData<ChunkVertex>();
            geometry.Build(vertices, Templates, Walls, out float3 min, out float3 max);
            output.GetIndexData<ushort>().CopyFrom(AllIndices.GetSubArray(range.x, range.y));
            output.subMeshCount = 1;
            var bounds = new Bounds((min + max) * 0.5f, max - min);
            output.SetSubMesh(0, new SubMeshDescriptor(0, range.y) { bounds = bounds, vertexCount = row * row },
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            Bounds[index] = new float3x2(min, max);
        }
    }
}
