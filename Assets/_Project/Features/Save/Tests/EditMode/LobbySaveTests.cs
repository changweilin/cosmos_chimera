using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Save.Tests
{
    public sealed class LobbySaveTests
    {
        private static readonly Progression P = Progression.FromJson(File.ReadAllText(
            Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Data", "balance.json")));

        private static CreatureStage? Stage(string speciesId) =>
            speciesId.EndsWith("_2") ? CreatureStage.Adult : speciesId.EndsWith("_1") ? CreatureStage.Adolescent
            : speciesId == "unknown" ? (CreatureStage?)null : CreatureStage.Juvenile;

        private static LobbySave Parse(string json) => LobbySave.FromJson(json, P, Stage);

        [Test]
        public void Round_trips_through_json()
        {
            var s = new LobbySave();
            s.ChooseStarter("line_sun", "sun_0", P.XpThreshold(10));
            s.Add("wood_fern_chick", VesselKind.GlassBead, 1234);
            LobbySave r = Parse(s.ToJson());
            Assert.That(r.StarterLine, Is.EqualTo("line_sun"));
            Assert.That(r.Owned.Count, Is.EqualTo(2));
            Assert.That(r.Owned[1].Vessel, Is.EqualTo(VesselKind.GlassBead));
            Assert.That(r.Owned[0].Exp, Is.EqualTo(P.XpThreshold(10)));
            Assert.That(r.Owned[1].Exp, Is.EqualTo(1234));
            Assert.That(r.LoadedFormatVersion, Is.EqualTo(LobbySave.FormatVersion));
            CollectionAssert.AreEqual(s.Party, r.Party);
            Assert.That(r.Add("x", VesselKind.MagicCube, 0).InstanceId, Is.EqualTo(3), "next id survives the round trip");
        }

        [Test]
        public void Party_fills_to_six_then_captures_go_to_storage()
        {
            var s = new LobbySave();
            for (int i = 0; i < 8; i++)
            {
                s.Add("c" + i, VesselKind.TalismanCard, 0);
            }

            Assert.That(s.Party.Count, Is.EqualTo(6));
            Assert.That(s.Owned.Count, Is.EqualTo(8));
        }

        [Test]
        public void Starter_can_only_be_chosen_once()
        {
            var s = new LobbySave();
            s.ChooseStarter("line_sun", "sun_0", 0);
            Assert.Throws<ContractException>(() => s.ChooseStarter("line_moon", "moon_0", 0));
        }

        [Test]
        public void Newer_format_is_refused()
        {
            string json = new LobbySave().ToJson().Replace("\"version\": 2", "\"version\": 99");
            Assert.Throws<InvalidDataException>(() => Parse(json), "must hold in Release too (SOL D0 H2)");
        }

        [Test]
        public void Party_entry_must_be_owned()
        {
            var s = new LobbySave();
            s.Add("a", VesselKind.TalismanCard, 0);
            Assert.Throws<ContractException>(() => s.SetParty(new[] { 1, 2 }));
        }

        [Test]
        public void Setting_the_party_to_itself_keeps_it()
        {
            // SOL D0 H1: Party is the live list; SetParty(Party) used to clear its own input.
            var s = new LobbySave();
            s.ChooseStarter("line_sun", "sun_0", 0);
            s.Add("b", VesselKind.GlassBead, 0);
            s.SetParty(s.Party);
            Assert.That(s.Party.Count, Is.EqualTo(2));
            Assert.That(Parse(s.ToJson()).Party.Count, Is.EqualTo(2));
        }

        [Test]
        public void Corrupt_entries_are_refused()
        {
            Assert.Throws<InvalidDataException>(() => Parse("{}"));
            Assert.Throws<InvalidDataException>(() => Parse(
                "{\"version\":2,\"next_instance_id\":1,\"owned\":[{\"instance_id\":5,\"species_id\":\"a\",\"vessel\":0,\"exp\":0}]}"),
                "an owned id at or above next_instance_id would be handed out twice");
            Assert.Throws<InvalidDataException>(() => Parse(
                "{\"version\":2,\"next_instance_id\":2,\"owned\":[{\"instance_id\":1,\"species_id\":\"a\",\"vessel\":7,\"exp\":0}]}"),
                "undefined vessel");
            Assert.Throws<InvalidDataException>(() => Parse(
                "{\"version\":2,\"next_instance_id\":2,\"owned\":[{\"instance_id\":1,\"species_id\":\"a\",\"vessel\":0,\"exp\":-1}]}"),
                "negative exp");
        }

        // SOL P2-12 R3: the party is the XP settlement roster; a duplicate or oversized party would be paid twice.
        [Test]
        public void Invalid_parties_are_refused()
        {
            const string one = "\"owned\":[{\"instance_id\":1,\"species_id\":\"a\",\"vessel\":0,\"exp\":0}]";
            Assert.Throws<InvalidDataException>(() => Parse("{\"version\":2,\"next_instance_id\":2,\"party\":[1,1]," + one + "}"),
                "duplicate");
            Assert.Throws<InvalidDataException>(() => Parse(
                "{\"version\":2,\"next_instance_id\":2,\"party\":[1,1,1,1,1,1,1]," + one + "}"), "over 6");
            Assert.Throws<InvalidDataException>(() => Parse("{\"version\":2,\"next_instance_id\":2," + one + "}"),
                "owned without a party");
            var seven = new LobbySave();
            for (int i = 0; i < 7; i++)
            {
                seven.Add("a", VesselKind.TalismanCard, 0);
            }

            Assert.That(Parse(seven.ToJson()).Party.Count, Is.EqualTo(LobbySave.MaxParty), "a legal full party round-trips");
        }

        // SOL P2-12 R4: the cursor is validated even with no creatures, and exhaustion never writes an unreadable save.
        [Test]
        public void Instance_id_cursor_is_validated_and_exhaustion_leaves_the_save_unchanged()
        {
            Assert.Throws<InvalidDataException>(() => Parse("{\"version\":2}"), "missing cursor");
            Assert.Throws<InvalidDataException>(() => Parse("{\"version\":2,\"next_instance_id\":0}"));
            Assert.That(Parse(new LobbySave().ToJson()).Owned.Count, Is.EqualTo(0), "a new empty save round-trips");

            LobbySave full = Parse("{\"version\":2,\"next_instance_id\":2147483647}");
            string before = full.ToJson();
            Assert.Throws<System.InvalidOperationException>(() => full.Add("a", VesselKind.TalismanCard, 0));
            Assert.That(full.ToJson(), Is.EqualTo(before));
            Assert.That(Parse(before).Owned.Count, Is.EqualTo(0));
        }

        // REQ: level-progression.md#獎勵量 option A — old evolution XP is not cumulative level XP; restart at T(floor).
        [Test]
        public void Format_1_migrates_to_the_stage_floor_and_ignores_old_exp()
        {
            LobbySave r = Parse("{\"version\":1,\"next_instance_id\":4,\"party\":[1,2,3],\"owned\":[" +
                "{\"instance_id\":1,\"species_id\":\"a_0\",\"vessel\":0,\"exp\":999}," +
                "{\"instance_id\":2,\"species_id\":\"a_1\",\"vessel\":0,\"exp\":0}," +
                "{\"instance_id\":3,\"species_id\":\"a_2\",\"vessel\":1,\"exp\":5}]}");
            Assert.That(r.LoadedFormatVersion, Is.EqualTo(1));
            Assert.That(r.Owned[0].Exp, Is.EqualTo(P.XpThreshold(10)));
            Assert.That(r.Owned[1].Exp, Is.EqualTo(P.XpThreshold(20)));
            Assert.That(r.Owned[2].Exp, Is.EqualTo(P.XpThreshold(35)));
            Assert.That(r.Owned[2].Vessel, Is.EqualTo(VesselKind.GlassBead));
            Assert.That(r.ToJson(), Does.Contain("\"version\": 2"));
        }

        [Test]
        public void Format_1_with_an_unknown_species_is_refused()
        {
            Assert.Throws<InvalidDataException>(() => Parse(
                "{\"version\":1,\"next_instance_id\":2,\"owned\":[{\"instance_id\":1,\"species_id\":\"unknown\",\"vessel\":0,\"exp\":0}]}"));
        }

        [Test]
        public void Exp_above_the_cap_is_clipped_on_load()
        {
            LobbySave r = Parse("{\"version\":2,\"next_instance_id\":2,\"party\":[1],\"owned\":[{\"instance_id\":1,\"species_id\":\"a\",\"vessel\":0,\"exp\":2147483647}]}");
            Assert.That(r.Owned[0].Exp, Is.EqualTo(P.MaxExp));
        }

        [Test]
        public void SetExp_replaces_only_exp()
        {
            var s = new LobbySave();
            s.Add("a", VesselKind.GlassBead, 10);
            s.SetExp(1, 500);
            Assert.That(s.Get(1).Exp, Is.EqualTo(500));
            Assert.That(s.Get(1).Vessel, Is.EqualTo(VesselKind.GlassBead));
            Assert.Throws<ContractException>(() => s.SetExp(2, 0));
            Assert.Throws<ContractException>(() => s.SetExp(1, -1));
        }

        [Test]
        public void Write_then_load_from_disk()
        {
            string path = Path.Combine(Path.GetTempPath(), "cosmimera-save-test", "lobby.json");
            var s = new LobbySave();
            s.ChooseStarter("line_star", "star_0", 0);
            s.Write(path);
            s.Add("b", VesselKind.GlassBead, 0);
            s.Write(path);
            LobbySave r = LobbySave.Load(path, P, Stage);
            Assert.That(r.Owned.Count, Is.EqualTo(2));
            File.Delete(path);
            Assert.That(LobbySave.Load(path, P, Stage).Owned.Count, Is.EqualTo(0), "missing file = new save");
        }

        [Test]
        public void Loading_a_format_1_file_keeps_a_backup_once()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cosmimera-save-test-v1");
            string path = Path.Combine(dir, "lobby.json");
            string backup = path + ".v1.bak";
            Directory.CreateDirectory(dir);
            File.Delete(backup);
            const string v1 = "{\"version\":1,\"next_instance_id\":2,\"party\":[1],\"owned\":[{\"instance_id\":1,\"species_id\":\"a_0\",\"vessel\":0,\"exp\":7}]}";
            File.WriteAllText(path, v1);
            LobbySave r = LobbySave.Load(path, P, Stage);
            Assert.That(File.ReadAllText(backup), Is.EqualTo(v1));
            r.Write(path);
            File.WriteAllText(path, v1.Replace("\"exp\":7", "\"exp\":8"));
            LobbySave.Load(path, P, Stage);
            Assert.That(File.ReadAllText(backup), Is.EqualTo(v1), "an existing backup is never overwritten");
            File.Delete(backup);
            File.Delete(path);
        }
    }
}
