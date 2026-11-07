using System.Reflection;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Player.Tests
{
    // Regressions for docs/reviews/P1-review-r7-sol.md (N12, N13, U17) r8 (N12-R8, N14, N16, N17) and r9 (N18, N19) r10 (N20) and r11 (N22, N23, camera/brain output ownership) and r12 (N24, N25). PlayMode because Configure needs Cinemachine.
    public sealed class PlayerCameraRigConfigureTests
    {
        private GameObject _root;
        private PlanetConfig _config;
        private Transform _target;
        private Transform _pivot;
        private CinemachineBrain _brain;
        private CinemachineCamera _third;
        private CinemachineCamera _first;
        private PlayerCameraRig _rig;

        [SetUp]
        public void SetUp()
        {
            _config = new PlanetConfig(640f, float3.zero);
            _root = new GameObject("RigConfigureTest");
            _target = new GameObject("Target").transform;
            _target.SetParent(_root.transform, false);
            _pivot = new GameObject("Pivot").transform;
            _pivot.SetParent(_root.transform, false);
            var cam = new GameObject("Camera");
            cam.transform.SetParent(_root.transform, false);
            cam.AddComponent<Camera>();
            _brain = cam.AddComponent<CinemachineBrain>();
            _third = new GameObject("Third").AddComponent<CinemachineCamera>();
            _third.transform.SetParent(_root.transform, false);
            _first = new GameObject("First").AddComponent<CinemachineCamera>();
            _first.transform.SetParent(_root.transform, false);
            _target.SetPositionAndRotation(new float3(0f, 640f, 0f), quaternion.LookRotationSafe(new float3(0f, 0f, 1f), new float3(0f, 1f, 0f)));
            _rig = _target.gameObject.AddComponent<PlayerCameraRig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        private void Configure() => _rig.Configure(_config, _target, _pivot, _third, _first, _brain);

        private void SetField(string name, float value) =>
            typeof(PlayerCameraRig).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_rig, value);

        [TestCase("_sensitivityDegrees", -PlayerCameraRig.MaxSensitivityDegrees * 1.01f)]
        [TestCase("_sensitivityDegrees", PlayerCameraRig.MaxSensitivityDegrees * 1.01f)]
        [TestCase("_gamepadDegreesPerSecond", PlayerCameraRig.MaxGamepadDegreesPerSecond * 1.01f)]
        public void N12_configure_rejects_out_of_range_multipliers_before_wiring_anything(string field, float value)
        {
            SetField(field, value);
            Assert.Throws<ContractException>(Configure);
            AssertNothingWritten();
        }

        // sol P1 r8 N12-R8: TrackingTarget alone could not see an early write to the rig's own fields.
        private void AssertNothingWritten()
        {
            Assert.IsNull(_third.Target.TrackingTarget, "nothing was wired");
            Assert.Throws<ContractException>(() => _ = _rig.Heading, "still unconfigured");
            var planet = (PlanetConfig)typeof(PlayerCameraRig).GetField("_planet", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_rig);
            Assert.IsFalse(planet.IsValid, "_planet untouched");
            foreach (string field in new[] { "_target", "_pivot", "_thirdPerson", "_firstPerson" })
            {
                Assert.IsNull(typeof(PlayerCameraRig).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_rig), field);
            }
        }

        [Test]
        public void N16_target_at_the_planet_centre_is_rejected_before_wiring()
        {
            _target.position = Vector3.zero;
            Assert.Throws<ContractException>(Configure);
            AssertNothingWritten();
        }

        // sol P1 r9 N19: the centre alone cannot tell a correct 1e-3 m threshold from a weakened `> 0` one; the
        // ±1e-3 cases also pin the strict inequality at the boundary.
        [TestCase(0.0005f)]
        [TestCase(-0.0005f)]
        [TestCase(0.001f)]
        [TestCase(-0.001f)]
        public void N19_target_inside_the_centre_exclusion_box_is_rejected_before_wiring(float y)
        {
            _target.position = new Vector3(0f, y, 0f);
            Assert.Throws<ContractException>(Configure);
            AssertNothingWritten();
        }

        [Test]
        public void N18_target_parented_under_the_pivot_is_rejected_before_wiring()
        {
            _target.SetParent(_pivot, true);
            Vector3 before = _target.position;

            Assert.Throws<ContractException>(Configure);
            AssertNothingWritten();
            Assert.AreEqual(before, _target.position, "the body was not dragged by a pivot write");
        }

        [Test]
        public void N18_target_used_as_its_own_pivot_is_rejected_before_wiring()
        {
            Assert.Throws<ContractException>(() => _rig.Configure(_config, _target, _target, _third, _first, _brain));
            AssertNothingWritten();
        }

        // sol P1 r10 N20: the rig-owned level transform is reachable through brain.WorldUpOverride after Configure.
        [Test]
        public void N20_reconfigure_with_the_owned_world_up_as_pivot_is_rejected_before_rewiring()
        {
            Configure();
            Transform owned = _brain.WorldUpOverride;

            Assert.Throws<ContractException>(() => _rig.Configure(_config, _target, owned, _third, _first, _brain));
            Assert.AreSame(_pivot, _third.Target.TrackingTarget, "first wiring kept");
            Assert.AreSame(owned, _brain.WorldUpOverride);
        }

        [Test]
        public void N20_reconfigure_with_a_child_of_the_owned_world_up_as_pivot_is_rejected()
        {
            Configure();
            _pivot.SetParent(_brain.WorldUpOverride, true);

            Assert.Throws<ContractException>(Configure);
        }

        private object Field(string name) =>
            typeof(PlayerCameraRig).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_rig);

        // sol P1 r11 N22: the rejected re-Configure uses a different planet, so a guard moved after `_planet = planet`
        // shows up as a changed radius.
        private void AssertReconfigureRejectedWithoutWrites(Transform target, Transform pivot)
        {
            var other = new PlanetConfig(320f, float3.zero);
            Vector3 pivotPose = _pivot.position;

            Assert.Throws<ContractException>(() => _rig.Configure(other, target, pivot, _third, _first, _brain));
            Assert.AreEqual(640f, ((PlanetConfig)Field("_planet")).Radius, "_planet untouched");
            Assert.AreSame(_target, Field("_target"), "_target untouched");
            Assert.AreSame(_pivot, Field("_pivot"), "_pivot untouched");
            Assert.AreSame(_pivot, _third.Target.TrackingTarget, "wiring untouched");
            Assert.AreEqual(pivotPose, _pivot.position, "pivot pose untouched");
        }

        [Test]
        public void N22_reconfigure_with_the_owned_world_up_as_target_is_rejected_before_any_write()
        {
            Configure();
            AssertReconfigureRejectedWithoutWrites(_brain.WorldUpOverride, _pivot);
        }

        [Test]
        public void N22_reconfigure_with_a_child_of_the_owned_world_up_as_target_is_rejected_before_any_write()
        {
            Configure();
            var other = new GameObject("BelowOwned").transform;
            other.SetParent(_brain.WorldUpOverride, false);
            other.position = _target.position;
            try
            {
                AssertReconfigureRejectedWithoutWrites(other, _pivot);
            }
            finally
            {
                Object.DestroyImmediate(other.gameObject);
            }
        }

        [Test]
        public void N22_reconfigure_with_the_owned_world_up_as_pivot_is_rejected_before_any_write()
        {
            Configure();
            AssertReconfigureRejectedWithoutWrites(_target, _brain.WorldUpOverride);
        }

        [TestCase(PlayerCameraRig.MaxTargetComponent * 1.001f)]
        [TestCase(-PlayerCameraRig.MaxTargetComponent * 1.001f)]
        [TestCase(1e8f)]
        public void N23_target_beyond_the_eye_height_precision_domain_is_rejected_before_wiring(float y)
        {
            _target.position = new Vector3(0f, y, 0f);
            Assert.Throws<ContractException>(Configure);
            AssertNothingWritten();
        }

        [Test]
        public void N23_target_at_the_domain_edge_keeps_the_eye_height()
        {
            _target.position = new Vector3(0f, PlayerCameraRig.MaxTargetComponent, 0f);
            Configure();
            Assert.AreEqual(1.6f, Vector3.Distance(_target.position, _pivot.position), 4e-3f);
        }

        // sol P1 r12 N25: the bound is per component and on the world position, not on position − Center.
        [TestCase(PlayerCameraRig.MaxTargetComponent * 1.001f, 640f, 0f)]
        [TestCase(0f, 640f, -PlayerCameraRig.MaxTargetComponent * 1.001f)]
        public void N25_x_or_z_beyond_the_domain_is_rejected_before_wiring(float x, float y, float z)
        {
            _target.position = new Vector3(x, y, z);
            Assert.Throws<ContractException>(Configure);
            AssertNothingWritten();
        }

        [Test]
        public void N25_the_bound_applies_to_the_world_position_not_the_centre_offset()
        {
            var offCentre = new PlanetConfig(640f, new float3(16000f, 0f, 0f));
            _target.position = new Vector3(48000f, 0f, 0f);
            Assert.Throws<ContractException>(() => _rig.Configure(offCentre, _target, _pivot, _third, _first, _brain),
                "offset 32000 is in range, the world position is not");
            AssertNothingWritten();

            _target.position = new Vector3(-20000f, 0f, 0f);
            Assert.DoesNotThrow(() => _rig.Configure(offCentre, _target, _pivot, _third, _first, _brain),
                "offset −36000 is out of range, the world position is not");
        }

        [Test]
        public void N25_camera_above_the_pivot_is_rejected_before_wiring()
        {
            _pivot.SetParent(_third.transform, true);
            Assert.Throws<ContractException>(Configure);
            AssertNothingWritten();
        }

        // sol P1 r12 N24: CinemachineBrain writes ControlledObject.transform, which can differ from brain.transform.
        [Test]
        public void N24_brain_output_on_the_pivot_is_rejected_before_wiring()
        {
            _brain.ControlledObject = _pivot.gameObject;
            Assert.Throws<ContractException>(Configure);
            AssertNothingWritten();
        }

        [Test]
        public void N24_brain_output_above_the_target_is_rejected_before_wiring()
        {
            var holder = new GameObject("OutputHolder").transform;
            holder.SetParent(_root.transform, false);
            _target.SetParent(holder, true);
            _brain.ControlledObject = holder.gameObject;
            Assert.Throws<ContractException>(Configure);
            AssertNothingWritten();
        }

        [Test]
        public void N24_reconfigure_with_brain_output_on_the_owned_world_up_is_rejected_before_any_write()
        {
            Configure();
            _brain.ControlledObject = _brain.WorldUpOverride.gameObject;
            AssertReconfigureRejectedWithoutWrites(_target, _pivot);
        }

        [Test]
        public void R11_camera_on_the_pivot_object_is_rejected_before_wiring()
        {
            var onPivot = _pivot.gameObject.AddComponent<CinemachineCamera>();
            Assert.Throws<ContractException>(() => _rig.Configure(_config, _target, _pivot, onPivot, _first, _brain));
            AssertNothingWritten();
        }

        [Test]
        public void R11_brain_above_the_target_is_rejected_before_wiring()
        {
            _target.SetParent(_brain.transform, true);
            Assert.Throws<ContractException>(Configure);
            AssertNothingWritten();
        }

        [Test]
        public void R11_reconfigure_with_a_camera_above_the_owned_world_up_is_rejected_before_any_write()
        {
            Configure();
            Transform owned = _brain.WorldUpOverride;
            owned.SetParent(_first.transform, true);
            AssertReconfigureRejectedWithoutWrites(_target, _pivot);
        }

        [Test]
        public void N20_reconfigure_with_separate_transforms_keeps_the_pivot_at_eye_height()
        {
            Configure();
            Vector3 eye = _pivot.position;
            Configure();

            Assert.AreEqual(0f, Vector3.Distance(eye, _pivot.position), 1e-4f, "repeat Configure is idempotent");
            Assert.Greater(Vector3.Distance(_target.position, _pivot.position), 1f, "pivot sits above the body");
        }

        [Test]
        public void N17_the_same_camera_for_both_views_is_rejected_before_wiring()
        {
            Assert.Throws<ContractException>(() => _rig.Configure(_config, _target, _pivot, _third, _third, _brain));
            AssertNothingWritten();
        }

        [Test]
        public void N12_finite_negative_sensitivity_below_the_bound_is_rejected_by_add_look()
        {
            SetField("_sensitivityDegrees", -PlayerCameraRig.MaxSensitivityDegrees * 1.01f);
            Assert.Throws<ContractException>(() => _rig.AddLook(new Vector2(1f, 0f), Vector2.zero, 0f));
        }

        [Test]
        public void N13_look_queued_before_configure_is_discarded()
        {
            _rig.AddLook(new Vector2(750f, 0f), Vector2.zero, 0f);
            Configure();

            Assert.AreEqual(float2.zero, _rig.PendingLook);
            Assert.Greater(math.dot((float3)_rig.Heading, new float3(0f, 0f, 1f)), 0.9999f, "heading == target forward");
        }

        [Test]
        public void N14_pitch_queued_before_configure_is_discarded_too()
        {
            _rig.AddLook(new Vector2(0f, 750f), Vector2.zero, 0f);
            Configure();

            var orbit = (ViewOrbit)typeof(PlayerCameraRig).GetField("_orbit", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_rig);
            Assert.AreEqual(0f, orbit.PitchDegrees);
        }

        [Test]
        public void U17_pitch_limits_are_plus_minus_80_degrees()
        {
            // REQ: docs/reviews/P1-05-sol.md — the camera pitch range is a fixed spec value, not just a shared constant.
            Assert.AreEqual(80f, ViewOrbit.MaxPitch);
            Assert.AreEqual(-80f, ViewOrbit.MinPitch);
        }
    }
}
