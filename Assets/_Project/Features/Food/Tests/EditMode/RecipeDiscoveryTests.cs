using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Food.Tests
{
    // REQ: docs/design/cuisine.md#6 — "？" hints, 帶入投料, and the route showcase dishes (Q-F3).
    public class RecipeDiscoveryTests
    {
        private FoodCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = FoodCatalog.CreateDefault();
        }

        private static CookingContext Kitchen() =>
            new CookingContext(CookingStationKind.Kitchen, FieldUtilityRules.ComputeTeamModifiers(new HashSet<FieldUtility>()));

        private RecipeSpec Recipe(string foodId)
        {
            foreach (RecipeSpec recipe in _catalog.Recipes)
                if (recipe.OutputFoodId == foodId) return recipe;
            Assert.Fail("missing recipe for " + foodId);
            return null;
        }

        [Test]
        public void HintCategory_ReportsTheFirstRequirementsCategory()
        {
            Assert.AreEqual(IngredientCategory.Seafood,
                CookingRules.RecipeHintCategory(Recipe("food_steamed_sea_bass"), _catalog));
            Assert.AreEqual(IngredientCategory.MeatGround,
                CookingRules.RecipeHintCategory(Recipe("food_charred_boar_skewer"), _catalog));
        }

        [Test]
        public void HintCategory_RejectsNullArguments()
        {
            Assert.Throws<ContractException>(() => CookingRules.RecipeHintCategory(null, _catalog));
            Assert.Throws<ContractException>(() => CookingRules.RecipeHintCategory(Recipe("food_steamed_sea_bass"), null));
        }

        [Test]
        public void PlanRecipePot_ListsOneIdPerUnitAndResolvesToTheRecipe()
        {
            RecipeSpec recipe = Recipe("food_crispy_shrimp_tempura");
            var inv = new FoodInventory();
            inv.AddIngredient("ing_net_shrimp", 3);
            inv.AddIngredient("ing_golden_oilseed", 1);
            inv.AddIngredient("ing_sun_wheat", 1);
            var pot = new List<string>();

            Assert.IsTrue(inv.TryPlanRecipePot(recipe, _catalog, pot));

            CollectionAssert.AreEqual(new[] { "ing_golden_oilseed", "ing_net_shrimp", "ing_net_shrimp", "ing_sun_wheat" }, pot);
            Assert.AreEqual(3, inv.CountIngredient("ing_net_shrimp"), "planning must not consume");
            PotResolution r = PotCookingRules.Resolve(recipe.Method, pot, _catalog, Kitchen());
            Assert.AreEqual(PotDishKind.Signature, r.Kind);
            Assert.AreEqual(recipe.Id, r.Recipe.Id);
        }

        [Test]
        public void PlanRecipePot_PrefersRawGoodsForCategoryRequirements()
        {
            var inv = new FoodInventory();
            inv.AddIngredient("ing_chilled_sea_bass", 1);
            inv.AddIngredient("ing_chilled_berry", 1);
            inv.AddIngredient("ing_wild_berry", 1);
            var pot = new List<string>();

            Assert.IsTrue(inv.TryPlanRecipePot(Recipe("food_steamed_sea_bass"), _catalog, pot));

            CollectionAssert.AreEqual(new[] { "ing_chilled_sea_bass", "ing_wild_berry" }, pot);
        }

        [Test]
        public void PlanRecipePot_ShortStock_ReturnsFalseAndEmptiesThePot()
        {
            var inv = new FoodInventory();
            inv.AddIngredient("ing_chilled_sea_bass", 1);
            var pot = new List<string> { "ing_wild_berry" };

            Assert.IsFalse(inv.TryPlanRecipePot(Recipe("food_steamed_sea_bass"), _catalog, pot));
            Assert.IsEmpty(pot);
        }

        [Test]
        public void PlanRecipePot_RejectsNullArguments()
        {
            var inv = new FoodInventory();
            RecipeSpec recipe = Recipe("food_steamed_sea_bass");
            Assert.Throws<ContractException>(() => inv.TryPlanRecipePot(null, _catalog, new List<string>()));
            Assert.Throws<ContractException>(() => inv.TryPlanRecipePot(recipe, null, new List<string>()));
            Assert.Throws<ContractException>(() => inv.TryPlanRecipePot(recipe, _catalog, null));
        }

        [TestCase("food_chilled_seafood_salad")]
        [TestCase("food_golden_wheat_tempura")]
        [TestCase("food_pickled_root_hare_stew")]
        [TestCase("food_pickled_berry_cooler")]
        public void RouteShowcaseDishes_CookFromTheirExactRecipe(string foodId)
        {
            RecipeSpec recipe = Recipe(foodId);
            var inv = new FoodInventory();
            foreach (RecipeIngredientRequirement req in recipe.Requirements)
                inv.AddIngredient(req.IsCategoryMatch ? AnyOf(req.Category) : req.IngredientId, req.Quantity);
            var pot = new List<string>();
            Assert.IsTrue(inv.TryPlanRecipePot(recipe, _catalog, pot));

            PotResolution r = PotCookingRules.TryCook(recipe.Method, pot, _catalog, inv, Kitchen());

            Assert.AreEqual(PotDishKind.Signature, r.Kind);
            Assert.AreEqual(foodId, r.Food.Id);
            Assert.GreaterOrEqual(inv.CountFood(foodId), 1);
        }

        [TestCase("ing_wild_berry", "ing_pickled_berry")]
        [TestCase("ing_wild_root", "ing_pickled_root")]
        public void Pickling_FermentsFruitAndVegetables(string input, string output)
        {
            ProcessSpec process = null;
            foreach (ProcessSpec p in _catalog.Processes)
                if (p.InputIngredientId == input && p.OutputIngredientId == output) process = p;
            Assert.IsNotNull(process);
            Assert.AreEqual(ProcessKind.Ferment, process.Kind);
            Assert.IsTrue(_catalog.TryGetIngredient(output, out IngredientSpec pickled));
            Assert.IsTrue(pickled.IsProcessed);
            Assert.AreNotEqual(IngredientTags.None, pickled.Traits.Tags & IngredientTags.Fermented);
            Assert.AreEqual(CuisineTraits.FermentedDurationAddMs, pickled.Traits.DurationAddMs);
        }

        private string AnyOf(IngredientCategory category)
        {
            foreach (IngredientSpec ingredient in _catalog.Ingredients)
                if (ingredient.Category == category && !ingredient.IsProcessed) return ingredient.Id;
            Assert.Fail("no raw ingredient in " + category);
            return null;
        }
    }
}
