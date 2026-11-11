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
    /// D0 scatter (P3-09): trees, rocks and crystals placed around the player on a fixed cube-face cell grid.
    /// REQ: planet-world.md (7. 散佈), CLAUDE.md (generation must be reproducible: no UnityEngine.Random).
    /// </summary>
    /// <remarks>
    /// Pre: Configure once. Post: the set of live objects depends only on (seed, planet type, player position), so a
    /// cell always yields the same item whenever it streams in; items never float (altitude comes from the same
    /// ground function the terrain mesh uses) and never stand in water or on steep slopes.
    /// Why streaming and not baking the whole planet: R=640 m is ~5 Mm² of surface; only the horizon (~47 m) is visible.
    /// Side effects: creates/destroys primitive GameObjects under this component; owns its materials.
    /// </remarks>
    public sealed class ScatterField : MonoBehaviour
    {
        private enum Shape { Tree, Rock, Crystal }

        private readonly struct Kind
        {
            public readonly Shape Shape;
            public readonly Color Color;
            public readonly Color Accent;
            public readonly float MinSize;
            public readonly float MaxSize;
            public readonly int Weight;

            public Kind(Shape shape, Color color, Color accent, float minSize, float maxSize, int weight)
            {
                Shape = shape;
                Color = color;
                Accent = accent;
                MinSize = minSize;
                MaxSize = maxSize;
                Weight = weight;
            }
        }

        private const float CellM = 10f;
        private const float RadiusM = 90f;
        private const float PlaceChance = 0.55f;
        private const float MaxSlope = 0.6f; // rise/run over the 3 m probe, ~31°
        private const float ProbeM = 1.5f;
        private const float RefreshMoveM = 6f;
        private const float RefreshSeconds = 0.5f;
        private const ulong Salt = 0x5CA77E5ul;

        private static readonly Dictionary<string, Kind[]> Sets = new Dictionary<string, Kind[]>
        {
            ["verdant_planet"] = new[]
            {
                new Kind(Shape.Tree, C(0x3F7F36), C(0x6B4A2B), 3.5f, 7f, 6),
                new Kind(Shape.Rock, C(0x6E6A62), C(0x6E6A62), 0.8f, 2.2f, 3),
            },
            ["lava_planet"] = new[]
            {
                new Kind(Shape.Rock, C(0x2E2422), C(0x2E2422), 1f, 3f, 6),
                new Kind(Shape.Crystal, C(0xFF8A1E), C(0xFF8A1E), 1.5f, 4f, 3),
            },
            ["ice_crystal_planet"] = new[]
            {
                new Kind(Shape.Crystal, C(0x9FD8F0), C(0x9FD8F0), 2f, 6f, 6),
                new Kind(Shape.Rock, C(0xDCEBF2), C(0xDCEBF2), 0.8f, 2.4f, 3),
            },
            ["storm_planet"] = new[]
            {
                new Kind(Shape.Tree, C(0x5E7050), C(0x4A4038), 3f, 5.5f, 3),
                new Kind(Shape.Rock, C(0x5C5A60), C(0x5C5A60), 1f, 2.6f, 5),
            },
            ["mineral_planet"] = new[]
            {
                new Kind(Shape.Crystal, C(0xE0B060), C(0xE0B060), 1.5f, 5f, 4),
                new Kind(Shape.Rock, C(0x8C4E36), C(0x8C4E36), 1f, 3f, 5),
            },
            ["twilight_planet"] = new[]
            {
                new Kind(Shape.Tree, C(0x8A6AC8), C(0x3E3050), 3.5f, 7f, 4),
                new Kind(Shape.Crystal, C(0xE070B0), C(0xE070B0), 1.5f, 4f, 3),
            },
        };

        private PlanetConfig _config;
        private ulong _seed;
        private Kind[] _kinds;
        private int _totalWeight;
        private int _cellsPerFace;
        private Func<float3, float> _groundAltitude;
        private Func<float3, bool> _isSea;
        private Func<float3, bool> _isClear;
        private Func<float3> _focus;
        private Shader _shader;
        private readonly Dictionary<long, List<GameObject>> _live = new Dictionary<long, List<GameObject>>();
        private readonly HashSet<long> _wanted = new HashSet<long>();
        private readonly List<long> _stale = new List<long>();
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
            _config = config;
            _seed = seed;
            _kinds = planetTypeId != null && Sets.TryGetValue(planetTypeId, out Kind[] k) ? k : Sets["verdant_planet"];
            _totalWeight = 0;
            foreach (Kind kind in _kinds)
            {
                _totalWeight += kind.Weight;
            }

            // One face spans a quarter of a great circle (π/2 · R); cells are CellM wide at the face centre.
            _cellsPerFace = math.max(1, (int)math.ceil(config.Radius * math.PI * 0.5f / CellM));
            _groundAltitude = groundAltitude;
            _isSea = isSea;
            _isClear = isClear;
            _focus = focus;
            _shader = shader;
            _configured = true;
            _lastFocus = new float3(float.NaN);
            _nextRefresh = 0f;
        }

        /// <summary>Spawns the items of one cell, or none.</summary>
        /// <remarks>
        /// Pre: Configure called; key encodes (face, j, i). Post: ≤ 1 item rooted at the ground of that cell.
        /// Why a per-cell RNG stream: the same cell regenerates identically after being streamed out and back in.
        /// Side effects: creates GameObjects.
        /// </remarks>
        private void BuildCell(long key)
        {
            var objects = new List<GameObject>();
            _live[key] = objects;
            int n = _cellsPerFace;
            var face = (CubeFace)(int)(key / ((long)n * n));
            int rem = (int)(key % ((long)n * n));
            int j = rem / n;
            int i = rem % n;
            var rng = new Pcg32(_seed ^ Salt, (ulong)key);
            if (rng.NextFloat() > PlaceChance)
            {
                return;
            }

            float u = math.clamp(-1f + 2f * (i + rng.NextFloat()) / n, -1f, 1f);
            float v = math.clamp(-1f + 2f * (j + rng.NextFloat()) / n, -1f, 1f);
            Kind kind = Pick(ref rng);
            float size = math.lerp(kind.MinSize, kind.MaxSize, rng.NextFloat());
            float yaw = rng.NextFloat() * 360f;
            float3 dir = CubeSphere.FaceToDirection(face, u, v);
            if (_isSea != null && _isSea(dir))
            {
                return;
            }

            // REQ: planet-world.md#道路與建築 (P3-11) — after every draw, so a road only removes items, never moves others.
            if (_isClear != null && !_isClear(dir))
            {
                return;
            }

            float alt = _groundAltitude(dir);
            PlanetFrame frame = PlanetFrame.FromUp(dir);
            if (SlopeAt(dir, frame, alt) > MaxSlope)
            {
                return;
            }

            float3 world = _config.ToWorld(dir, alt - 0.1f); // sink slightly so slopes never expose the base
            var root = new GameObject("Scatter_" + kind.Shape);
            root.transform.SetParent(transform, false);
            root.transform.position = new Vector3(world.x, world.y, world.z);
            // Local Y is already the planet's up here (LookRotation above), so Vector3.up is only the local yaw axis.
            root.transform.rotation = Quaternion.LookRotation((Vector3)frame.North, (Vector3)frame.Up) *
                                      Quaternion.AngleAxis(yaw, Vector3.up);
            Build(root.transform, kind, size);
            objects.Add(root);
        }

        // Rise over run across a ±ProbeM cross; the north/east offsets are tangent so no world axis is assumed "up".
        private float SlopeAt(float3 dir, PlanetFrame frame, float centerAlt)
        {
            float3 pos = _config.ToWorld(dir, centerAlt);
            float worst = 0f;
            for (int k = 0; k < 4; k++)
            {
                float3 offset = k switch { 0 => frame.East, 1 => -frame.East, 2 => frame.North, _ => -frame.North };
                float3 d = _config.ToDirection(pos + offset * ProbeM);
                worst = math.max(worst, math.abs(_groundAltitude(d) - centerAlt) / ProbeM);
            }

            return worst;
        }

        private Kind Pick(ref Pcg32 rng)
        {
            int roll = (int)rng.NextUInt((uint)_totalWeight);
            foreach (Kind k in _kinds)
            {
                roll -= k.Weight;
                if (roll < 0)
                {
                    return k;
                }
            }

            return _kinds[_kinds.Length - 1];
        }

        // Placeholder geometry (art-asset-pipeline.md#佔位模型): primitives with toon materials, replaceable later.
        private void Build(Transform parent, Kind kind, float size)
        {
            switch (kind.Shape)
            {
                case Shape.Tree:
                    Part(parent, PrimitiveType.Cylinder, kind.Accent, new Vector3(size * 0.12f, size * 0.25f, size * 0.12f),
                        new Vector3(0f, size * 0.25f, 0f));
                    Part(parent, PrimitiveType.Sphere, kind.Color, new Vector3(size * 0.6f, size * 0.55f, size * 0.6f),
                        new Vector3(0f, size * 0.7f, 0f));
                    break;
                case Shape.Rock:
                    Part(parent, PrimitiveType.Sphere, kind.Color, new Vector3(size, size * 0.65f, size * 0.85f),
                        new Vector3(0f, size * 0.25f, 0f));
                    break;
                default:
                    Part(parent, PrimitiveType.Cube, kind.Color, new Vector3(size * 0.28f, size, size * 0.28f),
                        new Vector3(0f, size * 0.5f, 0f), Quaternion.Euler(0f, 45f, 8f));
                    break;
            }
        }

        private void Part(Transform parent, PrimitiveType type, Color color, Vector3 scale, Vector3 localPos,
            Quaternion? rotation = null)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>()); // decorative: must not block movement or combat raycasts
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
                m = PlanetLook.CreatureMaterial(_shader, color, 0.03f);
                _materials[color] = m;
            }

            return m;
        }

        private void CollectCells(float3 up)
        {
            int n = _cellsPerFace;
            // Each parameter's angular derivative is at most π/4. A cell's centre is therefore at most
            // R·π/(2n) from any point in it (two half-widths). Padding keeps every intersecting cell,
            // including cells found by the former tangent-disc sampler; per-cell RNG keys stay unchanged.
            float angle = math.min(math.PI, RadiusM / _config.Radius + math.PI / (2f * n));
            float chord = 2f * math.sin(angle * 0.5f) + 1e-6f;
            _wanted.Clear();
            for (int f = 0; f < CubeSphere.FaceCount; f++)
            {
                var face = (CubeFace)f;
                float normal = math.dot(up, (float3)CubeSphere.Normal(face));
                // Every direction owned by this face has normal component ≥ 1/√3. Intersect its
                // component interval with the cap's chord ball before dividing to bound tan(u·π/4).
                float lo = math.max(math.rsqrt(3f), normal - chord);
                float hi = math.min(1f, normal + chord);
                if (lo > hi)
                {
                    continue;
                }

                CellRange(math.dot(up, (float3)CubeSphere.AxisU(face)), chord, lo, hi, n, out int iMin, out int iMax);
                CellRange(math.dot(up, (float3)CubeSphere.AxisV(face)), chord, lo, hi, n, out int jMin, out int jMax);
                for (int j = jMin; j <= jMax; j++)
                {
                    for (int i = iMin; i <= iMax; i++)
                    {
                        float3 centre = CubeSphere.FaceToDirection(face, -1f + 2f * (i + 0.5f) / n,
                            -1f + 2f * (j + 0.5f) / n);
                        if (math.distancesq(up, centre) <= chord * chord)
                        {
                            _wanted.Add(((long)f * n + j) * n + i);
                        }
                    }
                }
            }
        }

        private static void CellRange(float component, float chord, float lo, float hi, int n,
            out int first, out int last)
        {
            float a = component - chord;
            float b = component + chord;
            float min = CubeSphere.Unwarp(a / (a < 0f ? lo : hi));
            float max = CubeSphere.Unwarp(b / (b > 0f ? lo : hi));
            // An extra index absorbs inverse-projection rounding at exact cell boundaries.
            first = math.clamp((int)math.floor((min + 1f) * 0.5f * n) - 1, 0, n - 1);
            last = math.clamp((int)math.floor((max + 1f) * 0.5f * n) + 1, 0, n - 1);
        }

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

        private static Color C(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
