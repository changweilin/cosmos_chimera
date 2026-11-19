using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Food.Tests
{
    public class FoodGatheringTests
    {
        private FoodCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = FoodCatalog.CreateDefault();
        }

        private static TeamUtilityModifiers AllGatheringSkills()
        {
            var utilities = new HashSet<FieldUtility>
            {
                FieldUtility.HuntGround,
                FieldUtility.HuntSky,
                FieldUtility.SetTrap,
                FieldUtility.Fishing,
                FieldUtility.CastNet,
                FieldUtility.ForageBerries,
                FieldUtility.HarvestCrops,
                FieldUtility.ProduceMilk,
            };
            return FieldUtilityRules.ComputeTeamModifiers(utilities);
        }

        [Test]
        public void TryGather_SucceedsForAllSevenImmediateAcquisitionMethods()
        {
            TeamUtilityModifiers mods = AllGatheringSkills();
            var ctx = new GatheringContext(mods, onGround: true, hasOpenSky: true, nearWater: true, hasFoliage: true, hasFarmland: true);
            var inv = new FoodInventory();
            var rng = new Pcg32(42UL, 1UL);
            var yields = new List<GatheredStack>();

            var methods = new[]
            {
                FoodAcquisitionMethod.HuntGround,
                FoodAcquisitionMethod.HuntSky,
                FoodAcquisitionMethod.Fishing,
                FoodAcquisitionMethod.CastNet,
                FoodAcquisitionMethod.ForageBerries,
                FoodAcquisitionMethod.HarvestCrops,
                FoodAcquisitionMethod.ProduceMilk,
            };

            foreach (FoodAcquisitionMethod method in methods)
            {
                GatheringRefusalReason res = FoodGatheringRules.TryGather(method, ctx, _catalog, inv, ref rng, yields);
                Assert.AreEqual(GatheringRefusalReason.None, res, $"Expected success for {method}");
                Assert.IsTrue(yields.Count >= 1, $"Expected yields for {method}");
                foreach (GatheredStack stack in yields)
                {
                    Assert.AreEqual(method, stack.Ingredient.Source);
                    Assert.GreaterOrEqual(inv.CountIngredient(stack.Ingredient.Id), stack.Quantity);
                }
            }
        }

        [Test]
        public void ForageFungi_NeedsSkillAndFoliage()
        {
            TeamUtilityModifiers fungi = FieldUtilityRules.ComputeTeamModifiers(new HashSet<FieldUtility> { FieldUtility.ForageFungi });
            var shade = new GatheringContext(fungi, onGround: true, hasOpenSky: true, nearWater: false, hasFoliage: true, hasFarmland: false);
            var open = new GatheringContext(fungi, onGround: true, hasOpenSky: true, nearWater: false, hasFoliage: false, hasFarmland: false);
            var unskilled = new GatheringContext(AllGatheringSkills(), onGround: true, hasOpenSky: true, nearWater: false, hasFoliage: true, hasFarmland: false);
            var inv = new FoodInventory();
            var rng = new Pcg32(7UL, 1UL);
            var yields = new List<GatheredStack>();

            Assert.AreEqual(GatheringRefusalReason.MissingSkill, FoodGatheringRules.CanGather(FoodAcquisitionMethod.ForageFungi, unskilled));
            Assert.AreEqual(GatheringRefusalReason.InvalidEnvironment, FoodGatheringRules.CanGather(FoodAcquisitionMethod.ForageFungi, open));
            Assert.AreEqual(GatheringRefusalReason.None, FoodGatheringRules.TryGather(FoodAcquisitionMethod.ForageFungi, shade, _catalog, inv, ref rng, yields));
            Assert.IsTrue(yields.Count >= 1);
            foreach (GatheredStack stack in yields) Assert.AreEqual(IngredientCategory.Fungi, stack.Ingredient.Category);
        }

        [Test]
        public void TryGather_RefusesWhenSkillOrEnvironmentMissing()
        {
            TeamUtilityModifiers emptyMods = FieldUtilityRules.ComputeTeamModifiers(new HashSet<FieldUtility>());
            var noSkillCtx = new GatheringContext(emptyMods, onGround: true, hasOpenSky: true, nearWater: true, hasFoliage: true, hasFarmland: true);
            var inv = new FoodInventory();
            var rng = new Pcg32(1UL, 1UL);
            var yields = new List<GatheredStack>();

            Assert.AreEqual(GatheringRefusalReason.MissingSkill, FoodGatheringRules.TryGather(FoodAcquisitionMethod.HuntGround, noSkillCtx, _catalog, inv, ref rng, yields));
            Assert.IsEmpty(yields);

            TeamUtilityModifiers allSkills = AllGatheringSkills();
            var wrongEnvCtx = new GatheringContext(allSkills, onGround: false, hasOpenSky: false, nearWater: false, hasFoliage: false, hasFarmland: false);

            Assert.AreEqual(GatheringRefusalReason.InvalidEnvironment, FoodGatheringRules.TryGather(FoodAcquisitionMethod.HuntGround, wrongEnvCtx, _catalog, inv, ref rng, yields));
            Assert.AreEqual(GatheringRefusalReason.InvalidEnvironment, FoodGatheringRules.TryGather(FoodAcquisitionMethod.HuntSky, wrongEnvCtx, _catalog, inv, ref rng, yields));
            Assert.AreEqual(GatheringRefusalReason.InvalidEnvironment, FoodGatheringRules.TryGather(FoodAcquisitionMethod.Fishing, wrongEnvCtx, _catalog, inv, ref rng, yields));
            Assert.AreEqual(GatheringRefusalReason.InvalidEnvironment, FoodGatheringRules.TryGather(FoodAcquisitionMethod.CastNet, wrongEnvCtx, _catalog, inv, ref rng, yields));
            Assert.AreEqual(GatheringRefusalReason.InvalidEnvironment, FoodGatheringRules.TryGather(FoodAcquisitionMethod.ForageBerries, wrongEnvCtx, _catalog, inv, ref rng, yields));
            Assert.AreEqual(GatheringRefusalReason.InvalidEnvironment, FoodGatheringRules.TryGather(FoodAcquisitionMethod.HarvestCrops, wrongEnvCtx, _catalog, inv, ref rng, yields));
            // ProduceMilk does not depend on terrain flags
            Assert.AreEqual(GatheringRefusalReason.None, FoodGatheringRules.TryGather(FoodAcquisitionMethod.ProduceMilk, wrongEnvCtx, _catalog, inv, ref rng, yields));
        }

        [Test]
        public void TrapManager_PlacesAndCollectsDeterministicYieldsAfterDuration()
        {
            TeamUtilityModifiers mods = AllGatheringSkills();
            var ctx = new GatheringContext(mods, onGround: true, hasOpenSky: true, nearWater: false, hasFoliage: false, hasFarmland: false);
            var manager = new TrapManager();
            var inv = new FoodInventory();
            var rng = new Pcg32(99UL, 1UL);
            var collected = new List<GatheredStack>();

            Assert.AreEqual(GatheringRefusalReason.None, manager.TryPlaceTrap(ctx, 1000, out TrapState t1));
            Assert.AreEqual(GatheringRefusalReason.None, manager.TryPlaceTrap(ctx, 2000, out TrapState t2));
            Assert.AreEqual(GatheringRefusalReason.None, manager.TryPlaceTrap(ctx, 3000, out _));
            Assert.AreEqual(GatheringRefusalReason.TrapLimitReached, manager.TryPlaceTrap(ctx, 4000, out _));
            Assert.AreEqual(TrapManager.MaxActiveTraps, manager.ActiveTraps.Count);

            // Not ready yet at 10_000 ms
            Assert.AreEqual(0, manager.CollectReadyTraps(_catalog, inv, 10_000, ref rng, collected));
            Assert.AreEqual(3, manager.ActiveTraps.Count);

            // t1 ready at 31_000 ms; t2 & t3 not ready yet
            Assert.AreEqual(1, manager.CollectReadyTraps(_catalog, inv, t1.ReadyAtMs, ref rng, collected));
            Assert.AreEqual(1, collected.Count);
            Assert.AreEqual(FoodAcquisitionMethod.SetTrap, collected[0].Ingredient.Source);
            Assert.AreEqual(2, manager.ActiveTraps.Count);

            // Remaining 2 ready at 40_000 ms
            Assert.AreEqual(2, manager.CollectReadyTraps(_catalog, inv, t2.ReadyAtMs + 2000, ref rng, collected));
            Assert.AreEqual(2, collected.Count);
            Assert.AreEqual(0, manager.ActiveTraps.Count);
        }

        [Test]
        public void TryGather_IsDeterministicWithSameSeed()
        {
            TeamUtilityModifiers mods = AllGatheringSkills();
            var ctx = new GatheringContext(mods, onGround: true, hasOpenSky: true, nearWater: true, hasFoliage: true, hasFarmland: true);
            var invA = new FoodInventory();
            var invB = new FoodInventory();
            var rngA = new Pcg32(123456UL, 1UL);
            var rngB = new Pcg32(123456UL, 1UL);
            var yA = new List<GatheredStack>();
            var yB = new List<GatheredStack>();

            FoodGatheringRules.TryGather(FoodAcquisitionMethod.CastNet, ctx, _catalog, invA, ref rngA, yA);
            FoodGatheringRules.TryGather(FoodAcquisitionMethod.CastNet, ctx, _catalog, invB, ref rngB, yB);

            Assert.AreEqual(yA.Count, yB.Count);
            for (int i = 0; i < yA.Count; i++)
            {
                Assert.AreEqual(yA[i].Ingredient.Id, yB[i].Ingredient.Id);
                Assert.AreEqual(yA[i].Quantity, yB[i].Quantity);
            }
        }

        [Test]
        public void TryGather_ViolatingContracts_ThrowsContractException()
        {
            TeamUtilityModifiers mods = AllGatheringSkills();
            var ctx = new GatheringContext(mods, true, true, true, true, true);
            var inv = new FoodInventory();
            var rng = new Pcg32(1UL, 1UL);
            var yields = new List<GatheredStack>();

            Assert.Throws<ContractException>(() => FoodGatheringRules.TryGather((FoodAcquisitionMethod)999, ctx, _catalog, inv, ref rng, yields));
            Assert.Throws<ContractException>(() => FoodGatheringRules.TryGather(FoodAcquisitionMethod.Fishing, ctx, null, inv, ref rng, yields));
            Assert.Throws<ContractException>(() => FoodGatheringRules.TryGather(FoodAcquisitionMethod.Fishing, ctx, _catalog, null, ref rng, yields));
            Assert.Throws<ContractException>(() => FoodGatheringRules.TryGather(FoodAcquisitionMethod.Fishing, ctx, _catalog, inv, ref rng, null));
        }
    }
}
