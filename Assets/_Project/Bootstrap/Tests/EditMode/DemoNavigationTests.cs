using Cosmimera.Bootstrap.Demo;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class DemoNavigationTests
    {
        [TestCase(-179f)]
        [TestCase(-90f)]
        [TestCase(-45f)]
        [TestCase(0f)]
        [TestCase(45f)]
        [TestCase(90f)]
        [TestCase(179f)]
        public void View_relative_bearing_survives_poles_seams_pitch_and_horizon(float expected)
        {
            foreach (float3 up in new[] { new float3(0, 1, 0), new float3(0, -1, 0), math.normalize(new float3(1, 1, 1)) })
            {
                float3 forward = math.normalize(math.cross(up, new float3(1, 0, 0)));
                float3 right = math.cross(up, forward);
                float angle = math.radians(expected);
                float3 toward = forward * math.cos(angle) + right * math.sin(angle);
                // 120° away is beyond the horizon; a world-space screen projection would point underground.
                float3 target = up * math.cos(math.radians(120f)) + toward * math.sin(math.radians(120f));
                Assert.That(DemoNavigation.TryBearing(up, target, forward + up * 3f, out float actual), Is.True);
                Assert.That(actual, Is.EqualTo(expected).Within(0.001f));
            }
        }

        [Test]
        public void Undefined_bearings_never_emit_an_arbitrary_turn()
        {
            float3 up = new float3(0, 1, 0);
            float3 forward = new float3(0, 0, 1);
            foreach (float3 target in new[] { up, -up, float3.zero, new float3(float.NaN, 0, 0) })
            {
                Assert.That(DemoNavigation.TryBearing(up, target, forward, out float angle), Is.False);
                Assert.That(angle, Is.Zero);
            }

            Assert.That(DemoNavigation.TryBearing(up, forward, up, out _), Is.False);
            Assert.That(DemoNavigation.TryBearing(float3.zero, forward, forward, out _), Is.False);
        }
    }
}
