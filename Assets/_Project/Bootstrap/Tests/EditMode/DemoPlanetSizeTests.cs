using System;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: planet-world.md#星球尺度 (Q-D1, 2026-11-11). SOL Q-D1-IMPL-02 M5: no test pinned the shipped tier mapping,
    // so Moon silently falling to the 320 m combat minimum passed every suite.
    public sealed class DemoPlanetSizeTests
    {
        [TestCase(PlanetSize.Asteroid, 320f)]
        [TestCase(PlanetSize.Moon, 640f)]
        [TestCase(PlanetSize.Planet, 960f)]
        [TestCase(PlanetSize.Supergiant, 1280f)]
        public void Each_size_tier_maps_to_its_radius(PlanetSize size, float radius)
        {
            Assert.That(DemoGame.RadiusFor(size), Is.EqualTo(radius));
        }

        [Test]
        public void An_undefined_size_fails_loudly()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DemoGame.RadiusFor((PlanetSize)99));
        }
    }
}
