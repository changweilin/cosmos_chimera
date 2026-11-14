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
    /// D0 presentation of a <see cref="RunLayout"/> (P3-11): draped road ribbons, stone bridges over the sea, and
    /// buildings shaped by <see cref="ArchitectureMeshes"/> in the planet's tradition and era, each settlement mixing in
    /// a secondary style nudged by its region theme.
    /// REQ: docs/design/planet-world.md#道路與建築.
    /// </summary>
    /// <remarks>
    /// Pre: Configure once. Post: one mesh per road and one root per building, all built immediately; geometry depends
    /// only on (layout, ground function), so every client draws the same village.
    /// Why build everything at once, unlike ScatterField: a run has ≈ 10 roads and ≤ 60 buildings — a few thousand
    /// vertices total, cheaper than streaming bookkeeping.
    /// Each building carries one box collider on the rule layer's footprint (it blocks the character); road ribbons and
    /// bridge masonry have none, so the terrain stays the only walking surface.
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
        private const float SinkM = 0.15f;
        private const float ParapetM = 0.75f;
        private const float ParapetWidthM = 0.35f;
        // Rows (metres) between bridge piers; each span between two piers is one arch.
        private const int PierEvery = 10;
        private const int LampEvery = 5;
        private const float ArchRiseM = 2.2f;

        private static readonly Color MainRoad = C(0x8A7F6E);
        private static readonly Color Trail = C(0x9C7A54);
        private static readonly Color BridgeStone = C(0x9A958C);
        private static readonly Color BridgeMoss = C(0x5F7F4A);
        private static readonly Color BridgeLamp = C(0xFFD27A);

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<GameObject> _roads = new List<GameObject>();
        private readonly List<GameObject> _buildings = new List<GameObject>();
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();
        private readonly Dictionary<int, Material[]> _styleMaterials = new Dictionary<int, Material[]>();
        private readonly List<Material> _ownedMaterials = new List<Material>();
        private Material[] _bridgeMaterials;
        private PlanetConfig _config;
        private Func<float3, float> _groundAltitude;
        private Shader _shader;
        private Shader _sceneryShader;
        private ArchPlanetStyle _style;
        private ArchMix[] _mixes;
        private ulong _seed;

        /// <summary>Road meshes built, one per RunLayout road.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public IReadOnlyList<GameObject> Roads => _roads;

        /// <summary>Building roots built, one per RunLayout building, in layout order.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public IReadOnlyList<GameObject> Buildings => _buildings;

        /// <summary>Builds every road and building of <paramref name="layout"/>.</summary>
        /// <remarks>
        /// Pre: config valid and the one the layout was generated for; layout, groundAltitude and shader non-null;
        ///      sceneryShader is Cosmimera/ToonScenery, or null to draw the same meshes with shader (flat colours, no
        ///      weathering or glow); planetTypeId picks the regional tradition (null or unknown: verdant) and seed the
        ///      planet's era; plan, when given, is the plan the layout was generated from (its region themes nudge
        ///      each settlement's style; null: no nudge); called once.
        /// Post: Roads.Count == layout.Roads.Count; Buildings.Count == layout.Buildings.Count. Road vertices sit
        ///       RoadLiftM above the ground (bridge rows: at least BridgeDeckM above sea level); a road with a run of
        ///       two or more bridge rows has a "Bridge" child (parapets, arches, piers, lamps); building bases sit below
        ///       the lowest corner of their footprint, so no wall floats on a slope. Each building root holds one mesh
        ///       with ArchitectureMeshes.SubMeshCount materials and one BoxCollider no wider than its footprint. The
        ///       same (layout, ground, planetTypeId, seed, plan) draws the same settlement on every client.
        /// Side effects: creates GameObjects, meshes and materials; calls groundAltitude.
        /// </remarks>
        public void Configure(in PlanetConfig config, RunLayout layout, Func<float3, float> groundAltitude, Shader shader,
            Shader sceneryShader = null, string planetTypeId = null, ulong seed = 0, RunPlan plan = null)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(layout != null && groundAltitude != null && shader != null,
                "layout, groundAltitude and shader are required");
            Contract.Requires(_roads.Count == 0 && _buildings.Count == 0, "Configure must be called once");
            _config = config;
            _groundAltitude = groundAltitude;
            _shader = shader;
            _sceneryShader = sceneryShader;
            _style = ArchitectureMeshes.PlanetStyleFor(planetTypeId, seed);
            _seed = seed;
            _mixes = new ArchMix[layout.Settlements.Count];
            for (int i = 0; i < _mixes.Length; i++)
            {
                RunSettlement s = layout.Settlements[i];
                string theme = plan != null && s.Region < plan.Regions.Count ? plan.Regions[s.Region].ThemeId : null;
                _mixes[i] = ArchitectureMeshes.MixFor(_style, s.Kind, theme, seed, i);
            }

            foreach (RunRoad road in layout.Roads)
            {
                _roads.Add(BuildRoad(road));
            }

            for (int i = 0; i < layout.Buildings.Count; i++)
            {
                RunBuilding building = layout.Buildings[i];
                _buildings.Add(BuildBuilding(building, i, layout.Settlements[building.Settlement]));
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
            var sides = new float3[rows];
            var decks = new float[rows];
            var floors = new float[rows];
            bool anyBridge = false;
            for (int r = 0; r < rows; r++)
            {
                float3 up = centres[r];
                float3 ahead = centres[math.min(r + 1, rows - 1)] - centres[math.max(r - 1, 0)];
                float3 side = math.normalize(math.cross(up, ahead));
                float rowMax = float.MinValue;
                float rowMin = float.MaxValue;
                for (int c = 0; c < Columns; c++)
                {
                    float offsetM = (c / (Columns - 1f) - 0.5f) * road.WidthM;
                    column[c] = math.normalize(up + side * (offsetM / _config.Radius));
                    ground[c] = _groundAltitude(column[c]);
                    rowMax = math.max(rowMax, ground[c]);
                    rowMin = math.min(rowMin, ground[c]);
                }

                sides[r] = side;
                decks[r] = math.max(BridgeDeckM, rowMax + RoadLiftM);
                floors[r] = rowMin;
                anyBridge |= bridges[r];

                for (int c = 0; c < Columns; c++)
                {
                    // Decks stay flat across their width; on land each column drapes over its own ground.
                    float alt = bridges[r] ? decks[r] : ground[c] + RoadLiftM;
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
            if (anyBridge)
            {
                BuildBridge(go.transform, origin, road.WidthM * 0.5f, centres, sides, bridges, decks, floors);
            }

            return go;
        }

        // Masonry for every run of bridge rows: a parapet on both edges whose outer face drops to an arch between
        // piers, a pier every PierEvery rows standing on the sea floor, and lamp posts. Rows are 1 m apart.
        private void BuildBridge(Transform road, float3 origin, float halfWidthM, List<float3> centres, float3[] sides,
            List<bool> bridges, float[] decks, float[] floors)
        {
            var stone = new ArchitectureMeshes.Geo();
            var glow = new ArchitectureMeshes.Geo();
            int rows = centres.Count;
            int start = -1;
            for (int r = 0; r <= rows; r++)
            {
                bool on = r < rows && bridges[r];
                if (on && start < 0)
                {
                    start = r;
                }
                else if (!on && start >= 0)
                {
                    if (r - start >= 2)
                    {
                        BridgeRun(stone, glow, origin, halfWidthM, centres, sides, decks, floors, start, r - 1);
                    }

                    start = -1;
                }
            }

            if (stone.Part.T.Count == 0)
            {
                return;
            }

            var data = new SceneryMeshes.MeshData(2);
            data.EmitFaceted(stone.Part, 0, 0.8f, null);
            data.EmitFaceted(glow.Part, 1, 0.8f, null);
            Mesh mesh = data.ToMesh("Bridge");
            _meshes.Add(mesh);
            var go = new GameObject("Bridge");
            go.transform.SetParent(road, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            if (_bridgeMaterials == null)
            {
                _bridgeMaterials = _sceneryShader != null
                    ? new[]
                    {
                        Own(PlanetLook.SceneryMaterial(_sceneryShader, BridgeStone, BridgeMoss, 0.7f, 0f, 0f, 0f)),
                        Own(PlanetLook.SceneryMaterial(_sceneryShader, BridgeLamp, BridgeLamp, 0f, 0f, 0f, 0.9f)),
                    }
                    : new[] { MaterialFor(BridgeStone, 0f), MaterialFor(BridgeLamp, 0f) };
            }

            go.AddComponent<MeshRenderer>().sharedMaterials = _bridgeMaterials;
        }

        private void BridgeRun(ArchitectureMeshes.Geo stone, ArchitectureMeshes.Geo glow, float3 origin, float halfWidthM,
            List<float3> centres, float3[] sides, float[] decks, float[] floors, int first, int last)
        {
            float3 At(int r, float lateralM, float alt) =>
                _config.ToWorld(math.normalize(centres[r] + sides[r] * (lateralM / _config.Radius)), alt) - origin;

            // How far the outer face hangs below the deck: shallow at mid-span, down to the pier at each end of a span.
            float Drop(int r)
            {
                float t = ((r - first) % PierEvery) / (float)PierEvery;
                float x = 2f * t - 1f;
                float arch = 0.5f + ArchRiseM * (1f - math.sqrt(math.max(0f, 1f - x * x)));
                return math.min(arch, math.max(0.5f, decks[r] - floors[r] + 0.5f));
            }

            for (int e = -1; e <= 1; e += 2)
            {
                float inner = e * (halfWidthM - 0.05f), outer = e * (halfWidthM + ParapetWidthM);
                int count = last - first + 1;
                var low = new int[count];
                var outTop = new int[count];
                var inTop = new int[count];
                var inFoot = new int[count];
                for (int r = first; r <= last; r++)
                {
                    int i = r - first;
                    float deck = decks[r], foot = deck - Drop(r);
                    // Weed and damp climb the stone from the waterline (accent mask = 1 − a).
                    low[i] = stone.V(At(r, outer, foot), 0.66f, math.saturate((foot + 0.2f) / 1.5f));
                    outTop[i] = stone.V(At(r, outer, deck + ParapetM), 1f, 1f);
                    inTop[i] = stone.V(At(r, inner, deck + ParapetM), 1f, 0.75f);
                    inFoot[i] = stone.V(At(r, inner, deck - 0.05f), 0.8f, 1f);
                }

                for (int i = 0; i + 1 < count; i++)
                {
                    int r = first + i;
                    float3 up = centres[r], side = sides[r] * e;
                    stone.Quad(low[i], low[i + 1], outTop[i + 1], outTop[i], side, (i / 2 & 1) == 0 ? 1f : 0.95f);
                    stone.Quad(outTop[i], outTop[i + 1], inTop[i + 1], inTop[i], up, 1.04f);
                    stone.Quad(inTop[i], inTop[i + 1], inFoot[i + 1], inFoot[i], -side, 0.9f);
                }

                for (int r = first; r <= last; r++)
                {
                    if ((r - first) % LampEvery != 0)
                    {
                        continue;
                    }

                    float3 up = centres[r], side = sides[r];
                    float3 forward = math.cross(side, up);
                    float3 foot = At(r, e * (halfWidthM + ParapetWidthM * 0.5f - 0.03f), decks[r] - 0.2f);
                    Post(stone, foot, side, forward, up, 0.27f, ParapetM + 0.65f, 0.78f, 1.02f);
                    if ((r - first) % (LampEvery * 2) == 0)
                    {
                        Post(glow, foot + up * (ParapetM + 0.65f), side, forward, up, 0.16f, 0.34f, 1f, 1.1f);
                    }
                }
            }

            for (int r = first; r <= last; r += PierEvery)
            {
                float top = decks[r] - 0.45f, bottom = floors[r] - 1f;
                if (top - bottom < 1.2f)
                {
                    continue; // the deck already rests on the shore here
                }

                float3 up = centres[r], side = sides[r];
                float3 forward = math.cross(side, up);
                float3 head = At(r, 0f, top), foot = At(r, 0f, bottom);
                float half = halfWidthM + ParapetWidthM;
                var c = new float3[8];
                for (int k = 0; k < 4; k++)
                {
                    float sx = k == 0 || k == 3 ? -1f : 1f, sz = k < 2 ? -1f : 1f;
                    c[k] = foot + side * (sx * half * 1.12f) + forward * (sz * 1.15f);
                    c[4 + k] = head + side * (sx * half) + forward * (sz * 0.85f);
                }

                PostFaces(stone, c, 0.6f, 0.9f, math.saturate((bottom + 0.2f) / 1.5f), 1f);
            }
        }

        private static void Post(ArchitectureMeshes.Geo geo, float3 foot, float3 side, float3 forward, float3 up, float half,
            float height, float toneFoot, float toneHead)
        {
            var c = new float3[8];
            for (int k = 0; k < 4; k++)
            {
                float sx = k == 0 || k == 3 ? -1f : 1f, sz = k < 2 ? -1f : 1f;
                c[k] = foot + side * (sx * half) + forward * (sz * half);
                c[4 + k] = c[k] + up * height;
            }

            PostFaces(geo, c, toneFoot, toneHead, 1f, 1f);
        }

        // Four sides and a cap of a box given as a ring of four corners and the ring above it.
        private static void PostFaces(ArchitectureMeshes.Geo geo, float3[] c, float toneFoot, float toneHead, float accentFoot,
            float accentHead)
        {
            float3 inside = float3.zero;
            foreach (float3 p in c)
            {
                inside += p * 0.125f;
            }

            for (int k = 0; k < 4; k++)
            {
                int n = (k + 1) & 3;
                geo.Quad(geo.V(c[k], toneFoot, accentFoot), geo.V(c[n], toneFoot, accentFoot), geo.V(c[4 + n], toneHead, accentHead),
                    geo.V(c[4 + k], toneHead, accentHead), (c[k] + c[n] + c[4 + n] + c[4 + k]) * 0.25f - inside, (k & 1) == 0 ? 1f : 0.94f);
            }

            geo.Quad(geo.V(c[4], toneHead, accentHead), geo.V(c[5], toneHead, accentHead), geo.V(c[6], toneHead, accentHead),
                geo.V(c[7], toneHead, accentHead), (c[4] + c[5] + c[6] + c[7]) * 0.25f - inside, 1.04f);
        }

        private GameObject BuildBuilding(RunBuilding b, int index, RunSettlement settlement)
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

            // Local frame: +Z = Facing (entrance), +Y = planet up, +X = across. The slope (hi − lo) is added below the
            // entrance floor so the uphill side still shows full storeys.
            ArchEra era = ArchitectureMeshes.SettlementEra(_style, settlement.Kind);
            // Golden-ratio stride: neighbouring lots get unrelated variation streams from one planet seed.
            var spec = new ArchSpec(b.Kind, _style.Region, era, b.WidthM, b.DepthM, b.Storeys, hi - lo + SinkM,
                _seed ^ ((ulong)(index + 1) * 0x9E3779B97F4A7C15ul), _mixes[settlement.Index]);
            Mesh mesh = ArchitectureMeshes.Build(spec);
            _meshes.Add(mesh);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterials = MaterialsFor(era, b.Kind, settlement.Index);
            // One box on the rule layer's footprint: the character stops where creatures do (planet-world.md#道路與建築);
            // roofs and porches overhang it without blocking.
            Vector3 body = ArchitectureMeshes.BodySize(spec);
            var box = root.AddComponent<BoxCollider>();
            box.size = body;
            box.center = new Vector3(0f, body.y * 0.5f, 0f);
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

        private Material[] MaterialsFor(ArchEra era, BuildingKind kind, int settlement)
        {
            // Megaliths share one stone everywhere; everything else is coloured by its settlement's style mix (a
            // building's own shade is baked into its vertex colours, so one material set serves the whole settlement).
            int key = kind == BuildingKind.Pillar ? -1 : settlement;
            if (_styleMaterials.TryGetValue(key, out Material[] set))
            {
                return set;
            }

            ArchPalette p = ArchitectureMeshes.PaletteFor(_style.Region, era, kind, _mixes[settlement]);
            set = new Material[ArchitectureMeshes.SubMeshCount];
            if (_sceneryShader != null)
            {
                set[ArchitectureMeshes.WallSubMesh] =
                    Own(PlanetLook.SceneryMaterial(_sceneryShader, p.Wall, p.Weathering, p.WeatheringStrength, 0f, 0f, 0f));
                set[ArchitectureMeshes.RoofSubMesh] =
                    Own(PlanetLook.SceneryMaterial(_sceneryShader, p.Roof, p.Weathering, p.WeatheringStrength, 0f, 0f, 0f));
                set[ArchitectureMeshes.TrimSubMesh] = Own(PlanetLook.SceneryMaterial(_sceneryShader, p.Trim, p.Trim, 0f, 0f, 0f, 0f));
                set[ArchitectureMeshes.GlowSubMesh] = Own(PlanetLook.SceneryMaterial(_sceneryShader, p.Glow, p.Glow, 0f, 0f, 0f, 0.9f));
            }
            else
            {
                set[ArchitectureMeshes.WallSubMesh] = MaterialFor(p.Wall, 0.03f);
                set[ArchitectureMeshes.RoofSubMesh] = MaterialFor(p.Roof, 0.03f);
                set[ArchitectureMeshes.TrimSubMesh] = MaterialFor(p.Trim, 0.03f);
                set[ArchitectureMeshes.GlowSubMesh] = MaterialFor(p.Glow, 0.03f);
            }

            _styleMaterials[key] = set;
            return set;
        }

        private Material Own(Material material)
        {
            _ownedMaterials.Add(material);
            return material;
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

            foreach (Material m in _ownedMaterials)
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
