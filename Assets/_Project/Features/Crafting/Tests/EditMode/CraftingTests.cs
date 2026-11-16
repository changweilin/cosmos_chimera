using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Crafting.Tests
{
    public sealed class CraftingTests
    {
        private CraftCatalog _catalog;
        private CraftInventory _bag;
        private static readonly CraftStack[] Empty = Array.Empty<CraftStack>();
        private static readonly HashSet<FieldUtility> AllSkills = new HashSet<FieldUtility>((FieldUtility[])Enum.GetValues(typeof(FieldUtility)));
        private static readonly HashSet<FieldUtility> NoSkills = new HashSet<FieldUtility>();
        private static string Data(string folder) => File.ReadAllText(Path.Combine(Application.dataPath,
            "../Data/_incoming/life-crafting-draft-01", folder, folder + ".json"));

        [SetUp]
        public void SetUp() { _catalog = new CraftCatalog(Data("craft-items"), Data("craft-recipes")); _bag = new CraftInventory(); }
        private void Add(string id, int count) => Assert.That(_bag.TryExchange(Empty, new[] { new CraftStack(id, count) }), Is.True);
        private CraftRecipe Recipe(string id)
        {
            foreach (CraftRecipe recipe in _catalog.Recipes) if (recipe.Id == id) return recipe;
            throw new InvalidOperationException(id);
        }

        [Test]
        public void Exchange_sums_repeated_costs_and_cannot_pay_costs_with_outputs()
        {
            Add("raw_hemp", 3);
            var costs = new[] { new CraftStack("raw_hemp", 2), new CraftStack("raw_hemp", 2) };
            Assert.That(_bag.TryExchange(costs, new[] { new CraftStack("raw_hemp", 10) }), Is.False);
            Assert.That(_bag.Count("raw_hemp"), Is.EqualTo(3));
            Assert.That(_bag.TryExchange(new[] { new CraftStack("raw_hemp", 3) }, new[] { new CraftStack("raw_hemp", 2) }), Is.True);
            Assert.That(_bag.Count("raw_hemp"), Is.EqualTo(2));
        }

        [Test]
        public void Overflow_never_consumes_inputs_or_truncates_outputs()
        {
            Add("raw_hemp", 2); Add("craft_linen", int.MaxValue);
            CraftRecipe recipe = Recipe("recipe_weave_linen");
            Assert.That(CraftRules.TryCraft(recipe, AllSkills, _bag), Is.EqualTo(CraftResult.CapacityExceeded));
            Assert.That(_bag.Count("raw_hemp"), Is.EqualTo(2));
            Assert.That(_bag.Count("craft_linen"), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void Inventory_preconditions_and_snapshots_are_independent()
        {
            Assert.Throws<ContractException>(() => new CraftStack("bad-id", 1));
            Assert.Throws<ContractException>(() => new CraftStack("a", 0));
            Assert.Throws<ContractException>(() => _bag.Count(null));
            Assert.Throws<ContractException>(() => _bag.TryExchange(null, Empty));
            Assert.Throws<ContractException>(() => _bag.TryExchange(Empty, new[] { default(CraftStack) }));
            Add("z", 1); Add("a", 1);
            IReadOnlyList<CraftStack> snapshot = _bag.Snapshot(); Add("a", 1);
            Assert.That(snapshot[0].Id, Is.EqualTo("a")); Assert.That(snapshot[0].Quantity, Is.EqualTo(1));
            Assert.That(_bag.TryExchange(new[] { new CraftStack("a", 2) }, Empty), Is.True);
            Assert.That(_bag.Count("a"), Is.Zero);
        }

        [Test]
        public void Nodes_require_skill_season_and_only_deplete_after_success()
        {
            var gathering = new CraftGathering(); var yields = new List<CraftStack>();
            Assert.That(gathering.TryGather(1, "shrub", "summer", _catalog, NoSkills, _bag, yields), Is.EqualTo(CraftResult.MissingSkill));
            Assert.That(gathering.TryGather(1, "shrub", "winter", _catalog, AllSkills, _bag, yields), Is.EqualTo(CraftResult.NoResource));
            Assert.That(gathering.TryGather(1, "shrub", "summer", _catalog, AllSkills, _bag, yields), Is.EqualTo(CraftResult.Success));
            int count = _bag.Count("raw_cotton"); Assert.That(count, Is.Positive);
            Assert.That(gathering.TryGather(1, "shrub", "summer", _catalog, AllSkills, _bag, yields), Is.EqualTo(CraftResult.Depleted));
            Assert.That(yields, Is.Empty); Assert.That(_bag.Count("raw_cotton"), Is.EqualTo(count));
            Assert.Throws<ContractException>(() => gathering.Preview(-1, "shrub", "summer", _catalog, AllSkills, _bag, yields));
            Assert.Throws<ContractException>(() => gathering.Preview(1, "shrub", "wrong", _catalog, AllSkills, _bag, yields));
        }

        [Test]
        public void Full_bag_keeps_node_available_and_pickaxe_replaces_mining_skill()
        {
            var gathering = new CraftGathering(); var yields = new List<CraftStack>();
            Add("craft_pickaxe", 1); Add("raw_iron_ore", int.MaxValue);
            Assert.That(gathering.TryGather(2, "ore_iron", "spring", _catalog, NoSkills, _bag, yields), Is.EqualTo(CraftResult.CapacityExceeded));
            Assert.That(_bag.TryExchange(new[] { new CraftStack("raw_iron_ore", int.MaxValue) }, Empty), Is.True);
            Assert.That(gathering.TryGather(2, "ore_iron", "spring", _catalog, NoSkills, _bag, yields), Is.EqualTo(CraftResult.Success));
            Assert.That(_bag.Count("craft_pickaxe"), Is.EqualTo(1));
        }

        [Test]
        public void Recipe_checks_skills_materials_and_reusable_tools_before_consumption()
        {
            CraftRecipe recipe = Recipe("recipe_linen_outfit"); Add("craft_linen", 2); Add("craft_dye", 1);
            Assert.That(CraftRules.TryCraft(recipe, NoSkills, _bag), Is.EqualTo(CraftResult.MissingSkill));
            Assert.That(CraftRules.TryCraft(recipe, AllSkills, _bag), Is.EqualTo(CraftResult.MissingTool));
            Assert.That(_bag.Count("craft_linen"), Is.EqualTo(2)); Add("craft_needle", 1);
            Assert.That(CraftRules.TryCraft(recipe, AllSkills, _bag), Is.EqualTo(CraftResult.Success));
            Assert.That(_bag.Count("craft_needle"), Is.EqualTo(1)); Assert.That(_bag.Count("craft_linen_outfit"), Is.EqualTo(1));
            Assert.That(CraftRules.TryCraft(recipe, AllSkills, _bag), Is.EqualTo(CraftResult.MissingMaterials));
            Assert.Throws<ContractException>(() => CraftRules.CanCraft(null, AllSkills, _bag));
        }

        [Test]
        public void Equipment_reserves_copies_and_refuses_wrong_target_and_overflowing_returns()
        {
            var gear = new EquipmentLoadout(); Add("craft_iron_blade", 1);
            Assert.That(gear.TryEquip(0, "craft_iron_blade", _catalog, _bag), Is.False);
            Assert.That(gear.TryEquip(1, "craft_iron_blade", _catalog, _bag), Is.True);
            Assert.That(gear.TryEquip(2, "craft_iron_blade", _catalog, _bag), Is.False);
            Add("craft_iron_blade", int.MaxValue);
            Assert.That(gear.TryUnequip(1, "weapon", _bag), Is.False);
            Assert.That(gear.Snapshot().Count, Is.EqualTo(1));
            Assert.That(_bag.TryExchange(new[] { new CraftStack("craft_iron_blade", 1) }, Empty), Is.True);
            Assert.That(gear.TryUnequip(1, "weapon", _bag), Is.True);
            Assert.That(_bag.Count("craft_iron_blade"), Is.EqualTo(int.MaxValue));
            Assert.Throws<ContractException>(() => gear.Restore(new[] { new EquippedItem(0, "armor", "a"), new EquippedItem(0, "armor", "b") }));
        }

        [Test]
        public void Equipment_stats_apply_without_changing_roll_duration_or_granting_retired_item_bonuses()
        {
            var entries = new[] { new EquippedItem(0, "armor", "craft_iron_guard"), new EquippedItem(0, "accessory", "craft_gem_pendant") };
            var baseline = new PlayerVitalsConfig(100, 300, 200, 1000);
            PlayerVitalsConfig modified = EquipmentRules.ApplyPlayer(baseline, EquipmentRules.Collect(0, entries, _catalog));
            Assert.That(modified.MaxHp, Is.GreaterThan(baseline.MaxHp));
            Assert.That(modified.RollCooldownMs, Is.LessThan(baseline.RollCooldownMs));
            Assert.That(modified.RollDurationMs, Is.EqualTo(baseline.RollDurationMs));
            var retired = new[] { new EquippedItem(0, "armor", "retired_outfit") };
            Assert.That(EquipmentRules.ApplyPlayer(baseline, EquipmentRules.Collect(0, retired, _catalog)).MaxHp, Is.EqualTo(baseline.MaxHp));
            Assert.Throws<InvalidDataException>(() => EquipmentRules.Collect(0, new[] { new EquippedItem(0, "armor", "craft_iron_blade") }, _catalog));
        }

        [Test]
        public void Catalog_refuses_bad_references_and_duplicate_ids()
        {
            JArray items = JArray.Parse(Data("craft-items")); items.Add(items[0].DeepClone());
            Assert.Throws<InvalidDataException>(() => new CraftCatalog(items.ToString(), Data("craft-recipes")));
            JArray recipes = JArray.Parse(Data("craft-recipes")); recipes[0]["costs"][0]["item"] = "missing_item";
            Assert.Throws<InvalidDataException>(() => new CraftCatalog(Data("craft-items"), recipes.ToString()));
            Assert.Throws<InvalidDataException>(() => new CraftCatalog("[{", "[]"));
            Assert.Throws<ContractException>(() => new CraftCatalog(null, "[]"));
        }
    }
}
