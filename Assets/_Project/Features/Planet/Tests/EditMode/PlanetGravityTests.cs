using Cosmimera.Core.Contracts;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Planet.Tests
{
    // REQ: ADR-0009 §2 — loose objects fall toward the planet centre, never along a world axis.
    public sealed class PlanetGravityTests
    {
        private static readonly PlanetConfig Planet = new PlanetConfig(320f, new float3(5f, -3f, 2f));

        [TestCase(0f, 1f, 0f)]
        [TestCase(0f, -1f, 0f)]
        [TestCase(1f, 0f, 0f)]
        [TestCase(0.3f, -0.4f, 0.866f)]
        public void Acceleration_points_at_the_centre_with_magnitude_g(float x, float y, float z)
        {
            float3 dir = math.normalize(new float3(x, y, z));
            float3 at = Planet.ToWorld(dir, 12f);
            float3 a = PlanetGravity.AccelerationAt(Planet, at, 9.81f);
            Assert.That(math.length(a), Is.EqualTo(9.81f).Within(1e-3f));
            Assert.That(math.dot(math.normalize(a), -dir), Is.GreaterThan(1f - 1e-5f));
        }

        [Test]
        public void Zero_g_gives_no_acceleration()
        {
            Assert.That(math.length(PlanetGravity.AccelerationAt(Planet, Planet.ToWorld(new float3(0, 1, 0), 0f), 0f)),
                Is.EqualTo(0f));
        }

        [Test]
        public void Contract_violations_throw()
        {
            float3 ok = Planet.ToWorld(new float3(0, 1, 0), 0f);
            Assert.Throws<ContractException>(() => PlanetGravity.AccelerationAt(Planet, new float3(float.NaN), 9.81f));
            Assert.Throws<ContractException>(() => PlanetGravity.AccelerationAt(Planet, Planet.Center, 9.81f));
            Assert.Throws<ContractException>(() => PlanetGravity.AccelerationAt(Planet, ok, -1f));
            Assert.Throws<ContractException>(() => PlanetGravity.AccelerationAt(Planet, ok, float.PositiveInfinity));
        }
    }
}
