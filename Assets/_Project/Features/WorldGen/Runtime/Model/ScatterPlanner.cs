using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>Placeholder silhouette of a scatter item; the presentation picks primitives from it.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum ScatterShape
    {
        Tree = 0,
        Rock = 1,
        Crystal = 2,

        /// <summary>Low woody bush (灌木): blocks like a tree trunk, burns like leaves.</summary>
        Shrub = 3,

        /// <summary>Clump of thin canes (箭竹): a highland thicket that blocks.</summary>
        Bamboo = 4,

        /// <summary>Tussock of tall plumed grass (芒草): never blocks (destructibles.md#碰撞: 草不擋).</summary>
        TallGrass = 5,
    }

    /// <summary>Altitude band a scatter kind grows in.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum ScatterHabitat
    {
        Any = 0,

        /// <summary>Below <see cref="ScatterPlanner.HighlandFractionOfRadius"/> · Radius.</summary>
        Lowland = 1,

        /// <summary>At or above <see cref="ScatterPlanner.HighlandFractionOfRadius"/> · Radius.</summary>
        Highland = 2,
    }

    /// <summary>One weighted entry of a planet type's scatter table.</summary>
    /// <remarks>Pre: none. Post: immutable value. Side effects: none.</remarks>
    public readonly struct ScatterKind
    {
        /// <summary>Creates an entry.</summary>
        /// <remarks>
        /// Pre: 0 &lt; minSizeM &lt;= maxSizeM; weight &gt;= 1; substanceId non-empty (an env-materials substance id).
        /// Post: fields echo the arguments. Side effects: none.
        /// </remarks>
        public ScatterKind(ScatterShape shape, float minSizeM, float maxSizeM, int weight, string substanceId)
            : this(shape, minSizeM, maxSizeM, weight, substanceId, ScatterHabitat.Any)
        {
        }

        /// <summary>Creates an entry that only grows in <paramref name="habitat"/>.</summary>
        /// <remarks>
        /// Pre: as the five-argument constructor; habitat defined.
        /// Post: fields echo the arguments. Side effects: none.
        /// </remarks>
        public ScatterKind(ScatterShape shape, float minSizeM, float maxSizeM, int weight, string substanceId,
            ScatterHabitat habitat)
        {
            Contract.Requires(minSizeM > 0f && minSizeM <= maxSizeM, "sizes must satisfy 0 < min <= max");
            Contract.Requires(weight >= 1 && !string.IsNullOrEmpty(substanceId), "weight >= 1 and substance id required");
            Shape = shape;
            MinSizeM = minSizeM;
            MaxSizeM = maxSizeM;
            Weight = weight;
            SubstanceId = substanceId;
            Habitat = habitat;
        }

        /// <summary>Silhouette.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ScatterShape Shape { get; }

        /// <summary>Altitude band the kind grows in.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ScatterHabitat Habitat { get; }

        /// <summary>Smallest size (m).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float MinSizeM { get; }

        /// <summary>Largest size (m).</summary>
        /// <remarks>Pre: none. Post: &gt;= MinSizeM. Side effects: none.</remarks>
        public float MaxSizeM { get; }

        /// <summary>Pick weight.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int Weight { get; }

        /// <summary>Substance the item is made of (destructibles.md: 散佈物件的物質由 biome 散佈表指定).</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string SubstanceId { get; }
    }

    /// <summary>One placed scatter item.</summary>
    /// <remarks>Pre: produced by <see cref="ScatterPlanner"/>. Post: immutable value. Side effects: none.</remarks>
    public readonly struct ScatterItem
    {
        internal ScatterItem(long cellKey, int kindIndex, ScatterShape shape, float3 direction, float altitudeM,
            float sizeM, float yawDeg, string substanceId)
        {
            CellKey = cellKey;
            KindIndex = kindIndex;
            Shape = shape;
            Direction = direction;
            AltitudeM = altitudeM;
            SizeM = sizeM;
            YawDeg = yawDeg;
            SubstanceId = substanceId;
        }

        /// <summary>Cell key; also the item's destructible id within the scatter id space (≤ 1 item per cell).</summary>
        /// <remarks>
        /// Pre: none. Post: 0 ≤ key &lt; <see cref="ScatterPlanner.KeyCount"/>; canopy cells (face, j, i) come first,
        /// understory cells after them. Side effects: none.
        /// </remarks>
        public long CellKey { get; }

        /// <summary>Index into <see cref="ScatterPlanner.Kinds"/>.</summary>
        /// <remarks>Pre: none. Post: valid index. Side effects: none.</remarks>
        public int KindIndex { get; }

        /// <summary>Silhouette.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ScatterShape Shape { get; }

        /// <summary>Ground point (unit direction).</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 Direction { get; }

        /// <summary>Ground altitude under the item (m above Radius).</summary>
        /// <remarks>Pre: none. Post: finite. Side effects: none.</remarks>
        public float AltitudeM { get; }

        /// <summary>Size (m), within the kind's range.</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float SizeM { get; }

        /// <summary>Yaw about the local up (degrees, 0..360).</summary>
        /// <remarks>Pre: none. Post: finite. Side effects: none.</remarks>
        public float YawDeg { get; }

        /// <summary>Substance id.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string SubstanceId { get; }

        /// <summary>Radius (m) that blocks bodies: trunk for trees, rock body, crystal shaft (destructibles.md#碰撞).</summary>
        /// <remarks>Pre: none. Post: &gt; 0; matches the placeholder geometry ScatterField builds. Side effects: none.</remarks>
        public float BlockRadiusM => Shape switch
        {
            ScatterShape.Tree => SizeM * 0.06f,
            ScatterShape.Rock => SizeM * 0.45f,
            ScatterShape.Shrub => SizeM * 0.3f,
            ScatterShape.Bamboo => SizeM * 0.18f,
            ScatterShape.TallGrass => SizeM * 0.15f,
            _ => SizeM * 0.2f,
        };

        /// <summary>Whether the intact item stops bodies (destructibles.md#碰撞: 草不擋; trees, shrubs, rocks do).</summary>
        /// <remarks>Pre: none. Post: false only for <see cref="ScatterShape.TallGrass"/>. Side effects: none.</remarks>
        public bool Blocks => Shape != ScatterShape.TallGrass;

        /// <summary>Volume in litres (dm³) of the placeholder geometry: HP and drop chances scale with it.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int VolumeL
        {
            get
            {
                double s = SizeM;
                double m3 = Shape switch
                {
                    // Trunk cylinder r 0.06 s, h 0.5 s; crown ellipsoid semi-axes 0.3, 0.275, 0.3 s.
                    ScatterShape.Tree => Math.PI * 0.0036 * 0.5 * s * s * s + 4.0 / 3.0 * Math.PI * 0.3 * 0.275 * 0.3 * s * s * s,
                    // Ellipsoid semi-axes 0.5, 0.325, 0.425 s.
                    ScatterShape.Rock => 4.0 / 3.0 * Math.PI * 0.5 * 0.325 * 0.425 * s * s * s,
                    // Foliage ellipsoid semi-axes 0.5, 0.4, 0.5 s (size = width).
                    ScatterShape.Shrub => 4.0 / 3.0 * Math.PI * 0.5 * 0.4 * 0.5 * s * s * s,
                    // Cane clump: cylinder r 0.2 s, h 0.9 s (size = height).
                    ScatterShape.Bamboo => Math.PI * 0.04 * 0.9 * s * s * s,
                    // Tussock: cone r 0.3 s, h s (size = height).
                    ScatterShape.TallGrass => Math.PI * 0.09 / 3.0 * s * s * s,
                    _ => 0.28 * 0.28 * s * s * s,
                };
                return Math.Max(1, (int)Math.Round(m3 * 1000.0));
            }
        }
    }

    /// <summary>
    /// Rule-layer scatter (P3-09, P3-12): which cell holds which tree, rock, crystal, shrub, cane clump or grass tussock,
    /// so creatures can be blocked by them and moves can hit them without any GameObject. ScatterField only draws what this returns.
    /// REQ: planet-world.md (7. 散佈); destructibles.md#實作分層 (散佈物件的位置必須由規則層產生).
    /// </summary>
    /// <remarks>
    /// Two independent layers share one key space. Canopy: each cube-face cell (≈ 10 m) holds at most one tree, rock or
    /// crystal. Understory: each finer cell (≈ 6 m) holds at most one shrub, cane clump or grass tussock, chosen by the
    /// altitude band it stands in. Every cell draws from its own Pcg32 stream, so an item is the same whenever it is
    /// asked for, in any order. Items avoid sea, roads/buildings (isClear) and steep slopes; an understory item also
    /// keeps off the canopy item next to it.
    /// Pre: single-threaded. Post: items depend only on (seed, planet type, planet, ground/sea/clear functions).
    /// Side effects: caches items per cell.
    /// </remarks>
    public sealed class ScatterPlanner
    {
        /// <summary>Nominal cell width at a face centre (m).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float CellM = 10f;

        /// <summary>Bumped whenever the same inputs would give a different item set (testing.md: snapshots follow it).</summary>
        /// <remarks>Pre: none. Post: constant; 2 = understory layer added, canopy items bit-identical to 1. Side effects: none.</remarks>
        public const int GeneratorVersion = 2;

        /// <summary>Nominal understory cell width at a face centre (m).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float UnderstoryCellM = 6f;

        /// <summary>Ground at or above this fraction of the radius is highland (R = 640 m → 7.7 m).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float HighlandFractionOfRadius = 0.012f;

        private const float DefaultPlaceChance = 0.55f;
        private const float MaxSlope = 0.6f; // rise/run over the 3 m probe, ~31°
        private const float ProbeM = 1.5f;
        private const ulong Salt = 0x5CA77E5ul;
        private const ulong UnderstorySalt = 0xF10A5EEDul;
        // Gap between an understory item's body and a canopy trunk or rock, so nothing grows out of a boulder.
        private const float CanopyGapM = 0.4f;
        private const string DefaultType = "verdant_planet";

        // Substance per biome (destructibles.md#物質): trees are wood; rocks stone; crystals crystal, ice on the ice planet.
        private static readonly Dictionary<string, ScatterKind[]> Sets = new Dictionary<string, ScatterKind[]>
        {
            ["verdant_planet"] = new[]
            {
                new ScatterKind(ScatterShape.Tree, 3.5f, 7f, 6, "wood"),
                new ScatterKind(ScatterShape.Rock, 0.8f, 2.2f, 3, "stone"),
            },
            ["lava_planet"] = new[]
            {
                new ScatterKind(ScatterShape.Rock, 1f, 3f, 6, "stone"),
                new ScatterKind(ScatterShape.Crystal, 1.5f, 4f, 3, "crystal"),
            },
            ["ice_crystal_planet"] = new[]
            {
                new ScatterKind(ScatterShape.Crystal, 2f, 6f, 6, "ice"),
                new ScatterKind(ScatterShape.Rock, 0.8f, 2.4f, 3, "stone"),
            },
            ["storm_planet"] = new[]
            {
                new ScatterKind(ScatterShape.Tree, 3f, 5.5f, 3, "wood"),
                new ScatterKind(ScatterShape.Rock, 1f, 2.6f, 5, "stone"),
            },
            ["mineral_planet"] = new[]
            {
                new ScatterKind(ScatterShape.Crystal, 1.5f, 5f, 4, "crystal"),
                new ScatterKind(ScatterShape.Rock, 1f, 3f, 5, "stone"),
            },
            ["twilight_planet"] = new[]
            {
                new ScatterKind(ScatterShape.Tree, 3.5f, 7f, 4, "wood"),
                new ScatterKind(ScatterShape.Crystal, 1.5f, 4f, 3, "crystal"),
            },
        };

        private readonly struct Understory
        {
            public readonly float PlaceChance;
            public readonly ScatterKind[] Kinds;

            public Understory(float placeChance, params ScatterKind[] kinds)
            {
                PlaceChance = placeChance;
                Kinds = kinds;
            }
        }

        // REQ: planet-world.md (7. 散佈 — 植被下層). Shrubs and grass are `plant`, canes are `wood` (destructibles.md#物質).
        // Every table keeps at least one ScatterHabitat.Any kind, so every altitude has something to pick.
        // Cane clumps (箭竹) are highland only; tall plumed grass (芒草) is a pioneer of open and volcanic ground.
        private static readonly Dictionary<string, Understory> UnderSets = new Dictionary<string, Understory>
        {
            ["verdant_planet"] = new Understory(0.5f,
                new ScatterKind(ScatterShape.Shrub, 0.8f, 1.8f, 5, "plant"),
                new ScatterKind(ScatterShape.TallGrass, 1.2f, 2.4f, 4, "plant", ScatterHabitat.Lowland),
                new ScatterKind(ScatterShape.Bamboo, 1.5f, 3f, 7, "wood", ScatterHabitat.Highland)),
            ["lava_planet"] = new Understory(0.2f,
                new ScatterKind(ScatterShape.TallGrass, 1f, 2f, 5, "plant"),
                new ScatterKind(ScatterShape.Shrub, 0.6f, 1.2f, 2, "plant", ScatterHabitat.Lowland)),
            ["ice_crystal_planet"] = new Understory(0.15f,
                new ScatterKind(ScatterShape.Shrub, 0.5f, 1f, 3, "plant")),
            ["storm_planet"] = new Understory(0.45f,
                new ScatterKind(ScatterShape.TallGrass, 1.2f, 2.2f, 6, "plant"),
                new ScatterKind(ScatterShape.Shrub, 0.7f, 1.4f, 3, "plant", ScatterHabitat.Lowland),
                new ScatterKind(ScatterShape.Bamboo, 1.2f, 2.4f, 5, "wood", ScatterHabitat.Highland)),
            ["mineral_planet"] = new Understory(0.2f,
                new ScatterKind(ScatterShape.Shrub, 0.6f, 1.3f, 3, "plant"),
                new ScatterKind(ScatterShape.TallGrass, 1f, 1.8f, 2, "plant", ScatterHabitat.Lowland)),
            ["twilight_planet"] = new Understory(0.4f,
                new ScatterKind(ScatterShape.Shrub, 0.8f, 1.6f, 4, "plant"),
                new ScatterKind(ScatterShape.TallGrass, 1.2f, 2.2f, 3, "plant", ScatterHabitat.Lowland),
                new ScatterKind(ScatterShape.Bamboo, 1.5f, 2.8f, 4, "wood", ScatterHabitat.Highland)),
        };

        private readonly PlanetConfig _config;
        private readonly ulong _seed;
        private readonly ScatterKind[] _kinds;
        private readonly int _totalWeight;
        private readonly Func<float3, float> _groundAltitude;
        private readonly Func<float3, bool> _isSea;
        private readonly Func<float3, bool> _isClear;
        private readonly float _placeChance;
        private readonly Dictionary<long, ScatterItem> _items = new Dictionary<long, ScatterItem>();
        private readonly HashSet<long> _empty = new HashSet<long>();
        private readonly HashSet<long> _queryCells = new HashSet<long>();
        // Separate from _queryCells: an understory cell is evaluated while Query is still walking that set.
        private readonly HashSet<long> _probeCells = new HashSet<long>();
        private readonly int _canopyKinds;
        private readonly float _understoryChance;
        private readonly float _maxCanopyBlockRadiusM;
        private readonly long _canopyKeys;

        /// <summary>Creates the planner with the default obstacle density.</summary>
        /// <remarks>
        /// Pre: config valid; groundAltitude non-null; isSea and isClear may be null (no sea mask / nothing to avoid);
        ///      all three are deterministic. planetTypeId may be null or unknown (falls back to verdant_planet).
        /// Post: <see cref="Kinds"/> is the planet type's table; no cell evaluated yet. Side effects: none.
        /// </remarks>
        public ScatterPlanner(in PlanetConfig config, ulong seed, string planetTypeId, Func<float3, float> groundAltitude,
            Func<float3, bool> isSea, Func<float3, bool> isClear)
            : this(config, seed, planetTypeId, groundAltitude, isSea, isClear, DefaultPlaceChance)
        {
        }

        /// <summary>Creates the planner with an explicit landform obstacle density <paramref name="placeChance"/>.</summary>
        /// <remarks>
        /// Pre: config valid; groundAltitude non-null; placeChance finite in [0, 1]; isSea and isClear may be null.
        /// Post: <see cref="Kinds"/> is the planet type's table; no cell evaluated yet. Side effects: none.
        /// </remarks>
        public ScatterPlanner(in PlanetConfig config, ulong seed, string planetTypeId, Func<float3, float> groundAltitude,
            Func<float3, bool> isSea, Func<float3, bool> isClear, float placeChance)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(groundAltitude != null, "groundAltitude is required");
            Contract.Requires(math.isfinite(placeChance) && placeChance >= 0f && placeChance <= 1f, "placeChance must be in [0, 1]");
            _config = config;
            _seed = seed;
            ScatterKind[] canopy = KindsFor(planetTypeId);
            Understory under = planetTypeId != null && UnderSets.TryGetValue(planetTypeId, out Understory u)
                ? u
                : UnderSets[DefaultType];
            _kinds = new ScatterKind[canopy.Length + under.Kinds.Length];
            canopy.CopyTo(_kinds, 0);
            under.Kinds.CopyTo(_kinds, canopy.Length);
            _canopyKinds = canopy.Length;
            _understoryChance = under.PlaceChance;
            foreach (ScatterKind kind in canopy)
            {
                _totalWeight += kind.Weight;
                _maxCanopyBlockRadiusM = math.max(_maxCanopyBlockRadiusM,
                    new ScatterItem(0, 0, kind.Shape, default, 0f, kind.MaxSizeM, 0f, kind.SubstanceId).BlockRadiusM);
            }

            // One face spans a quarter of a great circle (π/2 · R); cells are CellM wide at the face centre.
            CellsPerFace = math.max(1, (int)math.ceil(config.Radius * math.PI * 0.5f / CellM));
            UnderstoryCellsPerFace = math.max(1, (int)math.ceil(config.Radius * math.PI * 0.5f / UnderstoryCellM));
            _canopyKeys = 6L * CellsPerFace * CellsPerFace;
            _groundAltitude = groundAltitude;
            _isSea = isSea;
            _isClear = isClear;
            _placeChance = placeChance;
        }

        /// <summary>Canopy scatter table of a planet type (the default table for null or unknown ids).</summary>
        /// <remarks>Pre: none. Post: non-empty; the same array instance for the same id. Side effects: none.</remarks>
        public static ScatterKind[] KindsFor(string planetTypeId) =>
            planetTypeId != null && Sets.TryGetValue(planetTypeId, out ScatterKind[] k) ? k : Sets[DefaultType];

        /// <summary>Understory scatter table of a planet type (the default table for null or unknown ids).</summary>
        /// <remarks>
        /// Pre: none. Post: non-empty; at least one kind has <see cref="ScatterHabitat.Any"/>; the same array instance
        /// for the same id. Side effects: none.
        /// </remarks>
        public static ScatterKind[] UnderstoryKindsFor(string planetTypeId) =>
            planetTypeId != null && UnderSets.TryGetValue(planetTypeId, out Understory u) ? u.Kinds : UnderSets[DefaultType].Kinds;

        /// <summary>The kinds this planner picks from: the canopy table, then the understory table.</summary>
        /// <remarks>Pre: none. Post: non-empty; the first <see cref="CanopyKindCount"/> are canopy kinds. Side effects: none.</remarks>
        public IReadOnlyList<ScatterKind> Kinds => _kinds;

        /// <summary>Number of canopy kinds at the front of <see cref="Kinds"/>.</summary>
        /// <remarks>Pre: none. Post: 1 ≤ value &lt; Kinds.Count. Side effects: none.</remarks>
        public int CanopyKindCount => _canopyKinds;

        /// <summary>Canopy cells along one face edge.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int CellsPerFace { get; }

        /// <summary>Understory cells along one face edge.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int UnderstoryCellsPerFace { get; }

        /// <summary>Size of the key space: canopy keys are [0, 6·CellsPerFace²), understory keys follow.</summary>
        /// <remarks>Pre: none. Post: 6·CellsPerFace² + 6·UnderstoryCellsPerFace². Side effects: none.</remarks>
        public long KeyCount => _canopyKeys + 6L * UnderstoryCellsPerFace * UnderstoryCellsPerFace;

        /// <summary>Whether <paramref name="key"/> names an understory cell.</summary>
        /// <remarks>Pre: none. Post: true iff 6·CellsPerFace² ≤ key &lt; KeyCount. Side effects: none.</remarks>
        public bool IsUnderstoryKey(long key) => key >= _canopyKeys && key < KeyCount;

        /// <summary>Largest <see cref="ScatterItem.BlockRadiusM"/> any kind can produce (m).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float MaxBlockRadiusM
        {
            get
            {
                float r = 0f;
                foreach (ScatterKind k in _kinds)
                {
                    r = math.max(r, new ScatterItem(0, 0, k.Shape, default, 0f, k.MaxSizeM, 0f, k.SubstanceId).BlockRadiusM);
                }

                return r;
            }
        }

        /// <summary>The item in cell <paramref name="key"/>, if any.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= key &lt; KeyCount. Post: same answer on every call and in any call order.
        /// Side effects: caches the cell (an understory cell also caches the canopy cells around it).
        /// </remarks>
        public bool TryGetItem(long key, out ScatterItem item)
        {
            Contract.Requires(key >= 0 && key < KeyCount, "key out of range");
            if (_items.TryGetValue(key, out item))
            {
                return true;
            }

            if (_empty.Contains(key))
            {
                return false;
            }

            if (key < _canopyKeys ? Evaluate(key, out item) : EvaluateUnderstory(key, out item))
            {
                _items.Add(key, item);
                return true;
            }

            _empty.Add(key);
            return false;
        }

        /// <summary>Collects every cell that may hold ground within <paramref name="radiusM"/> of <paramref name="up"/>.</summary>
        /// <remarks>
        /// Pre: up unit length; radiusM finite and ≥ 0; into non-null.
        /// Post: into is cleared, then holds every canopy and understory cell whose area meets the spherical cap (a
        ///       superset; never misses a cell at a face seam or corner).
        /// Side effects: mutates into.
        /// </remarks>
        public void CollectCells(float3 up, float radiusM, HashSet<long> into)
        {
            Contract.Requires(into != null, "into must be non-null");
            Contract.Requires(math.isfinite(radiusM) && radiusM >= 0f, "radiusM must be finite and >= 0");
            into.Clear();
            CollectLayer(up, radiusM, CellsPerFace, 0L, into);
            CollectLayer(up, radiusM, UnderstoryCellsPerFace, _canopyKeys, into);
        }

        private void CollectLayer(float3 up, float radiusM, int n, long keyBase, HashSet<long> into)
        {
            // Each parameter's angular derivative is at most π/4. A cell's centre is therefore at most
            // R·π/(2n) from any point in it (two half-widths). Padding keeps every intersecting cell.
            float angle = math.min(math.PI, radiusM / _config.Radius + math.PI / (2f * n));
            float chord = 2f * math.sin(angle * 0.5f) + 1e-6f;
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
                            into.Add(keyBase + ((long)f * n + j) * n + i);
                        }
                    }
                }
            }
        }

        /// <summary>Appends items whose ground point lies within <paramref name="radiusM"/> (straight line, on the sphere) of <paramref name="direction"/>.</summary>
        /// <remarks>
        /// Pre: direction finite and non-zero (normalized here); radiusM finite and ≥ 0; into non-null.
        /// Post: appends in ascending cell-key order (stable across peers). Side effects: caches cells; appends to into.
        /// </remarks>
        public void Query(float3 direction, float radiusM, List<ScatterItem> into)
        {
            Contract.Requires(into != null, "into must be non-null");
            Contract.Requires(math.all(math.isfinite(direction)) && math.lengthsq(direction) > 0f,
                "direction must be finite and non-zero");
            float3 up = math.normalize(direction);
            CollectCells(up, radiusM, _queryCells);
            int first = into.Count;
            foreach (long key in _queryCells)
            {
                if (TryGetItem(key, out ScatterItem item) &&
                    math.distance(up, item.Direction) * _config.Radius <= radiusM)
                {
                    into.Add(item);
                }
            }

            // HashSet order is an implementation detail; sort so hit and event order never depends on it.
            into.Sort(first, into.Count - first, KeyOrder.Instance);
        }

        private sealed class KeyOrder : IComparer<ScatterItem>
        {
            public static readonly KeyOrder Instance = new KeyOrder();

            public int Compare(ScatterItem a, ScatterItem b) => a.CellKey.CompareTo(b.CellKey);
        }

        private bool Evaluate(long key, out ScatterItem item)
        {
            item = default;
            int n = CellsPerFace;
            var face = (CubeFace)(int)(key / ((long)n * n));
            int rem = (int)(key % ((long)n * n));
            int j = rem / n;
            int i = rem % n;
            // A per-cell stream: the same cell regenerates identically after being streamed out and back in.
            var rng = new Pcg32(_seed ^ Salt, (ulong)key);
            if (rng.NextFloat() > _placeChance)
            {
                return false;
            }

            float u = math.clamp(-1f + 2f * (i + rng.NextFloat()) / n, -1f, 1f);
            float v = math.clamp(-1f + 2f * (j + rng.NextFloat()) / n, -1f, 1f);
            int kindIndex = Pick(ref rng);
            ScatterKind kind = _kinds[kindIndex];
            float size = math.lerp(kind.MinSizeM, kind.MaxSizeM, rng.NextFloat());
            float yaw = rng.NextFloat() * 360f;
            float3 dir = CubeSphere.FaceToDirection(face, u, v);
            if (_isSea != null && _isSea(dir))
            {
                return false;
            }

            // REQ: planet-world.md#道路與建築 (P3-11) — after every draw, so a road only removes items, never moves others.
            if (_isClear != null && !_isClear(dir))
            {
                return false;
            }

            float alt = _groundAltitude(dir);
            PlanetFrame frame = PlanetFrame.FromUp(dir);
            if (SlopeAt(dir, frame, alt) > MaxSlope)
            {
                return false;
            }

            item = new ScatterItem(key, kindIndex, kind.Shape, dir, alt, size, yaw, kind.SubstanceId);
            return true;
        }

        private bool EvaluateUnderstory(long key, out ScatterItem item)
        {
            item = default;
            long local = key - _canopyKeys;
            int n = UnderstoryCellsPerFace;
            var face = (CubeFace)(int)(local / ((long)n * n));
            int rem = (int)(local % ((long)n * n));
            int j = rem / n;
            int i = rem % n;
            var rng = new Pcg32(_seed ^ UnderstorySalt, (ulong)local);
            if (rng.NextFloat() > _understoryChance)
            {
                return false;
            }

            float u = math.clamp(-1f + 2f * (i + rng.NextFloat()) / n, -1f, 1f);
            float v = math.clamp(-1f + 2f * (j + rng.NextFloat()) / n, -1f, 1f);
            uint roll = rng.NextUInt();
            float sizeT = rng.NextFloat();
            float yaw = rng.NextFloat() * 360f;
            float3 dir = CubeSphere.FaceToDirection(face, u, v);
            if ((_isSea != null && _isSea(dir)) || (_isClear != null && !_isClear(dir)))
            {
                return false;
            }

            float alt = _groundAltitude(dir);
            if (SlopeAt(dir, PlanetFrame.FromUp(dir), alt) > MaxSlope)
            {
                return false;
            }

            // Resolved after every draw: the habitat reads the altitude, and the stream must not depend on it.
            int kindIndex = PickUnderstory(roll, alt >= HighlandFractionOfRadius * _config.Radius);
            ScatterKind kind = _kinds[kindIndex];
            item = new ScatterItem(key, kindIndex, kind.Shape, dir, alt, math.lerp(kind.MinSizeM, kind.MaxSizeM, sizeT),
                yaw, kind.SubstanceId);
            return !TouchesCanopy(item);
        }

        private int PickUnderstory(uint roll, bool highland)
        {
            ScatterHabitat excluded = highland ? ScatterHabitat.Lowland : ScatterHabitat.Highland;
            int total = 0;
            for (int k = _canopyKinds; k < _kinds.Length; k++)
            {
                total += _kinds[k].Habitat == excluded ? 0 : _kinds[k].Weight;
            }

            int pick = (int)(roll % (uint)total);
            for (int k = _canopyKinds; k < _kinds.Length; k++)
            {
                if (_kinds[k].Habitat == excluded)
                {
                    continue;
                }

                pick -= _kinds[k].Weight;
                if (pick < 0)
                {
                    return k;
                }
            }

            return _canopyKinds;
        }

        // Canopy cells never read the understory, so this cannot recurse and the answer is order-independent.
        private bool TouchesCanopy(in ScatterItem under)
        {
            float reach = under.BlockRadiusM + CanopyGapM;
            _probeCells.Clear();
            CollectLayer(under.Direction, reach + _maxCanopyBlockRadiusM, CellsPerFace, 0L, _probeCells);
            foreach (long key in _probeCells)
            {
                if (TryGetItem(key, out ScatterItem canopy) &&
                    math.distance(under.Direction, canopy.Direction) * _config.Radius <= reach + canopy.BlockRadiusM)
                {
                    return true;
                }
            }

            return false;
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

        private int Pick(ref Pcg32 rng)
        {
            int roll = (int)rng.NextUInt((uint)_totalWeight);
            for (int k = 0; k < _canopyKinds; k++)
            {
                roll -= _kinds[k].Weight;
                if (roll < 0)
                {
                    return k;
                }
            }

            return _canopyKinds - 1;
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
    }
}
