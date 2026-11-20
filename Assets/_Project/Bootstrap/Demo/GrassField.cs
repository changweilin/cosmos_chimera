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
    /// P1-09 (D0) / P1-12: GPU-instanced grass and flower meadows streamed around the player and bent by the wind field
    /// (Cosmimera/ToonGrass).
    /// REQ: vision.md (風場驅動草、GPU instancing 大量草叢; 美術投資 第 1 項 草海密度 ×10、花海); planet-tech.md (everything is
    /// laid out on the cube-sphere).
    /// </summary>
    /// <remarks>
    /// Pre: Configure once. Post: which clumps exist depends only on (seed, cell), never on the path walked: cells are
    /// fixed squares of the cube-sphere grid, so grass does not swim or reshuffle as it streams. Clumps stand on the
    /// same ground function the terrain mesh uses and never in the sea, on sand, cliffs, snow, roads or buildings.
    /// Presentation only: grass has no collider and no rule-layer meaning.
    /// Two rings: every cell within <see cref="RadiusM"/> grows its base clumps (some swapped for flower clumps inside
    /// a meadow); cells within <see cref="NearM"/> also grow a dense near layer, which is where the ×10 blade density
    /// is spent, because only there can the eye tell blades apart. The near layer of a cell is generated once from its
    /// own stream, so it too is identical whenever the cell comes back into reach.
    /// Why cells + batches instead of one compute buffer: the instance set changes only when a cell streams or the
    /// near ring moves (every <c>RefreshMoveM</c>), so the per-frame cost is the draw calls alone.
    /// Side effects: draws with Graphics.RenderMeshInstanced every frame; owns its meshes and materials; writes the
    /// _CosmiPlayer, _CosmiMeadow and _CosmiBloom globals.
    /// </remarks>
    public sealed class GrassField : MonoBehaviour
    {
        /// <summary>Distance (m) from the focus within which grass is streamed in.</summary>
        /// <remarks>Pre: none. Post: constant; just inside the R=320 eye-height horizon (33 m). Side effects: none.</remarks>
        public const float RadiusM = 30f;

        /// <summary>Distance (m) from the focus within which the dense near layer grows.</summary>
        /// <remarks>Pre: none. Post: constant, &lt; RadiusM. Side effects: none.</remarks>
        public const float NearM = 14f;

        private const float CellM = 4f;
        private const int ClumpsPerSide = 7;
        private const int ClumpsPerCell = ClumpsPerSide * ClumpsPerSide;
        private const int BladesPerClump = 18;
        // Near layer: 16² clumps of 30 blades per 4 m cell on top of the base 7² × 18 → ~10× the blades per m².
        private const int NearPerSide = 16;
        private const int NearPerCell = NearPerSide * NearPerSide;
        private const int NearBlades = 30;
        private const int NearGrid = 5; // ground samples per cell side for the near layer (1 m, the finest terrain spacing)
        private const int NearCellsPerFrame = 4;
        private const int BatchSize = 1023; // Graphics.RenderMeshInstanced's per-call limit
        private const int CellsPerFrame = 10;
        private const float RefreshMoveM = 2f;
        // Sand reaches ~1.5 m (CosmiBiome.hlsl); starting just inside it gives a ragged beach-grass edge.
        private const float MinAltitudeM = 1.1f;
        private const float MaxSlopeRise = 0.3f; // rise/run across a cell: steeper ground is drawn as rock
        // Inside a full-strength meadow this share of base clumps grows as flowers.
        private const float FlowerShare = 0.65f;
        private const ulong Stream = 0x62A55ul;
        private const ulong NearStream = 0x6E4Aul;
        private static readonly int PlayerId = Shader.PropertyToID("_CosmiPlayer");
        private static readonly int MeadowId = Shader.PropertyToID("_CosmiMeadow");
        private static readonly int BloomId = Shader.PropertyToID("_CosmiBloom");
        private static readonly int FadeId = Shader.PropertyToID("_GrassFade");

        private sealed class Cell
        {
            public readonly Matrix4x4[] Grass = new Matrix4x4[ClumpsPerCell];
            public readonly Matrix4x4[] Flowers = new Matrix4x4[ClumpsPerCell];
            public readonly Matrix4x4[] Near = new Matrix4x4[NearPerCell];
            public int GrassCount;
            public int FlowerCount;
            public int NearCount;
            public bool Steep;
            public bool NearBuilt;
            public float3 Centre;
        }

        // Instance matrices split into RenderMeshInstanced-sized arrays; arrays are kept and reused across rebuilds.
        private sealed class Batches
        {
            private readonly List<Matrix4x4[]> _arrays = new List<Matrix4x4[]>();
            public int Total;

            public void Clear() => Total = 0;

            public void Add(in Matrix4x4 m)
            {
                int b = Total / BatchSize;
                if (b == _arrays.Count) _arrays.Add(new Matrix4x4[BatchSize]);
                _arrays[b][Total % BatchSize] = m;
                Total++;
            }

            public void Draw(in RenderParams renderParams, Mesh mesh)
            {
                for (int b = 0; b * BatchSize < Total; b++)
                {
                    Graphics.RenderMeshInstanced(renderParams, mesh, 0, _arrays[b], math.min(BatchSize, Total - b * BatchSize));
                }
            }
        }

        private PlanetConfig _config;
        private ulong _seed;
        private Func<float3, float> _groundAltitude;
        private Func<float3, bool> _isClear;
        private Func<float3> _focus;
        private Material _material;
        private Material _nearMaterial;
        private float _density;
        private float _maxAltitudeM;
        private float _maxLat;
        private float _meadow;
        private int _cellsPerFace;
        private Mesh _mesh;
        private Mesh _nearMesh;
        private Mesh _flowerMesh;
        private RenderParams _renderParams;
        private RenderParams _nearParams;
        private readonly Dictionary<long, Cell> _cells = new Dictionary<long, Cell>();
        private readonly HashSet<long> _wanted = new HashSet<long>();
        private readonly List<long> _scratch = new List<long>();
        private readonly Stack<Cell> _pool = new Stack<Cell>();
        private readonly Batches _grassBatches = new Batches();
        private readonly Batches _flowerBatches = new Batches();
        private readonly Batches _nearBatches = new Batches();
        private readonly float[] _nearAltitude = new float[NearGrid * NearGrid];
        private float3 _lastFocus = new float3(float.NaN);
        private bool _dirty;
        private bool _configured;

        /// <summary>Number of grass and flower clumps currently drawn (both rings). For tests and diagnostics.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public int InstanceCount { get; private set; }

        /// <summary>Number of flower clumps currently drawn. For tests and diagnostics.</summary>
        /// <remarks>Pre: none. Post: 0 ≤ FlowerCount ≤ InstanceCount; 0 when the meadow strength is 0. Side effects: none.</remarks>
        public int FlowerCount { get; private set; }

        /// <summary>Number of near-layer clumps currently drawn. For tests and diagnostics.</summary>
        /// <remarks>Pre: none. Post: 0 ≤ NearCount ≤ InstanceCount. Side effects: none.</remarks>
        public int NearCount { get; private set; }

        /// <summary>Starts growing grass (and meadows, when <paramref name="meadow"/> &gt; 0) around <paramref name="focus"/>.</summary>
        /// <remarks>
        /// Pre: groundAltitude, focus, material non-null; material uses an instancing shader with enableInstancing;
        ///      isClear may be null (nothing to avoid) and must be deterministic; density in [0, 1];
        ///      maxAltitudeM > 0 (grass stops below it: the snow line); maxLat in (0, 1] (|sin latitude| of the polar cap);
        ///      meadow in [0, 1]; blooms has exactly 4 colours when meadow > 0 (ignored otherwise).
        /// Post: Update streams cells in and out and draws them; nothing grows when density == 0; no flowers when
        ///       meadow == 0; this component owns material and destroys it with itself.
        /// Side effects: allocates the clump meshes and a copy of material for the near layer; sets the meadow globals.
        /// </remarks>
        public void Configure(in PlanetConfig config, ulong seed, Func<float3, float> groundAltitude, Func<float3, bool> isClear,
            Func<float3> focus, Material material, float density, float maxAltitudeM, float maxLat, float meadow = 0f,
            Color[] blooms = null)
        {
            Contract.Requires(groundAltitude != null && focus != null && material != null,
                "groundAltitude, focus and material are required");
            Contract.Requires(density >= 0f && density <= 1f && maxAltitudeM > 0f && maxLat > 0f && maxLat <= 1f,
                "density in [0,1]; maxAltitudeM > 0; maxLat in (0,1]");
            Contract.Requires(meadow >= 0f && meadow <= 1f && (meadow == 0f || (blooms != null && blooms.Length == 4)),
                "meadow in [0,1]; four bloom colours when meadow > 0");
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
            _meadow = meadow;
            _cellsPerFace = math.max(1, (int)math.round(config.Radius * math.PI * 0.5f / CellM));
            _mesh = BuildClumpMesh(BladesPerClump, 0, 0.42f, 0xC1A5Ful, "GrassClump");
            _nearMesh = BuildClumpMesh(NearBlades, 0, 0.3f, 0xC1A5Eul, "GrassClumpNear");
            _flowerMesh = BuildClumpMesh(10, 7, 0.4f, 0xF10Eul, "FlowerClump");
            // The near layer shrinks to nothing at its own edge, so the ring boundary never shows as a line.
            _nearMaterial = new Material(material);
            _nearMaterial.SetFloat(FadeId, NearM);
            _renderParams = new RenderParams(material)
            {
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = true,
            };
            _nearParams = new RenderParams(_nearMaterial)
            {
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = true,
            };
            Shader.SetGlobalVector(MeadowId, new Vector4(meadow, 0f, 0f, 0f));
            if (meadow > 0f)
            {
                var colours = new Vector4[4];
                for (int k = 0; k < 4; k++) colours[k] = blooms[k];
                Shader.SetGlobalVectorArray(BloomId, colours);
            }

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
                _dirty = true; // the near ring moved with the focus
            }

            BuildPending();
            if (_dirty)
            {
                Rebatch();
            }

            var bounds = new Bounds((Vector3)focus, Vector3.one * (2f * (RadiusM + CellM * 2f)));
            _renderParams.worldBounds = bounds;
            _nearParams.worldBounds = bounds;
            _grassBatches.Draw(_renderParams, _mesh);
            _flowerBatches.Draw(_renderParams, _flowerMesh);
            _nearBatches.Draw(_nearParams, _nearMesh);
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

        // A few cells per frame: a base cell costs ~50 ground samples and a near layer 25 plus its clear checks;
        // building the whole disc at once (~250 cells) would hitch on landing.
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

            int near = 0;
            foreach (KeyValuePair<long, Cell> pair in _cells)
            {
                Cell cell = pair.Value;
                if (cell.NearBuilt || cell.Steep || !InNearRing(cell))
                {
                    continue;
                }

                BuildNear(pair.Key, cell);
                _dirty = true;
                if (++near >= NearCellsPerFrame)
                {
                    break;
                }
            }
        }

        private bool InNearRing(Cell cell) => math.distance(cell.Centre, _lastFocus) <= NearM + CellM;

        private void Corner(long key, out CubeFace face, out float u0, out float v0, out float span)
        {
            int n = _cellsPerFace;
            face = (CubeFace)(int)(key / ((long)n * n));
            int rem = (int)(key % ((long)n * n));
            int j = rem / n;
            int i = rem % n;
            u0 = -1f + 2f * i / n;
            v0 = -1f + 2f * j / n;
            span = 2f / n;
        }

        private void BuildCell(long key, Cell cell)
        {
            cell.GrassCount = 0;
            cell.FlowerCount = 0;
            cell.NearCount = 0;
            cell.NearBuilt = false;
            Corner(key, out CubeFace face, out float u0, out float v0, out float span);

            // One slope estimate per cell from its corners; per-clump normals would triple the ground samples.
            float a00 = _groundAltitude(CubeSphere.FaceToDirection(face, u0, v0));
            float a10 = _groundAltitude(CubeSphere.FaceToDirection(face, u0 + span, v0));
            float a01 = _groundAltitude(CubeSphere.FaceToDirection(face, u0, v0 + span));
            float a11 = _groundAltitude(CubeSphere.FaceToDirection(face, u0 + span, v0 + span));
            cell.Centre = _config.ToWorld(CubeSphere.FaceToDirection(face, u0 + span * 0.5f, v0 + span * 0.5f),
                (a00 + a10 + a01 + a11) * 0.25f);
            float rise = math.max(math.max(math.abs(a10 - a00), math.abs(a01 - a00)),
                math.max(math.abs(a11 - a10), math.abs(a11 - a01)));
            cell.Steep = rise / CellM > MaxSlopeRise;
            if (cell.Steep)
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
                    float floral = rng.NextFloat();
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

                    Matrix4x4 m = Place(dir, altitude, yaw, scale);
                    if (_meadow > 0f && floral < MeadowAt(_config, dir, _meadow) * FlowerShare)
                    {
                        cell.Flowers[cell.FlowerCount++] = m;
                    }
                    else
                    {
                        cell.Grass[cell.GrassCount++] = m;
                    }
                }
            }
        }

        // The near layer reads altitude from a 5 × 5 grid (1 m, the finest terrain vertex spacing) instead of one
        // sample per clump: 25 samples rather than 256 per cell, and the bilinear surface is the one the terrain
        // mesh draws there anyway.
        private void BuildNear(long key, Cell cell)
        {
            cell.NearBuilt = true;
            cell.NearCount = 0;
            Corner(key, out CubeFace face, out float u0, out float v0, out float span);
            const int last = NearGrid - 1;
            for (int gy = 0; gy < NearGrid; gy++)
            {
                for (int gx = 0; gx < NearGrid; gx++)
                {
                    _nearAltitude[gy * NearGrid + gx] =
                        _groundAltitude(CubeSphere.FaceToDirection(face, u0 + span * gx / last, v0 + span * gy / last));
                }
            }

            var rng = new Pcg32(_seed ^ NearStream, (ulong)key);
            for (int cy = 0; cy < NearPerSide; cy++)
            {
                for (int cx = 0; cx < NearPerSide; cx++)
                {
                    float fx = (cx + rng.NextFloat()) / NearPerSide;
                    float fy = (cy + rng.NextFloat()) / NearPerSide;
                    float keep = rng.NextFloat();
                    float yaw = rng.NextFloat() * 360f;
                    float scale = 0.7f + rng.NextFloat() * 0.5f;
                    float floral = rng.NextFloat();
                    if (keep > _density)
                    {
                        continue;
                    }

                    float3 dir = CubeSphere.FaceToDirection(face, u0 + span * fx, v0 + span * fy);
                    if (math.abs(dir.y) > _maxLat || (_isClear != null && !_isClear(dir)))
                    {
                        continue;
                    }

                    // A meadow's sward is thinner, or ten times the blades would bury its flowers.
                    if (_meadow > 0f && floral < MeadowAt(_config, dir, _meadow) * 0.6f)
                    {
                        continue;
                    }

                    float gxf = fx * last, gyf = fy * last;
                    int ix = math.min((int)gxf, last - 1), iy = math.min((int)gyf, last - 1);
                    float tx = gxf - ix, ty = gyf - iy;
                    float altitude = math.lerp(
                        math.lerp(_nearAltitude[iy * NearGrid + ix], _nearAltitude[iy * NearGrid + ix + 1], tx),
                        math.lerp(_nearAltitude[(iy + 1) * NearGrid + ix], _nearAltitude[(iy + 1) * NearGrid + ix + 1], tx), ty);
                    if (altitude < MinAltitudeM || altitude > _maxAltitudeM)
                    {
                        continue;
                    }

                    cell.Near[cell.NearCount++] = Place(dir, altitude, yaw, scale);
                }
            }
        }

        private Matrix4x4 Place(float3 dir, float altitude, float yaw, float scale)
        {
            PlanetFrame frame = PlanetFrame.FromUp(dir);
            float3 world = _config.ToWorld(dir, altitude - 0.04f); // roots slightly buried: no gap on slopes
            // Local +Y is the planet's up at the root (LookRotation), so the yaw axis below is local, not world.
            Quaternion rotation = Quaternion.LookRotation((Vector3)frame.North, (Vector3)frame.Up) *
                                  Quaternion.AngleAxis(yaw, Vector3.up);
            return Matrix4x4.TRS((Vector3)world, rotation, Vector3.one * scale);
        }

        private void Rebatch()
        {
            _dirty = false;
            _grassBatches.Clear();
            _flowerBatches.Clear();
            _nearBatches.Clear();
            foreach (Cell cell in _cells.Values)
            {
                for (int k = 0; k < cell.GrassCount; k++) _grassBatches.Add(cell.Grass[k]);
                for (int k = 0; k < cell.FlowerCount; k++) _flowerBatches.Add(cell.Flowers[k]);
                if (cell.NearBuilt && InNearRing(cell))
                {
                    for (int k = 0; k < cell.NearCount; k++) _nearBatches.Add(cell.Near[k]);
                }
            }

            FlowerCount = _flowerBatches.Total;
            NearCount = _nearBatches.Total;
            InstanceCount = _grassBatches.Total + FlowerCount + NearCount;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_nearMesh != null) Destroy(_nearMesh);
            if (_flowerMesh != null) Destroy(_flowerMesh);
            if (_material != null) Destroy(_material);
            if (_nearMaterial != null) Destroy(_nearMaterial);
            if (_configured)
            {
                Shader.SetGlobalVector(PlayerId, Vector4.zero);
                Shader.SetGlobalVector(MeadowId, Vector4.zero);
            }
        }

        /// <summary>Flower meadow strength at <paramref name="direction"/>: the mask the terrain shader tints with.</summary>
        /// <remarks>
        /// Pre: config valid; direction non-zero; strength in [0, 1].
        /// Post: in [0, strength]; equals CosmiMeadow in CosmiBiome.hlsl up to float rounding (the mask is a smooth ramp,
        ///       so rounding moves a meadow's edge by millimetres); depends only on the arguments.
        /// Side effects: none.
        /// </remarks>
        internal static float MeadowAt(in PlanetConfig config, float3 direction, float strength)
        {
            float3 plan = config.Center + math.normalize(direction) * config.Radius;
            float n = Noise(plan * 0.012f) * 0.75f + Noise(plan * 0.07f + 31f) * 0.25f;
            return math.smoothstep(0.5f, 0.62f, n) * strength;
        }

        // Mirrors CosmiHash / CosmiNoise (CosmiAtmosphere.hlsl).
        private static float Hash(float3 p)
        {
            p = math.frac(p * 0.1031f);
            p += math.dot(p, p.zyx + 31.32f);
            return math.frac((p.x + p.y) * p.z);
        }

        private static float Noise(float3 p)
        {
            float3 i = math.floor(p);
            float3 f = math.frac(p);
            f = f * f * (3f - 2f * f);
            float a = math.lerp(math.lerp(Hash(i), Hash(i + new float3(1, 0, 0)), f.x),
                math.lerp(Hash(i + new float3(0, 1, 0)), Hash(i + new float3(1, 1, 0)), f.x), f.y);
            float b = math.lerp(math.lerp(Hash(i + new float3(0, 0, 1)), Hash(i + new float3(1, 0, 1)), f.x),
                math.lerp(Hash(i + new float3(0, 1, 1)), Hash(i + new float3(1, 1, 1)), f.x), f.y);
            return math.lerp(a, b, f.z);
        }

        // One clump: tapered three-point blades fanned around the root, then `flowers` stems each topped with a
        // star-shaped blossom. uv.y is the height fraction the shader bends and tints by; uv.x a per-blade random that
        // de-phases the sway; uv2.x = 1 marks blossom vertices (ToonGrass paints them with the meadow colour).
        // Fixed stream: every clump of a kind shares this mesh.
        private static Mesh BuildClumpMesh(int blades, int flowers, float spread, ulong state, string name)
        {
            var rng = new Pcg32(state, Stream);
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var marks = new List<Vector2>();
            var indices = new List<int>();
            for (int b = 0; b < blades; b++)
            {
                float angle = rng.NextFloat() * 2f * math.PI;
                float radius = math.sqrt(rng.NextFloat()) * spread;
                var root = new Vector3(math.cos(angle) * radius, 0f, math.sin(angle) * radius);
                float facing = rng.NextFloat() * 2f * math.PI;
                var side = new Vector3(math.cos(facing), 0f, math.sin(facing));
                var lean = new Vector3(-side.z, 0f, side.x) * (0.04f + rng.NextFloat() * 0.1f);
                float height = 0.18f + rng.NextFloat() * 0.2f;
                float width = 0.016f + rng.NextFloat() * 0.012f;
                float random = rng.NextFloat();
                Blade(vertices, uvs, marks, indices, root, side, lean, height, width, random);
            }

            for (int f = 0; f < flowers; f++)
            {
                float angle = rng.NextFloat() * 2f * math.PI;
                float radius = math.sqrt(rng.NextFloat()) * spread * 0.8f;
                var root = new Vector3(math.cos(angle) * radius, 0f, math.sin(angle) * radius);
                float facing = rng.NextFloat() * 2f * math.PI;
                var side = new Vector3(math.cos(facing), 0f, math.sin(facing));
                var lean = new Vector3(-side.z, 0f, side.x) * (0.02f + rng.NextFloat() * 0.05f);
                // Taller than the blades (≤ 0.38): the blossoms must sit on top of the sward, not inside it.
                float height = 0.36f + rng.NextFloat() * 0.18f;
                float random = rng.NextFloat();
                Blade(vertices, uvs, marks, indices, root, side, lean, height, 0.007f, random);

                // Star blossom: ten rim points alternating petal tip and notch, tipped a little off level.
                Vector3 head = root + lean + new Vector3(0f, height, 0f);
                float petal = 0.055f + rng.NextFloat() * 0.03f;
                float turn = rng.NextFloat() * 2f * math.PI;
                var tilt = new Vector3(rng.NextFloat() - 0.5f, 0f, rng.NextFloat() - 0.5f) * 0.6f;
                int centre = vertices.Count;
                vertices.Add(head + new Vector3(0f, 0.008f, 0f));
                uvs.Add(new Vector2(random, 1f));
                marks.Add(new Vector2(1f, 1f)); // y = 1: the blossom's heart
                for (int k = 0; k < 10; k++)
                {
                    float a = turn + k * math.PI / 5f;
                    float r = (k & 1) == 0 ? petal : petal * 0.45f;
                    var rim = new Vector3(math.cos(a) * r, 0f, math.sin(a) * r);
                    rim.y = Vector3.Dot(rim, tilt);
                    vertices.Add(head + rim);
                    uvs.Add(new Vector2(random, 1f));
                    marks.Add(new Vector2(1f, 0f));
                }

                for (int k = 0; k < 10; k++)
                {
                    indices.Add(centre);
                    indices.Add(centre + 1 + k);
                    indices.Add(centre + 1 + (k + 1) % 10);
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            // Always written: a mesh without UV1 does not read 0 there on every platform, and stray values paint blades as petals.
            mesh.SetUVs(1, marks);
            mesh.SetTriangles(indices, 0);
            mesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 2f, 2f)); // room for wind bend
            return mesh;
        }

        private static void Blade(List<Vector3> vertices, List<Vector2> uvs, List<Vector2> marks, List<int> indices,
            Vector3 root, Vector3 side, Vector3 lean, float height, float width, float random)
        {
            int v = vertices.Count;
            vertices.Add(root - side * width);
            vertices.Add(root + side * width);
            vertices.Add(root - side * (width * 0.7f) + lean * 0.35f + new Vector3(0f, height * 0.5f, 0f));
            vertices.Add(root + side * (width * 0.7f) + lean * 0.35f + new Vector3(0f, height * 0.5f, 0f));
            vertices.Add(root + lean + new Vector3(0f, height, 0f));
            uvs.Add(new Vector2(random, 0f));
            uvs.Add(new Vector2(random, 0f));
            uvs.Add(new Vector2(random, 0.5f));
            uvs.Add(new Vector2(random, 0.5f));
            uvs.Add(new Vector2(random, 1f));
            for (int k = 0; k < 5; k++) marks.Add(Vector2.zero);
            indices.Add(v);
            indices.Add(v + 2);
            indices.Add(v + 1);
            indices.Add(v + 1);
            indices.Add(v + 2);
            indices.Add(v + 3);
            indices.Add(v + 2);
            indices.Add(v + 4);
            indices.Add(v + 3);
        }
    }
}
