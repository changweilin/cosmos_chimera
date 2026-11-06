using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Player
{
    /// <summary>Drives a view pivot from <see cref="ViewOrbit"/> and switches Cinemachine between third and first person.</summary>
    /// <remarks>
    /// Pre: <see cref="Configure"/> before the first LateUpdate; both cameras use the pivot as tracking target — third
    ///      person with CinemachineThirdPersonFollow, first person with HardLockToTarget + RotateWithFollowTarget — so
    ///      the camera inherits the pivot's planet-relative rotation instead of computing one against world Y.
    /// Post: after each LateUpdate the pivot sits EyeHeight above the character along local up, rotated by the orbit.
    /// Side effects: writes pivot transform, camera priorities and the brain's WorldUpOverride.
    /// </remarks>
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        [SerializeField] private float _eyeHeight = 1.6f;
        [SerializeField] private float _sensitivityDegrees = 0.12f;
        [SerializeField] private float _gamepadDegreesPerSecond = 180f;

        private PlanetConfig _planet;
        private Transform _target;
        private Transform _pivot;
        private CinemachineCamera _thirdPerson;
        private CinemachineCamera _firstPerson;
        private ViewOrbit _orbit;
        private float2 _pendingLook;
        private bool _configured;

        /// <summary>True when the first-person camera is live.</summary>
        /// <remarks>Pre: none. Post: false after Configure. Side effects: none.</remarks>
        public bool IsFirstPerson { get; private set; }

        /// <summary>Horizontal camera heading (unit, tangent to the planet) for movement input.</summary>
        /// <remarks>Pre: Configure called. Post: unit, ⟂ local up of the last LateUpdate. Side effects: none.</remarks>
        public Vector3 Heading => _orbit.Heading;

        /// <summary>Wires the rig.</summary>
        /// <remarks>
        /// Pre: planet.IsValid; target, pivot, both cameras and brain non-null; pivot is not a child of target (the
        ///      orbit, not the body, owns its rotation).
        /// Post: third person live; orbit heading == target forward; brain.WorldUpOverride == pivot.
        /// Side effects: mutates camera targets/priorities and brain settings.
        /// </remarks>
        public void Configure(in PlanetConfig planet, Transform target, Transform pivot, CinemachineCamera thirdPerson,
            CinemachineCamera firstPerson, CinemachineBrain brain)
        {
            Contract.Requires(planet.IsValid, "planet must be valid");
            Contract.Requires(target != null && pivot != null && thirdPerson != null && firstPerson != null && brain != null,
                "all rig references are required");
            Contract.Requires(!pivot.IsChildOf(target), "pivot must not be parented to the target");

            _planet = planet;
            _target = target;
            _pivot = pivot;
            _thirdPerson = thirdPerson;
            _firstPerson = firstPerson;
            _thirdPerson.Target.TrackingTarget = pivot;
            _firstPerson.Target.TrackingTarget = pivot;
            // Cinemachine blends and damps in the brain's up; the pivot's up is the local planet up.
            brain.WorldUpOverride = pivot;

            float3 up = PlanetFrame.At(planet, target.position).Up;
            _orbit = ViewOrbit.Create(up, SphericalMotor.TangentOr(target.forward, up, PlanetFrame.FromUp(up).North));
            SetFirstPerson(false);
            _configured = true;
            LateUpdate();
        }

        /// <summary>Queues look input for the next LateUpdate.</summary>
        /// <remarks>
        /// Pre: delta finite. pointerDelta is in pixels (mouse); stick is in [-1, 1] and scaled by deltaTime here.
        /// Post: accumulated until consumed. Side effects: mutates pending look.
        /// </remarks>
        public void AddLook(Vector2 pointerDelta, Vector2 stick, float deltaTime)
        {
            _pendingLook += (float2)pointerDelta * _sensitivityDegrees + (float2)stick * (_gamepadDegreesPerSecond * deltaTime);
        }

        /// <summary>Switches between third and first person.</summary>
        /// <remarks>Pre: Configure called. Post: IsFirstPerson toggled; exactly one camera has the higher priority. Side effects: priorities.</remarks>
        public void ToggleView() => SetFirstPerson(!IsFirstPerson);

        private void SetFirstPerson(bool firstPerson)
        {
            IsFirstPerson = firstPerson;
            _firstPerson.Priority.Value = firstPerson ? 20 : 10;
            _thirdPerson.Priority.Value = firstPerson ? 10 : 20;
        }

        private void LateUpdate()
        {
            if (!_configured)
            {
                return;
            }

            float3 position = _target.position;
            float3 up = PlanetFrame.At(_planet, position).Up;
            _orbit.Advance(up, _pendingLook.x, _pendingLook.y);
            _pendingLook = float2.zero;
            _pivot.SetPositionAndRotation(position + up * _eyeHeight, _orbit.Rotation());
        }
    }
}
