using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Far scenery layer (P1-15): keeps the silhouettes of trees, large rocks, spires and crystals on screen from the
    /// edge of <see cref="ScatterField"/>'s near field out into the fog.
    /// REQ: vision.md#美術投資 第 5 項 (遠景 LOD／impostor); phase-1.md P1-15 (90 m 外仍看得到樹與建築輪廓 —
    /// buildings are already drawn planet-wide by SettlementView).
    /// </summary>
    /// <remarks>
    /// The canopy grid is cut into tiles of <see cref="TileCells"/>² cells; each tile near enough becomes one merged
    /// mesh of <see cref="FarSceneryMeshes"/> silhouettes with the item colours baked into vertices, so the whole
    /// layer is a few dozen draw calls with no per-frame CPU work. Cosmimera/ToonScenery shrinks every item into its
    /// ground point inside the near field (where the real object stands) and at the layer's outer edge.
    /// It draws exactly the items the rule layer places (same seed → same skyline on every client) but is not itself
    /// part of the reproducibility contract: nothing here blocks, breaks or is hit.
    /// Pre: Configure once, main thread. Post: at most <see cref="TilesPerFrame"/> tile meshes are built per frame.
    /// Side effects: creates/destroys GameObjects under this component; owns its material and meshes; writes the
    /// _CosmiFarFocus shader global (zeroed on destroy).
    /// </remarks>
    public sealed class FarScenery : MonoBehaviour
    {
        /// <summary>Canopy cells along one edge of a tile (≈ 80 m).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int TileCells = 8;

        /// <summary>Largest far radius (m) worth drawing: past it a tree is a few fogged pixels.</summary>
        /// <remarks>Pre: none. Post: constant &gt; ScatterField.RadiusM. Side effects: none.</remarks>
        public const float MaxRadiusM = 260f;

        /// <summary>Tile meshes built per frame: spreads the first fill over about half a second.</summary>
        /// <remarks>Pre: none. Post: constant ≥ 1. Side effects: none.</remarks>
        public const int TilesPerFrame = 2;

        // Silhouettes are full size from here on; ScatterField's cells cover at least RadiusM, so a real object
        // already stands wherever a silhouette is still shrunk.
        private const float FadeInEndM = ScatterField.RadiusM + 6f;
        private const float FadeInStartM = ScatterField.RadiusM - 6f;
        private const float FadeOutM = 40f;
        private const float RefreshMoveM = 10f;

        private static readonly int FarFocusId = Shader.PropertyToID("_CosmiFarFocus");
        private static readonly int FarFadeId = Shader.PropertyToID("_FarFade");

        private sealed class Tile
        {
            public GameObject Go;
            public Mesh Mesh;
            public bool Built;
        }

        private PlanetConfig _config;
        private ScatterField _field;
        private Func<float3> _focus;
        private Material _material;
        private float _radiusM;
        private int _cellsPerFace;
        private int _tilesPerFace;
        private float _tileReachM;
        private readonly Dictionary<int, Tile> _tiles = new Dictionary<int, Tile>();
        private readonly List<int> _pending = new List<int>();
        private readonly List<int> _stale = new List<int>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Color32> _colors = new List<Color32>();
        private readonly List<Vector3> _anchors = new List<Vector3>();
        private readonly List<int> _triangles = new List<int>();
        private float3 _lastFocus = new float3(float.NaN);
        private bool _configured;

        /// <summary>Number of tiles whose mesh is built, for tests and diagnostics.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public int BuiltTileCount
        {
            get
            {
                int n = 0;
                foreach (Tile t in _tiles.Values)
                {
                    n += t.Built ? 1 : 0;
                }

                return n;
            }
        }

        /// <summary>Number of tiles waiting for their mesh.</summary>
        /// <remarks>Pre: none. Post: ≥ 0; 0 once the layer has caught up with the focus. Side effects: none.</remarks>
        public int PendingTileCount => _pending.Count;

        /// <summary>Starts drawing the far silhouettes of <paramref name="field"/>'s items around <paramref name="focus"/>.</summary>
        /// <remarks>
        /// Pre: config is the planner's planet; field is configured with planner and outlives this component;
        ///      focus non-null; sceneryShader is Cosmimera/ToonScenery; radiusM finite and &gt; ScatterField.RadiusM + 46
        ///      (room for both fades).
        /// Post: Update streams tiles whose ground lies within radiusM of the focus. Side effects: allocates a Material;
        ///       subscribes to field.ItemGone.
        /// </remarks>
        public void Configure(in PlanetConfig config, ScatterField field, ScatterPlanner planner, Func<float3> focus,
            Shader sceneryShader, float radiusM)
        {
            Contract.Requires(field != null && planner != null && focus != null && sceneryShader != null,
                "field, planner, focus and shader are required");
            Contract.Requires(math.isfinite(radiusM) && radiusM > FadeInEndM + FadeOutM, "radiusM must leave room for both fades");
            _config = config;
            _field = field;
            _focus = focus;
            _radiusM = radiusM;
            _cellsPerFace = planner.CellsPerFace;
            _tilesPerFace = (_cellsPerFace + TileCells - 1) / TileCells;
            // Half a tile's diagonal on the ground: a face spans a quarter great circle over CellsPerFace cells, and
            // the cube-sphere warp stretches a cell by at most ~1.3 near a face centre.
            float cellM = config.Radius * math.PI * 0.5f / _cellsPerFace;
            _tileReachM = TileCells * cellM * 0.7071f * 1.3f;
            // White base and no accent: every colour comes from the baked vertex colours.
            _material = PlanetLook.SceneryMaterial(sceneryShader, Color.white, Color.white, 0f, 0f, 0f, 0f);
            _material.SetVector(FarFadeId, new Vector4(FadeInStartM, FadeInEndM, radiusM - FadeOutM, radiusM));
            _field.ItemGone += OnItemGone;
            _configured = true;
        }

        private void Update()
        {
            if (!_configured)
            {
                return;
            }

            float3 focus = _focus();
            Shader.SetGlobalVector(FarFocusId, new Vector4(focus.x, focus.y, focus.z, 0f));
            if (!math.all(math.isfinite(_lastFocus)) || math.distance(focus, _lastFocus) > RefreshMoveM)
            {
                _lastFocus = focus;
                Refresh(_config.ToDirection(focus));
            }

            for (int n = 0; n < TilesPerFrame && _pending.Count > 0; n++)
            {
                // Nearest first: the tiles just past the near field are the ones on screen.
                float3 up = _config.ToDirection(focus);
                int best = 0;
                float bestDot = -2f;
                for (int i = 0; i < _pending.Count; i++)
                {
                    float d = math.dot(TileCentre(_pending[i]), up);
                    if (d > bestDot)
                    {
                        bestDot = d;
                        best = i;
                    }
                }

                int id = _pending[best];
                _pending[best] = _pending[_pending.Count - 1];
                _pending.RemoveAt(_pending.Count - 1);
                BuildTile(id);
            }
        }

        private float3 TileCentre(int id)
        {
            int face = id / (_tilesPerFace * _tilesPerFace);
            int rem = id % (_tilesPerFace * _tilesPerFace);
            float u = math.min(1f, -1f + 2f * ((rem % _tilesPerFace) * TileCells + TileCells * 0.5f) / _cellsPerFace);
            float v = math.min(1f, -1f + 2f * ((rem / _tilesPerFace) * TileCells + TileCells * 0.5f) / _cellsPerFace);
            return CubeSphere.FaceToDirection((CubeFace)face, u, v);
        }

        private void Refresh(float3 up)
        {
            float reach = (_radiusM + _tileReachM) / _config.Radius;
            // Chord length on the unit sphere for that arc; the whole planet once the arc passes the antipode.
            float chord = reach >= math.PI ? 2.1f : 2f * math.sin(reach * 0.5f);
            int count = 6 * _tilesPerFace * _tilesPerFace;
            _stale.Clear();
            foreach (int id in _tiles.Keys)
            {
                if (math.distance(TileCentre(id), up) > chord)
                {
                    _stale.Add(id);
                }
            }

            foreach (int id in _stale)
            {
                DestroyTile(_tiles[id]);
                _tiles.Remove(id);
                _pending.Remove(id);
            }

            for (int id = 0; id < count; id++)
            {
                if (!_tiles.ContainsKey(id) && math.distance(TileCentre(id), up) <= chord)
                {
                    _tiles.Add(id, new Tile());
                    _pending.Add(id);
                }
            }
        }

        private void BuildTile(int id)
        {
            Tile tile = _tiles[id];
            tile.Built = true;
            _vertices.Clear();
            _normals.Clear();
            _colors.Clear();
            _anchors.Clear();
            _triangles.Clear();
            int n = _cellsPerFace;
            int face = id / (_tilesPerFace * _tilesPerFace);
            int rem = id % (_tilesPerFace * _tilesPerFace);
            int i0 = (rem % _tilesPerFace) * TileCells;
            int j0 = (rem / _tilesPerFace) * TileCells;
            for (int j = j0; j < math.min(n, j0 + TileCells); j++)
            {
                for (int i = i0; i < math.min(n, i0 + TileCells); i++)
                {
                    long key = ((long)face * n + j) * n + i;
                    if (!_field.TryGetFarItem(key, out ScatterItem item, out FarShape shape, out Color body, out Color accent,
                            out Color top))
                    {
                        continue;
                    }

                    // Same placement as ScatterField.BuildCell: sunk 0.1 m, local +Y = the planet's up there.
                    float3 world = _config.ToWorld(item.Direction, item.AltitudeM - 0.1f);
                    PlanetFrame frame = PlanetFrame.FromUp(item.Direction);
                    Quaternion rotation = Quaternion.LookRotation((Vector3)frame.North, (Vector3)frame.Up) *
                                          Quaternion.AngleAxis(item.YawDeg, Vector3.up);
                    FarSceneryMeshes.Append(shape, _vertices, _normals, _colors, _anchors, _triangles,
                        new Vector3(world.x, world.y, world.z), rotation, Vector3.one * item.SizeM, body, accent, top);
                }
            }

            if (_vertices.Count == 0)
            {
                if (tile.Go != null)
                {
                    tile.Go.SetActive(false);
                }

                return;
            }

            if (tile.Go == null)
            {
                tile.Go = new GameObject("FarTile_" + id);
                tile.Go.transform.SetParent(transform, false);
                // Vertices are in world space: the tile must not inherit a transform.
                tile.Go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                tile.Mesh = new Mesh { name = "FarTile_" + id, indexFormat = IndexFormat.UInt32 };
                tile.Go.AddComponent<MeshFilter>().sharedMesh = tile.Mesh;
                MeshRenderer renderer = tile.Go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                // Past the shadow distance anyway; casting would only cost a second pass per tile.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            tile.Go.SetActive(true);
            tile.Mesh.Clear();
            tile.Mesh.SetVertices(_vertices);
            tile.Mesh.SetNormals(_normals);
            tile.Mesh.SetColors(_colors);
            tile.Mesh.SetUVs(2, _anchors);
            tile.Mesh.SetTriangles(_triangles, 0);
            tile.Mesh.RecalculateBounds();
        }

        // A destroyed or woken item must not come back as a silhouette when the player walks away from it.
        private void OnItemGone(long key)
        {
            int n = _cellsPerFace;
            if (key < 0 || key >= 6L * n * n)
            {
                return; // understory cells have no silhouettes
            }

            int face = (int)(key / ((long)n * n));
            int rem = (int)(key % ((long)n * n));
            int id = (face * _tilesPerFace + rem / n / TileCells) * _tilesPerFace + rem % n / TileCells;
            if (_tiles.TryGetValue(id, out Tile tile) && tile.Built)
            {
                tile.Built = false;
                _pending.Add(id);
            }
        }

        private static void DestroyTile(Tile tile)
        {
            if (tile.Mesh != null)
            {
                Destroy(tile.Mesh);
            }

            if (tile.Go != null)
            {
                Destroy(tile.Go);
            }
        }

        private void OnDestroy()
        {
            if (_field != null)
            {
                _field.ItemGone -= OnItemGone;
            }

            foreach (Tile tile in _tiles.Values)
            {
                DestroyTile(tile);
            }

            _tiles.Clear();
            if (_material != null)
            {
                Destroy(_material);
            }

            Shader.SetGlobalVector(FarFocusId, Vector4.zero);
        }
    }
}
