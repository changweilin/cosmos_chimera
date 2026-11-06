using System.Collections;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Player.Tests
{
    // REQ: docs/reviews/P1-05-sol.md#F2 — the brain's world up must stay the local planet up while the view pitches.
    public sealed class PlayerCameraRigTests
    {
        private GameObject _root;
        private PlanetConfig _config;
        private Transform _target;
        private Transform _pivot;
        private CinemachineBrain _brain;
        private PlayerCameraRig _rig;

        [SetUp]
        public void SetUp()
        {
            _config = new PlanetConfig(640f, new float3(10f, -20f, 30f));
            _root = new GameObject("PlayerCameraRigTest");
            _target = new GameObject("Target").transform;
            _target.SetParent(_root.transform, false);
            _pivot = new GameObject("Pivot").transform;
            _pivot.SetParent(_root.transform, false);
            var cam = new GameObject("Camera");
            cam.transform.SetParent(_root.transform, false);
            cam.AddComponent<Camera>();
            _brain = cam.AddComponent<CinemachineBrain>();
            var third = new GameObject("Third").AddComponent<CinemachineCamera>();
            third.transform.SetParent(_root.transform, false);
            third.gameObject.AddComponent<CinemachineThirdPersonFollow>();
            var first = new GameObject("First").AddComponent<CinemachineCamera>();
            first.transform.SetParent(_root.transform, false);
            first.gameObject.AddComponent<CinemachineHardLockToTarget>();
            first.gameObject.AddComponent<CinemachineRotateWithFollowTarget>();

            float3 dir = math.normalize(new float3(0.3f, 0.9f, -0.2f));
            _target.SetPositionAndRotation(_config.ToWorld(dir, 0f), quaternion.LookRotationSafe(PlanetFrame.FromUp(dir).North, dir));
            _rig = _target.gameObject.AddComponent<PlayerCameraRig>();
            _rig.Configure(_config, _target, _pivot, third, first, _brain);
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_root);
        }

        private float3 LocalUp => PlanetFrame.At(_config, _target.position).Up;

        [UnityTest]
        public IEnumerator Brain_world_up_ignores_pitch_in_both_views()
        {
            foreach (float pitch in new[] { 0f, ViewOrbit.MaxPitch, ViewOrbit.MinPitch })
            {
                foreach (bool firstPerson in new[] { false, true })
                {
                    if (_rig.IsFirstPerson != firstPerson)
                    {
                        _rig.ToggleView();
                    }

                    // Stick at the default 180°/s for 1 s. Pin the pitch at MinPitch first, then step up by the offset
                    // from MinPitch, so the orbit lands exactly on the target whatever the previous iteration left.
                    _rig.AddLook(Vector2.zero, new Vector2(0f, -1000f), 1f);
                    yield return null;
                    _rig.AddLook(Vector2.zero, new Vector2(0f, (pitch - ViewOrbit.MinPitch) / 180f), 1f);
                    yield return null;

                    Assert.AreSame(_rig.WorldUpReference, _brain.WorldUpOverride);
                    Assert.AreEqual(1f, math.dot((float3)_brain.DefaultWorldUp, LocalUp), 1e-5f, $"pitch {pitch}, first {firstPerson}");
                    float pivotTilt = math.dot((float3)_pivot.up, LocalUp);
                    Assert.AreEqual(math.cos(math.radians(pitch)), pivotTilt, 1e-3f, "pivot keeps the look pitch");
                }
            }
        }

        [UnityTest]
        public IEnumerator Brain_world_up_follows_local_up_across_the_pole()
        {
            float3 axis = new float3(1f, 0f, 0f);
            float3 start = math.normalize(new float3(0f, 1f, -0.05f));
            _rig.AddLook(Vector2.zero, new Vector2(0f, 60f / 180f), 1f);
            for (int i = 0; i < 40; i++)
            {
                float3 dir = math.mul(quaternion.AxisAngle(axis, i * 0.0025f), start);
                _target.position = _config.ToWorld(dir, 0f);
                yield return null;
                Assert.AreEqual(1f, math.dot((float3)_brain.DefaultWorldUp, LocalUp), 1e-5f, $"step {i}");
            }
        }
    }
}
