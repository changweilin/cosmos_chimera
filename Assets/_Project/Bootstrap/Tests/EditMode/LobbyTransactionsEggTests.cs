using System.Collections.Generic;
using System.IO;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Capture;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Save;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // P4-05: a run's egg progress reaches the hatchery only through the same all-or-nothing settlement as XP.
    public sealed class LobbyTransactionsEggTests
    {
        private static readonly Progression P = Progression.FromJson(File.ReadAllText(
            Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Data", "balance.json")));

        private static readonly EggCatalog Catalog = EggCatalog.FromJson(
            "[{\"id\":\"egg_a\",\"outcomes\":[{\"species\":\"hatchling\",\"weight\":1}]}]");

        private string _dir;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cosmimera-eggtx-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, "lobby.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private static LobbySave WithEggs(params IncubatingEgg[] eggs)
        {
            var s = new LobbySave();
            s.ChooseStarter("line", "a", P.XpThreshold(20));
            s.SetEggs(eggs);
            return s;
        }

        private static RunOutcome Outcome(LobbySave live, long ppm)
        {
            var o = new RunOutcome { EggProgressPpm = ppm, PlayerExp = live.PlayerExp };
            o.PartyExp.Add(live.Get(1).Exp);
            return o;
        }

        [Test]
        public void Progress_is_banked_into_incubating_eggs_and_persisted()
        {
            LobbySave live = WithEggs(new IncubatingEgg("egg_a", 0, 100_000));
            Assert.That(LobbyTransactions.Settle(live, live.Party, Outcome(live, 250_000), P, _path,
                out LobbySave next, out _, out _), Is.EqualTo(CommitStatus.Committed));
            Assert.That(next.Eggs[0].ProgressPpm, Is.EqualTo(350_000));
            Assert.That(live.Eggs[0].ProgressPpm, Is.EqualTo(100_000), "the live save is never mutated");
            Assert.That(LobbySave.Load(_path, P, _ => CreatureStage.Juvenile).Eggs[0].ProgressPpm, Is.EqualTo(350_000));
        }

        [Test]
        public void Ready_egg_hatches_into_an_owned_creature_with_the_inherited_vessel()
        {
            LobbySave live = WithEggs(new IncubatingEgg("egg_a", (int)VesselKind.GlassBead, 900_000), new IncubatingEgg("egg_a", 0));
            var hatch = new HatchSettlement { Catalog = Catalog, Seed = 5 };
            Assert.That(LobbyTransactions.Settle(live, live.Party, Outcome(live, 100_000), P, _path,
                out LobbySave next, out _, out _, hatch), Is.EqualTo(CommitStatus.Committed));
            Assert.That(next.Eggs.Count, Is.EqualTo(1));
            Assert.That(next.Eggs[0].ProgressPpm, Is.EqualTo(100_000));
            Assert.That(next.Owned.Count, Is.EqualTo(2));
            Assert.That(next.Owned[1].SpeciesId, Is.EqualTo("hatchling"));
            Assert.That(next.Owned[1].Vessel, Is.EqualTo(VesselKind.GlassBead));
            Assert.That(next.Owned[1].Exp, Is.Zero);
            Assert.That(hatch.Hatched, Is.EqualTo(new List<(string, string)> { ("egg_a", "hatchling") }));
        }

        [Test]
        public void Without_a_hatch_context_a_ready_egg_waits()
        {
            LobbySave live = WithEggs(new IncubatingEgg("egg_a", 0, 900_000));
            LobbyTransactions.Settle(live, live.Party, Outcome(live, 500_000), P, _path, out LobbySave next, out _, out _);
            Assert.That(next.Eggs[0].IsReady, Is.True);
            Assert.That(next.Owned.Count, Is.EqualTo(1));
        }

        [Test]
        public void Egg_with_a_retired_id_stays_incubating()
        {
            LobbySave live = WithEggs(new IncubatingEgg("egg_retired", 0, 900_000));
            var hatch = new HatchSettlement { Catalog = Catalog, Seed = 1 };
            LobbyTransactions.Settle(live, live.Party, Outcome(live, 500_000), P, _path, out LobbySave next, out _, out _, hatch);
            Assert.That(next.Eggs.Count, Is.EqualTo(1));
            Assert.That(next.Owned.Count, Is.EqualTo(1));
            Assert.That(hatch.Hatched, Is.Empty);
        }

        [Test]
        public void Failed_write_changes_nothing_so_the_hatch_can_be_retried()
        {
            LobbySave live = WithEggs(new IncubatingEgg("egg_a", 0, 900_000));
            Directory.CreateDirectory(_path + ".tmp");
            var hatch = new HatchSettlement { Catalog = Catalog, Seed = 1 };
            Assert.That(LobbyTransactions.Settle(live, live.Party, Outcome(live, 500_000), P, _path,
                out LobbySave same, out _, out _, hatch), Is.EqualTo(CommitStatus.WriteFailed));
            Assert.That(same, Is.SameAs(live));
            Assert.That(live.Eggs[0].ProgressPpm, Is.EqualTo(900_000));
            Assert.That(hatch.Hatched, Is.Empty);
        }
    }
}
