using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/combat.md#格擋 (Q-C56) and #完美閃避 (Q-C55: the main creature rolls).
    public sealed class BlockRollTests
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

        private MoveDef Move(string id, int power, int cooldownMs, int windupMs, int rangeMm = 4000)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId(id);
            m.Set(Element.Fire, MoveKind.Damage, power, null, 0, 0, cooldownMs, 100, RangeClass.Melee, MoveShape.Front, rangeMm,
                MoveMovement.None, BurstOrigin.Caster);
            m.SetWindup(windupMs);
            return m;
        }

        private CreatureDef Creature(string id, MoveDef move, int agi = 25)
        {
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            c.Set("line_" + id, CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Hold, Temperament.Normal, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(25, 25, 25, agi), move, null, "{}", false);
            return c;
        }

        // The defender's own move cannot reach (0.1 m), so it never draws from the combat stream and two runs that differ
        // only in blocking consume the same rolls.
        private Encounter Arena(int wildPower, int wildCooldownMs, int wildWindupMs, out EncounterUnit mine,
            out EncounterUnit wild, ulong seed = 7)
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), seed);
            mine = e.AddCreature(EncounterSide.Player, Creature("m", Move("tap", 1, 600000, 0, 100), 1), null, AtMeters(0f), 30);
            wild = e.AddCreature(EncounterSide.Wild, Creature("w", Move("bite", wildPower, wildCooldownMs, wildWindupMs)), null,
                AtMeters(1.5f), 30);
            e.Field(mine.Id, AtMeters(0f), false, 0);
            return e;
        }

        private float3 Feet => _planet.ToWorld(AtMeters(-30f), 0f);

        private static float3 AtMeters(float meters)
        {
            float a = meters / R;
            return math.normalize(new float3(math.sin(a), math.cos(a), 0f));
        }

        private static bool Has(Encounter e, EncounterEventKind kind, int source, int target = int.MinValue)
        {
            foreach (EncounterEvent ev in e.Events)
            {
                if (ev.Kind == kind && ev.SourceId == source && (target == int.MinValue || ev.TargetId == target)) return true;
            }

            return false;
        }

        private static int Amount(Encounter e, EncounterEventKind kind, int source, int target)
        {
            foreach (EncounterEvent ev in e.Events)
            {
                if (ev.Kind == kind && ev.SourceId == source && ev.TargetId == target) return ev.Amount;
            }

            return -1;
        }

        private int FirstHitDamage(bool hold)
        {
            Encounter e = Arena(40, 1000, 0, out EncounterUnit mine, out EncounterUnit wild);
            e.SetBlockHeld(mine.Id, hold, 0);
            for (long t = Dt; t <= 5000; t += Dt)
            {
                e.Tick(t, Dt, Feet, Free, mine.Id);
                int dmg = Amount(e, EncounterEventKind.Hit, wild.Id, mine.Id);
                if (dmg < 0) continue;
                Assert.That(Has(e, EncounterEventKind.Blocked, wild.Id, mine.Id), Is.EqualTo(hold));
                return dmg;
            }

            Assert.Fail("wild never hit");
            return -1;
        }

        [Test]
        public void A_held_block_keeps_65_percent_of_the_damage()
        {
            int open = FirstHitDamage(false);
            int blocked = FirstHitDamage(true);
            Assert.That(open, Is.GreaterThan(1));
            int expected = (open * (1000 - _balance.Block.BasePermille) + 500) / 1000;
            Assert.That(blocked, Is.EqualTo(expected));
        }

        [Test]
        public void A_blocking_main_never_casts_and_its_windup_is_cancelled()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 3);
            EncounterUnit mine = e.AddCreature(EncounterSide.Player, Creature("m", Move("slow", 5, 5000, 600)), null, AtMeters(0f), 30);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", Move("nip", 1, 600000, 0, 100)), null,
                AtMeters(1.5f), 30);
            e.Field(mine.Id, AtMeters(0f), false, 0);
            long t = Dt;
            for (; t <= 3000 && mine.WindupSlot == EncounterUnit.NoWindup; t += Dt) e.Tick(t, Dt, Feet, Free, mine.Id);
            Assert.That(mine.WindupSlot, Is.EqualTo(0), "mine started a windup");

            e.SetBlockHeld(mine.Id, true, t);
            e.Tick(t, Dt, Feet, Free, mine.Id);
            Assert.That(mine.IsBlocking, Is.True);
            Assert.That(Has(e, EncounterEventKind.Interrupted, mine.Id), Is.True);
            Assert.That(mine.WindupSlot, Is.EqualTo(EncounterUnit.NoWindup));
            for (t += Dt; t <= 12000; t += Dt)
            {
                e.Tick(t, Dt, Feet, Free, mine.Id);
                Assert.That(Has(e, EncounterEventKind.Cast, mine.Id), Is.False, "cast while blocking at " + t);
            }

            Assert.That(wild.Combatant.Hp, Is.EqualTo(wild.Combatant.MaxHp));
        }

        [Test]
        public void A_support_blocks_automatically_while_a_wild_windup_targets_it()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 5);
            // The main stays unfielded so the wild's only target is the support.
            EncounterUnit main = e.AddCreature(EncounterSide.Player, Creature("main", Move("tap0", 1, 600000, 0, 100)), null,
                AtMeters(-60f), 30);
            EncounterUnit support = e.AddCreature(EncounterSide.Player, Creature("s", Move("tap", 1, 600000, 0, 100), 1), null,
                AtMeters(0f), 30);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", Move("bite", 40, 2000, 300)), null,
                AtMeters(1.5f), 30);
            e.Field(support.Id, AtMeters(0f), false, 0);

            bool sawWindup = false;
            for (long t = Dt; t <= 6000; t += Dt)
            {
                bool wasTargeted = wild.WindupSlot != EncounterUnit.NoWindup;
                e.Tick(t, Dt, Feet, Free, main.Id);
                if (wasTargeted)
                {
                    sawWindup = true;
                    Assert.That(support.IsBlocking, Is.True, "support blocks during the wild windup at " + t);
                }
                else if (wild.WindupSlot == EncounterUnit.NoWindup)
                {
                    Assert.That(support.IsBlocking, Is.False, "no windup, no block at " + t);
                }

                if (Has(e, EncounterEventKind.Hit, wild.Id, support.Id))
                {
                    Assert.That(Has(e, EncounterEventKind.Blocked, wild.Id, support.Id), Is.True);
                    Assert.That(Has(e, EncounterEventKind.PerfectBlock, wild.Id, support.Id), Is.False, "a support is never perfect");
                    return;
                }
            }

            Assert.That(sawWindup, Is.True);
            Assert.Fail("wild never hit the support");
        }

        // Holds block, waits for the wild's 300 ms windup to start, releases `releaseBeforeHitMs` before it lands.
        private void ReleaseBeforeHit(int releaseBeforeHitMs, out bool perfect, out bool blocked)
        {
            Encounter e = Arena(40, 2000, 300, out EncounterUnit mine, out EncounterUnit wild);
            e.SetBlockHeld(mine.Id, true, 0);
            long hitAt = -1;
            for (long t = Dt; t <= 8000; t += Dt)
            {
                if (hitAt > 0 && t == hitAt - releaseBeforeHitMs) e.SetBlockHeld(mine.Id, false, t);
                e.Tick(t, Dt, Feet, Free, mine.Id);
                if (hitAt < 0 && Has(e, EncounterEventKind.Cast, wild.Id)) hitAt = wild.WindupEndsAtMs;
                if (!Has(e, EncounterEventKind.Hit, wild.Id, mine.Id)) continue;
                Assert.That(t, Is.EqualTo(hitAt));
                perfect = Has(e, EncounterEventKind.PerfectBlock, wild.Id, mine.Id);
                blocked = Has(e, EncounterEventKind.Blocked, wild.Id, mine.Id);
                return;
            }

            Assert.Fail("wild never hit");
            perfect = blocked = false;
        }

        [Test]
        public void Releasing_within_the_window_is_a_perfect_block()
        {
            ReleaseBeforeHit(_balance.Block.PerfectWindowMs, out bool perfect, out bool blocked);
            Assert.That(perfect, Is.True);
            Assert.That(blocked, Is.True, "pending Q-C59 a perfect block resolves as a normal block");
        }

        [Test]
        public void Releasing_too_early_takes_the_full_hit()
        {
            ReleaseBeforeHit(_balance.Block.PerfectWindowMs + Dt, out bool perfect, out bool blocked);
            Assert.That(perfect, Is.False);
            Assert.That(blocked, Is.False);
        }

        [Test]
        public void A_full_stance_gauge_breaks_the_guard()
        {
            Encounter e = Arena(400, 500, 0, out EncounterUnit mine, out EncounterUnit wild);
            e.SetBlockHeld(mine.Id, true, 0);
            int capacity = _balance.Block.CapacityHp(mine.Combatant.MaxHp);
            for (long t = Dt; t <= 20000; t += Dt)
            {
                e.Tick(t, Dt, Feet, Free, mine.Id);
                Assert.That(mine.Combatant.IsFainted, Is.False, "stance (40 %) must fill before 65 % blocked hits faint it");
                if (!Has(e, EncounterEventKind.GuardBroken, wild.Id, mine.Id))
                {
                    Assert.That(mine.StanceGauge, Is.LessThan(capacity));
                    continue;
                }

                Assert.That(mine.IsGuardBroken(t), Is.True);
                Assert.That(mine.IsBlocking, Is.False);
                Assert.That(mine.StanceGauge, Is.Zero);
                long until = mine.GuardBrokenUntilMs;
                Assert.That(until, Is.EqualTo(t + _balance.Block.GuardBreakMs));
                float3 at = mine.Position;
                e.Remove(wild.Id);
                e.Tick(t + Dt, Dt, Feet, Free, mine.Id);
                Assert.That(mine.IsBlocking, Is.False, "no block while broken, key still held");
                Assert.That(math.distance(at, mine.Position), Is.LessThan(1e-4f), "no movement while broken");
                return;
            }

            Assert.Fail("guard never broke");
        }

        [Test]
        public void Stance_recovers_only_after_the_regen_delay_without_blocking()
        {
            Encounter e = Arena(40, 1000, 0, out EncounterUnit mine, out EncounterUnit wild);
            e.SetBlockHeld(mine.Id, true, 0);
            long t = Dt;
            for (; t <= 5000 && mine.StanceGauge == 0; t += Dt) e.Tick(t, Dt, Feet, Free, mine.Id);
            Assert.That(mine.StanceGauge, Is.GreaterThan(0));

            e.Remove(wild.Id);
            e.SetBlockHeld(mine.Id, false, t);
            int filled = mine.StanceGauge;
            long lastBlockingTick = t - Dt;
            for (; t < lastBlockingTick + _balance.Block.StanceRegenDelayMs; t += Dt) e.Tick(t, Dt, Feet, Free, mine.Id);
            Assert.That(mine.StanceGauge, Is.EqualTo(filled), "no recovery inside the delay");
            for (long end = t + 10000; t <= end; t += Dt) e.Tick(t, Dt, Feet, Free, mine.Id);
            Assert.That(mine.StanceGauge, Is.Zero);
        }

        // Rolls `rollBeforeHitMs` before the wild's 300 ms windup lands.
        private Encounter RollBeforeHit(int rollBeforeHitMs, out EncounterUnit mine, out EncounterUnit wild, out long hitAt)
        {
            Encounter e = Arena(40, 2000, 300, out mine, out wild);
            hitAt = -1;
            for (long t = Dt; t <= 8000; t += Dt)
            {
                if (hitAt > 0 && t == hitAt - rollBeforeHitMs) Assert.That(e.TryRoll(mine.Id, t), Is.True);
                e.Tick(t, Dt, Feet, Free, mine.Id);
                if (hitAt < 0 && Has(e, EncounterEventKind.Cast, wild.Id)) hitAt = wild.WindupEndsAtMs;
                if (hitAt > 0 && t == hitAt) return e;
            }

            Assert.Fail("wild never cast");
            return e;
        }

        [Test]
        public void A_roll_just_before_the_hit_is_a_perfect_dodge_that_staggers_the_attacker()
        {
            Encounter e = RollBeforeHit(100, out EncounterUnit mine, out EncounterUnit wild, out long hitAt);
            Assert.That(Has(e, EncounterEventKind.Miss, wild.Id, mine.Id), Is.True);
            Assert.That(mine.Combatant.Hp, Is.EqualTo(mine.Combatant.MaxHp));
            Assert.That(Has(e, EncounterEventKind.PerfectDodge, wild.Id, mine.Id), Is.True);
            Assert.That(Has(e, EncounterEventKind.Staggered, mine.Id, wild.Id), Is.True);
            Assert.That(wild.IsStaggered(hitAt), Is.True);
        }

        [Test]
        public void An_early_roll_still_has_i_frames_but_no_perfect_dodge()
        {
            int early = _balance.CreatureRoll.PerfectDodgeMs + Dt;
            Assume.That(early, Is.LessThan(_balance.CreatureRoll.InvulnerableMs));
            Encounter e = RollBeforeHit(early, out EncounterUnit mine, out EncounterUnit wild, out long hitAt);
            Assert.That(Has(e, EncounterEventKind.Miss, wild.Id, mine.Id), Is.True);
            Assert.That(Has(e, EncounterEventKind.PerfectDodge, wild.Id, mine.Id), Is.False);
            Assert.That(wild.IsStaggered(hitAt), Is.False);
        }

        [Test]
        public void Rolls_respect_the_cooldown()
        {
            Encounter e = Arena(1, 600000, 0, out EncounterUnit mine, out _);
            Assert.That(e.TryRoll(mine.Id, 100), Is.True);
            Assert.That(e.TryRoll(mine.Id, 100 + _balance.CreatureRoll.CooldownMs - 1), Is.False);
            Assert.That(e.TryRoll(mine.Id, 100 + _balance.CreatureRoll.CooldownMs), Is.True);
            Assert.That(mine.RollStartedAtMs, Is.EqualTo(100 + _balance.CreatureRoll.CooldownMs));
        }

        [Test]
        public void An_unfielded_unit_cannot_roll()
        {
            Encounter e = Arena(1, 600000, 0, out EncounterUnit mine, out _);
            e.Unfield(mine.Id);
            Assert.That(e.TryRoll(mine.Id, 100), Is.False);
        }

        [Test]
        public void Wild_units_cannot_block_or_roll()
        {
            Encounter e = Arena(1, 600000, 0, out _, out EncounterUnit wild);
            Assert.Throws<ContractException>(() => e.SetBlockHeld(wild.Id, true, 0));
            Assert.Throws<ContractException>(() => e.TryRoll(wild.Id, 0));
        }

        [Test]
        public void Tuning_rejects_out_of_range_values()
        {
            Assert.Throws<ContractException>(() => new BlockTuning(1001, 400, 2000, 250, 1500, 150, 500));
            Assert.Throws<ContractException>(() => new BlockTuning(350, 0, 2000, 250, 1500, 150, 500));
            Assert.Throws<ContractException>(() => new BlockTuning(350, 400, 2000, 250, 0, 150, 500));
            Assert.Throws<ContractException>(() => new CreatureRollTuning(600, 400, 800, 401));
            Assert.Throws<ContractException>(() => new CreatureRollTuning(600, 601, 800, 150));
            Assert.Throws<ContractException>(() => new CreatureRollTuning(600, 400, 599, 150));
            Assert.Throws<ContractException>(() => new BlockTuning(350, 400, 2000, 250, 1500, 150, 500).CapacityHp(0));
        }
    }
}
