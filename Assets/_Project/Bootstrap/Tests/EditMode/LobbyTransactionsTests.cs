using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Save;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // SOL P2-05 W-01/W-02: a lobby change reaches memory only after it reached disk; a failed bank is retryable.
    public sealed class LobbyTransactionsTests
    {
        private static readonly Progression P = Progression.FromJson(File.ReadAllText(
            Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Data", "balance.json")));

        private static readonly MaterialKey Light1 = new MaterialKey(Element.Light, 1);

        private static EvolutionRequirement Req() =>
            new EvolutionRequirement(20, new List<MaterialStack> { new MaterialStack(Light1, 3) }, "a_1");

        private string _dir;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cosmimera-lobbytx-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _path = Path.Combine(_dir, "lobby.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private static LobbySave Ready(int lightQty = 3)
        {
            var s = new LobbySave();
            s.ChooseStarter("line", "a", P.XpThreshold(20));
            s.AddMaterials(new[] { new MaterialStack(Light1, lightQty) });
            return s;
        }

        private LobbySave Reload() => LobbySave.Load(_path, P, _ => CreatureStage.Juvenile);

        // A directory squatting on the .tmp path makes LobbySave.Write throw before the final file is touched, on every OS
        // and even as root (CI runs Linux containers as root). The earlier FileShare lock only failed File.Replace on
        // Windows: Linux lets a file be replaced while it is open, so CI saw Committed.
        private IDisposable LockSave()
        {
            string tmp = _path + ".tmp";
            Directory.CreateDirectory(tmp);
            return new Blocker(tmp);
        }

        private sealed class Blocker : IDisposable
        {
            private readonly string _tmp;

            public Blocker(string tmp) => _tmp = tmp;

            public void Dispose()
            {
                if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true);
            }
        }

        [Test]
        public void Evolve_commits_to_disk_then_memory()
        {
            LobbySave live = Ready();
            live.Write(_path);
            CommitStatus st = LobbyTransactions.Evolve(live, 1, Req(), P, _path, out LobbySave next, out EvolutionResult r,
                out Exception error);
            Assert.That(st, Is.EqualTo(CommitStatus.Committed));
            Assert.IsNull(error);
            Assert.IsTrue(r.Succeeded);
            Assert.That(next.Get(1).SpeciesId, Is.EqualTo("a_1"));
            Assert.That(next.Materials.Count(Light1), Is.EqualTo(0));
            Assert.That(live.Get(1).SpeciesId, Is.EqualTo("a"), "the live save is never mutated");
            Assert.That(live.Materials.Count(Light1), Is.EqualTo(3));
            Assert.That(Reload().ToJson(), Is.EqualTo(next.ToJson()));
        }

        [Test]
        public void Evolve_refusal_writes_nothing()
        {
            LobbySave live = Ready(lightQty: 2);
            CommitStatus st = LobbyTransactions.Evolve(live, 1, Req(), P, _path, out LobbySave next, out EvolutionResult r, out _);
            Assert.That(st, Is.EqualTo(CommitStatus.Refused));
            Assert.That(next, Is.SameAs(live));
            Assert.That(r.Check.Shortfalls.Count, Is.EqualTo(1));
            Assert.That(live.Materials.Count(Light1), Is.EqualTo(2));
            Assert.IsFalse(File.Exists(_path));
        }

        [Test]
        public void Evolve_write_failure_keeps_memory_and_disk_unchanged()
        {
            LobbySave live = Ready();
            live.Write(_path);
            string before = File.ReadAllText(_path);
            CommitStatus st;
            LobbySave next;
            Exception error;
            using (LockSave())
            {
                st = LobbyTransactions.Evolve(live, 1, Req(), P, _path, out next, out _, out error);
            }

            Assert.That(st, Is.EqualTo(CommitStatus.WriteFailed));
            Assert.That(error, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            Assert.That(next, Is.SameAs(live));
            Assert.That(live.Get(1).SpeciesId, Is.EqualTo("a"));
            Assert.That(live.Materials.Count(Light1), Is.EqualTo(3), "materials are not consumed by an unsaved evolution");
            Assert.That(File.ReadAllText(_path), Is.EqualTo(before));
        }

        private static RunOutcome Outcome(int partyExp, params MaterialStack[] materials)
        {
            var o = new RunOutcome();
            o.PartyExp.Add(partyExp);
            o.Captured.Add(("b", VesselKind.GlassBead, 50));
            o.Materials.AddRange(materials);
            return o;
        }

        [Test]
        public void Settle_banks_xp_captures_and_materials()
        {
            LobbySave live = Ready();
            CommitStatus st = LobbyTransactions.Settle(live, new[] { 1 }, Outcome(P.XpThreshold(21), new MaterialStack(Light1, 2)),
                P, _path, out LobbySave next, out _, out Exception error);
            Assert.That(st, Is.EqualTo(CommitStatus.Committed));
            Assert.IsNull(error);
            Assert.That(next.Get(1).Exp, Is.EqualTo(P.XpThreshold(21)));
            Assert.That(next.Get(2).SpeciesId, Is.EqualTo("b"));
            Assert.That(next.Get(2).Exp, Is.EqualTo(50), "a capture keeps its own XP, not the run's reward");
            Assert.That(next.Materials.Count(Light1), Is.EqualTo(5));
            Assert.That(live.Owned.Count, Is.EqualTo(1), "the live save is never mutated");
            Assert.That(Reload().ToJson(), Is.EqualTo(next.ToJson()));
        }

        // REQ: level-progression.md#玩家等級 (D0-B2): the run's cumulative player XP is banked with the creatures'.
        [Test]
        public void Settle_banks_player_exp()
        {
            LobbySave live = Ready();
            live.SetPlayerExp(100, P);
            RunOutcome outcome = Outcome(P.XpThreshold(21));
            outcome.PlayerExp = 700;
            CommitStatus st = LobbyTransactions.Settle(live, new[] { 1 }, outcome, P, _path, out LobbySave next, out _, out _);
            Assert.That(st, Is.EqualTo(CommitStatus.Committed));
            Assert.That(next.PlayerExp, Is.EqualTo(700));
            Assert.That(live.PlayerExp, Is.EqualTo(100), "the live save is never mutated");
            Assert.That(Reload().PlayerExp, Is.EqualTo(700));

            outcome.PlayerExp = 99;
            Assert.Throws<ContractException>(() =>
                LobbyTransactions.Settle(live, new[] { 1 }, outcome, P, _path, out _, out _, out _), "a run never lowers player XP");

            outcome.PlayerExp = P.PlayerMaxExp + 1;
            Assert.Throws<ContractException>(() =>
                LobbyTransactions.Settle(live, new[] { 1 }, outcome, P, _path, out _, out _, out _), "above the player cap");
            Assert.Throws<ContractException>(() =>
                LobbyTransactions.Settle(live, new[] { 1 }, Outcome(P.XpThreshold(21)), null, _path, out _, out _, out _));
        }

        [Test]
        public void Settle_write_failure_is_retryable_without_double_banking()
        {
            LobbySave live = Ready();
            live.Write(_path);
            RunOutcome outcome = Outcome(P.XpThreshold(21), new MaterialStack(Light1, 2));
            CommitStatus st;
            LobbySave next;
            using (LockSave())
            {
                st = LobbyTransactions.Settle(live, new[] { 1 }, outcome, P, _path, out next, out _, out _);
            }

            Assert.That(st, Is.EqualTo(CommitStatus.WriteFailed));
            Assert.That(next, Is.SameAs(live));
            Assert.That(live.Owned.Count, Is.EqualTo(1));
            Assert.That(Reload().Materials.Count(Light1), Is.EqualTo(3));

            st = LobbyTransactions.Settle(live, new[] { 1 }, outcome, P, _path, out next, out _, out _);
            Assert.That(st, Is.EqualTo(CommitStatus.Committed));
            Assert.That(next.Owned.Count, Is.EqualTo(2), "one capture, not two");
            Assert.That(next.Materials.Count(Light1), Is.EqualTo(5), "materials banked once");
        }

        [Test]
        public void Settle_stock_full_names_the_key_and_changes_nothing()
        {
            LobbySave live = Ready(lightQty: int.MaxValue);
            live.Write(_path);
            string before = File.ReadAllText(_path);
            CommitStatus st = LobbyTransactions.Settle(live, new[] { 1 },
                Outcome(P.XpThreshold(21), new MaterialStack(new MaterialKey(Element.Fire, 1), 1), new MaterialStack(Light1, 1)),
                P, _path, out LobbySave next, out MaterialKey overflow, out _);
            Assert.That(st, Is.EqualTo(CommitStatus.StockFull));
            Assert.That(overflow, Is.EqualTo(Light1));
            Assert.That(next, Is.SameAs(live));
            Assert.That(live.Get(1).Exp, Is.EqualTo(P.XpThreshold(20)), "no XP applied either");
            Assert.That(live.Owned.Count, Is.EqualTo(1));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(before));
        }

        [Test]
        public void Preconditions()
        {
            LobbySave live = Ready();
            Assert.Throws<ContractException>(() => LobbyTransactions.TryWrite(null, _path, out _));
            Assert.Throws<ContractException>(() => LobbyTransactions.TryWrite(live, "", out _));
            Assert.Throws<ContractException>(() =>
                LobbyTransactions.Evolve(null, 1, Req(), P, _path, out _, out _, out _));
            Assert.Throws<ContractException>(() =>
                LobbyTransactions.Settle(live, new[] { 1 }, new RunOutcome(), P, _path, out _, out _, out _), "PartyExp count");
            Assert.Throws<ContractException>(() =>
                LobbyTransactions.Settle(live, null, Outcome(0), P, _path, out _, out _, out _));
            // R2-04: a data refusal (Refused / StockFull) must not mask a caller bug.
            Assert.Throws<ContractException>(() =>
                LobbyTransactions.Evolve(Ready(lightQty: 1), 1, Req(), P, "", out _, out _, out _), "refused with empty path");
            LobbySave full = Ready(lightQty: int.MaxValue);
            Assert.Throws<ContractException>(() => LobbyTransactions.Settle(full, new[] { 99 },
                Outcome(0, new MaterialStack(Light1, 1)), P, _path, out _, out _, out _), "stock full with unowned party id");
            Assert.Throws<ContractException>(() => LobbyTransactions.Settle(full, new[] { 1 },
                Outcome(0, new MaterialStack(Light1, 1)), P, "", out _, out _, out _), "stock full with empty path");
        }
    }
}
