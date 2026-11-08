using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using NUnit.Framework;

namespace Cosmimera.Capture.Tests
{
    public sealed class CaptureRulesTests
    {
        [Test]
        public void Chance_uses_boss_or_base_plus_bounty_clamped()
        {
            Assert.That(CaptureRules.ChancePercent(false, false, 60, 100), Is.EqualTo(60));
            Assert.That(CaptureRules.ChancePercent(false, true, 60, 100), Is.EqualTo(70));
            Assert.That(CaptureRules.ChancePercent(true, true, 60, 100), Is.EqualTo(100));
            Assert.That(CaptureRules.ChancePercent(true, false, 60, 95), Is.EqualTo(95));
        }

        [Test]
        public void Chance_rejects_out_of_range_percents()
        {
            Assert.Throws<ContractException>(() => CaptureRules.ChancePercent(false, false, 101, 100));
            Assert.Throws<ContractException>(() => CaptureRules.ChancePercent(false, false, 50, -1));
        }

        [Test]
        public void Roll_extremes_are_certain()
        {
            var rng = new Pcg32(1, 2);
            for (int i = 0; i < 1000; i++)
            {
                Assert.That(CaptureRules.Roll(100, ref rng), Is.True);
                Assert.That(CaptureRules.Roll(0, ref rng), Is.False);
            }
        }

        [Test]
        public void Roll_rate_matches_chance_within_one_percent()
        {
            var rng = new Pcg32(42, 7);
            int hits = 0;
            const int n = 200000;
            for (int i = 0; i < n; i++)
            {
                if (CaptureRules.Roll(60, ref rng))
                {
                    hits++;
                }
            }

            Assert.That(hits / (double)n, Is.EqualTo(0.6).Within(0.01));
        }

        [Test]
        public void Roll_rejects_out_of_range_chance()
        {
            var rng = new Pcg32(1, 2);
            Assert.Throws<ContractException>(() => CaptureRules.Roll(101, ref rng));
        }
    }
}
