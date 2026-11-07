using Cosmimera.Core.Contracts;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Player.Tests
{
    // Endpoint sweep from docs/reviews/P1-review-r16-sol.md (N32, N34): each closed bound of the Initial / Step Pre and
    // Post gets an accepting case at the endpoint, so boundary flips and extra rejections both fail.
    public sealed class SphericalMotorReviewR16Tests
    {
        private const float Dt = 0.02f;
        private const float Max = SphericalMotor.MaxStateSpeed;
        private static readonly float3 Y = new float3(0f, 1f, 0f);
        private static readonly float3 X = new float3(1f, 0f, 0f);
        private static readonly float3 Z = new float3(0f, 0f, 1f);

        // Largest float below 1.001f: its length error 9.9993e-4 is inside the documented ±1e-3 (sol r16 N32).
        private static readonly float JustBelow1001 = math.asfloat(math.asuint(1.001f) - 1u);

        private static MotorState Step(MotorState prev, MotorInput input = default, float dt = Dt) =>
            SphericalMotor.Step(MotorSettings.Default, prev, input, default, prev.Up, dt);

        [Test]
        public void N32_up_length_just_inside_the_upper_slack_is_accepted()
        {
            Assert.DoesNotThrow(() => SphericalMotor.Initial(Y * JustBelow1001, Z));
            MotorState prev = SphericalMotor.Initial(Y, Z);
            Assert.DoesNotThrow(() => SphericalMotor.Step(MotorSettings.Default, prev, default, default, Y * JustBelow1001, Dt));
        }

        [Test]
        public void N34_up_length_at_the_lower_slack_is_accepted()
        {
            Assert.DoesNotThrow(() => SphericalMotor.Initial(Y * 0.999f, Z));
            MotorState prev = SphericalMotor.Initial(Y, Z);
            Assert.DoesNotThrow(() => SphericalMotor.Step(MotorSettings.Default, prev, default, default, Y * 0.999f, Dt));
        }

        [TestCase(1.0011f)]
        [TestCase(0.9989f)]
        public void N32_up_length_outside_the_slack_is_rejected(float length)
        {
            Assert.Throws<ContractException>(() => SphericalMotor.Initial(Y * length, Z));
            MotorState prev = SphericalMotor.Initial(Y, Z);
            Assert.Throws<ContractException>(() => SphericalMotor.Step(MotorSettings.Default, prev, default, default, Y * length, Dt));
        }

        [Test]
        public void N32_move_just_inside_the_slack_is_accepted_and_beyond_is_rejected()
        {
            MotorState prev = SphericalMotor.Initial(Y, Z);
            Assert.DoesNotThrow(() => Step(prev, new MotorInput { Move = new float2(JustBelow1001, 0f), ViewForward = Z }));
            // lengthsq == 1.002f exactly sat on the old cut-off (sol r16 N34).
            Assert.DoesNotThrow(() => Step(prev, new MotorInput { Move = new float2(1f, math.sqrt(0.002f)), ViewForward = Z }));
            Assert.Throws<ContractException>(() => Step(prev, new MotorInput { Move = new float2(1.0011f, 0f), ViewForward = Z }));
        }

        [Test]
        public void N34_up_exactly_90_degrees_from_prev_up_is_rejected()
        {
            MotorState prev = SphericalMotor.Initial(Y, Z);
            Assert.Throws<ContractException>(() => SphericalMotor.Step(MotorSettings.Default, prev, default, default, X, Dt));
        }

        // The Initial Pre excludes sin θ ≤ 1e-3 as tested in float; this facing (θ = 1e-3 + 9e-11 rad) lands exactly on
        // lengthsq == 1e-6 and is rejected by that documented test (sol r16 N32), while as ViewForward it is legal input.
        [Test]
        public void N32_N34_facing_on_the_float_degeneracy_cutoff()
        {
            var nearUp = new float3(2.4e-7f, 1f, 0.0010000004f);
            Assert.Throws<ContractException>(() => SphericalMotor.Initial(Y, nearUp));
            MotorState prev = SphericalMotor.Initial(Y, Z);
            MotorState next = default;
            Assert.DoesNotThrow(() => next = Step(prev, new MotorInput { Move = new float2(0f, 1f), ViewForward = nearUp }));
            Assert.AreEqual(1f, math.length(next.Facing), 1e-5f);
        }

        [Test]
        public void N34_initial_velocity_is_exactly_zero()
        {
            MotorState s = SphericalMotor.Initial(Y, Z);
            Assert.AreEqual(float3.zero, s.Velocity);
            Assert.AreEqual(float3.zero, s.PlanarVelocity);
            Assert.AreEqual(0f, s.VerticalSpeed);
            Assert.IsFalse(s.Grounded);
            Assert.IsFalse(s.JumpStarted);
        }

        [TestCase(-1f, 0f, 0f, 0f, 1f, 0f)]
        [TestCase(0f, 1f, 0f, 1f, 0f, 0f)]
        [TestCase(0f, -1f, 0f, 1f, 0f, 0f)]
        [TestCase(0f, 0f, 1f, 0f, 1f, 0f)]
        [TestCase(0f, 0f, -1f, 0f, 1f, 0f)]
        public void N34_planar_velocity_component_at_max_state_speed_is_accepted(float vx, float vy, float vz, float ux, float uy, float uz)
        {
            var up = new float3(ux, uy, uz);
            MotorState prev = SphericalMotor.Initial(up, math.cross(up, new float3(vx, vy, vz)));
            prev.PlanarVelocity = prev.Velocity = new float3(vx, vy, vz) * Max;

            Assert.DoesNotThrow(() => Step(prev));
        }

        // ±2e4 on one axis is also |Velocity| == 2·MaxStateSpeed, the lengthsq == 4e8 endpoint.
        [TestCase(1f, 0f, 0f)]
        [TestCase(-1f, 0f, 0f)]
        [TestCase(0f, 1f, 0f)]
        [TestCase(0f, -1f, 0f)]
        [TestCase(0f, 0f, 1f)]
        [TestCase(0f, 0f, -1f)]
        public void N34_velocity_component_at_twice_max_state_speed_is_accepted(float x, float y, float z)
        {
            MotorState prev = SphericalMotor.Initial(Y, Z);
            prev.Velocity = new float3(x, y, z) * 2f * Max;

            Assert.DoesNotThrow(() => Step(prev));
        }

        [Test]
        public void N34_vertical_speed_at_plus_max_is_accepted_and_reproduced()
        {
            MotorState prev = SphericalMotor.Initial(Y, Z);
            prev.VerticalSpeed = Max;
            prev.Velocity = Y * Max;

            // A tiny legal dt keeps gravity below one ulp of 1e4, so the output sits on the same endpoint.
            MotorState next = Step(prev, default, 1e-30f);

            Assert.AreEqual(Max, next.VerticalSpeed);
            Assert.DoesNotThrow(() => Step(next));
        }

        [TestCase("WalkSpeed")]
        [TestCase("GroundAcceleration")]
        [TestCase("AirAcceleration")]
        [TestCase("Gravity")]
        [TestCase("JumpHeight")]
        [TestCase("TurnRateDegrees")]
        [TestCase("MaxSlopeDegrees")]
        public void N34_zero_for_a_strictly_positive_setting_is_invalid(string field)
        {
            Assert.IsFalse(WithZero(field).IsValid);
        }

        [TestCase("SnapDistance")]
        [TestCase("StickSpeed")]
        public void N34_zero_for_a_non_negative_setting_is_valid(string field)
        {
            MotorSettings settings = WithZero(field);
            Assert.IsTrue(settings.IsValid);
            Assert.DoesNotThrow(() => SphericalMotor.Step(settings, SphericalMotor.Initial(Y, Z), default, default, Y, Dt));
        }

        private static MotorSettings WithZero(string field)
        {
            MotorSettings s = MotorSettings.Default;
            switch (field)
            {
                case "WalkSpeed": s.WalkSpeed = 0f; break;
                case "GroundAcceleration": s.GroundAcceleration = 0f; break;
                case "AirAcceleration": s.AirAcceleration = 0f; break;
                case "Gravity": s.Gravity = 0f; break;
                case "JumpHeight": s.JumpHeight = 0f; break;
                case "TurnRateDegrees": s.TurnRateDegrees = 0f; break;
                case "MaxSlopeDegrees": s.MaxSlopeDegrees = 0f; break;
                case "SnapDistance": s.SnapDistance = 0f; break;
                case "StickSpeed": s.StickSpeed = 0f; break;
                default: Assert.Fail(field); break;
            }

            return s;
        }
    }
}
