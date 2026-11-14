using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Food.Tests
{
    public class CookingTests
    {
        private FoodCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = FoodCatalog.CreateDefault();
        }

        private static void StockAllIngredients(FoodInventory inv)
        {
            foreach (IngredientSpec ing in FoodCatalog.CreateDefault().Ingredients)
            {
                inv.AddIngredient(ing.Id, 10);
            }
        }

        [Test]
        public void TryCook_SucceedsForAllSevenCookingMethodsInKitchen()
        {
            var inv = new FoodInventory();
            StockAllIngredients(inv);
            TeamUtilityModifiers mods = FieldUtilityRules.ComputeTeamModifiers(new HashSet<FieldUtility>());
            var ctx = new CookingContext(CookingStationKind.Kitchen, mods);

            var recipeIds = new[]
            {
                "recipe_food_charred_boar_skewer",   // Grill
                "recipe_food_hearty_beast_stew",     // Stew
                "recipe_food_gale_wing_stir_fry",    // StirFry
                "recipe_food_crispy_shrimp_tempura", // DeepFry
                "recipe_food_steamed_abyssal_salmon",// Steam
                "recipe_food_chilled_herb_salad",    // ColdMix
                "recipe_food_revival_herb_powder",   // Grind
            };

            foreach (string recipeId in recipeIds)
            {
                Assert.IsTrue(_catalog.TryGetRecipe(recipeId, out RecipeSpec recipe));
                Assert.AreEqual(CookRefusalReason.None, CookingRules.TryCook(recipe, _catalog, inv, ctx, out CookOutcome outcome), $"Failed {recipeId}");
                Assert.GreaterOrEqual(outcome.QuantityProduced, recipe.OutputQuantity);
                Assert.GreaterOrEqual(inv.CountFood(recipe.OutputFoodId), outcome.QuantityProduced);
            }
        }

        [Test]
        public void FieldCooking_EnforcesCookFireAndCulinaryPrepPerMethod()
        {
            var inv = new FoodInventory();
            StockAllIngredients(inv);

            TeamUtilityModifiers noSkill = FieldUtilityRules.ComputeTeamModifiers(new HashSet<FieldUtility>());
            TeamUtilityModifiers cookFireOnly = FieldUtilityRules.ComputeTeamModifiers(new HashSet<FieldUtility> { FieldUtility.CookFire });
            TeamUtilityModifiers prepOnly = FieldUtilityRules.ComputeTeamModifiers(new HashSet<FieldUtility> { FieldUtility.CulinaryPrep });

            Assert.IsTrue(_catalog.TryGetRecipe("recipe_food_charred_boar_skewer", out RecipeSpec grill));
            Assert.IsTrue(_catalog.TryGetRecipe("recipe_food_steamed_abyssal_salmon", out RecipeSpec steam));
            Assert.IsTrue(_catalog.TryGetRecipe("recipe_food_chilled_herb_salad", out RecipeSpec coldMix));
            Assert.IsTrue(_catalog.TryGetRecipe("recipe_food_revival_herb_powder", out RecipeSpec grind));

            // No skills -> all fail in Field
            var noSkillCtx = new CookingContext(CookingStationKind.Field, noSkill);
            Assert.AreEqual(CookRefusalReason.MissingCookingSkill, CookingRules.TryCook(grill, _catalog, inv, noSkillCtx, out _));
            Assert.AreEqual(CookRefusalReason.MissingCookingSkill, CookingRules.TryCook(coldMix, _catalog, inv, noSkillCtx, out _));

            // CookFire unlocks Grill and Steam, not ColdMix or Grind
            var fireCtx = new CookingContext(CookingStationKind.Field, cookFireOnly);
            Assert.AreEqual(CookRefusalReason.None, CookingRules.TryCook(grill, _catalog, inv, fireCtx, out _));
            Assert.AreEqual(CookRefusalReason.None, CookingRules.TryCook(steam, _catalog, inv, fireCtx, out _));
            Assert.AreEqual(CookRefusalReason.MissingCookingSkill, CookingRules.TryCook(coldMix, _catalog, inv, fireCtx, out _));
            Assert.AreEqual(CookRefusalReason.MissingCookingSkill, CookingRules.TryCook(grind, _catalog, inv, fireCtx, out _));

            // CulinaryPrep unlocks ColdMix, Grind, and Steam, not Grill
            var prepCtx = new CookingContext(CookingStationKind.Field, prepOnly);
            Assert.AreEqual(CookRefusalReason.None, CookingRules.TryCook(coldMix, _catalog, inv, prepCtx, out _));
            Assert.AreEqual(CookRefusalReason.None, CookingRules.TryCook(grind, _catalog, inv, prepCtx, out _));
            Assert.AreEqual(CookRefusalReason.None, CookingRules.TryCook(steam, _catalog, inv, prepCtx, out _));
            Assert.AreEqual(CookRefusalReason.MissingCookingSkill, CookingRules.TryCook(grill, _catalog, inv, prepCtx, out _));
        }

        [Test]
        public void TryCook_MasterworkGrantsBonusOutputAndScaledDuration()
        {
            var inv = new FoodInventory();
            inv.AddIngredient("ing_boar_flank", 1);
            inv.AddIngredient("ing_wild_berry", 1);

            TeamUtilityModifiers both = FieldUtilityRules.ComputeTeamModifiers(new HashSet<FieldUtility>
            {
                FieldUtility.CookFire,
                FieldUtility.CulinaryPrep,
            });
            var ctx = new CookingContext(CookingStationKind.Kitchen, both, new[] { Element.Fire });

            Assert.IsTrue(_catalog.TryGetRecipe("recipe_food_charred_boar_skewer", out RecipeSpec grill));
            Assert.AreEqual(CookRefusalReason.None, CookingRules.TryCook(grill, _catalog, inv, ctx, out CookOutcome outcome));
            Assert.AreEqual(CookingQuality.Masterwork, outcome.Quality);
            Assert.AreEqual(grill.OutputQuantity + 1, outcome.QuantityProduced);
            Assert.AreEqual(0, inv.CountIngredient("ing_boar_flank"));
            Assert.AreEqual(0, inv.CountIngredient("ing_wild_berry"));

            Assert.AreEqual(168_000, CookingRules.ScaleDurationMs(120_000, CookingQuality.Masterwork));
            Assert.AreEqual(35, CookingRules.ScaleHealPercent(25, CookingQuality.Masterwork));
        }

        [Test]
        public void TryCook_MissingIngredients_LeavesInventoryUnchanged()
        {
            var inv = new FoodInventory();
            inv.AddIngredient("ing_boar_flank", 1);
            // Missing BerryHerb category ingredient

            TeamUtilityModifiers mods = FieldUtilityRules.ComputeTeamModifiers(new HashSet<FieldUtility> { FieldUtility.CookFire });
            var ctx = new CookingContext(CookingStationKind.Field, mods);
            Assert.IsTrue(_catalog.TryGetRecipe("recipe_food_charred_boar_skewer", out RecipeSpec grill));

            Assert.AreEqual(CookRefusalReason.MissingIngredients, CookingRules.TryCook(grill, _catalog, inv, ctx, out _));
            Assert.AreEqual(1, inv.CountIngredient("ing_boar_flank"));
            Assert.AreEqual(0, inv.CountFood("food_charred_boar_skewer"));
        }

        [Test]
        public void CookingRules_ViolatingContracts_ThrowsContractException()
        {
            var inv = new FoodInventory();
            TeamUtilityModifiers mods = FieldUtilityRules.ComputeTeamModifiers(new HashSet<FieldUtility>());
            var ctx = new CookingContext(CookingStationKind.Kitchen, mods);
            Assert.IsTrue(_catalog.TryGetRecipe("recipe_food_charred_boar_skewer", out RecipeSpec grill));

            Assert.Throws<ContractException>(() => CookingRules.CanCook(null, _catalog, inv, ctx));
            Assert.Throws<ContractException>(() => CookingRules.CanCook(grill, null, inv, ctx));
            Assert.Throws<ContractException>(() => CookingRules.CanCook(grill, _catalog, null, ctx));
            Assert.Throws<ContractException>(() => CookingRules.ScaleDurationMs(-1, CookingQuality.Normal));
            Assert.Throws<ContractException>(() => CookingRules.ScaleHealPercent(101, CookingQuality.Normal));
        }
    }
}
