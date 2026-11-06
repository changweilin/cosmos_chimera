using Cosmimera.Core.Contracts;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Player.Tests
{
    public sealed class ViewOrbitTests
    {
        [Test]
        public void Yaw_positive_turns_right()
        {
            float3 up = new float3(0f, 1f, 0f);
            var orbit = ViewOrbit.Create(up, new float3(0f, 0f, 1f));
            orbit.Advance(up, 90f, 0f);
            // Unity screen-right for forward +Z, up +Y is +X.
            float3 expectedRight = new float3(1f, 0f, 0f);
            Assert.AreEqual(1f, math.dot(orbit.Heading, expectedRight), 1e-5f);
        }

        [Test]
        public void Positive_pitch_looks_up_and_is_clamped()
        {
            float3 up = new float3(0f, 0f, 1f);
            var orbit = ViewOrbit.Create(up, new float3(1f, 0f, 0f));
            orbit.Advance(up, 0f, 30f);
            float3 forward = math.mul(orbit.Rotation(), new float3(0f, 0f, 1f));
            Assert.AreEqual(math.sin(math.radians(30f)), math.dot(forward, up), 1e-5f);

            orbit.Advance(up, 0f, 500f);
            Assert.AreEqual(ViewOrbit.MaxPitch, orbit.PitchDegrees);
            orbit.Advance(up, 0f, -5000f);
            Assert.AreEqual(ViewOrbit.MinPitch, orbit.PitchDegrees);
        }

        [Test]
        public void Rotation_up_stays_on_the_local_up_side()
        {
            float3 up = math.normalize(new float3(0.3f, -0.8f, 0.5f));
            var orbit = ViewOrbit.Create(up, math.cross(up, new float3(1f, 0f, 0f)));
            orbit.Advance(up, 37f, -20f);
            float3 cameraUp = math.mul(orbit.Rotation(), new float3(0f, 1f, 0f));
            Assert.Greater(math.dot(cameraUp, up), math.cos(math.radians(ViewOrbit.MaxPitch)) - 1e-4f);
        }

        // Acceptance P1-05: circling the pole at 1 m radius spins East/North a full turn, but the carried heading must
        // only follow the parallel transport (holonomy ≈ enclosed solid angle, tiny at this scale).
        [Test]
        public void Circling_the_pole_tightly_does_not_spin_the_heading()
        {
            const float R = 640f;
            float tilt = 1f / R;
            float3 Up(float t) => math.normalize(new float3(math.sin(tilt) * math.cos(t), math.cos(tilt), math.sin(tilt) * math.sin(t)));
            var orbit = ViewOrbit.Create(Up(0f), new float3(1f, 0f, 0f));
            float3 start = orbit.Heading;
            const int steps = 720;
            for (int i = 1; i <= steps; i++)
            {
                float3 before = orbit.Heading;
                orbit.Advance(Up(i * 2f * math.PI / steps), 0f, 0f);
                Assert.Greater(math.dot(before, orbit.Heading), math.cos(math.radians(0.5f)), $"heading jumped at step {i}");
            }

            Assert.Greater(math.dot(start, orbit.Heading), math.cos(math.radians(0.1f)));
        }

        [Test]
        public void Advance_rejects_contract_violations()
        {
            float3 up = new float3(0f, 1f, 0f);
            var orbit = ViewOrbit.Create(up, new float3(1f, 0f, 0f));
            Assert.Throws<ContractException>(() => orbit.Advance(up * 2f, 0f, 0f));
            Assert.Throws<ContractException>(() => orbit.Advance(-up, 0f, 0f));
            Assert.Throws<ContractException>(() => orbit.Advance(up, float.NaN, 0f));
            var uninitialised = default(ViewOrbit);
            Assert.Throws<ContractException>(() => uninitialised.Advance(up, 0f, 0f));
            Assert.Throws<ContractException>(() => ViewOrbit.Create(up, up));
        }
    }
}
