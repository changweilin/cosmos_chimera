using Cosmimera.Core.Contracts;
using NUnit.Framework;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class WeatherControlTests
    {
        private const int Cooldown = 180_000;

        [Test]
        public void Refusals_change_nothing_and_applied_starts_the_shared_cooldown()
        {
            var control = new WeatherControl(Cooldown);
            Assert.That(control.TryApply(1, WeatherKind.Rain, 0, false, false, false), Is.EqualTo(WeatherControlResult.MissingSkill));
            Assert.That(control.OverridesFor(1), Is.Empty);
            Assert.That(control.CooldownRemainingMs(0), Is.Zero);

            Assert.That(control.TryApply(1, WeatherKind.Rain, 0, true, false, false), Is.EqualTo(WeatherControlResult.Applied));
            Assert.That(control.OverridesFor(1).Count, Is.EqualTo(1));
            Assert.That(control.TryApply(2, WeatherKind.Gale, 1, true, false, false), Is.EqualTo(WeatherControlResult.OnCooldown));
            Assert.That(control.TryDispelExotics(2, 1, true), Is.EqualTo(WeatherControlResult.OnCooldown));
            Assert.That(control.OverridesFor(2), Is.Empty, "leaving does not carry weather to another region");
            Assert.That(control.CooldownRemainingMs(1), Is.EqualTo(Cooldown - 1));
            Assert.That(control.TryApply(2, WeatherKind.Clear, Cooldown, true, false, false), Is.EqualTo(WeatherControlResult.Applied));
        }

        [Test]
        public void Exotics_need_native_terrain_and_base_weather_is_refused_indoors()
        {
            var control = new WeatherControl(Cooldown);
            Assert.That(control.TryApply(0, WeatherKind.VolcanicAsh, 0, true, false, false), Is.EqualTo(WeatherControlResult.NotHere));
            Assert.That(control.TryApply(0, WeatherKind.Rain, 0, true, false, true), Is.EqualTo(WeatherControlResult.NotHere));
            Assert.That(control.CooldownRemainingMs(0), Is.Zero, "refusals do not start the cooldown");
            Assert.That(control.TryApply(0, WeatherKind.RadDust, 0, true, true, true), Is.EqualTo(WeatherControlResult.Applied),
                "an exotic native to an indoor dungeon may be summoned there");
            WeatherOverride pin = control.OverridesFor(0)[0];
            Assert.That(pin.Value((int)WeatherKind.RadDust), Is.EqualTo(1000));
            Assert.That(pin.Mask & 0xFFu, Is.Zero, "an exotic summon leaves base attributes natural");
        }

        [Test]
        public void Pin_lasts_for_the_hold_and_dispel_clears_every_exotic()
        {
            var control = new WeatherControl(Cooldown);
            Assert.That(control.TryDispelExotics(3, 1000, true), Is.EqualTo(WeatherControlResult.Applied));
            Assert.That(control.PinnedRemainingMs(3, 1000), Is.EqualTo(WeatherField.OverrideHoldMs));
            Assert.That(control.PinnedRemainingMs(3, 1000 + WeatherField.OverrideHoldMs - 1), Is.EqualTo(1));
            Assert.That(control.PinnedRemainingMs(3, 1000 + WeatherField.OverrideHoldMs), Is.Zero);
            Assert.That(control.PinnedRemainingMs(4, 1000), Is.Zero);
            WeatherOverride pin = control.OverridesFor(3)[0];
            for (int i = (int)WeatherKind.RadDust; i <= (int)WeatherKind.Miasma; i++) Assert.That(pin.Value(i), Is.Zero);
            Assert.That(pin.Mask, Is.EqualTo(0x3FF00u));
        }

        [Test]
        public void Contracts_reject_invalid_arguments()
        {
            Assert.Throws<ContractException>(() => new WeatherControl(0));
            var control = new WeatherControl(Cooldown);
            Assert.Throws<ContractException>(() => control.TryApply(-1, WeatherKind.Rain, 0, true, false, false));
            Assert.Throws<ContractException>(() => control.TryApply(0, (WeatherKind)(-1), 0, true, false, false));
            Assert.Throws<ContractException>(() => control.TryApply(0, (WeatherKind)18, 0, true, false, false));
            Assert.Throws<ContractException>(() => control.TryApply(0, WeatherKind.Rain, -1, true, false, false));
            Assert.Throws<ContractException>(() => control.TryDispelExotics(-1, 0, true));
            Assert.Throws<ContractException>(() => control.OverridesFor(-1));
            Assert.Throws<ContractException>(() => control.PinnedRemainingMs(0, -1));

            control.TryApply(0, WeatherKind.Rain, 100, true, false, false);
            Assert.Throws<ContractException>(() => control.TryApply(0, WeatherKind.Fog, 99, true, false, false));
            Assert.Throws<ContractException>(() => control.CooldownRemainingMs(99));
            Assert.Throws<ContractException>(() => control.OverridesFor(0)[0].Value(18));
        }
    }
}
