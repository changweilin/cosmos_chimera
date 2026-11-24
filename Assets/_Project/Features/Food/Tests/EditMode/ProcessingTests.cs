using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Food.Tests
{
    public class ProcessingTests
    {
        private FoodCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = FoodCatalog.CreateDefault();
        }

        private static CookingContext Field(params FieldUtility[] utilities) =>
            new CookingContext(CookingStationKind.Field, FieldUtilityRules.ComputeTeamModifiers(new HashSet<FieldUtility>(utilities)));

        private static CookingContext Kitchen() =>
            new CookingContext(CookingStationKind.Kitchen, FieldUtilityRules.ComputeTeamModifiers(new HashSet<FieldUtility>()));

        private ProcessSpec Process(string outputId)
        {
            Assert.IsTrue(_catalog.TryGetProcess("process_" + outputId, out ProcessSpec spec), outputId);
            return spec;
        }

        [Test]
        public void TryProcess_ConvertsOneInputIntoOneOutput()
        {
            var inv = new FoodInventory();
            inv.AddIngredient("ing_sea_bass", 2);

            Assert.AreEqual(ProcessRefusalReason.None, ProcessingRules.TryProcess(Process("ing_chilled_sea_bass"), inv, Kitchen()));

            Assert.AreEqual(1, inv.CountIngredient("ing_sea_bass"));
            Assert.AreEqual(1, inv.CountIngredient("ing_chilled_sea_bass"));
        }

        [Test]
        public void TryProcess_QuantityConvertsThatManyUnits()
        {
            var inv = new FoodInventory();
            inv.AddIngredient("ing_sun_wheat", 3);

            Assert.AreEqual(ProcessRefusalReason.None, ProcessingRules.TryProcess(Process("ing_wheat_flour"), inv, Kitchen(), 3));

            Assert.AreEqual(0, inv.CountIngredient("ing_sun_wheat"));
            Assert.AreEqual(3, inv.CountIngredient("ing_wheat_flour"));
        }

        [Test]
        public void FieldProcessing_RequiresTheMatchingLifeSkill()
        {
            var cases = new[]
            {
                ("ing_chilled_sea_bass", FieldUtility.FreezePrep),
                ("ing_wheat_flour", FieldUtility.CulinaryPrep),
                ("ing_seed_oil", FieldUtility.CulinaryPrep),
                ("ing_butter", FieldUtility.CulinaryPrep),
                ("ing_cheese", FieldUtility.FermentCure),
                ("ing_smoked_boar", FieldUtility.SmokeDry),
            };

            foreach ((string output, FieldUtility skill) in cases)
            {
                ProcessSpec spec = Process(output);
                var inv = new FoodInventory();
                inv.AddIngredient(spec.InputIngredientId, 1);

                Assert.AreEqual(ProcessRefusalReason.MissingSkill, ProcessingRules.TryProcess(spec, inv, Field(FieldUtility.CookFire)), output);
                Assert.AreEqual(1, inv.CountIngredient(spec.InputIngredientId), output);
                Assert.AreEqual(0, inv.CountIngredient(output), output);

                Assert.AreEqual(ProcessRefusalReason.None, ProcessingRules.TryProcess(spec, inv, Field(skill)), output);
                Assert.AreEqual(1, inv.CountIngredient(output), output);
            }
        }

        [Test]
        public void TryProcess_MissingInputLeavesInventoryUnchanged()
        {
            var inv = new FoodInventory();
            inv.AddIngredient("ing_sea_bass", 1);

            Assert.AreEqual(ProcessRefusalReason.MissingInput, ProcessingRules.TryProcess(Process("ing_chilled_sea_bass"), inv, Kitchen(), 2));

            Assert.AreEqual(1, inv.CountIngredient("ing_sea_bass"));
            Assert.AreEqual(0, inv.CountIngredient("ing_chilled_sea_bass"));
        }

        [Test]
        public void ProcessedGoods_ChangeTraitsAsDesigned()
        {
            Assert.IsTrue(_catalog.TryGetIngredient("ing_sun_wheat", out IngredientSpec wheat));
            Assert.IsTrue(_catalog.TryGetIngredient("ing_wheat_flour", out IngredientSpec flour));
            Assert.AreEqual(wheat.Traits.EffectPoints * 2, flour.Traits.EffectPoints, "grinding doubles points");
            Assert.AreNotEqual(0, (int)(flour.Traits.Tags & IngredientTags.Flour));

            Assert.IsTrue(_catalog.TryGetIngredient("ing_chilled_sea_bass", out IngredientSpec chilled));
            Assert.AreEqual(0, (int)(chilled.Traits.Tags & IngredientTags.Raw));
            Assert.AreNotEqual(0, (int)(chilled.Traits.Tags & IngredientTags.Chilled));

            Assert.IsTrue(_catalog.TryGetIngredient("ing_smoked_hare", out IngredientSpec jerky));
            Assert.IsTrue(jerky.IsEdibleRaw, "smoked goods are rations");

            Assert.IsTrue(_catalog.TryGetIngredient("ing_cheese", out IngredientSpec cheese));
            Assert.AreEqual(CuisineTraits.FermentedDurationAddMs, cheese.Traits.DurationAddMs);
        }

        [Test]
        public void SmokedGoods_CanBeFermentedAgain()
        {
            var inv = new FoodInventory();
            inv.AddIngredient("ing_beast_prime_rib", 1);

            Assert.AreEqual(ProcessRefusalReason.None, ProcessingRules.TryProcess(Process("ing_smoked_rib"), inv, Kitchen()));
            Assert.AreEqual(ProcessRefusalReason.None, ProcessingRules.TryProcess(Process("ing_aged_beast_meat"), inv, Kitchen()));

            Assert.AreEqual(1, inv.CountIngredient("ing_aged_beast_meat"));
        }

        [Test]
        public void ProcessedGoods_AreNeverYieldedByGathering()
        {
            var pool = new List<IngredientSpec>();
            foreach (FoodAcquisitionMethod source in System.Enum.GetValues(typeof(FoodAcquisitionMethod)))
            {
                _catalog.GetIngredientsBySource(source, pool);
                foreach (IngredientSpec spec in pool) Assert.IsFalse(spec.IsProcessed, spec.Id);
            }
        }

        [Test]
        public void RegisterProcess_RejectsReprocessingAndRawOutputs()
        {
            Assert.Throws<ContractException>(() =>
                _catalog.RegisterProcess(new ProcessSpec("process_bad_refreeze", ProcessKind.Freeze, "ing_chilled_sea_bass", "ing_chilled_trout")));
            Assert.Throws<ContractException>(() =>
                _catalog.RegisterProcess(new ProcessSpec("process_bad_raw_output", ProcessKind.Freeze, "ing_sea_bass", "ing_river_trout")));
            Assert.Throws<ContractException>(() =>
                _catalog.RegisterProcess(new ProcessSpec("process_bad_unknown", ProcessKind.Freeze, "ing_missing", "ing_chilled_trout")));
        }

        [Test]
        public void ProcessingRules_ViolatingContracts_ThrowsContractException()
        {
            ProcessSpec spec = Process("ing_chilled_sea_bass");
            var inv = new FoodInventory();
            Assert.Throws<ContractException>(() => ProcessingRules.CanProcess(null, inv, Kitchen()));
            Assert.Throws<ContractException>(() => ProcessingRules.CanProcess(spec, null, Kitchen()));
            Assert.Throws<ContractException>(() => ProcessingRules.TryProcess(spec, inv, Kitchen(), 0));
            Assert.Throws<ContractException>(() => ProcessingRules.HasSkillForProcess((ProcessKind)99, Kitchen()));
            Assert.Throws<ContractException>(() => new ProcessSpec("p", ProcessKind.Freeze, "same", "same"));
        }
    }
}
