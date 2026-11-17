using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/creatures.md#六大能力值, #技能星級; combat.md#傷害公式 (D0-S1, Q-C65–Q-C72).
    public sealed class SixStatsTests
    {
        private EffectRulesConfig _cfg;
        private DerivedStats _stats;
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _cfg = BalanceFixture.EffectConfig();
            _stats = new DerivedStats(BalanceFixture.Coefficients());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private Combatant Make(CreatureStats core) =>
            new Combatant(_stats, _cfg, core, new int[10], new int[10], new EffectBook(), 2);

        private static CastMove Hit(Polarity polarity, int power = 60) =>
            new CastMove("move_hit", Element.Fire, MoveKind.Damage, power, 100, 2000, false, default, 0, polarity);

        // Crit off so the formula damage is exact; +100 accuracy beats the shared base evasion so every roll hits.
        private CastContext Cast(Combatant caster, in CastMove move)
        {
            caster.Book.SetEquipmentEffects(new[]
            {
                new EffectSpec(EffectType.CritRateMod, false, default, -100, 0, 1),
                new EffectSpec(EffectType.AccuracyMod, false, default, 100, 0, 1),
            });
            Assert.AreEqual(CastResult.Accepted, caster.BeginCast(0, move, 0, out CastContext ctx));
            return ctx;
        }

        [Test]
        public void MaxHp_is_hp_points_times_per_point_with_a_floor_of_one()
        {
            int per = _stats.Coefficients.HpPerPoint;
            Assert.That(_stats.MaxHpFromPoints(78), Is.EqualTo(78 * per));
            Assert.That(_stats.MaxHpFromPoints(0), Is.EqualTo(1));
            Assert.That(_stats.MaxHpFromPoints(int.MaxValue), Is.EqualTo(int.MaxValue));
            Assert.Throws<ContractException>(() => _stats.MaxHpFromPoints(-1));
            Assert.That(Make(new CreatureStats(78, 1, 1, 1, 1, 1)).MaxHp, Is.EqualTo(78 * per));
        }

        [Test]
        public void Attack_ratio_is_a_clamped_quotient_and_caps_on_zero_defence()
        {
            DerivedStatsCoefficients c = _stats.Coefficients;
            Assert.That(_stats.AttackRatioPermille(50, 50), Is.EqualTo(1000));
            Assert.That(_stats.AttackRatioPermille(75, 50), Is.EqualTo(1500));
            Assert.That(_stats.AttackRatioPermille(1, 1000), Is.EqualTo(c.DamageRatioMinPermille));
            Assert.That(_stats.AttackRatioPermille(1000, 1), Is.EqualTo(c.DamageRatioMaxPermille));
            Assert.That(_stats.AttackRatioPermille(0, 0), Is.EqualTo(c.DamageRatioMaxPermille));
            Assert.Throws<ContractException>(() => _stats.AttackRatioPermille(-1, 1));
            Assert.Throws<ContractException>(() => _stats.AttackRatioPermille(1, -1));
        }

        [Test]
        public void Ratio_damage_matches_the_plain_formula_at_x1_and_scales_with_the_ratio()
        {
            Assert.That(DamageModel.Compute(60, 20, 10, 1000), Is.EqualTo(DamageModel.Compute(60, 20, 10)));
            Assert.That(DamageModel.Compute(60, 0, 0, 2000), Is.EqualTo(120));
            Assert.That(DamageModel.Compute(60, 0, 0, 250), Is.EqualTo(15));
            Assert.That(DamageModel.Compute(1, 0, 99, 250), Is.EqualTo(1), "a non-immune hit still deals 1");
            Assert.That(DamageModel.Compute(60, 0, 100, 4000), Is.Zero, "immunity stays 0");
            Assert.Throws<ContractException>(() => DamageModel.Compute(60, 0, 0, 0));
            Assert.Throws<ContractException>(
                () => DamageModel.Compute(60, 0, 0, DerivedStatsCoefficients.MaxDamageRatioPermille + 1));
        }

        [Test]
        public void Polarity_picks_the_attack_and_defence_pair()
        {
            // Yang attacker 100 vs yang defence 50 → ×2; yin attacker 25 vs yin defence 100 → ×0.25.
            Combatant attacker = Make(new CreatureStats(100, 30, 25, 100, 25, 25));
            var rng = new Pcg32(3UL, 1UL);
            CastContext yang = Cast(attacker, Hit(Polarity.Yang));
            Assert.That(yang.Attack, Is.EqualTo(100));
            HitReport r = Make(new CreatureStats(100, 30, 25, 25, 100, 50)).ReceiveHostile(yang, true, ref rng, 0);
            Assert.That(r.Damage, Is.EqualTo(DamageModel.Compute(60, 0, 0, 2000)));

            Combatant yinCaster = Make(new CreatureStats(100, 30, 25, 100, 25, 25));
            CastContext yin = Cast(yinCaster, Hit(Polarity.Yin));
            Assert.That(yin.Attack, Is.EqualTo(25));
            r = Make(new CreatureStats(100, 30, 25, 25, 100, 50)).ReceiveHostile(yin, true, ref rng, 0);
            Assert.That(r.Damage, Is.EqualTo(DamageModel.Compute(60, 0, 0, 250)));
        }

        [Test]
        public void Speed_drives_cooldown_and_move_speed_and_evasion_is_shared()
        {
            Combatant slow = Make(new CreatureStats(100, 0, 25, 25, 25, 25));
            Combatant fast = Make(new CreatureStats(100, 200, 25, 25, 25, 25));
            Assert.That(fast.MoveSpeedMmS(0), Is.GreaterThan(slow.MoveSpeedMmS(0)));
            Assert.AreEqual(CastResult.Accepted, slow.BeginCast(0, Hit(Polarity.Yin), 0, out CastContext a));
            Assert.AreEqual(CastResult.Accepted, fast.BeginCast(0, Hit(Polarity.Yin), 0, out CastContext b));
            Assert.That(b.CooldownMs, Is.LessThan(a.CooldownMs));
            Assert.That(_stats.EvasionPermille(_stats.Coefficients.SharedBaseStat),
                Is.EqualTo(EffectMath.EvasionPermille(_stats, _stats.Coefficients.SharedBaseStat, 0, _cfg)));
        }

        [Test]
        public void Rescale_and_overload_swap_the_whole_core()
        {
            Combatant c = Make(new CreatureStats(50, 10, 10, 10, 10, 10));
            c.Rescale(new CreatureStats(60, 20, 30, 40, 50, 60));
            Assert.That(c.Core.YangDef, Is.EqualTo(60));
            c.BeginOverload(new CreatureStats(80, 1, 2, 3, 4, 5), new int[10], new int[10]);
            Assert.That(c.Core.Speed, Is.EqualTo(1));
            c.EndOverload();
            Assert.That(c.Core.Speed, Is.EqualTo(20));
            Assert.Throws<ContractException>(() => c.Rescale(new CreatureStats(60, -1, 0, 0, 0, 0)));
        }

        private MoveDef StarMove(int star)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId("move_star");
            m.Set(Element.Fire, MoveKind.Damage, 90, null, 0, 0, 3000, 100, RangeClass.Melee, MoveShape.Front, 2000,
                MoveMovement.None, BurstOrigin.Caster);
            m.SetSkill(star, Polarity.Yang);
            return m;
        }

        [Test]
        public void A_partner_provided_move_uses_its_star_one_version()
        {
            MoveDef m = StarMove(3);
            m.SetDowngraded(40, 5000, -1, null, -1);
            CastMove own = CastMove.FromDef(m);
            CastMove partner = CastMove.FromDef(m, true);
            Assert.That(own.Power, Is.EqualTo(90));
            Assert.That(own.BaseCooldownMs, Is.EqualTo(3000));
            Assert.That(partner.Power, Is.EqualTo(40));
            Assert.That(partner.BaseCooldownMs, Is.EqualTo(5000));
            Assert.That(partner.AccuracyPercent, Is.EqualTo(100), "-1 keeps the full value");
            Assert.That(partner.Polarity, Is.EqualTo(Polarity.Yang));

            MoveDef plain = StarMove(1);
            Assert.That(CastMove.FromDef(plain, true).Power, Is.EqualTo(90), "a ★ move has no separate version");
        }

        [Test]
        public void A_star_one_version_must_change_something_and_only_replace_an_existing_effect()
        {
            MoveDef m = StarMove(2);
            Assert.Throws<ContractException>(() => m.SetDowngraded(-1, -1, -1, null, -1));
            var effect = new EffectDef(EffectType.RangeModPercent, false, default, 5, 0, 1);
            Assert.Throws<ContractException>(() => m.SetDowngraded(-1, -1, -1, effect, -1));
            Assert.Throws<ContractException>(() => m.SetSkill(0, Polarity.Yin));
            Assert.Throws<ContractException>(() => m.SetSkill(4, Polarity.Yin));
        }

        [Test]
        public void A_partner_trait_uses_its_star_one_effects()
        {
            var t = ScriptableObject.CreateInstance<TraitDef>();
            _created.Add(t);
            var full = new List<EffectDef> { new EffectDef(EffectType.MoveSpeedModPercent, false, default, 20, 0, 1) };
            var down = new List<EffectDef> { new EffectDef(EffectType.MoveSpeedModPercent, false, default, 5, 0, 1) };
            t.Set(new TriggerDef(TriggerType.Always, false, default, 0, 0), full);
            Assert.That(t.EffectsFor(true), Is.SameAs(t.Effects), "no ★ version stored");
            t.SetSkill(2, down);
            Assert.That(t.Star, Is.EqualTo(2));
            Assert.That(t.EffectsFor(false)[0].Delta, Is.EqualTo(20));
            Assert.That(t.EffectsFor(true)[0].Delta, Is.EqualTo(5));
        }

        [Test]
        public void CreatureStats_scale_all_six_and_pick_by_polarity()
        {
            var s = new CreatureStats(100, 100, 100, 100, 100, 100).AtLevel(10);
            Assert.That(new[] { s.Hp, s.Speed, s.YinAtk, s.YangAtk, s.YinDef, s.YangDef }, Is.All.EqualTo(18));
            var p = new CreatureStats(1, 2, 3, 4, 5, 6);
            Assert.That(p.Attack(Polarity.Yin), Is.EqualTo(3));
            Assert.That(p.Attack(Polarity.Yang), Is.EqualTo(4));
            Assert.That(p.Defence(Polarity.Yin), Is.EqualTo(5));
            Assert.That(p.Defence(Polarity.Yang), Is.EqualTo(6));
            Assert.That(p.Total, Is.EqualTo(21));
            Assert.Throws<ContractException>(() => p.Attack((Polarity)2));
        }
    }
}
