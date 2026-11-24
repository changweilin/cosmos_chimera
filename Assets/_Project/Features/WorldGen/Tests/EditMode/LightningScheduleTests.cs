using Cosmimera.Core.Contracts;
using NUnit.Framework;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class LightningScheduleTests
    {
        [Test]
        public void Chance_is_zero_in_calm_and_grows_with_strength()
        {
            Assert.That(LightningSchedule.ChancePermille(0, 0), Is.Zero);
            Assert.That(LightningSchedule.ChancePermille(1, 0), Is.EqualTo(50));
            Assert.That(LightningSchedule.ChancePermille(1000, 0), Is.EqualTo(166));
            Assert.That(LightningSchedule.ChancePermille(0, 1000), Is.EqualTo(250));
            Assert.That(LightningSchedule.ChancePermille(1000, 1000), Is.EqualTo(416));
            Assert.That(LightningSchedule.ChancePermille(500, 0), Is.GreaterThan(LightningSchedule.ChancePermille(100, 0)));
        }

        [Test]
        public void Strike_rate_matches_the_chance_and_is_reproducible()
        {
            int hits = 0;
            for (long slot = 0; slot < 20_000; slot++)
            {
                bool a = LightningSchedule.TryStrike(9UL, 2, slot, 1000, 0, out int angle, out int reach);
                bool b = LightningSchedule.TryStrike(9UL, 2, slot, 1000, 0, out int angle2, out int reach2);
                Assert.That(b, Is.EqualTo(a));
                if (!a) continue;
                hits++;
                Assert.That(angle, Is.InRange(0, 999));
                Assert.That(reach, Is.InRange(0, 999));
                Assert.That(angle2, Is.EqualTo(angle));
                Assert.That(reach2, Is.EqualTo(reach));
            }

            Assert.That(hits, Is.InRange(166 * 20 - 300, 166 * 20 + 300), "~16.6 % of slots");
            Assert.That(LightningSchedule.TryStrike(9UL, 2, 5, 0, 0, out _, out _), Is.False);
        }

        [Test]
        public void Bad_arguments_are_rejected()
        {
            Assert.Throws<ContractException>(() => LightningSchedule.ChancePermille(-1, 0));
            Assert.Throws<ContractException>(() => LightningSchedule.ChancePermille(0, 1001));
            Assert.Throws<ContractException>(() => LightningSchedule.TryStrike(1UL, -1, 0, 500, 0, out _, out _));
            Assert.Throws<ContractException>(() => LightningSchedule.TryStrike(1UL, 0, -1, 500, 0, out _, out _));
        }
    }
}
