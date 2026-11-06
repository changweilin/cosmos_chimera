using Cosmimera.Core.Contracts;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Player.Tests
{
    public sealed class SphericalMotorTests
    {
        private const float R = 640f;
        private const float Dt = 1f / 50f;

        private static GroundContact FlatGround(float3 up) => new GroundContact { Hit = true, Normal = up, Distance = 0f };

        private static MotorInput Forward(float3 view, bool sprint = false) =>
            new MotorInput { Move = new float2(0f, 1f), ViewForward = view, Sprint = sprint };

        /// <summary>Kinematic walk on an ideal sphere: the motor's tangential velocity moves the point along the surface.</summary>
        private static float3 Advance(float3 dir, float3 velocity)
        {
            float3 p = dir * R + velocity * Dt;
            return math.normalize(p);
        }

        [Test]
        public void Initial_projects_facing_onto_the_tangent_plane()
        {
            MotorState s = SphericalMotor.Initial(new float3(0f, 1f, 0f), new float3(1f, 5f, 0f));
            Assert.AreEqual(1f, math.length(s.Facing), 1e-5f);
            Assert.AreEqual(0f, math.dot(s.Facing, s.Up), 1e-5f);
            Assert.AreEqual(1f, s.Facing.x, 1e-5f);
            Assert.IsFalse(s.Grounded);
        }

        [Test]
        public void Walking_reaches_walk_speed_and_sprint_reaches_sprint_speed()
        {
            float3 up = new float3(0f, 0f, 1f);
            MotorSettings settings = MotorSettings.Default;
            MotorState s = SphericalMotor.Initial(up, new float3(1f, 0f, 0f));
            for (int i = 0; i < 50; i++)
            {
                s = SphericalMotor.Step(settings, s, Forward(new float3(1f, 0f, 0f)), FlatGround(up), up, Dt);
            }

            float3 tangential = s.Velocity - up * math.dot(s.Velocity, up);
            Assert.AreEqual(settings.WalkSpeed, math.length(tangential), 1e-3f);
            Assert.IsTrue(s.Grounded);

            for (int i = 0; i < 50; i++)
            {
                s = SphericalMotor.Step(settings, s, Forward(new float3(1f, 0f, 0f), sprint: true), FlatGround(up), up, Dt);
            }

            tangential = s.Velocity - up * math.dot(s.Velocity, up);
            Assert.AreEqual(settings.SprintSpeed, math.length(tangential), 1e-3f);
        }

        // Acceptance P1-05: no flip at the poles. The camera heading is carried, so the motor must keep going straight
        // over the pole (a great circle) and the facing must change smoothly, never by a sudden half turn.
        [TestCase(0f, 1f, 0f, TestName = "Walk_over_north_pole_stays_on_great_circle")]
        [TestCase(0f, -1f, 0f, TestName = "Walk_over_south_pole_stays_on_great_circle")]
        [TestCase(1f, 1f, 1f, TestName = "Walk_over_cube_corner_stays_on_great_circle")]
        [TestCase(1f, 1f, 0f, TestName = "Walk_over_cube_edge_stays_on_great_circle")]
        public void Walk_over_point_stays_on_great_circle(float x, float y, float z)
        {
            float3 target = math.normalize(new float3(x, y, z));
            float3 side = math.normalize(math.cross(target, math.abs(target.z) < 0.9f ? new float3(0f, 0f, 1f) : new float3(1f, 0f, 0f)));
            // Start 20 m before the target on the great circle through it, heading towards it.
            float startAngle = -20f / R;
            float3 dir = math.mul(quaternion.AxisAngle(side, startAngle), target);
            float3 heading = math.normalize(target - dir * math.dot(target, dir));
            MotorSettings settings = MotorSettings.Default;
            MotorState s = SphericalMotor.Initial(dir, heading);
            var orbit = ViewOrbit.Create(dir, heading);

            float maxFacingStep = 0f;
            float maxOffPlane = 0f;
            for (int i = 0; i < 400; i++)
            {
                float3 prevFacing = s.Facing;
                orbit.Advance(dir, 0f, 0f);
                s = SphericalMotor.Step(settings, s, Forward(orbit.Heading, sprint: true), FlatGround(dir), dir, Dt);
                Assert.IsTrue(math.all(math.isfinite(s.Velocity)) && math.all(math.isfinite(s.Facing)), $"non-finite at step {i}");
                maxFacingStep = math.max(maxFacingStep, math.degrees(math.acos(math.clamp(math.dot(prevFacing, s.Facing), -1f, 1f))));
                float3 tangential = s.Velocity - dir * math.dot(s.Velocity, dir);
                dir = Advance(dir, tangential);
                maxOffPlane = math.max(maxOffPlane, math.abs(math.dot(dir, side)));
            }

            // 400 steps × up to 8 m/s × 0.02 s ≈ 60 m: well past the target.
            Assert.Greater(math.dot(dir, math.mul(quaternion.AxisAngle(side, 20f / R), target)), math.cos(30f / R),
                "did not pass the target along the great circle");
            Assert.Less(maxOffPlane, 1e-3f, "drifted off the great circle");
            // Transport alone turns facing by the arc per step (0.16 m / 640 m ≈ 0.014°).
            Assert.Less(maxFacingStep, 0.1f, "facing jumped");
        }

        [Test]
        public void Strafe_right_moves_towards_unity_screen_right()
        {
            float3 up = new float3(0f, 1f, 0f);
            MotorState s = SphericalMotor.Initial(up, new float3(0f, 0f, 1f));
            var strafe = new MotorInput { Move = new float2(1f, 0f), ViewForward = new float3(0f, 0f, 1f) };
            s = SphericalMotor.Step(MotorSettings.Default, s, strafe, FlatGround(up), up, Dt);
            Assert.Greater(s.Velocity.x, 0f, "Vector3.right is +X when looking along +Z with +Y up");
        }

        [Test]
        public void Jump_apex_matches_jump_height()
        {
            float3 up = new float3(0f, 1f, 0f);
            MotorSettings settings = MotorSettings.Default;
            MotorState s = SphericalMotor.Initial(up, new float3(0f, 0f, 1f));
            s = SphericalMotor.Step(settings, s, default(MotorInput), FlatGround(up), up, Dt);
            Assert.IsTrue(s.Grounded);

            var jump = new MotorInput { Jump = true, ViewForward = new float3(0f, 0f, 1f) };
            s = SphericalMotor.Step(settings, s, jump, FlatGround(up), up, Dt);
            Assert.IsFalse(s.Grounded);

            float height = 0f;
            float apex = 0f;
            for (int i = 0; i < 100 && math.dot(s.Velocity, up) > 0f; i++)
            {
                height += math.dot(s.Velocity, up) * Dt;
                apex = math.max(apex, height);
                s = SphericalMotor.Step(settings, s, default(MotorInput), default(GroundContact), up, Dt);
            }

            // Explicit Euler over 0.02 s steps overshoots by about v·dt/2 ≈ 0.05 m.
            Assert.AreEqual(settings.JumpHeight, apex, 0.1f);
        }

        [Test]
        public void Jump_is_ignored_while_airborne()
        {
            float3 up = new float3(0f, 1f, 0f);
            MotorState s = SphericalMotor.Initial(up, new float3(0f, 0f, 1f));
            var jump = new MotorInput { Jump = true };
            s = SphericalMotor.Step(MotorSettings.Default, s, jump, default(GroundContact), up, Dt);
            Assert.Less(math.dot(s.Velocity, up), 0f);
        }

        [Test]
        public void Too_steep_ground_is_not_walkable_and_cannot_be_climbed()
        {
            float3 up = new float3(0f, 1f, 0f);
            MotorSettings settings = MotorSettings.Default;
            // 60° wall facing -z (its normal points back towards the walker coming from -z).
            float a = math.radians(60f);
            float3 normal = new float3(0f, math.cos(a), -math.sin(a));
            var wall = new GroundContact { Hit = true, Normal = normal, Distance = 0f };
            MotorState s = SphericalMotor.Initial(up, new float3(0f, 0f, 1f));
            for (int i = 0; i < 50; i++)
            {
                s = SphericalMotor.Step(settings, s, Forward(new float3(0f, 0f, 1f)), wall, up, Dt);
                Assert.IsFalse(s.Grounded);
                Assert.LessOrEqual(s.Velocity.z, 1e-4f, "pushed into the steep slope");
            }
        }

        [Test]
        public void Walkable_slope_is_followed_at_full_speed()
        {
            float3 up = new float3(0f, 1f, 0f);
            MotorSettings settings = MotorSettings.Default;
            float a = math.radians(30f);
            float3 normal = new float3(0f, math.cos(a), -math.sin(a));
            var slope = new GroundContact { Hit = true, Normal = normal, Distance = 0.05f };
            MotorState s = SphericalMotor.Initial(up, new float3(0f, 0f, 1f));
            for (int i = 0; i < 50; i++)
            {
                s = SphericalMotor.Step(settings, s, Forward(new float3(0f, 0f, 1f)), slope, up, Dt);
            }

            Assert.IsTrue(s.Grounded);
            float3 along = s.Velocity + normal * settings.StickSpeed;
            Assert.AreEqual(0f, math.dot(along, normal), 1e-4f);
            Assert.AreEqual(settings.WalkSpeed, math.length(along), 1e-3f);
            Assert.Greater(along.y, 0f, "uphill velocity must rise");
        }

        // Regression: re-deriving the planar velocity from the slope-following velocity lost cos(slope) per step and
        // the stick push added an uphill component, so walking downhill a 25° slope decayed to ~2.5 m/s.
        [TestCase(25f, TestName = "Downhill_slope_keeps_walk_speed")]
        [TestCase(-25f, TestName = "Uphill_slope_keeps_walk_speed")]
        public void Slope_keeps_walk_speed(float downhillDegrees)
        {
            float3 up = new float3(0f, 1f, 0f);
            MotorSettings settings = MotorSettings.Default;
            float a = math.radians(downhillDegrees);
            // Walking +z; a positive tilt of the normal towards +z means the ground falls away ahead.
            float3 normal = new float3(0f, math.cos(a), math.sin(a));
            var slope = new GroundContact { Hit = true, Normal = normal, Distance = 0f };
            MotorState s = SphericalMotor.Initial(up, new float3(0f, 0f, 1f));
            for (int i = 0; i < 100; i++)
            {
                s = SphericalMotor.Step(settings, s, Forward(new float3(0f, 0f, 1f)), slope, up, Dt);
            }

            Assert.AreEqual(settings.WalkSpeed, math.length(s.PlanarVelocity), 1e-3f);
            float3 along = s.Velocity + normal * settings.StickSpeed;
            Assert.AreEqual(settings.WalkSpeed, math.length(along), 1e-3f);
        }

        [Test]
        public void Facing_turns_at_most_turn_rate()
        {
            float3 up = new float3(0f, 1f, 0f);
            MotorSettings settings = MotorSettings.Default;
            MotorState s = SphericalMotor.Initial(up, new float3(0f, 0f, 1f));
            var back = new MotorInput { Move = new float2(0f, 1f), ViewForward = new float3(0f, 0f, -1f) };
            s = SphericalMotor.Step(settings, s, back, FlatGround(up), up, Dt);
            float turned = math.degrees(math.acos(math.clamp(s.Facing.z, -1f, 1f)));
            Assert.AreEqual(settings.TurnRateDegrees * Dt, turned, 0.01f);
        }

        [Test]
        public void Initial_rejects_bad_input()
        {
            Assert.Throws<ContractException>(() => SphericalMotor.Initial(new float3(0f, 2f, 0f), new float3(1f, 0f, 0f)));
            Assert.Throws<ContractException>(() => SphericalMotor.Initial(new float3(0f, 1f, 0f), new float3(0f, 3f, 0f)));
            Assert.Throws<ContractException>(() => SphericalMotor.Initial(new float3(0f, 1f, 0f), new float3(float.NaN, 0f, 0f)));
        }

        [Test]
        public void Step_rejects_contract_violations()
        {
            float3 up = new float3(0f, 1f, 0f);
            MotorSettings ok = MotorSettings.Default;
            MotorState s = SphericalMotor.Initial(up, new float3(1f, 0f, 0f));
            GroundContact g = FlatGround(up);
            var input = new MotorInput();
            MotorSettings bad = ok;
            bad.MaxSlopeDegrees = 90f;

            Assert.Throws<ContractException>(() => SphericalMotor.Step(bad, s, input, g, up, Dt));
            Assert.Throws<ContractException>(() => SphericalMotor.Step(ok, s, input, g, up, 0f));
            Assert.Throws<ContractException>(() => SphericalMotor.Step(ok, s, input, g, up, 0.2f));
            Assert.Throws<ContractException>(() => SphericalMotor.Step(ok, s, input, g, up * 2f, Dt));
            Assert.Throws<ContractException>(() => SphericalMotor.Step(ok, s, input, g, -up, Dt));
            Assert.Throws<ContractException>(() => SphericalMotor.Step(ok, default(MotorState), input, g, up, Dt));
            Assert.Throws<ContractException>(() => SphericalMotor.Step(ok, s, new MotorInput { Move = new float2(1f, 1f) }, g, up, Dt));
            Assert.Throws<ContractException>(() =>
                SphericalMotor.Step(ok, s, new MotorInput { ViewForward = new float3(float.PositiveInfinity, 0f, 0f) }, g, up, Dt));
            Assert.Throws<ContractException>(() =>
                SphericalMotor.Step(ok, s, input, new GroundContact { Hit = true, Normal = up * 3f }, up, Dt));
            Assert.Throws<ContractException>(() =>
                SphericalMotor.Step(ok, s, input, new GroundContact { Hit = true, Normal = up, Distance = -1f }, up, Dt));
        }
    }
}
