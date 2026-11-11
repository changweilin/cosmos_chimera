using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    public sealed class CastTests
    {
        private EffectRulesConfig _cfg;
        private DerivedStats _stats;

        [SetUp]
        public void SetUp()
        {
            _cfg = BalanceFixture.EffectConfig();
            _stats = new DerivedStats(BalanceFixture.Coefficients());
        }

        private static int[] Table(Element e = Element.Fire, int value = 0)
        {
            var t = new int[10];
            t[(int)e] = value;
            return t;
        }

        private static EffectSpec Mod(EffectType t, int delta, Element e = Element.Fire, int maxStacks = 1) =>
            new EffectSpec(t, false, e, delta, 0, maxStacks);

        private static EffectSpec Heal(int percent) =>
            new EffectSpec(EffectType.HealPercentMaxHp, false, default, 0, percent, 1);

        private static CastMove Damage(int power = 50, int accuracy = 100, int cd = 2000, Element e = Element.Fire) =>
            new CastMove("move_dmg", e, MoveKind.Damage, power, accuracy, cd, false, default, 0);

        private static CastMove Both(EffectSpec effect, int duration, int power = 30) =>
            new CastMove("move_both", Element.Fire, MoveKind.Both, power, 100, 2000, true, effect, duration);

        private static CastMove Support(EffectSpec effect, int duration) =>
            new CastMove("move_sup", Element.Fire, MoveKind.Effect, 0, 100, 2000, true, effect, duration);

        private Combatant Make(EffectBook book = null, int con = 30, int spi = 30, int agi = 0,
            int[] mastery = null, int[] resist = null, int slots = 2) =>
            new Combatant(_stats, _cfg, con, spi, agi, mastery ?? Table(), resist ?? Table(), book ?? new EffectBook(),
                slots);

        private static EffectBook Trait(TriggerSpec trigger, params EffectSpec[] effects) =>
            new EffectBook("trait_t", trigger, effects);

        private static Pcg32 Rng() => new Pcg32(12345UL, 7UL);

        private CastContext Cast(Combatant caster, in CastMove move, long now, int slot = 0)
        {
            Assert.AreEqual(CastResult.Accepted, caster.BeginCast(slot, move, now, out CastContext ctx));
            return ctx;
        }

        // ---- CastMove ----

        [Test]
        public void CastMove_rejects_out_of_domain_values()
        {
            Assert.Throws<ContractException>(() => new CastMove("", Element.Fire, MoveKind.Damage, 1, 100, 0, false, default, 0));
            Assert.Throws<ContractException>(() => new CastMove("m", (Element)10, MoveKind.Damage, 1, 100, 0, false, default, 0));
            Assert.Throws<ContractException>(() => new CastMove("m", Element.Fire, (MoveKind)3, 1, 100, 0, false, default, 0));
            Assert.Throws<ContractException>(() => new CastMove("m", Element.Fire, MoveKind.Damage, -1, 100, 0, false, default, 0));
            Assert.Throws<ContractException>(() => new CastMove("m", Element.Fire, MoveKind.Damage, DamageModel.MaxPower + 1, 100, 0, false, default, 0));
            Assert.Throws<ContractException>(() => new CastMove("m", Element.Fire, MoveKind.Damage, 1, 101, 0, false, default, 0));
            Assert.Throws<ContractException>(() => new CastMove("m", Element.Fire, MoveKind.Damage, 1, -1, 0, false, default, 0));
            Assert.Throws<ContractException>(() => new CastMove("m", Element.Fire, MoveKind.Damage, 1, 100, -1, false, default, 0));
            Assert.Throws<ContractException>(() => new CastMove("m", Element.Fire, MoveKind.Damage, 1, 100, 0, true, Heal(10), 0));
            Assert.Throws<ContractException>(() => new CastMove("m", Element.Fire, MoveKind.Effect, 0, 100, 0, false, default, 0));
            Assert.Throws<ContractException>(() => new CastMove("m", Element.Fire, MoveKind.Effect, 0, 100, 0, true, default, 0));
            Assert.Throws<ContractException>(() => Support(Mod(EffectType.MasteryMod, 5), 0));
            Assert.Throws<ContractException>(() => Support(Mod(EffectType.MasteryMod, 5), EffectBook.MaxDurationMs + 1));
            Assert.Throws<ContractException>(() => Support(Heal(10), 1000));
            Assert.Throws<ContractException>(() => new CastMove("m", Element.Fire, MoveKind.Damage, 1, 100, 0, false, default, 5));
            Assert.Throws<ContractException>(() => CastMove.FromDef(null));
        }

        [Test]
        public void CastMove_accepts_domain_edges()
        {
            Assert.DoesNotThrow(() => new CastMove("m", Element.Shadow, MoveKind.Damage, DamageModel.MaxPower, 0, 0, false, default, 0));
            Assert.DoesNotThrow(() => Support(Mod(EffectType.MasteryMod, 5), 1));
            Assert.DoesNotThrow(() => Support(Mod(EffectType.MasteryMod, 5), EffectBook.MaxDurationMs));
            Assert.DoesNotThrow(() => Support(Heal(100), 0));
            Assert.IsTrue(Damage().DealsDamage);
            Assert.IsFalse(Support(Heal(5), 0).DealsDamage);
        }

        // ---- MoveCooldowns ----

        [Test]
        public void Cooldowns_reject_bad_construction_slots_and_time()
        {
            Assert.Throws<ContractException>(() => new MoveCooldowns(0));
            Assert.Throws<ContractException>(() => new MoveCooldowns(MoveCooldowns.MaxSlots + 1));
            var cd = new MoveCooldowns(2);
            Assert.Throws<ContractException>(() => cd.IsReady(-1, 0));
            Assert.Throws<ContractException>(() => cd.IsReady(2, 0));
            Assert.Throws<ContractException>(() => cd.ReadyAtMs(2));
            Assert.Throws<ContractException>(() => cd.Start(0, 0, -1));
            Assert.Throws<ContractException>(() => cd.IsReady(0, -1));
            Assert.Throws<ContractException>(() => cd.IsReady(0, CombatTime.MaxMs + 1));
            cd.Start(0, 500, 100);
            Assert.Throws<ContractException>(() => cd.IsReady(1, 499), "time went backwards");
            Assert.Throws<ContractException>(() => cd.Reset(499));
        }

        [Test]
        public void Cooldown_is_half_open_from_the_cast_instant()
        {
            var cd = new MoveCooldowns(MoveCooldowns.MaxSlots);
            Assert.AreEqual(MoveCooldowns.MaxSlots, cd.SlotCount);
            Assert.IsTrue(cd.IsReady(0, 0));
            cd.Start(0, 1000, 2500);
            Assert.AreEqual(3500, cd.ReadyAtMs(0));
            Assert.IsFalse(cd.IsReady(0, 3499));
            Assert.IsTrue(cd.IsReady(1, 3499), "slots are independent");
            Assert.Throws<ContractException>(() => cd.Start(0, 3499, 10));
            Assert.IsTrue(cd.IsReady(0, 3500));
            cd.Start(0, 3500, 0);
            Assert.IsTrue(cd.IsReady(0, 3500), "a zero cooldown is ready immediately");
        }

        [Test]
        public void Cooldown_at_max_time_does_not_wrap()
        {
            var cd = new MoveCooldowns(1);
            cd.Start(0, CombatTime.MaxMs, int.MaxValue);
            Assert.AreEqual(CombatTime.MaxMs + int.MaxValue, cd.ReadyAtMs(0));
            Assert.IsFalse(cd.IsReady(0, CombatTime.MaxMs));
        }

        [Test]
        public void Cooldown_reset_makes_every_slot_ready()
        {
            var cd = new MoveCooldowns(2);
            cd.Start(0, 100, 10_000);
            cd.Start(1, 100, 10_000);
            cd.Reset(200);
            Assert.AreEqual(0, cd.ReadyAtMs(0));
            Assert.AreEqual(0, cd.ReadyAtMs(1));
            Assert.IsTrue(cd.IsReady(0, 200));
        }

        // ---- Combatant construction ----

        [Test]
        public void Combatant_rejects_bad_construction()
        {
            int[] ok = Table();
            var book = new EffectBook();
            Assert.Throws<ContractException>(() => new Combatant(null, _cfg, 1, 1, 1, ok, ok, book, 1));
            Assert.Throws<ContractException>(() => new Combatant(_stats, null, 1, 1, 1, ok, ok, book, 1));
            Assert.Throws<ContractException>(() => new Combatant(_stats, _cfg, 1, 1, 1, ok, ok, null, 1));
            Assert.Throws<ContractException>(() => new Combatant(_stats, _cfg, -1, 1, 1, ok, ok, book, 1));
            Assert.Throws<ContractException>(() => new Combatant(_stats, _cfg, 1, -1, 1, ok, ok, book, 1));
            Assert.Throws<ContractException>(() => new Combatant(_stats, _cfg, 1, 1, -1, ok, ok, book, 1));
            Assert.Throws<ContractException>(() => new Combatant(_stats, _cfg, 1, 1, 1, new int[9], ok, book, 1));
            Assert.Throws<ContractException>(() => new Combatant(_stats, _cfg, 1, 1, 1, ok, new int[11], book, 1));
            Assert.Throws<ContractException>(() => new Combatant(_stats, _cfg, 1, 1, 1, null, ok, book, 1));
            Assert.Throws<ContractException>(() => new Combatant(_stats, _cfg, 1, 1, 1, ok, null, book, 1));
            Assert.Throws<ContractException>(() => new Combatant(_stats, _cfg, 1, 1, 1, Table(Element.Fire, -1), ok, book, 1));
            Assert.Throws<ContractException>(() => new Combatant(_stats, _cfg, 1, 1, 1, Table(Element.Fire, DamageModel.MaxMasteryPercent + 1), ok, book, 1));
            Assert.Throws<ContractException>(() => new Combatant(_stats, _cfg, 1, 1, 1, ok, Table(Element.Fire, 101), book, 1));
            Assert.Throws<ContractException>(() => new Combatant(_stats, _cfg, 1, 1, 1, ok, Table(Element.Fire, -101), book, 1));
            Assert.Throws<ContractException>(() => new Combatant(_stats, _cfg, 1, 1, 1, ok, ok, book, 0));
        }

        [Test]
        public void Combatant_starts_at_full_hp_with_ready_moves()
        {
            var book = new EffectBook();
            Combatant c = Make(book, con: 40, slots: 3);
            Assert.AreEqual(_stats.MaxHp(40), c.MaxHp);
            Assert.AreEqual(c.MaxHp, c.Hp);
            Assert.IsFalse(c.IsFainted);
            Assert.AreSame(book, c.Book);
            Assert.AreEqual(3, c.Cooldowns.SlotCount);
            Assert.IsTrue(c.Cooldowns.IsReady(2, 0));
        }

        [Test]
        public void Combatant_copies_element_tables()
        {
            int[] resist = Table(Element.Fire, 50);
            Combatant target = Make(resist: resist);
            resist[(int)Element.Fire] = 100;
            Combatant caster = Make();
            Pcg32 rng = Rng();
            HitReport r = target.ReceiveHostile(Cast(caster, Damage(), 0), true, ref rng, 0);
            Assert.AreEqual(DamageModel.Compute(50, 0, 50), r.Damage);
        }

        // ---- BeginCast ----

        [Test]
        public void BeginCast_starts_the_effective_cooldown_and_samples_mastery()
        {
            Combatant c = Make(spi: 40, mastery: Table(Element.Fire, 25));
            CastContext ctx = Cast(c, Damage(cd: 4000), 1000, slot: 1);
            int expected = _stats.EffectiveCooldownMs(4000, 40);
            Assert.AreEqual(expected, ctx.CooldownMs);
            Assert.AreEqual(1000 + expected, c.Cooldowns.ReadyAtMs(1));
            Assert.AreEqual(25, ctx.MasteryPercent);
            Assert.AreEqual(0, ctx.RangePercentSum);
            Assert.AreEqual(1000, ctx.CastAtMs);
            Assert.AreEqual("move_dmg", ctx.Move.Id);
            Assert.AreEqual(0, c.Cooldowns.ReadyAtMs(0), "other slots untouched");
        }

        [Test]
        public void BeginCast_refuses_while_cooling_down_without_side_effects()
        {
            var trigger = new TriggerSpec(TriggerType.OnMoveUsed, false, Element.Fire, 0, 5000);
            EffectBook book = Trait(trigger, Mod(EffectType.MasteryMod, 10, maxStacks: 5));
            Combatant c = Make(book);
            CastContext first = Cast(c, Damage(cd: 3000), 0);
            Assert.AreEqual(1, book.StacksOf("trait_t", 0, 0));
            Assert.AreEqual(CastResult.OnCooldown, c.BeginCast(0, Damage(cd: 3000), first.CooldownMs - 1, out CastContext ctx));
            Assert.AreEqual(default(CastContext).CooldownMs, ctx.CooldownMs);
            Assert.AreEqual(1, book.StacksOf("trait_t", 0, first.CooldownMs - 1), "refused casts fire no on_move_used");
            Assert.AreEqual(first.CooldownMs, c.Cooldowns.ReadyAtMs(0));
        }

        [Test]
        public void BeginCast_refuses_a_fainted_caster()
        {
            Combatant victim = Make(con: 0);
            Combatant attacker = Make(mastery: Table(Element.Fire, DamageModel.MaxMasteryPercent));
            Pcg32 rng = Rng();
            victim.ReceiveHostile(Cast(attacker, Damage(power: DamageModel.MaxPower), 0), true, ref rng, 0);
            Assert.IsTrue(victim.IsFainted);
            Assert.AreEqual(CastResult.Fainted, victim.BeginCast(0, Damage(), 0, out _));
            Assert.AreEqual(0, victim.Cooldowns.ReadyAtMs(0));
        }

        [Test]
        public void On_move_used_buffs_apply_to_the_triggering_cast()
        {
            // User decision 2026-11-07: the move that fires on_move_used already benefits (mastery and cd).
            var trigger = new TriggerSpec(TriggerType.OnMoveUsed, false, Element.Fire, 0, 5000);
            EffectBook book = Trait(trigger, Mod(EffectType.MasteryMod, 15),
                new EffectSpec(EffectType.CdModPercent, true, default, -50, 0, 1));
            Combatant c = Make(book, spi: 0, mastery: Table(Element.Fire, 20));
            CastContext ctx = Cast(c, Damage(cd: 4000), 0);
            Assert.AreEqual(35, ctx.MasteryPercent);
            Assert.AreEqual(EffectMath.EffectiveCooldownMs(_stats, 4000, 0, -50, _cfg), ctx.CooldownMs);
            Assert.Less(ctx.CooldownMs, _stats.EffectiveCooldownMs(4000, 0));
        }

        [Test]
        public void On_move_used_for_another_element_does_not_fire()
        {
            var trigger = new TriggerSpec(TriggerType.OnMoveUsed, false, Element.Water, 0, 5000);
            Combatant c = Make(Trait(trigger, Mod(EffectType.MasteryMod, 15)), mastery: Table(Element.Fire, 20));
            Assert.AreEqual(20, Cast(c, Damage(), 0).MasteryPercent);
        }

        [Test]
        public void BeginCast_samples_range_modifiers()
        {
            var book = new EffectBook();
            book.Apply("buff", 0, Mod(EffectType.RangeModPercent, 20), 1000, 0);
            Combatant c = Make(book);
            Assert.AreEqual(20, Cast(c, Damage(), 0).RangePercentSum);
        }

        [Test]
        public void BeginCast_rejects_bad_slot_and_default_move()
        {
            Combatant c = Make(slots: 1);
            Assert.Throws<ContractException>(() => c.BeginCast(1, Damage(), 0, out _));
            Assert.Throws<ContractException>(() => c.BeginCast(-1, Damage(), 0, out _));
            Assert.Throws<ContractException>(() => c.BeginCast(0, default, 0, out _));
        }

        // ---- ReceiveHostile ----

        [Test]
        public void No_contact_consumes_no_roll_and_changes_nothing()
        {
            Combatant caster = Make();
            Combatant target = Make();
            Pcg32 rng = Rng();
            Pcg32 before = rng;
            HitReport r = target.ReceiveHostile(Cast(caster, Damage(), 0), false, ref rng, 0);
            Assert.AreEqual(HitOutcome.NoContact, r.Outcome);
            Assert.AreEqual(before.NextUInt(), rng.NextUInt());
            Assert.AreEqual(target.MaxHp, target.Hp);
            Assert.AreEqual(0, r.Damage);
        }

        [Test]
        public void Roll_matches_hit_resolver_with_target_evasion_after_modifiers()
        {
            var book = new EffectBook();
            book.Apply("evade", 0, Mod(EffectType.EvasionModPercent, 50), EffectBook.MaxDurationMs, 0);
            Combatant target = Make(book, con: 100, agi: 100);
            Combatant caster = Make();
            int evasion = EffectMath.EvasionPermille(_stats, 100, 50, _cfg);
            int chance = _stats.HitChancePermille(60, evasion);
            Pcg32 rng = Rng();
            int misses = 0;
            // 150 casts × 3 s stay inside the buff window, and 3 s exceeds the 2 s base cooldown.
            for (int i = 0; i < 150; i++)
            {
                Pcg32 oracle = rng;
                HitOutcome expected = HitResolver.Resolve(true, false, chance, ref oracle);
                long now = i * 3_000L;
                HitReport r = target.ReceiveHostile(Cast(caster, Damage(power: 1, accuracy: 60), now), true, ref rng, now);
                Assert.AreEqual(expected, r.Outcome);
                Pcg32 a = rng;
                Pcg32 b = oracle;
                Assert.AreEqual(b.NextUInt(), a.NextUInt(), "exactly one draw per contact");
                if (r.Outcome == HitOutcome.Miss)
                {
                    misses++;
                    Assert.AreEqual(0, r.Damage);
                    Assert.AreEqual(r.HpBefore, r.HpAfter);
                }
            }

            Assert.Greater(misses, 0, "60% accuracy against high evasion must miss sometimes");
        }

        [Test]
        public void Hit_deals_damage_model_damage_with_cast_mastery_and_target_resist()
        {
            var tBook = new EffectBook();
            tBook.Apply("shell", 0, Mod(EffectType.ResistMod, 15), 100_000, 0);
            Combatant target = Make(tBook, con: 100, resist: Table(Element.Fire, 10));
            Combatant caster = Make(mastery: Table(Element.Fire, 30));
            Pcg32 rng = Rng();
            HitReport r = target.ReceiveHostile(Cast(caster, Damage(power: 80), 0), true, ref rng, 0);
            Assert.AreEqual(HitOutcome.Hit, r.Outcome);
            int expected = DamageModel.Compute(80, 30, 25);
            Assert.AreEqual(expected, r.Damage);
            Assert.AreEqual(target.MaxHp - expected, target.Hp);
            Assert.AreEqual(target.MaxHp, r.HpBefore);
            Assert.AreEqual(target.Hp, r.HpAfter);
            Assert.IsFalse(r.Fainted);
        }

        [Test]
        public void Cast_mastery_is_frozen_at_cast_time()
        {
            var cBook = new EffectBook();
            cBook.Apply("focus", 0, Mod(EffectType.MasteryMod, 40), 100, 0);
            Combatant caster = Make(cBook);
            CastContext ctx = Cast(caster, Damage(power: 60), 0);
            Combatant target = Make(con: 100);
            Pcg32 rng = Rng();
            HitReport r = target.ReceiveHostile(ctx, true, ref rng, 5_000);
            Assert.AreEqual(DamageModel.Compute(60, 40, 0), r.Damage, "buff expired at 100 ms but the cast kept it");
        }

        [Test]
        public void Damage_is_capped_at_remaining_hp_and_faint_fires_no_triggers()
        {
            // User decision 2026-11-07: a unit at 0 HP fires no trait event, so an on_hit_taken heal cannot revive.
            var trigger = new TriggerSpec(TriggerType.OnHitTaken, true, default, 0, 5000);
            EffectBook book = Trait(trigger, Heal(50));
            Combatant target = Make(book, con: 0);
            Combatant caster = Make(mastery: Table(Element.Fire, DamageModel.MaxMasteryPercent));
            Pcg32 rng = Rng();
            HitReport r = target.ReceiveHostile(Cast(caster, Damage(power: DamageModel.MaxPower), 0), true, ref rng, 0);
            Assert.AreEqual(target.MaxHp, r.Damage);
            Assert.AreEqual(0, target.Hp);
            Assert.IsTrue(r.Fainted);
            Assert.IsTrue(target.IsFainted);
            Assert.AreEqual(0, r.Healed);
            Assert.Throws<ContractException>(() => target.ReceiveHostile(Cast(caster, Damage(), 0, slot: 1), true, ref rng, 0));
        }

        [Test]
        public void Faint_skips_the_moves_own_effect()
        {
            Combatant target = Make(con: 0);
            Combatant caster = Make(mastery: Table(Element.Fire, DamageModel.MaxMasteryPercent));
            Pcg32 rng = Rng();
            CastMove move = Both(Mod(EffectType.EvasionModPercent, -20), 3000, power: DamageModel.MaxPower);
            HitReport r = target.ReceiveHostile(Cast(caster, move, 0), true, ref rng, 0);
            Assert.IsTrue(r.Fainted);
            Assert.IsFalse(r.EffectApplied);
            Assert.AreEqual(0, target.Book.StacksOf("move_both", 0, 0));
        }

        [Test]
        public void On_hit_taken_buff_applies_from_the_next_hit()
        {
            // User decision 2026-11-07: the triggering hit uses the pre-hit modifiers.
            var trigger = new TriggerSpec(TriggerType.OnHitTaken, false, Element.Fire, 0, 10_000);
            Combatant target = Make(Trait(trigger, Mod(EffectType.ResistMod, 40)), con: 200);
            Combatant caster = Make();
            Pcg32 rng = Rng();
            HitReport first = target.ReceiveHostile(Cast(caster, Damage(power: 100), 0), true, ref rng, 0);
            Assert.AreEqual(DamageModel.Compute(100, 0, 0), first.Damage);
            HitReport second = target.ReceiveHostile(Cast(caster, Damage(power: 100), 0, slot: 1), true, ref rng, 0);
            Assert.AreEqual(DamageModel.Compute(100, 0, 40), second.Damage);
        }

        [Test]
        public void On_hit_taken_does_not_fire_on_miss_or_other_element()
        {
            var trigger = new TriggerSpec(TriggerType.OnHitTaken, false, Element.Water, 0, 10_000);
            EffectBook book = Trait(trigger, Mod(EffectType.ResistMod, 40));
            Combatant target = Make(book, con: 200);
            Combatant caster = Make();
            Pcg32 rng = Rng();
            target.ReceiveHostile(Cast(caster, Damage(power: 10), 0), true, ref rng, 0);
            Assert.AreEqual(0, book.StacksOf("trait_t", 0, 0));
            target.ReceiveHostile(Cast(caster, Damage(power: 10, e: Element.Water), 0, slot: 1), false, ref rng, 0);
            Assert.AreEqual(0, book.StacksOf("trait_t", 0, 0));
        }

        [Test]
        public void Hp_cross_below_heal_resolves_after_the_hit()
        {
            var trigger = new TriggerSpec(TriggerType.OnHpCrossBelow, false, default, 50, 1000);
            Combatant target = Make(Trait(trigger, Heal(20)), con: 0);
            int maxHp = target.MaxHp;
            // Power picked so the hit lands strictly between 0 and 50 % of max HP.
            int power = 1;
            while (DamageModel.Compute(power, 0, 0) * 100 <= 50 * maxHp)
            {
                power++;
            }

            Combatant caster = Make();
            Pcg32 rng = Rng();
            HitReport r = target.ReceiveHostile(Cast(caster, Damage(power: power), 0), true, ref rng, 0);
            Assert.Less(maxHp - r.Damage, maxHp / 2 + 1);
            Assert.Greater(maxHp - r.Damage, 0);
            Assert.AreEqual(EffectMath.HealAmount(maxHp, 20), r.Healed);
            Assert.AreEqual(maxHp - r.Damage + r.Healed, target.Hp);
            Assert.AreEqual(r.HpBefore - r.Damage + r.Healed, r.HpAfter);
        }

        [Test]
        public void Hostile_timed_effect_lands_only_on_hit()
        {
            Combatant target = Make(con: 200, agi: 200);
            Combatant caster = Make();
            CastMove move = new CastMove("move_slow", Element.Fire, MoveKind.Effect, 0, 0, 0, true,
                Mod(EffectType.MoveSpeedModPercent, -30), 3000);
            Pcg32 rng = Rng();
            bool sawMiss = false;
            bool sawHit = false;
            for (int i = 0; i < 400 && !(sawMiss && sawHit); i++)
            {
                long now = i * 10_000L;
                HitReport r = target.ReceiveHostile(Cast(caster, move, now), true, ref rng, now);
                Assert.AreEqual(0, r.Damage, "effect-only moves deal no damage");
                Assert.AreEqual(r.Outcome == HitOutcome.Hit, r.EffectApplied);
                Assert.AreEqual(r.Outcome == HitOutcome.Hit ? 1 : 0, target.Book.StacksOf("move_slow", 0, now));
                sawMiss |= r.Outcome == HitOutcome.Miss;
                sawHit |= r.Outcome == HitOutcome.Hit;
            }

            Assert.IsTrue(sawMiss && sawHit);
        }

        [Test]
        public void Hostile_heal_heals_whatever_the_shape_hits()
        {
            Combatant target = Make(con: 100);
            Combatant caster = Make();
            Pcg32 rng = Rng();
            target.ReceiveHostile(Cast(caster, Damage(power: 120), 0), true, ref rng, 0);
            int hp = target.Hp;
            CastMove heal = new CastMove("move_h", Element.Fire, MoveKind.Both, 1, 100, 0, true, Heal(10), 0);
            HitReport r = target.ReceiveHostile(Cast(caster, heal, 0, slot: 1), true, ref rng, 0);
            Assert.AreEqual(hp - r.Damage + EffectMath.HealAmount(target.MaxHp, 10), target.Hp);
            Assert.IsFalse(r.EffectApplied);
        }

        [Test]
        public void ReceiveHostile_rejects_default_cast()
        {
            Combatant target = Make();
            Pcg32 rng = Rng();
            Assert.Throws<ContractException>(() => target.ReceiveHostile(default, true, ref rng, 0));
        }

        // ---- ReceiveSupport ----

        [Test]
        public void Support_heal_needs_no_roll_and_clamps_at_max_hp()
        {
            Combatant target = Make(con: 100);
            Combatant caster = Make();
            Pcg32 rng = Rng();
            target.ReceiveHostile(Cast(caster, Damage(power: 30), 0), true, ref rng, 0);
            int missing = target.MaxHp - target.Hp;
            Pcg32 before = rng;
            HitReport r = target.ReceiveSupport(Cast(caster, Support(Heal(100), 0), 0, slot: 1), 0);
            Assert.AreEqual(HitOutcome.Hit, r.Outcome);
            Assert.AreEqual(missing, r.Healed);
            Assert.AreEqual(target.MaxHp, target.Hp);
            Assert.AreEqual(before.NextUInt(), rng.NextUInt(), "support never touches the combat stream");
        }

        [Test]
        public void Support_heal_at_full_hp_changes_nothing()
        {
            Combatant target = Make();
            HitReport r = target.ReceiveSupport(Cast(Make(), Support(Heal(30), 0), 0), 0);
            Assert.AreEqual(0, r.Healed);
            Assert.AreEqual(target.MaxHp, target.Hp);
        }

        [Test]
        public void Support_timed_effect_is_applied_and_stacks_by_move_id()
        {
            Combatant target = Make();
            Combatant caster = Make();
            CastMove buff = Support(Mod(EffectType.MasteryMod, 10, maxStacks: 3), 4000);
            HitReport r = target.ReceiveSupport(Cast(caster, buff, 0), 0);
            Assert.IsTrue(r.EffectApplied);
            target.ReceiveSupport(Cast(caster, buff, 0, slot: 1), 0);
            Assert.AreEqual(2, target.Book.StacksOf("move_sup", 0, 0));
            Assert.AreEqual(0, target.Book.StacksOf("move_sup", 0, 4000), "window is half-open");
        }

        [Test]
        public void Support_rejects_fainted_target_and_default_cast()
        {
            Combatant caster = Make(mastery: Table(Element.Fire, DamageModel.MaxMasteryPercent));
            Combatant dead = Make(con: 0);
            Pcg32 rng = Rng();
            dead.ReceiveHostile(Cast(caster, Damage(power: DamageModel.MaxPower), 0), true, ref rng, 0);
            Combatant healer = Make();
            Assert.Throws<ContractException>(() => dead.ReceiveSupport(Cast(healer, Support(Heal(50), 0), 0), 0));
            Assert.Throws<ContractException>(() => Make().ReceiveSupport(default, 0));
        }

        [Test]
        public void Support_damage_is_a_cost_without_roll_or_hit_trigger()
        {
            // User decision 2026-11-07 (ARGON K3): self/ally damage hurts the receiver, uses caster mastery and receiver
            // resist, never rolls, never fires on_hit_taken.
            var trigger = new TriggerSpec(TriggerType.OnHitTaken, true, default, 0, 5000);
            EffectBook book = Trait(trigger, Mod(EffectType.ResistMod, 40));
            Combatant self = Make(book, con: 100, mastery: Table(Element.Fire, 20), resist: Table(Element.Fire, 10));
            Pcg32 rng = Rng();
            Pcg32 before = rng;
            CastMove cost = Both(Mod(EffectType.MasteryMod, 30), 4000, power: 40);
            CastContext ctx = Cast(self, cost, 0);
            HitReport r = self.ReceiveSupport(ctx, 0);
            Assert.AreEqual(HitOutcome.Hit, r.Outcome);
            Assert.AreEqual(DamageModel.Compute(40, 20, 10), r.Damage);
            Assert.AreEqual(self.MaxHp - r.Damage, self.Hp);
            Assert.IsTrue(r.EffectApplied);
            Assert.AreEqual(1, book.StacksOf("move_both", 0, 0));
            Assert.AreEqual(0, book.StacksOf("trait_t", 0, 0), "no on_hit_taken");
            Assert.AreEqual(before.NextUInt(), rng.NextUInt(), "no roll");

            HitReport plain = self.ReceiveSupport(Cast(self, Damage(power: 10), 0, slot: 1), 0);
            Assert.AreEqual(DamageModel.Compute(10, 20 + 30, 10), plain.Damage, "mastery buff from the cost move");
            Assert.IsFalse(plain.EffectApplied);
        }

        [Test]
        public void Support_damage_can_faint_and_then_nothing_else_happens()
        {
            Combatant self = Make(con: 0, mastery: Table(Element.Fire, DamageModel.MaxMasteryPercent));
            HitReport r = self.ReceiveSupport(
                Cast(self, Both(Mod(EffectType.MasteryMod, 5), 1000, power: DamageModel.MaxPower), 0), 0);
            Assert.IsTrue(r.Fainted);
            Assert.AreEqual(self.MaxHp, r.Damage);
            Assert.IsTrue(self.IsFainted);
            Assert.IsFalse(r.EffectApplied);
            Assert.AreEqual(0, self.Book.StacksOf("move_both", 0, 0));
        }

        [Test]
        public void Support_damage_can_fire_hp_cross_below()
        {
            var trigger = new TriggerSpec(TriggerType.OnHpCrossBelow, false, default, 50, 1000);
            Combatant self = Make(Trait(trigger, Heal(20)), con: 0);
            int power = 1;
            while (DamageModel.Compute(power, 0, 0) * 100 <= 50 * self.MaxHp)
            {
                power++;
            }

            HitReport r = self.ReceiveSupport(Cast(self, Damage(power: power), 0), 0);
            Assert.Greater(r.Damage, 0);
            Assert.AreEqual(EffectMath.HealAmount(self.MaxHp, 20), r.Healed);
            Assert.AreEqual(r.HpBefore - r.Damage + r.Healed, r.HpAfter);
        }

        // ---- ARGON cast review K1 / K2 / K4 / K5 ----

        [Test]
        public void FromDef_copies_timed_and_instant_moves()
        {
            MoveDef timed = UnityEngine.ScriptableObject.CreateInstance<MoveDef>();
            MoveDef heal = UnityEngine.ScriptableObject.CreateInstance<MoveDef>();
            try
            {
                timed.SetId("m_timed");
                timed.Set(Element.Fire, MoveKind.Both, 45,
                    new EffectDef(EffectType.MasteryMod, false, Element.Fire, 15, 0, 2), 3500, 0, 2500, 85,
                    RangeClass.Mid, MoveShape.Line, 12000, MoveMovement.None, default);
                CastMove t = CastMove.FromDef(timed);
                Assert.AreEqual("m_timed", t.Id);
                Assert.AreEqual(Element.Fire, t.Element);
                Assert.AreEqual(MoveKind.Both, t.Kind);
                Assert.AreEqual(45, t.Power);
                Assert.AreEqual(85, t.AccuracyPercent);
                Assert.AreEqual(2500, t.BaseCooldownMs);
                Assert.IsTrue(t.HasEffect);
                Assert.AreEqual(3500, t.EffectDurationMs);
                Assert.AreEqual(15, t.Effect.Delta);
                Assert.AreEqual(2, t.Effect.MaxStacks);

                heal.SetId("m_heal");
                heal.Set(Element.Water, MoveKind.Effect, 0,
                    new EffectDef(EffectType.HealPercentMaxHp, false, default, 0, 25, 1), 0, 0, 4000, 100,
                    RangeClass.Melee, MoveShape.Ally, 12000, MoveMovement.None, default);
                CastMove h = CastMove.FromDef(heal);
                Assert.AreEqual(0, h.EffectDurationMs);
                Assert.IsTrue(h.Effect.IsInstant);
                Assert.AreEqual(25, h.Effect.Percent);
                Assert.IsFalse(h.DealsDamage);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(timed);
                UnityEngine.Object.DestroyImmediate(heal);
            }
        }

        [Test]
        public void Combatant_rejects_too_many_slots()
        {
            Assert.Throws<ContractException>(() => Make(slots: MoveCooldowns.MaxSlots + 1));
            Assert.DoesNotThrow(() => Make(slots: MoveCooldowns.MaxSlots));
        }

        [Test]
        public void Refused_casts_still_check_slot_and_time()
        {
            Combatant dead = Make(con: 0);
            dead.EnterField(false, 500);
            Pcg32 rng = Rng();
            dead.ReceiveHostile(Cast(Make(mastery: Table(Element.Fire, DamageModel.MaxMasteryPercent)),
                Damage(power: DamageModel.MaxPower), 0), true, ref rng, 500);
            Assert.Throws<ContractException>(() => dead.BeginCast(-1, Damage(), 500, out _));
            Assert.Throws<ContractException>(() => dead.BeginCast(2, Damage(), 500, out _));
            Assert.Throws<ContractException>(() => dead.BeginCast(0, Damage(), 499, out _), "Book watermark");
            Assert.Throws<ContractException>(() => dead.BeginCast(0, Damage(), CombatTime.MaxMs + 1, out _));

            Combatant c = Make();
            Cast(c, Damage(cd: 3000), 0);
            c.Book.Apply("buff", 0, Mod(EffectType.MasteryMod, 10), 5000, 1000);
            Assert.Throws<ContractException>(() => c.BeginCast(0, Damage(cd: 3000), 500, out _),
                "cooling down but before the Book watermark");
            Assert.AreEqual(CastResult.OnCooldown, c.BeginCast(0, Damage(cd: 3000), 1000, out _));
        }

        [Test]
        public void Support_checks_time_even_at_full_hp()
        {
            Combatant target = Make();
            target.Book.Apply("b", 0, Mod(EffectType.MasteryMod, 5), 5000, 500);
            CastContext ctx = Cast(Make(), Support(Heal(30), 0), 500);
            Assert.Throws<ContractException>(() => target.ReceiveSupport(ctx, 499));
            Assert.Throws<ContractException>(() => target.ReceiveSupport(ctx, -1));
            Assert.DoesNotThrow(() => target.ReceiveSupport(ctx, 500));
        }

        [Test]
        public void Default_hit_report_is_not_a_faint()
        {
            Assert.IsFalse(default(HitReport).Fainted);
        }

        [Test]
        public void On_move_used_heal_reaches_hp()
        {
            var trigger = new TriggerSpec(TriggerType.OnMoveUsed, false, Element.Fire, 0, 1000);
            Combatant c = Make(Trait(trigger, Heal(10)), con: 100);
            Pcg32 rng = Rng();
            c.ReceiveHostile(Cast(Make(), Damage(power: 200), 0), true, ref rng, 0);
            int hp = c.Hp;
            Cast(c, Damage(), 0);
            Assert.AreEqual(System.Math.Min(c.MaxHp, hp + EffectMath.HealAmount(c.MaxHp, 10)), c.Hp);
        }

        [Test]
        public void Continuous_traits_see_current_hp_when_casting_and_when_hit()
        {
            // on_hp_below{50}: +30 fire mastery and +40 fire resist only once HP is under half.
            var trigger = new TriggerSpec(TriggerType.OnHpBelow, false, default, 50, 0);
            Combatant c = Make(Trait(trigger, Mod(EffectType.MasteryMod, 30), Mod(EffectType.ResistMod, 40)), con: 0);
            Assert.AreEqual(0, Cast(c, Damage(), 0).MasteryPercent, "full HP: trait off");
            Combatant caster = Make();
            int power = 1;
            while (DamageModel.Compute(power, 0, 0) * 100 <= 60 * c.MaxHp)
            {
                power++;
            }

            Pcg32 rng = Rng();
            HitReport first = c.ReceiveHostile(Cast(caster, Damage(power: power), 0), true, ref rng, 0);
            Assert.AreEqual(DamageModel.Compute(power, 0, 0), first.Damage, "trait off before the hit");
            Assert.Less((long)c.Hp * 100, 50L * c.MaxHp);
            Assert.AreEqual(30, Cast(c, Damage(), 0, slot: 1).MasteryPercent, "below half: trait on for the cast");
            HitReport second = c.ReceiveHostile(Cast(caster, Damage(power: 10), 0, slot: 1), true, ref rng, 0);
            Assert.AreEqual(System.Math.Min(c.Hp + second.Damage, DamageModel.Compute(10, 0, 40)), second.Damage,
                "below half: trait resist on for the hit");
        }

        [Test]
        public void Sure_hit_contact_still_draws_once()
        {
            Combatant target = Make(con: 200);
            Pcg32 rng = Rng();
            Pcg32 expected = rng;
            expected.NextUInt(DerivedStats.Permille);
            HitReport r = target.ReceiveHostile(Cast(Make(), Damage(power: 5, accuracy: 100), 0), true, ref rng, 0);
            Assert.AreEqual(HitOutcome.Hit, r.Outcome);
            Assert.AreEqual(expected.NextUInt(), rng.NextUInt());
        }

        [Test]
        public void Hostile_timed_effect_lasts_its_duration()
        {
            Combatant target = Make(con: 200);
            CastMove move = Both(Mod(EffectType.EvasionModPercent, -20), 3000, power: 5);
            Pcg32 rng = Rng();
            target.ReceiveHostile(Cast(Make(), move, 1000), true, ref rng, 1000);
            Assert.AreEqual(1, target.Book.StacksOf("move_both", 0, 3999));
            Assert.AreEqual(0, target.Book.StacksOf("move_both", 0, 4000));
        }

        [Test]
        public void Survivor_on_hit_taken_heal_and_move_heal_resolve_once()
        {
            var trigger = new TriggerSpec(TriggerType.OnHitTaken, true, default, 0, 1000);
            Combatant target = Make(Trait(trigger, Heal(7)), con: 100);
            Combatant caster = Make();
            Pcg32 rng = Rng();
            target.ReceiveHostile(Cast(caster, Damage(power: 300), 0), true, ref rng, 0);
            int hp = target.Hp;
            CastMove healing = new CastMove("move_hh", Element.Fire, MoveKind.Both, 1, 100, 0, true, Heal(9), 0);
            HitReport r = target.ReceiveHostile(Cast(caster, healing, 0, slot: 1), true, ref rng, 0);
            int afterDamage = hp - r.Damage;
            int expected = System.Math.Min(target.MaxHp - afterDamage, EffectMath.HealAmount(target.MaxHp, 16));
            Assert.AreEqual(expected, r.Healed, "7 + 9 percent summed before rounding");
            Assert.AreEqual(afterDamage + expected, target.Hp);
        }

        [Test]
        public void Support_report_and_heal_advance_the_book()
        {
            Combatant target = Make(con: 100);
            Pcg32 rng = Rng();
            target.ReceiveHostile(Cast(Make(), Damage(power: 200), 0), true, ref rng, 0);
            int hp = target.Hp;
            HitReport r = target.ReceiveSupport(Cast(Make(), Support(Heal(5), 0), 500), 500);
            Assert.AreEqual(hp, r.HpBefore);
            Assert.AreEqual(target.Hp, r.HpAfter);
            Assert.AreEqual(r.HpBefore + r.Healed, r.HpAfter);
            Assert.Throws<ContractException>(() => target.Book.StacksOf("x", 0, 499), "heal notified the book at 500");
        }

        [Test]
        public void EnterField_fires_summon_and_swap_and_heals_once()
        {
            var summon = new TriggerSpec(TriggerType.OnSummon, false, default, 0, 2000);
            Combatant a = Make(Trait(summon, Heal(8)), con: 100);
            Pcg32 rng = Rng();
            a.ReceiveHostile(Cast(Make(), Damage(power: 300), 0), true, ref rng, 0);
            int hp = a.Hp;
            int healed = a.EnterField(false, 100);
            Assert.AreEqual(EffectMath.HealAmount(a.MaxHp, 8), healed);
            Assert.AreEqual(hp + healed, a.Hp);

            var swap = new TriggerSpec(TriggerType.OnAllySwap, false, default, 0, 2000);
            Combatant b = Make(Trait(swap, Mod(EffectType.MoveSpeedModPercent, 20)));
            Assert.AreEqual(0, b.EnterField(false, 0));
            Assert.AreEqual(0, b.Book.StacksOf("trait_t", 0, 0), "opening summon is not a swap");
            Assert.AreEqual(0, b.EnterField(true, 10));
            Assert.AreEqual(1, b.Book.StacksOf("trait_t", 0, 10));
            Assert.AreEqual(0, b.Book.StacksOf("trait_t", 0, 2010));
        }

        [Test]
        public void EnterField_rejects_fainted_and_backwards_time()
        {
            Combatant dead = Make(con: 0);
            Pcg32 rng = Rng();
            dead.ReceiveHostile(Cast(Make(mastery: Table(Element.Fire, DamageModel.MaxMasteryPercent)),
                Damage(power: DamageModel.MaxPower), 0), true, ref rng, 0);
            Assert.Throws<ContractException>(() => dead.EnterField(true, 0));
            Combatant c = Make();
            c.EnterField(false, 100);
            Assert.Throws<ContractException>(() => c.EnterField(false, 99));
        }

        // ---- SOL cast review r1 K1 witnesses (W04–W49) ----

        [Test]
        public void W04_reset_moves_the_watermark()
        {
            var cd = new MoveCooldowns(1);
            cd.Reset(500);
            Assert.Throws<ContractException>(() => cd.IsReady(0, 499));
        }

        [Test]
        public void W07_mastery_modifier_is_read_for_the_move_element()
        {
            var book = new EffectBook();
            book.Apply("buff", 0, Mod(EffectType.MasteryMod, 40, Element.Water), 1000, 0);
            Combatant c = Make(book);
            Assert.AreEqual(40, Cast(c, Damage(e: Element.Water), 0).MasteryPercent);
        }

        [Test]
        public void W27_cost_damage_uses_receiver_resist_modifiers()
        {
            var book = new EffectBook();
            book.Apply("shell", 0, Mod(EffectType.ResistMod, 40), 1000, 0);
            Combatant self = Make(book, con: 100);
            HitReport r = self.ReceiveSupport(Cast(self, Damage(power: 100), 0), 0);
            Assert.AreEqual(DamageModel.Compute(100, 0, 40), r.Damage);
        }

        [Test]
        public void W32_support_timed_effect_lasts_its_duration()
        {
            Combatant target = Make();
            target.ReceiveSupport(Cast(Make(), Support(Mod(EffectType.MasteryMod, 5), 3000), 1000), 1000);
            Assert.AreEqual(1, target.Book.StacksOf("move_sup", 0, 3999));
            Assert.AreEqual(0, target.Book.StacksOf("move_sup", 0, 4000));
        }

        [Test]
        public void W34_swap_entry_also_fires_on_summon()
        {
            var summon = new TriggerSpec(TriggerType.OnSummon, false, default, 0, 2000);
            Combatant c = Make(Trait(summon, Mod(EffectType.MoveSpeedModPercent, 20)));
            c.EnterField(true, 0);
            Assert.AreEqual(1, c.Book.StacksOf("trait_t", 0, 0));
        }

        [Test]
        public void W35_swap_heal_reaches_hp()
        {
            var swap = new TriggerSpec(TriggerType.OnAllySwap, false, default, 0, 2000);
            Combatant c = Make(Trait(swap, Heal(10)), con: 100);
            Pcg32 rng = Rng();
            c.ReceiveHostile(Cast(Make(), Damage(power: 300), 0), true, ref rng, 0);
            int hp = c.Hp;
            int healed = c.EnterField(true, 100);
            Assert.AreEqual(EffectMath.HealAmount(c.MaxHp, 10), healed);
            Assert.AreEqual(hp + healed, c.Hp);
        }

        [Test]
        public void W37_context_does_not_alias_later_samples()
        {
            var book = new EffectBook();
            book.Apply("focus", 0, Mod(EffectType.MasteryMod, 40), 100, 0);
            Combatant c = Make(book);
            CastContext early = Cast(c, Damage(), 0);
            Assert.AreEqual(0, Cast(c, Damage(), 1000, slot: 1).MasteryPercent);
            Assert.AreEqual(40, early.MasteryPercent);
        }

        [Test]
        public void W47_mastery_table_is_copied()
        {
            int[] mastery = Table(Element.Fire, 20);
            Combatant c = Make(mastery: mastery);
            mastery[(int)Element.Fire] = 100;
            Assert.AreEqual(20, Cast(c, Damage(), 0).MasteryPercent);
        }

        [Test]
        public void W48_on_move_used_range_buff_applies_to_the_triggering_cast()
        {
            var trigger = new TriggerSpec(TriggerType.OnMoveUsed, false, Element.Fire, 0, 1000);
            Combatant c = Make(Trait(trigger, Mod(EffectType.RangeModPercent, 20)));
            Assert.AreEqual(20, Cast(c, Damage(), 0).RangePercentSum);
        }

        [Test]
        public void W49_cost_damage_sees_current_hp_for_continuous_traits()
        {
            var trigger = new TriggerSpec(TriggerType.OnHpBelow, false, default, 50, 0);
            Combatant self = Make(Trait(trigger, Mod(EffectType.ResistMod, 40)), con: 0);
            int power = 1;
            while (DamageModel.Compute(power, 0, 0) * 100 <= 60 * self.MaxHp)
            {
                power++;
            }

            self.ReceiveSupport(Cast(self, Damage(power: power), 0), 0);
            Assert.Less((long)self.Hp * 100, 50L * self.MaxHp);
            HitReport second = self.ReceiveSupport(Cast(self, Damage(power: 10), 0, slot: 1), 0);
            Assert.AreEqual(System.Math.Min(self.Hp + second.Damage, DamageModel.Compute(10, 0, 40)), second.Damage);
        }

        // ---- MoveSpeedMmS (tech-debt D0-E3) ----

        [Test]
        public void MoveSpeed_without_modifiers_equals_the_agi_derived_value()
        {
            Combatant c = Make(agi: 40);
            Assert.AreEqual(_stats.MoveSpeedMmS(40), c.MoveSpeedMmS(0));
        }

        [Test]
        public void MoveSpeed_follows_an_always_trait_in_both_directions()
        {
            var always = new TriggerSpec(TriggerType.Always, false, default, 0, 0);
            Combatant fast = Make(Trait(always, Mod(EffectType.MoveSpeedModPercent, 30)), agi: 40);
            Combatant slow = Make(Trait(always, Mod(EffectType.MoveSpeedModPercent, -30)), agi: 40);
            int baseline = _stats.MoveSpeedMmS(40);
            Assert.AreEqual(EffectMath.MoveSpeedMmS(_stats, 40, 30, _cfg), fast.MoveSpeedMmS(0));
            Assert.AreEqual(EffectMath.MoveSpeedMmS(_stats, 40, -30, _cfg), slow.MoveSpeedMmS(0));
            Assert.Greater(fast.MoveSpeedMmS(0), baseline);
            Assert.Less(slow.MoveSpeedMmS(0), baseline);
        }

        [Test]
        public void MoveSpeed_timed_modifier_expires()
        {
            Combatant c = Make(agi: 40);
            c.Book.Apply("s", 0, Mod(EffectType.MoveSpeedModPercent, -20), 1000, 0);
            Assert.Less(c.MoveSpeedMmS(999), _stats.MoveSpeedMmS(40));
            Assert.AreEqual(_stats.MoveSpeedMmS(40), c.MoveSpeedMmS(1000));
        }

        [Test]
        public void MoveSpeed_rejects_time_before_the_watermark()
        {
            Combatant c = Make(agi: 40);
            c.Book.Apply("s", 0, Mod(EffectType.MoveSpeedModPercent, -20), 1000, 500);
            Assert.Throws<ContractException>(() => c.MoveSpeedMmS(499));
        }
    }
}
