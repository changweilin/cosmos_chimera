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
    /// D0 presentation of a <see cref="RunLayout"/> (P3-11): draped road ribbons and placeholder building models.
    /// REQ: docs/design/planet-world.md#道路與建築.
    /// </summary>
    /// <remarks>
    /// Pre: Configure once. Post: one mesh per road and one root per building, all built immediately; geometry depends
    /// only on (layout, ground function), so every client draws the same village.
    /// Why build everything at once, unlike ScatterField: a run has ≈ 10 roads and ≤ 60 buildings — a few thousand
    /// vertices total, cheaper than streaming bookkeeping.
    /// Building parts keep their primitive colliders (they block the character); road ribbons have none, so the terrain
    /// stays the only walking surface.
    /// Side effects: creates GameObjects, meshes and materials under this component and destroys them with it.
    /// </remarks>
    public sealed class SettlementView : MonoBehaviour
    {
        // Matches the terrain's finest vertex spacing (PlanetTerrain: 1 m), so the ribbon follows every bump it can see.
        private const float SampleM = 1f;
        private const int Columns = 5;
        // Clears the terrain mesh's linear interpolation between its 1 m vertices on rough ground without visibly floating.
        private const float RoadLiftM = 0.12f;
        // Bridge deck height above sea level (altitude 0); the terrain shader paints water below 0.
        private const float BridgeDeckM = 0.6f;
        private const float StoreyM = 3f;
        private const float SinkM = 0.15f;

        private static readonly Color MainRoad = C(0x8A7F6E);
        private static readonly Color Trail = C(0x9C7A54);
        private static readonly Color Canvas = C(0xD8C8A0);
        private static readonly Color Wall = C(0xE8DCC0);
        private static readonly Color Roof = C(0xA4553A);
        private static readonly Color Door = C(0x3A2A20);
        private static readonly Color RuinStone = C(0x8C8880);
        private static readonly Color LairStone = C(0x4A4450);

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<GameObject> _roads = new List<GameObject>();
        private readonly List<GameObject> _buildings = new List<GameObject>();
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();
        private PlanetConfig _config;
        private Func<float3, float> _groundAltitude;
        private Shader _shader;

        /// <summary>Road meshes built, one per RunLayout road.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public IReadOnlyList<GameObject> Roads => _roads;

        /// <summary>Building roots built, one per RunLayout building, in layout order.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public IReadOnlyList<GameObject> Buildings => _buildings;

        /// <summary>Builds every road and building of <paramref name="layout"/>.</summary>
        /// <remarks>
        /// Pre: config valid and the one the layout was generated for; layout, groundAltitude and shader non-null;
        ///      called once.
        /// Post: Roads.Count == layout.Roads.Count; Buildings.Count == layout.Buildings.Count. Road vertices sit
        ///       RoadLiftM above the ground (bridge rows: at least BridgeDeckM above sea level); building bases sit
        ///       below the lowest corner of their footprint, so no wall floats on a slope.
        /// Side effects: creates GameObjects, meshes and materials; calls groundAltitude.
        /// </remarks>
        public void Configure(in PlanetConfig config, RunLayout layout, Func<float3, float> groundAltitude, Shader shader)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(layout != null && groundAltitude != null && shader != null,
                "layout, groundAltitude and shader are required");
            Contract.Requires(_roads.Count == 0 && _buildings.Count == 0, "Configure must be called once");
            _config = config;
            _groundAltitude = groundAltitude;
            _shader = shader;

            foreach (RunRoad road in layout.Roads)
            {
                _roads.Add(BuildRoad(road));
            }

            foreach (RunBuilding building in layout.Buildings)
            {
                _buildings.Add(BuildBuilding(building, layout.Settlements[building.Settlement].Kind));
            }
        }

        private GameObject BuildRoad(RunRoad road)
        {
            var centres = new List<float3>();
            var bridges = new List<bool>();
            IReadOnlyList<float3> pts = road.Points;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                int steps = math.max(1, (int)math.ceil(math.distance(pts[i], pts[i + 1]) * _config.Radius / SampleM));
                for (int s = 0; s < steps; s++)
                {
                    float t = (float)s / steps;
                    centres.Add(math.normalize(math.lerp(pts[i], pts[i + 1], t)));
                    // A row between a land and a sea vertex counts as bridge, so the deck starts on the shore.
                    bridges.Add(road.Bridge[i] || road.Bridge[i + 1]);
                }
            }

            centres.Add(pts[pts.Count - 1]);
            bridges.Add(road.Bridge[pts.Count - 1]);

            int rows = centres.Count;
            float3 origin = _config.ToWorld(centres[0], _groundAltitude(centres[0]));
            var vertices = new Vector3[rows * Columns];
            var column = new float3[Columns];
            var ground = new float[Columns];
            for (int r = 0; r < rows; r++)
            {
                float3 up = centres[r];
                float3 ahead = centres[math.min(r + 1, rows - 1)] - centres[math.max(r - 1, 0)];
                float3 side = math.normalize(math.cross(up, ahead));
                float rowMax = float.MinValue;
                for (int c = 0; c < Columns; c++)
                {
                    float offsetM = (c / (Columns - 1f) - 0.5f) * road.WidthM;
                    column[c] = math.normalize(up + side * (offsetM / _config.Radius));
                    ground[c] = _groundAltitude(column[c]);
                    rowMax = math.max(rowMax, ground[c]);
                }

                for (int c = 0; c < Columns; c++)
                {
                    // Decks stay flat across their width; on land each column drapes over its own ground.
                    float alt = bridges[r] ? math.max(BridgeDeckM, rowMax + RoadLiftM) : ground[c] + RoadLiftM;
                    float3 p = _config.ToWorld(column[c], alt) - origin;
                    vertices[r * Columns + c] = new Vector3(p.x, p.y, p.z);
                }
            }

            var triangles = new int[(rows - 1) * (Columns - 1) * 6];
            int k = 0;
            for (int r = 0; r + 1 < rows; r++)
            {
                for (int c = 0; c + 1 < Columns; c++)
                {
                    int a = r * Columns + c;
                    int b = a + Columns;
                    // cross(ahead, side) == up, so (a, b, a+1) winds front-facing toward the sky.
                    triangles[k++] = a;
                    triangles[k++] = b;
                    triangles[k++] = a + 1;
                    triangles[k++] = a + 1;
                    triangles[k++] = b;
                    triangles[k++] = b + 1;
                }
            }

            var mesh = new Mesh { name = "Road" };
            if (vertices.Length > ushort.MaxValue)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _meshes.Add(mesh);

            var go = new GameObject(road.Class == RoadClass.Main ? "Road_Main" : "Road_Trail");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(new Vector3(origin.x, origin.y, origin.z), Quaternion.identity);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialFor(road.Class == RoadClass.Main ? MainRoad : Trail, 0f);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        private GameObject BuildBuilding(RunBuilding b, SettlementKind settlement)
        {
            float3 up = b.Direction;
            float3 across = math.normalize(math.cross(up, b.Facing));
            float lo = _groundAltitude(up);
            float hi = lo;
            for (int i = 0; i < 4; i++)
            {
                float3 corner = b.Facing * ((i & 1) == 0 ? 0.5f : -0.5f) * b.DepthM +
                                across * ((i & 2) == 0 ? 0.5f : -0.5f) * b.WidthM;
                float alt = _groundAltitude(math.normalize(up + corner / _config.Radius));
                lo = math.min(lo, alt);
                hi = math.max(hi, alt);
            }

            float baseAlt = lo - SinkM;
            float3 world = _config.ToWorld(up, baseAlt);
            var root = new GameObject("Building_" + b.Kind);
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(new Vector3(world.x, world.y, world.z),
                Quaternion.LookRotation((Vector3)b.Facing, (Vector3)up));

            // Local frame: +Z = Facing (entrance), +Y = planet up, +X = across. The slope (hi − lo) is added to the walls
            // so the uphill side still shows full storeys.
            float slack = hi - lo + SinkM;
            float w = b.WidthM;
            float d = b.DepthM;
            bool ruined = settlement == SettlementKind.Ruins;
            switch (b.Kind)
            {
                case BuildingKind.Tent:
                {
                    float side = w / math.SQRT2;
                    Part(root.transform, PrimitiveType.Cube, Canvas, new Vector3(side, side, d),
                        new Vector3(0f, slack, 0f), Quaternion.Euler(0f, 0f, 45f));
                    break;
                }
                case BuildingKind.Pillar:
                {
                    float h = b.Storeys * StoreyM + slack;
                    Part(root.transform, PrimitiveType.Cylinder, LairStone, new Vector3(w, h * 0.5f, d),
                        new Vector3(0f, h * 0.5f, 0f));
                    break;
                }
                default:
                {
                    // Ruins keep only their ground floor and lose their roofs (abandoned 聚落遺跡).
                    int storeys = ruined ? 1 : b.Storeys;
                    float h = storeys * StoreyM * (ruined ? 0.6f : 1f) + slack;
                    Part(root.transform, PrimitiveType.Cube, ruined ? RuinStone : Wall, new Vector3(w, h, d),
                        new Vector3(0f, h * 0.5f, 0f));
                    if (ruined)
                    {
                        break;
                    }

                    if (b.Kind == BuildingKind.Tower)
                    {
                        Part(root.transform, PrimitiveType.Cube, Roof, new Vector3(w + 0.4f, 0.4f, d + 0.4f),
                            new Vector3(0f, h + 0.2f, 0f));
                    }
                    else
                    {
                        // A cube turned 45° about the ridge (local Z) reads as a gable roof; its lower half hides in the walls.
                        float side = w * 0.75f;
                        Part(root.transform, PrimitiveType.Cube, Roof, new Vector3(side, side, d + 0.4f),
                            new Vector3(0f, h, 0f), Quaternion.Euler(0f, 0f, 45f));
                    }

                    Part(root.transform, PrimitiveType.Cube, Door, new Vector3(1.1f, 2f, 0.1f),
                        new Vector3(0f, slack + 1f, d * 0.5f + 0.03f));
                    break;
                }
            }

            return root;
        }

        /// <summary>Hides building <paramref name="index"/> and its colliders (it was destroyed; destructibles.md#碰撞).</summary>
        /// <remarks>
        /// Pre: Configure ran; 0 &lt;= index &lt; Buildings.Count. Post: Buildings[index] is inactive (indices stay aligned
        ///      with RunLayout.Buildings). Side effects: deactivates a GameObject.
        /// </remarks>
        public void RemoveBuilding(int index)
        {
            Contract.Requires(index >= 0 && index < _buildings.Count, "index out of range");
            _buildings[index].SetActive(false);
        }

        private void Part(Transform parent, PrimitiveType type, Color color, Vector3 scale, Vector3 localPos,
            Quaternion? rotation = null)
        {
            // Keeps the primitive's collider: buildings block the character (REQ: planet-world.md#道路與建築); creatures
            // get the same footprints through IEncounterSurface.IsBlocked.
            GameObject go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.transform.localRotation = rotation ?? Quaternion.identity;
            go.GetComponent<Renderer>().sharedMaterial = MaterialFor(color, 0.03f);
        }

        private Material MaterialFor(Color color, float outlineWidthM)
        {
            if (!_materials.TryGetValue(color, out Material m))
            {
                m = PlanetLook.CreatureMaterial(_shader, color, outlineWidthM);
                _materials[color] = m;
            }

            return m;
        }

        private void OnDestroy()
        {
            foreach (Material m in _materials.Values)
            {
                if (m != null)
                {
                    Destroy(m);
                }
            }

            foreach (Mesh mesh in _meshes)
            {
                if (mesh != null)
                {
                    Destroy(mesh);
                }
            }
        }

        private static Color C(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
