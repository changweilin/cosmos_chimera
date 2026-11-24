using System.Collections.Generic;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Food.Tests
{
    public class PotCookingTests
    {
        private FoodCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = FoodCatalog.CreateDefault();
        }

        private static CookingContext Kitchen() =>
            new CookingContext(CookingStationKind.Kitchen, FieldUtilityRules.ComputeTeamModifiers(new HashSet<FieldUtility>()));

        private static CookingContext Field(params FieldUtility[] utilities) =>
            new CookingContext(CookingStationKind.Field, FieldUtilityRules.ComputeTeamModifiers(new HashSet<FieldUtility>(utilities)));

        private static FoodInventory Stocked(params string[] pot)
        {
            var inv = new FoodInventory();
            foreach (string id in pot) inv.AddIngredient(id, 1);
            return inv;
        }

        [Test]
        public void ExactRecipeMatch_ProducesTheSignatureDish()
        {
            var pot = new[] { "ing_chilled_sea_bass", "ing_wild_berry" };
            FoodInventory inv = Stocked(pot);

            PotResolution r = PotCookingRules.TryCook(CookingMethod.Steam, pot, _catalog, inv, Kitchen());

            Assert.AreEqual(CookRefusalReason.None, r.Refusal);
            Assert.AreEqual(PotDishKind.Signature, r.Kind);
            Assert.AreEqual("food_steamed_sea_bass", r.Food.Id);
            Assert.AreEqual(r.Quantity, inv.CountFood("food_steamed_sea_bass"));
            Assert.AreEqual(0, inv.CountIngredient("ing_chilled_sea_bass"));
            Assert.AreEqual(0, inv.CountIngredient("ing_wild_berry"));
        }

        [Test]
        public void AnExtraIngredient_BreaksTheSignatureMatch()
        {
            var pot = new[] { "ing_chilled_sea_bass", "ing_wild_berry", "ing_river_trout" };

            PotResolution r = PotCookingRules.Resolve(CookingMethod.Steam, pot, _catalog, Kitchen());

            Assert.AreEqual(PotDishKind.Free, r.Kind);
            Assert.IsTrue(r.Food.FreeDish.HasValue);
        }

        [Test]
        public void ExistingRecipes_StillMatchFromThePot()
        {
            var pot = new[] { "ing_boar_flank", "ing_wild_berry" };
            PotResolution r = PotCookingRules.Resolve(CookingMethod.Grill, pot, _catalog, Kitchen());
            Assert.AreEqual(PotDishKind.Signature, r.Kind);
            Assert.AreEqual("food_charred_boar_skewer", r.Food.Id);
        }

        [Test]
        public void SameFamilyIngredients_StackIntoHigherLevels()
        {
            int previous = 0;
            for (int n = 1; n <= PotCookingRules.MaxPotSize; n++)
            {
                var pot = new string[n];
                for (int i = 0; i < n; i++) pot[i] = "ing_glowcap";

                PotResolution r = PotCookingRules.Resolve(CookingMethod.Grill, pot, _catalog, Kitchen());

                Assert.AreEqual(PotDishKind.Free, r.Kind, $"n={n}");
                FreeDishDescriptor dish = r.Food.FreeDish.Value;
                Assert.AreEqual(FoodEffectFamily.Fierce, dish.Family);
                Assert.GreaterOrEqual(dish.Level, previous, "more of one family never lowers the level");
                Assert.That(dish.Level, Is.InRange(1, 3));
                Assert.Greater(r.Food.Effect.DurationMs, 0);
                previous = dish.Level;
            }

            Assert.Greater(previous, 1, "a full pot of one family reaches above Lv1");
        }

        [Test]
        public void MixedFamilies_CancelLeavingOnlyHealing()
        {
            var pot = new[] { "ing_glowcap", "ing_gale_mushroom" };

            PotResolution r = PotCookingRules.Resolve(CookingMethod.Grill, pot, _catalog, Kitchen());

            Assert.AreEqual(PotDishKind.Free, r.Kind);
            Assert.AreEqual(FoodEffectFamily.None, r.Food.FreeDish.Value.Family);
            Assert.AreEqual(0, r.Food.Effect.CombatEffects.Count);
            Assert.AreEqual(0, r.Food.Effect.DurationMs);
            Assert.Greater(r.Food.Effect.CreatureHealPercent, 0);
        }

        [Test]
        public void SameFamilyDifferentElement_AlsoCancels()
        {
            var pot = new[] { "ing_boar_flank", "ing_beast_prime_rib" };
            PotResolution r = PotCookingRules.Resolve(CookingMethod.Grill, pot, _catalog, Kitchen());
            Assert.AreEqual(FoodEffectFamily.None, r.Food.FreeDish.Value.Family);
        }

        [Test]
        public void NeutralBases_DoNotDiluteTheEffect()
        {
            var pot = new[] { "ing_boar_flank", "ing_river_trout", "ing_wild_berry" };
            PotResolution r = PotCookingRules.Resolve(CookingMethod.Grill, pot, _catalog, Kitchen());
            Assert.AreEqual(FoodEffectFamily.Mastery, r.Food.FreeDish.Value.Family);
            Assert.AreEqual(Element.Fire, r.Food.FreeDish.Value.FamilyElement);
            Assert.AreEqual(DishTheme.Mixed, r.Food.FreeDish.Value.Theme);
        }

        [Test]
        public void MethodConditions_FailIntoDubiousFoodAndStillConsume()
        {
            var cases = new (CookingMethod, string[])[]
            {
                (CookingMethod.StirFry, new[] { "ing_boar_flank" }),                                   // no oil
                (CookingMethod.DeepFry, new[] { "ing_boar_flank", "ing_seed_oil" }),                   // no flour
                (CookingMethod.ColdMix, new[] { "ing_boar_flank", "ing_wild_berry", "ing_sea_salt" }), // raw meat
                (CookingMethod.ChillBrew, new[] { "ing_chilled_berry", "ing_chilled_fowl" }),          // meat in a drink
                (CookingMethod.Grill, new[] { "ing_sea_salt" }),                                       // seasoning only
                (CookingMethod.Stew, new[] { "ing_boar_flank" }),                                      // too thin for a stew
            };

            foreach ((CookingMethod method, string[] pot) in cases)
            {
                FoodInventory inv = Stocked(pot);
                PotResolution r = PotCookingRules.TryCook(method, pot, _catalog, inv, Kitchen());

                Assert.AreEqual(CookRefusalReason.None, r.Refusal, method.ToString());
                Assert.AreEqual(PotDishKind.Dubious, r.Kind, method.ToString());
                Assert.AreEqual(PotCookingRules.DubiousFoodId, r.Food.Id);
                Assert.AreEqual(CookingQuality.Normal, r.Quality);
                Assert.AreEqual(1, inv.CountFood(PotCookingRules.DubiousFoodId), method.ToString());
                foreach (string id in pot) Assert.AreEqual(0, inv.CountIngredient(id), method + " " + id);
            }
        }

        [Test]
        public void ProcessedGoods_UnlockMethodsRawGoodsCannotUse()
        {
            Assert.AreEqual(PotDishKind.Free, PotCookingRules.Resolve(CookingMethod.ColdMix, new[] { "ing_smoked_boar", "ing_wild_berry" }, _catalog, Kitchen()).Kind);
            Assert.AreEqual(PotDishKind.Free, PotCookingRules.Resolve(CookingMethod.StirFry, new[] { "ing_boar_flank", "ing_seed_oil" }, _catalog, Kitchen()).Kind);
            Assert.AreEqual(PotDishKind.Free, PotCookingRules.Resolve(CookingMethod.DeepFry, new[] { "ing_net_shrimp", "ing_seed_oil", "ing_wheat_flour" }, _catalog, Kitchen()).Kind);
            Assert.AreEqual(PotDishKind.Free, PotCookingRules.Resolve(CookingMethod.Stew, new[] { "ing_boar_flank", "ing_fish_sauce" }, _catalog, Kitchen()).Kind);
        }

        [Test]
        public void Revival_SurvivesOnlyGrindingOrStewing()
        {
            PotResolution grill = PotCookingRules.Resolve(CookingMethod.Grill, new[] { "ing_sky_nest_egg" }, _catalog, Kitchen());
            PotResolution grind = PotCookingRules.Resolve(CookingMethod.Grind, new[] { "ing_sky_nest_egg", "ing_fern_powder" }, _catalog, Kitchen());

            Assert.AreEqual(0, grill.Food.Effect.RevivePercent);
            Assert.AreEqual(FoodEffectFamily.Revive, grind.Food.FreeDish.Value.Family);
            Assert.Greater(grind.Food.Effect.RevivePercent, 0);
        }

        [Test]
        public void StirFryAndDeepFry_AddOnePoint()
        {
            // 3 glowcaps = 3 points (Lv1); frying adds one point and crosses into Lv2.
            PotResolution grill = PotCookingRules.Resolve(CookingMethod.Grill, new[] { "ing_glowcap", "ing_glowcap", "ing_glowcap" }, _catalog, Kitchen());
            PotResolution fry = PotCookingRules.Resolve(CookingMethod.StirFry, new[] { "ing_glowcap", "ing_glowcap", "ing_glowcap", "ing_seed_oil" }, _catalog, Kitchen());
            Assert.AreEqual(1, grill.Food.FreeDish.Value.Level);
            // Seed oil carries Bounty, so the fry cancels; use butter (no family) to isolate the +1.
            PotResolution butterFry = PotCookingRules.Resolve(CookingMethod.StirFry, new[] { "ing_glowcap", "ing_glowcap", "ing_glowcap", "ing_butter" }, _catalog, Kitchen());
            Assert.AreEqual(FoodEffectFamily.None, fry.Food.FreeDish.Value.Family);
            Assert.AreEqual(2, butterFry.Food.FreeDish.Value.Level);
        }

        [Test]
        public void ChilledIngredients_RaiseQualityForGentleMethods()
        {
            CookingContext fire = Field(FieldUtility.CookFire);
            PotResolution raw = PotCookingRules.Resolve(CookingMethod.Steam, new[] { "ing_river_trout" }, _catalog, fire);
            PotResolution chilled = PotCookingRules.Resolve(CookingMethod.Steam, new[] { "ing_chilled_trout" }, _catalog, fire);
            Assert.Greater(chilled.Quality, raw.Quality);
        }

        [Test]
        public void SamePotTwice_YieldsTheSameRegisteredDish()
        {
            var pot = new[] { "ing_glowcap", "ing_glowcap" };
            FoodInventory inv = Stocked(pot);
            inv.AddIngredient("ing_glowcap", 2);

            PotResolution first = PotCookingRules.TryCook(CookingMethod.Grill, pot, _catalog, inv, Kitchen());
            PotResolution second = PotCookingRules.TryCook(CookingMethod.Grill, pot, _catalog, inv, Kitchen());

            Assert.AreEqual(first.Food.Id, second.Food.Id);
            Assert.IsTrue(_catalog.TryGetFood(first.Food.Id, out _));
            Assert.AreEqual(first.Quantity + second.Quantity, inv.CountFood(first.Food.Id));
        }

        [Test]
        public void FreeDishes_CanBeEaten()
        {
            var pot = new[] { "ing_glowcap", "ing_glowcap" };
            FoodInventory inv = Stocked(pot);
            inv.AddIngredient("ing_glowcap", 1);
            PotResolution r = PotCookingRules.TryCook(CookingMethod.Grill, pot, _catalog, inv, Kitchen());

            var player = new PlayerVitals(new PlayerVitalsConfig(1000, 600, 400, 800));
            var buffs = new ActiveFoodBuffs();
            ConsumeRefusalReason eaten = FoodConsumptionRules.TryEatFood(r.Food.Id, _catalog, inv, player, null, buffs, 1000, out _);

            Assert.AreEqual(ConsumeRefusalReason.None, eaten);
        }

        [Test]
        public void MissingSkill_RefusesWithoutConsuming()
        {
            var pot = new[] { "ing_boar_flank" };
            FoodInventory inv = Stocked(pot);

            PotResolution r = PotCookingRules.TryCook(CookingMethod.Grill, pot, _catalog, inv, Field(FieldUtility.CulinaryPrep));

            Assert.AreEqual(CookRefusalReason.MissingCookingSkill, r.Refusal);
            Assert.IsNull(r.Food);
            Assert.AreEqual(0, r.Quantity);
            Assert.AreEqual(1, inv.CountIngredient("ing_boar_flank"));
        }

        [Test]
        public void ChillBrew_NeedsItsOwnSkillInTheField()
        {
            var pot = new[] { "ing_chilled_berry", "ing_chilled_berry", "ing_wild_honey" };
            Assert.AreEqual(CookRefusalReason.MissingCookingSkill, PotCookingRules.Resolve(CookingMethod.ChillBrew, pot, _catalog, Field(FieldUtility.CookFire)).Refusal);
            PotResolution r = PotCookingRules.Resolve(CookingMethod.ChillBrew, pot, _catalog, Field(FieldUtility.ChillBrew));
            Assert.AreEqual("food_frostberry_smoothie", r.Food.Id);
        }

        [Test]
        public void MissingIngredients_RefusesWithoutConsuming()
        {
            var pot = new[] { "ing_glowcap", "ing_glowcap" };
            FoodInventory inv = Stocked("ing_glowcap");

            PotResolution r = PotCookingRules.TryCook(CookingMethod.Grill, pot, _catalog, inv, Kitchen());

            Assert.AreEqual(CookRefusalReason.MissingIngredients, r.Refusal);
            Assert.AreEqual(1, inv.CountIngredient("ing_glowcap"));
        }

        [Test]
        public void EveryNewSignatureRecipe_IsCookableFromAnExactPot()
        {
            string[] ids =
            {
                "recipe_food_steamed_sea_bass", "recipe_food_sea_bass_crudo", "recipe_food_aged_beast_ragout",
                "recipe_food_gale_fried_fowl", "recipe_food_meadow_cheese_soup", "recipe_food_frostberry_smoothie",
                "recipe_food_glowcap_tonic", "recipe_food_truffle_butter_grill", "recipe_food_honey_glazed_nuts",
            };
            foreach (string id in ids)
            {
                Assert.IsTrue(_catalog.TryGetRecipe(id, out RecipeSpec recipe), id);
                var inv = new FoodInventory();
                foreach (IngredientSpec ing in _catalog.Ingredients) inv.AddIngredient(ing.Id, 10);
                Assert.AreEqual(CookRefusalReason.None, CookingRules.TryCook(recipe, _catalog, inv, Kitchen(), out CookOutcome outcome), id);
                Assert.AreEqual(recipe.OutputFoodId, outcome.Food.Id);
            }
        }

        [Test]
        public void LevelForPoints_Boundaries()
        {
            Assert.AreEqual(0, PotCookingRules.LevelForPoints(0));
            Assert.AreEqual(1, PotCookingRules.LevelForPoints(1));
            Assert.AreEqual(1, PotCookingRules.LevelForPoints(3));
            Assert.AreEqual(2, PotCookingRules.LevelForPoints(4));
            Assert.AreEqual(2, PotCookingRules.LevelForPoints(6));
            Assert.AreEqual(3, PotCookingRules.LevelForPoints(7));
            Assert.Throws<ContractException>(() => PotCookingRules.LevelForPoints(-1));
        }

        [Test]
        public void PotCookingRules_ViolatingContracts_ThrowsContractException()
        {
            CookingContext ctx = Kitchen();
            Assert.Throws<ContractException>(() => PotCookingRules.Resolve(CookingMethod.Grill, new string[0], _catalog, ctx));
            Assert.Throws<ContractException>(() => PotCookingRules.Resolve(CookingMethod.Grill, new string[6] { "ing_glowcap", "ing_glowcap", "ing_glowcap", "ing_glowcap", "ing_glowcap", "ing_glowcap" }, _catalog, ctx));
            Assert.Throws<ContractException>(() => PotCookingRules.Resolve(CookingMethod.Grill, new[] { "ing_unknown" }, _catalog, ctx));
            Assert.Throws<ContractException>(() => PotCookingRules.Resolve(CookingMethod.Grill, null, _catalog, ctx));
            Assert.Throws<ContractException>(() => PotCookingRules.Resolve(CookingMethod.Grill, new[] { "ing_glowcap" }, null, ctx));
            Assert.Throws<ContractException>(() => PotCookingRules.Resolve((CookingMethod)99, new[] { "ing_glowcap" }, _catalog, ctx));
            Assert.Throws<ContractException>(() => PotCookingRules.TryCook(CookingMethod.Grill, new[] { "ing_glowcap" }, _catalog, null, ctx));
            Assert.Throws<ContractException>(() => new CuisineTraits(IngredientRole.Effect, IngredientTags.Fruit, 10));
            Assert.Throws<ContractException>(() => new CuisineTraits(IngredientRole.Base, IngredientTags.Fruit, 10, FoodEffectFamily.Swift, Element.Wind, 1));
        }
    }
}
