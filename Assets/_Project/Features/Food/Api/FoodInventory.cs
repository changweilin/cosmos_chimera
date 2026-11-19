using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Food
{
    /// <summary>
    /// Run-scoped bag holding raw ingredients and prepared food items.
    /// REQ: docs/design/field-utilities.md, docs/design/lobby-meta.md (Q-M4 run-scoped consumables).
    /// </summary>
    /// <remarks>Pre: valid ids and positive quantities. Post: counts are always &gt;= 0. Side effects: instance mutation only.</remarks>
    public sealed class FoodInventory
    {
        private readonly Dictionary<string, int> _ingredients = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _foods = new Dictionary<string, int>();

        /// <summary>Current count of <paramref name="ingredientId"/>.</summary>
        /// <remarks>Pre: ingredientId != null. Post: &gt;= 0. Side effects: none.</remarks>
        public int CountIngredient(string ingredientId)
        {
            Contract.Requires(ingredientId != null, "ingredientId must not be null");
            return _ingredients.TryGetValue(ingredientId, out int count) ? count : 0;
        }

        /// <summary>Current count of <paramref name="foodId"/>.</summary>
        /// <remarks>Pre: foodId != null. Post: &gt;= 0. Side effects: none.</remarks>
        public int CountFood(string foodId)
        {
            Contract.Requires(foodId != null, "foodId must not be null");
            return _foods.TryGetValue(foodId, out int count) ? count : 0;
        }

        /// <summary>Adds <paramref name="quantity"/> units of <paramref name="ingredientId"/>, saturating at int.MaxValue.</summary>
        /// <remarks>
        /// Pre: ingredientId non-empty; quantity &gt;= 1.
        /// Post: CountIngredient(ingredientId) increased by up to quantity without overflow.
        /// Side effects: mutates ingredient stock.
        /// </remarks>
        public void AddIngredient(string ingredientId, int quantity)
        {
            Contract.Requires(!string.IsNullOrEmpty(ingredientId), "ingredientId must be non-empty");
            Contract.Requires(quantity >= 1, "quantity must be >= 1");

            long sum = (long)CountIngredient(ingredientId) + quantity;
            _ingredients[ingredientId] = sum > int.MaxValue ? int.MaxValue : (int)sum;
        }

        /// <summary>Adds <paramref name="quantity"/> units of <paramref name="foodId"/>, saturating at int.MaxValue.</summary>
        /// <remarks>
        /// Pre: foodId non-empty; quantity &gt;= 1.
        /// Post: CountFood(foodId) increased by up to quantity without overflow.
        /// Side effects: mutates food stock.
        /// </remarks>
        public void AddFood(string foodId, int quantity)
        {
            Contract.Requires(!string.IsNullOrEmpty(foodId), "foodId must be non-empty");
            Contract.Requires(quantity >= 1, "quantity must be >= 1");

            long sum = (long)CountFood(foodId) + quantity;
            _foods[foodId] = sum > int.MaxValue ? int.MaxValue : (int)sum;
        }

        /// <summary>Consumes <paramref name="quantity"/> units of <paramref name="ingredientId"/> if available.</summary>
        /// <remarks>
        /// Pre: ingredientId non-empty; quantity &gt;= 1.
        /// Post: returns true and decrements stock iff at least quantity was held; false leaves stock unchanged.
        /// Side effects: mutates ingredient stock on true.
        /// </remarks>
        public bool TryConsumeIngredient(string ingredientId, int quantity)
        {
            Contract.Requires(!string.IsNullOrEmpty(ingredientId), "ingredientId must be non-empty");
            Contract.Requires(quantity >= 1, "quantity must be >= 1");

            int held = CountIngredient(ingredientId);
            if (held < quantity)
            {
                return false;
            }

            int remaining = held - quantity;
            if (remaining == 0)
            {
                _ingredients.Remove(ingredientId);
            }
            else
            {
                _ingredients[ingredientId] = remaining;
            }

            return true;
        }

        /// <summary>Consumes <paramref name="quantity"/> units of <paramref name="foodId"/> if available.</summary>
        /// <remarks>
        /// Pre: foodId non-empty; quantity &gt;= 1.
        /// Post: returns true and decrements stock iff at least quantity was held; false leaves stock unchanged.
        /// Side effects: mutates food stock on true.
        /// </remarks>
        public bool TryConsumeFood(string foodId, int quantity = 1)
        {
            Contract.Requires(!string.IsNullOrEmpty(foodId), "foodId must be non-empty");
            Contract.Requires(quantity >= 1, "quantity must be >= 1");

            int held = CountFood(foodId);
            if (held < quantity)
            {
                return false;
            }

            int remaining = held - quantity;
            if (remaining == 0)
            {
                _foods.Remove(foodId);
            }
            else
            {
                _foods[foodId] = remaining;
            }

            return true;
        }

        /// <summary>Checks whether all ingredient requirements of <paramref name="recipe"/> can be satisfied.</summary>
        /// <remarks>
        /// Pre: recipe != null; catalog != null.
        /// Post: true iff sufficient ingredients exist (exact ids reserved before category wildcards).
        /// Side effects: allocates small temporary scratch dictionary.
        /// </remarks>
        public bool HasIngredientsFor(RecipeSpec recipe, FoodCatalog catalog)
        {
            Contract.Requires(recipe != null, "recipe must not be null");
            Contract.Requires(catalog != null, "catalog must not be null");

            return TryPlanConsumption(recipe, catalog, null);
        }

        /// <summary>Atomically consumes all ingredients required by <paramref name="recipe"/>.</summary>
        /// <remarks>
        /// Pre: recipe != null; catalog != null.
        /// Post: returns true and decrements all consumed ingredients iff HasIngredientsFor was true; false leaves inventory unchanged.
        /// Side effects: mutates ingredient stock on true.
        /// </remarks>
        public bool TryConsumeForRecipe(RecipeSpec recipe, FoodCatalog catalog)
        {
            return TryConsumeForRecipe(recipe, catalog, new Dictionary<string, int>());
        }

        /// <summary>Same as <see cref="TryConsumeForRecipe(RecipeSpec, FoodCatalog)"/>, reporting the consumed ids.</summary>
        /// <remarks>
        /// Pre: recipe, catalog, plan non-null.
        /// Post: plan holds exactly the consumed id → quantity map on true; content unspecified on false.
        /// Side effects: mutates ingredient stock on true; overwrites plan.
        /// </remarks>
        internal bool TryConsumeForRecipe(RecipeSpec recipe, FoodCatalog catalog, Dictionary<string, int> plan)
        {
            Contract.Requires(recipe != null, "recipe must not be null");
            Contract.Requires(catalog != null, "catalog must not be null");
            Contract.Requires(plan != null, "plan must not be null");

            if (!TryPlanConsumption(recipe, catalog, plan))
            {
                return false;
            }

            foreach (KeyValuePair<string, int> kv in plan)
            {
                bool ok = TryConsumeIngredient(kv.Key, kv.Value);
                Contract.Requires(ok, "planned consumption must succeed");
            }

            return true;
        }

        /// <summary>Empties all ingredients and cooked food (e.g. at run end).</summary>
        /// <remarks>Pre: none. Post: all counts == 0. Side effects: clears internal dictionaries.</remarks>
        public void Clear()
        {
            _ingredients.Clear();
            _foods.Clear();
        }

        private bool TryPlanConsumption(RecipeSpec recipe, FoodCatalog catalog, Dictionary<string, int> outPlan)
        {
            var reserved = outPlan ?? new Dictionary<string, int>();
            reserved.Clear();

            // Pass 1: exact ingredient id requirements first so category wildcards cannot steal them.
            for (int i = 0; i < recipe.Requirements.Count; i++)
            {
                RecipeIngredientRequirement req = recipe.Requirements[i];
                if (req.IsCategoryMatch)
                {
                    continue;
                }

                int alreadyReserved = reserved.TryGetValue(req.IngredientId, out int r) ? r : 0;
                if (CountIngredient(req.IngredientId) - alreadyReserved < req.Quantity)
                {
                    return false;
                }

                reserved[req.IngredientId] = alreadyReserved + req.Quantity;
            }

            // Pass 2: category wildcard requirements, preferring raw over processed goods (the player spent a skill on those),
            // then lower tier, then deterministic catalog order.
            for (int i = 0; i < recipe.Requirements.Count; i++)
            {
                RecipeIngredientRequirement req = recipe.Requirements[i];
                if (!req.IsCategoryMatch)
                {
                    continue;
                }

                int needed = req.Quantity;
                IReadOnlyList<IngredientSpec> all = catalog.Ingredients;

                for (int sweep = 0; sweep < 6 && needed > 0; sweep++)
                {
                    bool processed = sweep >= 3;
                    int tier = sweep % 3 + 1;
                    for (int j = 0; j < all.Count && needed > 0; j++)
                    {
                        IngredientSpec spec = all[j];
                        if (spec.Category != req.Category || spec.Tier != tier || spec.IsProcessed != processed)
                        {
                            continue;
                        }

                        int held = CountIngredient(spec.Id);
                        int alreadyReserved = reserved.TryGetValue(spec.Id, out int r) ? r : 0;
                        int available = held - alreadyReserved;
                        if (available <= 0)
                        {
                            continue;
                        }

                        int take = available < needed ? available : needed;
                        reserved[spec.Id] = alreadyReserved + take;
                        needed -= take;
                    }
                }

                if (needed > 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
