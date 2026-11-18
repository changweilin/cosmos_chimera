using System;
using System.IO;
using Cosmimera.Crafting;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Save.Tests
{
    public sealed class LobbySavePresetTests
    {
        private static readonly Progression Progression = Progression.FromJson(File.ReadAllText(Path.Combine(Application.dataPath, "../Data/balance.json")));
        private static LobbySave Load(string json) => LobbySave.FromJson(json, Progression, _ => CreatureStage.Juvenile);

        private static string Ring(string id) =>
            "{ \"id\": \"" + id + "\", \"category\": \"equipment\", \"sources\": [], \"utility\": \"none\", \"quantity\": 1, \"season\": \"all\"," +
            " \"equipment\": { \"kind\": \"accessory\", \"target\": \"both\", \"player\": { \"max_hp_percent\": 1, \"move_speed_percent\": 0, \"roll_cooldown_percent\": 0 }," +
            " \"creature_effects\": [] } }";

        private static readonly CraftCatalog Catalog = new CraftCatalog("[" + Ring("ring_a") + "," + Ring("ring_b") + "]", "[]");

        private static void Stock(LobbySave s, string id) =>
            Assert.That(s.CraftStock.TryExchange(Array.Empty<CraftStack>(), new[] { new CraftStack(id, 1) }), Is.True);

        private static LobbySave Roster()
        {
            var s = new LobbySave();
            for (int i = 0; i < 3; i++) s.Add("sp" + i, VesselKind.TalismanCard, 0);
            return s;
        }

        [Test]
        public void Apply_restores_party_subs_equipment_and_appearance()
        {
            LobbySave s = Roster();
            s.SetParty(new[] { 1, 2 });
            s.SetPartners(new[] { new PartnerPair(1, 3) });
            Stock(s, "ring_a"); Stock(s, "ring_b");
            Assert.That(s.Equipment.TryEquip(0, "ring_a", "accessory", EquipmentRules.PlayerGrips, Catalog, s.CraftStock), Is.True);
            s.SetVisualVessel(1, VesselKind.MagicCube);
            s.SavePreset(0, "A");

            s.SetPartners(Array.Empty<PartnerPair>());
            s.SetParty(new[] { 3 });
            s.SetVisualVessel(1, VesselKind.TalismanCard);
            Assert.That(s.Equipment.TryEquip(0, "ring_b", "accessory", EquipmentRules.PlayerGrips, Catalog, s.CraftStock), Is.True);

            PresetApplyResult r = s.TryApplyPreset(0, Catalog);

            Assert.That(r.Applied, Is.True);
            Assert.That(r.MissingCreatures + r.MissingItems, Is.Zero);
            Assert.That(s.Party, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(s.Partners.Count, Is.EqualTo(1));
            Assert.That(s.Get(1).VisualVessel, Is.EqualTo(VesselKind.MagicCube));
            Assert.That(s.Equipment.ItemIn(0, "accessory"), Is.EqualTo("ring_a"));
            Assert.That(s.CraftStock.Count("ring_a"), Is.Zero);
            Assert.That(s.CraftStock.Count("ring_b"), Is.EqualTo(1));
        }

        [Test]
        public void Missing_item_is_counted_and_the_rest_still_applies()
        {
            LobbySave s = Roster();
            Stock(s, "ring_a");
            s.Equipment.TryEquip(0, "ring_a", "accessory", EquipmentRules.PlayerGrips, Catalog, s.CraftStock);
            s.SavePreset(1, "A");
            s.Equipment.TryUnequip(0, "accessory", s.CraftStock);
            Assert.That(s.CraftStock.TryExchange(new[] { new CraftStack("ring_a", 1) }, Array.Empty<CraftStack>()), Is.True);
            s.SetParty(new[] { 2 });

            PresetApplyResult r = s.TryApplyPreset(1, Catalog);

            Assert.That(r.Applied, Is.True);
            Assert.That(r.MissingItems, Is.EqualTo(1));
            Assert.That(s.Party, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(s.GetPreset(1).Equipment.Count, Is.EqualTo(1), "stored preset is never trimmed");
        }

        [Test]
        public void Uncovered_creature_keeps_its_gear()
        {
            LobbySave s = Roster();
            s.SetParty(new[] { 1 });
            s.SavePreset(0, "solo");
            Stock(s, "ring_a");
            Assert.That(s.Equipment.TryEquip(3, "ring_a", "accessory", EquipmentRules.PlayerGrips, Catalog, s.CraftStock), Is.True);

            Assert.That(s.TryApplyPreset(0, Catalog).Applied, Is.True);

            Assert.That(s.Equipment.ItemIn(3, "accessory"), Is.EqualTo("ring_a"));
        }

        [Test]
        public void Round_trip_and_clone_keep_presets_independent()
        {
            LobbySave s = Roster();
            Stock(s, "ring_a");
            s.Equipment.TryEquip(0, "ring_a", "accessory", EquipmentRules.PlayerGrips, Catalog, s.CraftStock);
            s.SavePreset(4, "Boss");
            LobbySave loaded = Load(s.ToJson());
            Assert.That(loaded.ToJson(), Is.EqualTo(s.ToJson()));
            Assert.That(loaded.GetPreset(4).Name, Is.EqualTo("Boss"));
            Assert.That(loaded.GetPreset(0), Is.Null);

            LobbySave clone = loaded.Clone();
            clone.ClearPreset(4);
            Assert.That(loaded.GetPreset(4), Is.Not.Null);
        }

        [Test]
        public void Rename_changes_only_the_name()
        {
            LobbySave s = Roster();
            s.SavePreset(2, "old");
            LoadoutPreset before = s.GetPreset(2);
            s.RenamePreset(2, "new");
            Assert.That(s.GetPreset(2).Name, Is.EqualTo("new"));
            Assert.That(s.GetPreset(2).Party, Is.EqualTo(before.Party));
            Assert.That(Load(s.ToJson()).GetPreset(2).Name, Is.EqualTo("new"));
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => s.RenamePreset(2, " "));
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => s.RenamePreset(0, "x"));
        }

        [Test]
        public void Older_formats_load_with_no_presets()
        {
            LobbySave s = Load("{\"version\":11,\"next_instance_id\":1,\"codex\":[]}");
            for (int i = 0; i < LobbySave.MaxPresets; i++) Assert.That(s.GetPreset(i), Is.Null);
        }

        [TestCase("\"slot\":9,\"name\":\"x\",\"party\":[1]")]
        [TestCase("\"slot\":0,\"name\":\"\",\"party\":[1]")]
        [TestCase("\"slot\":0,\"name\":\"x\",\"party\":[]")]
        [TestCase("\"slot\":0,\"name\":\"x\",\"party\":[1,1]")]
        [TestCase("\"slot\":0,\"name\":\"x\",\"party\":[1],\"partners\":[{\"main\":2,\"sub\":3}]")]
        [TestCase("\"slot\":0,\"name\":\"x\",\"party\":[1],\"equipment\":[{\"owner\":5,\"slot\":\"accessory\",\"item\":\"a\"}]")]
        [TestCase("\"slot\":0,\"name\":\"x\",\"party\":[1],\"visuals\":[{\"instance_id\":1,\"vessel\":99}]")]
        public void Invalid_preset_is_rejected(string fields)
        {
            Assert.Throws<InvalidDataException>(() => Load("{\"version\":" + LobbySave.FormatVersion +
                ",\"next_instance_id\":1,\"codex\":[],\"presets\":[{" + fields + "}]}"));
        }

        [Test]
        public void Apply_refuses_and_leaves_save_untouched_when_gear_cannot_return()
        {
            LobbySave s = Roster();
            Stock(s, "ring_a");
            s.Equipment.TryEquip(0, "ring_a", "accessory", EquipmentRules.PlayerGrips, Catalog, s.CraftStock);
            s.SavePreset(0, "A");
            Assert.That(s.CraftStock.TryExchange(Array.Empty<CraftStack>(), new[] { new CraftStack("ring_a", int.MaxValue) }), Is.True);
            string before = s.ToJson();

            PresetApplyResult r = s.TryApplyPreset(0, Catalog);

            Assert.That(r.Applied, Is.False);
            Assert.That(s.ToJson(), Is.EqualTo(before));
        }
    }
}
