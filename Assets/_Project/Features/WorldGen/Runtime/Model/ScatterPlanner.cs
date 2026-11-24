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

        /// <summary>Fallen trunk lying on the ground (倒木): blocks, low.</summary>
        Log = 6,

        /// <summary>Columnar succulent with arms (仙人掌).</summary>
        Cactus = 7,

        /// <summary>Giant mushroom: a stalk under a wide cap; only the stalk blocks.</summary>
        Mushroom = 8,

        /// <summary>Tall tapering pillar: stalagmite, stone-forest spire, basalt column or ice pillar.</summary>
        Spire = 9,

        /// <summary>Rock outcrop carrying a mineral: ore, coal, oil shale or a gem geode.</summary>
        OreVein = 10,

        /// <summary>Rosette of fronds (蕨類): never blocks.</summary>
        Fern = 11,

        /// <summary>Stand of thin upright stems (蘆葦): never blocks.</summary>
        Reed = 12,

        /// <summary>Low patch of flowers (花叢): never blocks.</summary>
        Flowers = 13,
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

    /// <summary>One scatter species: its silhouette, size range, substance and altitude band.</summary>
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

        /// <summary>Creates catalog species <paramref name="id"/> (weights live in the landform tables, so Weight is 1).</summary>
        /// <remarks>
        /// Pre: id non-empty; 0 &lt; minSizeM &lt;= maxSizeM; substanceId non-empty; habitat defined.
        /// Post: fields echo the arguments; Weight == 1. Side effects: none.
        /// </remarks>
        public ScatterKind(string id, ScatterShape shape, float minSizeM, float maxSizeM, string substanceId,
            ScatterHabitat habitat = ScatterHabitat.Any)
            : this(shape, minSizeM, maxSizeM, 1, substanceId, habitat)
        {
            Contract.Requires(!string.IsNullOrEmpty(id), "species id required");
            Id = id;
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
            Id = "custom";
        }

        internal ScatterKind WithSubstance(string substanceId) =>
            new ScatterKind(Id, Shape, MinSizeM, MaxSizeM, substanceId, Habitat);

        /// <summary>Species id (ScatterBiomes catalog); "custom" for kinds built without one.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string Id { get; }

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
            float sizeM, float yawDeg, string substanceId, int age = 1000, int health = 1000)
        {
            Age = age;
            Health = health;
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

        /// <summary>Relative age 0 (sapling) – 1000 (oldest): the item's position within its size range.</summary>
        /// <remarks>Pre: none. Post: 0 ≤ Age ≤ 1000. Side effects: none.</remarks>
        public int Age { get; }

        /// <summary>Plant health 0–1000 (planet-world.md#自然生物): 1000 for non-plants and understory, 0 for snags.</summary>
        /// <remarks>Pre: none. Post: 0 ≤ Health ≤ 1000; a living tree's Health &gt; ScatterPlanner.DeadHealth unless it
        /// stands in a plantation. Side effects: none.</remarks>
        public int Health { get; }

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
            ScatterShape.Log => SizeM * 0.3f,
            ScatterShape.Cactus => SizeM * 0.12f,
            ScatterShape.Mushroom => SizeM * 0.08f,
            ScatterShape.Spire => SizeM * 0.2f,
            ScatterShape.OreVein => SizeM * 0.4f,
            ScatterShape.Fern => SizeM * 0.25f,
            ScatterShape.Reed => SizeM * 0.15f,
            ScatterShape.Flowers => SizeM * 0.25f,
            _ => SizeM * 0.2f,
        };

        /// <summary>Whether the intact item stops bodies (destructibles.md#碰撞: 草不擋; trees, shrubs, rocks do).</summary>
        /// <remarks>Pre: none. Post: false only for tall grass, ferns, reeds and flowers. Side effects: none.</remarks>
        public bool Blocks => Shape != ScatterShape.TallGrass && Shape != ScatterShape.Fern &&
                              Shape != ScatterShape.Reed && Shape != ScatterShape.Flowers;

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
                    // Trunk section on its side: cylinder r 0.1 s, length 0.8 s (size = length).
                    ScatterShape.Log => Math.PI * 0.01 * 0.8 * s * s * s,
                    // Column r 0.12 s, h s, plus about a third again in arms.
                    ScatterShape.Cactus => Math.PI * 0.0144 * 1.3 * s * s * s,
                    // Stalk cylinder r 0.08 s, h 0.7 s; cap ellipsoid semi-axes 0.35, 0.12, 0.35 s.
                    ScatterShape.Mushroom => Math.PI * 0.0064 * 0.7 * s * s * s + 4.0 / 3.0 * Math.PI * 0.35 * 0.12 * 0.35 * s * s * s,
                    // Blunt cone r 0.22 s, h s: half again a true cone's volume.
                    ScatterShape.Spire => Math.PI * 0.0484 / 3.0 * 1.5 * s * s * s,
                    // Ellipsoid semi-axes 0.45, 0.35, 0.4 s.
                    ScatterShape.OreVein => 4.0 / 3.0 * Math.PI * 0.45 * 0.35 * 0.4 * s * s * s,
                    // Rosette: cone r 0.4 s, h 0.5 s (size = width).
                    ScatterShape.Fern => Math.PI * 0.16 / 3.0 * 0.5 * s * s * s,
                    // Stand of stems: cone r 0.2 s, h s (size = height).
                    ScatterShape.Reed => Math.PI * 0.04 / 3.0 * s * s * s,
                    // Low cushion: disc r 0.5 s, h 0.3 s, about a third filled (size = width).
                    ScatterShape.Flowers => Math.PI * 0.25 * 0.3 * 0.3 * s * s * s,
                    _ => 0.28 * 0.28 * s * s * s,
                };
                return Math.Max(1, (int)Math.Round(m3 * 1000.0));
            }
        }
    }

    /// <summary>
    /// Rule-layer scatter (P3-09, P3-12, P3-25): which cell holds which tree, rock, mineral vein, spire, shrub or grass,
    /// so creatures can be blocked by them and moves can hit them without any GameObject. ScatterField only draws what this returns.
    /// REQ: planet-world.md (7. 散佈; Q-W15 地貌為主); destructibles.md#實作分層 (散佈物件的位置必須由規則層產生).
    /// </summary>
    /// <remarks>
    /// Two independent layers share one key space. Canopy: each cube-face cell (≈ 10 m) holds at most one tree, rock,
    /// spire, vein or crystal. Understory: each finer cell (≈ 6 m) holds at most one shrub, cane clump, fern, reed,
    /// flower patch or grass tussock, chosen by the altitude band it stands in. Which species a cell can hold comes from
    /// the landform under it (<see cref="ScatterBiomes"/>); canopy cells are grouped into forest stands (≈ 60 m) whose
    /// <see cref="ForestType"/> decides the trees. Every cell draws from its own Pcg32 stream, so an item is the same whenever it is
    /// asked for, in any order. Items avoid sea, roads/buildings (isClear) and steep slopes; an understory item also
    /// keeps off the canopy item next to it.
    /// Pre: single-threaded. Post: items depend only on (seed, planet type, planet, ground/sea/clear/density/biome functions).
    /// Side effects: caches items per cell.
    /// </remarks>
    public sealed class ScatterPlanner
    {
        /// <summary>Nominal cell width at a face centre (m).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float CellM = 10f;

        /// <summary>Bumped whenever the same inputs would give a different item set (testing.md: snapshots follow it).</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: constant; 2 = understory layer added, canopy items bit-identical to 1; 3 = canopy density may follow the
        ///       local landform (the per-point constructor), constant-density planners bit-identical to 2;
        ///       4 = species follow the landform (ScatterBiomes) and forest stands; every item set changed;
        ///       5 = bedrock (ScatterGeology) swaps country rock and the catalog gained granite_tor, which shifts
        ///       every understory kind index by one; positions, sizes and canopy picks of planners without a
        ///       geology map are otherwise as in 4;
        ///       6 = tree health (planet-world.md#自然生物): living trees outside plantations with Health ≤ DeadHealth
        ///       become snags; every item gains Age/Health; positions and other picks as in 5.
        /// Side effects: none.
        /// </remarks>
        public const int GeneratorVersion = 7;

        /// <summary>Mean tree health (0–1000) outside secondary stands.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int HealthMean = 720;

        /// <summary>Standard deviation of tree health.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int HealthSd = 130;

        /// <summary>A secondary stand lowers mean health by this much.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int SecondaryHealthDrop = 80;

        /// <summary>Health at or below which a living tree outside a plantation becomes a snag (≈ 3 %).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int DeadHealth = 470;

        private static readonly int SnagIndex = ScatterBiomes.IndexOf("snag");

        /// <summary>Canopy cells along one edge of a forest stand (≈ 60 m).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int StandCells = 6;

        /// <summary>Nominal understory cell width at a face centre (m).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float UnderstoryCellM = 6f;

        /// <summary>Ground at or above this fraction of the radius is highland (R = 640 m → 7.7 m).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float HighlandFractionOfRadius = 0.012f;

        /// <summary>Ground at or above this fraction of the radius is above the treeline (R = 640 m → 16.6 m).</summary>
        /// <remarks>Pre: none. Post: constant &gt; HighlandFractionOfRadius. Side effects: none.</remarks>
        public const float AlpineFractionOfRadius = 0.026f;

        private const float DefaultPlaceChance = 0.55f;
        private const float MaxSlope = 0.6f; // rise/run over the 3 m probe, ~31°
        private const float ProbeM = 1.5f;
        private const ulong Salt = 0x5CA77E5ul;
        private const ulong UnderstorySalt = 0xF10A5EEDul;
        // Gap between an understory item's body and a canopy trunk or rock, so nothing grows out of a boulder.
        private const float CanopyGapM = 0.4f;
        private const string DefaultType = "verdant_planet";

        // |y| of the unit direction (y = polar axis, LatLon): below → tropical stands, above → cold stands.
        private const float TropicalMaxAbsY = 0.3f;
        private const float ColdMinAbsY = 0.7f;
        private const ulong StandSalt = 0x57A4D5EEDul;
        // A plantation keeps this share of the cell jitter: rows stay readable without looking machine-stamped.
        private const float PlantationJitter = 0.2f;

        private readonly PlanetConfig _config;
        private readonly ulong _seed;
        private readonly ScatterKind[] _kinds;
        private readonly Func<float3, float> _groundAltitude;
        private readonly Func<float3, bool> _isSea;
        private readonly Func<float3, bool> _isClear;
        private readonly float _placeChance;
        private readonly Func<float3, float> _placeChanceAt;
        private readonly Dictionary<long, ScatterItem> _items = new Dictionary<long, ScatterItem>();
        private readonly HashSet<long> _empty = new HashSet<long>();
        private readonly HashSet<long> _queryCells = new HashSet<long>();
        // Separate from _queryCells: an understory cell is evaluated while Query is still walking that set.
        private readonly HashSet<long> _probeCells = new HashSet<long>();
        private readonly int _canopyKinds;
        private readonly int _baseBiome;
        private readonly Func<float3, uint, int> _biomeAt;
        private readonly Func<float3, uint, Geology> _geologyAt;
        private readonly float _maxCanopyBlockRadiusM;
        private readonly long _canopyKeys;

        /// <summary>Creates the planner with the default obstacle density.</summary>
        /// <remarks>
        /// Pre: config valid; groundAltitude non-null; isSea and isClear may be null (no sea mask / nothing to avoid);
        ///      all three are deterministic. planetTypeId may be null (verdant_planet) or unknown (grassland species).
        /// Post: <see cref="Kinds"/> is ScatterBiomes.KindsFor(planetTypeId); every cell grows the planet type's own
        ///       surface landform; no cell evaluated yet. Side effects: none.
        /// </remarks>
        public ScatterPlanner(in PlanetConfig config, ulong seed, string planetTypeId, Func<float3, float> groundAltitude,
            Func<float3, bool> isSea, Func<float3, bool> isClear)
            : this(config, seed, planetTypeId, groundAltitude, isSea, isClear, DefaultPlaceChance)
        {
        }

        /// <summary>Creates the planner with an explicit landform obstacle density <paramref name="placeChance"/>.</summary>
        /// <remarks>
        /// Pre: config valid; groundAltitude non-null; placeChance finite in [0, 1]; isSea and isClear may be null.
        /// Post: as the six-argument constructor, with that density. Side effects: none.
        /// </remarks>
        public ScatterPlanner(in PlanetConfig config, ulong seed, string planetTypeId, Func<float3, float> groundAltitude,
            Func<float3, bool> isSea, Func<float3, bool> isClear, float placeChance)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(groundAltitude != null, "groundAltitude is required");
            Contract.Requires(math.isfinite(placeChance) && placeChance >= 0f && placeChance <= 1f, "placeChance must be in [0, 1]");
            _config = config;
            _seed = seed;
            _kinds = ScatterBiomes.KindsFor(planetTypeId);
            _canopyKinds = ScatterBiomes.CanopySpeciesCount;
            // Between landform stamps (and without a biome map) the planet type's own surface landform grows.
            _baseBiome = ScatterBiomes.BiomeOf(TerrainProfiles.ForPlanetType(planetTypeId ?? DefaultType).Id);
            for (int k = 0; k < _canopyKinds; k++)
            {
                ScatterKind kind = _kinds[k];
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

        /// <summary>Creates the planner with a canopy obstacle density that varies over the planet.</summary>
        /// <remarks>
        /// Pre: as the six-argument constructor; placeChanceAt non-null, deterministic, and returns a finite value in
        ///      [0, 1] for every unit direction (TemplatePlanner.ScatterDensity: the landform under that point).
        /// Post: a canopy cell holds an item iff the constant-density planner with the chance at that item's ground
        ///       point would; positions, kinds and sizes are the same as that planner's. The understory is unaffected.
        ///       (A plantation or secondary stand multiplies either chance by ScatterBiomes.DensityBoost.)
        /// Side effects: none.
        /// </remarks>
        public ScatterPlanner(in PlanetConfig config, ulong seed, string planetTypeId, Func<float3, float> groundAltitude,
            Func<float3, bool> isSea, Func<float3, bool> isClear, Func<float3, float> placeChanceAt)
            : this(config, seed, planetTypeId, groundAltitude, isSea, isClear, DefaultPlaceChance)
        {
            Contract.Requires(placeChanceAt != null, "placeChanceAt is required");
            _placeChanceAt = placeChanceAt;
        }

        /// <summary>Creates the planner with a canopy density and a landform (biome) that both vary over the planet.</summary>
        /// <remarks>
        /// Pre: as the per-point density constructor; biomeAt non-null, deterministic, and maps (unit direction, roll)
        ///      to a valid ScatterBiomes biome index (TemplatePlanner.ScatterBiome: the roll dithers stamp edges).
        /// Post: each cell picks its species from the biome biomeAt returns for it; positions do not depend on biomeAt
        ///       outside plantation stands.
        /// Side effects: none.
        /// </remarks>
        public ScatterPlanner(in PlanetConfig config, ulong seed, string planetTypeId, Func<float3, float> groundAltitude,
            Func<float3, bool> isSea, Func<float3, bool> isClear, Func<float3, float> placeChanceAt,
            Func<float3, uint, int> biomeAt)
            : this(config, seed, planetTypeId, groundAltitude, isSea, isClear, placeChanceAt)
        {
            Contract.Requires(biomeAt != null, "biomeAt is required");
            _biomeAt = biomeAt;
        }

        /// <summary>Creates the planner with a density, a landform and a bedrock that all vary over the planet.</summary>
        /// <remarks>
        /// REQ: planet-world.md#地質變體 (P3-24 a).
        /// Pre: as the biome constructor; geologyAt non-null, deterministic, and maps (unit direction, roll) to a
        ///      defined Geology (TemplatePlanner.ScatterGeologyAt: called with the roll biomeAt was called with).
        /// Post: where a cell's landform table picks country rock, the cell holds ScatterGeology.Substitute's rock for
        ///       the bedrock there instead; every other cell, and every position and yaw, is the biome constructor's.
        /// Side effects: none.
        /// </remarks>
        public ScatterPlanner(in PlanetConfig config, ulong seed, string planetTypeId, Func<float3, float> groundAltitude,
            Func<float3, bool> isSea, Func<float3, bool> isClear, Func<float3, float> placeChanceAt,
            Func<float3, uint, int> biomeAt, Func<float3, uint, Geology> geologyAt)
            : this(config, seed, planetTypeId, groundAltitude, isSea, isClear, placeChanceAt, biomeAt)
        {
            Contract.Requires(geologyAt != null, "geologyAt is required");
            _geologyAt = geologyAt;
        }

        /// <summary>The species this planner picks from (ScatterBiomes.KindsFor): canopy species, then understory species.</summary>
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

        /// <summary>Forest type of the stand canopy cell <paramref name="key"/> belongs to.</summary>
        /// <remarks>
        /// Pre: key names a canopy cell (0 ≤ key &lt; 6·CellsPerFace²).
        /// Post: the type every tree slot of that cell resolves with; cells of one stand (StandCells × StandCells,
        ///       edges dithered by one cell) share it unless they straddle a climate band. Same answer on every call.
        /// Side effects: none.
        /// </remarks>
        public ForestType ForestTypeOfCell(long key)
        {
            Contract.Requires(key >= 0 && key < _canopyKeys, "key must name a canopy cell");
            int n = CellsPerFace;
            int face = (int)(key / ((long)n * n));
            int rem = (int)(key % ((long)n * n));
            return ForestOf(key, face, rem / n, rem % n, out _);
        }

        private ForestType ForestOf(long key, int face, int j, int i, out uint standHash)
        {
            // Integer-only stand lookup: a cell borrows the neighbouring stand at random (±1 cell), which frays the
            // square stand edges without any float noise that peers could round differently.
            uint cell = Mix((ulong)key ^ _seed ^ StandSalt);
            int di = (int)(cell & 3u) - 1;
            int dj = (int)((cell >> 2) & 3u) - 1;
            int si = (i + (di > 1 ? 0 : di) + StandCells) / StandCells;
            int sj = (j + (dj > 1 ? 0 : dj) + StandCells) / StandCells;
            standHash = Mix(_seed ^ StandSalt ^ ((ulong)face << 40) ^ ((ulong)sj << 20) ^ (ulong)si);
            int n = CellsPerFace;
            float3 centre = CubeSphere.FaceToDirection((CubeFace)face, -1f + 2f * (i + 0.5f) / n, -1f + 2f * (j + 0.5f) / n);
            return ScatterBiomes.ForestTypeFor(standHash, ClimateOf(centre));
        }

        private static ForestClimate ClimateOf(float3 centre)
        {
            float absY = math.abs(centre.y);
            return absY < TropicalMaxAbsY ? ForestClimate.Tropical
                : absY > ColdMinAbsY ? ForestClimate.Cold
                : ForestClimate.Temperate;
        }

        // SplitMix64 finaliser.
        private static uint Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15ul;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
            return (uint)((z ^ (z >> 31)) >> 16);
        }

        private bool Evaluate(long key, out ScatterItem item)
        {
            item = default;
            int n = CellsPerFace;
            int f = (int)(key / ((long)n * n));
            var face = (CubeFace)f;
            int rem = (int)(key % ((long)n * n));
            int j = rem / n;
            int i = rem % n;
            // A per-cell stream: the same cell regenerates identically after being streamed out and back in.
            // Every value is drawn up front, so no decision below can shift another's draw.
            var rng = new Pcg32(_seed ^ Salt, (ulong)key);
            float placeRoll = rng.NextFloat();
            float ju = rng.NextFloat();
            float jv = rng.NextFloat();
            uint kindRoll = rng.NextUInt();
            float sizeT = rng.NextFloat();
            float yaw = rng.NextFloat() * 360f;
            uint treeRoll = rng.NextUInt();
            uint biomeRoll = rng.NextUInt();
            // Drawn last: version 4's draws keep their places in the stream.
            uint rockRoll = rng.NextUInt();
            // Drawn after rockRoll (v6) as a mean-free offset: the stand that sets the mean is only known later, and
            // BoundedNormal always consumes 12 draws, so nothing before it moves. ±1000 never clamps (6σ < 1000).
            int healthOffset = BoundedNormal.Sample(ref rng, 0, HealthSd, -1000, 1000);
            // Cheap exit before any ground or stamp lookup: no stand can raise the chance past MaxDensityBoost.
            if (_placeChanceAt == null && placeRoll > math.min(1f, _placeChance * ScatterBiomes.MaxDensityBoost))
            {
                return false;
            }

            // The biome is read at the cell centre: a plantation moves the item, and the move must not change the biome.
            float3 centre = CubeSphere.FaceToDirection(face, -1f + 2f * (i + 0.5f) / n, -1f + 2f * (j + 0.5f) / n);
            int biome = BiomeAt(centre, biomeRoll);
            bool wooded = ScatterBiomes.HasTrees(biome);
            uint standHash = 0u;
            ForestType forest = wooded ? ForestOf(key, f, j, i, out standHash) : ForestType.Mixed;
            if (wooded && forest == ForestType.Plantation)
            {
                ju = 0.5f + (ju - 0.5f) * PlantationJitter;
                jv = 0.5f + (jv - 0.5f) * PlantationJitter;
            }

            float u = math.clamp(-1f + 2f * (i + ju) / n, -1f, 1f);
            float v = math.clamp(-1f + 2f * (j + jv) / n, -1f, 1f);
            float3 dir = CubeSphere.FaceToDirection(face, u, v);
            float chance = _placeChance;
            if (_placeChanceAt != null)
            {
                chance = _placeChanceAt(dir);
                Contract.Requires(math.isfinite(chance) && chance >= 0f && chance <= 1f, "placeChanceAt must return [0, 1]");
            }

            if (wooded)
            {
                chance = math.min(1f, chance * ScatterBiomes.DensityBoost(forest));
            }

            if (placeRoll > chance)
            {
                return false;
            }

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

            int kindIndex = ScatterBiomes.PickCanopy(biome, kindRoll);
            // REQ: planet-world.md#散佈 緯度×海拔 — read at the cell centre like the stand, so a stand shares its climate.
            ForestClimate climate = ClimateOf(centre);
            bool alpine = alt >= AlpineFractionOfRadius * _config.Radius;
            if (kindIndex == ScatterBiomes.TreeSlot)
            {
                kindIndex = ScatterBiomes.PickTree(forest, climate, alt >= HighlandFractionOfRadius * _config.Radius, alpine,
                    standHash, treeRoll, out float lo, out float hi);
                sizeT = math.lerp(lo, hi, sizeT);
            }
            else
            {
                kindIndex = ScatterBiomes.ForClimate(kindIndex, climate, alpine);
                if (_geologyAt != null)
                {
                    // Same point and roll as the biome: on a stamp's dithered edge both come from the same side.
                    Geology geology = _geologyAt(centre, biomeRoll);
                    Contract.Requires(geology >= Geology.Native && geology <= Geology.Slate, "geologyAt must return a defined Geology");
                    kindIndex = ScatterGeology.Substitute(kindIndex, geology, rockRoll);
                }
            }

            // REQ: docs/design/planet-world.md#自然生物 — the unhealthiest tail of living trees stands dead; plantations are
            // tended, so they never convert. Size keeps its roll, so a converted tree is as old as it would have been.
            int health = 1000;
            if (_kinds[kindIndex].Shape == ScatterShape.Tree)
            {
                int mean = wooded && forest == ForestType.Secondary ? HealthMean - SecondaryHealthDrop : HealthMean;
                health = kindIndex == SnagIndex ? 0 : math.clamp(mean + healthOffset, 0, 1000);
                if (health <= DeadHealth && kindIndex != SnagIndex && !(wooded && forest == ForestType.Plantation))
                {
                    kindIndex = SnagIndex;
                    health = 0;
                }
            }

            ScatterKind kind = _kinds[kindIndex];
            item = new ScatterItem(key, kindIndex, kind.Shape, dir, alt, math.lerp(kind.MinSizeM, kind.MaxSizeM, sizeT),
                yaw, kind.SubstanceId, (int)(sizeT * 1000f), health);
            return true;
        }

        private int BiomeAt(float3 dir, uint roll)
        {
            if (_biomeAt == null)
            {
                return _baseBiome;
            }

            int biome = _biomeAt(dir, roll);
            Contract.Requires(biome >= 0 && biome < ScatterBiomes.BiomeIds.Count, "biomeAt must return a ScatterBiomes biome index");
            return biome;
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
            float placeRoll = rng.NextFloat();
            float u = math.clamp(-1f + 2f * (i + rng.NextFloat()) / n, -1f, 1f);
            float v = math.clamp(-1f + 2f * (j + rng.NextFloat()) / n, -1f, 1f);
            uint roll = rng.NextUInt();
            float sizeT = rng.NextFloat();
            float yaw = rng.NextFloat() * 360f;
            uint biomeRoll = rng.NextUInt();
            if (placeRoll > ScatterBiomes.MaxUnderstoryChance)
            {
                return false;
            }

            float3 dir = CubeSphere.FaceToDirection(face, u, v);
            // REQ: Q-W15 — understory density follows the landform (desert sparse, swamp and forest dense).
            int biome = BiomeAt(dir, biomeRoll);
            if (placeRoll > ScatterBiomes.UnderstoryChance(biome))
            {
                return false;
            }

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
            int kindIndex = ScatterBiomes.PickUnderstory(biome, roll, alt >= HighlandFractionOfRadius * _config.Radius);
            ScatterKind kind = _kinds[kindIndex];
            item = new ScatterItem(key, kindIndex, kind.Shape, dir, alt, math.lerp(kind.MinSizeM, kind.MaxSizeM, sizeT),
                yaw, kind.SubstanceId);
            return !TouchesCanopy(item);
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
