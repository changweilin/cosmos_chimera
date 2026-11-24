using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Save.Tests
{
    // REQ: docs/design/cuisine.md#6 — recipe discovery (format 16).
    public sealed class LobbySaveRecipeTests
    {
        private static readonly Progression Progression = Progression.FromJson(
            File.ReadAllText(Path.Combine(Application.dataPath, "../Data/balance.json")));

        private static LobbySave Load(string json) =>
            LobbySave.FromJson(json, Progression, _ => CreatureStage.Juvenile);

        [Test]
        public void Format_16_round_trips_discovered_recipes_sorted()
        {
            Assert.That(LobbySave.FormatVersion, Is.GreaterThanOrEqualTo(16));
            var save = new LobbySave();
            Assert.That(save.IsRecipeDiscovered("recipe_food_steamed_sea_bass"), Is.False);
            Assert.That(save.RecordRecipeDiscovered("recipe_food_steamed_sea_bass"), Is.True);
            Assert.That(save.RecordRecipeDiscovered("recipe_food_aged_beast_ragout"), Is.True);
            Assert.That(save.RecordRecipeDiscovered("recipe_food_steamed_sea_bass"), Is.False);

            LobbySave reloaded = Load(save.ToJson());
            Assert.That(reloaded.LoadedFormatVersion, Is.EqualTo(LobbySave.FormatVersion));
            CollectionAssert.AreEqual(new[] { "recipe_food_aged_beast_ragout", "recipe_food_steamed_sea_bass" },
                reloaded.DiscoveredRecipesSnapshot());
            Assert.That(reloaded.ToJson(), Is.EqualTo(save.ToJson()));
        }

        [Test]
        public void Format_15_loads_with_no_discovered_recipes()
        {
            LobbySave save = Load("{\"version\":15,\"next_instance_id\":1,\"codex\":[]}");
            Assert.That(save.LoadedFormatVersion, Is.EqualTo(15));
            Assert.That(save.DiscoveredRecipesSnapshot(), Is.Empty);
        }

        [Test]
        public void Format_15_ignores_a_discovered_recipes_field()
        {
            LobbySave save = Load("{\"version\":15,\"next_instance_id\":1,\"codex\":[],\"discovered_recipes\":[\"recipe_a\"]}");
            Assert.That(save.DiscoveredRecipesSnapshot(), Is.Empty);
        }

        [TestCase("\"discovered_recipes\":[\"BadId\"]")]
        [TestCase("\"discovered_recipes\":[\"recipe_a\",\"recipe_a\"]")]
        public void Corrupt_v16_discovered_recipes_are_refused(string fields)
        {
            Assert.Throws<InvalidDataException>(() =>
                Load("{\"version\":16,\"next_instance_id\":1,\"codex\":[]," + fields + "}"));
        }

        [Test]
        public void Clone_copies_discovered_recipes_independently()
        {
            var save = new LobbySave();
            save.RecordRecipeDiscovered("recipe_a");
            LobbySave clone = save.Clone();
            Assert.That(clone.ToJson(), Is.EqualTo(save.ToJson()));
            clone.RecordRecipeDiscovered("recipe_b");
            Assert.That(save.IsRecipeDiscovered("recipe_b"), Is.False);
            Assert.That(clone.IsRecipeDiscovered("recipe_a"), Is.True);
        }

        [Test]
        public void Recipe_discovery_enforces_contracts()
        {
            var save = new LobbySave();
            Assert.Throws<ContractException>(() => save.RecordRecipeDiscovered("Bad-Id"));
            Assert.Throws<ContractException>(() => save.IsRecipeDiscovered(""));
        }
    }
}
