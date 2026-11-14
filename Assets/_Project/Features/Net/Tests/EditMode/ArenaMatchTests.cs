using System.Collections.Generic;
using System.IO;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Net.Api;
using Cosmimera.Net.Runtime.Model;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Net.Tests
{
    public sealed class ArenaMatchTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;
            public bool IsSea(float3 direction) => false;
            public bool IsBlocked(float3 direction, float clearanceM) => false;
        }

        private readonly List<Object> _created = new List<Object>();
        private PlanetConfig _planet;
        private CombatBalance _balance;
        private MoveDef _bite;
        private CreatureDef _fighter;
        private CreatureDef _adultFighter;

        [SetUp]
        public void SetUp()
        {
            _planet = new PlanetConfig(640f, float3.zero);
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            _balance = CombatBalance.FromJson(File.ReadAllText(Path.Combine(root, "Data", "balance.json")));
            _bite = CreateMove("arena_bite", 45, MoveShape.Front, RangeClass.Melee, 2500, 1000);
            _fighter = CreateCreature("arena_fighter", _bite, CreatureStage.Juvenile, 25);
            _adultFighter = CreateCreature("arena_adult", _bite, CreatureStage.Adult, 40);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        private MoveDef CreateMove(string id, int power, MoveShape shape, RangeClass range, int baseRangeMm, int cooldownMs)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId(id);
            m.Set(Element.Fire, MoveKind.Damage, power, null, 0, 0, cooldownMs, 100, range, shape, baseRangeMm,
                MoveMovement.None, BurstOrigin.Caster);
            return m;
        }

        private CreatureDef CreateCreature(string id, MoveDef move, CreatureStage stage, int statValue)
        {
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            c.Set("line_" + id, stage, CreatureSizeClass.M, AiStyle.Charge, Temperament.Fierce, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 20) },
                new CreatureStats(statValue, statValue, statValue, statValue), move, null, "{}", false);
            return c;
        }

        private ArenaParticipantConfig MakeParticipant(int id, int team, int count = 1, int level = 50,
            bool autoRefill = true, bool overload = false)
        {
            var roster = new List<ArenaCreatureSpec>(count);
            var pool = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                roster.Add(new ArenaCreatureSpec(overload ? _adultFighter : _fighter, null, level, overload));
                if (i < TeamState.MaxPool)
                {
                    pool.Add(i);
                }
            }

            return new ArenaParticipantConfig(id, team, roster, pool, autoRefill);
        }

        private static int RunToFinish(ArenaMatch match, int maxTicks = 2000, int dtMs = 50)
        {
            for (int t = 1; t <= maxTicks; t++)
            {
                match.Tick(t * (long)dtMs, dtMs);
                if (match.Phase == ArenaPhase.Finished)
                {
                    return t;
                }
            }

            return -1;
        }

        [Test]
        public void Contracts_reject_invalid_configs_and_lifecycle_order()
        {
            var cfg = new ArenaConfig(ArenaMode.FreeForAll, new float3(0f, 1f, 0f), 25f, 42UL);
            Assert.Throws<ContractException>(() => new ArenaConfig(ArenaMode.FreeForAll, new float3(0f, 0f, 0f), 25f, 1UL));
            Assert.Throws<ContractException>(() => new ArenaConfig(ArenaMode.FreeForAll, new float3(0f, 1f, 0f), 2f, 1UL));

            // 1 participant is invalid for FFA
            Assert.Throws<ContractException>(() => new ArenaMatch(_planet, _balance, new FlatSurface(), cfg,
                new[] { MakeParticipant(0, 0) }));

            // Duplicate team in FFA is invalid
            Assert.Throws<ContractException>(() => new ArenaMatch(_planet, _balance, new FlatSurface(), cfg,
                new[] { MakeParticipant(0, 0), MakeParticipant(1, 0) }));

            // 2v2 requires 4 participants (2 per team)
            var cfg2v2 = new ArenaConfig(ArenaMode.Teams2v2, new float3(0f, 1f, 0f), 25f, 42UL);
            Assert.Throws<ContractException>(() => new ArenaMatch(_planet, _balance, new FlatSurface(), cfg2v2,
                new[] { MakeParticipant(0, 0), MakeParticipant(1, 1) }));

            var match = new ArenaMatch(_planet, _balance, new FlatSurface(), cfg,
                new[] { MakeParticipant(0, 0), MakeParticipant(1, 1) });

            // Tick before Start fails
            Assert.Throws<ContractException>(() => match.Tick(50, 50));

            match.Start(0);
            // Double Start fails
            Assert.Throws<ContractException>(() => match.Start(10));
            // Time going backwards fails
            Assert.Throws<ContractException>(() => match.Tick(-1, 50));
            match.Tick(100, 50);
            Assert.Throws<ContractException>(() => match.Tick(99, 50));
        }

        [Test]
        public void FreeForAll_2_players_finishes_with_valid_winner()
        {
            var cfg = new ArenaConfig(ArenaMode.FreeForAll, new float3(0f, 1f, 0f), 25f, 101UL);
            var match = new ArenaMatch(_planet, _balance, new FlatSurface(), cfg,
                new[]
                {
                    MakeParticipant(0, 0, count: 1, level: 80),
                    MakeParticipant(1, 1, count: 1, level: 10),
                });

            match.Start(0);
            Assert.That(match.Phase, Is.EqualTo(ArenaPhase.Active));
            Assert.That(match.Participants[0].Team.Active.Count, Is.EqualTo(1));
            Assert.That(match.Participants[1].Team.Active.Count, Is.EqualTo(1));

            int ticks = RunToFinish(match);
            Assert.That(ticks, Is.GreaterThan(0), "match should finish within maxTicks");
            Assert.That(match.Phase, Is.EqualTo(ArenaPhase.Finished));
            Assert.That(match.WinningTeamIndex, Is.EqualTo(0));
            Assert.That(match.Participants[1].IsEliminated, Is.True);
            Assert.That(match.Participants[0].IsEliminated, Is.False);
            Assert.That(match.Participants[0].Team.InCombat, Is.False);
        }

        [Test]
        public void FreeForAll_4_players_finishes_with_at_most_one_surviving_team()
        {
            var cfg = new ArenaConfig(ArenaMode.FreeForAll, new float3(0f, 1f, 0f), 30f, 202UL);
            var match = new ArenaMatch(_planet, _balance, new FlatSurface(), cfg,
                new[]
                {
                    MakeParticipant(0, 0, count: 2, level: 60),
                    MakeParticipant(1, 1, count: 2, level: 40),
                    MakeParticipant(2, 2, count: 2, level: 40),
                    MakeParticipant(3, 3, count: 2, level: 40),
                });

            match.Start(0);
            int ticks = RunToFinish(match, maxTicks: 3000);
            Assert.That(ticks, Is.GreaterThan(0));
            Assert.That(match.Phase, Is.EqualTo(ArenaPhase.Finished));

            int aliveCount = 0;
            for (int i = 0; i < match.Participants.Count; i++)
            {
                if (!match.Participants[i].IsEliminated)
                {
                    aliveCount++;
                }
            }

            Assert.That(aliveCount, Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void Teams2v2_never_friendly_fires_and_finishes_cleanly()
        {
            var cfg = new ArenaConfig(ArenaMode.Teams2v2, new float3(0f, 1f, 0f), 30f, 303UL);
            var match = new ArenaMatch(_planet, _balance, new FlatSurface(), cfg,
                new[]
                {
                    MakeParticipant(0, 0, count: 1, level: 80),
                    MakeParticipant(1, 0, count: 1, level: 80),
                    MakeParticipant(2, 1, count: 1, level: 15),
                    MakeParticipant(3, 1, count: 1, level: 15),
                });

            match.Start(0);
            int ticks = RunToFinish(match);
            Assert.That(ticks, Is.GreaterThan(0));
            Assert.That(match.Phase, Is.EqualTo(ArenaPhase.Finished));
            Assert.That(match.WinningTeamIndex, Is.EqualTo(0));
            Assert.That(match.Participants[0].IsEliminated, Is.False);
            Assert.That(match.Participants[1].IsEliminated, Is.False);
            Assert.That(match.Participants[2].IsEliminated, Is.True);
            Assert.That(match.Participants[3].IsEliminated, Is.True);
        }

        [Test]
        public void Same_seed_and_inputs_produce_deterministic_replay()
        {
            var cfg = new ArenaConfig(ArenaMode.FreeForAll, new float3(0f, 1f, 0f), 25f, 999UL);
            var m1 = new ArenaMatch(_planet, _balance, new FlatSurface(), cfg,
                new[] { MakeParticipant(0, 0, 2, 50), MakeParticipant(1, 1, 2, 50) });
            var m2 = new ArenaMatch(_planet, _balance, new FlatSurface(), cfg,
                new[] { MakeParticipant(0, 0, 2, 50), MakeParticipant(1, 1, 2, 50) });

            m1.Start(0);
            m2.Start(0);

            int t1 = RunToFinish(m1);
            int t2 = RunToFinish(m2);

            Assert.That(t1, Is.EqualTo(t2));
            Assert.That(m1.WinningTeamIndex, Is.EqualTo(m2.WinningTeamIndex));
            for (int i = 0; i < m1.Units.Count; i++)
            {
                Assert.That(m1.Units[i].Combatant.Hp, Is.EqualTo(m2.Units[i].Combatant.Hp));
            }
        }

        [Test]
        public void AutoRefill_summons_reserve_creature_after_faint()
        {
            var cfg = new ArenaConfig(ArenaMode.FreeForAll, new float3(0f, 1f, 0f), 20f, 555UL);
            var match = new ArenaMatch(_planet, _balance, new FlatSurface(), cfg,
                new[]
                {
                    MakeParticipant(0, 0, count: 2, level: 90, autoRefill: true),
                    MakeParticipant(1, 1, count: 3, level: 1, autoRefill: true),
                });

            match.Start(0);
            // Keep Participant 1's player behind their creatures so the level-90 fighters down a creature first.
            float3 p1Dir = match.Participants[1].Direction;
            match.SetPlayerDirection(1, math.normalize(new float3(p1Dir.x * 1.6f, p1Dir.y, p1Dir.z * 1.6f)));
            bool sawReserveSummoned = false;
            int reserveUnitId = match.Participants[1].UnitIdForRosterIndex(2);

            for (int t = 1; t <= 2000 && match.Phase == ArenaPhase.Active; t++)
            {
                match.Tick(t * 50L, 50);
                foreach (ArenaEvent ev in match.Events)
                {
                    if (ev.Kind == ArenaEventKind.UnitSummoned && ev.SourceUnitId == reserveUnitId)
                    {
                        sawReserveSummoned = true;
                    }
                }
            }

            Assert.That(sawReserveSummoned, Is.True, "3rd creature in pool should auto-summon when an active slot faints");
        }

        [Test]
        public void Player_direction_clamps_to_arena_and_commands_work()
        {
            var cfg = new ArenaConfig(ArenaMode.FreeForAll, new float3(0f, 1f, 0f), 20f, 777UL);
            var match = new ArenaMatch(_planet, _balance, new FlatSurface(), cfg,
                new[]
                {
                    MakeParticipant(0, 0, count: 3, level: 50, autoRefill: false, overload: true),
                    MakeParticipant(1, 1, count: 1, level: 50),
                });

            match.Start(0);

            // Far away direction (90 deg east) should clamp to ArenaRadiusM (20 m)
            match.SetPlayerDirection(0, new float3(1f, 0f, 0f));
            float angle = math.acos(math.clamp(math.dot(cfg.CenterDirection, match.Participants[0].Direction), -1f, 1f));
            float distM = angle * _planet.Radius;
            Assert.That(distM, Is.EqualTo(20f).Within(0.05f));

            // Dodge roll
            Assert.That(match.TryRoll(0, 10), Is.EqualTo(RollResult.Ok));
            Assert.That(match.TryRoll(0, 20), Is.EqualTo(RollResult.OnCooldown));

            // Switch active slot 0 -> roster 2
            Assert.That(match.Switch(0, 0, 2, 30), Is.EqualTo(TeamResult.Ok));
            Assert.That(match.Units[match.Participants[0].UnitIdForRosterIndex(0)].Fielded, Is.False);
            Assert.That(match.Units[match.Participants[0].UnitIdForRosterIndex(2)].Fielded, Is.True);

            // Overload on adult fielded creature
            int adultUnitId = match.Participants[0].UnitIdForRosterIndex(2);
            Assert.That(match.TryOverload(0, adultUnitId, 40), Is.EqualTo(OverloadResult.Ok));
            Assert.That(match.TryOverload(0, adultUnitId, 50), Is.EqualTo(OverloadResult.AlreadyOverloaded));
        }
    }
}
