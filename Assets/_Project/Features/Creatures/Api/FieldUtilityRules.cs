using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Creatures
{
    /// <summary>
    /// Aggregated out-of-combat and combat-paired exploration modifiers granted by a team's active field utilities.
    /// REQ: docs/design/field-utilities.md.
    /// </summary>
    /// <remarks>Pre: constructed via <see cref="FieldUtilityRules.ComputeTeamModifiers"/>. Post: immutable value. Side effects: none.</remarks>
    public readonly struct TeamUtilityModifiers
    {
        internal TeamUtilityModifiers(
            int catchRateBonusPercent,
            int dropRateBonusPercent,
            int pickupMultiplier,
            int wildDetectionPermille,
            int moveSpeedPermille,
            bool ignoresTerrainSlow,
            bool preventsWildEscape,
            bool canQuickEscape,
            bool canWeatherControl,
            bool canCookFire,
            bool canCampfireRevive,
            bool canOpenObstacle,
            bool canStealItem,
            bool canScoutPotential,
            bool canDive,
            bool canFishing,
            bool canForageBerries,
            bool canProduceMilk,
            bool canReturnVillage,
            bool hasEggHatchBoost)
        {
            CatchRateBonusPercent = catchRateBonusPercent;
            DropRateBonusPercent = dropRateBonusPercent;
            PickupMultiplier = pickupMultiplier;
            WildDetectionPermille = wildDetectionPermille;
            MoveSpeedPermille = moveSpeedPermille;
            IgnoresTerrainSlow = ignoresTerrainSlow;
            PreventsWildEscape = preventsWildEscape;
            CanQuickEscape = canQuickEscape;
            CanWeatherControl = canWeatherControl;
            CanCookFire = canCookFire;
            CanCampfireRevive = canCampfireRevive;
            CanOpenObstacle = canOpenObstacle;
            CanStealItem = canStealItem;
            CanScoutPotential = canScoutPotential;
            CanDive = canDive;
            CanFishing = canFishing;
            CanForageBerries = canForageBerries;
            CanProduceMilk = canProduceMilk;
            CanReturnVillage = canReturnVillage;
            HasEggHatchBoost = hasEggHatchBoost;
        }

        /// <summary>Flat bonus (percentage points) added to capture rolls.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int CatchRateBonusPercent { get; }

        /// <summary>Flat bonus (percentage points) added to wild knock-out material drop chance.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int DropRateBonusPercent { get; }

        /// <summary>Multiplier applied to dropped material quantity (1 or 2).</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int PickupMultiplier { get; }

        /// <summary>Permille multiplier applied to wild territory detection radius (500 when stealth active, else 1000).</summary>
        /// <remarks>Pre: none. Post: 500 or 1000. Side effects: none.</remarks>
        public int WildDetectionPermille { get; }

        /// <summary>Permille multiplier applied to exploration ground speed (1000, 1250, or 1350).</summary>
        /// <remarks>Pre: none. Post: &gt;= 1000. Side effects: none.</remarks>
        public int MoveSpeedPermille { get; }

        /// <summary>True when levitate, mount, or fly negates terrain movement slowdowns.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool IgnoresTerrainSlow { get; }

        /// <summary>True when <see cref="FieldUtility.PreventEscape"/> extends wild disengage range.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool PreventsWildEscape { get; }

        /// <summary>True when the team can force-disengage from non-boss wild encounters.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanQuickEscape { get; }

        /// <summary>True when the team can actively shift regional weather.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanWeatherControl { get; }

        /// <summary>True when the team can cook gathered ingredients into pre-battle food buffs.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanCookFire { get; }

        /// <summary>True when the team can revive a knocked-out main creature at camp.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanCampfireRevive { get; }

        /// <summary>True when the team can break terrain obstacles and shortcuts.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanOpenObstacle { get; }

        /// <summary>True when the team can steal materials/items from wild targets.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanStealItem { get; }

        /// <summary>True when the team can inspect wild weaknesses and potential before combat.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanScoutPotential { get; }

        /// <summary>True when the team can dive into deep-water POIs.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanDive { get; }

        /// <summary>True when the team can fish aquatic ingredients/materials.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanFishing { get; }

        /// <summary>True when the team can forage healing berries from foliage.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanForageBerries { get; }

        /// <summary>True when the team periodically produces nutritious milk.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanProduceMilk { get; }

        /// <summary>True when the team can fast-travel back to a discovered village or starting town.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanReturnVillage { get; }

        /// <summary>True when the team accelerates egg hatching in the lobby.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool HasEggHatchBoost { get; }
    }

    /// <summary>
    /// Pure rules for each creature's single out-of-combat field utility move or trait.
    /// REQ: docs/design/planet-world.md#進階地形與通行限制, docs/design/creatures.md#隊伍規則, docs/design/field-utilities.md.
    /// </summary>
    /// <remarks>
    /// Pre: valid GameData enums and ISpeciesLookup.
    /// Post: deterministic, allocation-light queries with no UnityEngine dependency.
    /// Side effects: none.
    /// </remarks>
    public static class FieldUtilityRules
    {
        /// <summary>Flat capture chance bonus (percentage points) granted by <see cref="FieldUtility.CatchRateUp"/>.</summary>
        /// <remarks>Pre: none. Post: constant 15. Side effects: none.</remarks>
        public const int CatchRateBonusPercentValue = 15;

        /// <summary>Flat knock-out material drop chance bonus (percentage points) granted by <see cref="FieldUtility.DropRateUp"/>.</summary>
        /// <remarks>Pre: none. Post: constant 25. Side effects: none.</remarks>
        public const int DropRateBonusPercentValue = 25;

        /// <summary>Wild territory detection permille when <see cref="FieldUtility.PartyStealth"/> is active.</summary>
        /// <remarks>Pre: none. Post: constant 500. Side effects: none.</remarks>
        public const int StealthDetectionPermille = 500;

        /// <summary>Ground speed permille when <see cref="FieldUtility.Mount"/> is active.</summary>
        /// <remarks>Pre: none. Post: constant 1350. Side effects: none.</remarks>
        public const int MountSpeedPermille = 1350;

        /// <summary>Ground speed permille when <see cref="FieldUtility.Fly"/> is active.</summary>
        /// <remarks>Pre: none. Post: constant 1250. Side effects: none.</remarks>
        public const int FlySpeedPermille = 1250;

        private static readonly FieldUtility[] TerrainImmunityByElement =
        {
            FieldUtility.ImmuneLaboratory, // Metal = 0
            FieldUtility.ImmuneRainforest, // Wood = 1
            FieldUtility.ImmuneDeepSea,    // Water = 2
            FieldUtility.ImmuneVolcano,    // Fire = 3
            FieldUtility.ImmuneCave,       // Earth = 4
            FieldUtility.ImmuneIceCap,     // Ice = 5
            FieldUtility.ImmuneSpace,      // Wind = 6
            FieldUtility.ImmuneTower,      // Thunder = 7
            FieldUtility.ImmuneTemple,     // Light = 8
            FieldUtility.ImmuneLabyrinth,  // Shadow = 9
        };

        /// <summary>Classifies a <see cref="FieldUtility"/> as an active move, passive trait, or none.</summary>
        /// <remarks>
        /// Pre: <paramref name="utility"/> is a defined <see cref="FieldUtility"/>.
        /// Post: <see cref="FieldUtilityKind.None"/> iff <paramref name="utility"/> == <see cref="FieldUtility.None"/>.
        /// Side effects: none.
        /// </remarks>
        public static FieldUtilityKind GetKind(FieldUtility utility)
        {
            Contract.Requires(Enum.IsDefined(typeof(FieldUtility), utility), "utility must be a defined FieldUtility");
            switch (utility)
            {
                case FieldUtility.None:
                    return FieldUtilityKind.None;

                case FieldUtility.OpenObstacle:
                case FieldUtility.Dive:
                case FieldUtility.Fly:
                case FieldUtility.Levitate:
                case FieldUtility.StealItem:
                case FieldUtility.QuickEscape:
                case FieldUtility.Mount:
                case FieldUtility.ReturnVillage:
                case FieldUtility.PartyStealth:
                case FieldUtility.WeatherControl:
                case FieldUtility.Fishing:
                case FieldUtility.CookFire:
                case FieldUtility.CampfireRevive:
                    return FieldUtilityKind.Move;

                default:
                    return FieldUtilityKind.Trait;
            }
        }

        /// <summary>Returns the terrain-immunity utility required to enter the advanced terrain of <paramref name="element"/>.</summary>
        /// <remarks>
        /// Pre: <paramref name="element"/> is a defined <see cref="Element"/>.
        /// Post: returns one of the ten Immune* utilities.
        /// Side effects: none.
        /// </remarks>
        public static FieldUtility ImmunityForTerrain(Element element)
        {
            Contract.Requires(Enum.IsDefined(typeof(Element), element), "element must be a defined Element");
            return TerrainImmunityByElement[(int)element];
        }

        /// <summary>
        /// Evaluates which field utilities are active for a party of <paramref name="mains"/> and optional <paramref name="subs"/>.
        /// </summary>
        /// <remarks>
        /// Pre: <paramref name="mains"/> != null; <paramref name="species"/> != null; every creature has a valid SpeciesId in <paramref name="species"/>.
        /// Post: returns a deduplicated set of active utilities (never contains <see cref="FieldUtility.None"/>).
        ///       Each main contributes its single utility; each sub contributes its utility only when its vessel matches
        ///       the utility's kind (TalismanCard → Move, GlassBead → Trait; MagicCube provides overload only).
        /// Side effects: allocates and returns a new HashSet.
        /// </remarks>
        public static HashSet<FieldUtility> CollectTeamUtilities(
            IReadOnlyList<OwnedCreature> mains,
            IReadOnlyList<OwnedCreature> subs,
            ISpeciesLookup species)
        {
            Contract.Requires(mains != null, "mains is null");
            Contract.Requires(species != null, "species is null");

            var active = new HashSet<FieldUtility>();

            for (int i = 0; i < mains.Count; i++)
            {
                SpeciesInfo info = RequireSpecies(mains[i].SpeciesId, species);
                if (info.FieldUtility != FieldUtility.None)
                {
                    active.Add(info.FieldUtility);
                }
            }

            if (subs == null)
            {
                return active;
            }

            for (int i = 0; i < subs.Count; i++)
            {
                OwnedCreature sub = subs[i];
                SpeciesInfo info = RequireSpecies(sub.SpeciesId, species);
                if (info.FieldUtility == FieldUtility.None)
                {
                    continue;
                }

                FieldUtilityKind kind = GetKind(info.FieldUtility);
                bool vesselMatches =
                    (sub.Vessel == VesselKind.TalismanCard && kind == FieldUtilityKind.Move) ||
                    (sub.Vessel == VesselKind.GlassBead && kind == FieldUtilityKind.Trait);

                if (vesselMatches)
                {
                    active.Add(info.FieldUtility);
                }
            }

            return active;
        }

        /// <summary>Checks whether <paramref name="activeUtilities"/> grants safe entry to the advanced terrain of <paramref name="terrainElement"/>.</summary>
        /// <remarks>
        /// Pre: <paramref name="activeUtilities"/> != null; <paramref name="terrainElement"/> is a defined <see cref="Element"/>.
        /// Post: true iff the corresponding terrain immunity is present.
        /// Side effects: none.
        /// </remarks>
        public static bool CanEnterAdvancedTerrain(IReadOnlyCollection<FieldUtility> activeUtilities, Element terrainElement)
        {
            Contract.Requires(activeUtilities != null, "activeUtilities is null");
            Contract.Requires(Enum.IsDefined(typeof(Element), terrainElement), "terrainElement must be a defined Element");

            FieldUtility required = ImmunityForTerrain(terrainElement);
            if (activeUtilities is HashSet<FieldUtility> set)
            {
                return set.Contains(required);
            }

            foreach (FieldUtility u in activeUtilities)
            {
                if (u == required)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Maps an advanced terrain id (or its design alias) to its gating <see cref="Element"/>.</summary>
        /// <remarks>
        /// Pre: <paramref name="terrainId"/> may be null or empty.
        /// Post: returns true and sets <paramref name="element"/> iff <paramref name="terrainId"/> is one of the 10 advanced terrains or their aliases.
        /// Side effects: none.
        /// </remarks>
        public static bool TryRequiredElementForTerrain(string terrainId, out Element element)
        {
            if (string.IsNullOrEmpty(terrainId))
            {
                element = default;
                return false;
            }

            switch (terrainId.ToLowerInvariant())
            {
                case "laboratory":
                case "factory":
                case "server_room":
                    element = Element.Metal;
                    return true;
                case "rainforest":
                case "jungle":
                    element = Element.Wood;
                    return true;
                case "deep_sea":
                case "trench":
                    element = Element.Water;
                    return true;
                case "volcano":
                case "caldera":
                    element = Element.Fire;
                    return true;
                case "cave":
                case "mine":
                case "underground":
                    element = Element.Earth;
                    return true;
                case "ice_cap":
                case "permafrost":
                    element = Element.Ice;
                    return true;
                case "space":
                case "floating_island":
                case "space_facility":
                    element = Element.Wind;
                    return true;
                case "tower":
                case "spire":
                case "skyscraper":
                    element = Element.Thunder;
                    return true;
                case "temple":
                case "shrine":
                    element = Element.Light;
                    return true;
                case "labyrinth":
                case "maze":
                    element = Element.Shadow;
                    return true;
                default:
                    element = default;
                    return false;
            }
        }

        /// <summary>Checks whether <paramref name="activeUtilities"/> permits entry into <paramref name="terrainId"/>.</summary>
        /// <remarks>
        /// Pre: <paramref name="activeUtilities"/> != null; <paramref name="terrainId"/> non-empty.
        /// Post: true for all general terrains; for advanced terrains, true iff the matching terrain immunity is active.
        /// Side effects: none.
        /// </remarks>
        public static bool CanEnterTerrain(IReadOnlyCollection<FieldUtility> activeUtilities, string terrainId)
        {
            Contract.Requires(activeUtilities != null, "activeUtilities is null");
            Contract.Requires(!string.IsNullOrEmpty(terrainId), "terrainId is null or empty");

            if (!TryRequiredElementForTerrain(terrainId, out Element requiredElement))
            {
                return true;
            }

            return CanEnterAdvancedTerrain(activeUtilities, requiredElement);
        }

        /// <summary>Computes aggregated exploration and combat-paired modifiers from <paramref name="activeUtilities"/>.</summary>
        /// <remarks>
        /// Pre: <paramref name="activeUtilities"/> != null.
        /// Post: returns deterministic <see cref="TeamUtilityModifiers"/> with valid non-negative bonuses and multipliers.
        /// Side effects: none.
        /// </remarks>
        public static TeamUtilityModifiers ComputeTeamModifiers(IReadOnlyCollection<FieldUtility> activeUtilities)
        {
            Contract.Requires(activeUtilities != null, "activeUtilities is null");

            int catchBonus = 0;
            int dropBonus = 0;
            int pickupMult = 1;
            int detectPermille = 1000;
            int speedPermille = 1000;
            bool ignoreSlow = false;
            bool preventEscape = false;
            bool quickEscape = false;
            bool weatherControl = false;
            bool cookFire = false;
            bool campfireRevive = false;
            bool openObstacle = false;
            bool stealItem = false;
            bool scoutPotential = false;
            bool dive = false;
            bool fishing = false;
            bool forageBerries = false;
            bool produceMilk = false;
            bool returnVillage = false;
            bool eggHatchBoost = false;

            foreach (FieldUtility u in activeUtilities)
            {
                switch (u)
                {
                    case FieldUtility.CatchRateUp: catchBonus = CatchRateBonusPercentValue; break;
                    case FieldUtility.DropRateUp: dropBonus = DropRateBonusPercentValue; break;
                    case FieldUtility.MagnetPickupDouble: pickupMult = 2; break;
                    case FieldUtility.PartyStealth: detectPermille = StealthDetectionPermille; break;
                    case FieldUtility.Mount:
                        if (MountSpeedPermille > speedPermille) speedPermille = MountSpeedPermille;
                        ignoreSlow = true;
                        break;
                    case FieldUtility.Fly:
                        if (FlySpeedPermille > speedPermille) speedPermille = FlySpeedPermille;
                        ignoreSlow = true;
                        break;
                    case FieldUtility.Levitate: ignoreSlow = true; break;
                    case FieldUtility.PreventEscape: preventEscape = true; break;
                    case FieldUtility.QuickEscape: quickEscape = true; break;
                    case FieldUtility.WeatherControl: weatherControl = true; break;
                    case FieldUtility.CookFire: cookFire = true; break;
                    case FieldUtility.CampfireRevive: campfireRevive = true; break;
                    case FieldUtility.OpenObstacle: openObstacle = true; break;
                    case FieldUtility.StealItem: stealItem = true; break;
                    case FieldUtility.ScoutPotential: scoutPotential = true; break;
                    case FieldUtility.Dive: dive = true; break;
                    case FieldUtility.Fishing: fishing = true; break;
                    case FieldUtility.ForageBerries: forageBerries = true; break;
                    case FieldUtility.ProduceMilk: produceMilk = true; break;
                    case FieldUtility.ReturnVillage: returnVillage = true; break;
                    case FieldUtility.EggHatchBoost: eggHatchBoost = true; break;
                }
            }

            return new TeamUtilityModifiers(
                catchBonus,
                dropBonus,
                pickupMult,
                detectPermille,
                speedPermille,
                ignoreSlow,
                preventEscape,
                quickEscape,
                weatherControl,
                cookFire,
                campfireRevive,
                openObstacle,
                stealItem,
                scoutPotential,
                dive,
                fishing,
                forageBerries,
                produceMilk,
                returnVillage,
                eggHatchBoost);
        }

        /// <summary>Checks whether <paramref name="utility"/> aligns thematically and mechanically with <paramref name="element"/>.</summary>
        /// <remarks>
        /// Pre: <paramref name="utility"/> and <paramref name="element"/> are defined enums.
        /// Post: returns true if <paramref name="utility"/> is <see cref="FieldUtility.None"/> or matches the element alignment table in field-utilities.md.
        /// Side effects: none.
        /// </remarks>
        public static bool IsElementAligned(FieldUtility utility, Element element)
        {
            Contract.Requires(Enum.IsDefined(typeof(FieldUtility), utility), "utility must be a defined FieldUtility");
            Contract.Requires(Enum.IsDefined(typeof(Element), element), "element must be a defined Element");

            switch (utility)
            {
                case FieldUtility.None: return true;
                case FieldUtility.ImmuneLaboratory: return element == Element.Metal;
                case FieldUtility.ImmuneRainforest: return element == Element.Wood;
                case FieldUtility.ImmuneDeepSea: return element == Element.Water;
                case FieldUtility.ImmuneVolcano: return element == Element.Fire;
                case FieldUtility.ImmuneCave: return element == Element.Earth;
                case FieldUtility.ImmuneIceCap: return element == Element.Ice;
                case FieldUtility.ImmuneSpace: return element == Element.Wind;
                case FieldUtility.ImmuneTower: return element == Element.Thunder;
                case FieldUtility.ImmuneTemple: return element == Element.Light;
                case FieldUtility.ImmuneLabyrinth: return element == Element.Shadow;
                case FieldUtility.MagnetPickupDouble: return element == Element.Metal || element == Element.Thunder;
                case FieldUtility.CatchRateUp: return element == Element.Metal || element == Element.Light || element == Element.Wood;
                case FieldUtility.ForageBerries: return element == Element.Wood;
                case FieldUtility.ProduceMilk: return element == Element.Wood || element == Element.Earth;
                case FieldUtility.Fishing:
                case FieldUtility.Dive: return element == Element.Water;
                case FieldUtility.CookFire: return element == Element.Fire;
                case FieldUtility.EggHatchBoost:
                case FieldUtility.CampfireRevive: return element == Element.Fire || element == Element.Light;
                case FieldUtility.OpenObstacle: return element == Element.Earth || element == Element.Metal || element == Element.Fire;
                case FieldUtility.DropRateUp: return element == Element.Earth || element == Element.Metal || element == Element.Shadow;
                case FieldUtility.PreventEscape: return element == Element.Ice || element == Element.Earth || element == Element.Shadow;
                case FieldUtility.WeatherControl: return element == Element.Wind || element == Element.Water || element == Element.Fire;
                case FieldUtility.Levitate: return element == Element.Wind || element == Element.Light || element == Element.Thunder;
                case FieldUtility.Fly: return element == Element.Wind || element == Element.Light || element == Element.Fire;
                case FieldUtility.Mount: return element == Element.Thunder || element == Element.Ice || element == Element.Earth || element == Element.Wind;
                case FieldUtility.QuickEscape: return element == Element.Thunder || element == Element.Wind || element == Element.Shadow;
                case FieldUtility.ScoutPotential: return element == Element.Light || element == Element.Metal || element == Element.Thunder;
                case FieldUtility.ReturnVillage: return element == Element.Light || element == Element.Wood || element == Element.Wind;
                case FieldUtility.PartyStealth: return element == Element.Shadow || element == Element.Wood || element == Element.Wind;
                case FieldUtility.StealItem: return element == Element.Shadow || element == Element.Thunder || element == Element.Wind;
                default: return false;
            }
        }

        /// <summary>
        /// Evaluates a pre-battle cooked meal buff when both <see cref="FieldUtility.CookFire"/> and an ingredient utility are active.
        /// </summary>
        /// <remarks>
        /// Pre: <paramref name="activeUtilities"/> != null; <paramref name="ingredientSource"/> is a defined <see cref="FieldUtility"/>.
        /// Post: returns true with non-zero heal and mastery delta iff CookFire and a valid ingredient source (ForageBerries/Fishing/ProduceMilk) are both active.
        /// Side effects: none.
        /// </remarks>
        public static bool TryEvaluateCookedMeal(
            IReadOnlyCollection<FieldUtility> activeUtilities,
            FieldUtility ingredientSource,
            out Element buffElement,
            out int masteryDelta,
            out int healPercent)
        {
            Contract.Requires(activeUtilities != null, "activeUtilities is null");
            Contract.Requires(Enum.IsDefined(typeof(FieldUtility), ingredientSource), "ingredientSource must be a defined FieldUtility");

            TeamUtilityModifiers mods = ComputeTeamModifiers(activeUtilities);
            if (!mods.CanCookFire)
            {
                buffElement = default;
                masteryDelta = 0;
                healPercent = 0;
                return false;
            }

            switch (ingredientSource)
            {
                case FieldUtility.ForageBerries when mods.CanForageBerries:
                    buffElement = Element.Wood;
                    masteryDelta = 12;
                    healPercent = 20;
                    return true;
                case FieldUtility.Fishing when mods.CanFishing:
                    buffElement = Element.Water;
                    masteryDelta = 15;
                    healPercent = 20;
                    return true;
                case FieldUtility.ProduceMilk when mods.CanProduceMilk:
                    buffElement = Element.Earth;
                    masteryDelta = 12;
                    healPercent = 25;
                    return true;
                default:
                    buffElement = default;
                    masteryDelta = 0;
                    healPercent = 0;
                    return false;
            }
        }

        private static SpeciesInfo RequireSpecies(string speciesId, ISpeciesLookup species)
        {
            if (!species.TryGet(speciesId, out SpeciesInfo info))
            {
                throw new InvalidOperationException("Species '" + speciesId + "' not found in ISpeciesLookup.");
            }

            return info;
        }
    }
}
