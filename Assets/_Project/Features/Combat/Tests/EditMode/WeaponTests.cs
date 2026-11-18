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
    }
}
