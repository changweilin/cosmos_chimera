using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    // Regressions for docs/reviews/P2-09-sol.md (E1–E6). Each test is named after the finding or mutant it pins.
    public sealed class EffectsReviewSolTests
    {
        private EffectRulesConfig _cfg;
        private DerivedStats _stats;
        private HitVolumeDefaults _d;

        [SetUp]
        public void SetUp()
        {
            _cfg = BalanceFixture.EffectConfig();
            _stats = new DerivedStats(BalanceFixture.Coefficients());
            _d = BalanceFixture.HitDefaults();
        }

        private static EffectSpec Mod(EffectType t, int delta, int maxStacks = 1, Element e = Element.Fire) =>
            new EffectSpec(t, false, e, delta, 0, maxStacks);

        // ---- E1: saturation direction at extreme deltas.

        [Test]
        public void E1_extreme_deltas_saturate_towards_their_sign()
        {
            Assert.AreEqual(DamageModel.MaxMasteryPercent, EffectMath.EffectiveMastery(1, long.MaxValue));
            Assert.AreEqual(0, EffectMath.EffectiveMastery(1, long.MinValue));
            Assert.AreEqual(DamageModel.ResistClamp, EffectMath.EffectiveResist(1, long.MaxValue));
            Assert.AreEqual(-DamageModel.ResistClamp, EffectMath.EffectiveResist(-1, long.MinValue));
            Assert.AreEqual(DamageModel.ResistClamp, EffectMath.EffectiveResist(int.MinValue, long.MaxValue));
            Assert.AreEqual(-DamageModel.ResistClamp, EffectMath.EffectiveResist(int.MaxValue, long.MinValue));
            Assert.AreEqual(DamageModel.MaxMasteryPercent, EffectMath.EffectiveMastery(int.MaxValue, 0));
        }

        // ---- E2: the range bound follows the move's range class, as in MoveGeometry.Build.

        [TestCase(30000, 45000)]
        [TestCase(37000, 55500)]
        public void E2_melee_range_buff_is_not_capped_at_the_ranged_horizon(int reachMm, int expected)
        {
            HitVolume line = MoveGeometry.Build(MoveShape.Line, MoveMovement.None, RangeClass.Melee, default, reachMm, 0, _stats, _d);
            Assert.AreEqual(reachMm, line.ReachMm, "STR 0 keeps melee base range");
            Assert.AreEqual(expected, EffectMath.ApplyRange(line, RangeClass.Melee, 50, _stats, _cfg).ReachMm);
        }

        [Test]
        public void E2_ranged_range_buff_is_capped_and_an_overcapped_ranged_volume_is_rejected()
        {
            int cap = _stats.Coefficients.RangedCapMm;
            HitVolume mid = MoveGeometry.Build(MoveShape.Line, MoveMovement.None, RangeClass.Mid, default, 30000, 0, _stats, _d);
            Assert.AreEqual(cap, EffectMath.ApplyRange(mid, RangeClass.Mid, 50, _stats, _cfg).ReachMm);

            HitVolume meleeLong = MoveGeometry.Build(MoveShape.Line, MoveMovement.None, RangeClass.Melee, default, cap + 1000, 0, _stats, _d);
            Assert.Throws<ContractException>(() => EffectMath.ApplyRange(meleeLong, RangeClass.Long, 10, _stats, _cfg));
            Assert.Throws<ContractException>(() => EffectMath.ApplyRange(mid, (RangeClass)9, 10, _stats, _cfg));
        }

        // ---- E3: preconditions are checked before any early return or match.

        [Test]
        public void E3_undefined_event_elements_and_null_config_are_rejected()
        {
            var anyHit = new EffectBook("trait_x", new TriggerSpec(TriggerType.OnHitTaken, true, default, 0, 1000),
                new[] { Mod(EffectType.EvasionModPercent, 10) });
            Assert.Throws<ContractException>(() => anyHit.NotifyHitTaken((Element)99, 0, out _));
            var plain = new EffectBook();
            Assert.Throws<ContractException>(() => plain.NotifyHitTaken((Element)99, 0, out _));
            Assert.Throws<ContractException>(() => plain.NotifyMoveUsed((Element)99, 0, out _));

            HitVolume line = MoveGeometry.Build(MoveShape.Line, MoveMovement.None, RangeClass.Mid, default, 12000, 0, _stats, _d);
            HitVolume dash = MoveGeometry.Build(MoveShape.Line, MoveMovement.Dash, RangeClass.Melee, default, 5000, 0, _stats, _d);
            Assert.Throws<ContractException>(() => EffectMath.ApplyRange(line, RangeClass.Mid, 0, _stats, null));
            Assert.Throws<ContractException>(() => EffectMath.ApplyRange(dash, RangeClass.Melee, 10, _stats, null));
        }

        // ---- E5: mutants that survived the original suite.

        [Test]
        public void E5_M03_a_one_ms_effect_has_no_stacks_at_its_expiry()
        {
            var book = new EffectBook();
            book.Apply("move_x", 0, Mod(EffectType.EvasionModPercent, 10), 1, 0);
            Assert.AreEqual(1, book.StacksOf("move_x", 0, 0));
            Assert.AreEqual(0, book.StacksOf("move_x", 0, 1));
        }

        [Test]
        public void E5_M12_hp_threshold_uses_64_bit_products()
        {
            var book = new EffectBook("trait_x", new TriggerSpec(TriggerType.OnHpBelow, false, default, 50, 0),
                new[] { Mod(EffectType.EvasionModPercent, 10) });
            var totals = new ModifierTotals();
            book.Collect(0, int.MaxValue, int.MaxValue, totals);
            Assert.AreEqual(0, totals.EvasionPercent, "full HP is not below 50 %");
        }

        [Test]
        public void E5_M19_opposite_percent_sources_cancel_before_the_multiplier_clamp()
        {
            var book = new EffectBook();
            book.Apply("move_fast", 0, new EffectSpec(EffectType.CdModPercent, true, default, -100, 0, 1), 1000, 0);
            book.Apply("move_slow", 0, new EffectSpec(EffectType.CdModPercent, true, default, 100, 0, 1), 1000, 0);
            var totals = new ModifierTotals();
            book.Collect(0, 1, 1, totals);
            long sum = totals.CdPercent(Element.Fire);
            Assert.AreEqual(0, sum);
            Assert.AreEqual(_stats.EffectiveCooldownMs(1000, 0), EffectMath.EffectiveCooldownMs(_stats, 1000, 0, sum, _cfg));
        }

        [Test]
        public void E5_M20_apply_percent_rounds_half_up()
        {
            Assert.AreEqual(2, EffectMath.ApplyPercent(1, 50, _cfg));
        }

        [Test]
        public void E5_M21_cooldown_rounds_any_positive_remainder_up()
        {
            int derived = _stats.EffectiveCooldownMs(1001, 0);
            long ceil = ((long)derived * 101 + 99) / 100;
            Assume.That(ceil, Is.Not.EqualTo(((long)derived * 101 + 50) / 100), "witness must separate ceil from half-up");
            Assert.AreEqual(ceil, EffectMath.EffectiveCooldownMs(_stats, 1001, 0, 1, _cfg));
        }

        [Test]
        public void E5_M30_M32_tick_counting_at_zero_steps_and_high_placements()
        {
            var atZero = new FieldTimeline(0, 3000, 1000);
            Assert.AreEqual(0, atZero.TicksDue(0, 0));
            Assert.AreEqual(1, atZero.TicksDue(-1, 0));

            long placed = 1L << 32;
            var high = new FieldTimeline(placed, 3000, 1000);
            Assert.AreEqual(1, high.TicksDue(placed - 1, placed));
            Assert.AreEqual(3, high.TicksDue(placed - 1, placed + 10_000));
        }

        // ---- E6: element must be defined even where it is meaningless.

        [Test]
        public void E6_specs_reject_an_undefined_element_for_every_type()
        {
            Assert.Throws<ContractException>(() => new EffectSpec(EffectType.EvasionModPercent, false, (Element)99, 5, 0, 1));
            Assert.Throws<ContractException>(() => new TriggerSpec(TriggerType.Always, false, (Element)99, 0, 0));
        }
    }
}
