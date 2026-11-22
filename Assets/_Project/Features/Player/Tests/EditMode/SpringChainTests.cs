using Cosmimera.Core.Contracts;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Player.Tests
{
    public sealed class SpringChainTests
    {
        private static readonly float3 Side = new float3(1f, 0f, 0f);
        private static readonly float3 Down = new float3(0f, -1f, 0f);

        // A three-segment chain sticking out sideways, like a sleeve panel on a raised arm.
        private static SpringChain Sideways() =>
            new SpringChain(new[] { float3.zero, Side * 0.1f, Side * 0.2f, Side * 0.3f });

        private static float3[] Dirs(float3 d) => new[] { d, d, d };

        [Test]
        public void Free_chain_settles_hanging_along_gravity()
        {
            var c = Sideways();
            for (int i = 0; i < 600; i++)
            {
                c.Step(float3.zero, Dirs(Side), Down * 9.81f, 0f, 0.1f, 1f / 60f);
            }

            float3 tipDir = math.normalize(c.Joint(3) - c.Joint(0));
            Assert.Greater(math.dot(tipDir, Down), 0.99f);
        }

        [Test]
        public void Rigid_chain_without_gravity_keeps_its_rest_shape()
        {
            var c = Sideways();
            for (int i = 0; i < 60; i++)
            {
                c.Step(float3.zero, Dirs(Side), float3.zero, 1f, 0f, 1f / 60f);
            }

            Assert.AreEqual(0.3f, c.Joint(3).x, 1e-4f);
            Assert.AreEqual(0f, c.Joint(3).y, 1e-4f);
        }

        [Test]
        public void Segments_keep_their_length_and_root_follows()
        {
            var c = Sideways();
            float3 root = new float3(0.5f, 1f, -0.2f);
            c.Step(root, Dirs(Side), Down * 9.81f, 0.04f, 0.12f, 1f / 30f);
            Assert.AreEqual(0f, math.distance(c.Joint(0), root), 1e-6f);
            for (int i = 0; i < c.Count - 1; i++)
            {
                Assert.AreEqual(c.SegmentLength(i), math.distance(c.Joint(i), c.Joint(i + 1)), 1e-5f);
            }
        }

        [Test]
        public void Moving_root_does_not_fling_the_chain()
        {
            // Translating the whole body (across the chain) must not read as swing velocity.
            var c = Sideways();
            float3 walk = new float3(0f, 0f, 1f);
            for (int i = 0; i < 120; i++)
            {
                c.Step(walk * (0.05f * i), Dirs(Side), float3.zero, 0f, 0.5f, 1f / 60f);
            }

            float3 rel = c.Joint(3) - c.Joint(0);
            Assert.AreEqual(0.3f, rel.x, 1e-4f);
            Assert.AreEqual(0f, rel.z, 1e-4f);
        }

        [Test]
        public void Hanging_chain_stays_outside_a_body_capsule()
        {
            // A thigh-like capsule right below the root: the free chain must drape around it, not through it.
            var c = Sideways();
            var thigh = new[] { new Capsule { A = new float3(0.03f, -0.12f, -0.2f), B = new float3(0.03f, -0.12f, 0.2f), Radius = 0.06f } };
            for (int i = 0; i < 600; i++)
            {
                c.Step(float3.zero, Dirs(Side), Down * 9.81f, 0f, 0.1f, 1f / 60f, thigh, 1);
            }

            for (int i = 1; i < c.Count; i++)
            {
                float3 p = c.Joint(i);
                float dist = math.length(new float2(p.x - 0.03f, p.y + 0.12f));
                Assert.GreaterOrEqual(dist, 0.06f - 1e-3f, $"joint {i} inside the capsule");
            }
        }

        [Test]
        public void Capsule_push_out_moves_inside_points_to_the_surface_only()
        {
            var cap = new Capsule { A = float3.zero, B = new float3(0f, 0f, 1f), Radius = 0.1f };
            float3 inside = cap.PushOut(new float3(0.05f, 0f, 0.5f), Side);
            Assert.AreEqual(0.1f, math.length(inside.xy), 1e-5f);
            float3 outside = new float3(0.3f, 0f, 0.5f);
            Assert.AreEqual(0f, math.distance(cap.PushOut(outside, Side), outside), 1e-6f);
        }

        [Test]
        public void Collider_count_beyond_array_violates_precondition()
        {
            var c = Sideways();
            Assert.Throws<ContractException>(() => c.Step(float3.zero, Dirs(Side), float3.zero, 0f, 0f, 0.01f, new Capsule[1], 2));
        }

        [Test]
        public void Coincident_joints_violate_precondition()
        {
            Assert.Throws<ContractException>(() => new SpringChain(new[] { float3.zero, float3.zero }));
        }

        [Test]
        public void Single_joint_violates_precondition()
        {
            Assert.Throws<ContractException>(() => new SpringChain(new[] { float3.zero }));
        }

        [Test]
        public void Nonpositive_dt_violates_precondition()
        {
            var c = Sideways();
            Assert.Throws<ContractException>(() => c.Step(float3.zero, Dirs(Side), float3.zero, 0f, 0f, 0f));
        }

        [Test]
        public void Wrong_direction_count_violates_precondition()
        {
            var c = Sideways();
            Assert.Throws<ContractException>(() => c.Step(float3.zero, new[] { Side }, float3.zero, 0f, 0f, 0.01f));
        }

        [Test]
        public void Stiffness_out_of_range_violates_precondition()
        {
            var c = Sideways();
            Assert.Throws<ContractException>(() => c.Step(float3.zero, Dirs(Side), float3.zero, 1.5f, 0f, 0.01f));
        }
    }
}
