using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Player.Tests
{
    // Counter-examples from docs/reviews/P1-review-r5-sol.md (N6–N9) and r8 N15.
    public sealed class SphericalMotorReviewR5Tests
    {
        private const float Dt = 0.02f;
        private const float Max = SphericalMotor.MaxStateSpeed;
        private static readonly float3 Y = new float3(0f, 1f, 0f);
        private static readonly float3 Z = new float3(0f, 0f, 1f);

        private static MotorSettings Gliding()
        {
            MotorSettings settings = MotorSettings.Default;
            settings.AirAcceleration = 1e-20f;
            return settings;
        }

        private static void AssertInDomain(in MotorState s)
        {
            Assert.LessOrEqual(math.lengthsq(s.PlanarVelocity), Max * Max, "planar lengthsq");
            Assert.LessOrEqual(math.abs(s.VerticalSpeed), Max, "vertical");
            Assert.LessOrEqual(math.lengthsq(s.Velocity), 4f * Max * Max, "velocity lengthsq");
            Assert.LessOrEqual(math.abs(math.dot(s.PlanarVelocity, s.Up)),
                1e-4f * math.max(1f, math.length(s.PlanarVelocity)), "relative tangent");
        }

        [Test]
        public void N6_oblique_limit_speed_output_is_accepted_by_the_next_step()
        {
            var from = new float3(-0.0648168847f, 0.9838996f, -0.166554645f);
            var to = new float3(0.8500662f, 0.153920412f, 0.5036825f);
            MotorState s = SphericalMotor.Initial(from, Z);
            s.PlanarVelocity = s.Velocity = new float3(-9968.19f, -560.8229f, 566.2624f);

            MotorState next = SphericalMotor.Step(Gliding(), s, default, default, to, Dt);

            AssertInDomain(next);
            Assert.DoesNotThrow(() => SphericalMotor.Step(Gliding(), next, default, default, to, Dt));
        }

        [Test]
        public void N7_high_speed_tangent_residual_is_within_the_relative_bound()
        {
            var from = new float3(-0.144974574f, 0.5815f, 0.80052495f);
            var to = new float3(-0.3284381f, 0.9019396f, 0.280416429f);
            MotorState s = SphericalMotor.Initial(from, Z);
            s.PlanarVelocity = s.Velocity = new float3(930.007263f, -7974.81934f, 5961.32f);

            MotorState next = SphericalMotor.Step(Gliding(), s, default, default, to, Dt);

            AssertInDomain(next);
        }

        // The r5 sweep found 2,271 of 8,764 legal limit-speed inputs whose output the next Step rejected.
        [Test]
        public void N6_N7_limit_speed_sweep_is_closed_over_two_steps()
        {
            var rng = new Pcg32(77, 11);
            int checkedCases = 0;
            for (int i = 0; i < 20000; i++)
            {
                float3 from = RandomUnit(ref rng);
                float3 to = RandomUnit(ref rng);
                if (math.dot(from, to) <= 0.05f)
                {
                    continue;
                }

                float3 dir = RandomUnit(ref rng);
                float3 tangent = dir - from * math.dot(dir, from);
                if (math.lengthsq(tangent) < 1e-3f)
                {
                    continue;
                }

                float3 v = math.normalize(tangent) * Max;
                if (math.lengthsq(v) > Max * Max || math.abs(math.dot(v, from)) > 1e-4f * Max)
                {
                    continue;
                }

                MotorState s = SphericalMotor.Initial(from, tangent);
                s.PlanarVelocity = s.Velocity = v;
                MotorState next = SphericalMotor.Step(Gliding(), s, default, default, to, Dt);
                AssertInDomain(next);
                MotorState third = SphericalMotor.Step(Gliding(), next, default, default, to, Dt);
                AssertInDomain(third);
                checkedCases++;
            }

            Assert.Greater(checkedCases, 5000, "sweep must exercise enough legal inputs");
        }

        // ---- N9: each speed bound must reject on its own.

        [Test]
        public void N9_oblique_planar_speed_with_legal_components_is_rejected()
        {
            MotorState s = SphericalMotor.Initial(Y, Z);
            s.PlanarVelocity = s.Velocity = new float3(8000f, 0f, 8000f);
            Assert.Throws<ContractException>(() => SphericalMotor.Step(MotorSettings.Default, s, default, default, Y, Dt));
        }

        [TestCase(Max + 1f)]
        [TestCase(-Max - 1f)]
        public void N9_vertical_speed_beyond_the_domain_is_rejected(float vertical)
        {
            MotorState s = SphericalMotor.Initial(Y, Z);
            s.VerticalSpeed = vertical;
            s.Velocity = Y * vertical;
            Assert.Throws<ContractException>(() => SphericalMotor.Step(MotorSettings.Default, s, default, default, Y, Dt));
        }

        [Test]
        public void N9_vertical_speed_at_the_limit_is_accepted()
        {
            MotorState s = SphericalMotor.Initial(Y, Z);
            s.VerticalSpeed = -Max;
            s.Velocity = Y * -Max;
            Assert.DoesNotThrow(() => SphericalMotor.Step(MotorSettings.Default, s, default, default, Y, Dt));
        }

        [TestCase(2f * Max + 1f, 0f)]
        [TestCase(1.5f * Max, 1.5f * Max)]
        public void N9_velocity_beyond_the_domain_is_rejected_with_other_speeds_zero(float x, float z)
        {
            MotorState s = SphericalMotor.Initial(Y, Z);
            s.Velocity = new float3(x, 0f, z);
            Assert.Throws<ContractException>(() => SphericalMotor.Step(MotorSettings.Default, s, default, default, Y, Dt));
        }

        // ---- sol P1 r8 N15: the |Move| tolerance must not push speed past SprintSpeed.

        [Test]
        public void N15_move_within_the_tolerance_does_not_exceed_sprint_speed()
        {
            MotorSettings settings = MotorSettings.Default;
            settings.WalkSpeed = settings.SprintSpeed = 100f;
            settings.GroundAcceleration = 1000f;
            var input = new MotorInput { Move = new float2(0f, 1.0009f), ViewForward = Z, Sprint = true };
            var flat = new GroundContact { Hit = true, Normal = Y, Distance = 0f };

            MotorState s = SphericalMotor.Initial(Y, Z);
            for (int i = 0; i < 3; i++)
            {
                s = SphericalMotor.Step(settings, s, input, flat, Y, 0.1f);
                Assert.LessOrEqual(math.length(s.PlanarVelocity), settings.SprintSpeed * (1f + 1e-6f));
            }
        }

        // ---- N8: legal finite look input must not poison the pending look.

        [Test]
        public void N8_add_look_keeps_the_pending_look_finite_and_rejects_out_of_domain_input()
        {
            var go = new GameObject("N8Rig");
            try
            {
                var rig = go.AddComponent<PlayerCameraRig>();
                Assert.Throws<ContractException>(() => rig.AddLook(Vector2.zero, new Vector2(1f, 0f), float.MaxValue));
                Assert.Throws<ContractException>(() => rig.AddLook(Vector2.zero, new Vector2(1f, 0f), PlayerCameraRig.MaxLookDeltaTime * 1.01f));
                Assert.Throws<ContractException>(() => rig.AddLook(new Vector2(float.MaxValue, 0f), Vector2.zero, 0f));
                for (int i = 0; i < 1000; i++)
                {
                    Assert.DoesNotThrow(() => rig.AddLook(
                        new Vector2(PlayerCameraRig.MaxPointerDelta, -PlayerCameraRig.MaxPointerDelta), new Vector2(1f, 1f),
                        PlayerCameraRig.MaxLookDeltaTime));
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static float3 RandomUnit(ref Pcg32 rng)
        {
            float3 v = new float3(rng.NextFloat() * 2f - 1f, rng.NextFloat() * 2f - 1f, rng.NextFloat() * 2f - 1f);
            float lenSq = math.lengthsq(v);
            return lenSq < 1e-4f ? new float3(0f, 1f, 0f) : v / math.sqrt(lenSq);
        }
    }
}
