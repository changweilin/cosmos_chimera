using Cosmimera.Core.Contracts;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Player.Tests
{
    // Counter-examples and surviving mutations from docs/reviews/P1-05-sol.md, grouped by finding.
    public sealed class SphericalMotorReviewTests
    {
        private const float Dt = 0.02f;
        private static readonly float3 Y = new float3(0f, 1f, 0f);
        private static readonly float3 Z = new float3(0f, 0f, 1f);
        private static readonly float3 X = new float3(1f, 0f, 0f);

        private static GroundContact Flat(float3 up, float distance = 0f) =>
            new GroundContact { Hit = true, Normal = up, Distance = distance };

        private static MotorInput Forward(float3 view) => new MotorInput { Move = new float2(0f, 1f), ViewForward = view };

        // ---- F1: jump buffer window and consumption.

        [Test]
        public void F1_press_is_pending_through_the_window_inclusive_and_not_after()
        {
            var b = new JumpBuffer();
            Assert.IsFalse(b.IsPending(0f));
            // Pressed at 0 so the boundary age is exactly WindowSeconds (1.15f − 1f is not 0.15f in float).
            b.Press(0f);
            Assert.IsTrue(b.IsPending(0.14f));
            Assert.IsTrue(b.IsPending(JumpBuffer.WindowSeconds), "window is inclusive");
            Assert.IsFalse(b.IsPending(0.16f));
            b.Press(2f);
            b.Consume();
            Assert.IsFalse(b.IsPending(2f));
            Assert.Throws<ContractException>(() => b.Press(float.NaN));
            Assert.Throws<ContractException>(() => b.IsPending(float.PositiveInfinity));
        }

        [Test]
        public void F1_JumpStarted_is_set_only_on_the_step_a_jump_begins()
        {
            MotorSettings settings = MotorSettings.Default;
            MotorState s = SphericalMotor.Initial(Y, Z);
            // Landing step with a buffered press: the review's case where "left the ground" never fired.
            s = SphericalMotor.Step(settings, s, new MotorInput { Jump = true, ViewForward = Z }, Flat(Y), Y, Dt);
            Assert.IsTrue(s.JumpStarted);
            Assert.IsFalse(s.Grounded);

            s = SphericalMotor.Step(settings, s, new MotorInput { Jump = true, ViewForward = Z }, default, Y, Dt);
            Assert.IsFalse(s.JumpStarted, "airborne press must not start or report a jump");

            MotorState walk = SphericalMotor.Step(settings, SphericalMotor.Initial(Y, Z), Forward(Z), Flat(Y), Y, Dt);
            MotorState offLedge = SphericalMotor.Step(settings, walk, Forward(Z), default, Y, Dt);
            Assert.IsFalse(offLedge.Grounded);
            Assert.IsFalse(offLedge.JumpStarted, "walking off a ledge is not a jump");
        }

        // ---- F2: the level rotation carries the local up regardless of pitch.

        [TestCase(0f)]
        [TestCase(80f)]
        [TestCase(-80f)]
        public void F2_level_rotation_up_is_local_up_at_any_pitch(float pitch)
        {
            ViewOrbit o = ViewOrbit.Create(Y, Z);
            o.Advance(Y, 0f, pitch);
            Assert.AreEqual(1f, math.dot(math.mul(o.LevelRotation(), Y), Y), 1e-6f);
            Assert.AreEqual(1f, math.dot(math.mul(o.LevelRotation(), Z), o.Heading), 1e-6f);
            Assert.Throws<ContractException>(() => default(ViewOrbit).LevelRotation());
        }

        // ---- F3: direction handling is scale invariant and tangent to 1e-6.

        [TestCase(1f)]
        [TestCase(1e20f)]
        [TestCase(1e-20f)]
        [TestCase(1e-38f)]
        [TestCase(3e38f)]
        public void F3_view_forward_length_does_not_change_the_direction(float scale)
        {
            MotorSettings settings = MotorSettings.Default;
            MotorState s = SphericalMotor.Initial(Y, Z);
            s = SphericalMotor.Step(settings, s, Forward(X * scale), Flat(Y), Y, Dt);
            Assert.AreEqual(1f, math.dot(math.normalize(s.PlanarVelocity), X), 1e-6f, $"scale {scale}");
        }

        [TestCase(1e20f)]
        [TestCase(1e-20f)]
        [TestCase(1e-38f)]
        public void F3_Initial_accepts_any_finite_length(float scale)
        {
            MotorState s = SphericalMotor.Initial(Y, Z * scale);
            Assert.AreEqual(1f, math.dot(s.Facing, Z), 1e-6f);
        }

        [Test]
        public void F3_near_degenerate_facing_is_tangent()
        {
            // Review repro B: one Gram-Schmidt pass left |dot| = 3.5e-4.
            float3 up = new float3(0.38094947f, 0.8153129f, -0.43605313f);
            MotorState s = SphericalMotor.Initial(up, new float3(38.196377f, 81.48646f, -43.600533f));
            Assert.LessOrEqual(math.abs(math.dot(s.Facing, s.Up)), 1e-6f);
            Assert.AreEqual(1f, math.length(s.Facing), 1e-6f);

            // Just past the 1e-3 rad cut-off, many orientations.
            var rng = new Cosmimera.Core.Random.Pcg32(5UL, 6UL);
            for (int k = 0; k < 2000; k++)
            {
                float3 u = math.normalize(new float3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()) * 2f - 1f + 1e-3f);
                float3 side = math.normalize(math.cross(u, math.abs(u.x) < 0.9f ? X : Y));
                float3 v = math.normalize(u + side * 1.2e-3f) * 37f;
                float3 t = SphericalMotor.TangentOr(v, u, float3.zero);
                Assert.AreNotEqual(float3.zero, t, $"#{k}");
                Assert.LessOrEqual(math.abs(math.dot(t, u)), 1e-6f, $"#{k}");
            }
        }

        // ---- F4: settings domain keeps every output finite and the speed Post achievable.

        [Test]
        public void F4_out_of_range_settings_are_invalid()
        {
            MotorSettings s = MotorSettings.Default;
            Assert.IsTrue(s.IsValid);
            s.Gravity = float.MaxValue;
            Assert.IsFalse(s.IsValid);
            s = MotorSettings.Default;
            s.WalkSpeed = 10f;
            s.SprintSpeed = 1f;
            Assert.IsFalse(s.IsValid, "walk faster than sprint breaks the speed Post");
            s = MotorSettings.Default;
            s.JumpHeight = MotorSettings.MaxJumpHeight * 1.01f;
            Assert.IsFalse(s.IsValid);
            s = MotorSettings.Default;
            s.SnapDistance = MotorSettings.MaxSnapDistance * 1.01f;
            Assert.IsFalse(s.IsValid);
            s = MotorSettings.Default;
            s.StickSpeed = float.NaN;
            Assert.IsFalse(s.IsValid);
            s = MotorSettings.Default;
            s.TurnRateDegrees = float.PositiveInfinity;
            Assert.IsFalse(s.IsValid);
        }

        [Test]
        public void F4_extreme_legal_settings_give_finite_bounded_motion()
        {
            MotorSettings settings = MotorSettings.Default;
            settings.WalkSpeed = settings.SprintSpeed = settings.StickSpeed = MotorSettings.MaxSpeed;
            settings.GroundAcceleration = settings.AirAcceleration = settings.Gravity = MotorSettings.MaxAcceleration;
            settings.JumpHeight = MotorSettings.MaxJumpHeight;
            settings.TurnRateDegrees = MotorSettings.MaxTurnRateDegrees;
            settings.SnapDistance = MotorSettings.MaxSnapDistance;
            Assert.IsTrue(settings.IsValid);
            MotorState s = SphericalMotor.Initial(Y, Z);
            for (int i = 0; i < 200; i++)
            {
                var input = new MotorInput { Move = new float2(0f, 1f), ViewForward = Z, Sprint = true, Jump = i % 7 == 0 };
                s = SphericalMotor.Step(settings, s, input, i % 3 == 0 ? default : Flat(Y), Y, 0.1f);
                Assert.IsTrue(math.all(math.isfinite(s.Velocity)), $"step {i}");
                Assert.LessOrEqual(math.length(s.PlanarVelocity), settings.SprintSpeed * (1f + 1e-6f), $"step {i}");
            }
        }

        // ---- F5: transport preserves planar speed while nothing else corrects it.

        [Test]
        public void F5_free_glide_keeps_speed_over_long_transport()
        {
            MotorSettings settings = MotorSettings.Default;
            settings.AirAcceleration = 1e-20f;
            MotorState s = SphericalMotor.Initial(Y, X);
            s.PlanarVelocity = 5f * X;
            for (int i = 1; i <= 100000; i++)
            {
                float a = i * 0.00025f;
                s.VerticalSpeed = 0f;
                s = SphericalMotor.Step(settings, s, new MotorInput(), default, new float3(math.sin(a), math.cos(a), 0f), Dt);
            }

            // Without the length restore this glide gained 0.28 %; with it, rounding alone leaves ≈ −0.03 %. The
            // invariant is "never speeds up, loses < 0.1 %", not exact equality.
            float speed = math.length(s.PlanarVelocity);
            Assert.LessOrEqual(speed, 5f * (1f + 1e-5f));
            Assert.GreaterOrEqual(speed, 5f * (1f - 1e-3f));
            Assert.LessOrEqual(math.abs(math.dot(s.PlanarVelocity, s.Up)), 1e-5f);
        }

        // ---- F6: analytic minimal-rotation transport (removing transport turns the result by 18.4°).

        private static readonly float3 To60 = new float3(math.sin(math.radians(60f)), math.cos(math.radians(60f)), 0f);
        private static readonly float3 Transported = new float3(0.35355338f, -0.61237246f, 0.70710677f);

        [Test]
        public void F6_motor_transport_matches_the_analytic_minimal_rotation()
        {
            MotorSettings settings = MotorSettings.Default;
            settings.AirAcceleration = 1e-20f;
            float3 heading = math.normalize(new float3(1f, 0f, 1f));
            MotorState s = SphericalMotor.Initial(Y, heading);
            s.PlanarVelocity = 5f * heading;
            s = SphericalMotor.Step(settings, s, new MotorInput(), default, To60, Dt);
            Assert.LessOrEqual(math.distance(s.Facing, Transported), 1e-5f);
            Assert.LessOrEqual(math.distance(math.normalize(s.PlanarVelocity), Transported), 1e-5f);
            Assert.AreEqual(5f, math.length(s.PlanarVelocity), 1e-4f);
            Assert.LessOrEqual(math.abs(math.dot(s.PlanarVelocity, s.Up)), 1e-6f);
        }

        [Test]
        public void F6_orbit_transport_matches_the_analytic_minimal_rotation()
        {
            ViewOrbit o = ViewOrbit.Create(Y, math.normalize(new float3(1f, 0f, 1f)));
            o.Advance(To60, 0f, 0f);
            Assert.LessOrEqual(math.distance(o.Heading, Transported), 1e-5f);
        }

        // ---- Surviving mutations M04–M06: snap boundary, rising-body rule, air control.

        [Test]
        public void M04_snap_distance_is_inclusive_and_sharp()
        {
            MotorSettings settings = MotorSettings.Default;
            MotorState s0 = SphericalMotor.Initial(Y, Z);
            Assert.IsTrue(SphericalMotor.Step(settings, s0, new MotorInput(), Flat(Y, settings.SnapDistance), Y, Dt).Grounded);
            Assert.IsFalse(SphericalMotor.Step(settings, s0, new MotorInput(), Flat(Y, settings.SnapDistance + 0.01f), Y, Dt).Grounded);
            Assert.IsFalse(SphericalMotor.Step(settings, s0, new MotorInput(), Flat(Y, 2f * settings.SnapDistance - 0.01f), Y, Dt).Grounded);
        }

        [Test]
        public void M05_rising_body_is_not_grounded_even_on_contact()
        {
            MotorState s = SphericalMotor.Initial(Y, Z);
            s.VerticalSpeed = 1f;
            Assert.IsFalse(SphericalMotor.Step(MotorSettings.Default, s, new MotorInput(), Flat(Y), Y, Dt).Grounded);
            s.VerticalSpeed = 0.5f;
            Assert.IsTrue(SphericalMotor.Step(MotorSettings.Default, s, new MotorInput(), Flat(Y), Y, Dt).Grounded);
        }

        [Test]
        public void M06_airborne_uses_air_acceleration()
        {
            MotorSettings settings = MotorSettings.Default;
            MotorState s = SphericalMotor.Step(settings, SphericalMotor.Initial(Y, Z), Forward(Z), default, Y, Dt);
            Assert.AreEqual(settings.AirAcceleration * Dt, math.length(s.PlanarVelocity), 1e-5f);
        }

        // ---- F7 / M15 / M16: previously unchecked preconditions.

        [Test]
        public void F7_invariant_and_argument_violations_are_rejected()
        {
            MotorSettings settings = MotorSettings.Default;
            MotorState ok = SphericalMotor.Initial(Y, Z);

            MotorState badFacing = ok;
            badFacing.Facing = new float3(float.NaN, float.NaN, float.NaN);
            Assert.Throws<ContractException>(() => SphericalMotor.Step(settings, badFacing, new MotorInput(), Flat(Y), Y, Dt));
            MotorState radialFacing = ok;
            radialFacing.Facing = Y;
            Assert.Throws<ContractException>(() => SphericalMotor.Step(settings, radialFacing, new MotorInput(), Flat(Y), Y, Dt));
            MotorState radialVelocity = ok;
            radialVelocity.PlanarVelocity = Y;
            Assert.Throws<ContractException>(() => SphericalMotor.Step(settings, radialVelocity, new MotorInput(), Flat(Y), Y, Dt));
            MotorState nanVertical = ok;
            nanVertical.VerticalSpeed = float.NaN;
            Assert.Throws<ContractException>(() => SphericalMotor.Step(settings, nanVertical, new MotorInput(), Flat(Y), Y, Dt));

            Assert.Throws<ContractException>(() => SphericalMotor.Step(settings, ok, new MotorInput(), Flat(Y, -0.01f), Y, Dt));
            Assert.Throws<ContractException>(() => SphericalMotor.Initial(Y, float3.zero));
            Assert.Throws<ContractException>(() => SphericalMotor.Initial(Y, Y * 1e20f));

            Assert.Throws<ContractException>(() => default(ViewOrbit).Rotation());
            ViewOrbit o = ViewOrbit.Create(Y, Z);
            Assert.Throws<ContractException>(() => o.Advance(Y, 0f, float.NaN));
            ViewOrbit badHeading = o;
            badHeading.Heading = Y;
            Assert.Throws<ContractException>(() => badHeading.Advance(Y, 0f, 0f));
        }
    }
}
