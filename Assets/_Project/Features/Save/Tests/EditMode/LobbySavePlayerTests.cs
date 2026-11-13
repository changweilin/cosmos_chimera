using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Save.Tests
{
    // REQ: level-progression.md#玩家等級 (D0-B2) — cumulative player XP, save format 4.
    public sealed class LobbySavePlayerTests
    {
        private static readonly Progression P = Progression.FromJson(File.ReadAllText(
            Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Data", "balance.json")));

        private static LobbySave Parse(string json) => LobbySave.FromJson(json, P, _ => CreatureStage.Juvenile);

        private const string Owned = "\"party\":[1],\"owned\":[{\"instance_id\":1,\"species_id\":\"a\",\"vessel\":0,\"exp\":0}]";

        [Test]
        public void New_save_starts_at_zero_player_exp()
        {
            Assert.That(new LobbySave().PlayerExp, Is.EqualTo(0));
        }

        [Test]
        public void Player_exp_round_trips_and_clones()
        {
            var s = new LobbySave();
            s.Add("a", VesselKind.TalismanCard, 0);
            s.SetPlayerExp(12345, P);
            Assert.That(Parse(s.ToJson()).PlayerExp, Is.EqualTo(12345));
            LobbySave c = s.Clone();
            Assert.That(c.PlayerExp, Is.EqualTo(12345));
            c.SetPlayerExp(1, P);
            Assert.That(s.PlayerExp, Is.EqualTo(12345), "the clone is independent");
        }

        [Test]
        public void Format_3_loads_with_zero_player_exp_and_writes_format_4()
        {
            LobbySave r = Parse("{\"version\":3,\"next_instance_id\":2," + Owned + ",\"player_exp\":999}");
            Assert.That(r.LoadedFormatVersion, Is.EqualTo(3));
            Assert.That(r.PlayerExp, Is.EqualTo(0), "a v3 file never had player XP; nothing is reconstructed");
            Assert.That(r.ToJson(), Does.Contain("\"version\": 4"));
        }

        [Test]
        public void Player_exp_above_the_cap_is_clipped()
        {
            LobbySave r = Parse("{\"version\":4,\"next_instance_id\":2," + Owned + ",\"player_exp\":2147483647}");
            Assert.That(r.PlayerExp, Is.EqualTo(P.PlayerMaxExp));
        }

        [Test]
        public void Negative_player_exp_is_refused()
        {
            Assert.Throws<InvalidDataException>(() => Parse("{\"version\":4,\"next_instance_id\":2," + Owned + ",\"player_exp\":-1}"));
            Assert.Throws<ContractException>(() => new LobbySave().SetPlayerExp(-1, P));
        }

        // SOL W-B2-SOL-04: the setter's domain equals what FromJson keeps, so a set value always survives Write/Load.
        [Test]
        public void Set_player_exp_accepts_the_cap_and_refuses_above_it()
        {
            var s = new LobbySave();
            s.Add("a", VesselKind.TalismanCard, 0);
            s.SetPlayerExp(P.PlayerMaxExp, P);
            Assert.That(Parse(s.ToJson()).PlayerExp, Is.EqualTo(P.PlayerMaxExp));
            Assert.Throws<ContractException>(() => s.SetPlayerExp(P.PlayerMaxExp + 1, P));
            Assert.Throws<ContractException>(() => s.SetPlayerExp(0, null));
        }
    }
}
