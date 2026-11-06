using Cosmimera.Core.Contracts;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    // Balance values are placeholders, so these assert the formula's invariants and boundaries
    // (testing.md), plus a few hand-computed points that pin the rounding rule.
    public sealed class DamageModelTests
    {
        [TestCase(60, 0, 0, 60)]
        [TestCase(60, 50, 0, 90)]       // mastery 50% → ×1.5
        [TestCase(60, 0, 50, 30)]       // resist 50% → ×0.5
        [TestCase(60, 0, -100, 120)]    // weakness saturates at ×2
        [TestCase(60, 20, -25, 90)]     // 60·1.2·1.25
        [TestCase(33, 0, 50, 17)]       // 16.5 rounds half up
        [TestCase(33, 0, 51, 16)]       // 16.17 rounds down
        public void Hand_computed_points(int power, int mastery, int resist, int expected)
        {
            Assert.AreEqual(expected, DamageModel.Compute(power, mastery, resist));
        }

        [Test]
        public void Resist_100_is_immunity_and_beyond_is_clamped()
        {
            Assert.AreEqual(0, DamageModel.Compute(120, 300, 100));
            Assert.AreEqual(0, DamageModel.Compute(120, 300, 250));
            Assert.AreEqual(DamageModel.Compute(50, 0, -100), DamageModel.Compute(50, 0, -400));
        }

        [Test]
        public void Zero_power_deals_zero()
        {
            Assert.AreEqual(0, DamageModel.Compute(0, 100, -100));
        }

        [Test]
        public void Non_immune_hit_never_rounds_to_zero()
        {
            Assert.AreEqual(1, DamageModel.Compute(1, 0, 99));
            Assert.AreEqual(1, DamageModel.Compute(20, 0, 99));
        }

        [Test]
        public void Maximum_inputs_do_not_overflow()
        {
            int max = DamageModel.Compute(DamageModel.MaxPower, DamageModel.MaxMasteryPercent, -100);
            Assert.AreEqual(DamageModel.MaxPower * 11 * 2, max);
        }

        [Test]
        public void Monotonic_in_every_input()
        {
            for (int resist = -110; resist <= 110; resist += 5)
            {
                for (int mastery = 0; mastery <= 200; mastery += 10)
                {
                    for (int power = 0; power <= 150; power += 3)
                    {
                        int d = DamageModel.Compute(power, mastery, resist);
                        Assert.GreaterOrEqual(d, 0);
                        Assert.GreaterOrEqual(DamageModel.Compute(power + 1, mastery, resist), d, "power");
                        Assert.GreaterOrEqual(DamageModel.Compute(power, mastery + 1, resist), d, "mastery");
                        Assert.LessOrEqual(DamageModel.Compute(power, mastery, resist + 1), d, "resist");
                        Assert.AreEqual(power == 0 || resist >= 100, d == 0, "zero iff no power or immune");
                    }
                }
            }
        }

        [TestCase(-1, 0)]
        [TestCase(DamageModel.MaxPower + 1, 0)]
        [TestCase(10, -1)]
        [TestCase(10, DamageModel.MaxMasteryPercent + 1)]
        public void Out_of_range_inputs_violate_the_contract(int power, int mastery)
        {
            Assert.Throws<ContractException>(() => DamageModel.Compute(power, mastery, 0));
        }
    }
}
