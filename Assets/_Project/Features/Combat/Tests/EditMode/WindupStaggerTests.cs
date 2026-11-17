using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/combat.md#前搖 (Q-C45, Q-C52) and #失衡與擊退 (Q-C46, Q-C52; values Q-C53).
    public sealed class WindupStaggerTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;

            public bool IsSea(float3 direction) => false;

            public bool IsBlocked(float3 direction, float clearanceM) => false;
        }

        private const float R = 640f;
        private const int Dt = 50;
        private readonly List<Object> _created = new List<Object>();
        private PlanetConfig _planet;
        private CombatBalance _balance;
        private static readonly AiCommand Free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);

        [SetUp]
        public void SetUp()
        {
            _planet = new PlanetConfig(R, float3.zero);
            _balance = CombatBalance.FromJson(File.ReadAllText(BalanceFixture.RepoPath("Data", "balance.json")));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private MoveDef Move(string id, int power, int cooldownMs, int windupMs)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId(id);
            m.Set(Element.Fire, MoveKind.Damage, power, null, 0, 0, cooldownMs, 100, RangeClass.Melee, MoveShape.Front, 4000,
                MoveMovement.None, BurstOrigin.Caster);
            m.SetWindup(windupMs);
            return m;
        }

        private CreatureDef Creature(string id, MoveDef move, Habit habit = Habit.Tolerant)
        {
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            TestWild.WithHabit(c, habit).Set("line_" + id, CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Charge, null, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(25, 25, 25, 25), move, null, "{}", false);
            return c;
        }

        private static float3 AtMeters(float meters)
        {
            float a = meters / R;
            return math.normalize(new float3(math.sin(a), math.cos(a), 0f));
        }

        private float ArcM(float3 a, float3 b)
        {
            float3 da = _planet.ToDirection(a);
            float3 db = _planet.ToDirection(b);
            return math.acos(math.clamp(math.dot(da, db), -1f, 1f)) * R;
        }

        private static bool Has(Encounter e, EncounterEventKind kind, int source, int target = int.MinValue)
        {
            foreach (EncounterEvent ev in e.Events)
            {
                if (ev.Kind == kind && ev.SourceId == source && (target == int.MinValue || ev.TargetId == target)) return true;
            }

            return false;
        }

        private static int DamageFrom(Encounter e, int source, int target)
        {
            foreach (EncounterEvent ev in e.Events)
            {
                if (ev.Kind == EncounterEventKind.Hit && ev.SourceId == source && ev.TargetId == target) return ev.Amount;
            }

            return -1;
        }

        [Test]
        public void A_cast_resolves_only_after_its_windup()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 3);
            EncounterUnit mine = e.AddCreature(EncounterSide.Player, Creature("m", Move("slow", 5, 5000, 300)), null, AtMeters(0f), 30);
            EncounterUnit wild = e.AddWildT(Creature("w", Move("nip", 1, 5000, 0)), null, AtMeters(1.5f), 30);
            e.Field(mine.Id, AtMeters(0f), false, 0);
            float3 feet = _planet.ToWorld(AtMeters(0f), 0f);

            long castAt = -1;
            for (long t = Dt; t <= 3000; t += Dt)
            {
                e.Tick(t, Dt, feet, Free, mine.Id);
                bool resolved = Has(e, EncounterEventKind.Hit, mine.Id, wild.Id) || Has(e, EncounterEventKind.Miss, mine.Id, wild.Id);
                if (castAt < 0 && Has(e, EncounterEventKind.Cast, mine.Id))
                {
                    castAt = t;
                    Assert.That(resolved, Is.False, "nothing lands in the cast tick");
                    Assert.That(mine.WindupSlot, Is.EqualTo(0));
                    Assert.That(mine.WindupEndsAtMs, Is.EqualTo(t + 300));
                    continue;
                }

                if (castAt < 0) continue;
                if (t < castAt + 300)
                {
                    Assert.That(resolved, Is.False, "still winding up at " + t);
                    Assert.That(Has(e, EncounterEventKind.Cast, mine.Id), Is.False, "no second cast during a windup");
                    continue;
                }

                Assert.That(resolved, Is.True, "resolves on the first tick at or after the windup end");
                Assert.That(mine.WindupSlot, Is.EqualTo(EncounterUnit.NoWindup));
                return;
            }

            Assert.Fail("mine never cast");
        }

        [Test]
        public void A_boss_windup_is_scaled_by_boss_windup_permille()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 5);
            EncounterUnit mine = e.AddCreature(EncounterSide.Player, Creature("m", Move("tap", 1, 5000, 0)), null, AtMeters(0f), 30);
            EncounterUnit boss = e.AddBossT(Creature("b", Move("slam", 1, 5000, 400), Habit.Aggressive), null, AtMeters(2f), 30);
            e.Field(mine.Id, AtMeters(0f), false, 0);
            float3 feet = _planet.ToWorld(AtMeters(0f), 0f);
            for (long t = Dt; t <= 3000; t += Dt)
            {
                e.Tick(t, Dt, feet, Free, mine.Id);
                if (Has(e, EncounterEventKind.Cast, boss.Id))
                {
                    Assert.That(boss.WindupEndsAtMs - t, Is.EqualTo(400L * _balance.BossWindupPermille / 1000));
                    return;
                }
            }

            Assert.Fail("boss never cast");
        }

        [Test]
        public void Recalling_a_unit_mid_windup_cancels_it_and_refunds_the_cooldown()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 3);
            EncounterUnit mine = e.AddCreature(EncounterSide.Player, Creature("m", Move("slow", 5, 5000, 1000)), null, AtMeters(0f), 30);
            e.AddWildT(Creature("w", Move("nip", 1, 5000, 0)), null, AtMeters(1.5f), 30);
            e.Field(mine.Id, AtMeters(0f), false, 0);
            float3 feet = _planet.ToWorld(AtMeters(0f), 0f);
            long t = Dt;
            for (; t <= 3000 && mine.WindupSlot == EncounterUnit.NoWindup; t += Dt)
            {
                e.Tick(t, Dt, feet, Free, mine.Id);
            }

            Assert.That(mine.WindupSlot, Is.EqualTo(0), "mine must be winding up");
            Assert.That(mine.Combatant.Cooldowns.IsReady(0, t), Is.False);
            e.Unfield(mine.Id);
            e.Tick(t, Dt, feet, Free, CombatAi.NoTarget);
            Assert.That(Has(e, EncounterEventKind.Interrupted, mine.Id), Is.True);
            Assert.That(mine.WindupSlot, Is.EqualTo(EncounterUnit.NoWindup));
            Assert.That(mine.Combatant.Cooldowns.IsReady(0, t), Is.True, "Q-C52: an interrupted windup refunds its cooldown");
        }

        [Test]
        public void A_full_gauge_staggers_interrupts_blocks_casts_raises_damage_and_knocks_back()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 11);
            // The wild winds up long enough that mine's first hit lands mid-windup.
            EncounterUnit wild = e.AddWildT(Creature("w", Move("charge", 1, 100, 2000)), null, AtMeters(1.5f), 30);
            int maxHp = wild.Combatant.MaxHp;
            // ~40 % of max HP per hit (mastery 15 %, resist 0): above the 30 % gauge and the 15 % knockback line.
            int power = maxHp * 40 / 115;
            EncounterUnit mine = e.AddCreature(EncounterSide.Player, Creature("m", Move("smash", power, 1000, 300)), null, AtMeters(0f), 30);
            e.Field(mine.Id, AtMeters(0f), false, 0);
            float3 feet = _planet.ToWorld(AtMeters(0f), 0f);

            int firstDamage = -1;
            long staggerEnds = -1;
            float gapBefore = ArcM(mine.Position, wild.Position);
            for (long t = Dt; t <= 6000; t += Dt)
            {
                gapBefore = firstDamage < 0 ? ArcM(mine.Position, wild.Position) : gapBefore;
                e.Tick(t, Dt, feet, Free, mine.Id);
                int damage = DamageFrom(e, mine.Id, wild.Id);
                if (firstDamage < 0)
                {
                    if (damage < 0) continue;
                    firstDamage = damage;
                    Assert.That(Has(e, EncounterEventKind.Staggered, mine.Id, wild.Id), Is.True);
                    Assert.That(wild.IsStaggered(t), Is.True);
                    staggerEnds = wild.StaggeredUntilMs;
                    Assert.That(staggerEnds, Is.EqualTo(t + _balance.Stagger.DurationMs));
                    Assert.That(Has(e, EncounterEventKind.Interrupted, wild.Id), Is.True, "stagger cancels the windup");
                    Assert.That(wild.WindupSlot, Is.EqualTo(EncounterUnit.NoWindup));
                    Assert.That(wild.Combatant.Cooldowns.IsReady(0, t), Is.True, "and refunds its cooldown");
                    Assert.That(ArcM(mine.Position, wild.Position) - gapBefore, Is.GreaterThan(1.0f), "heavy hit knocks back");
                    continue;
                }

                if (t < staggerEnds)
                {
                    Assert.That(Has(e, EncounterEventKind.Cast, wild.Id), Is.False, "a staggered unit cannot cast");
                    if (damage >= 0)
                    {
                        int expected = (firstDamage * (100 + _balance.Stagger.DamageTakenBonusPercent) + 50) / 100;
                        Assert.That(damage, Is.EqualTo(System.Math.Min(expected, damage + wild.Combatant.Hp)),
                            "staggered targets take the bonus");
                        Assert.That(Has(e, EncounterEventKind.Staggered, mine.Id, wild.Id), Is.False, "no chained stagger");
                        return;
                    }
                }
            }

            Assert.Fail("expected a first hit and a second hit inside the stagger window");
        }

        [Test]
        public void The_gauge_drains_after_the_regen_delay()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 2);
            EncounterUnit wild = e.AddWildT(Creature("w", Move("nip", 1, 5000, 0), Habit.Avoidant),
                null, AtMeters(1.5f), 30);
            wild.StaggerGaugeMilli = 1000L * _balance.Stagger.CapacityHp(wild.Combatant.MaxHp) / 2;
            wild.LastDamagedMs = 0;
            long half = wild.StaggerGaugeMilli;
            float3 feet = _planet.ToWorld(AtMeters(300f), 0f);
            long t = Dt;
            for (; t < _balance.Stagger.RegenDelayMs; t += Dt) e.Tick(t, Dt, feet, Free, CombatAi.NoTarget);
            Assert.That(wild.StaggerGaugeMilli, Is.EqualTo(half), "no drain before the delay");
            for (; t <= _balance.Stagger.RegenDelayMs + 10000; t += Dt) e.Tick(t, Dt, feet, Free, CombatAi.NoTarget);
            Assert.That(wild.StaggerGaugeMilli, Is.EqualTo(0));
        }

        [Test]
        public void Bosses_are_not_knocked_back()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 9);
            EncounterUnit boss = e.AddBossT(Creature("b", Move("nip", 1, 5000, 0), Habit.Avoidant), null, AtMeters(1.5f), 1);
            int power = boss.Combatant.MaxHp * 20 / 115;
            EncounterUnit mine = e.AddCreature(EncounterSide.Player, Creature("m", Move("smash", power, 1000, 0)), null, AtMeters(0f), 30);
            e.Field(mine.Id, AtMeters(0f), false, 0);
            e.Provoke(boss.Id);
            float3 feet = _planet.ToWorld(AtMeters(0f), 0f);
            for (long t = Dt; t <= 3000; t += Dt)
            {
                float3 before = boss.Position;
                e.Tick(t, Dt, feet, new AiCommand(AiCommandKind.Focus, boss.Id), mine.Id);
                if (DamageFrom(e, mine.Id, boss.Id) > 0)
                {
                    Assert.That(ArcM(before, boss.Position), Is.LessThan(0.5f), "a boss holds its ground");
                    return;
                }
            }

            Assert.Fail("mine never hit the boss");
        }

        [Test]
        public void Refund_and_bonus_contracts()
        {
            var cds = new MoveCooldowns(2);
            cds.Start(0, 100, 1000);
            cds.Refund(0, 200);
            Assert.That(cds.IsReady(0, 200), Is.True);
            cds.Refund(1, 300);
            Assert.That(cds.ReadyAtMs(1), Is.EqualTo(0), "a ready slot is unchanged");
            Assert.Throws<ContractException>(() => cds.Refund(2, 300));
            Assert.Throws<ContractException>(() => cds.Refund(0, 100), "time may not go back");

            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit a = e.AddCreature(EncounterSide.Player, Creature("a", Move("tap", 10, 1000, 0)), null, AtMeters(0f), 30);
            EncounterUnit b = e.AddWildT(Creature("b", Move("nip", 1, 1000, 0)), null, AtMeters(1f), 30);
            Assert.That(a.Combatant.BeginCast(0, a.Moves[0], 10, out CastContext ctx), Is.EqualTo(CastResult.Accepted));
            var rng = new Pcg32(1, 'C');
            Assert.Throws<ContractException>(() => b.Combatant.ReceiveHostile(ctx, true, ref rng, 10, -1));
            Assert.Throws<ContractException>(() => b.Combatant.ReceiveHostile(ctx, true, ref rng, 10, 1001));

            Assert.Throws<ContractException>(() => new StaggerTuning(0, 0, 0, 1, 0, 1, 0));
            Assert.Throws<ContractException>(() => new StaggerTuning(300, 0, 0, 0, 0, 1, 0));
            Assert.Throws<ContractException>(() => new StaggerTuning(300, 0, 0, 1, 0, 1, 36001));
            Assert.That(new StaggerTuning(300, 0, 0, 1, 0, 1, 0).CapacityHp(1), Is.EqualTo(1));
            Assert.Throws<ContractException>(() => ((MoveDef)_created[0]).SetWindup(MoveDef.MaxWindupMs + 1));
        }
    }
}
