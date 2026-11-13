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
    /// D0 scatter (P3-09): draws the trees, rocks and crystals <see cref="ScatterPlanner"/> places around the player.
    /// REQ: planet-world.md (7. 散佈); destructibles.md (placement lives in the rule layer, items block and can break).
    /// </summary>
    /// <remarks>
    /// Pre: Configure once. Post: the set of live objects depends only on (seed, planet type, player position), so a
    /// cell always yields the same item whenever it streams in; items never float (altitude comes from the same
    /// ground function the terrain mesh uses) and never stand in water or on steep slopes.
    /// Why streaming and not baking the whole planet: R=640 m is ~5 Mm² of surface; only the horizon (~47 m) is visible.
    /// The trunk, rock body or crystal shaft keeps its primitive collider (destructibles.md#碰撞: trees and rocks block the
    /// character); crowns have none. Destroyed items (isDestroyed) are never drawn.
    /// Side effects: creates/destroys primitive GameObjects under this component; owns its materials.
    /// </remarks>
    public sealed class ScatterField : MonoBehaviour
    {
        private readonly struct Palette
        {
            public readonly Color Color;
            public readonly Color Accent;

            public Palette(Color color, Color accent)
            {
                Color = color;
                Accent = accent;
            }
        }

        private const float RadiusM = 90f;
        private const float RefreshMoveM = 6f;
        private const float RefreshSeconds = 0.5f;

        // Same order as ScatterPlanner.KindsFor(type): colour is presentation, the rest is rule layer.
        private static readonly Dictionary<string, Palette[]> Palettes = new Dictionary<string, Palette[]>
        {
            ["verdant_planet"] = new[] { new Palette(C(0x3F7F36), C(0x6B4A2B)), new Palette(C(0x6E6A62), C(0x6E6A62)) },
            ["lava_planet"] = new[] { new Palette(C(0x2E2422), C(0x2E2422)), new Palette(C(0xFF8A1E), C(0xFF8A1E)) },
            ["ice_crystal_planet"] = new[] { new Palette(C(0x9FD8F0), C(0x9FD8F0)), new Palette(C(0xDCEBF2), C(0xDCEBF2)) },
            ["storm_planet"] = new[] { new Palette(C(0x5E7050), C(0x4A4038)), new Palette(C(0x5C5A60), C(0x5C5A60)) },
            ["mineral_planet"] = new[] { new Palette(C(0xE0B060), C(0xE0B060)), new Palette(C(0x8C4E36), C(0x8C4E36)) },
            ["twilight_planet"] = new[] { new Palette(C(0x8A6AC8), C(0x3E3050)), new Palette(C(0xE070B0), C(0xE070B0)) },
        };

        private PlanetConfig _config;
        private ScatterPlanner _planner;
        private Palette[] _palette;
        private int _cellsPerFace;
        private Func<long, bool> _isDestroyed;
        private Func<float3> _focus;
        private Shader _shader;
        private readonly Dictionary<long, List<GameObject>> _live = new Dictionary<long, List<GameObject>>();
        private readonly HashSet<long> _wanted = new HashSet<long>();
        private readonly List<long> _stale = new List<long>();
        // Items handed to ObjectPhysics (woken rigidbodies): no longer streamed, never rebuilt at their home spot.
        private readonly HashSet<long> _detached = new HashSet<long>();
        private readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();
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
        ///      isClear may be null (no roads or buildings to avoid) and must be deterministic.
        /// Post: Update streams cells in and out; no item stands where isClear is false. Side effects: none until Update.
        /// </remarks>
        public void Configure(in PlanetConfig config, ulong seed, string planetTypeId, Func<float3, float> groundAltitude,
            Func<float3, bool> isSea, Func<float3> focus, Shader shader, Func<float3, bool> isClear = null)
        {
            Contract.Requires(groundAltitude != null && focus != null && shader != null,
                "groundAltitude, focus and shader are required");
            Configure(config, new ScatterPlanner(config, seed, planetTypeId, groundAltitude, isSea, isClear), planetTypeId,
                focus, shader, null);
        }

        /// <summary>Starts drawing <paramref name="planner"/>'s items around <paramref name="focus"/>.</summary>
        /// <remarks>
        /// Pre: config is the planner's planet; planner, focus and shader non-null; planetTypeId is the one the planner was
        ///      built with; isDestroyed may be null (nothing destroyed) and, once true for a key, stays true.
        /// Post: Update streams cells in and out; destroyed items are never drawn. Side effects: none until Update.
        /// </remarks>
        public void Configure(in PlanetConfig config, ScatterPlanner planner, string planetTypeId, Func<float3> focus,
            Shader shader, Func<long, bool> isDestroyed)
        {
            Contract.Requires(planner != null && focus != null && shader != null, "planner, focus and shader are required");
            _config = config;
            _planner = planner;
            _palette = planetTypeId != null && Palettes.TryGetValue(planetTypeId, out Palette[] p) ? p : Palettes["verdant_planet"];
            _cellsPerFace = planner.CellsPerFace;
            _isDestroyed = isDestroyed;
            _focus = focus;
            _shader = shader;
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
            Build(root.transform, item.Shape, _palette[item.KindIndex], item.SizeM);
            objects.Add(root);
        }

        private void Build(Transform parent, ScatterShape shape, Palette kind, float size)
        {
            // The first part of each shape is its body and keeps its collider (ScatterItem.BlockRadiusM matches it).
            switch (shape)
            {
                case ScatterShape.Tree:
                    Part(parent, PrimitiveType.Cylinder, kind.Accent, new Vector3(size * 0.12f, size * 0.25f, size * 0.12f),
                        new Vector3(0f, size * 0.25f, 0f), null, true);
                    // A crown of overlapping lobes in three tones (shaded underside, body, sunlit top) reads as foliage
                    // masses; one sphere reads as a lollipop. Lobes are decorative: only the trunk collides.
                    Color under = Tone(kind.Color, 0.8f);
                    Color top = Color.Lerp(Tone(kind.Color, 1.18f), new Color(0.85f, 0.9f, 0.45f), 0.14f);
                    Part(parent, PrimitiveType.Sphere, kind.Color, new Vector3(size * 0.56f, size * 0.46f, size * 0.56f),
                        new Vector3(0f, size * 0.68f, 0f));
                    Part(parent, PrimitiveType.Sphere, under, new Vector3(size * 0.42f, size * 0.32f, size * 0.42f),
                        new Vector3(size * 0.2f, size * 0.56f, size * 0.08f));
                    Part(parent, PrimitiveType.Sphere, under, new Vector3(size * 0.38f, size * 0.3f, size * 0.4f),
                        new Vector3(-size * 0.19f, size * 0.58f, -size * 0.12f));
                    Part(parent, PrimitiveType.Sphere, kind.Color, new Vector3(size * 0.36f, size * 0.3f, size * 0.36f),
                        new Vector3(-size * 0.06f, size * 0.62f, size * 0.21f));
                    Part(parent, PrimitiveType.Sphere, top, new Vector3(size * 0.36f, size * 0.3f, size * 0.36f),
                        new Vector3(size * 0.05f, size * 0.9f, -size * 0.03f));
                    break;
                case ScatterShape.Rock:
                    Part(parent, PrimitiveType.Sphere, kind.Color, new Vector3(size, size * 0.65f, size * 0.85f),
                        new Vector3(0f, size * 0.25f, 0f), null, true);
                    // A lighter cap and a half-buried shoulder break the egg silhouette (decorative, no collider).
                    Part(parent, PrimitiveType.Sphere, Tone(kind.Color, 1.12f), new Vector3(size * 0.62f, size * 0.42f, size * 0.55f),
                        new Vector3(size * 0.1f, size * 0.46f, -size * 0.05f), Quaternion.Euler(0f, 30f, 14f));
                    Part(parent, PrimitiveType.Sphere, Tone(kind.Color, 0.86f), new Vector3(size * 0.5f, size * 0.36f, size * 0.46f),
                        new Vector3(-size * 0.38f, size * 0.08f, size * 0.22f), Quaternion.Euler(0f, 55f, -10f));
                    break;
                default:
                    Part(parent, PrimitiveType.Cube, kind.Color, new Vector3(size * 0.28f, size, size * 0.28f),
                        new Vector3(0f, size * 0.5f, 0f), Quaternion.Euler(0f, 45f, 8f), true);
                    Part(parent, PrimitiveType.Cube, Tone(kind.Color, 1.15f), new Vector3(size * 0.16f, size * 0.55f, size * 0.16f),
                        new Vector3(size * 0.2f, size * 0.24f, size * 0.06f), Quaternion.Euler(14f, 20f, -22f));
                    Part(parent, PrimitiveType.Cube, Tone(kind.Color, 0.85f), new Vector3(size * 0.13f, size * 0.4f, size * 0.13f),
                        new Vector3(-size * 0.17f, size * 0.17f, -size * 0.1f), Quaternion.Euler(-10f, 60f, 26f));
                    break;
            }
        }

        private void Part(Transform parent, PrimitiveType type, Color color, Vector3 scale, Vector3 localPos,
            Quaternion? rotation = null, bool solid = false)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            if (!solid)
            {
                Destroy(go.GetComponent<Collider>()); // crowns are decorative: the trunk alone blocks
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.transform.localRotation = rotation ?? Quaternion.identity;
            go.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
        }

        private Material MaterialFor(Color color)
        {
            if (!_materials.TryGetValue(color, out Material m))
            {
                // Hairline only: vision.md keeps outlines for characters and creatures; scenery stays soft.
                m = PlanetLook.CreatureMaterial(_shader, color, 0.012f);
                _materials[color] = m;
            }

            return m;
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
        }

        private static Color Tone(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        private static Color C(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
