using Cosmimera.Core.Random;
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

        // The Initial Pre excludes sin θ ≤ 1e-3 as tested in float; a facing landing exactly on
        // lengthsq == 1e-6 (θ ≈ 1e-3 rad) is rejected by that documented test (sol r16 N32), while as ViewForward it is legal input.
        [Test]
        public void N32_N34_facing_on_the_float_degeneracy_cutoff()
        {
            float3 nearUp = NearUp;
            Assert.Throws<ContractException>(() => SphericalMotor.Initial(Y, nearUp));
            MotorState prev = SphericalMotor.Initial(Y, Z);
            MotorState next = default;
            Assert.DoesNotThrow(() => next = Step(prev, new MotorInput { Move = new float2(0f, 1f), ViewForward = nearUp }));
            Assert.AreEqual(1f, math.length(next.Facing), 1e-5f);
        }

        // sol P1 r17 N35: inputs whose float lengthsq sits exactly on (1 ∓ 1e-3)² must be accepted.
        private static readonly float3 LowerUp = new float3(0.000244140625f, math.asfloat(math.asuint(0.999f) - 1u), 0f);
        private static readonly float3 UpperUp = new float3(0f, 1.001f, 0f);

        [Test]
        public void N35_up_on_the_exact_squared_slack_bounds_is_accepted()
        {
            Assert.AreEqual(0.998001f, math.lengthsq(LowerUp), "witness sits on the lower bound");
            Assert.AreEqual(1.002001f, math.lengthsq(UpperUp), "witness sits on the upper bound");
            MotorState prev = SphericalMotor.Initial(Y, Z);
            foreach (float3 up in new[] { LowerUp, UpperUp })
            {
                Assert.DoesNotThrow(() => SphericalMotor.Initial(up, Z));
                Assert.DoesNotThrow(() => SphericalMotor.Step(MotorSettings.Default, prev, default, default, up, Dt));
            }
        }

        [Test]
        public void N35_move_on_the_exact_squared_upper_bound_is_accepted()
        {
            var move = new float2(1f, math.sqrt(0.002001f));
            Assert.AreEqual(1.002001f, math.lengthsq(move), "witness sits on the upper bound");
            Assert.DoesNotThrow(() => Step(SphericalMotor.Initial(Y, Z), new MotorInput { Move = move, ViewForward = Z }));
        }

        // sol P1 r17 N36: both signs of the cutoff; the ViewForward case must fall back to the existing facing (+X here,
        // unlike the projected direction ≈ +Z), not merely stay unit length.
        // Sol's (2.4e-7, ±1, 0.0010000004) hits lengthsq == 1e-6 exactly on CLR but not on Unity Mono, so the witness is
        // searched in the running runtime with the same steps as SphericalMotor.TangentOr (up = +Y, |v| largest = 1).
        private static readonly float3 NearUp = CutoffWitness(1f);
        private static readonly float3 NearDown = CutoffWitness(-1f);

        private static float3 CutoffWitness(float ySign)
        {
            uint start = math.asuint(0.001f) - 64u;
            for (uint zBits = start; zBits < start + 128u; zBits++)
            {
                for (uint xStep = 0u; xStep < 64u; xStep++)
                {
                    var v = new float3(xStep * 1e-8f, ySign, math.asfloat(zBits));
                    float3 d = math.normalize(v / math.cmax(math.abs(v)));
                    float3 t = d - Y * math.dot(d, Y);
                    if (math.lengthsq(t) == 1e-6f)
                    {
                        return v;
                    }
                }
            }

            Assert.Fail("no float witness on the 1e-6 cutoff in this runtime");
            return default;
        }

        [Test]
        public void N36_initial_rejects_the_cutoff_near_minus_up()
        {
            Assert.Throws<ContractException>(() => SphericalMotor.Initial(Y, NearDown));
        }

        [Test]
        public void N36_view_forward_on_the_cutoff_falls_back_to_facing()
        {
            foreach (float3 view in new[] { NearUp, NearDown })
            {
                MotorState prev = SphericalMotor.Initial(Y, X);
                MotorState next = default;
                Assert.DoesNotThrow(() => next = Step(prev, new MotorInput { Move = new float2(0f, 1f), ViewForward = view }));
                Assert.AreEqual(1f, math.dot(next.Facing, X), 1e-6f, "facing kept");
                Assert.Greater(next.PlanarVelocity.x, 0f, "moves along the kept facing");
                Assert.AreEqual(0f, next.PlanarVelocity.z, 1e-6f, "not along the projected view direction");
            }
        }

        // sol P1 r17 N37 (AD12): legal unit normals at a slope just under 90° with the velocity almost along the normal,
        // where the first projection is a rounding residue (lengthsq 4.4e-15 on .NET Framework CLR) that is still
        // non-zero; a raised cutoff would drop the along-slope speed to zero. Sample 0 is Sol's exact witness. On Unity
        // Mono it is not grounded (normalize(up) rounds differently: dot 1.72e-7 < cos 1.95e-7), and an instrumented
        // 3e6-sample search found no grounded residue below 1e-14 there, so AD12 is only distinguishable on CLR; the
        // ulp neighbourhood still checks the Post across the slope limit on every runtime.
        [Test]
        public void N37_tiny_nonzero_first_projection_still_restores_the_along_slope_speed()
        {
            uint3 upBits = new uint3(1062333398u, 3176025790u, 3205624947u);
            uint3 tangentBits = new uint3(1057905043u, 3190418800u, 1062239557u);
            uint3 normalBits = new uint3(1057905045u, 3190418800u, 1062239555u);
            MotorSettings settings = MotorSettings.Default;
            settings.MaxSlopeDegrees = math.asfloat(math.asuint(90f) - 1u);
            settings.GroundAcceleration = settings.AirAcceleration = 1e-20f;
            settings.StickSpeed = 0f;
            var rng = new Pcg32(37, 17);
            int grounded = 0;
            for (int i = 0; i < 20000; i++)
            {
                int ulps = i == 0 ? 0 : 64;
                uint3 upJitter = Jitter(ref rng, ulps);
                uint3 normalJitter = Jitter(ref rng, ulps);
                uint3 tangentJitter = Jitter(ref rng, ulps);
                // Sol's witness passes up and the normal exactly as given; only jittered samples are re-normalised.
                float3 up = i == 0 ? math.asfloat(upBits) : math.normalize(math.asfloat(upBits + upJitter));
                float3 normal = i == 0 ? math.asfloat(normalBits) : math.normalize(math.asfloat(normalBits + normalJitter));
                float3 tangent = math.asfloat(tangentBits + tangentJitter);
                MotorState prev;
                try
                {
                    prev = SphericalMotor.Initial(up, tangent);
                }
                catch (ContractException)
                {
                    continue;
                }

                prev.PlanarVelocity = prev.Velocity = (i == 0 ? tangent : prev.Facing) * 9000f;
                MotorState next = SphericalMotor.Step(settings, prev, default,
                    new GroundContact { Hit = true, Normal = normal, Distance = 0f }, up, Dt);
                if (!next.Grounded)
                {
                    continue;
                }

                grounded++;
                Assert.AreEqual(1f, math.length(next.Velocity) / math.length(next.PlanarVelocity), 1e-4f, $"sample {i}");
            }

            Assert.Greater(grounded, 1000, "the neighbourhood straddles the slope limit");
        }

        private static uint3 Jitter(ref Pcg32 rng, int ulps) => ulps == 0 ? uint3.zero :
            new uint3((uint)rng.NextInt(-ulps, ulps + 1), (uint)rng.NextInt(-ulps, ulps + 1), (uint)rng.NextInt(-ulps, ulps + 1));

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
