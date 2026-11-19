using Cosmimera.Core.Contracts;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    // Balance values are placeholders, so these assert the formula's invariants and boundaries
    // (testing.md), plus a few hand-computed points that pin the rounding rule.
    public sealed class DamageModelTests
    {
        [TestCase(60, 100, 100, 100, 100, 0, 60)]     // all neutral (100) -> 1x
        [TestCase(60, 150, 100, 100, 100, 0, 120)]    // mastery 150 -> 2x
        [TestCase(60, 50, 100, 100, 100, 0, 30)]      // mastery 50 -> 0.5x
        [TestCase(60, 0, 100, 100, 100, 0, 15)]       // mastery 0 -> 0.25x
        [TestCase(60, 125, 100, 100, 100, 0, 85)]     // mastery 125 -> 60 * sqrt(2) = 84.85 -> 85
        [TestCase(60, 100, 150, 100, 100, 0, 30)]     // resist 150 -> 0.5x
        [TestCase(60, 100, 50, 100, 100, 0, 120)]     // resist 50 -> 2x
        [TestCase(60, 100, 100, 150, 100, 0, 120)]    // atk 150 vs def 100 -> 2x
        [TestCase(60, 100, 100, 50, 100, 0, 30)]      // atk 50 vs def 100 -> 0.5x
        [TestCase(60, 100, 100, 100, 150, 0, 30)]     // atk 100 vs def 150 -> 0.5x
        [TestCase(60, 100, 100, 100, 100, 25, 120)]   // +25 levels -> 2x
        [TestCase(33, 100, 150, 100, 100, 0, 17)]     // 16.5 rounds half up
        public void Hand_computed_points(int power, int mastery, int resist, int atk, int def, int levelDiff, int expected)
        {
            Assert.AreEqual(expected, DamageModel.Compute(power, mastery, resist, atk, def, levelDiff));
        }

        [Test]
        public void Three_arg_overload_matches_neutral_attack_and_defence()
        {
            Assert.AreEqual(
                DamageModel.Compute(60, 125, 75, DamageModel.NeutralStat, DamageModel.NeutralStat, 0),
                DamageModel.Compute(60, 125, 75));
        }

        [Test]
        public void Zero_power_deals_zero()
        {
            Assert.AreEqual(0, DamageModel.Compute(0, 125, 25, 200, 50));
        }

        [Test]
        public void Non_zero_power_never_rounds_to_zero()
        {
            Assert.AreEqual(1, DamageModel.Compute(1, 0, 300, 0, 500));
            Assert.AreEqual(1, DamageModel.Compute(20, 0, 300, 0, 500));
        }

        [Test]
        public void Extreme_inputs_saturate_without_overflow()
        {
            int max = DamageModel.Compute(DamageModel.MaxPower, DamageModel.MaxMasteryPercent, -DamageModel.ResistClamp, 1000, 0, 100);
            Assert.AreEqual(int.MaxValue, max);
        }

        [Test]
        public void Monotonic_in_every_input()
        {
            for (int resist = 25; resist <= 175; resist += 25)
            {
                for (int mastery = 0; mastery <= 125; mastery += 25)
                {
                    for (int power = 0; power <= 150; power += 15)
                    {
                        int d = DamageModel.Compute(power, mastery, resist, 100, 100);
                        Assert.GreaterOrEqual(d, 0);
                        Assert.GreaterOrEqual(DamageModel.Compute(power + 1, mastery, resist, 100, 100), d, "power");
                        Assert.GreaterOrEqual(DamageModel.Compute(power, mastery + 1, resist, 100, 100), d, "mastery");
                        Assert.LessOrEqual(DamageModel.Compute(power, mastery, resist + 1, 100, 100), d, "resist");
                        Assert.GreaterOrEqual(DamageModel.Compute(power, mastery, resist, 101, 100), d, "attack");
                        Assert.LessOrEqual(DamageModel.Compute(power, mastery, resist, 100, 101), d, "defence");
                        Assert.AreEqual(power == 0, d == 0, "zero iff power == 0");
                    }
                }
            }
        }

        [TestCase(-1, 0, 100, 100)]
        [TestCase(DamageModel.MaxPower + 1, 0, 100, 100)]
        [TestCase(10, -1, 100, 100)]
        [TestCase(10, DamageModel.MaxMasteryPercent + 1, 100, 100)]
        [TestCase(10, 100, -1, 100)]
        [TestCase(10, 100, 100, -1)]
        public void Out_of_range_inputs_violate_the_contract(int power, int mastery, int atk, int def)
        {
            Assert.Throws<ContractException>(() => DamageModel.Compute(power, mastery, 100, atk, def));
        }
    }
}
