using System.Reflection;
using Cosmimera.Core.Contracts;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Player.Tests
{
    // Regressions for docs/reviews/P1-review-r6-sol.md (N8-R6, N10, N11).
    public sealed class PlayerCameraRigReviewR6Tests
    {
        private GameObject _go;
        private PlayerCameraRig _rig;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("R6Rig");
            _rig = _go.AddComponent<PlayerCameraRig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        private void SetField(string name, float value)
        {
            typeof(PlayerCameraRig).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_rig, value);
        }

        [TestCase("_sensitivityDegrees", float.MaxValue)]
        [TestCase("_sensitivityDegrees", -float.MaxValue)]
        [TestCase("_sensitivityDegrees", float.NaN)]
        [TestCase("_sensitivityDegrees", PlayerCameraRig.MaxSensitivityDegrees * 1.01f)]
        [TestCase("_gamepadDegreesPerSecond", float.MaxValue)]
        [TestCase("_gamepadDegreesPerSecond", -1f)]
        [TestCase("_gamepadDegreesPerSecond", PlayerCameraRig.MaxGamepadDegreesPerSecond * 1.01f)]
        public void N8_R6_out_of_range_serialized_multipliers_are_rejected_before_the_queue_changes(string field, float value)
        {
            _rig.AddLook(new Vector2(10f, 10f), Vector2.zero, 0f);
            float2 before = _rig.PendingLook;
            SetField(field, value);

            Assert.Throws<ContractException>(() => _rig.AddLook(new Vector2(1e6f, 0f), new Vector2(1.001f, 0f), 1f));
            Assert.AreEqual(before, _rig.PendingLook);
        }

        [Test]
        public void N8_R6_extreme_legal_multipliers_keep_the_queue_finite_and_in_range()
        {
            SetField("_sensitivityDegrees", -PlayerCameraRig.MaxSensitivityDegrees);
            SetField("_gamepadDegreesPerSecond", PlayerCameraRig.MaxGamepadDegreesPerSecond);
            for (int i = 0; i < 1000; i++)
            {
                _rig.AddLook(new Vector2(PlayerCameraRig.MaxPointerDelta, -PlayerCameraRig.MaxPointerDelta),
                    new Vector2(1.001f, 1.001f), PlayerCameraRig.MaxLookDeltaTime);
                AssertInRange(_rig.PendingLook);
            }
        }

        [Test]
        public void N10_pitch_saturates_at_a_half_turn_within_one_frame()
        {
            // Default sensitivity 0.12°/px: +300° then −200°. Saturation keeps +180 then lands at −20 (documented Post),
            // whereas exact summation would give +100.
            _rig.AddLook(new Vector2(0f, 2500f), Vector2.zero, 0f);
            Assert.AreEqual(180f, _rig.PendingLook.y, 1e-3f);
            _rig.AddLook(new Vector2(0f, -1666.6666f), Vector2.zero, 0f);
            Assert.AreEqual(-20f, _rig.PendingLook.y, 1e-2f);
        }

        [Test]
        public void N11_pending_yaw_wraps_and_pitch_saturates()
        {
            _rig.AddLook(new Vector2(1e6f, 1e6f), Vector2.zero, 0f);
            Assert.AreEqual(120f, _rig.PendingLook.x, 1e-2f, "1.2e5° of yaw wraps to 120°");
            Assert.AreEqual(180f, _rig.PendingLook.y, "pitch saturates");
            _rig.AddLook(new Vector2(-1e6f, -1e6f), Vector2.zero, 0f);
            AssertInRange(_rig.PendingLook);
            Assert.AreEqual(-180f, _rig.PendingLook.y);
        }

        private static void AssertInRange(float2 look)
        {
            Assert.IsTrue(math.all(math.isfinite(look)), "finite");
            Assert.Less(math.abs(look.x), 360f, "yaw in (-360, 360)");
            Assert.LessOrEqual(math.abs(look.y), 180f, "pitch in [-180, 180]");
        }
    }
}
