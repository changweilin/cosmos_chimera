using Cosmimera.Core.Contracts;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class WindFieldTests
    {
        [Test]
        public void Heading_is_reproducible_and_in_range()
        {
            for (long t = 0; t < 3_600_000; t += 7_919)
            {
                int a = WindField.HeadingTenths(42UL, 3, t);
                Assert.That(a, Is.InRange(0, 3599));
                Assert.That(WindField.HeadingTenths(42UL, 3, t), Is.EqualTo(a));
            }
        }

        [Test]
        public void Regions_and_seeds_get_their_own_heading()
        {
            int differ = 0;
            for (int region = 0; region < 16; region++)
            {
                if (WindField.HeadingTenths(42UL, region, 0) != WindField.HeadingTenths(42UL, region + 1, 0)) differ++;
                if (WindField.HeadingTenths(42UL, region, 0) != WindField.HeadingTenths(43UL, region, 0)) differ++;
            }

            Assert.That(differ, Is.GreaterThan(24));
        }

        [Test]
        public void Heading_turns_slowly_over_time()
        {
            int prev = WindField.HeadingTenths(9UL, 1, 0);
            int moved = 0;
            for (long t = 1_000; t < 1_200_000; t += 1_000)
            {
                int now = WindField.HeadingTenths(9UL, 1, t);
                int step = math.abs(now - prev);
                step = math.min(step, 3600 - step);
                // ±80° over 300 s and ±25° over 90 s cannot jump more than a few degrees per second.
                Assert.That(step, Is.LessThan(60), $"t={t}");
                if (step > 0) moved++;
                prev = now;
            }

            Assert.That(moved, Is.GreaterThan(100));
        }

        [Test]
        public void Gust_is_bounded_by_the_wind_and_zero_in_calm()
        {
            bool any = false;
            for (long t = 0; t < 600_000; t += 250)
            {
                Assert.That(WindField.GustPermille(5UL, 2, t, 0), Is.Zero);
                int g = WindField.GustPermille(5UL, 2, t, 800);
                Assert.That(g, Is.InRange(0, 800));
                any |= g > 200;
            }

            Assert.That(any, Is.True, "a strong wind must gust");
        }

        [Test]
        public void Tangent_is_unit_and_on_the_surface()
        {
            float3 up = math.normalize(new float3(0.3f, 0.5f, -0.8f));
            for (int h = -3600; h < 7200; h += 337)
            {
                float3 w = WindField.Tangent(up, h);
                Assert.That(math.length(w), Is.EqualTo(1f).Within(1e-4f));
                Assert.That(math.dot(w, up), Is.EqualTo(0f).Within(1e-4f));
            }
        }

        [Test]
        public void Bad_arguments_are_rejected()
        {
            Assert.Throws<ContractException>(() => WindField.HeadingTenths(1UL, -1, 0));
            Assert.Throws<ContractException>(() => WindField.HeadingTenths(1UL, 0, -1));
            Assert.Throws<ContractException>(() => WindField.GustPermille(1UL, 0, 0, 1001));
            Assert.Throws<ContractException>(() => WindField.GustPermille(1UL, 0, 0, -1));
        }
    }
}
