using Cosmimera.Core.Contracts;
using NUnit.Framework;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class WeatherControlTests
    {
        private const int Cooldown = WeatherPlanner.DefaultSlotDurationMs;

        [Test]
        public void Skill_and_cooldown_refusals_preserve_weather_and_the_original_deadline()
        {
            var control = new WeatherControl(Cooldown);
            Assert.That(control.CooldownRemainingMs(0), Is.Zero);
            Assert.That(control.TryApply(1, WeatherKind.Rain, 0, false), Is.EqualTo(WeatherControlResult.MissingSkill));
            Assert.That(control.TryGetOverride(1, 0, out WeatherProfile absent), Is.False);
            Assert.That(absent.Kind, Is.EqualTo(WeatherKind.Clear));
            Assert.That(control.CooldownRemainingMs(0), Is.Zero);
            Assert.That(control.TryApply(1, WeatherKind.Rain, 0, true), Is.EqualTo(WeatherControlResult.Applied));
            Assert.That(control.TryApply(2, WeatherKind.Gale, 1, true), Is.EqualTo(WeatherControlResult.OnCooldown));
            Assert.That(control.TryApply(1, WeatherKind.Clear, 1, false), Is.EqualTo(WeatherControlResult.MissingSkill));
            Assert.That(control.TryGetOverride(1, 1, out WeatherProfile weather), Is.True);
            Assert.That(weather.Kind, Is.EqualTo(WeatherKind.Rain));
            Assert.That(control.TryGetOverride(2, 1, out _), Is.False);
            Assert.That(control.CooldownRemainingMs(Cooldown - 1), Is.EqualTo(1));
            Assert.That(control.CooldownRemainingMs(Cooldown), Is.Zero);
            Assert.That(control.TryApply(2, WeatherKind.Clear, Cooldown, true), Is.EqualTo(WeatherControlResult.Applied));
            Assert.That(control.TryGetOverride(2, Cooldown, out weather), Is.True, "clear weather is also an explicit override");
            Assert.That(weather.Kind, Is.EqualTo(WeatherKind.Clear));
        }

        [Test]
        public void A_last_millisecond_cast_expires_at_the_next_slot_but_keeps_its_full_cooldown()
        {
            var control = new WeatherControl(Cooldown);
            long castAt = WeatherPlanner.DefaultSlotDurationMs - 1;
            Assert.That(control.TryApply(4, WeatherKind.Fog, castAt, true), Is.EqualTo(WeatherControlResult.Applied));
            Assert.That(control.TryGetOverride(4, castAt, out _), Is.True);
            Assert.That(control.TryGetOverride(5, castAt, out _), Is.False, "leaving does not carry weather to another region");
            Assert.That(control.TryGetOverride(4, castAt, out _), Is.True, "returning in the same slot preserves the override");
            Assert.That(control.TryGetOverride(4, castAt + 1, out _), Is.False);
            Assert.That(control.CooldownRemainingMs(castAt + 1), Is.EqualTo(Cooldown - 1));
            Assert.That(control.TryApply(5, WeatherKind.Blizzard, castAt + 1, true), Is.EqualTo(WeatherControlResult.OnCooldown));
            Assert.That(control.TryApply(5, WeatherKind.Blizzard, castAt + Cooldown, true), Is.EqualTo(WeatherControlResult.Applied));
        }

        [Test]
        public void Applying_to_another_region_does_not_erase_an_existing_override()
        {
            var control = new WeatherControl(1);
            control.TryApply(1, WeatherKind.Rain, 0, true);
            control.TryApply(2, WeatherKind.Gale, 1, true);
            Assert.That(control.TryGetOverride(1, 1, out WeatherProfile first), Is.True);
            Assert.That(first.Kind, Is.EqualTo(WeatherKind.Rain));
            Assert.That(control.TryGetOverride(2, 1, out WeatherProfile second), Is.True);
            Assert.That(second.Kind, Is.EqualTo(WeatherKind.Gale));
        }

        [Test]
        public void Extreme_clock_values_do_not_overflow_expiry_or_cooldown()
        {
            var control = new WeatherControl(Cooldown);
            Assert.That(control.TryApply(0, WeatherKind.Gale, long.MaxValue, true), Is.EqualTo(WeatherControlResult.Applied));
            Assert.That(control.TryGetOverride(0, long.MaxValue, out WeatherProfile weather), Is.True);
            Assert.That(weather.Kind, Is.EqualTo(WeatherKind.Gale));
            Assert.That(control.CooldownRemainingMs(long.MaxValue), Is.EqualTo(Cooldown));
            var early = new WeatherControl(Cooldown);
            early.TryApply(0, WeatherKind.Rain, 0, true);
            Assert.That(early.CooldownRemainingMs(long.MaxValue), Is.Zero);
            Assert.That(early.TryGetOverride(0, long.MaxValue, out _), Is.False);
        }

        [Test]
        public void Invalid_inputs_fail_before_mutating_the_controller()
        {
            Assert.Throws<ContractException>(() => new WeatherControl(0));
            Assert.Throws<ContractException>(() => new WeatherControl(-1));
            var control = new WeatherControl(Cooldown);
            Assert.Throws<ContractException>(() => control.TryApply(-1, WeatherKind.Rain, 0, true));
            Assert.Throws<ContractException>(() => control.TryApply(0, (WeatherKind)(-1), 0, true));
            Assert.Throws<ContractException>(() => control.TryApply(0, (WeatherKind)8, 0, true));
            Assert.Throws<ContractException>(() => control.TryApply(0, WeatherKind.Rain, -1, true));
            Assert.Throws<ContractException>(() => control.TryGetOverride(-1, 0, out _));
            Assert.Throws<ContractException>(() => control.TryGetOverride(0, -1, out _));
            Assert.Throws<ContractException>(() => control.CooldownRemainingMs(-1));
            Assert.That(control.CooldownRemainingMs(0), Is.Zero);
            control.TryApply(0, WeatherKind.Rain, 100, true);
            Assert.Throws<ContractException>(() => control.TryApply(0, WeatherKind.Fog, 99, true));
            Assert.Throws<ContractException>(() => control.TryGetOverride(0, 99, out _));
            Assert.Throws<ContractException>(() => control.CooldownRemainingMs(99));
        }
    }
}
