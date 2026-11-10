using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Save.Tests
{
    // REQ: creatures.md#進化 (Q-C32 option B) — lobby-wide material stock, save format 3.
    public sealed class LobbySaveMaterialTests
    {
        private static readonly Progression P = Progression.FromJson(File.ReadAllText(
            Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Data", "balance.json")));

        private static LobbySave Parse(string json) => LobbySave.FromJson(json, P, _ => CreatureStage.Juvenile);

        private static MaterialStack Stack(Element e, int tier, int qty) => new MaterialStack(new MaterialKey(e, tier), qty);

        private const string Owned = "\"party\":[1],\"owned\":[{\"instance_id\":1,\"species_id\":\"a\",\"vessel\":0,\"exp\":0}]";

        [Test]
        public void Materials_round_trip_through_json()
        {
            var s = new LobbySave();
            s.Add("a", VesselKind.TalismanCard, 0);
            s.AddMaterials(new[] { Stack(Element.Light, 1, 2), Stack(Element.Thunder, 3, 1), Stack(Element.Light, 1, 1) });
            string json = s.ToJson();
            Assert.That(json, Does.Contain("\"light\""), "element ids are readable names, not enum numbers");
            LobbySave r = Parse(json);
            Assert.That(r.Materials.Count(new MaterialKey(Element.Light, 1)), Is.EqualTo(3));
            Assert.That(r.Materials.Count(new MaterialKey(Element.Thunder, 3)), Is.EqualTo(1));
            Assert.That(r.Materials.Snapshot().Count, Is.EqualTo(2));
        }

        [Test]
        public void Format_2_loads_with_an_empty_stock_and_writes_the_current_format()
        {
            LobbySave r = Parse("{\"version\":2,\"next_instance_id\":2," + Owned +
                ",\"materials\":[{\"element\":\"light\",\"tier\":1,\"qty\":5}]}");
            Assert.That(r.LoadedFormatVersion, Is.EqualTo(2));
            Assert.That(r.Materials.Snapshot().Count, Is.EqualTo(0), "a v2 file never had a stock; nothing is reconstructed");
            Assert.That(r.ToJson(), Does.Contain("\"version\": " + LobbySave.FormatVersion));
        }

        [TestCase("{\"element\":\"plasma\",\"tier\":1,\"qty\":1}", TestName = "Unknown element is refused")]
        [TestCase("{\"element\":\"8\",\"tier\":1,\"qty\":1}", TestName = "Numeric element id is refused")]
        [TestCase("{\"element\":\"Light\",\"tier\":1,\"qty\":1}", TestName = "Element id is case sensitive")]
        [TestCase("{\"element\":\"light\",\"tier\":0,\"qty\":1}", TestName = "Tier 0 is refused")]
        [TestCase("{\"element\":\"light\",\"tier\":4,\"qty\":1}", TestName = "Tier above 3 is refused")]
        [TestCase("{\"element\":\"light\",\"tier\":1,\"qty\":0}", TestName = "Zero qty is refused")]
        [TestCase("{\"element\":\"light\",\"tier\":1,\"qty\":-3}", TestName = "Negative qty is refused")]
        [TestCase("{\"element\":\"light\",\"tier\":1,\"qty\":1},{\"element\":\"light\",\"tier\":1,\"qty\":1}",
            TestName = "Duplicate element and tier is refused")]
        public void Bad_material_entry_is_refused(string entries)
        {
            Assert.Throws<InvalidDataException>(() => Parse("{\"version\":3,\"next_instance_id\":2," + Owned +
                ",\"materials\":[" + entries + "]}"));
        }

        [Test]
        public void AddMaterials_is_all_or_nothing_on_overflow()
        {
            var s = new LobbySave();
            s.AddMaterials(new[] { Stack(Element.Fire, 1, int.MaxValue - 1) });
            Assert.Throws<InvalidOperationException>(() =>
                s.AddMaterials(new[] { Stack(Element.Water, 2, 7), Stack(Element.Fire, 1, 1), Stack(Element.Fire, 1, 1) }));
            Assert.That(s.Materials.Count(new MaterialKey(Element.Water, 2)), Is.EqualTo(0), "no partial deposit");
            Assert.That(s.Materials.Count(new MaterialKey(Element.Fire, 1)), Is.EqualTo(int.MaxValue - 1));
            s.AddMaterials(new[] { Stack(Element.Fire, 1, 1) });
            Assert.That(s.Materials.Count(new MaterialKey(Element.Fire, 1)), Is.EqualTo(int.MaxValue), "exact cap fits");
        }

        [Test]
        public void AddMaterials_requires_stacks()
        {
            Assert.Throws<ContractException>(() => new LobbySave().AddMaterials(null));
            var s = new LobbySave();
            s.AddMaterials(new List<MaterialStack>());
            Assert.That(s.Materials.Snapshot().Count, Is.EqualTo(0));
        }

        [Test]
        public void SetSpecies_replaces_only_the_form()
        {
            var s = new LobbySave();
            s.Add("a", VesselKind.GlassBead, 0);
            s.Add("b", VesselKind.MagicCube, 1234);
            s.SetSpecies(2, "b_1");
            Assert.That(s.Get(2).SpeciesId, Is.EqualTo("b_1"));
            Assert.That(s.Get(2).Exp, Is.EqualTo(1234));
            Assert.That(s.Get(2).Vessel, Is.EqualTo(VesselKind.MagicCube));
            CollectionAssert.AreEqual(new[] { 1, 2 }, s.Party);
            Assert.Throws<ContractException>(() => s.SetSpecies(3, "x"), "not owned");
            Assert.Throws<ContractException>(() => s.SetSpecies(1, ""), "empty species");
        }

        [Test]
        public void Clone_is_equal_and_independent()
        {
            var s = new LobbySave();
            s.ChooseStarter("line", "a", 10);
            s.Add("b", VesselKind.GlassBead, 7);
            s.AddMaterials(new[] { Stack(Element.Light, 1, 3) });
            LobbySave c = s.Clone();
            Assert.That(c.ToJson(), Is.EqualTo(s.ToJson()));
            Assert.That(c.LoadedFormatVersion, Is.EqualTo(s.LoadedFormatVersion));

            c.SetExp(1, 999);
            c.SetSpecies(2, "b_1");
            c.SetParty(new[] { 2 });
            c.Add("z", VesselKind.MagicCube, 0);
            c.AddMaterials(new[] { Stack(Element.Light, 1, 1), Stack(Element.Fire, 2, 1) });
            Assert.That(s.Get(1).Exp, Is.EqualTo(10));
            Assert.That(s.Get(2).SpeciesId, Is.EqualTo("b"));
            CollectionAssert.AreEqual(new[] { 1, 2 }, s.Party);
            Assert.That(s.Owned.Count, Is.EqualTo(2));
            Assert.That(s.Materials.Count(new MaterialKey(Element.Light, 1)), Is.EqualTo(3));
            Assert.That(s.Materials.Count(new MaterialKey(Element.Fire, 2)), Is.EqualTo(0));
            Assert.That(c.Add("y", VesselKind.MagicCube, 0).InstanceId, Is.EqualTo(s.Add("y", VesselKind.MagicCube, 0).InstanceId + 1),
                "the id cursor is copied, not shared");
        }

        [Test]
        public void WouldOverflow_names_the_lowest_full_key_and_changes_nothing()
        {
            var s = new LobbySave();
            s.AddMaterials(new[] { Stack(Element.Fire, 2, int.MaxValue), Stack(Element.Wood, 3, int.MaxValue - 1) });
            Assert.IsFalse(s.WouldOverflow(new[] { Stack(Element.Wood, 3, 1), Stack(Element.Light, 1, 9) }, out MaterialKey none));
            Assert.That(none, Is.EqualTo(default(MaterialKey)));
            Assert.IsTrue(s.WouldOverflow(new[] { Stack(Element.Fire, 2, 1), Stack(Element.Wood, 3, 1), Stack(Element.Wood, 3, 1) },
                out MaterialKey full));
            Assert.That(full, Is.EqualTo(new MaterialKey(Element.Wood, 3)), "Snapshot order: Wood (1) before Fire (3)");
            Assert.That(s.Materials.Count(new MaterialKey(Element.Wood, 3)), Is.EqualTo(int.MaxValue - 1));
            Assert.Throws<ContractException>(() => s.WouldOverflow(null, out _));
        }

        [Test]
        public void Stacks_outside_the_save_domain_are_refused_whole()
        {
            var s = new LobbySave();
            Assert.Throws<ArgumentException>(() =>
                s.AddMaterials(new[] { Stack(Element.Light, 1, 1), Stack(Element.Light, MaterialDrops.MaxTier + 1, 1) }));
            Assert.Throws<ArgumentException>(() => s.AddMaterials(new[] { Stack(Element.Light, 1, 1), default }));
            Assert.Throws<ArgumentException>(() => s.WouldOverflow(new MaterialStack[] { default }, out _));
            Assert.That(s.Materials.Snapshot().Count, Is.EqualTo(0), "no partial deposit");
        }

        [Test]
        public void ToJson_refuses_a_stock_it_could_not_read_back()
        {
            // Materials is exposed so TryEvolve can consume from it; its Add accepts any tier >= 1 (SOL W-04).
            var s = new LobbySave();
            s.Materials.Add(new MaterialKey(Element.Light, MaterialDrops.MaxTier + 1), 1);
            Assert.Throws<InvalidOperationException>(() => s.ToJson());
            string dir = Path.Combine(Path.GetTempPath(), "cosmimera-save-w04-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "lobby.json");
            try
            {
                Assert.Throws<InvalidOperationException>(() => s.Write(path));
                Assert.IsFalse(File.Exists(path), "nothing unreadable reaches the disk");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
