using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Creatures
{
    /// <summary>
    /// Pure rules for each creature's single out-of-combat field utility move or trait.
    /// REQ: docs/design/planet-world.md#進階地形與通行限制, docs/design/creatures.md#隊伍規則.
    /// </summary>
    /// <remarks>
    /// Pre: valid GameData enums and ISpeciesLookup.
    /// Post: deterministic, allocation-light queries with no UnityEngine dependency.
    /// Side effects: none.
    /// </remarks>
    public static class FieldUtilityRules
    {
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
