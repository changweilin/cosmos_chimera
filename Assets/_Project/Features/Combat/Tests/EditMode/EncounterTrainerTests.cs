using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/trainer-battles.md#共用規則
    public sealed class EncounterTrainerTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;
            public bool IsSea(float3 direction) => false;
            public bool IsBlocked(float3 direction, float clearanceM) => false;
        }

        private const float R = 640f;
        private readonly List<Object> _created = new List<Object>();
        private PlanetConfig _planet;
        private CombatBalance _balance;
        private NicheDef _niche;
        private static readonly AiCommand Free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);

        [SetUp]
        public void SetUp()
        {
            _planet = new PlanetConfig(R, float3.zero);
            _balance = CombatBalance.FromJson(File.ReadAllText(BalanceFixture.RepoPath("Data", "balance.json")));
            var dist = new WanderDistribution(300, 0, 0, 100, 600);
            _niche = ScriptableObject.CreateInstance<NicheDef>();
            _niche.SetId("test_niche");
            _niche.Set("Basal", new List<string> { "grassland" }, new List<HabitWeight> { new HabitWeight(Habit.Aggressive, 1) },
                WanderMode.BoundedRandom, 10000, 0, dist, dist, dist, 1, TrophicGroup.FoodWeb);
            _created.Add(_niche);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private CreatureDef Creature(string id, int power = 100)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId("hit_" + id);
            m.Set(Element.Fire, MoveKind.Damage, power, null, 0, 0, 500, 100, RangeClass.Melee, MoveShape.Front, 50,
                MoveMovement.None, BurstOrigin.Caster);
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            c.Set("line_" + id, CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Charge, null, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(78, 25, 25, 25, 25, 25), m, null, "{}", false);
            return c;
        }

        private static float3 AtMeters(float meters)
        {
            float a = meters / R;
            return math.normalize(new float3(math.sin(a), math.cos(a), 0f));
        }

        private float3 Feet(float meters) => _planet.ToWorld(AtMeters(meters), 0f);

        private Encounter NewEncounter() => new Encounter(_planet, _balance, new FlatSurface(), 1);

        private static EncounterUnit Player(Encounter e, CreatureDef def, float meters)
        {
            EncounterUnit p = e.AddCreature(EncounterSide.Player, def, null, AtMeters(meters), 10);
            e.Field(p.Id, AtMeters(meters), false, 0);
            return p;
        }

        // One cast reused for every knock-out, as in the confrontation tests: a second BeginCast would hit the cooldown.
        private static CastContext Cast(EncounterUnit attacker, long nowMs)
        {
            attacker.Combatant.BeginCast(0, attacker.Moves[0], nowMs, out CastContext ctx);
            return ctx;
        }

        private static void KnockOut(in CastContext ctx, EncounterUnit target, long nowMs)
        {
            target.Combatant.ReceiveReflect(in ctx, 1000, nowMs);
            Assert.That(target.Combatant.IsFainted, Is.True, "test knock-out must faint the target");
        }

        private static bool EndedWith(Encounter e, int trainerId, int amount)
        {
            foreach (EncounterEvent ev in e.Events)
            {
                if (ev.Kind == EncounterEventKind.TrainerBattleEnded && ev.SourceId == trainerId && ev.Amount == amount)
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void Trainer_creatures_wait_off_field_and_are_never_interactable()
        {
            Encounter e = NewEncounter();
            int t = e.RegisterTrainer();
            EncounterUnit c0 = e.AddTrainerCreature(Creature("c0"), null, AtMeters(1f), 10, t, 0);

            Assert.That(e.TrainerState(t), Is.EqualTo(TrainerBattleState.Idle));
            Assert.That(c0.IsTrainerOwned && c0.TrainerId == t && c0.TrainerOrder == 0, Is.True);
            Assert.That(c0.Fielded, Is.False);
            Assert.That(c0.Individual, Is.Null);
            Assert.That(e.NearestInteractable(Feet(1f)), Is.EqualTo(-1));

            Assert.That(e.StartTrainerBattle(t), Is.True);
            Assert.That(e.NearestInteractable(Feet(1f)), Is.EqualTo(-1), "a fielded trainer creature is not interactable");
        }

        [Test]
        public void Contract_violations_throw()
        {
            Encounter e = NewEncounter();
            int t = e.RegisterTrainer();
            int empty = e.RegisterTrainer();
            CreatureDef def = Creature("c");

            Assert.Throws<ContractException>(() => e.TrainerState(5));
            Assert.Throws<ContractException>(() => e.AddTrainerCreature(def, null, AtMeters(1f), 10, 5, 0));
            Assert.Throws<ContractException>(() => e.AddTrainerCreature(def, null, AtMeters(1f), 10, t, -1));
            e.AddTrainerCreature(def, null, AtMeters(1f), 10, t, 0);
            Assert.Throws<ContractException>(() => e.AddTrainerCreature(def, null, AtMeters(1f), 10, t, 0), "duplicate order");
            Assert.Throws<ContractException>(() => e.StartTrainerBattle(5));
            Assert.Throws<ContractException>(() => e.StartTrainerBattle(empty), "no creatures");

            Assert.That(e.StartTrainerBattle(t), Is.True);
            Assert.Throws<ContractException>(() => e.AddTrainerCreature(def, null, AtMeters(1f), 10, t, 1), "not Idle");
        }

        [Test]
        public void Start_fields_the_two_lowest_orders_engaged()
        {
            Encounter e = NewEncounter();
            int t = e.RegisterTrainer();
            EncounterUnit c2 = e.AddTrainerCreature(Creature("c2"), null, AtMeters(3f), 10, t, 2);
            EncounterUnit c0 = e.AddTrainerCreature(Creature("c0"), null, AtMeters(1f), 10, t, 0);
            EncounterUnit c1 = e.AddTrainerCreature(Creature("c1"), null, AtMeters(2f), 10, t, 1);

            Assert.That(e.StartTrainerBattle(t), Is.True);

            Assert.That(e.HasActiveTrainerBattle, Is.True);
            Assert.That(e.TrainerState(t), Is.EqualTo(TrainerBattleState.Active));
            Assert.That(c0.Fielded && c0.Engaged, Is.True);
            Assert.That(c1.Fielded && c1.Engaged, Is.True);
            Assert.That(c2.Fielded, Is.False, "third creature waits as a reserve");
        }

        [Test]
        public void Start_is_refused_while_a_wild_fight_or_another_trainer_battle_runs()
        {
            Encounter e = NewEncounter();
            EncounterUnit wild = e.AddWild(Creature("w"), null, AtMeters(30f), 10,
                new WildIndividual(_niche, Habit.Aggressive, 300, 500, 500, 7));
            int a = e.RegisterTrainer();
            int b = e.RegisterTrainer();
            EncounterUnit a0 = e.AddTrainerCreature(Creature("a0"), null, AtMeters(1f), 10, a, 0);
            e.AddTrainerCreature(Creature("b0"), null, AtMeters(2f), 10, b, 0);

            e.Provoke(wild.Id);
            Assert.That(e.StartTrainerBattle(a), Is.False);
            Assert.That(e.TrainerState(a), Is.EqualTo(TrainerBattleState.Idle));
            Assert.That(a0.Fielded, Is.False, "a refused start changes nothing");

            e.Tick(50, 50, Feet(100f), Free, CombatAi.NoTarget);
            e.Tick(100, 50, Feet(100f), Free, CombatAi.NoTarget);
            Assert.That(wild.Engaged || wild.WantsEngage, Is.False, "precondition: the wild fight has ended");

            Assert.That(e.StartTrainerBattle(a), Is.True);
            Assert.That(e.StartTrainerBattle(b), Is.False, "one trainer battle at a time");
        }

        [Test]
        public void Reserve_relays_in_team_order_when_a_fielded_creature_faints()
        {
            Encounter e = NewEncounter();
            EncounterUnit p = Player(e, Creature("p", 5000), 0f);
            int t = e.RegisterTrainer();
            EncounterUnit c0 = e.AddTrainerCreature(Creature("c0"), null, AtMeters(1f), 10, t, 0);
            EncounterUnit c1 = e.AddTrainerCreature(Creature("c1"), null, AtMeters(2f), 10, t, 1);
            EncounterUnit c3 = e.AddTrainerCreature(Creature("c3"), null, AtMeters(4f), 10, t, 3);
            EncounterUnit c2 = e.AddTrainerCreature(Creature("c2"), null, AtMeters(3f), 10, t, 2);
            Assert.That(e.StartTrainerBattle(t), Is.True);

            KnockOut(Cast(p, 10), c0, 10);
            e.Tick(50, 50, Feet(0f), Free, p.Id);

            Assert.That(c2.Fielded && c2.Engaged, Is.True, "lowest remaining order fields first");
            Assert.That(c3.Fielded, Is.False);
            Assert.That(c1.Fielded, Is.True);
            Assert.That(e.HasActiveTrainerBattle, Is.True);
        }

        [Test]
        public void Player_win_defeats_the_trainer_and_restores_both_sides()
        {
            Encounter e = NewEncounter();
            EncounterUnit p = Player(e, Creature("p", 5000), 0f);
            int t = e.RegisterTrainer();
            EncounterUnit c0 = e.AddTrainerCreature(Creature("c0"), null, AtMeters(1f), 10, t, 0);
            EncounterUnit c1 = e.AddTrainerCreature(Creature("c1"), null, AtMeters(2f), 10, t, 1);
            Assert.That(e.StartTrainerBattle(t), Is.True);

            c0.Combatant.BeginCast(0, c0.Moves[0], 5, out CastContext hurt);
            p.Combatant.ReceiveReflect(in hurt, 100, 5);
            Assert.That(p.Combatant.Hp, Is.LessThan(p.Combatant.MaxHp), "precondition: the player creature is hurt");
            CastContext strike = Cast(p, 10);
            KnockOut(strike, c0, 10);
            KnockOut(strike, c1, 20);
            e.Tick(50, 50, Feet(0f), Free, p.Id);

            Assert.That(e.TrainerState(t), Is.EqualTo(TrainerBattleState.Defeated));
            Assert.That(e.HasActiveTrainerBattle, Is.False);
            Assert.That(EndedWith(e, t, 1), Is.True);
            foreach (EncounterUnit c in new[] { c0, c1 })
            {
                Assert.That(c.Fielded || c.Engaged, Is.False);
                Assert.That(c.Combatant.Hp, Is.EqualTo(c.Combatant.MaxHp));
            }

            Assert.That(p.Combatant.Hp, Is.EqualTo(p.Combatant.MaxHp));
            Assert.That(e.StartTrainerBattle(t), Is.False, "a defeated trainer never challenges again");
        }

        [Test]
        public void Player_loss_returns_the_trainer_to_idle_for_a_rematch()
        {
            Encounter e = NewEncounter();
            EncounterUnit p0 = Player(e, Creature("p0"), 0f);
            EncounterUnit p1 = Player(e, Creature("p1"), 0.5f);
            int t = e.RegisterTrainer();
            EncounterUnit c0 = e.AddTrainerCreature(Creature("c0", 5000), null, AtMeters(1f), 10, t, 0);
            Assert.That(e.StartTrainerBattle(t), Is.True);

            CastContext strike = Cast(c0, 10);
            KnockOut(strike, p0, 10);
            e.Tick(50, 50, Feet(0f), Free, p1.Id);
            Assert.That(e.HasActiveTrainerBattle, Is.True, "one player creature still stands");

            KnockOut(strike, p1, 60);
            e.Tick(100, 50, Feet(0f), Free, p1.Id);

            Assert.That(e.TrainerState(t), Is.EqualTo(TrainerBattleState.Idle));
            Assert.That(EndedWith(e, t, 0), Is.True);
            Assert.That(p0.Combatant.IsFainted || p1.Combatant.IsFainted, Is.False, "player creatures are revived");
            Assert.That(p0.Combatant.Hp, Is.EqualTo(p0.Combatant.MaxHp));
            Assert.That(c0.Fielded, Is.False);
            Assert.That(e.StartTrainerBattle(t), Is.True, "rematch allowed after a loss");
        }

        [Test]
        public void Distance_and_forced_disengage_never_end_a_trainer_battle()
        {
            Encounter e = NewEncounter();
            Player(e, Creature("p"), 0f);
            int t = e.RegisterTrainer();
            EncounterUnit c0 = e.AddTrainerCreature(Creature("c0"), null, AtMeters(1f), 10, t, 0);
            Assert.That(e.StartTrainerBattle(t), Is.True);

            e.Tick(50, 50, Feet(Encounter.DisengageRadiusM * 3f), Free, CombatAi.NoTarget);
            Assert.That(c0.Engaged, Is.True, "no disengage by distance");

            e.ForceDisengageAllWild();
            Assert.That(c0.Engaged, Is.True, "escape utilities do not end a trainer battle");
            Assert.That(e.HasActiveTrainerBattle, Is.True);
        }

        [Test]
        public void Wild_units_sit_out_a_trainer_battle()
        {
            Encounter e = NewEncounter();
            EncounterUnit p = Player(e, Creature("p"), 0f);
            EncounterUnit wild = e.AddWild(Creature("w"), null, AtMeters(2f), 10,
                new WildIndividual(_niche, Habit.Aggressive, 300, 500, 500, 7));
            int t = e.RegisterTrainer();
            e.AddTrainerCreature(Creature("c0"), null, AtMeters(1f), 10, t, 0);
            Assert.That(e.StartTrainerBattle(t), Is.True);

            e.Provoke(wild.Id);
            e.Tick(50, 50, Feet(2f), Free, p.Id);

            Assert.That(wild.Engaged || wild.WantsEngage, Is.False, "territory and provoke are ignored during the battle");
        }
    }
}
