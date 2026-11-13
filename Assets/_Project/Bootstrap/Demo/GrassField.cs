using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Planet;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// P1-09 (D0): GPU-instanced grass streamed around the player and bent by the wind field (Cosmimera/ToonGrass).
    /// REQ: vision.md (風場驅動草、GPU instancing 大量草叢); planet-tech.md (everything is laid out on the cube-sphere).
    /// </summary>
    /// <remarks>
    /// Pre: Configure once. Post: which clumps exist depends only on (seed, cell), never on the path walked: cells are
    /// fixed squares of the cube-sphere grid, so grass does not swim or reshuffle as it streams. Clumps stand on the
    /// same ground function the terrain mesh uses and never in the sea, on sand, cliffs, snow, roads or buildings.
    /// Presentation only: grass has no collider and no rule-layer meaning.
    /// Why cells + batches instead of one compute buffer: the instance set changes only when a cell streams, so the
    /// per-frame cost is the draw calls alone (≤ ~10 batches of 1023).
    /// Side effects: draws with Graphics.RenderMeshInstanced every frame; owns its mesh; writes the _CosmiPlayer global.
    /// </remarks>
    public sealed class GrassField : MonoBehaviour
    {
        /// <summary>Distance (m) from the focus within which grass is streamed in.</summary>
        /// <remarks>Pre: none. Post: constant; just inside the R=320 eye-height horizon (33 m). Side effects: none.</remarks>
        public const float RadiusM = 30f;

        private const float CellM = 4f;
        private const int ClumpsPerSide = 7;
        private const int ClumpsPerCell = ClumpsPerSide * ClumpsPerSide;
        private const int BladesPerClump = 18;
        private const int BatchSize = 1023; // Graphics.RenderMeshInstanced's per-call limit
        private const int CellsPerFrame = 10;
        private const float RefreshMoveM = 2f;
        // Sand reaches ~1.5 m (CosmiBiome.hlsl); starting just inside it gives a ragged beach-grass edge.
        private const float MinAltitudeM = 1.1f;
        private const float MaxSlopeRise = 0.3f; // rise/run across a cell: steeper ground is drawn as rock
        private const ulong Stream = 0x62A55ul;
        private static readonly int PlayerId = Shader.PropertyToID("_CosmiPlayer");

        private sealed class Cell
        {
            public readonly Matrix4x4[] Matrices = new Matrix4x4[ClumpsPerCell];
            public int Count;
        }

        private PlanetConfig _config;
        private ulong _seed;
        private Func<float3, float> _groundAltitude;
        private Func<float3, bool> _isClear;
        private Func<float3> _focus;
        private Material _material;
        private float _density;
        private float _maxAltitudeM;
        private float _maxLat;
        private int _cellsPerFace;
        private Mesh _mesh;
        private RenderParams _renderParams;
        private readonly Dictionary<long, Cell> _cells = new Dictionary<long, Cell>();
        private readonly HashSet<long> _wanted = new HashSet<long>();
        private readonly List<long> _scratch = new List<long>();
        private readonly Stack<Cell> _pool = new Stack<Cell>();
        private readonly List<Matrix4x4[]> _batches = new List<Matrix4x4[]>();
        private readonly List<int> _batchCounts = new List<int>();
        private float3 _lastFocus = new float3(float.NaN);
        private bool _dirty;
        private bool _configured;

        /// <summary>Number of grass clumps currently drawn. For tests and diagnostics.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public int InstanceCount { get; private set; }

        /// <summary>Starts growing grass around <paramref name="focus"/>.</summary>
        /// <remarks>
        /// Pre: groundAltitude, focus, material non-null; material uses an instancing shader with enableInstancing;
        ///      isClear may be null (nothing to avoid) and must be deterministic; density in [0, 1];
        ///      maxAltitudeM > 0 (grass stops below it: the snow line); maxLat in (0, 1] (|sin latitude| of the polar cap).
        /// Post: Update streams cells in and out and draws them; nothing grows when density == 0; this component owns
        ///       material and destroys it with itself.
        /// Side effects: allocates the clump mesh.
        /// </remarks>
        public void Configure(in PlanetConfig config, ulong seed, Func<float3, float> groundAltitude, Func<float3, bool> isClear,
            Func<float3> focus, Material material, float density, float maxAltitudeM, float maxLat)
        {
            Contract.Requires(groundAltitude != null && focus != null && material != null,
                "groundAltitude, focus and material are required");
            Contract.Requires(density >= 0f && density <= 1f && maxAltitudeM > 0f && maxLat > 0f && maxLat <= 1f,
                "density in [0,1]; maxAltitudeM > 0; maxLat in (0,1]");
            Contract.Requires(!_configured, "GrassField.Configure called twice");
            _config = config;
            _seed = seed;
            _groundAltitude = groundAltitude;
            _isClear = isClear;
            _focus = focus;
            _material = material;
            _density = density;
            _maxAltitudeM = maxAltitudeM;
            _maxLat = maxLat;
            _cellsPerFace = math.max(1, (int)math.round(config.Radius * math.PI * 0.5f / CellM));
            _mesh = BuildClumpMesh();
            _renderParams = new RenderParams(material)
            {
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = true,
            };
            _configured = true;
        }

        private void Update()
        {
            if (!_configured || _density <= 0f)
            {
                return;
            }

            float3 focus = _focus();
            Shader.SetGlobalVector(PlayerId, new Vector4(focus.x, focus.y, focus.z, 0.9f));
            if (!math.all(math.isfinite(_lastFocus)) || math.distance(focus, _lastFocus) > RefreshMoveM)
            {
                _lastFocus = focus;
                CollectWanted(_config.ToDirection(focus));
                DropStale();
            }

            BuildPending();
            if (_dirty)
            {
                Rebatch();
            }

            _renderParams.worldBounds = new Bounds((Vector3)focus, Vector3.one * (2f * (RadiusM + CellM * 2f)));
            for (int b = 0; b < _batches.Count; b++)
            {
                if (_batchCounts[b] > 0)
                {
                    Graphics.RenderMeshInstanced(_renderParams, _mesh, 0, _batches[b], _batchCounts[b]);
                }
            }
        }

        // Samples the tangent plane at half-cell spacing and keeps the grid cell under each sample: every cell that
        // touches the disc is hit at least once, with no cube-face edge cases to enumerate.
        private void CollectWanted(float3 up)
        {
            _wanted.Clear();
            PlanetFrame frame = PlanetFrame.FromUp(up);
            float reach = RadiusM + CellM;
            float step = CellM * 0.5f;
            int n = (int)math.ceil(reach / step);
            for (int y = -n; y <= n; y++)
            {
                for (int x = -n; x <= n; x++)
                {
                    float px = x * step;
                    float py = y * step;
                    if (px * px + py * py > reach * reach)
                    {
                        continue;
                    }

                    float3 dir = up + (frame.East * px + frame.North * py) / _config.Radius;
                    CubeFace face = CubeSphere.DirectionToFace(dir, out float u, out float v);
                    int i = math.clamp((int)((u + 1f) * 0.5f * _cellsPerFace), 0, _cellsPerFace - 1);
                    int j = math.clamp((int)((v + 1f) * 0.5f * _cellsPerFace), 0, _cellsPerFace - 1);
                    _wanted.Add(((long)face * _cellsPerFace + j) * _cellsPerFace + i);
                }
            }
        }

        private void DropStale()
        {
            _scratch.Clear();
            foreach (long key in _cells.Keys)
            {
                if (!_wanted.Contains(key))
                {
                    _scratch.Add(key);
                }
            }

            foreach (long key in _scratch)
            {
                _pool.Push(_cells[key]);
                _cells.Remove(key);
                _dirty = true;
            }
        }

        // A few cells per frame: a cell costs ~50 ground samples, and building the whole disc at once (~250 cells)
        // would hitch on landing.
        private void BuildPending()
        {
            int built = 0;
            foreach (long key in _wanted)
            {
                if (_cells.ContainsKey(key))
                {
                    continue;
                }

                Cell cell = _pool.Count > 0 ? _pool.Pop() : new Cell();
                BuildCell(key, cell);
                _cells[key] = cell;
                _dirty = true;
                if (++built >= CellsPerFrame)
                {
                    break;
                }
            }
        }

        private void BuildCell(long key, Cell cell)
        {
            cell.Count = 0;
            int n = _cellsPerFace;
            var face = (CubeFace)(int)(key / ((long)n * n));
            int rem = (int)(key % ((long)n * n));
            int j = rem / n;
            int i = rem % n;
            float u0 = -1f + 2f * i / n;
            float v0 = -1f + 2f * j / n;
            float span = 2f / n;

            // One slope estimate per cell from its corners; per-clump normals would triple the ground samples.
            float a00 = _groundAltitude(CubeSphere.FaceToDirection(face, u0, v0));
            float a10 = _groundAltitude(CubeSphere.FaceToDirection(face, u0 + span, v0));
            float a01 = _groundAltitude(CubeSphere.FaceToDirection(face, u0, v0 + span));
            float a11 = _groundAltitude(CubeSphere.FaceToDirection(face, u0 + span, v0 + span));
            float rise = math.max(math.max(math.abs(a10 - a00), math.abs(a01 - a00)),
                math.max(math.abs(a11 - a10), math.abs(a11 - a01)));
            if (rise / CellM > MaxSlopeRise)
            {
                return;
            }

            // A per-cell stream: the same cell regrows identically after streaming out and back in.
            var rng = new Pcg32(_seed ^ Stream, (ulong)key);
            for (int cy = 0; cy < ClumpsPerSide; cy++)
            {
                for (int cx = 0; cx < ClumpsPerSide; cx++)
                {
                    float jx = rng.NextFloat();
                    float jy = rng.NextFloat();
                    float keep = rng.NextFloat();
                    float yaw = rng.NextFloat() * 360f;
                    float scale = 0.75f + rng.NextFloat() * 0.6f;
                    if (keep > _density)
                    {
                        continue;
                    }

                    float3 dir = CubeSphere.FaceToDirection(face, u0 + span * (cx + jx) / ClumpsPerSide,
                        v0 + span * (cy + jy) / ClumpsPerSide);
                    if (math.abs(dir.y) > _maxLat || (_isClear != null && !_isClear(dir)))
                    {
                        continue;
                    }

                    float altitude = _groundAltitude(dir);
                    if (altitude < MinAltitudeM || altitude > _maxAltitudeM)
                    {
                        continue;
                    }

                    PlanetFrame frame = PlanetFrame.FromUp(dir);
                    float3 world = _config.ToWorld(dir, altitude - 0.04f); // roots slightly buried: no gap on slopes
                    // Local +Y is the planet's up at the root (LookRotation), so the yaw axis below is local, not world.
                    Quaternion rotation = Quaternion.LookRotation((Vector3)frame.North, (Vector3)frame.Up) *
                                          Quaternion.AngleAxis(yaw, Vector3.up);
                    cell.Matrices[cell.Count++] = Matrix4x4.TRS((Vector3)world, rotation, Vector3.one * scale);
                }
            }
        }

        private void Rebatch()
        {
            _dirty = false;
            int total = 0;
            foreach (Cell cell in _cells.Values)
            {
                for (int k = 0; k < cell.Count; k++)
                {
                    int b = total / BatchSize;
                    if (b == _batches.Count)
                    {
                        _batches.Add(new Matrix4x4[BatchSize]);
                        _batchCounts.Add(0);
                    }

                    _batches[b][total % BatchSize] = cell.Matrices[k];
                    total++;
                }
            }

            for (int b = 0; b < _batches.Count; b++)
            {
                _batchCounts[b] = math.clamp(total - b * BatchSize, 0, BatchSize);
            }

            InstanceCount = total;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
            }

            if (_material != null)
            {
                Destroy(_material);
            }

            if (_configured)
            {
                Shader.SetGlobalVector(PlayerId, Vector4.zero);
            }
        }

        // One clump: tapered three-point blades fanned around the root. uv.y is the height fraction the shader bends
        // and tints by; uv.x a per-blade random that de-phases the sway. Fixed stream: every clump shares this mesh.
        private static Mesh BuildClumpMesh()
        {
            var rng = new Pcg32(0xC1A5Ful, Stream);
            var vertices = new Vector3[BladesPerClump * 5];
            var uvs = new Vector2[BladesPerClump * 5];
            var indices = new int[BladesPerClump * 9];
            for (int b = 0; b < BladesPerClump; b++)
            {
                float angle = rng.NextFloat() * 2f * math.PI;
                float radius = math.sqrt(rng.NextFloat()) * 0.42f;
                var root = new Vector3(math.cos(angle) * radius, 0f, math.sin(angle) * radius);
                float facing = rng.NextFloat() * 2f * math.PI;
                var side = new Vector3(math.cos(facing), 0f, math.sin(facing));
                var lean = new Vector3(-side.z, 0f, side.x) * (0.04f + rng.NextFloat() * 0.1f);
                float height = 0.18f + rng.NextFloat() * 0.2f;
                float width = 0.016f + rng.NextFloat() * 0.012f;
                float random = rng.NextFloat();

                int v = b * 5;
                vertices[v] = root - side * width;
                vertices[v + 1] = root + side * width;
                vertices[v + 2] = root - side * (width * 0.7f) + lean * 0.35f + new Vector3(0f, height * 0.5f, 0f);
                vertices[v + 3] = root + side * (width * 0.7f) + lean * 0.35f + new Vector3(0f, height * 0.5f, 0f);
                vertices[v + 4] = root + lean + new Vector3(0f, height, 0f);
                uvs[v] = uvs[v + 1] = new Vector2(random, 0f);
                uvs[v + 2] = uvs[v + 3] = new Vector2(random, 0.5f);
                uvs[v + 4] = new Vector2(random, 1f);

                int t = b * 9;
                indices[t] = v;
                indices[t + 1] = v + 2;
                indices[t + 2] = v + 1;
                indices[t + 3] = v + 1;
                indices[t + 4] = v + 2;
                indices[t + 5] = v + 3;
                indices[t + 6] = v + 2;
                indices[t + 7] = v + 4;
                indices[t + 8] = v + 3;
            }

            var mesh = new Mesh { name = "GrassClump" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = indices;
            mesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 2f, 2f)); // room for wind bend
            return mesh;
        }
    }
}
