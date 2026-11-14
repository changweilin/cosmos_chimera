using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Regional building tradition (地區): materials, roof shape and palette, one per planet type.</summary>
    /// <remarks>Pre: none. Post: stable values (they seed the mesh streams). Side effects: none.</remarks>
    public enum ArchRegion
    {
        /// <summary>翠綠星: plaster and timber frames under sagging gable roofs.</summary>
        Verdant = 0,

        /// <summary>熔岩星: battered basalt blocks, flat roofs behind parapets.</summary>
        Lava = 1,

        /// <summary>冰晶星: dark stave walls under steep snow-shedding gables.</summary>
        Ice = 2,

        /// <summary>風暴星: low hipped slate roofs and eight-sided towers that give the wind no flat wall.</summary>
        Storm = 3,

        /// <summary>晶礦星: sandstone under domed tile roofs.</summary>
        Crystal = 4,

        /// <summary>晨昏星: swept eaves with lifted corners.</summary>
        Twilight = 5,
    }

    /// <summary>Building era (時間): one per planet, drawn from the seed; ruins and the lair are always Ancient.</summary>
    /// <remarks>Pre: none. Post: stable values (they seed the mesh streams). Side effects: none.</remarks>
    public enum ArchEra
    {
        /// <summary>Tents, thatch and unglazed openings.</summary>
        Primal = 0,

        /// <summary>Timber frames on a low stone footing.</summary>
        Agrarian = 1,

        /// <summary>Jettied upper storeys, shutters and chimneys.</summary>
        Mercantile = 2,

        /// <summary>Brick, cornices, tall glazing and smokestacks.</summary>
        Industrial = 3,

        /// <summary>What is left of any other era: broken courses without a roof (遺跡), and the lair's megaliths.</summary>
        Ancient = 4,

        /// <summary>Brick and brass: bulging mansards, pipes up the walls, ember-topped stacks (蒸汽龐克).</summary>
        Steampunk = 5,

        /// <summary>Pale composite shells, strip glazing and light bands at every floor (未來／太空).</summary>
        Future = 6,

        /// <summary>Scrap-plated walls under sheet roofs, boarded openings (廢土).</summary>
        Wasteland = 7,
    }

    /// <summary>
    /// The secondary style a building borrows from, and how old and how damaged its settlement is on average.
    /// Each building draws its own share, age and damage around these means (bounded normal), so one street holds
    /// pure-primary houses next to hybrids, and fresh ones next to patched ones.
    /// </summary>
    /// <remarks>Pre: built by the constructor or <see cref="Pure"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct ArchMix
    {
        /// <summary>Describes a settlement's style mix.</summary>
        /// <remarks>
        /// Pre: defined enums; weight, ageMean and damageMean in [0, 1]; 0.5 ≤ pitchScale ≤ 1.6; 0 ≤ plinthAddM ≤ 1.
        /// Post: properties return the arguments. Side effects: none.
        /// </remarks>
        public ArchMix(ArchRegion secondaryRegion, ArchEra secondaryEra, float weight, float ageMean, float damageMean,
            float pitchScale = 1f, float plinthAddM = 0f)
        {
            Contract.Requires(Enum.IsDefined(typeof(ArchRegion), secondaryRegion) && Enum.IsDefined(typeof(ArchEra), secondaryEra),
                "secondary region and era must be defined");
            Contract.Requires(weight >= 0f && weight <= 1f && ageMean >= 0f && ageMean <= 1f && damageMean >= 0f && damageMean <= 1f,
                "weight, ageMean and damageMean in [0, 1]");
            Contract.Requires(pitchScale >= 0.5f && pitchScale <= 1.6f && plinthAddM >= 0f && plinthAddM <= 1f,
                "pitchScale in [0.5, 1.6]; plinthAddM in [0, 1]");
            SecondaryRegion = secondaryRegion;
            SecondaryEra = secondaryEra;
            Weight = weight;
            AgeMean = ageMean;
            DamageMean = damageMean;
            PitchScale = pitchScale;
            PlinthAddM = plinthAddM;
        }

        /// <summary>Tradition the secondary traits come from.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArchRegion SecondaryRegion { get; }

        /// <summary>Era the secondary traits come from.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArchEra SecondaryEra { get; }

        /// <summary>Mean share of secondary traits; 0 = every building is pure primary.</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float Weight { get; }

        /// <summary>Mean age: 0 = just built, 1 = generations of weather (grime, sag, lean, faded colour).</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float AgeMean { get; }

        /// <summary>Mean damage: 0 = intact, 1 = boarded windows, holed roofs, broken chimneys, propped walls.</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float DamageMean { get; }

        /// <summary>Roof pitch multiplier from the region theme (steeper on highlands, lower on coasts).</summary>
        /// <remarks>Pre: none. Post: in [0.5, 1.6]. Side effects: none.</remarks>
        public float PitchScale { get; }

        /// <summary>Extra stone footing from the region theme (riverbanks build above the flood), metres.</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float PlinthAddM { get; }

        /// <summary>A mix with no secondary style and the era's usual age and damage.</summary>
        /// <remarks>Pre: defined arguments. Post: Weight == 0. Side effects: none.</remarks>
        public static ArchMix Pure(ArchRegion region, ArchEra era)
        {
            (float age, float damage) = ArchitectureMeshes.EraWear(era);
            return new ArchMix(region, era, 0f, age, damage);
        }
    }

    /// <summary>The style axes of one planet: tradition and era, and the secondary of each that its towns lean toward.</summary>
    /// <remarks>Pre: built by <see cref="ArchitectureMeshes.PlanetStyleFor"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct ArchPlanetStyle
    {
        internal ArchPlanetStyle(ArchRegion region, ArchEra era, ArchRegion secondaryRegion, ArchEra secondaryEra, float weight)
        {
            Region = region;
            Era = era;
            SecondaryRegion = secondaryRegion;
            SecondaryEra = secondaryEra;
            Weight = weight;
        }

        /// <summary>Primary tradition (the planet type's).</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArchRegion Region { get; }

        /// <summary>Era of every inhabited settlement on the planet.</summary>
        /// <remarks>Pre: none. Post: defined, never Ancient. Side effects: none.</remarks>
        public ArchEra Era { get; }

        /// <summary>Secondary tradition.</summary>
        /// <remarks>Pre: none. Post: defined, != Region. Side effects: none.</remarks>
        public ArchRegion SecondaryRegion { get; }

        /// <summary>Secondary era: one the primary era grew out of or is growing into.</summary>
        /// <remarks>Pre: none. Post: defined, != Era, never Ancient. Side effects: none.</remarks>
        public ArchEra SecondaryEra { get; }

        /// <summary>Mean share of secondary traits before the region theme adjusts it.</summary>
        /// <remarks>Pre: none. Post: in [0.15, 0.45]. Side effects: none.</remarks>
        public float Weight { get; }
    }

    /// <summary>Everything that decides one building mesh.</summary>
    /// <remarks>Pre: built by the constructor. Post: immutable. Side effects: none.</remarks>
    public readonly struct ArchSpec
    {
        /// <summary>Describes one building.</summary>
        /// <remarks>
        /// Pre: kind, region and era defined; 1 ≤ widthM, depthM ≤ 40; 1 ≤ storeys ≤ 12; 0 ≤ foundationM ≤ 60 (the wall
        ///      height added below the entrance so the uphill side of a sloped lot still shows full storeys).
        /// Post: properties return the arguments. Side effects: none.
        /// </remarks>
        public ArchSpec(BuildingKind kind, ArchRegion region, ArchEra era, float widthM, float depthM, int storeys,
            float foundationM, ulong seed)
            : this(kind, region, era, widthM, depthM, storeys, foundationM, seed, ArchMix.Pure(region, era))
        {
        }

        /// <summary>Describes one building of a settlement with a style mix.</summary>
        /// <remarks>Pre: as the other constructor; mix built by its constructor. Post: properties return the arguments. Side effects: none.</remarks>
        public ArchSpec(BuildingKind kind, ArchRegion region, ArchEra era, float widthM, float depthM, int storeys,
            float foundationM, ulong seed, in ArchMix mix)
        {
            Contract.Requires(Enum.IsDefined(typeof(BuildingKind), kind) && Enum.IsDefined(typeof(ArchRegion), region) &&
                              Enum.IsDefined(typeof(ArchEra), era), "kind, region and era must be defined");
            Contract.Requires(widthM >= 1f && widthM <= 40f && depthM >= 1f && depthM <= 40f, "footprint in [1, 40] m");
            Contract.Requires(storeys >= 1 && storeys <= 12, "storeys in [1, 12]");
            Contract.Requires(foundationM >= 0f && foundationM <= 60f, "foundationM in [0, 60]");
            Kind = kind;
            Region = region;
            Era = era;
            WidthM = widthM;
            DepthM = depthM;
            Storeys = storeys;
            FoundationM = foundationM;
            Seed = seed;
            Mix = mix;
        }

        /// <summary>Secondary style and mean wear of the settlement the building stands in.</summary>
        /// <remarks>Pre: none. Post: as constructed (pure primary by default). Side effects: none.</remarks>
        public ArchMix Mix { get; }

        /// <summary>Rule-layer building kind.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public BuildingKind Kind { get; }

        /// <summary>Regional tradition.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArchRegion Region { get; }

        /// <summary>Era.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public ArchEra Era { get; }

        /// <summary>Footprint across the entrance (local X), metres.</summary>
        /// <remarks>Pre: none. Post: in [1, 40]. Side effects: none.</remarks>
        public float WidthM { get; }

        /// <summary>Footprint along the entrance direction (local Z), metres.</summary>
        /// <remarks>Pre: none. Post: in [1, 40]. Side effects: none.</remarks>
        public float DepthM { get; }

        /// <summary>Storeys of <see cref="ArchitectureMeshes.StoreyM"/> each.</summary>
        /// <remarks>Pre: none. Post: in [1, 12]. Side effects: none.</remarks>
        public int Storeys { get; }

        /// <summary>Height of the entrance floor above the mesh origin, metres.</summary>
        /// <remarks>Pre: none. Post: in [0, 60]. Side effects: none.</remarks>
        public float FoundationM { get; }

        /// <summary>Variation seed (door side, chimney, broken courses…).</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public ulong Seed { get; }
    }

    /// <summary>Material colours of one (region, era) style.</summary>
    /// <remarks>Pre: built by <see cref="ArchitectureMeshes.PaletteFor"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct ArchPalette
    {
        internal ArchPalette(Color wall, Color roof, Color trim, Color glow, Color weathering, float weatheringStrength)
        {
            Wall = wall;
            Roof = roof;
            Trim = trim;
            Glow = glow;
            Weathering = weathering;
            WeatheringStrength = weatheringStrength;
        }

        /// <summary>Walls, footings, canvas (sub-mesh <see cref="ArchitectureMeshes.WallSubMesh"/>).</summary>
        /// <remarks>Pre: none. Post: opaque. Side effects: none.</remarks>
        public Color Wall { get; }

        /// <summary>Roof covering, shutters and banners (sub-mesh <see cref="ArchitectureMeshes.RoofSubMesh"/>).</summary>
        /// <remarks>Pre: none. Post: opaque. Side effects: none.</remarks>
        public Color Roof { get; }

        /// <summary>Timber, frames and doors (sub-mesh <see cref="ArchitectureMeshes.TrimSubMesh"/>).</summary>
        /// <remarks>Pre: none. Post: opaque. Side effects: none.</remarks>
        public Color Trim { get; }

        /// <summary>Lit windows, lanterns and runes (sub-mesh <see cref="ArchitectureMeshes.GlowSubMesh"/>).</summary>
        /// <remarks>Pre: none. Post: opaque. Side effects: none.</remarks>
        public Color Glow { get; }

        /// <summary>What settles on walls and roofs (moss, soot, snow): the accent colour of the wall and roof materials.</summary>
        /// <remarks>Pre: none. Post: opaque. Side effects: none.</remarks>
        public Color Weathering { get; }

        /// <summary>How strongly the weathering mask shows.</summary>
        /// <remarks>Pre: none. Post: in [0, 1]. Side effects: none.</remarks>
        public float WeatheringStrength { get; }
    }

    /// <summary>
    /// Asset-free building meshes (P3-11 presentation): every RunLayout footprint becomes a tent, house, hall, tower,
    /// megalith or ruin whose form follows the planet's regional tradition and the settlement's era.
    /// REQ: planet-world.md#道路與建築; vision.md#畫風與沉浸感 (chunky silhouettes, flat tones, no outline on scenery).
    /// Drawn with Cosmimera/ToonScenery; shares its channel layout and mesh assembly with <see cref="SceneryMeshes"/>.
    /// </summary>
    /// <remarks>
    /// Meshes are in metres in the building's own frame: +Z = entrance side, +X = across, +Y = up, origin at the sunk
    /// base centre. Walls keep the rule layer's footprint and storey height (WorldObjects derives HP and drops from
    /// that volume, LayoutClearance blocks creatures on it); only roofs, porches and trim reach outside it.
    /// Pre: main thread (creates Unity objects). Post: results depend only on the spec. Side effects: allocates.
    /// </remarks>
    public static class ArchitectureMeshes
    {
        /// <summary>Sub-mesh of walls and footings.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int WallSubMesh = 0;

        /// <summary>Sub-mesh of roof coverings and cloth.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int RoofSubMesh = 1;

        /// <summary>Sub-mesh of timber, frames and doors.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int TrimSubMesh = 2;

        /// <summary>Sub-mesh of self-lit parts.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GlowSubMesh = 3;

        /// <summary>Sub-meshes of every building mesh (unused ones are empty, so material slots never shift).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int SubMeshCount = 4;

        /// <summary>Height of one storey, metres.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float StoreyM = 3f;

        /// <summary>Farthest any roof, porch or step reaches outside the footprint, metres.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float MaxOverhangM = 2.2f;

        private const ulong Salt = 0xA2C417EC7ul;
        private const float FacetCos = 0.8f; // same crease as SceneryMeshes: hard masonry edges, soft lathe surfaces
        private const float RuinKeep = 0.75f; // a ruin's tallest course, as a share of the storeys it keeps
        private const float TentPeakM = 3.2f;
        // A roof is built to pass through the wall head exactly at the wall face; seated this much higher, the wall's
        // top edge stays under the covering instead of drawing a line across it.
        private const float RoofSeatM = 0.06f;
        private const float TentBodyM = 1.5f; // the lowest tent (the snow house) is just taller than this

        /// <summary>Regional tradition of a planet type.</summary>
        /// <remarks>Pre: none (unknown or null ids read as verdant, like PlanetLook). Post: defined. Side effects: none.</remarks>
        public static ArchRegion RegionFor(string planetTypeId) => planetTypeId switch
        {
            "lava_planet" => ArchRegion.Lava,
            "ice_crystal_planet" => ArchRegion.Ice,
            "storm_planet" => ArchRegion.Storm,
            "mineral_planet" => ArchRegion.Crystal,
            "twilight_planet" => ArchRegion.Twilight,
            _ => ArchRegion.Verdant,
        };

        private static readonly ArchEra[] InhabitedEras =
        {
            ArchEra.Primal, ArchEra.Agrarian, ArchEra.Mercantile, ArchEra.Industrial, ArchEra.Steampunk, ArchEra.Future,
            ArchEra.Wasteland,
        };

        // Eras a town can plausibly be halfway to or from: the secondary era is always one of these.
        private static ArchEra[] Kin(ArchEra era) => era switch
        {
            ArchEra.Primal => new[] { ArchEra.Agrarian },
            ArchEra.Agrarian => new[] { ArchEra.Primal, ArchEra.Mercantile },
            ArchEra.Mercantile => new[] { ArchEra.Agrarian, ArchEra.Industrial },
            ArchEra.Industrial => new[] { ArchEra.Mercantile, ArchEra.Steampunk },
            ArchEra.Steampunk => new[] { ArchEra.Industrial, ArchEra.Future },
            ArchEra.Future => new[] { ArchEra.Steampunk, ArchEra.Industrial },
            _ => new[] { ArchEra.Industrial, ArchEra.Steampunk, ArchEra.Future },
        };

        /// <summary>Usual (age, damage) means of an era's buildings.</summary>
        /// <remarks>Pre: defined era. Post: both in [0, 1]. Side effects: none.</remarks>
        public static (float age, float damage) EraWear(ArchEra era) => era switch
        {
            ArchEra.Primal => (0.45f, 0.12f),
            ArchEra.Agrarian => (0.45f, 0.1f),
            ArchEra.Mercantile => (0.35f, 0.08f),
            ArchEra.Industrial => (0.4f, 0.1f),
            ArchEra.Steampunk => (0.4f, 0.1f),
            ArchEra.Future => (0.1f, 0.03f),
            ArchEra.Wasteland => (0.8f, 0.5f),
            _ => (0.9f, 0.9f),
        };

        /// <summary>Style axes of a planet: the planet type gives the tradition, the seed draws the era and both secondaries.</summary>
        /// <remarks>
        /// REQ: settlement-structures.md#建築造型 (整顆星球同一時代，由 seed 獨立抽).
        /// Pre: none (unknown or null ids read as verdant). Post: same arguments, same style on every client; Era and
        /// SecondaryEra are never Ancient; SecondaryRegion != Region. Side effects: none.
        /// </remarks>
        public static ArchPlanetStyle PlanetStyleFor(string planetTypeId, ulong seed)
        {
            ArchRegion region = RegionFor(planetTypeId);
            var rng = new Pcg32(Salt ^ seed, 0x455241ul);
            ArchEra era = InhabitedEras[rng.NextUInt((uint)InhabitedEras.Length)];
            // Skip over the primary: five other traditions, each equally likely.
            var secondary = (ArchRegion)(((int)region + 1 + (int)rng.NextUInt(5)) % 6);
            ArchEra[] kin = Kin(era);
            return new ArchPlanetStyle(region, era, secondary, kin[rng.NextUInt((uint)kin.Length)], 0.15f + 0.3f * rng.NextFloat());
        }

        /// <summary>Era a settlement of <paramref name="kind"/> is drawn in on a planet of <paramref name="planet"/>.</summary>
        /// <remarks>Pre: defined kind. Post: Ancient for Ruins and Lair, otherwise planet.Era. Side effects: none.</remarks>
        public static ArchEra SettlementEra(in ArchPlanetStyle planet, SettlementKind kind)
        {
            Contract.Requires(Enum.IsDefined(typeof(SettlementKind), kind), "kind must be defined");
            return kind == SettlementKind.Ruins || kind == SettlementKind.Lair ? ArchEra.Ancient : planet.Era;
        }

        /// <summary>Style mix of one settlement: the planet's secondaries, nudged by the region theme and the settlement kind.</summary>
        /// <remarks>
        /// REQ: settlement-structures.md#建築造型 (星球類型為主、區域主題微調).
        /// Pre: defined kind; settlementIndex ≥ 0; themeId may be null or unknown (no nudge).
        /// Post: same arguments, same mix; Ruins and Lair get a pure Ancient mix; otherwise Weight ≤ 0.7 and the
        ///       secondary era is the planet's or another kin of its era.
        /// Side effects: none.
        /// </remarks>
        public static ArchMix MixFor(in ArchPlanetStyle planet, SettlementKind kind, string themeId, ulong seed, int settlementIndex)
        {
            Contract.Requires(settlementIndex >= 0, "settlementIndex must be >= 0");
            ArchEra era = SettlementEra(planet, kind);
            if (era == ArchEra.Ancient)
            {
                return ArchMix.Pure(planet.Region, era);
            }

            // What the land asks of a builder: the tradition it pulls toward, and how it wears what stands on it.
            (int lean, float weight, float age, float damage, float pitch, float plinth) theme = themeId switch
            {
                "forest" => ((int)ArchRegion.Verdant, 0.1f, 0.1f, 0f, 1.05f, 0f),
                "riverbank" => (-1, 0f, 0.05f, 0f, 1f, 0.5f),
                "highland" => ((int)ArchRegion.Ice, 0.1f, 0f, 0f, 1.2f, 0f),
                "coast" => ((int)ArchRegion.Storm, 0.1f, 0.05f, 0f, 0.9f, 0.2f),
                "crater" => ((int)ArchRegion.Lava, 0.1f, 0f, 0.08f, 1f, 0f),
                "ice_shelf" => ((int)ArchRegion.Ice, 0.15f, 0f, 0f, 1.15f, 0f),
                "canyon" => ((int)ArchRegion.Crystal, 0.1f, 0.05f, 0f, 0.95f, 0f),
                "ruins" => (-1, 0f, 0.2f, 0.15f, 1f, 0f),
                _ => (-1, 0f, 0f, 0f, 1f, 0f),
            };
            // A capital is kept up; a hamlet makes do.
            float upkeep = kind switch
            {
                SettlementKind.Tribe => 0.05f,
                SettlementKind.Village => 0.1f,
                SettlementKind.City => -0.05f,
                SettlementKind.CityState => -0.1f,
                _ => 0f,
            };

            var rng = new Pcg32(Salt ^ seed, 0x534554ul + (ulong)settlementIndex);
            ArchRegion secondary = planet.SecondaryRegion;
            bool leans = rng.NextFloat() < 0.6f;
            if (theme.lean >= 0 && theme.lean != (int)planet.Region && leans)
            {
                secondary = (ArchRegion)theme.lean;
            }

            ArchEra[] kin = Kin(planet.Era);
            ArchEra other = kin[rng.NextUInt((uint)kin.Length)];
            ArchEra secondaryEra = rng.NextFloat() < 0.3f ? other : planet.SecondaryEra;
            (float eraAge, float eraDamage) = EraWear(planet.Era);
            return new ArchMix(secondary, secondaryEra,
                math.clamp(planet.Weight + theme.weight + 0.12f * Gauss(ref rng), 0f, 0.7f),
                math.saturate(eraAge + upkeep + theme.age + 0.08f * Gauss(ref rng)),
                math.saturate(eraDamage + theme.damage + 0.04f * Gauss(ref rng)),
                theme.pitch, theme.plinth);
        }

        // Irwin–Hall(4) scaled to unit variance: bounded (|g| ≤ 3.47), so no draw can push a roof past MaxOverhangM,
        // and free of log/sqrt whose last bit could differ between clients.
        private static float Gauss(ref Pcg32 rng) =>
            (rng.NextFloat() + rng.NextFloat() + rng.NextFloat() + rng.NextFloat() - 2f) * 1.7320508f;

        /// <summary>Whether <paramref name="spec"/> is drawn as a roofless ruin.</summary>
        /// <remarks>Pre: none. Post: true iff Ancient and a house, hall or tower. Side effects: none.</remarks>
        public static bool IsRuin(in ArchSpec spec) =>
            spec.Era == ArchEra.Ancient && spec.Kind != BuildingKind.Pillar && spec.Kind != BuildingKind.Tent;

        /// <summary>Material colours of a style.</summary>
        /// <remarks>
        /// Pre: defined arguments. Post: megaliths (Pillar) share one dark stone in every region and keep the region's
        /// glow; ruins are the region's wall colour washed toward grey. Side effects: none.
        /// </remarks>
        public static ArchPalette PaletteFor(ArchRegion region, ArchEra era, BuildingKind kind)
        {
            (int wall, int roof, int trim, int glow, int weather) c = region switch
            {
                ArchRegion.Verdant => era switch
                {
                    ArchEra.Primal => (0xD8C8A0, 0xC9A95A, 0x5A3E2B, 0xFFD27A, 0x6E8F45),
                    ArchEra.Industrial => (0xB5654A, 0x4E5B66, 0x2F2A28, 0xFFD27A, 0x6E8F45),
                    _ => (0xE8DCC0, 0xA4553A, 0x5A3E2B, 0xFFD27A, 0x6E8F45),
                },
                ArchRegion.Lava => era switch
                {
                    ArchEra.Primal => (0x7A5A48, 0x3A2E2E, 0x2A2224, 0xFF8A3C, 0x2B2326),
                    ArchEra.Industrial => (0x6B3A32, 0x2E2E33, 0xC07A3A, 0xFF8A3C, 0x2B2326),
                    _ => (0x5A4E50, 0x8E3B2E, 0x2A2224, 0xFF8A3C, 0x2B2326),
                },
                ArchRegion.Ice => era switch
                {
                    ArchEra.Primal => (0xE9F1F6, 0xBFD4E0, 0x5A4A40, 0xFFE0A0, 0xF4F8FB),
                    ArchEra.Industrial => (0x7C8A96, 0x34424E, 0xE6EEF2, 0xFFE0A0, 0xF4F8FB),
                    _ => (0x8A6A52, 0x2F5D6B, 0xE6EEF2, 0xFFE0A0, 0xF4F8FB),
                },
                ArchRegion.Storm => era switch
                {
                    ArchEra.Primal => (0xA89C86, 0x6A6258, 0x4A4038, 0xBFE8FF, 0x5E6772),
                    ArchEra.Industrial => (0x6F7785, 0x2B3040, 0xD0A040, 0xBFE8FF, 0x5E6772),
                    _ => (0x8E97A3, 0x3B4470, 0x4FA08A, 0xBFE8FF, 0x5E6772),
                },
                ArchRegion.Crystal => era switch
                {
                    ArchEra.Primal => (0xC9A070, 0x8A6A4A, 0x5A4030, 0x9FF0E0, 0xE0A8C8),
                    ArchEra.Industrial => (0xA8845E, 0x4A3A78, 0x2E6E7A, 0x9FF0E0, 0xE0A8C8),
                    _ => (0xD9B98A, 0x6A4FA8, 0x3E8E9A, 0x9FF0E0, 0xE0A8C8),
                },
                _ => era switch
                {
                    ArchEra.Primal => (0x8A7A9A, 0x3A3458, 0x3A3040, 0xFFC86A, 0xA8B8A0),
                    ArchEra.Industrial => (0x544C6A, 0x1C2040, 0xC09A48, 0xFFC86A, 0xA8B8A0),
                    _ => (0x6E6488, 0x232A52, 0xD8B45A, 0xFFC86A, 0xA8B8A0),
                },
            };

            if (kind == BuildingKind.Pillar)
            {
                return new ArchPalette(Hex(0x4A4450), Hex(0x4A4450), Hex(0x2E2A34), Hex(c.glow), Hex(c.weather), 0.5f);
            }

            if (era == ArchEra.Ancient)
            {
                return new ArchPalette(Color.Lerp(Hex(0x8C8880), Hex(c.wall), 0.25f), Hex(c.roof), Hex(0x4A4038),
                    Hex(c.glow), Hex(c.weather), 0.9f);
            }

            float strength = region == ArchRegion.Ice ? 0.9f : 0.65f;
            // The late eras bring their own materials; the region still shows through as a tint, so a brass town on
            // an ice world does not look like one on a lava world.
            switch (era)
            {
                case ArchEra.Steampunk:
                    return new ArchPalette(Color.Lerp(Hex(0x8C4F3C), Hex(c.wall), 0.3f), Color.Lerp(Hex(0x3F5A55), Hex(c.roof), 0.3f),
                        Color.Lerp(Hex(0xC8963C), Hex(c.trim), 0.15f), Color.Lerp(Hex(0xFFB04A), Hex(c.glow), 0.5f), Hex(c.weather), strength);
                case ArchEra.Future:
                    return new ArchPalette(Color.Lerp(Hex(0xE4EAEE), Hex(c.wall), 0.22f), Color.Lerp(Hex(0x36405A), Hex(c.roof), 0.4f),
                        Color.Lerp(Hex(0x8D99AA), Hex(c.trim), 0.2f), Color.Lerp(Hex(0x7FF2FF), Hex(c.glow), 0.45f), Hex(c.weather),
                        strength * 0.6f);
                case ArchEra.Wasteland:
                    return new ArchPalette(Color.Lerp(Hex(0x9C7E5E), Hex(c.wall), 0.35f), Color.Lerp(Hex(0x7C4A30), Hex(c.roof), 0.3f),
                        Hex(0x4C4138), Color.Lerp(Hex(0xFF7A3C), Hex(c.glow), 0.5f), Color.Lerp(Hex(0x6B5A48), Hex(c.weather), 0.4f),
                        math.max(strength, 0.85f));
            }

            return new ArchPalette(Hex(c.wall), Hex(c.roof), Hex(c.trim), Hex(c.glow), Hex(c.weather), strength);
        }

        /// <summary>Material colours of a settlement: its primary style washed toward the secondary by the mix weight.</summary>
        /// <remarks>
        /// Pre: defined arguments. Post: equals the primary palette when mix.Weight == 0 and mix.AgeMean is the era's
        /// usual; megaliths and Ancient ignore the mix; older settlements weather more strongly. Side effects: none.
        /// </remarks>
        public static ArchPalette PaletteFor(ArchRegion region, ArchEra era, BuildingKind kind, in ArchMix mix)
        {
            ArchPalette a = PaletteFor(region, era, kind);
            if (kind == BuildingKind.Pillar || era == ArchEra.Ancient)
            {
                return a;
            }

            float strength = math.saturate(a.WeatheringStrength + (mix.AgeMean - EraWear(era).age) * 0.5f);
            if (mix.Weight <= 0f)
            {
                return new ArchPalette(a.Wall, a.Roof, a.Trim, a.Glow, a.Weathering, strength);
            }

            // Capped below half: a settlement must still read as its planet's from across a valley.
            float t = mix.Weight * 0.7f;
            ArchPalette b = PaletteFor(mix.SecondaryRegion, mix.SecondaryEra, kind);
            return new ArchPalette(Color.Lerp(a.Wall, b.Wall, t), Color.Lerp(a.Roof, b.Roof, t), Color.Lerp(a.Trim, b.Trim, t),
                Color.Lerp(a.Glow, b.Glow, t), Color.Lerp(a.Weathering, b.Weathering, t), strength);
        }

        /// <summary>Size of the box that blocks the character for <paramref name="spec"/>, standing on the mesh origin.</summary>
        /// <remarks>
        /// Pre: none. Post: x ≤ WidthM, z ≤ DepthM (never wider than the rule layer's footprint); y is the height of the
        /// solid part: walls for buildings, the average standing course for ruins, the lowest tent's shoulder for tents.
        /// Side effects: none.
        /// </remarks>
        public static Vector3 BodySize(in ArchSpec spec)
        {
            if (spec.Kind == BuildingKind.Tent)
            {
                return new Vector3(spec.WidthM * 0.85f, spec.FoundationM + TentBodyM, spec.DepthM * 0.85f);
            }

            float height = IsRuin(spec) ? RuinHeight(spec) * 0.6f : spec.Storeys * StoreyM;
            return new Vector3(spec.WidthM, spec.FoundationM + height, spec.DepthM);
        }

        /// <summary>Builds the mesh of <paramref name="spec"/>.</summary>
        /// <remarks>
        /// Pre: main thread.
        /// Post: a new mesh with normals and colours and exactly <see cref="SubMeshCount"/> sub-meshes; the same spec
        ///       always gives the same vertices; nothing reaches more than <see cref="MaxOverhangM"/> outside the
        ///       footprint or more than 0.5 m below the origin; walls rise from below the origin, so a sloped lot never
        ///       shows a gap.
        /// Side effects: allocates a Mesh the caller owns (and must destroy).
        /// </remarks>
        public static Mesh Build(in ArchSpec spec)
        {
            var rng = new Pcg32(Salt ^ spec.Seed, ((ulong)spec.Kind << 16) | ((ulong)spec.Region << 8) | (ulong)spec.Era);
            var g = new Geo[SubMeshCount];
            for (int i = 0; i < g.Length; i++)
            {
                g[i] = new Geo();
            }

            bool ruin = IsRuin(spec);
            Variation vary = Variation.Draw(spec, ref rng);
            if (spec.Kind == BuildingKind.Tent)
            {
                BuildTent(g, spec, vary, ref rng);
            }
            else if (spec.Kind == BuildingKind.Pillar)
            {
                BuildPillar(g, spec, ref rng);
            }
            else if (ruin)
            {
                BuildRuin(g, spec, ref rng);
            }
            else if (spec.Kind == BuildingKind.Tower)
            {
                BuildTower(g, spec, Shape(spec, vary, ref rng), vary, ref rng);
            }
            else
            {
                BuildLodge(g, spec, Shape(spec, vary, ref rng), vary, ref rng);
            }

            float foundation = spec.FoundationM;
            float3 offset = new float3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()) * 41f;
            if (spec.Kind != BuildingKind.Pillar)
            {
                Settle(g, spec, vary, offset);
            }

            // Snow lies on far more of a roof than moss does; ruins are overgrown all the way up.
            float settle = spec.Region == ArchRegion.Ice ? 0.25f : 0f;
            float decay = ruin || spec.Kind == BuildingKind.Pillar ? 0.45f : 0f;
            // What settles spreads with the years: a new roof is clean, an old wall is streaked to the eaves.
            float aged = ruin ? 0f : vary.Age - 0.4f;
            float3 tint = vary.Tint;
            var data = new SceneryMeshes.MeshData(SubMeshCount);
            data.EmitFaceted(g[WallSubMesh].Part, WallSubMesh, FacetCos, (p, n, c) =>
            {
                float patch = noise.snoise(p * 0.8f + offset) * 0.5f + 0.5f;
                // Splash-back and damp darken the foot of a wall; ledges that look up collect whatever falls.
                float foot = 1f - math.smoothstep(0.1f, 1.3f, p.y - foundation);
                float ledge = math.smoothstep(0.6f, 0.9f, n.y);
                float grime = foot * (0.5f + 0.6f * patch) + ledge * (0.35f + settle) +
                              decay * math.smoothstep(0.45f, 0.7f, patch) +
                              math.max(0f, aged) * 1.2f * math.smoothstep(0.5f, 0.75f, patch);
                // min: planted parts (window boxes) arrive already fully in the accent colour.
                c.a = math.min(c.a, 1f - math.saturate(grime));
                return (n, Tinted(c, tint));
            });
            data.EmitFaceted(g[RoofSubMesh].Part, RoofSubMesh, FacetCos, (p, n, c) =>
            {
                // Patches, not a coat: the roof's own colour is what tells one region's village from another's.
                float patch = noise.snoise(p * 0.9f + offset) * 0.5f + 0.5f;
                float lie = settle + aged * 0.3f;
                float settled = math.smoothstep(0.3f, 0.7f, n.y) * math.smoothstep(0.58f - lie, 0.76f - lie, patch);
                c.a = 1f - settled;
                return (n, Tinted(c, tint));
            });
            // Timber greys with the wall but keeps half its own colour: frames must stay readable against plaster.
            float3 trimTint = math.lerp(new float3(1f), tint, 0.5f);
            data.EmitFaceted(g[TrimSubMesh].Part, TrimSubMesh, FacetCos, (p, n, c) => (n, Tinted(c, trimTint)));
            data.EmitFaceted(g[GlowSubMesh].Part, GlowSubMesh, FacetCos, null);
            return data.ToMesh("Arch_" + spec.Kind + "_" + spec.Region + "_" + spec.Era);
        }

        private static Color Tinted(Color c, float3 tint)
        {
            // Upper bound keeps baked tone × tint inside what the toon ramp shows as a step rather than a blow-out.
            c.r = math.min(c.r * tint.x, 1.29f);
            c.g = math.min(c.g * tint.y, 1.29f);
            c.b = math.min(c.b * tint.z, 1.29f);
            return c;
        }

        // ---- per-building variation ----------------------------------------------------------------------------

        // One building's draw around its settlement's means. Every field is a bounded normal, so most of a street sits
        // near the mean and the odd house stands out, instead of every house being equally "random".
        private struct Variation
        {
            public float Mix;      // share of secondary traits, 0..1
            public float Age;      // 0..1
            public float Damage;   // 0..1
            public float3 Tint;    // multiplies the baked tone: value and a warm/cool shift
            public float2 Lean;    // drift of the wall head, metres (x, z)
            public float Warp;     // amplitude of the low-frequency settling field, metres

            public static Variation Draw(in ArchSpec spec, ref Pcg32 rng)
            {
                ArchMix mix = spec.Mix;
                var v = default(Variation);
                float share = mix.Weight + 0.18f * Gauss(ref rng);
                v.Mix = mix.Weight > 0f ? math.saturate(share) : 0f;
                v.Age = math.saturate(mix.AgeMean + 0.15f * Gauss(ref rng));
                v.Damage = math.saturate(mix.DamageMean + 0.12f * Gauss(ref rng));
                // Limewash and thatch are remade a house at a time: neighbours never match, and old ones are duller.
                float value = math.clamp(1f + 0.05f * Gauss(ref rng) - 0.12f * (v.Age - 0.4f), 0.8f, 1.08f);
                float warm = math.clamp(0.025f * Gauss(ref rng), -0.06f, 0.06f);
                v.Tint = new float3(value * (1f + warm), value, value * (1f - warm));
                // A cast shell neither sags nor leans; everything hand-built does, more with every decade.
                bool cast = spec.Era == ArchEra.Future;
                float lean = cast ? 0f : v.Age * v.Age * 0.14f;
                v.Lean = math.clamp(new float2(Gauss(ref rng), Gauss(ref rng)) * lean, -0.14f, 0.14f);
                v.Warp = cast ? 0f : 0.015f + 0.04f * v.Age;
                return v;
            }
        }

        // The building's style: primary and secondary blended at this building's share, then its own proportions.
        private static Style Shape(in ArchSpec spec, in Variation vary, ref Pcg32 rng)
        {
            ArchMix mix = spec.Mix;
            Style st = StyleFor(spec.Region, spec.Era);
            if (vary.Mix > 0f)
            {
                st = Blend(st, StyleFor(mix.SecondaryRegion, mix.SecondaryEra), vary.Mix, ref rng);
            }

            st.Roof.Pitch = math.clamp(st.Roof.Pitch * mix.PitchScale * (1f + 0.12f * Gauss(ref rng)), 0.3f, 1.9f);
            st.Roof.OverhangM = math.clamp(st.Roof.OverhangM * (1f + 0.15f * Gauss(ref rng)), 0.15f, 0.95f);
            // Rafters creep: only roofs that were framed in timber to begin with (Sag > 0) keep sagging.
            st.Roof.Sag = st.Roof.Sag > 0f ? st.Roof.Sag * (0.6f + 1.4f * vary.Age) : 0f;
            st.PlinthM = math.max(0f, (st.PlinthM + mix.PlinthAddM) * (1f + 0.2f * Gauss(ref rng)));
            st.WindowH *= 1f + 0.07f * Gauss(ref rng);
            // A clipped gable (jerkinhead) now and then: the cheapest way a carpenter varies a row of gables.
            bool clipped = rng.NextFloat() < 0.2f;
            if (st.Roof.Hip == 0f && clipped && !st.Flat)
            {
                st.Roof.Hip = 0.3f;
            }

            return st;
        }

        private static bool Pick(bool primary, bool secondary, float share, ref Pcg32 rng) =>
            rng.NextFloat() < share ? secondary : primary;

        // Proportions interpolate; features are taken whole from one side or the other, each on its own coin, so two
        // hybrids of the same pair still differ in which traits they borrowed.
        private static Style Blend(Style a, Style b, float m, ref Pcg32 rng)
        {
            Style s = a;
            s.Roof.Pitch = math.lerp(a.Roof.Pitch, b.Roof.Pitch, m);
            s.Roof.Curve = math.lerp(a.Roof.Curve, b.Roof.Curve, m);
            s.Roof.Hip = math.lerp(a.Roof.Hip, b.Roof.Hip, m);
            s.Roof.OverhangM = math.lerp(a.Roof.OverhangM, b.Roof.OverhangM, m);
            s.Roof.Sag = math.lerp(a.Roof.Sag, b.Roof.Sag, m);
            s.Roof.Upturn = math.lerp(a.Roof.Upturn, b.Roof.Upturn, m);
            s.Roof.ThicknessM = math.lerp(a.Roof.ThicknessM, b.Roof.ThicknessM, m);
            s.Taper = math.lerp(a.Taper, b.Taper, m);
            s.PlinthM = math.lerp(a.PlinthM, b.PlinthM, m);
            s.DormerMean = math.lerp(a.DormerMean, b.DormerMean, m);
            bool glazing = Pick(false, true, m, ref rng);
            if (a.WindowW > 0f && b.WindowW > 0f)
            {
                s.WindowW = math.lerp(a.WindowW, b.WindowW, m);
                s.WindowH = math.lerp(a.WindowH, b.WindowH, m);
            }
            else if (glazing)
            {
                s.WindowW = b.WindowW;
                s.WindowH = b.WindowH;
            }

            s.JettyM = Pick(false, true, m, ref rng) ? b.JettyM : a.JettyM;
            s.Flat = Pick(a.Flat, b.Flat, m, ref rng);
            s.Timber = Pick(a.Timber, b.Timber, m, ref rng);
            s.Belt = Pick(a.Belt, b.Belt, m, ref rng);
            s.Cornice = Pick(a.Cornice, b.Cornice, m, ref rng);
            s.Chimney = Pick(a.Chimney, b.Chimney, m, ref rng);
            s.Stack = Pick(a.Stack, b.Stack, m, ref rng);
            s.Shutters = Pick(a.Shutters, b.Shutters, m, ref rng);
            s.RoundTower = Pick(a.RoundTower, b.RoundTower, m, ref rng);
            s.Battlement = Pick(a.Battlement, b.Battlement, m, ref rng);
            s.Rod = Pick(a.Rod, b.Rod, m, ref rng);
            s.Pipes = Pick(a.Pipes, b.Pipes, m, ref rng);
            s.GlowBands = Pick(a.GlowBands, b.GlowBands, m, ref rng);
            s.Scrap = Pick(a.Scrap, b.Scrap, m, ref rng);
            s.Antenna = Pick(a.Antenna, b.Antenna, m, ref rng);
            s.Strip = Pick(a.Strip, b.Strip, m, ref rng);
            s.WindowBox = Pick(a.WindowBox, b.WindowBox, m, ref rng);
            if (s.JettyM > 0f)
            {
                // Jetty and taper never combine (see BuildLodge.HalfAt): the overhanging storey must stay near the lot.
                s.Taper = 1f;
            }

            return s;
        }

        // Moves every vertex sideways (never vertically: the base depth and wall height are contracts) by a field
        // slow enough that a window and the wall behind it move together, then leans the whole building.
        private static void Settle(Geo[] g, in ArchSpec spec, in Variation vary, float3 offset)
        {
            if (vary.Warp <= 0f && math.lengthsq(vary.Lean) <= 0f)
            {
                return;
            }

            float fnd = spec.FoundationM, height = spec.Storeys * StoreyM;
            foreach (Geo geo in g)
            {
                List<float3> points = geo.Part.P;
                for (int i = 0; i < points.Count; i++)
                {
                    float3 p = points[i];
                    float rise = math.clamp((p.y - fnd) / height, 0f, 1.5f);
                    p.x += noise.snoise(p * 0.15f + offset) * vary.Warp + vary.Lean.x * rise;
                    p.z += noise.snoise(p * 0.15f + offset.zxy) * vary.Warp + vary.Lean.y * rise;
                    points[i] = p;
                }
            }
        }

        // ---- styles --------------------------------------------------------------------------------------------

        private struct RoofForm
        {
            public float Pitch;      // rise per metre of half-span
            public float Curve;      // > 1 sweeps the eaves outward (concave), < 1 bulges (thatch, dome)
            public float Hip;        // 0 = gable ends, 1 = the ridge shortens by a full half-span at each end
            public float OverhangM;
            public float Sag;        // ridge dip at mid-length, metres
            public float Upturn;     // eave-corner lift, metres
            public float ThicknessM;
        }

        private struct Style
        {
            public RoofForm Roof;
            public bool Flat;        // a roof deck behind a parapet instead of Roof
            public float Taper;      // wall width at the top relative to the foot (battered walls)
            public float JettyM;     // how far upper storeys step out
            public float PlinthM;    // stone footing above the entrance floor
            public bool Timber;
            public bool Belt;        // a string course at every floor line
            public bool Cornice;
            public bool Chimney;
            public bool Stack;       // the chimney is a tall factory-style flue
            public bool Shutters;
            public bool RoundTower;
            public bool Battlement;  // towers end in merlons and a beacon instead of a roof
            public float WindowW;    // 0 = unglazed
            public float WindowH;
            public float DormerMean; // mean dormers per roof slope
            public bool Rod;         // towers end in a tall rod with a charged tip (lightning rod, antenna)
            public bool Pipes;       // pipes up the side walls, ember-lit stack
            public bool GlowBands;   // a light band at every floor line
            public bool Scrap;       // walls patched with plates, a sheet roof propped over the deck
            public bool Antenna;     // a mast on the roof
            public bool Strip;       // strip glazing: no mullions, no shutters, always lit
            public bool WindowBox;   // planted boxes under some windows
        }

        private static Style StyleFor(ArchRegion region, ArchEra era)
        {
            var s = new Style { Taper = 1f, WindowW = 0.7f, WindowH = 0.9f };
            switch (region)
            {
                case ArchRegion.Verdant:
                    s.Roof = new RoofForm { Pitch = 0.95f, Curve = 1.15f, OverhangM = 0.55f, Sag = 0.14f, Upturn = 0.06f };
                    s.Timber = true;
                    break;
                case ArchRegion.Lava:
                    s.Roof = new RoofForm { Pitch = 0.5f, Curve = 1f, Hip = 1f, OverhangM = 0.3f };
                    s.Flat = true;
                    s.Taper = 0.9f;
                    s.Battlement = true;
                    break;
                case ArchRegion.Ice:
                    s.Roof = new RoofForm { Pitch = 1.45f, Curve = 1f, OverhangM = 0.45f, Sag = 0.06f };
                    s.Timber = true;
                    s.Chimney = true;
                    break;
                case ArchRegion.Storm:
                    s.Roof = new RoofForm { Pitch = 0.6f, Curve = 1f, Hip = 1f, OverhangM = 0.7f };
                    s.RoundTower = true;
                    s.Taper = 0.95f;
                    s.Rod = true; // storm towers are built to be struck
                    break;
                case ArchRegion.Crystal:
                    s.Roof = new RoofForm { Pitch = 0.8f, Curve = 0.65f, Hip = 1f, OverhangM = 0.3f };
                    s.RoundTower = true;
                    s.Taper = 0.92f;
                    break;
                default:
                    s.Roof = new RoofForm { Pitch = 0.85f, Curve = 1.7f, Hip = 0.6f, OverhangM = 0.8f, Sag = 0.1f, Upturn = 0.35f };
                    s.Timber = true;
                    break;
            }

            s.Roof.ThicknessM = 0.16f;
            switch (era)
            {
                case ArchEra.Primal:
                    s.Roof.Curve *= 0.8f;
                    s.Roof.OverhangM += 0.15f;
                    s.Roof.ThicknessM = 0.32f;
                    s.Timber = false;
                    s.Chimney = false;
                    s.WindowW = 0f;
                    break;
                case ArchEra.Agrarian:
                    s.PlinthM = 0.45f;
                    s.Belt = s.Timber;
                    s.Shutters = s.Timber;
                    s.Chimney |= region == ArchRegion.Verdant;
                    s.DormerMean = 0.25f;
                    s.WindowBox = true;
                    break;
                case ArchEra.Mercantile:
                    s.PlinthM = 0.8f;
                    s.JettyM = s.Timber ? 0.28f : 0f;
                    s.Belt = true;
                    s.Shutters = s.Timber;
                    s.Chimney = !s.Flat;
                    s.DormerMean = 0.9f;
                    s.WindowBox = true;
                    break;
                case ArchEra.Industrial:
                    s.Timber = false;
                    s.Taper = 1f;
                    s.PlinthM = 1f;
                    s.Belt = true;
                    s.Cornice = true;
                    s.Roof.Pitch *= 0.7f;
                    s.Roof.Sag = 0f;
                    s.WindowW = 0.8f;
                    s.WindowH = 1.5f;
                    s.Chimney = !s.Flat;
                    s.Stack = true;
                    s.DormerMean = 1.1f;
                    break;
                case ArchEra.Steampunk:
                    // Every region's roof swells into a mansard: the attic is where the boiler's heat is put to use.
                    s.Timber = false;
                    s.Flat = false;
                    s.Battlement = false;
                    s.Taper = 1f;
                    s.PlinthM = 0.9f;
                    s.Belt = true;
                    s.Cornice = false;
                    s.Roof.Pitch = math.max(s.Roof.Pitch, 1.1f);
                    s.Roof.Curve = 0.6f;
                    s.Roof.Hip = math.max(s.Roof.Hip, 0.6f);
                    s.Roof.Sag = 0f;
                    s.Roof.OverhangM = math.min(s.Roof.OverhangM, 0.45f);
                    s.WindowW = 0.75f;
                    s.WindowH = 1.35f;
                    s.Chimney = true;
                    s.Stack = true;
                    s.Pipes = true;
                    s.DormerMean = 1.4f;
                    break;
                case ArchEra.Future:
                    // A low shell with rounded shoulders; the region survives in the taper, the tower plan and the palette.
                    s.Timber = false;
                    s.Flat = false;
                    s.Battlement = false;
                    s.Chimney = false;
                    s.Taper = math.min(s.Taper, 0.94f);
                    s.PlinthM = 0.3f;
                    s.Roof = new RoofForm { Pitch = 0.38f, Curve = 0.55f, Hip = 1f, OverhangM = 0.22f, ThicknessM = 0.1f };
                    s.WindowW = 1.5f;
                    s.WindowH = 0.55f;
                    s.Strip = true;
                    s.GlowBands = true;
                    s.Antenna = true;
                    s.RoundTower = true;
                    s.Rod = true;
                    break;
                case ArchEra.Wasteland:
                    // Whatever stood here before, roofed with what could be dragged home.
                    s.Timber = false;
                    s.Flat = true;
                    s.Battlement = true;
                    s.Chimney = false;
                    s.Taper = 1f;
                    s.PlinthM = 0.2f;
                    s.WindowW = 0.6f;
                    s.WindowH = 0.7f;
                    s.Scrap = true;
                    s.Antenna = true;
                    break;
            }

            return s;
        }

        // ---- houses and halls ----------------------------------------------------------------------------------

        private static readonly float3[] FaceNormal = { new float3(0, 0, 1), new float3(0, 0, -1), new float3(1, 0, 0), new float3(-1, 0, 0) };
        private static readonly float3[] FaceRight = { new float3(1, 0, 0), new float3(-1, 0, 0), new float3(0, 0, -1), new float3(0, 0, 1) };

        private static void BuildLodge(Geo[] g, ArchSpec spec, Style st, Variation vary, ref Pcg32 rng)
        {
            bool hall = spec.Kind == BuildingKind.Hall;
            float hx = spec.WidthM * 0.5f, hz = spec.DepthM * 0.5f, fnd = spec.FoundationM;
            int storeys = spec.Storeys;
            float wallH = storeys * StoreyM, top = fnd + wallH;

            // Upper storeys step out by the jetty; the whole wall leans in by the taper. Jetty and taper never combine in
            // one style, so the upper storey of a jettied house still stays within 0.3 m of the footprint.
            float2 HalfAt(float y)
            {
                float t = math.saturate((y - fnd) / wallH);
                int storey = math.clamp((int)((y - fnd) / StoreyM), 0, storeys - 1);
                float scale = math.lerp(1f, st.Taper, t);
                float jetty = storey > 0 ? st.JettyM : 0f;
                return new float2(hx * scale + jetty, hz * scale + jetty);
            }

            Box(g[WallSubMesh], new float3(0f, -0.3f, 0f), hx + 0.12f, hz + 0.12f, fnd + 0.3f + st.PlinthM, 0.58f, 0.8f, 0.98f);
            for (int i = 0; i < storeys; i++)
            {
                float y0 = fnd + i * StoreyM, y1 = y0 + StoreyM;
                float2 lo = HalfAt(y0 + 0.01f), hi = HalfAt(y1 - 0.01f);
                // The ground storey starts below the origin when there is no footing to hide its foot.
                float foot = i == 0 ? -0.3f : y0;
                Hexa(g[WallSubMesh], Corners(lo.x, lo.y, foot, hi.x, hi.y, y1), 0.86f + 0.1f * i / storeys, 0.92f + 0.1f * (i + 1) / storeys,
                    i == storeys - 1, i > 0 && st.JettyM > 0f);
                if (st.Belt && i > 0)
                {
                    Box(g[TrimSubMesh], new float3(0f, y0 - 0.14f, 0f), lo.x + 0.07f, lo.y + 0.07f, 0.26f, 0.85f, 1f);
                }

                if (st.Timber)
                {
                    Frame(g[TrimSubMesh], lo, y0, ref rng);
                }
            }

            // A bulging roof starts below the wall head: a cornice there would poke through its skirt.
            if (st.Cornice && (st.Flat || st.Roof.Curve >= 0.95f))
            {
                float2 h = HalfAt(top - 0.01f);
                Box(g[TrimSubMesh], new float3(0f, top - 0.3f, 0f), h.x + 0.14f, h.y + 0.14f, 0.3f, 0.85f, 1.02f);
            }

            if (st.GlowBands)
            {
                // One line of light per floor: at night a shell town reads as stacked rings, not as lit windows.
                for (int i = 1; i <= storeys; i++)
                {
                    float y = i == storeys ? top - 0.22f : fnd + i * StoreyM;
                    float2 h = HalfAt(y - 0.05f);
                    Box(g[GlowSubMesh], new float3(0f, y - 0.06f, 0f), h.x + 0.035f, h.y + 0.035f, 0.09f, 1f, 1.05f);
                }
            }

            float doorW = hall ? 1.8f : 1f, doorH = hall ? 2.4f : 2f;
            float doorT = hall ? 0f : (rng.NextFloat() < 0.5f ? -1f : 1f) * rng.NextFloat() * math.max(0f, hx - 1.3f);
            float frontDist = HalfAt(fnd + 1f).y;
            Door(g, FaceNormal[0], FaceRight[0], frontDist, doorT, fnd, doorW, doorH);

            // Strip glazing is lit from within as one piece; elsewhere a wrecked house is a dark one.
            float litShare = st.Strip ? 0.95f : 0.7f * (1f - 0.8f * vary.Damage);
            if (st.WindowW > 0f)
            {
                for (int i = 0; i < storeys; i++)
                {
                    float sill = fnd + i * StoreyM + (StoreyM - st.WindowH) * 0.5f + 0.1f;
                    float2 half = HalfAt(sill + st.WindowH * 0.5f);
                    for (int f = 0; f < 4; f++)
                    {
                        float len = (f < 2 ? half.x : half.y) * 2f, dist = f < 2 ? half.y : half.x;
                        int count = math.max(0, (int)((len - 1.1f) / 1.7f));
                        for (int k = 0; k < count; k++)
                        {
                            float t = -len * 0.5f + len / count * (k + 0.5f);
                            bool lit = rng.NextFloat() < litShare;
                            bool bricked = !st.Cornice && rng.NextFloat() < 0.15f; // a hand-built street is not a grid
                            bool boarded = rng.NextFloat() < vary.Damage * 0.7f;
                            bool planted = st.WindowBox && rng.NextFloat() < 0.45f * (1f - vary.Age);
                            bool unhinged = rng.NextFloat() < vary.Age * 0.3f; // shutters are the first thing to go
                            bool atDoor = i == 0 && f == 0 && math.abs(t - doorT) < doorW * 0.5f + st.WindowW * 0.5f + 0.45f;
                            if (bricked || atDoor)
                            {
                                continue;
                            }

                            Window(g, FaceNormal[f], FaceRight[f], dist, t, sill, st.WindowW, st.WindowH, !st.Strip,
                                st.Shutters && !st.Strip && !unhinged, lit && !boarded);
                            if (boarded)
                            {
                                Boards(g[TrimSubMesh], FaceNormal[f], FaceRight[f], dist, t, sill, st.WindowW, st.WindowH);
                            }
                            else if (planted)
                            {
                                WindowBox(g, FaceNormal[f], FaceRight[f], dist, t, sill, st.WindowW);
                            }
                        }
                    }
                }
            }
            else
            {
                // Unglazed: one dark smoke opening on each side wall.
                float2 half = HalfAt(fnd + 1.8f);
                Panel(g[TrimSubMesh], FaceNormal[2], FaceRight[2], half.x, 0f, fnd + 1.6f, 0.45f, 0.45f, -0.02f, 0.03f, 0.3f, 0.35f);
                Panel(g[TrimSubMesh], FaceNormal[3], FaceRight[3], half.x, 0f, fnd + 1.6f, 0.45f, 0.45f, -0.02f, 0.03f, 0.3f, 0.35f);
            }

            float2 roofHalf = HalfAt(top - 0.01f);
            bool alongX = roofHalf.x >= roofHalf.y;
            // On a near-square lot either ridge is as cheap to frame: let the builder choose, so a row of equal lots
            // does not show the street a row of equal gables.
            bool turned = rng.NextFloat() < 0.5f;
            if (math.abs(roofHalf.x - roofHalf.y) < 0.15f * math.max(roofHalf.x, roofHalf.y) && turned)
            {
                alongX = !alongX;
            }

            float halfSpan = alongX ? roofHalf.y : roofHalf.x, halfLen = alongX ? roofHalf.x : roofHalf.y;
            float ridgeY;
            if (st.Flat)
            {
                FlatRoof(g, top, roofHalf, hall, st.Scrap, vary.Damage, ref rng);
                ridgeY = top + (st.Scrap ? 1.15f : 0.55f);
            }
            else
            {
                ridgeY = Roof(g[RoofSubMesh], g[WallSubMesh], new float3(0f, top + RoofSeatM, 0f), halfSpan, halfLen, alongX, st.Roof,
                    0.35f, vary.Damage, rng.NextFloat() * 50f);
                Dormers(g, top + RoofSeatM, halfSpan, halfLen, alongX, st.Roof, st.DormerMean, litShare, ref rng);
                if (st.Roof.Hip == 0f && (spec.Era == ArchEra.Primal || spec.Era == ArchEra.Agrarian || hall))
                {
                    // Crossed barge boards over each gable: the one ornament a thatcher's ridge always had.
                    for (int e = -1; e <= 1; e += 2)
                    {
                        float v = e * (halfLen + 0.3f);
                        Beam(g[TrimSubMesh], Along(alongX, -0.3f, ridgeY - 0.35f, v), Along(alongX, 0.5f, ridgeY + 0.75f, v), 0.12f, 0.08f,
                            Along(alongX, 0f, 0f, 1f), 0.85f, 1.05f);
                        Beam(g[TrimSubMesh], Along(alongX, 0.3f, ridgeY - 0.35f, v), Along(alongX, -0.5f, ridgeY + 0.75f, v), 0.12f, 0.08f,
                            Along(alongX, 0f, 0f, 1f), 0.85f, 1.05f);
                    }
                }
            }

            // A second flue on the long houses, where one hearth cannot warm both ends.
            bool second = rng.NextFloat() < 0.3f;
            int chimneys = st.Chimney ? (second && halfLen > 2.6f ? 2 : 1) : 0;
            for (int n = 0; n < chimneys; n++)
            {
                float u = halfSpan * 0.45f * (rng.NextFloat() < 0.5f ? -1f : 1f);
                float v = chimneys == 1
                    ? (rng.NextFloat() * 2f - 1f) * halfLen * 0.55f
                    : (n == 0 ? -1f : 1f) * halfLen * (0.35f + 0.3f * rng.NextFloat());
                bool broken = rng.NextFloat() < (vary.Damage - 0.35f) * 1.5f;
                float3 foot = Along(alongX, u, top - 0.2f, v);
                float half = st.Stack ? 0.26f : 0.34f;
                float height = ridgeY - top + (st.Stack ? 1.9f : 0.75f) * (1f + 0.15f * Gauss(ref rng));
                if (broken)
                {
                    // Snapped a little above the roof line: no cap, no smoke.
                    Box(g[WallSubMesh], foot, half, half, (ridgeY - top) * 0.6f + 0.35f, 0.78f, 0.9f, 0.94f);
                    continue;
                }

                Box(g[WallSubMesh], foot, half, half, height, 0.78f, 0.96f, 0.88f);
                Box(g[TrimSubMesh], foot + new float3(0f, height, 0f), half * 0.88f + 0.07f, half * 0.88f + 0.07f, 0.12f, 0.8f, 0.7f);
                if (st.Pipes)
                {
                    Box(g[GlowSubMesh], foot + new float3(0f, height + 0.12f, 0f), half * 0.6f, half * 0.6f, 0.1f, 1f, 1.1f);
                }
                else if (st.Belt && !st.Stack)
                {
                    // Clay pots: one per hearth below.
                    Box(g[RoofSubMesh], foot + new float3(0f, height + 0.12f, 0f), 0.11f, 0.11f, 0.3f, 0.85f, 1f, 0.8f);
                }
            }

            float2 widest = math.max(HalfAt(fnd + 0.1f), HalfAt(top - 0.01f));
            if (st.Pipes)
            {
                Pipe(g, 2 + (int)rng.NextUInt(2), widest.x, (rng.NextFloat() - 0.5f) * widest.y * 1.2f, fnd, wallH, storeys);
            }

            if (st.Scrap)
            {
                ScrapPlates(g, widest, fnd, wallH, doorT, doorW, ref rng);
            }

            if (st.Antenna)
            {
                // Lit at the tip: the one thing a shell roof or a sheet roof offers the night skyline.
                float3 foot = Along(alongX, 0f, ridgeY - 0.3f, (rng.NextFloat() - 0.5f) * halfLen);
                float3 tip = foot + new float3(st.Scrap ? 0.25f * Gauss(ref rng) : 0f, 2f + 0.5f * rng.NextFloat(), 0f);
                float3 yard = math.lerp(foot, tip, 0.7f);
                Beam(g[TrimSubMesh], foot, tip, 0.07f, 0.07f, new float3(0f, 0f, 1f), 0.8f, 1.05f);
                Beam(g[TrimSubMesh], yard - new float3(0.35f, 0f, 0f), yard + new float3(0.35f, 0f, 0f), 0.05f, 0.05f,
                    new float3(0f, 0f, 1f), 0.9f, 0.9f);
                Box(g[GlowSubMesh], tip, 0.07f, 0.07f, 0.14f, 1f, 1.1f);
            }

            for (int e = -1; e <= 1; e += 2)
            {
                // Raking shores: what a street does for a wall it cannot afford to rebuild.
                float z = (rng.NextFloat() - 0.5f) * hz;
                bool shored = rng.NextFloat() < (vary.Damage - 0.5f) * 1.6f;
                if (shored)
                {
                    Beam(g[TrimSubMesh], new float3(e * (hx + 0.85f), fnd - 0.2f, z), new float3(e * (HalfAt(fnd + 2.3f).x + 0.02f), fnd + 2.3f, z),
                        0.16f, 0.16f, new float3(0f, 0f, 1f), 0.7f, 0.95f);
                }
            }

            if (hall)
            {
                Porch(g, st, frontDist, fnd, doorW);
                for (int e = -1; e <= 1; e += 2)
                {
                    // Banners in the roof colour flank the door: a hall is the one building a newcomer must find.
                    float t = e * (hx - 0.75f);
                    float y = fnd + StoreyM + 0.4f;
                    float dist = HalfAt(y).y;
                    Panel(g[RoofSubMesh], FaceNormal[0], FaceRight[0], dist, t, y, 0.62f, 1.9f, 0.08f, 0.12f, 0.8f, 1.05f);
                    Panel(g[TrimSubMesh], FaceNormal[0], FaceRight[0], dist, t, y + 1.9f, 0.86f, 0.09f, 0.02f, 0.2f, 0.9f, 1f);
                }
            }
        }

        // Corner posts, a mid rail and one brace per corner on the front and back: enough to read as a timber frame
        // from across a valley without drawing every stud.
        private static void Frame(Geo trim, float2 half, float y0, ref Pcg32 rng)
        {
            for (int c = 0; c < 4; c++)
            {
                float sx = (c & 1) == 0 ? 1f : -1f, sz = (c & 2) == 0 ? 1f : -1f;
                Box(trim, new float3(sx * (half.x - 0.07f), y0, sz * (half.y - 0.07f)), 0.12f, 0.12f, StoreyM, 0.8f, 1f);
            }

            for (int f = 0; f < 2; f++)
            {
                float3 n = FaceNormal[f], r = FaceRight[f];
                Panel(trim, n, r, half.y, 0f, y0 + StoreyM * 0.5f - 0.08f, half.x * 2f, 0.16f, -0.02f, 0.05f, 0.9f, 0.95f);
                for (int e = -1; e <= 1; e += 2)
                {
                    float reach = 0.9f + rng.NextFloat() * 0.5f;
                    float3 from = n * (half.y + 0.01f) + r * (e * (half.x - 0.2f)) + new float3(0f, y0 + 0.15f, 0f);
                    float3 to = n * (half.y + 0.01f) + r * (e * (half.x - 0.2f - reach)) + new float3(0f, y0 + StoreyM * 0.5f - 0.1f, 0f);
                    Beam(trim, from, to, 0.15f, 0.1f, n, 0.85f, 0.95f);
                }
            }
        }

        private static void FlatRoof(Geo[] g, float top, float2 half, bool hall, bool scrap, float damage, ref Pcg32 rng)
        {
            Box(g[RoofSubMesh], new float3(0f, top - 0.12f, 0f), half.x - 0.05f, half.y - 0.05f, 0.14f, 0.8f, 0.95f);
            for (int f = 0; f < 4; f++)
            {
                float len = (f < 2 ? half.x : half.y) * 2f, dist = f < 2 ? half.y : half.x;
                // Parapet: the wall carried up past the deck, rounded off by hand. Damage bites a stretch out of it.
                float gap = rng.NextFloat(), bite = (rng.NextFloat() - 0.5f) * len * 0.4f;
                if (gap < (damage - 0.3f) * 1.2f && len > 3.4f)
                {
                    float left = bite - 0.6f + len * 0.5f, right = len * 0.5f - bite - 0.6f;
                    Panel(g[WallSubMesh], FaceNormal[f], FaceRight[f], dist - 0.3f, -len * 0.5f + left * 0.5f, top - 0.05f, left, 0.6f, 0f, 0.3f, 0.95f, 1.05f);
                    Panel(g[WallSubMesh], FaceNormal[f], FaceRight[f], dist - 0.3f, len * 0.5f - right * 0.5f, top - 0.05f, right, 0.6f, 0f, 0.3f, 0.95f, 1.05f);
                    Panel(g[WallSubMesh], FaceNormal[f], FaceRight[f], dist - 0.3f, bite, top - 0.05f, 1.2f, 0.2f, 0f, 0.3f, 0.9f, 0.95f);
                }
                else
                {
                    Panel(g[WallSubMesh], FaceNormal[f], FaceRight[f], dist - 0.3f, 0f, top - 0.05f, len, 0.6f, 0f, 0.3f, 0.95f, 1.05f);
                }

                if (f < 2)
                {
                    // Roof-beam ends poking through the front and back walls.
                    int beams = math.max(2, (int)(len / 0.95f));
                    for (int k = 0; k < beams; k++)
                    {
                        float t = -len * 0.5f + len / beams * (k + 0.5f);
                        Panel(g[TrimSubMesh], FaceNormal[f], FaceRight[f], dist, t, top - 0.55f, 0.17f, 0.17f, -0.1f, 0.38f, 0.75f, 0.95f);
                    }
                }
            }

            if (scrap)
            {
                // A salvaged sheet propped over the deck, tipped to shed rain off the back.
                float x = half.x + 0.15f, z = half.y + 0.15f, front = top + 0.7f, back = top + 1.15f;
                Hexa(g[RoofSubMesh], new[]
                {
                    new float3(-x, front - 0.07f, z), new float3(x, front - 0.07f, z), new float3(x, back - 0.07f, -z), new float3(-x, back - 0.07f, -z),
                    new float3(-x, front, z), new float3(x, front, z), new float3(x, back, -z), new float3(-x, back, -z),
                }, 0.8f, 1f, true, true);
                for (int c = 0; c < 4; c++)
                {
                    float sx = (c & 1) == 0 ? 1f : -1f, sz = (c & 2) == 0 ? 1f : -1f;
                    Box(g[TrimSubMesh], new float3(sx * (half.x - 0.25f), top, sz * (half.y - 0.25f)), 0.06f, 0.06f,
                        (sz > 0f ? 0.7f : 1.15f) - 0.1f, 0.75f, 0.95f);
                }

                return;
            }

            if (hall)
            {
                // A low dome over the hall's centre keeps it the tallest thing in a flat-roofed street.
                var profile = new List<float2>();
                float radius = math.min(half.x, half.y) * 0.55f;
                for (int i = 0; i <= 5; i++)
                {
                    float a = i / 5f * math.PI * 0.5f;
                    profile.Add(new float2(radius * math.cos(a), top + 0.02f + radius * 0.8f * math.sin(a)));
                }

                Lathe(g[RoofSubMesh], float3.zero, profile.ToArray(), 12, 0f, i => 0.85f + 0.04f * i, null, null);
            }
        }

        private static void Porch(Geo[] g, Style st, float frontDist, float fnd, float doorW)
        {
            float half = doorW * 0.5f + 0.75f, depth = 1.5f, eave = fnd + 2.75f;
            for (int e = -1; e <= 1; e += 2)
            {
                Box(g[TrimSubMesh], new float3(e * (half - 0.15f), -0.3f, frontDist + depth - 0.15f), 0.11f, 0.11f, eave + 0.3f, 0.75f, 1f);
            }

            Box(g[WallSubMesh], new float3(0f, -0.3f, frontDist + depth * 0.5f), half + 0.1f, depth * 0.5f + 0.1f, fnd + 0.3f, 0.6f, 0.82f);
            if (st.Flat)
            {
                Box(g[RoofSubMesh], new float3(0f, eave, frontDist + depth * 0.5f), half + 0.15f, depth * 0.5f + 0.1f, 0.16f, 0.82f, 1f);
                return;
            }

            RoofForm form = st.Roof;
            form.Hip = 0f;
            form.OverhangM = 0.25f;
            form.Sag = 0f;
            form.Upturn *= 0.5f;
            form.Pitch = math.min(form.Pitch, 0.9f);
            // Ridge runs out from the wall, so the porch shows the street a small timber pediment.
            Roof(g[RoofSubMesh], g[TrimSubMesh], new float3(0f, eave, frontDist + depth * 0.5f - 0.1f), half, depth * 0.5f + 0.1f, false, form, 0.2f);
        }

        // Small gabled windows standing on each roof slope; their count is drawn per slope, so the two sides differ.
        private static void Dormers(Geo[] g, float top, float halfSpan, float halfLen, bool alongX, RoofForm f, float mean, float lit,
            ref Pcg32 rng)
        {
            float g0 = Gauss(ref rng), g1 = Gauss(ref rng);
            const float h = 0.85f, halfW = 0.42f;
            float a = halfSpan + f.OverhangM, span = a * f.Pitch;
            float eave = top - span * math.pow(f.OverhangM / a, f.Curve);
            // The face stands back from the eave, over the room below rather than over the street.
            float outer = halfSpan * 0.72f;
            float yOuter = eave + span * math.pow(1f - outer / a, f.Curve);
            var form = new RoofForm { Pitch = math.clamp(f.Pitch, 0.7f, 1.1f), Curve = 1f, OverhangM = 0.14f, ThicknessM = 0.1f };
            float crest = yOuter + h + (halfW + form.OverhangM) * form.Pitch;
            if (mean <= 0f || halfSpan < 1.6f || eave + span - crest < 0.25f)
            {
                return; // no room under the ridge: a dormer taller than its roof reads as a second house
            }

            // Runs back until its ridge meets the slope; the buried part is hidden inside the roof.
            float inner = math.max(0.12f, outer - (h + 0.4f) / f.Pitch);
            float b = halfLen + (f.Hip > 0f ? f.OverhangM : 0.35f);
            float usable = math.min(halfLen, b - f.Hip * math.min(a, b * 0.98f) * (1f - (inner + outer) * 0.5f / a)) - 0.9f;
            if (usable < 0f)
            {
                return;
            }

            int most = math.min(3, 1 + (int)(2f * usable / 1.7f));
            for (int side = 0; side < 2; side++)
            {
                float sgn = side == 0 ? 1f : -1f;
                int count = math.clamp((int)math.round(mean + 0.7f * (side == 0 ? g0 : g1)), 0, most);
                for (int i = 0; i < count; i++)
                {
                    float v = count == 1 ? 0f : math.lerp(-usable, usable, i / (count - 1f));
                    bool glows = rng.NextFloat() < lit;
                    var c = new float3[8];
                    for (int k = 0; k < 8; k++)
                    {
                        int q = k & 3;
                        c[k] = Along(alongX, sgn * (q == 0 || q == 3 ? inner : outer), k < 4 ? yOuter - 0.1f : yOuter + h,
                            v + (q < 2 ? -halfW : halfW));
                    }

                    Hexa(g[WallSubMesh], c, 0.92f, 1.02f, false, false);
                    Window(g, Along(alongX, sgn, 0f, 0f), Along(alongX, 0f, 0f, 1f), outer, v, yOuter + 0.2f, 0.46f, 0.5f, true, false, glows);
                    Roof(g[RoofSubMesh], g[WallSubMesh], Along(alongX, sgn * (inner + outer) * 0.5f, yOuter + h, v), halfW,
                        (outer - inner) * 0.5f, !alongX, form, 0.12f);
                }
            }
        }

        // A riser up one wall into the attic, banded at every floor, with a lit gauge at eye level.
        private static void Pipe(Geo[] g, int face, float dist, float t, float fnd, float wallH, int storeys)
        {
            float3 n = FaceNormal[face], r = FaceRight[face];
            float run = wallH - 0.5f;
            Panel(g[TrimSubMesh], n, r, dist, t, fnd - 0.2f, 0.18f, run + 0.2f, 0.06f, 0.24f, 0.8f, 1.05f);
            Panel(g[TrimSubMesh], n, r, dist, t, fnd + run - 0.18f, 0.18f, 0.18f, -0.05f, 0.24f, 0.95f, 1.05f);
            for (int i = 0; i < storeys; i++)
            {
                Panel(g[TrimSubMesh], n, r, dist, t, fnd + i * StoreyM + 0.6f, 0.28f, 0.1f, 0.03f, 0.28f, 1f, 1.08f);
            }

            Panel(g[TrimSubMesh], n, r, dist, t + 0.34f, fnd + 1.42f, 0.3f, 0.3f, 0f, 0.06f, 0.9f, 1f);
            Panel(g[GlowSubMesh], n, r, dist, t + 0.34f, fnd + 1.47f, 0.2f, 0.2f, 0.02f, 0.09f, 1f, 1.1f);
        }

        // Plates over the holes: two metals, no two the same size.
        private static void ScrapPlates(Geo[] g, float2 half, float fnd, float wallH, float doorT, float doorW, ref Pcg32 rng)
        {
            for (int f = 0; f < 4; f++)
            {
                float len = (f < 2 ? half.x : half.y) * 2f, dist = f < 2 ? half.y : half.x;
                int plates = math.max(1, (int)(len * wallH / 7f));
                for (int k = 0; k < plates; k++)
                {
                    float w = math.min(len - 0.3f, 0.7f + 0.7f * rng.NextFloat()), h = 0.6f + 0.6f * rng.NextFloat();
                    float t = (rng.NextFloat() - 0.5f) * math.max(0f, len - w - 0.2f);
                    float y = fnd + 0.3f + rng.NextFloat() * math.max(0f, wallH - h - 0.6f);
                    float proud = 0.03f + 0.04f * rng.NextFloat(), tone = 0.85f + 0.25f * rng.NextFloat();
                    int sub = rng.NextFloat() < 0.5f ? TrimSubMesh : RoofSubMesh;
                    if (f == 0 && math.abs(t - doorT) < (doorW + w) * 0.5f + 0.2f)
                    {
                        continue;
                    }

                    Panel(g[sub], FaceNormal[f], FaceRight[f], dist, t, y, w, h, 0f, proud, tone * 0.92f, tone);
                }
            }
        }

        private static void Boards(Geo trim, float3 n, float3 r, float dist, float t, float y, float w, float h)
        {
            Panel(trim, n, r, dist, t, y + h * 0.3f, w + 0.34f, 0.13f, 0.1f, 0.15f, 0.7f, 0.8f);
            float3 at = n * (dist + 0.17f) + r * t;
            Beam(trim, at - r * (w * 0.5f + 0.12f) + new float3(0f, y + 0.08f, 0f), at + r * (w * 0.5f + 0.12f) + new float3(0f, y + h - 0.08f, 0f),
                0.13f, 0.04f, n, 0.65f, 0.8f);
        }

        private static void WindowBox(Geo[] g, float3 n, float3 r, float dist, float t, float y, float w)
        {
            Panel(g[TrimSubMesh], n, r, dist, t, y - 0.42f, w + 0.24f, 0.2f, 0.04f, 0.32f, 0.8f, 0.95f);
            // Accent-free 0: drawn wholly in the weathering colour, which is whatever grows (or settles) on this planet.
            Panel(g[WallSubMesh], n, r, dist, t, y - 0.26f, w + 0.14f, 0.2f, 0.08f, 0.3f, 1f, 1.12f, 1f, 0f);
        }

        // ---- towers --------------------------------------------------------------------------------------------

        private static void BuildTower(Geo[] g, ArchSpec spec, Style st, Variation vary, ref Pcg32 rng)
        {
            float r = math.min(spec.WidthM, spec.DepthM) * 0.5f, fnd = spec.FoundationM;
            float top = fnd + spec.Storeys * StoreyM;
            const float lookout = 2.4f, corbel = 0.45f, flare = 0.3f;
            float shaftTop = top - lookout;
            float neck = r * (st.RoundTower ? 0.82f : 0.86f) - flare; // the lookout flares back out to the footprint
            // "Round" towers are octagons with a face square to each side of the lot: a door or a window is a flat
            // panel, and on a true cylinder its edges would hang off the curve.
            const int sides = 8;
            float turn = -math.PI / sides;
            float flat = st.RoundTower ? math.cos(math.PI / sides) : 1f;
            float shaftFoot = r - (st.RoundTower ? 0.12f : 0.1f);

            float RadiusAt(float y) => flat * (y >= shaftTop + corbel
                ? neck + flare
                : y >= shaftTop
                    ? math.lerp(neck, neck + flare, (y - shaftTop) / corbel)
                    : math.lerp(shaftFoot, neck, math.saturate((y - fnd) / (shaftTop - fnd))));

            if (st.RoundTower)
            {
                var profile = new[]
                {
                    new float2(r, -0.3f), new float2(r, fnd + 0.5f), new float2(r - 0.12f, fnd + 0.62f),
                    new float2(neck, shaftTop), new float2(neck + flare, shaftTop + corbel), new float2(neck + flare, top),
                    new float2(0f, top),
                };
                Lathe(g[WallSubMesh], float3.zero, profile, sides, turn, i => i < 2 ? 0.66f : 0.84f + 0.04f * i, null, null);
                Lathe(g[TrimSubMesh], float3.zero, new[]
                {
                    new float2(neck + flare, shaftTop + corbel - 0.04f), new float2(neck + flare + 0.07f, shaftTop + corbel),
                    new float2(neck + flare + 0.07f, shaftTop + corbel + 0.2f), new float2(neck + flare, shaftTop + corbel + 0.24f),
                }, sides, turn, i => 0.9f, null, null);
            }
            else
            {
                Box(g[WallSubMesh], new float3(0f, -0.3f, 0f), r, r, fnd + 0.3f + 0.55f, 0.6f, 0.8f, 0.97f);
                Hexa(g[WallSubMesh], Corners(r - 0.1f, r - 0.1f, -0.3f, neck, neck, shaftTop), 0.84f, 0.98f, false, false);
                Hexa(g[WallSubMesh], Corners(neck, neck, shaftTop, neck + flare, neck + flare, shaftTop + corbel), 0.8f, 0.9f, false, false);
                Hexa(g[WallSubMesh], Corners(neck + flare, neck + flare, shaftTop + corbel, neck + flare, neck + flare, top), 0.96f, 1.04f, true, false);
                Box(g[TrimSubMesh], new float3(0f, shaftTop + corbel - 0.02f, 0f), neck + flare + 0.07f, neck + flare + 0.07f, 0.22f, 0.85f, 1f);
            }

            Door(g, FaceNormal[0], FaceRight[0], RadiusAt(fnd + 1f), 0f, fnd, st.RoundTower ? 0.8f : 0.95f, 2f);
            int firstFace = (int)rng.NextUInt(4);
            for (int i = 1; i + 1 < spec.Storeys; i++)
            {
                int f = (firstFace + i) & 3;
                float y = fnd + i * StoreyM + 0.9f;
                Window(g, FaceNormal[f], FaceRight[f], RadiusAt(y + 0.5f), 0f, y, 0.28f, 1f, false, false,
                    rng.NextFloat() < 0.75f * (1f - 0.8f * vary.Damage));
                if (st.GlowBands)
                {
                    int ring = st.RoundTower ? sides : 4;
                    // RadiusAt is the distance to a face; the lathe wants the distance to a corner.
                    float band = fnd + i * StoreyM, at = (RadiusAt(band) + 0.03f) / math.cos(math.PI / ring);
                    Lathe(g[GlowSubMesh], float3.zero, new[]
                    {
                        new float2(at - 0.03f, band - 0.01f), new float2(at, band), new float2(at, band + 0.09f), new float2(at - 0.03f, band + 0.1f),
                    }, ring, st.RoundTower ? turn : math.PI * 0.25f, p => 1f, null, null);
                }
            }

            if (st.Pipes)
            {
                // Hoops instead of a riser: a straight pipe would stand off a tapering shaft.
                for (int i = 1; i + 1 < spec.Storeys; i++)
                {
                    int ring = st.RoundTower ? sides : 4;
                    float band = fnd + i * StoreyM + 0.35f, at = (RadiusAt(band) + 0.05f) / math.cos(math.PI / ring);
                    Lathe(g[TrimSubMesh], float3.zero, new[]
                    {
                        new float2(at - 0.05f, band - 0.01f), new float2(at, band), new float2(at, band + 0.14f), new float2(at - 0.05f, band + 0.15f),
                    }, ring, st.RoundTower ? turn : math.PI * 0.25f, p => 0.95f, null, null);
                }
            }

            if (st.Scrap && !st.RoundTower)
            {
                // Square shafts only: a plate wider than an octagon's face would hang off its corners.
                float reach = RadiusAt(fnd + 0.5f);
                ScrapPlates(g, new float2(reach, reach), fnd, shaftTop - fnd, 0f, 1f, ref rng);
            }

            for (int f = 0; f < 4; f++)
            {
                // The lantern room: lit on all four sides, so a tower is a landmark at night from any road.
                Window(g, FaceNormal[f], FaceRight[f], RadiusAt(top - 1f), 0f, top - 1.75f, st.RoundTower ? 0.8f : math.min(1.1f, (neck + flare) * 0.9f), 1.15f, true, false, true);
            }

            float head = neck + flare;
            if (st.Battlement)
            {
                Box(g[RoofSubMesh], new float3(0f, top - 0.06f, 0f), head - 0.3f, head - 0.3f, 0.1f, 0.8f, 0.9f);
                for (int f = 0; f < 4; f++)
                {
                    for (int k = -1; k <= 1; k++)
                    {
                        // A fallen merlon leaves its stump: the crown turns jagged, never flat.
                        bool fallen = rng.NextFloat() < vary.Damage * 0.6f;
                        Panel(g[WallSubMesh], FaceNormal[f], FaceRight[f], head - 0.3f, k * (head - 0.3f) * 0.8f, top - 0.02f,
                            head * 0.42f, fallen ? 0.2f : k == 0 ? 0.45f : 0.85f, 0f, 0.3f, 0.95f, 1.05f);
                    }
                }

                Box(g[TrimSubMesh], new float3(0f, top, 0f), 0.3f, 0.3f, 0.55f, 0.8f, 0.95f, 1.25f);
                Box(g[GlowSubMesh], new float3(0f, top + 0.55f, 0f), 0.26f, 0.26f, 0.38f, 1f, 1.1f, 0.5f);
                return;
            }

            RoofForm form = st.Roof;
            form.Hip = 1f;
            form.Sag = 0f;
            form.Pitch = math.max(form.Pitch * 1.5f, 1.25f);
            form.OverhangM = math.min(form.OverhangM, 0.5f);
            float span = (head + form.OverhangM) * form.Pitch;
            float apex;
            if (st.RoundTower)
            {
                var profile = new List<float2> { new float2(head * 0.5f, top + 0.05f), new float2(head + form.OverhangM, top - 0.2f) };
                float eave = top - 0.08f;
                for (int i = 0; i <= 5; i++)
                {
                    float s = i / 5f;
                    profile.Add(new float2((head + form.OverhangM) * (1f - s), eave + span * math.pow(s, form.Curve)));
                }

                apex = eave + span;
                Lathe(g[RoofSubMesh], float3.zero, profile.ToArray(), sides, turn, i => i < 2 ? 0.55f : 0.8f + 0.05f * i,
                    (i, k) => i < 2 ? 1f : 1f + ((i & 1) == 0 ? 0.035f : -0.035f), null);
            }
            else
            {
                apex = Roof(g[RoofSubMesh], null, new float3(0f, top, 0f), head, head, false, form, 0f);
            }

            float spike = st.Rod ? 2.3f : 0.9f;
            Beam(g[TrimSubMesh], new float3(0f, apex - 0.25f, 0f), new float3(0f, apex + spike, 0f), 0.09f, 0.09f, new float3(0f, 0f, 1f), 0.8f, 1.05f);
            if (st.Rod)
            {
                // A lightning rod (or an aerial) with a charged ball.
                Box(g[GlowSubMesh], new float3(0f, apex + spike - 0.05f, 0f), 0.11f, 0.11f, 0.22f, 1f, 1.1f);
            }
        }

        // ---- tents ---------------------------------------------------------------------------------------------

        private static void BuildTent(Geo[] g, ArchSpec spec, Variation vary, ref Pcg32 rng)
        {
            float r = math.min(spec.WidthM, spec.DepthM) * 0.5f, fnd = spec.FoundationM;
            // A camp of mixed descent pitches both kinds of tent; a shell-era camp lands pods, whatever the planet.
            bool borrowed = rng.NextFloat() < vary.Mix;
            ArchRegion form = spec.Era == ArchEra.Future ? ArchRegion.Ice : borrowed ? spec.Mix.SecondaryRegion : spec.Region;
            if (spec.Era == ArchEra.Future)
            {
                Lathe(g[GlowSubMesh], float3.zero, new[]
                {
                    new float2(r * 0.94f, fnd + 0.3f), new float2(r * 0.97f, fnd + 0.32f), new float2(r * 0.97f, fnd + 0.4f), new float2(r * 0.94f, fnd + 0.42f),
                }, 9, 0f, i => 1f, null, null);
            }

            switch (form)
            {
                case ArchRegion.Ice:
                {
                    // Snow house: courses of cut blocks closing into a dome, entered through a low windbreak.
                    var profile = new List<float2> { new float2(r * 0.94f, -0.3f) };
                    for (int i = 0; i <= 5; i++)
                    {
                        float a = i / 5f * math.PI * 0.5f;
                        profile.Add(new float2(r * 0.94f * math.cos(a), fnd + r * 1.05f * math.sin(a)));
                    }

                    var tones = new float[profile.Count * 9];
                    for (int i = 0; i < tones.Length; i++)
                    {
                        tones[i] = 0.93f + rng.NextFloat() * 0.12f;
                    }

                    Lathe(g[WallSubMesh], float3.zero, profile.ToArray(), 9, rng.NextFloat(), i => 0.8f + 0.04f * i, (i, k) => tones[i * 9 + k], null);
                    Box(g[WallSubMesh], new float3(0f, -0.3f, r * 0.9f), 0.55f, 0.45f, fnd + 0.3f + 0.95f, 0.75f, 0.98f, 0.82f);
                    Panel(g[TrimSubMesh], FaceNormal[0], FaceRight[0], r * 0.9f + 0.4f, 0f, fnd, 0.6f, 0.72f, 0f, 0.05f, 0.3f, 0.35f);
                    break;
                }
                case ArchRegion.Storm:
                case ArchRegion.Crystal:
                {
                    // Round felt tent: low walls, a shallow cone and a smoke ring, nothing for the wind to catch.
                    float wall = fnd + 1.5f;
                    Lathe(g[WallSubMesh], float3.zero, new[] { new float2(r * 0.94f, -0.3f), new float2(r * 0.94f, wall) }, 12, 0f,
                        i => 0.8f + 0.2f * i, null, null);
                    Lathe(g[RoofSubMesh], float3.zero, new[]
                    {
                        new float2(r * 0.94f, wall - 0.12f), new float2(r, wall - 0.1f), new float2(r, wall + 0.04f),
                        new float2(0.32f, wall + 0.85f), new float2(0.32f, wall + 1f), new float2(0f, wall + 1f),
                    }, 12, 0f, i => 0.82f + 0.04f * i, null, null);
                    Lathe(g[TrimSubMesh], float3.zero, new[]
                    {
                        new float2(r * 0.94f, fnd + 0.9f), new float2(r * 0.94f + 0.03f, fnd + 0.92f),
                        new float2(r * 0.94f + 0.03f, fnd + 1.04f), new float2(r * 0.94f, fnd + 1.06f),
                    }, 12, 0f, i => 0.9f, null, null);
                    Door(g, FaceNormal[0], FaceRight[0], r * 0.94f - 0.05f, 0f, fnd, 0.8f, 1.3f);
                    break;
                }
                default:
                {
                    // Hide cone on crossed poles. Eight panels: a face looks straight at the entrance side.
                    const int sides = 8;
                    float turn = math.PI * 0.5f - math.PI / sides;
                    float peak = fnd + TentPeakM;
                    Lathe(g[WallSubMesh], float3.zero, new[] { new float2(r, -0.3f), new float2(r, fnd), new float2(0.14f, peak) }, sides, turn,
                        i => i == 0 ? 0.7f : 0.82f + 0.1f * i, (i, k) => (k & 1) == 0 ? 1f : 0.93f, null);
                    Lathe(g[RoofSubMesh], float3.zero, new[]
                    {
                        new float2(math.lerp(r, 0.14f, 0.34f) + 0.02f, fnd + TentPeakM * 0.34f),
                        new float2(math.lerp(r, 0.14f, 0.46f) + 0.02f, fnd + TentPeakM * 0.46f),
                    }, sides, turn, i => 0.95f, null, null);

                    float3 left = new float3(math.cos(turn) * r, fnd, math.sin(turn) * r);
                    float3 right = new float3(math.cos(turn + math.PI * 2f / sides) * r, fnd, math.sin(turn + math.PI * 2f / sides) * r);
                    float3 mid = (left + right) * 0.5f;
                    float3 tip = math.lerp(mid, new float3(0f, peak, 0f), 0.55f);
                    float3 outward = math.normalize(new float3(0f, r / TentPeakM, 1f));
                    int a = g[TrimSubMesh].V(math.lerp(left, right, 0.26f) + outward * 0.03f, 0.4f);
                    int b = g[TrimSubMesh].V(math.lerp(left, right, 0.74f) + outward * 0.03f, 0.4f);
                    int c = g[TrimSubMesh].V(tip + outward * 0.03f, 0.5f);
                    g[TrimSubMesh].Tri(a, b, c, outward);

                    float spin = rng.NextFloat() * math.PI;
                    for (int k = 0; k < 4; k++)
                    {
                        float angle = spin + k * math.PI * 0.5f;
                        float3 dir = new float3(math.cos(angle), 0f, math.sin(angle));
                        Beam(g[TrimSubMesh], dir * 0.42f + new float3(0f, peak - 0.75f, 0f), dir * -0.4f + new float3(0f, peak + 0.7f, 0f),
                            0.07f, 0.07f, new float3(-dir.z, 0f, dir.x), 0.8f, 1f);
                    }

                    break;
                }
            }
        }

        // ---- megaliths -----------------------------------------------------------------------------------------

        private static void BuildPillar(Geo[] g, ArchSpec spec, ref Pcg32 rng)
        {
            const int sides = 6;
            float r = math.min(spec.WidthM, spec.DepthM) * 0.5f, fnd = spec.FoundationM;
            float height = spec.Storeys * StoreyM, top = fnd + height;
            float turn = rng.NextFloat() * math.PI;
            float leanAngle = rng.NextFloat() * math.PI * 2f;
            // Lean stays inside the footprint: the rule layer's collider is an upright box.
            float3 lean = new float3(math.cos(leanAngle), 0f, math.sin(leanAngle)) * (r * 0.3f);
            float cut = rng.NextFloat() * math.PI * 2f;
            var jitter = new float[8 * sides];
            for (int i = 0; i < jitter.Length; i++)
            {
                jitter[i] = 0.93f + rng.NextFloat() * 0.12f;
            }

            float3 Bend(float3 p)
            {
                float t = math.saturate((p.y - fnd) / height);
                return p + lean * (t * t);
            }

            float RadiusAt(float t) => r * math.lerp(1f, 0.66f, t);

            var profile = new[]
            {
                new float2(r, -0.3f), new float2(r, fnd + 0.2f), new float2(RadiusAt(0.4f), fnd + height * 0.4f),
                new float2(RadiusAt(0.8f), fnd + height * 0.8f), new float2(RadiusAt(1f), top), new float2(0f, top + 0.18f),
            };
            Lathe(g[WallSubMesh], float3.zero, profile, sides, turn, i => i == 0 ? 0.6f : 0.76f + 0.07f * i, (i, k) => jitter[i * sides + k],
                (i, k, p) =>
                {
                    float angle = turn + k * math.PI * 2f / sides;
                    // Hand-split faces are never true; the top is a slanted break, not a sawn end.
                    float3 q = new float3(p.x * jitter[i * sides + k], p.y, p.z * jitter[i * sides + k]);
                    if (i == 4)
                    {
                        q.y += 0.28f * math.cos(angle - cut);
                    }

                    return Bend(q);
                });

            void Band(Geo geo, float t, float proud, float thickness, float tone)
            {
                float y = fnd + height * t, radius = RadiusAt(t) * 1.06f + proud;
                Lathe(geo, float3.zero, new[]
                {
                    new float2(radius - proud, y - 0.03f), new float2(radius, y), new float2(radius, y + thickness),
                    new float2(radius - proud, y + thickness + 0.03f),
                }, sides, turn, i => tone, null, (i, k, p) => Bend(p));
            }

            Band(g[TrimSubMesh], 0.22f, 0.07f, 0.24f, 0.9f);
            Band(g[TrimSubMesh], 0.7f, 0.06f, 0.2f, 0.95f);
            // The rune ring is what the fight is lit by at night.
            Band(g[GlowSubMesh], 0.6f, 0.03f, 0.14f, 1f);
            Band(g[GlowSubMesh], 0.3f, 0.03f, 0.07f, 0.9f);
        }

        // ---- ruins ---------------------------------------------------------------------------------------------

        private static float RuinHeight(in ArchSpec spec) =>
            (spec.Kind == BuildingKind.Tower ? math.min(spec.Storeys, 2) : 1) * StoreyM * RuinKeep;

        private static void BuildRuin(Geo[] g, ArchSpec spec, ref Pcg32 rng)
        {
            const float thick = 0.45f, course = 0.35f;
            float hx = spec.WidthM * 0.5f, hz = spec.DepthM * 0.5f, fnd = spec.FoundationM;
            float height = RuinHeight(spec);
            Box(g[WallSubMesh], new float3(0f, -0.3f, 0f), hx + 0.1f, hz + 0.1f, fnd + 0.3f + 0.12f, 0.58f, 0.76f);

            float phase = rng.NextFloat() * 100f;
            int tallCorner = (int)rng.NextUInt(4);
            float doorT = (rng.NextFloat() * 2f - 1f) * math.max(0f, hx - 1.3f);
            float walked = 0f;
            for (int f = 0; f < 4; f++)
            {
                float len = (f < 2 ? hx : hz) * 2f, dist = f < 2 ? hz : hx;
                int blocks = math.max(2, (int)math.round(len / 0.7f));
                float width = len / blocks;
                for (int k = 0; k < blocks; k++)
                {
                    float t = -len * 0.5f + width * (k + 0.5f);
                    float keep = 0.25f + 0.75f * math.saturate(0.5f + 0.6f * noise.snoise(new float2((walked + t) * 0.35f, phase)));
                    // One corner outlasts the rest: a ruin needs a high point to be seen over the grass.
                    float3 at = FaceNormal[f] * dist + FaceRight[f] * t;
                    float sx = (tallCorner & 1) == 0 ? 1f : -1f, sz = (tallCorner & 2) == 0 ? 1f : -1f;
                    float toCorner = math.length(new float2(at.x - sx * hx, at.z - sz * hz));
                    keep = math.max(keep, 1f - toCorner / 2.2f);
                    float h = math.round(height * keep / course) * course;
                    bool doorway = f == 0 && math.abs(t - doorT) < 0.65f;
                    if (h >= course && !doorway)
                    {
                        float tone = 0.93f + rng.NextFloat() * 0.12f;
                        Panel(g[WallSubMesh], FaceNormal[f], FaceRight[f], dist - thick, t, fnd, width + 0.02f, h, 0f, thick,
                            0.74f, 0.8f + 0.22f * h / height, tone);
                    }
                    else
                    {
                        rng.NextFloat(); // keep the stream aligned whether or not this block stands
                    }
                }

                walked += len;
            }

            int fallen = 3 + (int)rng.NextUInt(2);
            for (int i = 0; i < fallen; i++)
            {
                float edge = 0.22f + rng.NextFloat() * 0.16f;
                float3 centre = new float3((rng.NextFloat() * 2f - 1f) * (hx - thick - 0.5f), fnd + 0.12f + edge * 0.7f,
                    (rng.NextFloat() * 2f - 1f) * (hz - thick - 0.5f));
                quaternion tumble = quaternion.Euler(rng.NextFloat() * 0.6f, rng.NextFloat() * 6.28f, rng.NextFloat() * 0.6f);
                float3[] c = Corners(edge * 1.3f, edge, -edge, edge * 1.3f, edge, edge);
                for (int k = 0; k < c.Length; k++)
                {
                    c[k] = centre + math.mul(tumble, c[k]);
                }

                Hexa(g[WallSubMesh], c, 0.78f, 0.95f, true, true);
            }

            // One charred rafter still leans where the roof came down.
            Beam(g[TrimSubMesh], new float3(-hx * 0.45f, fnd + 0.1f, hz * 0.25f), new float3(hx * 0.3f, fnd + height * 0.7f, -hz + thick),
                0.2f, 0.2f, new float3(0f, 0f, 1f), 0.6f, 0.85f);
        }

        // ---- parts ---------------------------------------------------------------------------------------------

        private static void Door(Geo[] g, float3 n, float3 r, float dist, float t, float y, float w, float h)
        {
            Panel(g[TrimSubMesh], n, r, dist, t, y, w + 0.3f, h + 0.16f, -0.03f, 0.09f, 0.88f, 1f);
            Panel(g[TrimSubMesh], n, r, dist, t, y, w, h, 0.02f, 0.13f, 0.5f, 0.62f);
            Panel(g[WallSubMesh], n, r, dist, t, y - 0.5f, w + 0.6f, 0.5f, 0f, 0.55f, 0.6f, 0.8f);
            // A lamp over every door: at night a village is a string of lights along its road.
            Panel(g[TrimSubMesh], n, r, dist, t, y + h + 0.38f, 0.1f, 0.1f, 0f, 0.3f, 0.8f, 0.8f);
            Panel(g[GlowSubMesh], n, r, dist, t, y + h + 0.18f, 0.2f, 0.22f, 0.16f, 0.36f, 0.95f, 1.1f);
        }

        private static void Window(Geo[] g, float3 n, float3 r, float dist, float t, float y, float w, float h, bool mullion,
            bool shutters, bool lit)
        {
            Panel(g[TrimSubMesh], n, r, dist, t, y - 0.1f, w + 0.22f, h + 0.2f, -0.03f, 0.07f, 0.88f, 1f);
            Panel(g[lit ? GlowSubMesh : TrimSubMesh], n, r, dist, t, y, w, h, 0.02f, 0.1f, lit ? 0.85f : 0.3f, lit ? 1.05f : 0.36f);
            if (mullion)
            {
                Panel(g[TrimSubMesh], n, r, dist, t, y, 0.07f, h, 0.08f, 0.13f, 0.9f, 1f);
                Panel(g[TrimSubMesh], n, r, dist, t, y + h * 0.55f, w, 0.07f, 0.08f, 0.13f, 0.95f, 0.95f);
            }

            Panel(g[TrimSubMesh], n, r, dist, t, y - 0.2f, w + 0.4f, 0.1f, 0f, 0.2f, 0.95f, 1f);
            if (shutters)
            {
                for (int e = -1; e <= 1; e += 2)
                {
                    Panel(g[RoofSubMesh], n, r, dist, t + e * (w * 0.74f + 0.14f), y, w * 0.46f, h, 0f, 0.06f, 0.82f, 1f);
                }
            }
        }

        /// <summary>
        /// A roof as a stack of shrinking rectangular courses: gable (Hip 0), hipped, or a pyramid on a square plan.
        /// Returns the ridge height.
        /// </summary>
        // u runs across the span, v along the ridge. The eave is lowered until the roof passes through the wall top at
        // the wall face, so a swept (Curve > 1) roof never lets the wall corners poke through it.
        private static float Roof(Geo roof, Geo gable, float3 centre, float halfSpan, float halfLen, bool alongX, RoofForm f, float gableOverM,
            float damage = 0f, float phase = 0f)
        {
            const int K = 5, J = 6;

            // Damage first slips tiles (darker patches), then opens holes; never at the eave, ridge or verge, which
            // carry the silhouette.
            float Wear(int side, int k, int j) => noise.snoise(new float2(phase + side * 7.3f + j * 0.55f, k * 0.55f));
            bool Hole(int side, int k, int j) =>
                damage > 0.45f && k >= 1 && k < K - 1 && j >= 1 && j < J - 1 && Wear(side, k, j) > 1.25f - damage * 1.3f;

            bool hipped = f.Hip > 0f;
            float a = halfSpan + f.OverhangM;
            float b = halfLen + (hipped ? f.OverhangM : gableOverM);
            float hipLen = f.Hip * math.min(a, b * 0.98f);
            float span = a * f.Pitch;
            // Measured on the K straight courses actually drawn, not on the curve: under a bulging roof (Curve < 1)
            // the chords run below the curve, and the wall head would show through between two courses.
            float at = f.OverhangM / a * K;
            int course = math.min((int)at, K - 1);
            float eave = centre.y - span * math.lerp(math.pow(course / (float)K, f.Curve), math.pow((course + 1) / (float)K, f.Curve), at - course);

            float Height(float s, float v, float vk)
            {
                float along = vk > 1e-4f ? v / vk : 0f;
                float corner = v / b;
                return eave + span * math.pow(s, f.Curve) - f.Sag * s * (1f - along * along) +
                       f.Upturn * (1f - s) * (1f - s) * corner * corner;
            }

            float3 Pos(float u, float y, float v)
            {
                float3 m = Along(alongX, u, 0f, v);
                return new float3(centre.x + m.x, y, centre.z + m.z);
            }

            var top = new int[2, K + 1, J + 1];
            var under = new int[2, K + 1, J + 1];
            for (int side = 0; side < 2; side++)
            {
                for (int k = 0; k <= K; k++)
                {
                    float s = k / (float)K;
                    float u = a * (1f - s) * (side == 0 ? 1f : -1f);
                    float vk = b - hipLen * s;
                    for (int j = 0; j <= J; j++)
                    {
                        float v = math.lerp(-vk, vk, j / (float)J);
                        float3 p = Pos(u, Height(s, v, vk), v);
                        top[side, k, j] = roof.V(p, math.lerp(0.84f, 1.06f, s));
                        under[side, k, j] = roof.V(p - new float3(0f, f.ThicknessM, 0f), 0.5f);
                    }
                }
            }

            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? 1f : -1f;
                for (int k = 0; k < K; k++)
                {
                    // Alternating tones per course read as rows of tiles or thatch bundles.
                    float tone = (k & 1) == 0 ? 1.035f : 0.965f;
                    for (int j = 0; j < J; j++)
                    {
                        if (Hole(side, k, j))
                        {
                            continue;
                        }

                        bool slipped = damage > 0.15f && Wear(side, k + 11, j) > 1.05f - damage;
                        roof.Quad(top[side, k, j], top[side, k, j + 1], top[side, k + 1, j + 1], top[side, k + 1, j],
                            Along(alongX, sign * f.Pitch, 1f, 0f), slipped ? tone * 0.8f : tone);
                        roof.Quad(under[side, k, j], under[side, k, j + 1], under[side, k + 1, j + 1], under[side, k + 1, j],
                            Along(alongX, -sign * f.Pitch, -1f, 0f));
                    }
                }

                for (int j = 0; j < J; j++)
                {
                    roof.Quad(top[side, 0, j], top[side, 0, j + 1], under[side, 0, j + 1], under[side, 0, j], Along(alongX, sign, 0f, 0f), 0.7f);
                }
            }

            for (int e = 0; e < 2; e++)
            {
                int je = e == 0 ? 0 : J;
                float sv = e == 0 ? -1f : 1f;
                if (hipped)
                {
                    for (int k = 0; k < K; k++)
                    {
                        float tone = (k & 1) == 0 ? 1.035f : 0.965f;
                        roof.Quad(top[0, k, je], top[1, k, je], top[1, k + 1, je], top[0, k + 1, je], Along(alongX, 0f, 1f, sv * f.Pitch), tone);
                        roof.Quad(under[0, k, je], under[1, k, je], under[1, k + 1, je], under[0, k + 1, je], Along(alongX, 0f, -1f, -sv * f.Pitch));
                    }

                    roof.Quad(top[0, 0, je], top[1, 0, je], under[1, 0, je], under[0, 0, je], Along(alongX, 0f, 0f, sv), 0.7f);
                    continue;
                }

                for (int side = 0; side < 2; side++)
                {
                    for (int k = 0; k < K; k++)
                    {
                        roof.Quad(top[side, k, je], top[side, k + 1, je], under[side, k + 1, je], under[side, k, je], Along(alongX, 0f, 0f, sv), 0.7f);
                    }
                }

                if (gable == null)
                {
                    continue;
                }

                // The gable wall: from the wall-top corners up along the underside of the roof to the ridge.
                float v = sv * halfLen;
                var rim = new List<int> { gable.V(Pos(-halfSpan, centre.y, v), 0.95f) };
                for (int k = 0; k <= K; k++)
                {
                    float s = k / (float)K, u = a * (1f - s);
                    if (u < halfSpan)
                    {
                        rim.Add(gable.V(Pos(-u, Height(s, v, b) - f.ThicknessM * 0.5f, v), 0.97f + 0.08f * s));
                    }
                }

                for (int k = K - 1; k >= 0; k--)
                {
                    float s = k / (float)K, u = a * (1f - s);
                    if (u < halfSpan)
                    {
                        rim.Add(gable.V(Pos(u, Height(s, v, b) - f.ThicknessM * 0.5f, v), 0.97f + 0.08f * s));
                    }
                }

                rim.Add(gable.V(Pos(halfSpan, centre.y, v), 0.95f));
                int hub = gable.V(Pos(0f, centre.y, v), 0.95f);
                for (int i = 0; i + 1 < rim.Count; i++)
                {
                    gable.Tri(hub, rim[i], rim[i + 1], Along(alongX, 0f, 0f, sv));
                }
            }

            return eave + span - f.Sag;
        }

        // Maps (across, up, along-ridge) to the building frame for either ridge direction.
        private static float3 Along(bool alongX, float u, float y, float v) => alongX ? new float3(v, y, u) : new float3(u, y, v);

        /// <summary>A surface of revolution about the vertical through <paramref name="centre"/>.</summary>
        // `profile` is (radius, height), listed so the outside is on the right when walking it: up a wall, in over a
        // roof, out along an underside. That order alone decides which way each band faces.
        private static void Lathe(Geo geo, float3 centre, float2[] profile, int sides, float turn, Func<int, float> tone,
            Func<int, int, float> faceTone, Func<int, int, float3, float3> warp)
        {
            int first = geo.Part.P.Count;
            for (int i = 0; i < profile.Length; i++)
            {
                for (int k = 0; k < sides; k++)
                {
                    float angle = turn + k * math.PI * 2f / sides;
                    float3 p = centre + new float3(math.cos(angle) * profile[i].x, profile[i].y, math.sin(angle) * profile[i].x);
                    geo.V(warp != null ? warp(i, k, p) : p, tone(i));
                }
            }

            for (int i = 0; i + 1 < profile.Length; i++)
            {
                float dr = profile[i + 1].x - profile[i].x, dy = profile[i + 1].y - profile[i].y;
                for (int k = 0; k < sides; k++)
                {
                    int next = (k + 1) % sides;
                    float angle = turn + (k + 0.5f) * math.PI * 2f / sides;
                    geo.Quad(first + i * sides + k, first + i * sides + next, first + (i + 1) * sides + next, first + (i + 1) * sides + k,
                        new float3(math.cos(angle) * dy, -dr, math.sin(angle) * dy), faceTone != null ? faceTone(i, k) : 1f);
                }
            }
        }

        // A box standing on a wall face: `t` along the face, `y` its foot, from p0 to p1 out of the face.
        private static void Panel(Geo geo, float3 n, float3 r, float dist, float t, float y, float w, float h, float p0, float p1,
            float toneBottom, float toneTop, float faceTone = 1f, float accentFree = 1f)
        {
            float3 origin = n * dist + r * t;
            float3 half = r * (w * 0.5f);
            float3 back = origin + n * p0, front = origin + n * p1;
            float3 foot = new float3(0f, y, 0f), head = new float3(0f, y + h, 0f);
            Hexa(geo, new[]
            {
                back - half + foot, back + half + foot, front + half + foot, front - half + foot,
                back - half + head, back + half + head, front + half + head, front - half + head,
            }, toneBottom, toneTop, true, true, faceTone, accentFree);
        }

        private static void Box(Geo geo, float3 baseCentre, float halfX, float halfZ, float height, float toneBottom, float toneTop,
            float topScale = 1f)
        {
            float3[] c = Corners(halfX, halfZ, 0f, halfX * topScale, halfZ * topScale, height);
            for (int i = 0; i < c.Length; i++)
            {
                c[i] += baseCentre;
            }

            Hexa(geo, c, toneBottom, toneTop, true, false);
        }

        private static void Beam(Geo geo, float3 from, float3 to, float width, float depth, float3 depthDir, float tone0, float tone1)
        {
            float3 axis = math.normalize(to - from);
            float3 side = math.normalize(math.cross(depthDir, axis));
            float3 s = side * (width * 0.5f), d = math.cross(axis, side) * (depth * 0.5f);
            Hexa(geo, new[] { from - s - d, from + s - d, from + s + d, from - s + d, to - s - d, to + s - d, to + s + d, to - s + d },
                tone0, tone1, true, true);
        }

        private static float3[] Corners(float halfX0, float halfZ0, float y0, float halfX1, float halfZ1, float y1) => new[]
        {
            new float3(-halfX0, y0, -halfZ0), new float3(halfX0, y0, -halfZ0), new float3(halfX0, y0, halfZ0), new float3(-halfX0, y0, halfZ0),
            new float3(-halfX1, y1, -halfZ1), new float3(halfX1, y1, -halfZ1), new float3(halfX1, y1, halfZ1), new float3(-halfX1, y1, halfZ1),
        };

        // Eight corners: a ring of four, then the ring above it in the same order. Opposite sides get slightly
        // different tones so a box still reads as a solid when the sun is straight overhead.
        private static void Hexa(Geo geo, float3[] c, float toneBottom, float toneTop, bool cap, bool floor, float faceTone = 1f,
            float accentFree = 1f)
        {
            float3 inside = float3.zero;
            foreach (float3 p in c)
            {
                inside += p;
            }

            inside /= c.Length;
            for (int k = 0; k < 4; k++)
            {
                int n = (k + 1) & 3;
                float3 hint = (c[k] + c[n] + c[4 + n] + c[4 + k]) * 0.25f - inside;
                geo.Quad(geo.V(c[k], toneBottom, accentFree), geo.V(c[n], toneBottom, accentFree), geo.V(c[4 + n], toneTop, accentFree), geo.V(c[4 + k], toneTop, accentFree), hint,
                    faceTone * ((k & 1) == 0 ? 1f : 0.95f));
            }

            if (cap)
            {
                float3 hint = (c[4] + c[5] + c[6] + c[7]) * 0.25f - inside;
                geo.Quad(geo.V(c[4], toneTop, accentFree), geo.V(c[5], toneTop, accentFree), geo.V(c[6], toneTop, accentFree), geo.V(c[7], toneTop, accentFree), hint, faceTone * 1.03f);
            }

            if (floor)
            {
                float3 hint = (c[0] + c[1] + c[2] + c[3]) * 0.25f - inside;
                geo.Quad(geo.V(c[0], toneBottom, accentFree), geo.V(c[1], toneBottom, accentFree), geo.V(c[2], toneBottom, accentFree), geo.V(c[3], toneBottom, accentFree), hint,
                    faceTone * 0.8f);
            }
        }

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        /// <summary>One sub-mesh under construction: faces are wound by the direction they must look, not by vertex order.</summary>
        internal sealed class Geo
        {
            public readonly SceneryMeshes.Part Part = new SceneryMeshes.Part();

            // rgb = baked tone, a = 1 (the weathering mask is painted afterwards from position and slope).
            public int V(float3 position, float tone) => Part.Add(position, new Color(tone, tone, tone, 1f), float2.zero);

            public int V(float3 position, float tone, float accentFree) =>
                Part.Add(position, new Color(tone, tone, tone, accentFree), float2.zero);

            // Slivers (a lathe band closing on its axis, a pyramid's last course) are dropped: they shade as noise.
            public void Tri(int a, int b, int c, float3 outward, float tone = 1f)
            {
                float3 normal = math.cross(Part.P[b] - Part.P[a], Part.P[c] - Part.P[a]);
                if (math.lengthsq(normal) < 1e-10f)
                {
                    return;
                }

                if (math.dot(normal, outward) < 0f)
                {
                    (b, c) = (c, b);
                }

                Part.T.Add(a);
                Part.T.Add(b);
                Part.T.Add(c);
                Part.FaceTone.Add(tone);
            }

            public void Quad(int a, int b, int c, int d, float3 outward, float tone = 1f)
            {
                Tri(a, b, c, outward, tone);
                Tri(a, c, d, outward, tone);
            }
        }
    }
}
