using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: planet-tech.md#地平線; SOL Q-D1-R3: a fixed 40 m fog start lay past R=320's 33 m horizon.
    public sealed class AtmosphereFogTests
    {
        private const float Eye = 1.7f;

        private static PlanetConfig Planet(float radius) => new PlanetConfig(radius, float3.zero);

        [TestCase(640f)]
        [TestCase(960f)]
        [TestCase(1280f)]
        public void Planets_from_640_keep_the_tuned_40_m(float radius)
        {
            Assert.That(AtmosphereDriver.FogStartDistance(Planet(radius), Eye), Is.EqualTo(AtmosphereDriver.FogStartM));
        }

        [Test]
        public void An_asteroid_starts_fog_inside_its_horizon()
        {
            PlanetConfig asteroid = Planet(320f);
            float horizon = asteroid.HorizonDistance(Eye);
            float start = AtmosphereDriver.FogStartDistance(asteroid, Eye);

            Assert.That(horizon, Is.LessThan(AtmosphereDriver.FogStartM), "the case the old fixed 40 m got wrong");
            Assert.That(start, Is.EqualTo(AtmosphereDriver.FogStartHorizonFraction * horizon).Within(1e-4f));
            Assert.That(start, Is.LessThan(horizon));
        }

        [Test]
        public void Fog_start_never_shrinks_as_the_planet_grows()
        {
            float previous = 0f;
            for (float radius = 1f; radius <= 8000f; radius *= 1.25f)
            {
                float start = AtmosphereDriver.FogStartDistance(Planet(radius), Eye);
                Assert.That(start, Is.GreaterThanOrEqualTo(previous), $"R={radius}");
                Assert.That(start, Is.LessThanOrEqualTo(AtmosphereDriver.FogStartM), $"R={radius}");
                previous = start;
            }
        }

        [Test]
        public void A_negative_eye_height_is_rejected()
        {
            Assert.Throws<ContractException>(() => AtmosphereDriver.FogStartDistance(Planet(320f), -1f));
        }
    }
}
