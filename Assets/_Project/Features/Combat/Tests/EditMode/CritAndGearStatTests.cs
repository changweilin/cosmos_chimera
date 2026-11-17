using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/combat.md#暴擊, #命中與閃避, #格擋, #失衡與擊退; crafting.md#穿戴與數值 (D0-S1 gear stats).
    public sealed class CritAndGearStatTests
    {
        private EffectRulesConfig _cfg;
        private DerivedStats _stats;

        [SetUp]
        public void SetUp()
        {
            _cfg = BalanceFixture.EffectConfig();
            _stats = new DerivedStats(BalanceFixture.Coefficients());
        }

        private static EffectSpec Mod(EffectType type, int delta) => new EffectSpec(type, false, default, delta, 0, 1);

        private static CastMove Damage(int power = 50, int accuracy = 100) =>
            new CastMove("move_dmg", Element.Fire, MoveKind.Damage, power, accuracy, 2000, false, default, 0);

        private Combatant Make(params EffectSpec[] equipment)
        {
            var book = new EffectBook();
            book.SetEquipmentEffects(equipment);
            return new Combatant(_stats, _cfg, 30, 30, 0, new int[10], new int[10], book, 2);
        }

        private static CastContext Cast(Combatant caster, in CastMove move)
        {
            Assert.AreEqual(CastResult.Accepted, caster.BeginCast(0, move, 0, out CastContext ctx));
            return ctx;
        }

        [Test]
        public void ApplyCrit_scales_half_up_and_keeps_zero()
        {
            Assert.That(DamageModel.ApplyCrit(100, 150), Is.EqualTo(150));
            Assert.That(DamageModel.ApplyCrit(3, 150), Is.EqualTo(5));
            Assert.That(DamageModel.ApplyCrit(0, 150), Is.Zero);
            Assert.That(DamageModel.ApplyCrit(7, 100), Is.EqualTo(7));
            Assert.That(DamageModel.ApplyCrit(int.MaxValue, DamageModel.MaxCritDamagePercent), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void ApplyCrit_rejects_out_of_domain_values()
        {
            Assert.Throws<ContractException>(() => DamageModel.ApplyCrit(-1, 150));
            Assert.Throws<ContractException>(() => DamageModel.ApplyCrit(10, 99));
            Assert.Throws<ContractException>(() => DamageModel.ApplyCrit(10, DamageModel.MaxCritDamagePercent + 1));
        }

        [Test]
        public void RollCrit_zero_rate_draws_nothing_and_full_rate_always_crits()
        {
            var a = new Pcg32(99UL, 3UL);
            var b = new Pcg32(99UL, 3UL);
            Assert.That(HitResolver.RollCrit(0, ref a), Is.False);
            Assert.That(a.NextUInt(1000), Is.EqualTo(b.NextUInt(1000)), "a zero rate must not advance the stream");

            var rng = new Pcg32(5UL, 1UL);
            for (int i = 0; i < 50; i++) Assert.That(HitResolver.RollCrit(1000, ref rng), Is.True);
            Assert.Throws<ContractException>(() => HitResolver.RollCrit(-1, ref rng));
            Assert.Throws<ContractException>(() => HitResolver.RollCrit(1001, ref rng));
        }

        [Test]
        public void HitChance_accepts_accuracy_above_100_up_to_the_cap()
        {
            Assert.That(_stats.HitChancePermille(DerivedStats.MaxAccuracyPercent, 0), Is.EqualTo(DerivedStats.Permille));
            Assert.That(_stats.HitChancePermille(120, 250), Is.EqualTo(900));
            Assert.Throws<ContractException>(() => _stats.HitChancePermille(DerivedStats.MaxAccuracyPercent + 1, 0));
        }

        [Test]
        public void Cast_freezes_base_crit_and_gear_mods()
        {
            CastContext plain = Cast(Make(), Damage());
            Assert.That(plain.CritRatePermille, Is.EqualTo(_cfg.CritBaseRatePermille));
            Assert.That(plain.CritDamagePercent, Is.EqualTo(_cfg.CritBaseDamagePercent));
            Assert.That(plain.EffectiveAccuracyPercent, Is.EqualTo(100));

            CastContext geared = Cast(Make(Mod(EffectType.CritRateMod, 10), Mod(EffectType.CritDamageMod, 50),
                Mod(EffectType.AccuracyMod, 15)), Damage(accuracy: 90));
            Assert.That(geared.CritRatePermille, Is.EqualTo(_cfg.CritBaseRatePermille + 100));
            Assert.That(geared.CritDamagePercent, Is.EqualTo(_cfg.CritBaseDamagePercent + 50));
            Assert.That(geared.EffectiveAccuracyPercent, Is.EqualTo(105));
        }

        [Test]
        public void Crit_rate_and_damage_clamp_to_their_domains()
        {
            CastContext none = Cast(Make(Mod(EffectType.CritRateMod, -100), Mod(EffectType.CritDamageMod, -100)), Damage());
            Assert.That(none.CritRatePermille, Is.Zero);
            Assert.That(none.CritDamagePercent, Is.EqualTo(100));
            Assert.That(default(CastContext).CritRatePermille, Is.Zero);
            Assert.That(default(CastContext).CritDamagePercent, Is.EqualTo(100));
        }

        [Test]
        public void A_guaranteed_crit_multiplies_formula_damage()
        {
            Combatant attacker = Make(Mod(EffectType.CritRateMod, 100));
            CastContext ctx = Cast(attacker, Damage(60));
            Combatant target = Make();
            var rng = new Pcg32(1UL, 1UL);
            HitReport r = target.ReceiveHostile(ctx, true, ref rng, 0);
            Assert.That(r.Outcome, Is.EqualTo(HitOutcome.Hit));
            Assert.That(r.Critical, Is.True);
            int expected = DamageModel.ApplyCrit(DamageModel.Compute(60, 0, 0), ctx.CritDamagePercent);
            Assert.That(r.Damage, Is.EqualTo(System.Math.Min(expected, r.HpBefore)));
        }

        [Test]
        public void No_crit_rate_never_crits()
        {
            CastContext ctx = Cast(Make(Mod(EffectType.CritRateMod, -100)), Damage(60));
            var rng = new Pcg32(1UL, 1UL);
            for (int i = 0; i < 20; i++)
            {
                HitReport r = Make().ReceiveHostile(ctx, true, ref rng, 0);
                Assert.That(r.Critical, Is.False);
                Assert.That(r.Damage, Is.EqualTo(DamageModel.Compute(60, 0, 0)));
            }
        }

        [Test]
        public void Armor_mods_are_summed_for_block_and_stagger()
        {
            Combatant c = Make(Mod(EffectType.BlockReductionMod, 20), Mod(EffectType.BlockReductionMod, 5),
                Mod(EffectType.StaggerCapacityModPercent, 30));
            Assert.That(c.BlockReductionDelta(0), Is.EqualTo(25));
            Assert.That(c.StaggerCapacityPercent(0), Is.EqualTo(30));
            Assert.That(Make().BlockReductionDelta(0), Is.Zero);
        }

        [Test]
        public void EffectRulesConfig_rejects_out_of_domain_crit_bases()
        {
            Assert.Throws<ContractException>(() => new EffectRulesConfig(10, 300, 1000, -1, 150));
            Assert.Throws<ContractException>(() => new EffectRulesConfig(10, 300, 1000, 1001, 150));
            Assert.Throws<ContractException>(() => new EffectRulesConfig(10, 300, 1000, 50, 99));
            Assert.Throws<ContractException>(() => new EffectRulesConfig(10, 300, 1000, 50, 1001));
        }
    }
}
