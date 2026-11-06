using System.Reflection;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Player.Tests
{
    // Regressions for docs/reviews/P1-review-r7-sol.md (N12, N13, U17). PlayMode because Configure needs Cinemachine.
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
            Assert.IsNull(_third.Target.TrackingTarget, "nothing was wired");
            Assert.Throws<ContractException>(() => _ = _rig.Heading, "still unconfigured");
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
        public void U17_pitch_limits_are_plus_minus_80_degrees()
        {
            // REQ: docs/reviews/P1-05-sol.md — the camera pitch range is a fixed spec value, not just a shared constant.
            Assert.AreEqual(80f, ViewOrbit.MaxPitch);
            Assert.AreEqual(-80f, ViewOrbit.MinPitch);
        }
    }
}
