using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using NUnit.Framework;

namespace Cosmimera.Save.Tests
{
    public sealed class LobbySaveTests
    {
        [Test]
        public void Round_trips_through_json()
        {
            var s = new LobbySave();
            s.ChooseStarter("line_sun", "sun_0");
            s.Add("wood_fern_chick", VesselKind.GlassBead);
            LobbySave r = LobbySave.FromJson(s.ToJson());
            Assert.That(r.StarterLine, Is.EqualTo("line_sun"));
            Assert.That(r.Owned.Count, Is.EqualTo(2));
            Assert.That(r.Owned[1].Vessel, Is.EqualTo(VesselKind.GlassBead));
            CollectionAssert.AreEqual(s.Party, r.Party);
            Assert.That(r.Add("x", VesselKind.MagicCube).InstanceId, Is.EqualTo(3), "next id survives the round trip");
        }

        [Test]
        public void Party_fills_to_six_then_captures_go_to_storage()
        {
            var s = new LobbySave();
            for (int i = 0; i < 8; i++)
            {
                s.Add("c" + i, VesselKind.TalismanCard);
            }

            Assert.That(s.Party.Count, Is.EqualTo(6));
            Assert.That(s.Owned.Count, Is.EqualTo(8));
        }

        [Test]
        public void Starter_can_only_be_chosen_once()
        {
            var s = new LobbySave();
            s.ChooseStarter("line_sun", "sun_0");
            Assert.Throws<ContractException>(() => s.ChooseStarter("line_moon", "moon_0"));
        }

        [Test]
        public void Newer_format_is_refused()
        {
            string json = new LobbySave().ToJson().Replace("\"version\": 1", "\"version\": 99");
            Assert.Throws<ContractException>(() => LobbySave.FromJson(json));
        }

        [Test]
        public void Party_entry_must_be_owned()
        {
            var s = new LobbySave();
            s.Add("a", VesselKind.TalismanCard);
            Assert.Throws<ContractException>(() => s.SetParty(new[] { 1, 2 }));
        }

        [Test]
        public void Write_then_load_from_disk()
        {
            string path = Path.Combine(Path.GetTempPath(), "cosmimera-save-test", "lobby.json");
            var s = new LobbySave();
            s.ChooseStarter("line_star", "star_0");
            s.Write(path);
            s.Add("b", VesselKind.GlassBead);
            s.Write(path);
            LobbySave r = LobbySave.Load(path);
            Assert.That(r.Owned.Count, Is.EqualTo(2));
            File.Delete(path);
            Assert.That(LobbySave.Load(path).Owned.Count, Is.EqualTo(0), "missing file = new save");
        }
    }
}
