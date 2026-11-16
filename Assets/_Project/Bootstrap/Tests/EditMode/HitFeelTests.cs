using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using NUnit.Framework;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: combat-feel.md#命中回饋分級 — tiers by damage ÷ max HP, weakness → Heavy, budgeted hitstop.
    public sealed class HitFeelTests
    {
        private static readonly HitFeelTuning T = HitFeelTuning.Default;

        [Test]
        public void Classifies_by_damage_share_of_max_hp()
        {
            Assert.AreEqual(HitTier.Light, HitFeel.Classify(T, 49, 1000, 0, true, false));
            Assert.AreEqual(HitTier.Medium, HitFeel.Classify(T, 50, 1000, 0, true, false));
            Assert.AreEqual(HitTier.Medium, HitFeel.Classify(T, 149, 1000, 0, true, false));
            Assert.AreEqual(HitTier.Heavy, HitFeel.Classify(T, 150, 1000, 0, true, false));
        }

        [Test]
        public void Weakness_is_heavy_immune_and_knockout_override()
        {
            Assert.AreEqual(HitTier.Heavy, HitFeel.Classify(T, 1, 1000, -1, true, false));
            Assert.AreEqual(HitTier.Light, HitFeel.Classify(T, 0, 1000, -50, false, false), "no damage, no weakness flash");
            Assert.AreEqual(HitTier.Immune, HitFeel.Classify(T, 0, 1000, 100, true, false));
            Assert.AreEqual(HitTier.Light, HitFeel.Classify(T, 0, 1000, 100, false, false), "effect-only move is not 'immune'");
            Assert.AreEqual(HitTier.Knockout, HitFeel.Classify(T, 1, 1000, 0, true, true));
        }

        [Test]
        public void Classify_rejects_out_of_domain_input()
        {
            Assert.Throws<ContractException>(() => HitFeel.Classify(T, 1, 0, 0, true, false));
            Assert.Throws<ContractException>(() => HitFeel.Classify(T, -1, 10, 0, true, false));
            Assert.Throws<ContractException>(() => HitFeel.Classify(T, 1, 10, 101, true, false));
            Assert.Throws<ContractException>(() => HitFeel.Classify(T, 1, 10, -101, true, false));
        }

        [Test]
        public void Classify_does_not_overflow_at_int_max()
        {
            Assert.AreEqual(HitTier.Heavy, HitFeel.Classify(T, int.MaxValue, int.MaxValue, 0, true, false));
        }

        [Test]
        public void Field_ticks_cap_at_medium_except_knockout()
        {
            Assert.AreEqual(HitTier.Medium, HitFeel.CapForFieldTick(HitTier.Heavy));
            Assert.AreEqual(HitTier.Light, HitFeel.CapForFieldTick(HitTier.Light));
            Assert.AreEqual(HitTier.Knockout, HitFeel.CapForFieldTick(HitTier.Knockout));
        }

        [Test]
        public void Hitstop_and_shake_are_monotonic_and_boss_is_stronger()
        {
            for (HitTier t = HitTier.Miss; t < HitTier.Knockout; t++)
            {
                Assert.LessOrEqual(T.HitstopMs(t, false), T.HitstopMs(t + 1, false));
                Assert.LessOrEqual(T.ShakeM(t, false, 0f), T.ShakeM(t + 1, false, 0f));
                Assert.GreaterOrEqual(T.HitstopMs(t + 1, true), T.HitstopMs(t + 1, false));
            }

            Assert.AreEqual(0, T.HitstopMs(HitTier.Light, true), "light hits never stop, even on a boss");
        }

        [Test]
        public void Shake_fades_with_distance_from_the_player()
        {
            float near = T.ShakeM(HitTier.Heavy, false, 0f);
            float mid = T.ShakeM(HitTier.Heavy, false, (T.ShakeNearM + T.ShakeFarM) / 2f);
            Assert.Greater(near, mid);
            Assert.Greater(mid, 0f);
            Assert.AreEqual(0f, T.ShakeM(HitTier.Heavy, false, T.ShakeFarM));
        }

        [Test]
        public void Ledger_caps_burst_at_budget_and_overlaps_extend_not_stack()
        {
            var ledger = new HitstopLedger(200);
            Assert.AreEqual(150, ledger.Request(3, 1000f, 150));
            Assert.IsTrue(ledger.IsFrozen(3, 1149f));
            Assert.IsFalse(ledger.IsFrozen(3, 1150f));

            // Second request inside the first freeze only pays for the extension beyond it.
            Assert.AreEqual(0, ledger.Request(3, 1010f, 100), "100 ms ask while 140 ms remain costs nothing");
            // 50 ms still frozen, so 100 ms extension wanted; budget is 50 + 20 ms refill (100 ms × 200/s) = 70.
            Assert.AreEqual(70, ledger.Request(3, 1100f, 150));
            Assert.IsTrue(ledger.IsFrozen(3, 1219f));
            Assert.IsFalse(ledger.IsFrozen(3, 1220f));
        }

        [Test]
        public void Ledger_total_over_a_second_stays_within_twice_the_budget()
        {
            var ledger = new HitstopLedger(200);
            int total = 0;
            for (int ms = 0; ms < 1000; ms += 50)
            {
                total += ledger.Request(0, ms, 150);
            }

            Assert.LessOrEqual(total, 400);
            Assert.GreaterOrEqual(total, 200);
        }

        [Test]
        public void Ledger_units_are_independent_and_reject_bad_input()
        {
            var ledger = new HitstopLedger(200);
            ledger.Request(0, 0f, 200);
            Assert.AreEqual(150, ledger.Request(40, 0f, 150), "growing past the initial capacity starts with a full budget");
            Assert.IsFalse(ledger.IsFrozen(7, 0f));
            Assert.IsFalse(ledger.IsFrozen(-1, 0f));
            Assert.Throws<ContractException>(() => ledger.Request(-1, 0f, 10));
            Assert.Throws<ContractException>(() => ledger.Request(0, 0f, -1));
            Assert.Throws<ContractException>(() => new HitstopLedger(0));
        }
    }
}
