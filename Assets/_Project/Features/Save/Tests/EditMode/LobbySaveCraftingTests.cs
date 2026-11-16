using System;
using System.IO;
using Cosmimera.Crafting;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Save.Tests
{
    public sealed class LobbySaveCraftingTests
    {
        private static readonly Progression Progression = Progression.FromJson(File.ReadAllText(Path.Combine(Application.dataPath, "../Data/balance.json")));
        private static LobbySave Load(string json) => LobbySave.FromJson(json, Progression, _ => CreatureStage.Juvenile);

        [Test]
        public void Crafting_and_equipment_round_trip_and_clone_without_sharing_state()
        {
            var save = new LobbySave(); save.Add("a", VesselKind.TalismanCard, 0);
            save.CraftStock.TryExchange(Array.Empty<CraftStack>(), new[] { new CraftStack("raw_hemp", 5), new CraftStack("retired_gem", int.MaxValue) });
            save.Equipment.Restore(new[] { new EquippedItem(1, "armor", "retired_guard") });
            LobbySave loaded = Load(save.ToJson());
            Assert.That(loaded.ToJson(), Is.EqualTo(save.ToJson()));
            LobbySave clone = loaded.Clone();
            Assert.That(clone.CraftStock.TryExchange(new[] { new CraftStack("raw_hemp", 2) }, Array.Empty<CraftStack>()), Is.True);
            clone.Equipment.Restore(Array.Empty<EquippedItem>());
            Assert.That(loaded.CraftStock.Count("raw_hemp"), Is.EqualTo(5));
            Assert.That(loaded.Equipment.Snapshot().Count, Is.EqualTo(1));
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void Older_formats_start_without_crafting_stock_or_equipment(int version)
        {
            LobbySave save = Load("{\"version\":" + version + ",\"next_instance_id\":1,\"craft_stock\":[{\"id\":\"old\",\"qty\":1}],\"equipment\":[{\"owner\":0,\"slot\":\"armor\",\"item\":\"old\"}]}");
            Assert.That(save.CraftStock.Snapshot(), Is.Empty);
            Assert.That(save.Equipment.Snapshot(), Is.Empty);
        }

        [TestCase("\"craft_stock\":[null]")]
        [TestCase("\"craft_stock\":[{\"id\":\"bad-id\",\"qty\":1}]")]
        [TestCase("\"craft_stock\":[{\"id\":\"a\",\"qty\":0}]")]
        [TestCase("\"craft_stock\":[{\"id\":\"a\",\"qty\":1},{\"id\":\"a\",\"qty\":1}]")]
        [TestCase("\"equipment\":[{\"owner\":8,\"slot\":\"armor\",\"item\":\"a\"}]")]
        [TestCase("\"equipment\":[{\"owner\":0,\"slot\":\"hat\",\"item\":\"a\"}]")]
        [TestCase("\"equipment\":[{\"owner\":0,\"slot\":\"armor\",\"item\":\"a\"},{\"owner\":0,\"slot\":\"armor\",\"item\":\"b\"}]")]
        public void Invalid_stock_or_equipment_is_rejected(string fields)
        {
            Assert.Throws<InvalidDataException>(() => Load("{\"version\":7,\"next_instance_id\":1," + fields + "}"));
        }

        [Test]
        public void Writing_an_unknown_equipment_owner_is_refused()
        {
            var save = new LobbySave(); save.Equipment.Restore(new[] { new EquippedItem(1, "armor", "a") });
            Assert.Throws<InvalidOperationException>(() => save.ToJson());
        }
    }
}
