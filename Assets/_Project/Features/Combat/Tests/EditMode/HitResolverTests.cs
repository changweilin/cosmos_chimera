using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    public sealed class HitResolverTests
    {
        // P2-04 acceptance: with a fixed seed the observed hit rate is within 1 % of the requested chance.
        // N = 200k gives σ ≤ 0.11 %, so 1 % is > 9σ and the fixed seed makes it deterministic anyway.
        [TestCase(50)]
        [TestCase(123)]
        [TestCase(500)]
        [TestCase(772)]
        [TestCase(840)]
        [TestCase(999)]
        public void Fixed_seed_hit_rate_is_within_one_percent(int chancePermille)
        {
            var rng = new Pcg32(0xC0FFEEUL, 7UL);
            const int n = 200_000;
            int hits = 0;
            for (int i = 0; i < n; i++)
            {
                if (HitResolver.Resolve(true, false, chancePermille, ref rng) == HitOutcome.Hit)
                {
                    hits++;
                }
            }

            Assert.That(System.Math.Abs((double)hits / n - chancePermille / 1000.0), Is.LessThan(0.01));
        }

        [Test]
        public void Certain_hit_always_hits()
        {
            var rng = new Pcg32(1UL, 1UL);
            for (int i = 0; i < 10_000; i++)
            {
                Assert.AreEqual(HitOutcome.Hit, HitResolver.Resolve(true, false, 1000, ref rng));
            }
        }

        [Test]
        public void No_contact_and_invulnerable_do_not_consume_the_stream()
        {
            var rng = new Pcg32(42UL, 3UL);
            var twin = new Pcg32(42UL, 3UL);

            Assert.AreEqual(HitOutcome.NoContact, HitResolver.Resolve(false, false, 500, ref rng));
            Assert.AreEqual(HitOutcome.NoContact, HitResolver.Resolve(false, true, 500, ref rng));
            Assert.AreEqual(HitOutcome.Invulnerable, HitResolver.Resolve(true, true, 500, ref rng));

            Assert.AreEqual(twin.NextUInt(), rng.NextUInt());
        }

        [Test]
        public void Same_seed_and_contact_order_reproduce_the_same_outcomes()
        {
            var a = new Pcg32(99UL, 5UL);
            var b = new Pcg32(99UL, 5UL);
            for (int i = 0; i < 1000; i++)
            {
                int chance = 50 + i % 951;
                Assert.AreEqual(HitResolver.Resolve(true, false, chance, ref a), HitResolver.Resolve(true, false, chance, ref b));
            }
        }

        [TestCase(0)]
        [TestCase(1001)]
        [TestCase(-1)]
        public void Chance_outside_1_to_1000_violates_precondition(int chance)
        {
            var rng = new Pcg32(1UL, 1UL);
            Assert.Throws<ContractException>(() => HitResolver.Resolve(true, false, chance, ref rng));
        }
    }
}
