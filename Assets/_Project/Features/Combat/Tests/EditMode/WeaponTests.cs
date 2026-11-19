using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    public sealed class WeaponTests
    {
        private EffectRulesConfig _cfg;
        private DerivedStats _stats;

        [SetUp]
        public void SetUp()
        {
            _cfg = BalanceFixture.EffectConfig();
            _stats = new DerivedStats(BalanceFixture.Coefficients());
        }

        private Combatant Make() =>
            new Combatant(_stats, _cfg, BalanceFixture.Core(80, 30), new int[10], new int[10], new EffectBook(), 2);

        private static CastMove Damage(Element e, int cd = 4000) =>
            new CastMove("move_" + e, e, MoveKind.Damage, 50, 100, cd, false, default, 0);

        private static CastContext Cast(Combatant c, in CastMove move, long now, int slot)
        {
            Assert.That(c.BeginCast(slot, move, now, out CastContext ctx), Is.EqualTo(CastResult.Accepted));
            return ctx;
        }

        private static readonly WeaponProfile Fire = new WeaponProfile(Element.Fire, 20, 1500, 1000);
        private static readonly WeaponProfile Water = new WeaponProfile(Element.Water, 10, 800, 2000);

        [Test]
        public void Unarmed_casts_are_unchanged()
        {
            Combatant plain = Make(), armed = Make();
            armed.SetWeapons(default);
            CastContext a = Cast(plain, Damage(Element.Fire), 0, 0), b = Cast(armed, Damage(Element.Fire), 0, 0);
            Assert.That(b.WeaponPowerPermille, Is.EqualTo(1000));
            Assert.That(b.EffectivePower, Is.EqualTo(a.EffectivePower));
            Assert.That(b.CooldownMs, Is.EqualTo(a.CooldownMs));
            Assert.That(b.MasteryPercent, Is.EqualTo(a.MasteryPercent));
            Assert.That(default(CastContext).WeaponPowerPermille, Is.EqualTo(1000));
        }

        [Test]
        public void Two_handed_bonus_scales_power_and_matching_element_adds_mastery()
        {
            Combatant c = Make();
            c.SetWeapons(new WeaponLoadout(1, Fire, default, 35, 0, false));
            CastContext fire = Cast(c, Damage(Element.Fire), 0, 0);
            Assert.That(fire.WeaponPowerPermille, Is.EqualTo(2025));
            Assert.That(fire.EffectivePower, Is.EqualTo(101)); // 50 × 2.025 rounded half up
            Assert.That(fire.MasteryPercent, Is.EqualTo(20));
            CastContext water = Cast(c, Damage(Element.Water), 0, 1);
            Assert.That(water.MasteryPercent, Is.Zero, "element mastery only for the weapon's element");
            Assert.That(water.WeaponPowerPermille, Is.EqualTo(2025));
        }

        [Test]
        public void Dual_wield_alternates_hands_per_accepted_cast_and_speed_divides_cooldown()
        {
            Combatant plain = Make();
            int baseCd = Cast(plain, Damage(Element.Water), 0, 0).CooldownMs;
            Combatant c = Make();
            c.SetWeapons(new WeaponLoadout(2, Fire, Water, 0, 25, false));
            CastContext first = Cast(c, Damage(Element.Water), 0, 0);
            Assert.That(first.WeaponPowerPermille, Is.EqualTo(1500));
            Assert.That(first.MasteryPercent, Is.Zero, "first hand is fire");
            Assert.That(c.BeginCast(0, Damage(Element.Water), 1, out _), Is.EqualTo(CastResult.OnCooldown), "refused casts keep the hand");
            CastContext second = Cast(c, Damage(Element.Water), 1, 1);
            Assert.That(second.WeaponPowerPermille, Is.EqualTo(800));
            Assert.That(second.MasteryPercent, Is.EqualTo(10));
            Assert.That(second.CooldownMs, Is.LessThan(first.CooldownMs), "the faster hand shortens the cooldown");
            Assert.That(first.CooldownMs, Is.LessThan(baseCd), "dual wield speed bonus applies to every hand");
            Assert.That(second.CooldownMs, Is.GreaterThanOrEqualTo(_stats.Coefficients.CdFloorMs));
        }

        [Test]
        public void Weapons_are_departure_only_and_profiles_validate()
        {
            Combatant c = Make();
            Cast(c, Damage(Element.Fire), 10, 0);
            Assert.Throws<ContractException>(() => c.SetWeapons(default));
            Assert.Throws<ContractException>(() => new WeaponProfile((Element)10, 0, 1000, 1000));
            Assert.Throws<ContractException>(() => new WeaponProfile(Element.Fire, 0, 0, 1000));
            Assert.Throws<ContractException>(() => new WeaponProfile(Element.Fire, 0, 1000, 99));
            Assert.Throws<ContractException>(() => new WeaponLoadout(3, Fire, Water, 0, 0, false));
            Assert.Throws<ContractException>(() => new WeaponLoadout(1, Fire, default, -1, 0, false));
            Assert.Throws<ContractException>(() => default(WeaponLoadout).Hand(0));
        }

        [Test]
        public void Weapon_power_scaling_saturates_and_keeps_nonzero_hits()
        {
            Assert.That(DamageModel.ScalePower(0, 0, 5000), Is.Zero);
            Assert.That(DamageModel.ScalePower(1, 0, 1), Is.EqualTo(1));
            Assert.That(DamageModel.ScalePower(DamageModel.MaxPower, 100, DamageModel.MaxWeaponPermille), Is.EqualTo(DamageModel.MaxPower));
            Assert.That(DamageModel.ScalePower(100, 10, 1000), Is.EqualTo(DamageModel.ScalePower(100, 10)));
            Assert.Throws<ContractException>(() => DamageModel.ScalePower(100, 0, 0));
        }

        [Test]
        public void Weapon_range_and_polarity_mismatch_grants_zero_bonuses_and_accessory_floor_clamps_to_100()
        {
            var yinMelee = new WeaponProfile(Element.Fire, 25, 1500, 2000, WeaponRange.Melee, WeaponPolarity.Yin,
                new WeaponAffixes(10, 15, 30, -20));
            Combatant c = Make();
            c.SetWeapons(new WeaponLoadout(1, yinMelee, default, 35, 0, false));
            c.SetElementMasteryFloor(Element.Water, true);

            // Matching melee + yin damage move gets all weapon bonuses.
            var matchMove = new CastMove("m_match", Element.Fire, MoveKind.Damage, 50, 100, 2000, false, default, 0,
                Polarity.Yin, RangeClass.Melee);
            CastContext matchCtx = Cast(c, matchMove, 0, 0);
            Assert.That(matchCtx.MasteryPercent, Is.EqualTo(25));
            Assert.That(matchCtx.WeaponPowerPermille, Is.GreaterThan(1500));
            Assert.That(matchCtx.AccuracyDelta, Is.EqualTo(10));

            // Mismatched polarity (Yang) or mismatched range (Long) grants zero weapon bonuses.
            var wrongPolarity = new CastMove("m_yang", Element.Fire, MoveKind.Damage, 50, 100, 2000, false, default, 0,
                Polarity.Yang, RangeClass.Melee);
            CastContext yangCtx = Cast(c, wrongPolarity, 10000, 0);
            Assert.That(yangCtx.MasteryPercent, Is.Zero);
            Assert.That(yangCtx.WeaponPowerPermille, Is.EqualTo(1000));
            Assert.That(yangCtx.AccuracyDelta, Is.Zero);

            var wrongRange = new CastMove("m_ranged", Element.Fire, MoveKind.Damage, 50, 100, 2000, false, default, 0,
                Polarity.Yin, RangeClass.Long);
            CastContext rangedCtx = Cast(c, wrongRange, 20000, 0);
            Assert.That(rangedCtx.MasteryPercent, Is.Zero);
            Assert.That(rangedCtx.WeaponPowerPermille, Is.EqualTo(1000));

            // Pure effect move checks distance only, and Water floor clamps mastery to 100%.
            var pureEffect = new CastMove("m_buff", Element.Water, MoveKind.Effect, 0, 100, 2000, true,
                new EffectSpec(EffectType.AccuracyMod, false, default, 5, 0, 1), 1000, Polarity.Yang, RangeClass.Melee);
            CastContext buffCtx = Cast(c, pureEffect, 30000, 0);
            Assert.That(buffCtx.MasteryPercent, Is.EqualTo(100));
            Assert.That(buffCtx.AccuracyDelta, Is.EqualTo(10));
        }
    }
}
