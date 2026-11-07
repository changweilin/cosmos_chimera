using Cosmimera.Core.Random;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Player.Tests
{
    // Counter-example from docs/reviews/P1-review-r11-sol.md (N21): blocking a fast push into a steep wall cancels to a
    // rounding residue that left the tangent plane.
    public sealed class SphericalMotorReviewR11Tests
    {
        private const float Dt = 0.02f;

        private static MotorSettings Gliding()
        {
            MotorSettings settings = MotorSettings.Default;
            settings.AirAcceleration = 1e-20f;
            return settings;
        }

        private static void AssertTangentAndAccepted(MotorSettings settings, MotorState next, GroundContact ground, float3 up)
        {
            Assert.LessOrEqual(math.abs(math.dot(next.PlanarVelocity, next.Up)),
                1e-4f * math.max(1f, math.length(next.PlanarVelocity)), "relative tangent Post");
            Assert.DoesNotThrow(() => SphericalMotor.Step(settings, next, default, ground, up, Dt), "next Step accepts the output");
        }

        [Test]
        public void N21_sol_witness_steep_wall_cancellation_stays_tangent()
        {
            MotorSettings settings = Gliding();
            float3 up = math.normalize(new float3(1f, 2f, 3f));
            float3 normal = math.normalize(new float3(-1f, -3f, 3f));
            float3 wall = SphericalMotor.TangentOr(normal, up, float3.zero);
            MotorState prev = SphericalMotor.Initial(up, wall);
            prev.PlanarVelocity = prev.Velocity = -9999f * wall;
            var ground = new GroundContact { Hit = true, Normal = normal, Distance = 0f };

            MotorState next = SphericalMotor.Step(settings, prev, default, ground, up, Dt);

            AssertTangentAndAccepted(settings, next, ground, up);
        }

        [Test]
        public void N21_random_near_total_wall_pushes_stay_tangent()
        {
            MotorSettings settings = Gliding();
            float minDot = math.cos(math.radians(89f));
            float maxDot = math.cos(math.radians(settings.MaxSlopeDegrees));
            var rng = new Pcg32(21, 11);
            int steep = 0;
            for (int i = 0; i < 20000; i++)
            {
                float3 up = math.normalize(new float3(rng.NextFloat() * 2f - 1f, rng.NextFloat() * 2f - 1f, rng.NextFloat() * 2f - 1f) + 1e-3f);
                PlanetFrame frame = PlanetFrame.FromUp(up);
                float3 east = frame.East;
                float3 north = frame.North;
                float a = rng.NextFloat() * 2f * math.PI;
                float3 dir = east * math.cos(a) + north * math.sin(a);
                float d = math.lerp(minDot, maxDot * 0.999f, rng.NextFloat());
                float3 normal = math.normalize(up * d - dir * math.sqrt(1f - d * d));
                float3 wall = SphericalMotor.TangentOr(normal, up, float3.zero);
                // Speed almost entirely into the wall, with a tiny along-wall part, maximises cancellation.
                float3 along = math.normalize(math.cross(up, wall));
                float speed = math.lerp(1000f, SphericalMotor.MaxStateSpeed * 0.999f, rng.NextFloat());
                MotorState prev = SphericalMotor.Initial(up, wall);
                prev.PlanarVelocity = prev.Velocity = -speed * wall + along * (rng.NextFloat() * 1e-2f);
                var ground = new GroundContact { Hit = true, Normal = normal, Distance = 0f };

                MotorState next = SphericalMotor.Step(settings, prev, default, ground, up, Dt);

                AssertTangentAndAccepted(settings, next, ground, up);
                steep++;
            }

            Assert.AreEqual(20000, steep);
        }
    }
}
