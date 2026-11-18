using System;
using System.Collections.Generic;
using Cosmimera.Capture;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;

namespace Cosmimera.Crafting
{
    /// <summary>Material tiers for D0-U4 (cloth: hemp→cotton→silk; metal: copper→iron→silver/gold).</summary>
    public enum CraftMaterialTier
    {
        Tier1 = 1,
        Tier2 = 2,
        Tier3 = 3,
    }

    /// <summary>Seven crafting life-skill categories (docs/design/crafting.md#製造擴充).</summary>
    public enum CraftSkillCategory
    {
        Smelt = 0,
        Weave = 1,
        Tailor = 2,
        Smith = 3,
        Carpentry = 4,
        Jewelcraft = 5,
        Dye = 6,
    }

    /// <summary>Quality grade inherited from primary crafting material.</summary>
    public enum ItemQuality
    {
        Common = 0,
        Fine = 1,
        Masterwork = 2,
        Mythic = 3,
    }

    /// <summary>Reproducible affix rolled from item quality and planet/craft seed.</summary>
    public readonly struct CraftAffix
    {
        public CraftAffix(string statId, int bonusPercent)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(statId), "statId required");
            StatId = statId;
            BonusPercent = bonusPercent;
        }

        public string StatId { get; }
        public int BonusPercent { get; }
    }

    /// <summary>Durability tracker for D0-U4 equipment; broken items keep their slot with zero bonuses.</summary>
    public struct DurabilityState
    {
        public DurabilityState(int max)
        {
            Contract.Requires(max >= 1, "max durability must be >= 1");
            Max = max;
            Current = max;
        }

        public int Current { get; private set; }
        public int Max { get; }
        public bool IsBroken => Current <= 0;

        /// <summary>Reduces durability by <paramref name="amount"/>, clamped at 0.</summary>
        /// <remarks>Pre: amount >= 0. Post: 0 &lt;= Current &lt;= Max. Side effects: mutates Current.</remarks>
        public void Degrade(int amount)
        {
            Contract.Requires(amount >= 0, "amount must be non-negative");
            Current = Math.Max(0, Current - amount);
        }

        /// <summary>Restores durability to Max.</summary>
        /// <remarks>Pre: none. Post: Current == Max. Side effects: mutates Current.</remarks>
        public void Repair() => Current = Max;
    }

    /// <summary>Pure D0-U4 rules for crafting categories, fallback utilities, and reproducible quality affixes.</summary>
    public static class CraftingExpansionRules
    {
        private static readonly string[] AffixPool = { "str", "agi", "int", "spi", "vit", "crit_percent" };

        /// <summary>Maps a crafting category to its interim FieldUtility before P5 species roll out.</summary>
        /// <remarks>Pre: category defined. Post: valid FieldUtility. Side effects: none.</remarks>
        public static FieldUtility FallbackUtility(CraftSkillCategory category) => category switch
        {
            CraftSkillCategory.Smelt => FieldUtility.CookFire,
            CraftSkillCategory.Weave => FieldUtility.ForageBerries,
            CraftSkillCategory.Tailor => FieldUtility.ForageBerries,
            CraftSkillCategory.Carpentry => FieldUtility.ForageBerries,
            CraftSkillCategory.Dye => FieldUtility.ForageBerries,
            CraftSkillCategory.Smith => FieldUtility.MagnetPickupDouble,
            CraftSkillCategory.Jewelcraft => FieldUtility.ScoutPotential,
            _ => FieldUtility.CookFire,
        };

        /// <summary>True when a sub-partner with <paramref name="vessel"/> can supply a crafting category skill.</summary>
        /// <remarks>REQ: crafting.md#製造擴充 — magic cube sub-partners never provide crafting skills.</remarks>
        public static bool VesselSupportsCraftingSkill(VesselKind vessel) =>
            vessel == VesselKind.GlassOrb || vessel == VesselKind.TalismanCard;

        /// <summary>Number of random affixes rolled for <paramref name="quality"/>.</summary>
        /// <remarks>Pre: none. Post: 0..3. Side effects: none.</remarks>
        public static int AffixCountFor(ItemQuality quality) => quality switch
        {
            ItemQuality.Common => 0,
            ItemQuality.Fine => 1,
            ItemQuality.Masterwork => 2,
            ItemQuality.Mythic => 3,
            _ => 0,
        };

        /// <summary>Deterministically rolls affixes for an item from <paramref name="seed"/> and <paramref name="quality"/>.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: returned list length == AffixCountFor(quality), reproducible for identical (seed, quality).
        /// Side effects: allocates result list.
        /// </remarks>
        public static List<CraftAffix> RollAffixes(ulong seed, ItemQuality quality)
        {
            int count = AffixCountFor(quality);
            var result = new List<CraftAffix>(count);
            var rng = new Pcg32(seed ^ ((ulong)quality + 0x9E3779B97F4A7C15UL));
            for (int i = 0; i < count; i++)
            {
                string stat = AffixPool[(int)rng.NextUInt((uint)AffixPool.Length)];
                int bonus = 2 + (int)rng.NextUInt(5) + (int)quality * 2;
                result.Add(new CraftAffix(stat, bonus));
            }

            return result;
        }
    }
}
