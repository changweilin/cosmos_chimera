using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    // Q-C18 (user decision 2026-11-07): on_hp_cross_below fires once per combat when HP falls through the threshold.
    public sealed class EffectsHpCrossBelowTests
    {
        private static readonly EffectSpec Heal12 = new EffectSpec(EffectType.HealPercentMaxHp, false, default, 0, 12, 1);
        private static readonly EffectSpec Mastery20 = new EffectSpec(EffectType.MasteryMod, false, Element.Fire, 20, 0, 1);

        private static EffectBook Book(int percent, int durationMs, params EffectSpec[] effects) =>
            new EffectBook("trait_x", new TriggerSpec(TriggerType.OnHpCrossBelow, false, default, percent, durationMs), effects);

        private static int Heal(EffectBook book, int previousHp, int hp, long now, int maxHp = 100)
        {
            book.NotifyHpChanged(previousHp, hp, maxHp, now, out int heal);
            return heal;
        }

        [Test]
        public void TriggerSpec_needs_both_percent_and_duration()
        {
            Assert.DoesNotThrow(() => new TriggerSpec(TriggerType.OnHpCrossBelow, false, default, 25, 1000));
            Assert.Throws<ContractException>(() => new TriggerSpec(TriggerType.OnHpCrossBelow, false, default, 0, 1000));
            Assert.Throws<ContractException>(() => new TriggerSpec(TriggerType.OnHpCrossBelow, false, default, 101, 1000));
            Assert.Throws<ContractException>(() => new TriggerSpec(TriggerType.OnHpCrossBelow, false, default, 25, 0));
            Assert.Throws<ContractException>(() => new TriggerSpec(TriggerType.OnHpCrossBelow, true, default, 25, 1000));
            Assert.Throws<ContractException>(() => new TriggerSpec((TriggerType)8, false, default, 25, 1000));
            Assert.IsFalse(new TriggerSpec(TriggerType.OnHpCrossBelow, false, default, 25, 1000).IsContinuous);
        }

        [Test]
        public void A_heal_is_allowed_because_the_trigger_is_an_event()
        {
            Assert.DoesNotThrow(() => Book(25, 1000, Heal12));
        }

        // Boundary is exact integer math: previous·100 >= p·max and 0 < hp·100 < p·max.
        [TestCase(25, 100, 25, 24, true)]
        [TestCase(25, 100, 26, 25, false)]
        [TestCase(25, 100, 24, 23, false)]
        [TestCase(25, 1000, 250, 249, true)]
        [TestCase(25, 1000, 251, 250, false)]
        [TestCase(100, 100, 100, 99, true)]
        [TestCase(1, 100, 1, 0, false)]
        [TestCase(30, 100, 30, 1, true)]
        public void Fires_only_when_hp_falls_through_the_threshold(int percent, int maxHp, int previousHp, int hp, bool fires)
        {
            EffectBook book = Book(percent, 1000, Heal12);
            Assert.AreEqual(fires ? 12 : 0, Heal(book, previousHp, hp, 0, maxHp));
        }

        [Test]
        public void Fires_once_per_combat_and_Clear_rearms_it()
        {
            EffectBook book = Book(25, 1000, Heal12);
            Assert.AreEqual(12, Heal(book, 100, 20, 0));
            Assert.AreEqual(0, Heal(book, 20, 30, 10));
            Assert.AreEqual(0, Heal(book, 30, 10, 20), "the charge is spent for the rest of the combat");

            book.Clear(30);
            Assert.AreEqual(12, Heal(book, 30, 10, 40));
        }

        [Test]
        public void A_drop_to_zero_does_not_fire_or_spend_the_charge()
        {
            EffectBook book = Book(25, 1000, Heal12);
            Assert.AreEqual(0, Heal(book, 100, 0, 0));
            Assert.AreEqual(12, Heal(book, 100, 10, 10));
        }

        [Test]
        public void Timed_effects_open_a_window_and_are_not_continuous()
        {
            EffectBook book = Book(25, 1000, Mastery20, Heal12);
            var totals = new ModifierTotals();

            book.Collect(0, 10, 100, totals);
            Assert.AreEqual(0, totals.MasteryDelta(Element.Fire), "being below the threshold alone does nothing");

            Assert.AreEqual(12, Heal(book, 100, 10, 100));
            book.Collect(1099, 10, 100, totals);
            Assert.AreEqual(20, totals.MasteryDelta(Element.Fire));
            book.Collect(1100, 10, 100, totals);
            Assert.AreEqual(0, totals.MasteryDelta(Element.Fire));
        }

        [Test]
        public void Other_triggers_and_trait_less_books_ignore_hp_changes()
        {
            var summon = new EffectBook("trait_y", new TriggerSpec(TriggerType.OnSummon, false, default, 0, 1000), new[] { Heal12 });
            Assert.AreEqual(0, Heal(summon, 100, 10, 0));
            Assert.AreEqual(0, Heal(new EffectBook(), 100, 10, 0));
        }

        [TestCase(-1, 10, 100)]
        [TestCase(101, 10, 100)]
        [TestCase(100, -1, 100)]
        [TestCase(100, 101, 100)]
        [TestCase(0, 0, 0)]
        public void Rejects_hp_outside_the_domain(int previousHp, int hp, int maxHp)
        {
            EffectBook book = Book(25, 1000, Heal12);
            Assert.Throws<ContractException>(() => book.NotifyHpChanged(previousHp, hp, maxHp, 0, out _));
        }

        [Test]
        public void Rejects_time_before_the_watermark()
        {
            EffectBook book = Book(25, 1000, Heal12);
            book.Clear(500);
            Assert.Throws<ContractException>(() => book.NotifyHpChanged(100, 10, 100, 499, out _));
        }
    }
}
