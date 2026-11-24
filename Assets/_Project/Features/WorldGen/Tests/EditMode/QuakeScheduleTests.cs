using Cosmimera.Core.Contracts;
using NUnit.Framework;

namespace Cosmimera.WorldGen.Tests
{
    // REQ: docs/design/planet-world.md#地震噴發 — lava planet every 3–5 min, volcanic ground every 6–10 min, per region.
    public sealed class QuakeScheduleTests
    {
        [TestCase(true, 180_000L, 300_000L)]
        [TestCase(false, 360_000L, 600_000L)]
        public void Consecutive_quakes_keep_the_spacing_of_their_planet(bool lava, long min, long max)
        {
            for (int region = 0; region < 6; region++)
            {
                QuakeSchedule.Timing(17UL, region, 0, lava, out long previous, out _, out _);
                Assert.That(previous, Is.GreaterThanOrEqualTo(0));
                for (long window = 1; window < 120; window++)
                {
                    QuakeSchedule.Timing(17UL, region, window, lava, out long start, out _, out _);
                    Assert.That(start - previous, Is.InRange(min, max));
                    previous = start;
                }
            }
        }

        [Test]
        public void Eruption_length_and_crack_count_stay_in_range()
        {
            for (long window = 0; window < 300; window++)
            {
                QuakeSchedule.Timing(3UL, 2, window, true, out _, out int erupt, out int cracks);
                Assert.That(erupt, Is.InRange(6000, 10_000));
                Assert.That(cracks, Is.InRange(2, QuakeSchedule.MaxCracks));
                for (int attempt = 0; attempt < QuakeSchedule.CrackAttempts; attempt++)
                {
                    QuakeSchedule.Crack(3UL, 2, window, attempt, out int angle, out int reach, out int heading, out int length);
                    Assert.That(angle, Is.InRange(0, 999));
                    Assert.That(reach, Is.InRange(0, 999));
                    Assert.That(heading, Is.InRange(0, 1799));
                    Assert.That(length, Is.InRange(15, 40));
                }
            }
        }

        [Test]
        public void Bombs_land_beside_the_crack_and_the_second_is_rarer()
        {
            int first = 0, second = 0;
            const int slots = 4000;
            for (long slot = 0; slot < slots; slot++)
            {
                for (int bomb = 0; bomb < QuakeSchedule.BombsPerCrackSlot; bomb++)
                {
                    if (!QuakeSchedule.TryBomb(5UL, 1, 7, slot, 1, bomb, out int along, out int side)) continue;
                    if (bomb == 0) first++;
                    else second++;
                    Assert.That(along, Is.InRange(0, 999));
                    Assert.That(System.Math.Abs(side), Is.InRange(40, 140));
                }
            }

            Assert.That(first / (float)slots, Is.EqualTo(0.7f).Within(0.03f));
            Assert.That(second / (float)slots, Is.EqualTo(0.35f).Within(0.03f));
        }

        [Test]
        public void Same_inputs_give_the_same_quake_and_another_region_differs()
        {
            QuakeSchedule.Timing(99UL, 4, 12, false, out long a, out int ea, out int ca);
            QuakeSchedule.Timing(99UL, 4, 12, false, out long b, out int eb, out int cb);
            QuakeSchedule.Timing(99UL, 5, 12, false, out long c, out int ec, out _);
            Assert.That((a, ea, ca), Is.EqualTo((b, eb, cb)));
            Assert.That((a, ea), Is.Not.EqualTo((c, ec)));
        }

        [Test]
        public void Out_of_range_inputs_are_rejected()
        {
            Assert.Throws<ContractException>(() => QuakeSchedule.Timing(1UL, -1, 0, true, out _, out _, out _));
            Assert.Throws<ContractException>(() => QuakeSchedule.Timing(1UL, 0, -1, true, out _, out _, out _));
            Assert.Throws<ContractException>(() => QuakeSchedule.Crack(1UL, 0, 0, QuakeSchedule.CrackAttempts, out _, out _, out _, out _));
            Assert.Throws<ContractException>(() => QuakeSchedule.TryBomb(1UL, 0, 0, 0, QuakeSchedule.MaxCracks, 0, out _, out _));
            Assert.Throws<ContractException>(() => QuakeSchedule.TryBomb(1UL, 0, 0, -1, 0, 0, out _, out _));
        }
    }
}
