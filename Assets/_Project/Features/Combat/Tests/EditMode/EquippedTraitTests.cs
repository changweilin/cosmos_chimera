using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    public sealed class EquippedTraitTests
    {
        private static EffectSpec Mastery(int delta) => new EffectSpec(EffectType.MasteryMod, false, Element.Fire, delta, 0, 1);
        private static TriggerSpec Trigger(TriggerType type, int percent = 0) =>
            new TriggerSpec(type, false, Element.Fire, percent, type == TriggerType.Always ? 0 : 1000);

        [Test]
        public void Independent_partner_triggers_sum_with_the_native_trait_and_expire()
        {
            var book = new EffectBook("native", Trigger(TriggerType.Always), new[] { Mastery(10) });
            book.AddPartnerTrait("summon", Trigger(TriggerType.OnSummon), new[] { Mastery(20) });
            book.AddPartnerTrait("move", Trigger(TriggerType.OnMoveUsed), new[] { Mastery(30) });
            book.NotifySummoned(100, out int healed);
            Assert.That(healed, Is.Zero);
            book.NotifyMoveUsed(Element.Fire, 200, out _);
            var totals = new ModifierTotals();
            book.Collect(200, 100, 100, totals);
            Assert.That(totals.MasteryDelta(Element.Fire), Is.EqualTo(60));
            Assert.That(book.StacksOf("summon", 0, 200), Is.EqualTo(1));
            book.Collect(1100, 100, 100, totals);
            Assert.That(totals.MasteryDelta(Element.Fire), Is.EqualTo(40));
            book.Collect(1200, 100, 100, totals);
            Assert.That(totals.MasteryDelta(Element.Fire), Is.EqualTo(10));
        }

        [Test]
        public void Duplicate_trait_ids_do_not_multiply_the_same_trait()
        {
            var book = new EffectBook("native", Trigger(TriggerType.Always), new[] { Mastery(10) });
            book.AddPartnerTrait("native", Trigger(TriggerType.Always), new[] { Mastery(10) });
            book.AddPartnerTrait("other", Trigger(TriggerType.Always), new[] { Mastery(20) });
            book.AddPartnerTrait("other", Trigger(TriggerType.Always), new[] { Mastery(20) });
            var totals = new ModifierTotals();
            book.Collect(0, 1, 1, totals);
            Assert.That(totals.MasteryDelta(Element.Fire), Is.EqualTo(30));
        }

        [Test]
        public void Partner_hp_cross_charge_rearms_only_on_clear()
        {
            var book = new EffectBook();
            book.AddPartnerTrait("cross", Trigger(TriggerType.OnHpCrossBelow, 50),
                new[] { new EffectSpec(EffectType.HealPercentMaxHp, false, default, 0, 20, 1) });
            book.NotifyHpChanged(100, 40, 100, 100, out int heal);
            Assert.That(heal, Is.EqualTo(20));
            book.NotifyHpChanged(100, 40, 100, 200, out heal);
            Assert.That(heal, Is.Zero);
            book.Clear(300);
            book.NotifyHpChanged(100, 40, 100, 400, out heal);
            Assert.That(heal, Is.EqualTo(20));
        }

        [Test]
        public void Partner_traits_validate_input_capacity_and_assignment_time()
        {
            var book = new EffectBook();
            Assert.Throws<ContractException>(() => book.AddPartnerTrait(null, Trigger(TriggerType.Always), new[] { Mastery(1) }));
            Assert.Throws<ContractException>(() => book.AddPartnerTrait("x", Trigger(TriggerType.Always), null));
            for (int i = 0; i < 4; i++) book.AddPartnerTrait("trait" + i, Trigger(TriggerType.Always), new[] { Mastery(1) });
            Assert.Throws<ContractException>(() => book.AddPartnerTrait("fifth", Trigger(TriggerType.Always), new[] { Mastery(1) }));
            book.NotifySummoned(1, out _);
            Assert.Throws<ContractException>(() => book.AddPartnerTrait("late", Trigger(TriggerType.Always), new[] { Mastery(1) }));
        }
    }
}
