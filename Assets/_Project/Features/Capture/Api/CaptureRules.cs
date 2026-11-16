using System;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;

namespace Cosmimera.Capture
{
    /// <summary>Category of a knocked-out capture target.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum CaptureTargetKind
    {
        Wild = 0,
        PlanetBoss = 1,
        DungeonBoss = 2,
    }

    /// <summary>Tiered basic vs elemental advanced capture vessel class.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum CaptureVesselKind
    {
        Basic = 0,
        Advanced = 1,
    }

    /// <summary>Rule-layer specification of a consumable capture vessel (4-tier basic or 10-element advanced).</summary>
    /// <remarks>
    /// Pre: constructed via <see cref="Basic"/> or <see cref="Advanced"/>.
    /// Post: immutable, validated percentages in 0..100.
    /// Side effects: none.
    /// </remarks>
    public readonly struct CaptureVesselSpec
    {
        private CaptureVesselSpec(CaptureVesselKind kind, CreatureStage tier, Element terrainElement,
            int basePercent, int advancedTerrainBonusPercent)
        {
            Kind = kind;
            Tier = tier;
            TerrainElement = terrainElement;
            BasePercent = basePercent;
            AdvancedTerrainBonusPercent = advancedTerrainBonusPercent;
        }

        /// <summary>Whether this is a 4-tier basic vessel or an elemental advanced-terrain vessel.</summary>
        /// <remarks>Pre: none. Post: defined <see cref="CaptureVesselKind"/>. Side effects: none.</remarks>
        public CaptureVesselKind Kind { get; }

        /// <summary>Target stage tier for <see cref="CaptureVesselKind.Basic"/> (Juvenile..Overload).</summary>
        /// <remarks>Pre: none. Post: defined <see cref="CreatureStage"/>. Side effects: none.</remarks>
        public CreatureStage Tier { get; }

        /// <summary>Matching advanced-terrain/dungeon element for <see cref="CaptureVesselKind.Advanced"/>.</summary>
        /// <remarks>Pre: none. Post: defined <see cref="Element"/>. Side effects: none.</remarks>
        public Element TerrainElement { get; }

        /// <summary>Base capture percentage in 0..100.</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int BasePercent { get; }

        /// <summary>Extra capture percentage points when used in a matching advanced terrain or dungeon.</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int AdvancedTerrainBonusPercent { get; }

        /// <summary>Creates a basic consumable vessel for <paramref name="tier"/> (Juvenile/Adolescent/Adult/Overload).</summary>
        /// <remarks>
        /// Pre: <paramref name="tier"/> defined; 0 &lt;= <paramref name="basePercent"/> &lt;= 100.
        /// Post: Kind == Basic; Tier and BasePercent retained.
        /// Side effects: none.
        /// </remarks>
        public static CaptureVesselSpec Basic(CreatureStage tier, int basePercent)
        {
            Contract.Requires(Enum.IsDefined(typeof(CreatureStage), tier), "tier must be defined");
            Contract.Requires(basePercent >= 0 && basePercent <= 100, "basePercent must be 0..100");
            return new CaptureVesselSpec(CaptureVesselKind.Basic, tier, Element.Metal, basePercent, 0);
        }

        /// <summary>Creates a crafted advanced vessel specialized for <paramref name="terrainElement"/> terrains and dungeons.</summary>
        /// <remarks>
        /// Pre: <paramref name="terrainElement"/> defined; 0 &lt;= <paramref name="basePercent"/>, <paramref name="advancedTerrainBonusPercent"/> &lt;= 100.
        /// Post: Kind == Advanced; TerrainElement, BasePercent, and AdvancedTerrainBonusPercent retained.
        /// Side effects: none.
        /// </remarks>
        public static CaptureVesselSpec Advanced(Element terrainElement, int basePercent, int advancedTerrainBonusPercent)
        {
            Contract.Requires(Enum.IsDefined(typeof(Element), terrainElement), "terrainElement must be defined");
            Contract.Requires(basePercent >= 0 && basePercent <= 100, "basePercent must be 0..100");
            Contract.Requires(advancedTerrainBonusPercent >= 0 && advancedTerrainBonusPercent <= 100,
                "advancedTerrainBonusPercent must be 0..100");
            return new CaptureVesselSpec(CaptureVesselKind.Advanced, CreatureStage.Juvenile, terrainElement,
                basePercent, advancedTerrainBonusPercent);
        }
    }

    /// <summary>Knocked-out capture target descriptor.</summary>
    /// <remarks>
    /// Pre: constructed via <see cref="Wild"/>, <see cref="PlanetBoss(PlanetSize)"/>, <see cref="PlanetBoss(CreatureStage)"/>, or <see cref="DungeonBoss"/>.
    /// Post: immutable, valid stage and element.
    /// Side effects: none.
    /// </remarks>
    public readonly struct CaptureTarget
    {
        private CaptureTarget(CaptureTargetKind kind, CreatureStage stage, Element dungeonElement)
        {
            Kind = kind;
            Stage = stage;
            DungeonElement = dungeonElement;
        }

        /// <summary>Target role (wild, planet boss, or dungeon boss).</summary>
        /// <remarks>Pre: none. Post: defined <see cref="CaptureTargetKind"/>. Side effects: none.</remarks>
        public CaptureTargetKind Kind { get; }

        /// <summary>Target evolution stage (Juvenile..Overload).</summary>
        /// <remarks>Pre: none. Post: defined <see cref="CreatureStage"/>. Side effects: none.</remarks>
        public CreatureStage Stage { get; }

        /// <summary>Element of the dungeon when <see cref="Kind"/> is <see cref="CaptureTargetKind.DungeonBoss"/>.</summary>
        /// <remarks>Pre: none. Post: defined <see cref="Element"/>. Side effects: none.</remarks>
        public Element DungeonElement { get; }

        /// <summary>Creates a wild/elite capture target at <paramref name="stage"/>.</summary>
        /// <remarks>
        /// Pre: <paramref name="stage"/> defined.
        /// Post: Kind == Wild; Stage == <paramref name="stage"/>.
        /// Side effects: none.
        /// </remarks>
        public static CaptureTarget Wild(CreatureStage stage)
        {
            Contract.Requires(Enum.IsDefined(typeof(CreatureStage), stage), "stage must be defined");
            return new CaptureTarget(CaptureTargetKind.Wild, stage, Element.Metal);
        }

        /// <summary>Creates a planet boss target for one of the four <see cref="PlanetSize"/> classes.</summary>
        /// <remarks>
        /// Pre: <paramref name="planetSize"/> defined.
        /// Post: Kind == PlanetBoss; Stage == <see cref="CaptureRules.PlanetBossStage"/>(<paramref name="planetSize"/>).
        /// Side effects: none.
        /// </remarks>
        public static CaptureTarget PlanetBoss(PlanetSize planetSize) =>
            PlanetBoss(CaptureRules.PlanetBossStage(planetSize));

        /// <summary>Creates a planet boss target at <paramref name="stage"/>.</summary>
        /// <remarks>
        /// Pre: <paramref name="stage"/> defined.
        /// Post: Kind == PlanetBoss; Stage == <paramref name="stage"/>.
        /// Side effects: none.
        /// </remarks>
        public static CaptureTarget PlanetBoss(CreatureStage stage)
        {
            Contract.Requires(Enum.IsDefined(typeof(CreatureStage), stage), "stage must be defined");
            return new CaptureTarget(CaptureTargetKind.PlanetBoss, stage, Element.Metal);
        }

        /// <summary>Creates a dungeon boss target belonging to <paramref name="dungeonElement"/>.</summary>
        /// <remarks>
        /// Pre: <paramref name="stage"/> and <paramref name="dungeonElement"/> defined.
        /// Post: Kind == DungeonBoss; Stage and DungeonElement retained.
        /// Side effects: none.
        /// </remarks>
        public static CaptureTarget DungeonBoss(CreatureStage stage, Element dungeonElement)
        {
            Contract.Requires(Enum.IsDefined(typeof(CreatureStage), stage), "stage must be defined");
            Contract.Requires(Enum.IsDefined(typeof(Element), dungeonElement), "dungeonElement must be defined");
            return new CaptureTarget(CaptureTargetKind.DungeonBoss, stage, dungeonElement);
        }
    }

    /// <summary>Environmental modifiers active during a capture attempt.</summary>
    /// <remarks>
    /// Pre: <see cref="BonusPercent"/> &gt;= 0; <see cref="TerrainElement"/> defined.
    /// Post: immutable.
    /// Side effects: none.
    /// </remarks>
    public readonly struct CaptureEnvironment
    {
        /// <summary>Creates capture environment modifiers.</summary>
        /// <remarks>
        /// Pre: <paramref name="bonusPercent"/> &gt;= 0; <paramref name="terrainElement"/> defined.
        /// Post: fields retained.
        /// Side effects: none.
        /// </remarks>
        public CaptureEnvironment(bool bountyAffix, int bonusPercent, bool inAdvancedTerrain = false,
            Element terrainElement = Element.Metal)
        {
            Contract.Requires(bonusPercent >= 0, "bonusPercent must be >= 0");
            Contract.Requires(Enum.IsDefined(typeof(Element), terrainElement), "terrainElement must be defined");
            BountyAffix = bountyAffix;
            BonusPercent = bonusPercent;
            InAdvancedTerrain = inAdvancedTerrain;
            TerrainElement = terrainElement;
        }

        /// <summary>Whether the region has the bounty affix (+10%).</summary>
        /// <remarks>Pre: none. Post: boolean. Side effects: none.</remarks>
        public bool BountyAffix { get; }

        /// <summary>Additional percentage points from field utilities and food buffs.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int BonusPercent { get; }

        /// <summary>Whether the attempt occurs inside one of the 10 elemental advanced terrains/dungeons.</summary>
        /// <remarks>Pre: none. Post: boolean. Side effects: none.</remarks>
        public bool InAdvancedTerrain { get; }

        /// <summary>Element of the current advanced terrain/dungeon when <see cref="InAdvancedTerrain"/> is true.</summary>
        /// <remarks>Pre: none. Post: defined <see cref="Element"/>. Side effects: none.</remarks>
        public Element TerrainElement { get; }
    }

    /// <summary>Per-stage capture modifier steps when a basic vessel tier differs from the target's stage.</summary>
    /// <remarks>
    /// Pre: both step values &gt;= 0.
    /// Post: immutable.
    /// Side effects: none.
    /// </remarks>
    public readonly struct CaptureStageCurve
    {
        /// <summary>Creates a stage-difference modifier curve.</summary>
        /// <remarks>
        /// Pre: <paramref name="higherTierBonusPerStage"/> &gt;= 0; <paramref name="lowerTierPenaltyPerStage"/> &gt;= 0.
        /// Post: fields retained.
        /// Side effects: none.
        /// </remarks>
        public CaptureStageCurve(int higherTierBonusPerStage, int lowerTierPenaltyPerStage)
        {
            Contract.Requires(higherTierBonusPerStage >= 0, "higherTierBonusPerStage must be >= 0");
            Contract.Requires(lowerTierPenaltyPerStage >= 0, "lowerTierPenaltyPerStage must be >= 0");
            HigherTierBonusPerStage = higherTierBonusPerStage;
            LowerTierPenaltyPerStage = lowerTierPenaltyPerStage;
        }

        /// <summary>Percentage points added per tier above the target's stage.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int HigherTierBonusPerStage { get; }

        /// <summary>Percentage points subtracted per tier below the target's stage.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int LowerTierPenaltyPerStage { get; }
    }

    /// <summary>Capture roll for a knocked-out wild creature. REQ: lobby-meta.md#收服與蛋 (Q-M2: wild capture after knock-out).</summary>
    /// <remarks>
    /// Percentages come from Data/balance.json#capture (placeholder until P5-07, tech-debt D0-B1) and the region affix
    /// affix_bounty (+10, Terra affixes.json). Pure; the caller owns the RNG stream so every peer rolls identically.
    /// </remarks>
    public static class CaptureRules
    {
        /// <summary>Bonus of the affix_bounty region affix, percent.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int BountyBonusPercent = 10;

        /// <summary>Default stage-difference curve (+15 per tier above, -25 per tier below).</summary>
        /// <remarks>Pre: none. Post: non-negative steps. Side effects: none.</remarks>
        public static readonly CaptureStageCurve DefaultStageCurve = new CaptureStageCurve(15, 25);

        /// <summary>Maps one of the four <see cref="PlanetSize"/> classes to its planet boss <see cref="CreatureStage"/>.</summary>
        /// <remarks>
        /// Pre: <paramref name="planetSize"/> defined.
        /// Post: Asteroid-&gt;Juvenile, Moon-&gt;Adolescent, Planet-&gt;Adult, Supergiant-&gt;Overload.
        /// Side effects: none.
        /// </remarks>
        public static CreatureStage PlanetBossStage(PlanetSize planetSize)
        {
            Contract.Requires(Enum.IsDefined(typeof(PlanetSize), planetSize), "planetSize must be defined");
            switch (planetSize)
            {
                case PlanetSize.Asteroid: return CreatureStage.Juvenile;
                case PlanetSize.Moon: return CreatureStage.Adolescent;
                case PlanetSize.Planet: return CreatureStage.Adult;
                case PlanetSize.Supergiant: return CreatureStage.Overload;
                default: throw new ArgumentOutOfRangeException(nameof(planetSize));
            }
        }

        /// <summary>Capture chance in percent using <see cref="DefaultStageCurve"/>.</summary>
        /// <remarks>
        /// Pre: <paramref name="vessel"/>, <paramref name="target"/>, and <paramref name="environment"/> satisfy their invariants.
        /// Post: 0..100; 100 for Basic (tier &gt;= boss stage) on PlanetBoss, and 100 for Advanced (matching element) on DungeonBoss in matching advanced terrain.
        /// Side effects: none.
        /// </remarks>
        public static int ChancePercent(CaptureVesselSpec vessel, CaptureTarget target, CaptureEnvironment environment) =>
            ChancePercent(vessel, target, environment, DefaultStageCurve);

        /// <summary>Capture chance in percent for a tiered basic or elemental advanced vessel.</summary>
        /// <remarks>
        /// Pre: <paramref name="vessel"/>, <paramref name="target"/>, <paramref name="environment"/>, and <paramref name="stageCurve"/> satisfy their invariants.
        /// Post: result in 0..100; PlanetBoss is 100 iff Basic &amp;&amp; vessel.Tier &gt;= target.Stage; DungeonBoss is 100 iff Advanced &amp;&amp; in matching advanced terrain/dungeon element.
        /// Side effects: none.
        /// </remarks>
        public static int ChancePercent(CaptureVesselSpec vessel, CaptureTarget target,
            CaptureEnvironment environment, CaptureStageCurve stageCurve)
        {
            Contract.Requires(Enum.IsDefined(typeof(CaptureVesselKind), vessel.Kind), "vessel.Kind must be defined");
            Contract.Requires(Enum.IsDefined(typeof(CreatureStage), vessel.Tier), "vessel.Tier must be defined");
            Contract.Requires(Enum.IsDefined(typeof(Element), vessel.TerrainElement), "vessel.TerrainElement must be defined");
            Contract.Requires(vessel.BasePercent >= 0 && vessel.BasePercent <= 100, "vessel.BasePercent must be 0..100");
            Contract.Requires(vessel.AdvancedTerrainBonusPercent >= 0 && vessel.AdvancedTerrainBonusPercent <= 100,
                "vessel.AdvancedTerrainBonusPercent must be 0..100");
            Contract.Requires(Enum.IsDefined(typeof(CaptureTargetKind), target.Kind), "target.Kind must be defined");
            Contract.Requires(Enum.IsDefined(typeof(CreatureStage), target.Stage), "target.Stage must be defined");
            Contract.Requires(Enum.IsDefined(typeof(Element), target.DungeonElement), "target.DungeonElement must be defined");
            Contract.Requires(environment.BonusPercent >= 0, "environment.BonusPercent must be >= 0");
            Contract.Requires(Enum.IsDefined(typeof(Element), environment.TerrainElement), "environment.TerrainElement must be defined");
            Contract.Requires(stageCurve.HigherTierBonusPerStage >= 0 && stageCurve.LowerTierPenaltyPerStage >= 0,
                "stageCurve steps must be >= 0");

            if (target.Kind == CaptureTargetKind.PlanetBoss &&
                vessel.Kind == CaptureVesselKind.Basic &&
                vessel.Tier >= target.Stage)
            {
                return 100;
            }

            if (target.Kind == CaptureTargetKind.DungeonBoss &&
                vessel.Kind == CaptureVesselKind.Advanced &&
                environment.InAdvancedTerrain &&
                environment.TerrainElement == vessel.TerrainElement &&
                target.DungeonElement == vessel.TerrainElement)
            {
                return 100;
            }

            int chance = vessel.BasePercent;
            if (vessel.Kind == CaptureVesselKind.Basic)
            {
                int diff = (int)vessel.Tier - (int)target.Stage;
                chance += diff >= 0
                    ? diff * stageCurve.HigherTierBonusPerStage
                    : diff * stageCurve.LowerTierPenaltyPerStage;
            }
            else if (environment.InAdvancedTerrain && environment.TerrainElement == vessel.TerrainElement)
            {
                chance += vessel.AdvancedTerrainBonusPercent;
            }

            chance += (environment.BountyAffix ? BountyBonusPercent : 0) + environment.BonusPercent;
            return chance < 0 ? 0 : chance > 100 ? 100 : chance;
        }

        /// <summary>Capture chance in percent.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= basePercent, bossPercent &lt;= 100.
        /// Post: (isBoss ? bossPercent : basePercent) + (bounty ? 10 : 0), clamped to 0..100.
        /// Side effects: none.
        /// </remarks>
        public static int ChancePercent(bool isBoss, bool bounty, int basePercent, int bossPercent) =>
            ChancePercent(isBoss, bounty, basePercent, bossPercent, 0);

        /// <summary>Capture chance in percent including field-utility bonus.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= basePercent, bossPercent &lt;= 100; utilityBonusPercent &gt;= 0.
        /// Post: (isBoss ? bossPercent : basePercent) + (bounty ? 10 : 0) + utilityBonusPercent, clamped to 0..100.
        /// Side effects: none.
        /// </remarks>
        public static int ChancePercent(bool isBoss, bool bounty, int basePercent, int bossPercent, int utilityBonusPercent)
        {
            Contract.Requires(basePercent >= 0 && basePercent <= 100 && bossPercent >= 0 && bossPercent <= 100,
                "percents must be 0..100");
            Contract.Requires(utilityBonusPercent >= 0, "utilityBonusPercent must be >= 0");
            int chance = (isBoss ? bossPercent : basePercent) + (bounty ? BountyBonusPercent : 0) + utilityBonusPercent;
            return chance > 100 ? 100 : chance;
        }

        /// <summary>Rolls one capture attempt.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= chancePercent &lt;= 100; rng seeded.
        /// Post: true with probability chancePercent/100 (exactly: draw &lt; chance over 0..99); 100 always, 0 never.
        /// Side effects: advances rng by exactly one draw.
        /// </remarks>
        public static bool Roll(int chancePercent, ref Pcg32 rng)
        {
            Contract.Requires(chancePercent >= 0 && chancePercent <= 100, "chance must be 0..100");
            return rng.NextUInt(100) < (uint)chancePercent;
        }
    }
}
