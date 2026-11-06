using Cosmimera.Core.Contracts;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Player.Tests
{
    // Counter-examples from docs/reviews/P1-review-r4-sol.md (N4, N5, F7 remainder).
    public sealed class SphericalMotorReviewR4Tests
    {
        private const float Dt = 0.02f;
        private static readonly float3 Y = new float3(0f, 1f, 0f);
        private static readonly float3 Z = new float3(0f, 0f, 1f);
        private static readonly float3 X = new float3(1f, 0f, 0f);

        private static GroundContact Flat(float3 up) => new GroundContact { Hit = true, Normal = up, Distance = 0f };

        // ---- N4: finite but huge state speeds.

        [TestCase(2e19f)]
        [TestCase(1e20f)]
        [TestCase(3e38f)]
        [TestCase(SphericalMotor.MaxStateSpeed * 1.01f)]
        public void N4_planar_speed_beyond_the_state_domain_is_rejected(float speed)
        {
            MotorState s = SphericalMotor.Initial(Y, Z);
            s.PlanarVelocity = s.Velocity = speed * X;
            Assert.Throws<ContractException>(() =>
                SphericalMotor.Step(MotorSettings.Default, s, new MotorInput(), default, Y, Dt));
        }

        [Test]
        public void N4_state_speed_at_the_limit_steps_to_a_finite_state_inside_the_domain()
        {
            MotorSettings settings = MotorSettings.Default;
            settings.AirAcceleration = 1e-20f;
            float3 up = math.normalize(new float3(0.01f, 1f, 0f));
            MotorState s = SphericalMotor.Initial(Y, Z);
            s.PlanarVelocity = SphericalMotor.MaxStateSpeed * X;
            s.VerticalSpeed = -SphericalMotor.MaxStateSpeed;
            s.Velocity = s.PlanarVelocity + Y * s.VerticalSpeed;

            MotorState next = SphericalMotor.Step(settings, s, new MotorInput(), default, up, Dt);

            Assert.IsTrue(math.all(math.isfinite(next.Velocity)) && math.all(math.isfinite(next.PlanarVelocity)));
            Assert.LessOrEqual(math.length(next.PlanarVelocity), SphericalMotor.MaxStateSpeed * (1f + 1e-6f));
            Assert.AreEqual(-SphericalMotor.MaxStateSpeed, next.VerticalSpeed, "falling speed is clamped, not grown");
            Assert.DoesNotThrow(() => SphericalMotor.Step(settings, next, new MotorInput(), default, up, Dt),
                "Step's output must satisfy its own Pre");
        }

        // ---- N5: tangent correction after transport (fixed witness from the r4 sweep, seed 77, speed 1000).

        [Test]
        public void N5_transported_planar_velocity_stays_tangent_at_high_speed()
        {
            MotorSettings settings = MotorSettings.Default;
            settings.AirAcceleration = 1e-20f;
            float3 from = math.normalize(new float3(-0.77640027f, 0.15293439f, 0.611403f));
            float3 to = math.normalize(new float3(-0.47216615f, -0.8463181f, -0.24658595f));
            float3 heading = new float3(0.37746897f, 0.8897065f, 0.25678694f);
            MotorState s = SphericalMotor.Initial(from, heading);
            s.PlanarVelocity = 1000f * heading;

            MotorState next = SphericalMotor.Step(settings, s, new MotorInput(), default, to, Dt);

            // Regression precision for this witness (stricter than the relative MotorState Post at 1000 m/s).
            // Without the correction it measured 5.2e-4.
            Assert.LessOrEqual(math.abs(math.dot(next.PlanarVelocity, next.Up)), 1e-4f);
        }

        // ---- F7 remainder: invariants that Step / ViewOrbit accepted silently.

        [Test]
        public void F7_glue_reads_before_Configure_and_out_of_range_stick_are_rejected()
        {
            var go = new GameObject("F7Glue");
            try
            {
                var character = go.AddComponent<SphericalCharacter>();
                var rig = go.AddComponent<PlayerCameraRig>();
                Assert.Throws<ContractException>(() => _ = character.Facing);
                Assert.Throws<ContractException>(() => _ = rig.Heading);
                Assert.Throws<ContractException>(() => rig.AddLook(Vector2.zero, new Vector2(0f, -1000f), 1f));
                Assert.Throws<ContractException>(() => rig.AddLook(Vector2.zero, new Vector2(1.01f, 0f), 1f));
                Assert.DoesNotThrow(() => rig.AddLook(new Vector2(500f, -500f), new Vector2(-1f, 1f), 1f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void F7_grounded_prev_must_not_carry_vertical_speed_or_a_jump_start()
        {
            MotorState s = SphericalMotor.Step(MotorSettings.Default, SphericalMotor.Initial(Y, Z), new MotorInput(), Flat(Y), Y, Dt);
            Assert.IsTrue(s.Grounded);
            Assert.DoesNotThrow(() => SphericalMotor.Step(MotorSettings.Default, s, new MotorInput(), Flat(Y), Y, Dt));

            MotorState rising = s;
            rising.VerticalSpeed = 1f;
            Assert.Throws<ContractException>(() => SphericalMotor.Step(MotorSettings.Default, rising, new MotorInput(), Flat(Y), Y, Dt));
            MotorState jumped = s;
            jumped.JumpStarted = true;
            Assert.Throws<ContractException>(() => SphericalMotor.Step(MotorSettings.Default, jumped, new MotorInput(), Flat(Y), Y, Dt));
        }

        [Test]
        public void F7_orbit_rotations_reject_a_broken_heading_or_pitch()
        {
            ViewOrbit nanHeading = ViewOrbit.Create(Y, Z);
            nanHeading.Heading = new float3(float.NaN, float.NaN, float.NaN);
            Assert.Throws<ContractException>(() => nanHeading.Rotation());
            Assert.Throws<ContractException>(() => nanHeading.LevelRotation());

            ViewOrbit nanPitch = ViewOrbit.Create(Y, Z);
            nanPitch.PitchDegrees = float.NaN;
            Assert.Throws<ContractException>(() => nanPitch.Rotation());
            Assert.Throws<ContractException>(() => nanPitch.Advance(Y, 0f, 0f));

            ViewOrbit tooHigh = ViewOrbit.Create(Y, Z);
            tooHigh.PitchDegrees = ViewOrbit.MaxPitch + 1f;
            Assert.Throws<ContractException>(() => tooHigh.LevelRotation());

            ViewOrbit atLimit = ViewOrbit.Create(Y, Z);
            atLimit.PitchDegrees = ViewOrbit.MaxPitch;
            Assert.DoesNotThrow(() => atLimit.Rotation());
        }
    }
}
