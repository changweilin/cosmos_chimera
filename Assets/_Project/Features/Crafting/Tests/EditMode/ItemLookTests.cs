using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Crafting.Tests
{
    // REQ: docs/design/gear-looks.md#外觀與數值 — each crafted copy keeps its own look through bag, equipment and recipes.
    public sealed class ItemLookTests
    {
        private static readonly CraftStack[] Empty = Array.Empty<CraftStack>();
        private static readonly HashSet<FieldUtility> AllSkills = new HashSet<FieldUtility>((FieldUtility[])Enum.GetValues(typeof(FieldUtility)));
        private static readonly GripMode[] Dual = { GripMode.DualWield };
        private static readonly GearOrigin Lava = new GearOrigin("lava_planet", 77);
        private CraftCatalog _catalog;
        private CraftInventory _bag;

        private static string Data(string folder) => File.ReadAllText(Path.Combine(Application.dataPath,
            "../Data/_incoming/life-crafting-draft-01", folder, folder + ".json"));

        [SetUp]
        public void SetUp() { _catalog = new CraftCatalog(Data("craft-items"), Data("craft-recipes")); _bag = new CraftInventory(); }

        private void Add(string id, int count) => Assert.That(_bag.TryExchange(Empty, new[] { new CraftStack(id, count) }), Is.True);

        private static ItemLook Look(ulong seed) => new ItemLook(Lava, seed);

        private void AddTagged(string id, params ulong[] seeds)
        {
            Add(id, seeds.Length);
            var looks = new List<ItemLookList>(_bag.LookSnapshot());
            var mine = new List<ItemLook>();
            ItemLookList existing = looks.Find(l => l.Id == id);
            if (existing != null) mine.AddRange(existing.Looks);
            foreach (ulong s in seeds) mine.Add(Look(s));
            looks.RemoveAll(l => l.Id == id);
            looks.Add(new ItemLookList(id, mine));
            _bag.RestoreLooks(looks);
        }

        private (CraftRecipe recipe, string gear) GearRecipe()
        {
            foreach (CraftRecipe recipe in _catalog.Recipes)
            {
                foreach (CraftStack gain in recipe.Gains)
                {
                    if (_catalog.TryGetItem(gain.Id, out CraftItem item) && item.Equipment != null && recipe.Gains.Count == 1 && gain.Quantity == 1)
                        return (recipe, gain.Id);
                }
            }
            throw new InvalidOperationException("no single-output gear recipe in data");
        }

        private void Stock(CraftRecipe recipe, int times)
        {
            foreach (CraftStack cost in recipe.Costs) Add(cost.Id, cost.Quantity * times);
            foreach (string tool in recipe.Tools) if (_bag.Count(tool) == 0) Add(tool, 1);
        }

        [Test]
        public void Crafted_gear_is_tagged_with_its_origin_and_materials_are_not()
        {
            (CraftRecipe recipe, string gear) = GearRecipe();
            Stock(recipe, 2);
            Assert.That(CraftRules.TryCraft(recipe, AllSkills, _bag, _catalog, Lava), Is.EqualTo(CraftResult.Success));
            Assert.That(CraftRules.TryCraft(recipe, AllSkills, _bag, _catalog, Lava), Is.EqualTo(CraftResult.Success));
            Assert.That(_bag.Count(gear), Is.EqualTo(2));
            ItemLook first = _bag.LookOf(gear, 0), second = _bag.LookOf(gear, 1);
            Assert.That(first.Origin, Is.EqualTo(Lava));
            Assert.That(second.Origin, Is.EqualTo(Lava));
            Assert.That(first.Seed, Is.Not.EqualTo(second.Seed));
            foreach (ItemLookList list in _bag.LookSnapshot())
            {
                Assert.That(_catalog.TryGetItem(list.Id, out CraftItem item) && item.Equipment != null, Is.True, list.Id + " is not gear");
            }
        }

        [Test]
        public void Untagged_copies_read_stable_default_looks()
        {
            Add("craft_iron_blade", 3);
            Assert.That(_bag.LookOf("craft_iron_blade", 1), Is.EqualTo(ItemLook.Default("craft_iron_blade", 1)));
            Assert.That(_bag.LookOf("craft_iron_blade", 0).Origin, Is.EqualTo(GearOrigin.Cradle));
            Assert.That(ItemLook.Default("craft_iron_blade", 1), Is.Not.EqualTo(ItemLook.Default("craft_iron_blade", 2)));
            Assert.That(_bag.LookSnapshot(), Is.Empty);
        }

        [Test]
        public void Consumption_takes_the_newest_tagged_copies_first()
        {
            Add("craft_iron_blade", 2);
            AddTagged("craft_iron_blade", 10, 11);
            Assert.That(_bag.TryExchange(new[] { new CraftStack("craft_iron_blade", 1) }, Empty), Is.True);
            Assert.That(_bag.Count("craft_iron_blade"), Is.EqualTo(3));
            Assert.That(_bag.LookOf("craft_iron_blade", 2), Is.EqualTo(Look(10)));
            Assert.That(_bag.TryExchange(new[] { new CraftStack("craft_iron_blade", 2) }, Empty), Is.True);
            Assert.That(_bag.LookOf("craft_iron_blade", 0), Is.EqualTo(ItemLook.Default("craft_iron_blade", 0)));
            Assert.That(_bag.LookSnapshot(), Is.Empty);
        }

        [Test]
        public void Equipping_a_chosen_copy_moves_its_look_and_unequipping_returns_it()
        {
            AddTagged("craft_iron_blade", 1, 2, 3);
            var gear = new EquipmentLoadout();
            Assert.That(gear.TryEquip(1, "craft_iron_blade", "main_hand", Dual, _catalog, _bag, 1), Is.True);
            Assert.That(gear.TryGetEntry(1, "main_hand", out EquippedItem worn), Is.True);
            Assert.That(worn.Look, Is.EqualTo(Look(2)));
            Assert.That(_bag.LookOf("craft_iron_blade", 0), Is.EqualTo(Look(1)));
            Assert.That(_bag.LookOf("craft_iron_blade", 1), Is.EqualTo(Look(3)));

            // Swapping in another copy sends the worn one back with its look.
            Assert.That(gear.TryEquip(1, "craft_iron_blade", "main_hand", Dual, _catalog, _bag, 0), Is.True);
            Assert.That(_bag.FindCopy("craft_iron_blade", Look(2)), Is.GreaterThanOrEqualTo(0));
            Assert.That(_bag.FindCopy("craft_iron_blade", Look(1)), Is.EqualTo(-1));

            Assert.That(gear.TryUnequip(1, "main_hand", _bag), Is.True);
            Assert.That(_bag.Count("craft_iron_blade"), Is.EqualTo(3));
            Assert.That(_bag.FindCopy("craft_iron_blade", Look(1)), Is.EqualTo(2), "returned copy is the newest");
        }

        [Test]
        public void Equipping_an_untagged_copy_keeps_the_other_untagged_looks()
        {
            Add("craft_iron_blade", 3);
            ItemLook last = _bag.LookOf("craft_iron_blade", 2);
            var gear = new EquipmentLoadout();
            Assert.That(gear.TryEquip(1, "craft_iron_blade", "main_hand", Dual, _catalog, _bag, 0), Is.True);
            Assert.That(gear.TryGetEntry(1, "main_hand", out EquippedItem worn) && worn.Look.Equals(ItemLook.Default("craft_iron_blade", 0)), Is.True);
            Assert.That(_bag.LookOf("craft_iron_blade", 1), Is.EqualTo(last));
        }

        [Test]
        public void Copy_from_is_independent_and_carries_looks()
        {
            AddTagged("craft_iron_blade", 5);
            var copy = new CraftInventory();
            copy.CopyFrom(_bag);
            Assert.That(copy.LookOf("craft_iron_blade", 0), Is.EqualTo(Look(5)));
            Assert.That(copy.TryExchange(new[] { new CraftStack("craft_iron_blade", 1) }, Empty), Is.True);
            Assert.That(_bag.Count("craft_iron_blade"), Is.EqualTo(1));
            Assert.That(_bag.LookSnapshot().Count, Is.EqualTo(1));
        }

        [Test]
        public void Look_preconditions_are_enforced()
        {
            Add("craft_iron_blade", 1);
            Assert.Throws<ContractException>(() => _bag.LookOf("craft_iron_blade", 1));
            Assert.Throws<ContractException>(() => _bag.LookOf("craft_iron_blade", -1));
            Assert.Throws<ContractException>(() => _bag.RestoreLooks(new[] { new ItemLookList("craft_iron_blade", new[] { Look(1), Look(2) }) }));
            Assert.Throws<ContractException>(() => _bag.RestoreLooks(null));
            Assert.Throws<ContractException>(() => _bag.TryMoveCopies("craft_iron_blade", 1, Array.Empty<ItemCopy>(), out _));
            Assert.Throws<ContractException>(() => _bag.TryMoveCopies(null, 0, null, out _));
            Assert.Throws<ContractException>(() => _bag.CopyFrom(null));
            Assert.Throws<ContractException>(() => new GearOrigin("Bad-Id", 0));
            Assert.Throws<ContractException>(() => new ItemLook(default, 0));
            Assert.Throws<ContractException>(() => ItemLook.Default("craft_iron_blade", -1));
            Assert.Throws<ContractException>(() => new EquipmentLoadout().TryEquip(1, "craft_iron_blade", "main_hand", Dual, _catalog, _bag, 1));
        }

        [Test]
        public void Move_refuses_overflow_without_changes()
        {
            Add("craft_iron_blade", int.MaxValue);
            Assert.That(_bag.TryMoveCopies(null, 0, new[] { new ItemCopy("craft_iron_blade", Look(9)) }, out _), Is.False);
            Assert.That(_bag.Count("craft_iron_blade"), Is.EqualTo(int.MaxValue));
            Assert.That(_bag.LookSnapshot(), Is.Empty);
        }

        [Test]
        public void Crafted_looks_are_deterministic_and_differ_by_planet()
        {
            Assert.That(ItemLook.Crafted("craft_iron_blade", Lava, 3), Is.EqualTo(ItemLook.Crafted("craft_iron_blade", Lava, 3)));
            Assert.That(ItemLook.Crafted("craft_iron_blade", Lava, 3).Seed,
                Is.Not.EqualTo(ItemLook.Crafted("craft_iron_blade", new GearOrigin("lava_planet", 78), 3).Seed));
        }
    }
}
