using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Combat
{
    /// <summary>Which life skills harvest which object substances (destructibles.md#傷害, Q-C76/Q-C77).</summary>
    public static class HarvestRules
    {
        /// <summary>Whether <paramref name="utility"/> harvests objects whose dominant substance is <paramref name="substanceId"/>.</summary>
        /// <remarks>
        /// REQ: destructibles.md#傷害 — magnet_pickup_double: metal; drop_rate_up: stone, crystal; forage_berries: wood, plant.
        /// Pre: substanceId non-null. Post: true only for the table's pairs. Side effects: none.
        /// </remarks>
        public static bool Harvests(FieldUtility utility, string substanceId)
        {
            Contract.Requires(substanceId != null, "substanceId must not be null");
            switch (utility)
            {
                case FieldUtility.MagnetPickupDouble: return substanceId == "metal";
                case FieldUtility.DropRateUp: return substanceId == "stone" || substanceId == "crystal";
                case FieldUtility.ForageBerries: return substanceId == "wood" || substanceId == "plant";
                default: return false;
            }
        }

        /// <summary>Whether any of <paramref name="utilities"/> harvests <paramref name="substanceId"/>.</summary>
        /// <remarks>Pre: utilities and substanceId non-null. Post: true iff some element satisfies Harvests. Side effects: none.</remarks>
        public static bool AnyHarvests(IReadOnlyList<FieldUtility> utilities, string substanceId)
        {
            Contract.Requires(utilities != null, "utilities must not be null");
            for (int i = 0; i < utilities.Count; i++)
            {
                if (Harvests(utilities[i], substanceId)) return true;
            }

            return false;
        }
    }
}
