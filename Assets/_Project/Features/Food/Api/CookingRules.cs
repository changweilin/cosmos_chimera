using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;

namespace Cosmimera.Food
{
    /// <summary>Where cooking is performed.</summary>
    public enum CookingStationKind
    {
        /// <summary>Wilderness/field cooking; requires matching life skills.</summary>
        Field = 0,
        /// <summary>Settlement/camp kitchen; all 7 cooking methods are unlocked.</summary>
        Kitchen = 1,
    }

    /// <summary>Quality tier achieved when preparing a dish.</summary>
    public enum CookingQuality
    {
        /// <summary>Standard dish with base recipe stats.</summary>
        Normal = 0,
        /// <summary>Enhanced dish (+20% buff duration, +5% heal bonus).</summary>
        Tasty = 1,
        /// <summary>Masterwork dish (+40% buff duration, +10% heal bonus, +1 output quantity).</summary>
        Masterwork = 2,
    }

    /// <summary>Why a cooking attempt was accepted or refused.</summary>
    public enum CookRefusalReason
    {
        /// <summary>Cooking succeeded.</summary>
        None = 0,
        /// <summary>Recipe output food id is missing from the catalog.</summary>
        UnknownOutputFood = 1,
        /// <summary>Field cooking requires <see cref="FieldUtility.CookFire"/> or <see cref="FieldUtility.CulinaryPrep"/>.</summary>
        MissingCookingSkill = 2,
        /// <summary>Inventory lacks one or more required ingredients.</summary>
        MissingIngredients = 3,
    }

    /// <summary>Team and station context for evaluating cooking.</summary>
    /// <remarks>Pre: valid TeamUtilityModifiers. Post: immutable. Side effects: none.</remarks>
    public readonly struct CookingContext
    {
        /// <summary>Creates a cooking context.</summary>
        /// <remarks>
        /// Pre: station is a defined CookingStationKind; teamModifiers valid; partyElements may be null (treated as empty).
        /// Post: properties equal arguments.
        /// Side effects: none.
        /// </remarks>
        public CookingContext(
            CookingStationKind station,
            in TeamUtilityModifiers teamModifiers,
            IReadOnlyCollection<Element> partyElements = null)
        {
            Contract.Requires(Enum.IsDefined(typeof(CookingStationKind), station), "station must be defined");
            Station = station;
            TeamModifiers = teamModifiers;
            PartyElements = partyElements;
        }

        /// <summary>Station kind (Field or Kitchen).</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public CookingStationKind Station { get; }

        /// <summary>Active party utility modifiers.</summary>
        /// <remarks>Pre: none. Post: valid struct. Side effects: none.</remarks>
        public TeamUtilityModifiers TeamModifiers { get; }

        /// <summary>Elements present in the current party for synergy quality bonuses.</summary>
        /// <remarks>Pre: none. Post: may be null. Side effects: none.</remarks>
        public IReadOnlyCollection<Element> PartyElements { get; }
    }

    /// <summary>Result of a successful cooking operation.</summary>
    /// <remarks>Pre: constructed by <see cref="CookingRules.TryCook"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct CookOutcome
    {
        internal CookOutcome(FoodItemSpec food, int quantityProduced, CookingQuality quality)
        {
            Food = food;
            QuantityProduced = quantityProduced;
            Quality = quality;
        }

        /// <summary>Produced food item definition.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public FoodItemSpec Food { get; }

        /// <summary>Total units added to inventory (&gt;= 1).</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int QuantityProduced { get; }

        /// <summary>Quality tier achieved.</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public CookingQuality Quality { get; }
    }

    /// <summary>
    /// Pure rules for preparing food via all 7 cooking methods:
    /// Grill, Stew, StirFry, DeepFry, Steam, ColdMix, Grind.
    /// </summary>
    /// <remarks>Pre: valid catalog, recipe, inventory, context. Post: deterministic crafting. Side effects: mutates inventory on TryCook.</remarks>
    public static class CookingRules
    {
        /// <summary>Checks whether <paramref name="method"/> is unlocked in <paramref name="context"/>.</summary>
        /// <remarks>
        /// Pre: method is a defined CookingMethod.
        /// Post: true at Kitchen; in Field, heat methods require CookFire, ColdMix/Grind require CulinaryPrep, Steam accepts either.
        /// Side effects: none.
        /// </remarks>
        public static bool HasSkillForMethod(CookingMethod method, in CookingContext context)
        {
            Contract.Requires(Enum.IsDefined(typeof(CookingMethod), method), "method must be defined");
            if (context.Station == CookingStationKind.Kitchen)
            {
                return true;
            }

            switch (method)
            {
                case CookingMethod.Grill:
                case CookingMethod.Stew:
                case CookingMethod.StirFry:
                case CookingMethod.DeepFry:
                    return context.TeamModifiers.CanCookFire;

                case CookingMethod.Steam:
                    return context.TeamModifiers.CanCookFire || context.TeamModifiers.CanCulinaryPrep;

                case CookingMethod.ColdMix:
                case CookingMethod.Grind:
                    return context.TeamModifiers.CanCulinaryPrep;

                default:
                    return false;
            }
        }

        /// <summary>Evaluates whether <paramref name="recipe"/> can be cooked right now.</summary>
        /// <remarks>
        /// Pre: recipe != null; catalog != null; inventory != null.
        /// Post: returns None iff output food exists, method skill is satisfied, and inventory holds required ingredients.
        /// Side effects: none.
        /// </remarks>
        public static CookRefusalReason CanCook(
            RecipeSpec recipe,
            FoodCatalog catalog,
            FoodInventory inventory,
            in CookingContext context)
        {
            Contract.Requires(recipe != null, "recipe must not be null");
            Contract.Requires(catalog != null, "catalog must not be null");
            Contract.Requires(inventory != null, "inventory must not be null");

            if (!catalog.TryGetFood(recipe.OutputFoodId, out _))
            {
                return CookRefusalReason.UnknownOutputFood;
            }

            if (!HasSkillForMethod(recipe.Method, context))
            {
                return CookRefusalReason.MissingCookingSkill;
            }

            if (!inventory.HasIngredientsFor(recipe, catalog))
            {
                return CookRefusalReason.MissingIngredients;
            }

            return CookRefusalReason.None;
        }

        /// <summary>Computes the quality tier for <paramref name="recipe"/> in <paramref name="context"/>.</summary>
        /// <remarks>
        /// Pre: recipe != null.
        /// Post: Masterwork when both CookFire and CulinaryPrep (or Kitchen + both / element affinity + culinary skill) apply;
        ///       Tasty when one synergy applies; Normal otherwise.
        /// Side effects: none.
        /// </remarks>
        public static CookingQuality EvaluateQuality(RecipeSpec recipe, in CookingContext context)
        {
            Contract.Requires(recipe != null, "recipe must not be null");

            bool hasFire = context.TeamModifiers.CanCookFire;
            bool hasPrep = context.TeamModifiers.CanCulinaryPrep;
            bool hasElement = HasElement(context.PartyElements, recipe.ElementAffinity);

            int score = 0;
            if (context.Station == CookingStationKind.Kitchen) score++;
            if (hasFire && hasPrep) score += 2;
            else if (hasFire || hasPrep) score++;
            if (hasElement) score++;

            if (score >= 3) return CookingQuality.Masterwork;
            if (score >= 2) return CookingQuality.Tasty;
            return CookingQuality.Normal;
        }

        /// <summary>Attempts to cook <paramref name="recipe"/>, atomically consuming ingredients and adding produced food.</summary>
        /// <remarks>
        /// Pre: recipe != null; catalog != null; inventory != null.
        /// Post: returns None and updates inventory/outcome on success; on refusal, inventory is unchanged.
        /// Side effects: mutates inventory on success.
        /// </remarks>
        public static CookRefusalReason TryCook(
            RecipeSpec recipe,
            FoodCatalog catalog,
            FoodInventory inventory,
            in CookingContext context,
            out CookOutcome outcome)
        {
            Contract.Requires(recipe != null, "recipe must not be null");
            Contract.Requires(catalog != null, "catalog must not be null");
            Contract.Requires(inventory != null, "inventory must not be null");

            CookRefusalReason refusal = CanCook(recipe, catalog, inventory, context);
            if (refusal != CookRefusalReason.None)
            {
                outcome = default;
                return refusal;
            }

            if (!catalog.TryGetFood(recipe.OutputFoodId, out FoodItemSpec food))
            {
                outcome = default;
                return CookRefusalReason.UnknownOutputFood;
            }

            bool consumed = inventory.TryConsumeForRecipe(recipe, catalog);
            Contract.Requires(consumed, "CanCook passed so TryConsumeForRecipe must succeed");

            CookingQuality quality = EvaluateQuality(recipe, context);
            int qty = recipe.OutputQuantity + (quality == CookingQuality.Masterwork ? 1 : 0);
            inventory.AddFood(food.Id, qty);

            outcome = new CookOutcome(food, qty, quality);
            return CookRefusalReason.None;
        }

        /// <summary>Returns the scaled buff duration in milliseconds for <paramref name="quality"/>, clamped to EffectBook.MaxDurationMs.</summary>
        /// <remarks>
        /// Pre: baseDurationMs &gt;= 0.
        /// Post: 0 if baseDurationMs == 0; otherwise scaled by 100% / 120% / 140% clamped to MaxDurationMs.
        /// Side effects: none.
        /// </remarks>
        public static int ScaleDurationMs(int baseDurationMs, CookingQuality quality)
        {
            Contract.Requires(baseDurationMs >= 0, "baseDurationMs must be >= 0");
            Contract.Requires(Enum.IsDefined(typeof(CookingQuality), quality), "quality must be defined");
            if (baseDurationMs == 0)
            {
                return 0;
            }

            long pct = quality == CookingQuality.Masterwork ? 140L : quality == CookingQuality.Tasty ? 120L : 100L;
            long scaled = (baseDurationMs * pct + 50L) / 100L;
            return scaled > Combat.EffectBook.MaxDurationMs ? Combat.EffectBook.MaxDurationMs : (int)scaled;
        }

        /// <summary>Returns the quality-adjusted heal percentage clamped to 0..100.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= basePercent &lt;= 100.
        /// Post: 0 if basePercent == 0; otherwise basePercent + 0/5/10 clamped to 100.
        /// Side effects: none.
        /// </remarks>
        public static int ScaleHealPercent(int basePercent, CookingQuality quality)
        {
            Contract.Requires(basePercent >= 0 && basePercent <= 100, "basePercent must be 0..100");
            Contract.Requires(Enum.IsDefined(typeof(CookingQuality), quality), "quality must be defined");
            if (basePercent == 0)
            {
                return 0;
            }

            int bonus = quality == CookingQuality.Masterwork ? 10 : quality == CookingQuality.Tasty ? 5 : 0;
            int total = basePercent + bonus;
            return total > 100 ? 100 : total;
        }

        private static bool HasElement(IReadOnlyCollection<Element> elements, Element target)
        {
            if (elements == null)
            {
                return false;
            }

            foreach (Element e in elements)
            {
                if (e == target)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
