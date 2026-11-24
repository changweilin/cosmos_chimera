using System;
using System.IO;
using Cosmimera.Crafting;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Save.Tests
{
    // REQ: docs/design/gear-looks.md#外觀與數值 — per-copy looks and the last departed planet survive saves (format 14).
    public sealed class LobbySaveLookTests
    {
        private static readonly Progression Progression = Progression.FromJson(File.ReadAllText(Path.Combine(Application.dataPath, "../Data/balance.json")));
        private static readonly GearOrigin Storm = new GearOrigin("storm_planet", ulong.MaxValue - 3);
        private static LobbySave Load(string json) => LobbySave.FromJson(json, Progression, _ => CreatureStage.Juvenile);

        private static readonly CraftCatalog Catalog = new CraftCatalog("[" +
            "{ \"id\": \"ring_a\", \"category\": \"equipment\", \"sources\": [], \"utility\": \"none\", \"quantity\": 1, \"season\": \"all\"," +
            " \"equipment\": { \"kind\": \"accessory\", \"target\": \"both\", \"player\": { \"max_hp_percent\": 1, \"move_speed_percent\": 0, \"roll_cooldown_percent\": 0 }," +
            " \"creature_effects\": [] } }]", "[]");

        private static LobbySave WithRings(params ulong[] seeds)
        {
            var save = new LobbySave();
            save.Add("a", VesselKind.TalismanCard, 0);
            Assert.That(save.CraftStock.TryExchange(Array.Empty<CraftStack>(), new[] { new CraftStack("ring_a", seeds.Length + 1) }), Is.True);
            var looks = new ItemLook[seeds.Length];
            for (int i = 0; i < seeds.Length; i++) looks[i] = new ItemLook(Storm, seeds[i]);
            save.CraftStock.RestoreLooks(new[] { new ItemLookList("ring_a", looks) });
            return save;
        }

        [Test]
        public void Looks_equipped_looks_and_last_planet_round_trip()
        {
            LobbySave save = WithRings(ulong.MaxValue, 7, 8);
            Assert.That(save.Equipment.TryEquip(0, "ring_a", "accessory", EquipmentRules.PlayerGrips, Catalog, save.CraftStock, 2), Is.True);
            save.SetLastPlanet(Storm);
            LobbySave loaded = Load(save.ToJson());
            Assert.That(loaded.ToJson(), Is.EqualTo(save.ToJson()));
            Assert.That(loaded.LastPlanet, Is.EqualTo(Storm));
            Assert.That(loaded.CraftStock.LookOf("ring_a", 1), Is.EqualTo(new ItemLook(Storm, ulong.MaxValue)));
            Assert.That(loaded.Equipment.TryGetEntry(0, "accessory", out EquippedItem worn), Is.True);
            Assert.That(worn.Look, Is.EqualTo(new ItemLook(Storm, 7)));
            Assert.That(loaded.Clone().ToJson(), Is.EqualTo(save.ToJson()));
        }

        [Test]
        public void Format_13_loads_untagged_with_the_cradle_planet()
        {
            LobbySave save = Load("{\"version\":13,\"next_instance_id\":1,\"codex\":[]," +
                "\"craft_stock\":[{\"id\":\"ring_a\",\"qty\":2}],\"equipment\":[{\"owner\":0,\"slot\":\"accessory\",\"item\":\"ring_a\"}]}");
            Assert.That(save.LastPlanet, Is.EqualTo(GearOrigin.Cradle));
            Assert.That(save.CraftStock.LookSnapshot(), Is.Empty);
            Assert.That(save.CraftStock.LookOf("ring_a", 1), Is.EqualTo(ItemLook.Default("ring_a", 1)));
            Assert.That(save.Equipment.TryGetEntry(0, "accessory", out EquippedItem worn) && worn.Look.Equals(ItemLook.Default("ring_a", 0)), Is.True);
        }

        [TestCase("\"craft_stock\":[{\"id\":\"ring_a\",\"qty\":1,\"looks\":[{\"planet\":\"Bad\",\"planet_seed\":0,\"seed\":0}]}]")]
        [TestCase("\"craft_stock\":[{\"id\":\"ring_a\",\"qty\":1,\"looks\":[{\"planet\":\"a\",\"planet_seed\":0,\"seed\":0},{\"planet\":\"a\",\"planet_seed\":0,\"seed\":1}]}]")]
        [TestCase("\"equipment\":[{\"owner\":0,\"slot\":\"accessory\",\"item\":\"ring_a\",\"look\":{\"planet\":\"Bad\",\"planet_seed\":0,\"seed\":0}}]")]
        [TestCase("\"last_planet\":{\"planet\":\"Bad\",\"seed\":0}")]
        public void Invalid_looks_are_rejected(string fields)
        {
            Assert.Throws<InvalidDataException>(() => Load("{\"version\":14,\"next_instance_id\":1,\"codex\":[]," + fields + "}"));
        }

        [Test]
        public void Preset_re_equips_the_copy_it_remembers()
        {
            LobbySave save = WithRings(1, 2, 3);
            save.SetParty(new[] { 1 });
            Assert.That(save.Equipment.TryEquip(0, "ring_a", "accessory", EquipmentRules.PlayerGrips, Catalog, save.CraftStock, 2), Is.True);
            save.SavePreset(0, "A");
            Assert.That(save.Equipment.TryEquip(0, "ring_a", "accessory", EquipmentRules.PlayerGrips, Catalog, save.CraftStock, 0), Is.True);
            Assert.That(save.TryApplyPreset(0, Catalog).Applied, Is.True);
            Assert.That(save.Equipment.TryGetEntry(0, "accessory", out EquippedItem worn), Is.True);
            Assert.That(worn.Look, Is.EqualTo(new ItemLook(Storm, 2)));
            Assert.That(save.CraftStock.Count("ring_a"), Is.EqualTo(3));
        }

        [Test]
        public void Set_last_planet_requires_a_constructed_origin()
        {
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => new LobbySave().SetLastPlanet(default));
        }
    }
}
