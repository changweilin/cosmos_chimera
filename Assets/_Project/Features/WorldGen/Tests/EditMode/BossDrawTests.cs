using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class BossDrawTests
    {
        [Test]
        public void Juvenile_cap_always_draws_juvenile()
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                Assert.AreEqual(CreatureStage.Juvenile, BossDraw.Stage(seed, CreatureStage.Juvenile));
            }
        }

        [TestCase(CreatureStage.Adolescent)]
        [TestCase(CreatureStage.Adult)]
        public void Draw_stays_within_the_cap_and_reaches_every_unlocked_stage(CreatureStage cap)
        {
            var seen = new int[3];
            for (ulong seed = 0; seed < 3000; seed++)
            {
                CreatureStage s = BossDraw.Stage(seed, cap);
                Assert.That(s, Is.InRange(CreatureStage.Juvenile, cap));
                seen[(int)s]++;
            }

            // Uniform among unlocked stages (user 2026-11-10); a loose band so this tests the shape, not the RNG.
            int unlocked = (int)cap + 1;
            for (int stage = 0; stage < unlocked; stage++)
            {
                Assert.That(seen[stage], Is.InRange(3000 / unlocked * 8 / 10, 3000 / unlocked * 12 / 10), $"stage {stage}");
            }
        }

        [Test]
        public void Same_seed_same_stage()
        {
            for (ulong seed = 0; seed < 50; seed++)
            {
                Assert.AreEqual(BossDraw.Stage(seed, CreatureStage.Adult), BossDraw.Stage(seed, CreatureStage.Adult));
            }
        }

        // SOL W-B2-SOL-05: uniformity and self-replay cannot tell (seed,'B') from any other stream id. Expected stages
        // (0 juvenile, 1 adolescent, 2 adult) for seeds 0..15 come from an independent Python PCG32 reimplementation.
        [TestCase(CreatureStage.Adolescent, new[] { 1, 1, 1, 1, 1, 0, 0, 1, 0, 1, 1, 1, 0, 1, 1, 0 })]
        [TestCase(CreatureStage.Adult, new[] { 1, 1, 1, 2, 1, 0, 2, 2, 0, 0, 2, 0, 2, 0, 1, 1 })]
        public void Golden_vector_pins_the_B_stream(CreatureStage cap, int[] expected)
        {
            var actual = new int[expected.Length];
            for (int seed = 0; seed < expected.Length; seed++)
            {
                actual[seed] = (int)BossDraw.Stage((ulong)seed, cap);
            }

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Size_follows_the_boss_stage()
        {
            Assert.AreEqual(PlanetSize.Asteroid, BossDraw.SizeFor(CreatureStage.Juvenile));
            Assert.AreEqual(PlanetSize.Moon, BossDraw.SizeFor(CreatureStage.Adolescent));
            Assert.AreEqual(PlanetSize.Planet, BossDraw.SizeFor(CreatureStage.Adult));
        }

        [Test]
        public void Undefined_stage_is_rejected()
        {
            Assert.Throws<ContractException>(() => BossDraw.Stage(1, (CreatureStage)3));
            Assert.Throws<ContractException>(() => BossDraw.Stage(1, (CreatureStage)(-1)));
            Assert.Throws<ContractException>(() => BossDraw.SizeFor((CreatureStage)3));
        }
    }
}
