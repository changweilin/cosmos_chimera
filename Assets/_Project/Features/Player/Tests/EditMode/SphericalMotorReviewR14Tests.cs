using Cosmimera.Core.Random;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Player.Tests
{
    // Counter-examples from docs/reviews/P1-review-r14-sol.md (N28): the grounded along-slope speed restore was skipped
    // whenever the projection was at most 1e-6 m/s long, i.e. for micro speeds and for legal near-vertical slopes.
    public sealed class SphericalMotorReviewR14Tests
    {
        private static readonly float3 Up = new float3(0f, 1f, 0f);

        // math.length underflows below ~1e-19; scaling by the largest component keeps tiny witnesses measurable.
        private static float Len(float3 v)
        {
            float m = math.cmax(math.abs(v));
            return m > 0f ? m * math.length(v / m) : 0f;
        }

        private static void AssertAlongGroundAtPlanarSpeed(MotorState next, float3 normal)
        {
            Assert.IsTrue(next.Grounded, "grounded");
            float planar = Len(next.PlanarVelocity);
            float speed = Len(next.Velocity);
            Assert.AreEqual(1f, speed / planar, 1e-4f, "|Velocity| == |PlanarVelocity| with StickSpeed 0");
            Assert.LessOrEqual(math.abs(math.dot(next.Velocity / speed, normal)), 1e-4f, "Velocity lies in the ground plane");
        }

        private static MotorInput Forward() => new MotorInput { Move = new float2(0f, 1f), ViewForward = new float3(0f, 0f, 1f) };

        [Test]
        public void N28_sol_witness_micro_speed_keeps_its_along_slope_length()
        {
            MotorSettings settings = MotorSettings.Default;
            settings.WalkSpeed = settings.SprintSpeed = 1e-6f;
            settings.StickSpeed = 0f;
            float3 normal = math.normalize(new float3(0f, 1f, -1f));
            var ground = new GroundContact { Hit = true, Normal = normal, Distance = 0f };

            MotorState next = SphericalMotor.Step(settings, SphericalMotor.Initial(Up, new float3(0f, 0f, 1f)), Forward(), ground, Up, 0.02f);

            Assert.AreEqual(1e-6f, Len(next.PlanarVelocity), 1e-9f);
            AssertAlongGroundAtPlanarSpeed(next, normal);
        }

        [Test]
        public void N28_sol_witness_near_vertical_legal_slope_keeps_its_along_slope_length()
        {
            MotorSettings settings = MotorSettings.Default;
            settings.MaxSlopeDegrees = 89.99999f;
            settings.StickSpeed = 0f;
            float3 normal = math.normalize(new float3(0f, 2e-7f, -1f));
            var ground = new GroundContact { Hit = true, Normal = normal, Distance = 0f };

            MotorState next = SphericalMotor.Step(settings, SphericalMotor.Initial(Up, new float3(0f, 0f, 1f)), Forward(), ground, Up, 0.1f);

            Assert.AreEqual(4f, Len(next.Velocity), 1e-4f);
            AssertAlongGroundAtPlanarSpeed(next, normal);
        }

        // sol P1 r15 N30: the Post's `slope ≤ MaxSlope` is closed; only a contact exactly at the limit tells `>=` from `>`.
        [Test]
        public void N30_contact_exactly_at_max_slope_is_grounded()
        {
            MotorSettings settings = MotorSettings.Default;
            settings.MaxSlopeDegrees = 45f;
            settings.StickSpeed = 0f;
            float3 normal = math.normalize(new float3(0f, 1f, -1f));
            Assert.AreEqual(math.cos(math.radians(45f)), math.dot(normal, Up), "witness sits exactly on the float limit");
            var ground = new GroundContact { Hit = true, Normal = normal, Distance = 0f };

            MotorState next = SphericalMotor.Step(settings, SphericalMotor.Initial(Up, new float3(0f, 0f, 1f)), Forward(), ground, Up, 0.02f);

            Assert.IsTrue(next.Grounded);
        }

        [Test]
        public void N28_random_speeds_and_slopes_keep_the_along_slope_length()
        {
            MotorSettings settings = MotorSettings.Default;
            settings.MaxSlopeDegrees = 89.99999f;
            settings.StickSpeed = 0f;
            settings.GroundAcceleration = 1e-20f;
            float cosMax = math.cos(math.radians(settings.MaxSlopeDegrees));
            float minDot = cosMax * 1.01f;
            var rng = new Pcg32(28, 14);
            int checkedCount = 0;
            for (int i = 0; i < 20000; i++)
            {
                float3 up = math.normalize(new float3(rng.NextFloat() * 2f - 1f, rng.NextFloat() * 2f - 1f, rng.NextFloat() * 2f - 1f) + 1e-3f);
                PlanetFrame frame = PlanetFrame.FromUp(up);
                float a = rng.NextFloat() * 2f * math.PI;
                float3 slopeDir = frame.East * math.cos(a) + frame.North * math.sin(a);
                // Log-uniform cos(slope) from just above the limit to flat, so near-vertical slopes are well covered.
                float d = math.exp(math.lerp(math.log(minDot), 0f, rng.NextFloat()));
                float3 normal = math.normalize(up * d - slopeDir * math.sqrt(1f - d * d));
                // Normalising shifts dot(normal, up) by ~1e-7, comparable to cos(slope) itself near the limit; samples
                // that land beyond MaxSlope in float (Step re-normalises up, so test the same value) are not grounded.
                if (math.dot(normal, math.normalize(up)) < cosMax)
                {
                    continue;
                }

                float b = rng.NextFloat() * 2f * math.PI;
                float3 moveDir = frame.East * math.cos(b) + frame.North * math.sin(b);
                float speed = math.exp(math.lerp(math.log(1e-30f), math.log(SphericalMotor.MaxStateSpeed * 0.9f), rng.NextFloat()));
                MotorState prev = SphericalMotor.Initial(up, moveDir);
                prev.PlanarVelocity = prev.Velocity = moveDir * speed;
                var ground = new GroundContact { Hit = true, Normal = normal, Distance = 0f };

                MotorState next = SphericalMotor.Step(settings, prev, default, ground, up, 0.02f);

                Assert.DoesNotThrow(() => SphericalMotor.Step(settings, next, default, ground, up, 0.02f), "next Step accepts the output");
                if (Len(next.PlanarVelocity) == 0f)
                {
                    continue;
                }

                AssertAlongGroundAtPlanarSpeed(next, normal);
                checkedCount++;
            }

            Assert.Greater(checkedCount, 15000, "near-zero ground acceleration keeps most speeds non-zero");
        }
    }
}
