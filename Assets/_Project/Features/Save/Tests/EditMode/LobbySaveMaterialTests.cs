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
        public void Format_2_loads_with_an_empty_stock_and_writes_format_3()
        {
            LobbySave r = Parse("{\"version\":2,\"next_instance_id\":2," + Owned +
                ",\"materials\":[{\"element\":\"light\",\"tier\":1,\"qty\":5}]}");
            Assert.That(r.LoadedFormatVersion, Is.EqualTo(2));
            Assert.That(r.Materials.Snapshot().Count, Is.EqualTo(0), "a v2 file never had a stock; nothing is reconstructed");
            Assert.That(r.ToJson(), Does.Contain("\"version\": 3"));
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
    }
}
