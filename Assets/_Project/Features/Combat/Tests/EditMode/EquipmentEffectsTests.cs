using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    public sealed class EquipmentEffectsTests
    {
        private static EffectSpec Speed(int delta) => new EffectSpec(EffectType.MoveSpeedModPercent, false, default, delta, 0, 1);
        private static ModifierTotals Totals(EffectBook book, long now = 0)
        { var totals = new ModifierTotals(); book.Collect(now, 10, 10, totals); return totals; }

        [Test]
        public void Equipment_coexists_with_four_partners_and_timed_effects_without_using_their_slots()
        {
            var always = new TriggerSpec(TriggerType.Always, false, default, 0, 0);
            var book = new EffectBook("innate", always, new[] { Speed(2) });
            for (int i = 0; i < 4; i++) book.AddPartnerTrait("partner_" + i, always, new[] { Speed(1) });
            book.SetEquipmentEffects(new[] { Speed(5) });
            book.Apply("timed", 0, Speed(3), 1000, 0);
            Assert.That(Totals(book).MoveSpeedPercent, Is.EqualTo(14));
            book.Clear(20);
            Assert.That(Totals(book, 20).MoveSpeedPercent, Is.EqualTo(11));
        }

        [Test]
        public void Equipment_is_copied_replaced_and_not_removed_by_cleansing_or_innate_trait_changes()
        {
            var book = new EffectBook();
            var source = new List<EffectSpec> { Speed(-5) };
            book.SetEquipmentEffects(source); source[0] = Speed(100);
            Assert.That(Totals(book).MoveSpeedPercent, Is.EqualTo(-5));
            book.SetEquipmentEffects(new[] { Speed(10) });
            Assert.That(Totals(book).MoveSpeedPercent, Is.EqualTo(10), "replacement must not stack");
            book.SetEquipmentEffects(System.Array.Empty<EffectSpec>());
            Assert.That(Totals(book).MoveSpeedPercent, Is.Zero);
            book.SetEquipmentEffects(new[] { Speed(-5) });
            book.SetInnateTrait("form", new TriggerSpec(TriggerType.Always, false, default, 0, 0), new[] { Speed(2) });
            book.Apply("debuff", 0, Speed(-3), 1000, 0);
            Assert.That(book.RemoveNegativeTimedEffects(10), Is.EqualTo(1));
            Assert.That(Totals(book, 10).MoveSpeedPercent, Is.EqualTo(-3));
        }

        [Test]
        public void Invalid_equipment_effects_preserve_existing_modifiers()
        {
            var book = new EffectBook(); book.SetEquipmentEffects(new[] { Speed(4) });
            Assert.Throws<ContractException>(() => book.SetEquipmentEffects(null));
            Assert.Throws<ContractException>(() => book.SetEquipmentEffects(new EffectSpec[33]));
            Assert.Throws<ContractException>(() => book.SetEquipmentEffects(new[] { default(EffectSpec) }));
            var heal = new EffectSpec(EffectType.HealPercentMaxHp, false, default, 0, 10, 1);
            Assert.Throws<ContractException>(() => book.SetEquipmentEffects(new[] { Speed(1), heal }));
            Assert.That(Totals(book).MoveSpeedPercent, Is.EqualTo(4));
            book.Clear(1);
            Assert.Throws<ContractException>(() => book.SetEquipmentEffects(System.Array.Empty<EffectSpec>()));
        }

        [Test]
        public void Equipment_allows_32_modifiers_and_uses_existing_multiplier_clamps()
        {
            var book = new EffectBook(); var effects = new EffectSpec[32];
            for (int i = 0; i < effects.Length; i++) effects[i] = Speed(100);
            book.SetEquipmentEffects(effects);
            var config = new EffectRulesConfig(10, 300, 1000);
            Assert.That(EffectMath.Multiplier(Totals(book).MoveSpeedPercent, config), Is.EqualTo(300));
        }
    }
}
