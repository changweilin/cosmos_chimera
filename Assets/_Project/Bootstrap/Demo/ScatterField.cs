using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// D0 scatter (P3-09): draws the trees, rocks and crystals <see cref="ScatterPlanner"/> places around the player,
    /// as procedural meshes (<see cref="SceneryMeshes"/>) shaded by Cosmimera/ToonScenery.
    /// REQ: planet-world.md (7. 散佈); destructibles.md (placement lives in the rule layer, items block and can break).
    /// </summary>
    /// <remarks>
    /// Pre: Configure once. Post: the set of live objects depends only on (seed, planet type, player position), so a
    /// cell always yields the same item whenever it streams in; items never float (altitude comes from the same
    /// ground function the terrain mesh uses) and never stand in water or on steep slopes.
    /// Why streaming and not baking the whole planet: R=640 m is ~5 Mm² of surface; only the horizon (~47 m) is visible.
    /// The trunk, rock body or crystal shaft keeps its primitive collider (destructibles.md#碰撞: trees and rocks block the
    /// character) on a "Block" child; the drawn "Model" child has none. Destroyed items (isDestroyed) are never drawn.
    /// Side effects: creates/destroys GameObjects under this component; owns its materials and meshes.
    /// </remarks>
    public sealed class ScatterField : MonoBehaviour
    {
        private readonly struct Palette
        {
            public readonly Color Color;  // crown, rock body or crystal
            public readonly Color Accent; // trunk (trees only)
            public readonly Color Top;    // what settles on upward faces: sunlit leaves, moss, snow, ash; crystal tips
            public readonly float Glow;   // self-lit crystals

            public Palette(Color color, Color accent, Color top, float glow = 0f)
            {
                Color = color;
                Accent = accent;
                Top = top;
                Glow = glow;
            }
        }

        private enum Role
        {
            Bark = 0,
            Leaf = 1,
            Rock = 2,
            Crystal = 3,
        }

        private const float RadiusM = 90f;
        private const float RefreshMoveM = 6f;
        private const float RefreshSeconds = 0.5f;
        // Metres of lean per metre of height in a full gust: a 6 m tree's crown travels ~0.4 m.
        private const float TreeSway = 0.06f;
        private const float GroveCellM = 45f;

        // Same order as ScatterPlanner.KindsFor(type): colour is presentation, the rest is rule layer.
        private static readonly Dictionary<string, Palette[]> Palettes = new Dictionary<string, Palette[]>
        {
            ["verdant_planet"] = new[] { new Palette(C(0x3F7F36), C(0x6B4A2B), C(0x9CBF4A)), new Palette(C(0x6E6A62), C(0x6E6A62), C(0x5E8A3A)) },
            ["lava_planet"] = new[] { new Palette(C(0x2E2422), C(0x2E2422), C(0x5A4640)), new Palette(C(0xFF8A1E), C(0xFF8A1E), C(0xFFE08A), 0.9f) },
            ["ice_crystal_planet"] = new[] { new Palette(C(0x9FD8F0), C(0x9FD8F0), C(0xF2FBFF), 0.2f), new Palette(C(0xDCEBF2), C(0xDCEBF2), C(0xFFFFFF)) },
            ["storm_planet"] = new[] { new Palette(C(0x5E7050), C(0x4A4038), C(0x8A9A6A)), new Palette(C(0x5C5A60), C(0x5C5A60), C(0x7A8A5A)) },
            ["mineral_planet"] = new[] { new Palette(C(0xE0B060), C(0xE0B060), C(0xFFF0C0), 0.3f), new Palette(C(0x8C4E36), C(0x8C4E36), C(0xC89868)) },
            ["twilight_planet"] = new[] { new Palette(C(0x8A6AC8), C(0x3E3050), C(0xD0A8F0)), new Palette(C(0xE070B0), C(0xE070B0), C(0xFFD0F0), 0.6f) },
        };

        private PlanetConfig _config;
        private ScatterPlanner _planner;
        private Palette[] _palette;
        private int _cellsPerFace;
        private Func<long, bool> _isDestroyed;
        private Func<float3> _focus;
        private Shader _shader;
        private Shader _sceneryShader;
        private string _planetTypeId;
        private readonly Dictionary<long, List<GameObject>> _live = new Dictionary<long, List<GameObject>>();
        private readonly HashSet<long> _wanted = new HashSet<long>();
        private readonly List<long> _stale = new List<long>();
        // Items handed to ObjectPhysics (woken rigidbodies): no longer streamed, never rebuilt at their home spot.
        private readonly HashSet<long> _detached = new HashSet<long>();
        private readonly Dictionary<(Color, Role), Material> _materials = new Dictionary<(Color, Role), Material>();
        private readonly Dictionary<int, Mesh> _meshes = new Dictionary<int, Mesh>();
        private float3 _lastFocus;
        private float _nextRefresh;
        private bool _configured;

        /// <summary>Number of live scattered objects (roots), for tests and diagnostics.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public int LiveCount
        {
            get
            {
                int n = 0;
                foreach (List<GameObject> l in _live.Values)
                {
                    n += l.Count;
                }

                return n;
            }
        }

        /// <summary>Starts scattering around <paramref name="focus"/>.</summary>
        /// <remarks>
        /// Pre: groundAltitude, focus and shader non-null; isSea may be null (no sea mask on pure-noise planets);
        ///      isClear may be null (no roads or buildings to avoid) and must be deterministic; sceneryShader as in the
        ///      other overload.
        /// Post: Update streams cells in and out; no item stands where isClear is false. Side effects: none until Update.
        /// </remarks>
        public void Configure(in PlanetConfig config, ulong seed, string planetTypeId, Func<float3, float> groundAltitude,
            Func<float3, bool> isSea, Func<float3> focus, Shader shader, Func<float3, bool> isClear = null,
            Shader sceneryShader = null)
        {
            Contract.Requires(groundAltitude != null && focus != null && shader != null,
                "groundAltitude, focus and shader are required");
            Configure(config, new ScatterPlanner(config, seed, planetTypeId, groundAltitude, isSea, isClear), planetTypeId,
                focus, shader, null, sceneryShader);
        }

        /// <summary>Starts drawing <paramref name="planner"/>'s items around <paramref name="focus"/>.</summary>
        /// <remarks>
        /// Pre: config is the planner's planet; planner, focus and shader non-null; planetTypeId is the one the planner was
        ///      built with; isDestroyed may be null (nothing destroyed) and, once true for a key, stays true;
        ///      sceneryShader is Cosmimera/ToonScenery, or null to draw the same meshes with shader (flat colour, no
        ///      wind: the optional-layer convention of SceneryShaders).
        /// Post: Update streams cells in and out; destroyed items are never drawn. Side effects: none until Update.
        /// </remarks>
        public void Configure(in PlanetConfig config, ScatterPlanner planner, string planetTypeId, Func<float3> focus,
            Shader shader, Func<long, bool> isDestroyed, Shader sceneryShader = null)
        {
            Contract.Requires(planner != null && focus != null && shader != null, "planner, focus and shader are required");
            _config = config;
            _planner = planner;
            _palette = planetTypeId != null && Palettes.TryGetValue(planetTypeId, out Palette[] p) ? p : Palettes["verdant_planet"];
            _cellsPerFace = planner.CellsPerFace;
            _isDestroyed = isDestroyed;
            _focus = focus;
            _shader = shader;
            _sceneryShader = sceneryShader;
            _planetTypeId = planetTypeId;
            _configured = true;
            _lastFocus = new float3(float.NaN);
            _nextRefresh = 0f;
        }

        /// <summary>Removes the drawn item of cell <paramref name="key"/> (it was destroyed); it never streams back.</summary>
        /// <remarks>
        /// Pre: Configure called with an isDestroyed that now returns true for key.
        /// Post: no GameObject for key. Side effects: destroys GameObjects.
        /// </remarks>
        public void Remove(long key)
        {
            if (!_live.TryGetValue(key, out List<GameObject> objects))
            {
                return;
            }

            foreach (GameObject go in objects)
            {
                Destroy(go);
            }

            objects.Clear();
        }

        /// <summary>Hands the drawn item of cell <paramref name="key"/> to the caller; the cell never draws it again.</summary>
        /// <remarks>
        /// REQ: ADR-0009 §3 — a woken object becomes a rigidbody that may roll out of its cell, so streaming must neither
        /// destroy it with the cell nor rebuild a second copy at home. Pre: Configure called.
        /// Post: returns the item's root (null when the cell is not drawn or holds nothing); key is never built again;
        ///       the caller owns the root. Side effects: stops tracking it.
        /// </remarks>
        public GameObject Detach(long key)
        {
            _detached.Add(key);
            if (!_live.TryGetValue(key, out List<GameObject> objects) || objects.Count == 0)
            {
                return null;
            }

            GameObject root = objects[0];
            objects.Clear();
            return root;
        }

        /// <summary>Spawns the item of one cell, or none.</summary>
        /// <remarks>
        /// Pre: Configure called; key encodes (face, j, i). Post: ≤ 1 item rooted at the ground of that cell.
        /// Side effects: creates GameObjects.
        /// </remarks>
        private void BuildCell(long key)
        {
            var objects = new List<GameObject>();
            _live[key] = objects;
            if (_detached.Contains(key) || !_planner.TryGetItem(key, out ScatterItem item) ||
                (_isDestroyed != null && _isDestroyed(key)))
            {
                return;
            }

            float3 world = _config.ToWorld(item.Direction, item.AltitudeM - 0.1f); // sink slightly so slopes never expose the base
            PlanetFrame frame = PlanetFrame.FromUp(item.Direction);
            var root = new GameObject("Scatter_" + item.Shape);
            root.transform.SetParent(transform, false);
            root.transform.position = new Vector3(world.x, world.y, world.z);
            // Local Y is already the planet's up here (LookRotation above), so Vector3.up is only the local yaw axis.
            root.transform.rotation = Quaternion.LookRotation((Vector3)frame.North, (Vector3)frame.Up) *
                                      Quaternion.AngleAxis(item.YawDeg, Vector3.up);
            Build(root.transform, item, _palette[item.KindIndex]);
            objects.Add(root);
        }

        private void Build(Transform parent, in ScatterItem item, Palette kind)
        {
            float size = item.SizeM;
            // Presentation-only variety from the cell key: which mesh, and a little girth. The rule layer's size, yaw
            // and collider are untouched, so the same item blocks the same way whatever it looks like.
            uint hash = Hash(item.CellKey);
            float girth = 0.9f + ((hash >> 8) & 0xFF) / 255f * 0.24f;
            switch (item.Shape)
            {
                case ScatterShape.Tree:
                {
                    // Colliders keep the primitive shapes ScatterItem.BlockRadiusM / VolumeL were derived from.
                    CapsuleCollider trunk = Block<CapsuleCollider>(parent, new Vector3(0f, size * 0.25f, 0f), Quaternion.identity);
                    trunk.radius = size * 0.06f;
                    trunk.height = size * 0.5f;
                    Model(parent, MeshFor(TreeArchetype(item), hash), new Vector3(size * girth, size, size * girth),
                        MaterialFor(kind.Accent, Role.Bark, kind), MaterialFor(kind.Color, Role.Leaf, kind));
                    break;
                }

                case ScatterShape.Rock:
                {
                    SphereCollider body = Block<SphereCollider>(parent, new Vector3(0f, size * 0.25f, 0f), Quaternion.identity);
                    body.radius = size * 0.5f;
                    Model(parent, MeshFor(SceneryArchetype.Boulder, hash), Vector3.one * size, MaterialFor(kind.Color, Role.Rock, kind));
                    break;
                }

                default:
                {
                    BoxCollider shaft = Block<BoxCollider>(parent, new Vector3(0f, size * 0.5f, 0f), Quaternion.Euler(0f, 45f, 8f));
                    shaft.size = new Vector3(size * 0.28f, size, size * 0.28f);
                    Model(parent, MeshFor(SceneryArchetype.CrystalCluster, hash), new Vector3(size * girth, size, size * girth),
                        MaterialFor(kind.Color, Role.Crystal, kind));
                    break;
                }
            }
        }

        // Which tree grows here is a slow field over the planet, so species form groves instead of a salt-and-pepper mix.
        private SceneryArchetype TreeArchetype(in ScatterItem item)
        {
            switch (_planetTypeId)
            {
                case "storm_planet":
                    return SceneryArchetype.Conifer;
                case "twilight_planet":
                    return SceneryArchetype.Umbrella;
                default:
                    return noise.snoise(item.Direction * (_config.Radius / GroveCellM)) > 0.3f
                        ? SceneryArchetype.Conifer
                        : SceneryArchetype.Broadleaf;
            }
        }

        // The one collider of an item (destructibles.md#碰撞: only the trunk / body / shaft blocks).
        private static T Block<T>(Transform parent, Vector3 localPos, Quaternion rotation) where T : Collider
        {
            var go = new GameObject("Block");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rotation;
            return go.AddComponent<T>();
        }

        private static void Model(Transform parent, Mesh mesh, Vector3 scale, params Material[] materials)
        {
            var go = new GameObject("Model");
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }

        private Mesh MeshFor(SceneryArchetype archetype, uint hash)
        {
            int variant = (int)(hash % (uint)SceneryMeshes.VariantCount(archetype));
            int id = (int)archetype * 16 + variant;
            if (!_meshes.TryGetValue(id, out Mesh mesh))
            {
                mesh = SceneryMeshes.Build(archetype, variant);
                _meshes[id] = mesh;
            }

            return mesh;
        }

        private Material MaterialFor(Color color, Role role, Palette kind)
        {
            if (!_materials.TryGetValue((color, role), out Material m))
            {
                if (_sceneryShader == null)
                {
                    // Hairline only: vision.md keeps outlines for characters and creatures; scenery stays soft.
                    m = PlanetLook.CreatureMaterial(_shader, color, 0.012f);
                }
                else
                {
                    m = role switch
                    {
                        // Bark's accent is the ground-hugging tone at the root flare; a darker bark reads as damp wood.
                        Role.Bark => PlanetLook.SceneryMaterial(_sceneryShader, color, Tone(color, 0.72f), 0.8f, TreeSway, 0f, 0f),
                        Role.Leaf => PlanetLook.SceneryMaterial(_sceneryShader, color, kind.Top, 0.85f, TreeSway, 0.55f, 0f),
                        Role.Rock => PlanetLook.SceneryMaterial(_sceneryShader, color, kind.Top, 0.8f, 0f, 0f, 0f),
                        _ => PlanetLook.SceneryMaterial(_sceneryShader, color, kind.Top, 0.7f, 0f, 0f, kind.Glow),
                    };
                }

                _materials[(color, role)] = m;
            }

            return m;
        }

        // SplitMix64 finaliser: neighbouring cell keys must not pick neighbouring variants.
        private static uint Hash(long key)
        {
            ulong z = (ulong)key + 0x9E3779B97F4A7C15ul;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
            return (uint)((z ^ (z >> 31)) >> 16);
        }

        private void CollectCells(float3 up) => _planner.CollectCells(up, RadiusM, _wanted);

        private void Update()
        {
            if (!_configured)
            {
                return;
            }

            float3 focus = _focus();
            bool moved = !math.all(math.isfinite(_lastFocus)) || math.distance(focus, _lastFocus) > RefreshMoveM;
            if (!moved && Time.time < _nextRefresh)
            {
                return;
            }

            _lastFocus = focus;
            _nextRefresh = Time.time + RefreshSeconds;
            Refresh(focus);
        }

        private void Refresh(float3 focus)
        {
            float3 up = _config.ToDirection(focus);
            CollectCells(up);

            _stale.Clear();
            foreach (long key in _live.Keys)
            {
                if (!_wanted.Contains(key))
                {
                    _stale.Add(key);
                }
            }

            foreach (long key in _stale)
            {
                foreach (GameObject go in _live[key])
                {
                    Destroy(go);
                }

                _live.Remove(key);
            }

            foreach (long key in _wanted)
            {
                if (!_live.ContainsKey(key))
                {
                    BuildCell(key);
                }
            }
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

            foreach (Mesh mesh in _meshes.Values)
            {
                if (mesh != null)
                {
                    Destroy(mesh);
                }
            }
        }

        private static Color Tone(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        private static Color C(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
