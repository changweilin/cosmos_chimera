using System.Collections.Generic;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;

namespace Cosmimera.Food
{
    /// <summary>Why a food consumption attempt was accepted or refused.</summary>
    public enum ConsumeRefusalReason
    {
        /// <summary>Food was consumed and effects applied.</summary>
        None = 0,
        /// <summary>Food or ingredient id is not registered in the catalog.</summary>
        UnknownItem = 1,
        /// <summary>Item is not edible (e.g. inedible raw ingredient).</summary>
        NotEdible = 2,
        /// <summary>Inventory does not hold at least 1 unit.</summary>
        OutOfStock = 3,
        /// <summary>No eligible target was provided or alive/revivable for this food.</summary>
        NoEligibleTarget = 4,
    }

    /// <summary>Summary of effects applied when consuming a food item.</summary>
    /// <remarks>Pre: returned by <see cref="FoodConsumptionRules"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct ConsumeOutcome
    {
        internal ConsumeOutcome(int playerHpRestored, int creatureHpRestored, int creaturesRevived, int effectsCleansed, int combatBuffsApplied)
        {
            PlayerHpRestored = playerHpRestored;
            CreatureHpRestored = creatureHpRestored;
            CreaturesRevived = creaturesRevived;
            EffectsCleansed = effectsCleansed;
            CombatBuffsApplied = combatBuffsApplied;
        }

        /// <summary>Total HP restored to the player character.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int PlayerHpRestored { get; }

        /// <summary>Total HP restored across target creatures.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int CreatureHpRestored { get; }

        /// <summary>Number of fainted creatures revived.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int CreaturesRevived { get; }

        /// <summary>Number of negative timed effects cleansed.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int EffectsCleansed { get; }

        /// <summary>Number of timed combat effects applied to target combatants.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int CombatBuffsApplied { get; }
    }

    /// <summary>
    /// Pure rules for consuming prepared food or edible raw ingredients, applying healing, revival,
    /// debuff cleansing, player exploration buffs, and creature combat buffs.
    /// </summary>
    /// <remarks>Pre: valid catalog, inventory, targets, and CombatTime nowMs. Post: deterministic state updates. Side effects: mutates inventory, vitals, combatants, buffs.</remarks>
    public static class FoodConsumptionRules
    {
        /// <summary>Consumes a prepared food item <paramref name="foodId"/> and applies its effects to player and/or party.</summary>
        /// <remarks>
        /// Pre: foodId non-empty; catalog != null; inventory != null; nowMs in CombatTime;
        ///      at least one of player or creatures non-null; if activeBuffs != null, its watermark &lt;= nowMs.
        /// Post: returns None, consumes 1 unit, and applies effects on success; on refusal, inventory and targets are unchanged.
        /// Side effects: mutates inventory, player, creatures, activeBuffs on success.
        /// </remarks>
        public static ConsumeRefusalReason TryEatFood(
            string foodId,
            FoodCatalog catalog,
            FoodInventory inventory,
            PlayerVitals player,
            IReadOnlyList<Combatant> creatures,
            ActiveFoodBuffs activeBuffs,
            long nowMs,
            out ConsumeOutcome outcome,
            CookingQuality quality = CookingQuality.Normal)
        {
            Contract.Requires(!string.IsNullOrEmpty(foodId), "foodId must be non-empty");
            Contract.Requires(catalog != null, "catalog must not be null");
            Contract.Requires(inventory != null, "inventory must not be null");
            Contract.Requires(nowMs >= 0 && nowMs <= CombatTime.MaxMs, "nowMs must be in CombatTime");
            Contract.Requires(player != null || (creatures != null && creatures.Count > 0), "at least one target must be provided");

            if (!catalog.TryGetFood(foodId, out FoodItemSpec food))
            {
                outcome = default;
                return ConsumeRefusalReason.UnknownItem;
            }

            if (inventory.CountFood(foodId) < 1)
            {
                outcome = default;
                return ConsumeRefusalReason.OutOfStock;
            }

            FoodEffectSpec eff = food.Effect;
            bool hasCreatureTarget = HasEligibleCreature(creatures, eff);
            bool hasPlayerTarget = player != null && !player.IsDowned;

            if (!hasCreatureTarget && !hasPlayerTarget)
            {
                outcome = default;
                return ConsumeRefusalReason.NoEligibleTarget;
            }

            bool consumed = inventory.TryConsumeFood(foodId, 1);
            Contract.Requires(consumed, "CountFood >= 1 so TryConsumeFood must succeed");

            int playerHealed = 0;
            if (hasPlayerTarget && eff.PlayerHealPercent > 0)
            {
                int scaledPlayerHeal = CookingRules.ScaleHealPercent(eff.PlayerHealPercent, quality);
                playerHealed = player.HealPercent(scaledPlayerHeal, nowMs);
            }

            int creatureHealed = 0;
            int revived = 0;
            int cleansed = 0;
            int buffsApplied = 0;

            int scaledCreatureHeal = CookingRules.ScaleHealPercent(eff.CreatureHealPercent, quality);
            int scaledDuration = CookingRules.ScaleDurationMs(eff.DurationMs, quality);

            if (creatures != null)
            {
                for (int i = 0; i < creatures.Count; i++)
                {
                    Combatant c = creatures[i];
                    if (c == null)
                    {
                        continue;
                    }

                    if (c.IsFainted)
                    {
                        if (eff.RevivePercent > 0)
                        {
                            c.ReviveByPercent(eff.RevivePercent, nowMs);
                            revived++;
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else if (scaledCreatureHeal > 0)
                    {
                        creatureHealed += c.HealByPercent(scaledCreatureHeal, nowMs);
                    }

                    if (eff.CleanseNegativeEffects)
                    {
                        cleansed += c.CleanseNegativeEffects(nowMs);
                    }

                    if (scaledDuration > 0 && eff.CombatEffects.Count > 0)
                    {
                        for (int e = 0; e < eff.CombatEffects.Count; e++)
                        {
                            c.Book.Apply(food.Id, e, eff.CombatEffects[e], scaledDuration, nowMs);
                            buffsApplied++;
                        }
                    }
                }
            }

            if (activeBuffs != null && eff.HasTimedBuff && scaledDuration > 0)
            {
                activeBuffs.Apply(food.Id, eff, scaledDuration, nowMs);
            }

            outcome = new ConsumeOutcome(playerHealed, creatureHealed, revived, cleansed, buffsApplied);
            return ConsumeRefusalReason.None;
        }

        /// <summary>Consumes an edible raw ingredient <paramref name="ingredientId"/> for basic HP restoration.</summary>
        /// <remarks>
        /// Pre: ingredientId non-empty; catalog != null; inventory != null; nowMs in CombatTime;
        ///      at least one of player or creature non-null.
        /// Post: returns None and heals target(s) by RawHealPercent iff ingredient is edible and in stock; else inventory unchanged.
        /// Side effects: mutates inventory, player, creature on success.
        /// </remarks>
        public static ConsumeRefusalReason TryEatRawIngredient(
            string ingredientId,
            FoodCatalog catalog,
            FoodInventory inventory,
            PlayerVitals player,
            Combatant creature,
            long nowMs,
            out ConsumeOutcome outcome)
        {
            Contract.Requires(!string.IsNullOrEmpty(ingredientId), "ingredientId must be non-empty");
            Contract.Requires(catalog != null, "catalog must not be null");
            Contract.Requires(inventory != null, "inventory must not be null");
            Contract.Requires(nowMs >= 0 && nowMs <= CombatTime.MaxMs, "nowMs must be in CombatTime");
            Contract.Requires(player != null || creature != null, "at least one target must be provided");

            if (!catalog.TryGetIngredient(ingredientId, out IngredientSpec ing))
            {
                outcome = default;
                return ConsumeRefusalReason.UnknownItem;
            }

            if (!ing.IsEdibleRaw)
            {
                outcome = default;
                return ConsumeRefusalReason.NotEdible;
            }

            if (inventory.CountIngredient(ingredientId) < 1)
            {
                outcome = default;
                return ConsumeRefusalReason.OutOfStock;
            }

            bool playerOk = player != null && !player.IsDowned;
            bool creatureOk = creature != null && !creature.IsFainted;
            if (!playerOk && !creatureOk)
            {
                outcome = default;
                return ConsumeRefusalReason.NoEligibleTarget;
            }

            bool consumed = inventory.TryConsumeIngredient(ingredientId, 1);
            Contract.Requires(consumed, "CountIngredient >= 1 so TryConsumeIngredient must succeed");

            int playerHealed = playerOk ? player.HealPercent(ing.RawHealPercent, nowMs) : 0;
            int creatureHealed = creatureOk ? creature.HealByPercent(ing.RawHealPercent, nowMs) : 0;

            outcome = new ConsumeOutcome(playerHealed, creatureHealed, 0, 0, 0);
            return ConsumeRefusalReason.None;
        }

        private static bool HasEligibleCreature(IReadOnlyList<Combatant> creatures, FoodEffectSpec eff)
        {
            if (creatures == null)
            {
                return false;
            }

            for (int i = 0; i < creatures.Count; i++)
            {
                Combatant c = creatures[i];
                if (c == null)
                {
                    continue;
                }

                if (!c.IsFainted || eff.RevivePercent > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
