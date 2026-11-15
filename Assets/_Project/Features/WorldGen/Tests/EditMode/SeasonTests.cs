using NUnit.Framework;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class SeasonTests
    {
        [Test]
        public void The_same_seed_always_gives_the_same_defined_season()
        {
            for (ulong seed = 0; seed < 500; seed++)
            {
                Season a = Seasons.For(seed);
                Assert.That(a, Is.EqualTo(Seasons.For(seed)));
                Assert.That((int)a, Is.InRange(0, Seasons.Count - 1));
            }

            Assert.That((int)Seasons.For(ulong.MaxValue), Is.InRange(0, Seasons.Count - 1));
        }

        [Test]
        public void Every_season_occurs_about_equally_often_over_seeds()
        {
            const int Seeds = 4000;
            var counts = new int[Seasons.Count];
            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                counts[(int)Seasons.For(seed)]++;
            }

            // Invariant, not a balance pin: an equal draw puts each season near 25 %; 20–30 % is > 7 σ wide.
            foreach (int n in counts)
            {
                Assert.That(n, Is.InRange(Seeds / 5, Seeds * 3 / 10));
            }
        }

        [Test]
        public void Season_values_are_stable()
        {
            // Peers and saved runs compare the numeric value.
            Assert.That((int)Season.Spring, Is.EqualTo(0));
            Assert.That((int)Season.Summer, Is.EqualTo(1));
            Assert.That((int)Season.Autumn, Is.EqualTo(2));
            Assert.That((int)Season.Winter, Is.EqualTo(3));
            Assert.That(Seasons.Count, Is.EqualTo(4));
        }
    }
}
