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
    /// Post: after each LateUpdate the pivot sits EyeHeight above the character along local up, rotated by the orbit;
    ///       the brain's world up equals the local planet up regardless of pitch.
    /// Side effects: writes pivot transform, camera priorities and the brain's WorldUpOverride; owns one hidden
    ///       world-up transform (destroyed with the rig).
    /// </remarks>
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        [SerializeField] private float _eyeHeight = 1.6f;
        [SerializeField] private float _sensitivityDegrees = 0.12f;
        [SerializeField] private float _gamepadDegreesPerSecond = 180f;

        private PlanetConfig _planet;
        private Transform _target;
        private Transform _pivot;
        private Transform _worldUp;
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
        public Vector3 Heading
        {
            get
            {
                Contract.Requires(_configured, "Configure has not been called");
                return _orbit.Heading;
            }
        }

        /// <summary>Wires the rig.</summary>
        /// <remarks>
        /// Pre: planet.IsValid; target, pivot, both cameras and brain non-null; pivot is not a child of target (the
        ///      orbit, not the body, owns its rotation); serialized look multipliers in range (see AddLook).
        /// Post: third person live; orbit heading == target forward; brain.WorldUpOverride == WorldUpReference.
        /// Side effects: mutates camera targets/priorities and brain settings.
        /// </remarks>
        public void Configure(in PlanetConfig planet, Transform target, Transform pivot, CinemachineCamera thirdPerson,
            CinemachineCamera firstPerson, CinemachineBrain brain)
        {
            Contract.Requires(planet.IsValid, "planet must be valid");
            Contract.Requires(target != null && pivot != null && thirdPerson != null && firstPerson != null && brain != null,
                "all rig references are required");
            Contract.Requires(!pivot.IsChildOf(target), "pivot must not be parented to the target");
            RequireMultipliers();

            _planet = planet;
            _target = target;
            _pivot = pivot;
            _thirdPerson = thirdPerson;
            _firstPerson = firstPerson;
            _thirdPerson.Target.TrackingTarget = pivot;
            _firstPerson.Target.TrackingTarget = pivot;
            // Cinemachine blends and damps in WorldUpOverride.up. The pivot carries the look pitch, so it would tilt that
            // up by up to 80° (sol P1-05 F2); a separate level transform keeps it at the local planet up.
            if (_worldUp == null)
            {
                _worldUp = new GameObject("CameraWorldUp") { hideFlags = HideFlags.HideInHierarchy }.transform;
            }

            brain.WorldUpOverride = _worldUp;

            float3 up = PlanetFrame.At(planet, target.position).Up;
            _orbit = ViewOrbit.Create(up, SphericalMotor.TangentOr(target.forward, up, PlanetFrame.FromUp(up).North));
            SetFirstPerson(false);
            _configured = true;
            LateUpdate();
        }

        /// <summary>Transform whose up is the level local planet up; assigned to the brain's WorldUpOverride.</summary>
        /// <remarks>Pre: Configure called. Post: up == local up at the last LateUpdate. Side effects: none.</remarks>
        internal Transform WorldUpReference => _worldUp;

        /// <summary>Queues look input for the next LateUpdate.</summary>
        /// <remarks>
        /// Pre: each pointerDelta component finite with |·| ≤ MaxPointerDelta (pixels, mouse); each stick component in
        ///      [-1, 1] (+1e-3), scaled by deltaTime here; deltaTime in [0, MaxLookDeltaTime] (the project's Maximum
        ///      Allowed Timestep is 0.333 s, so Time.deltaTime satisfies it); the serialized multipliers are in range:
        ///      |sensitivity| ≤ MaxSensitivityDegrees, 0 ≤ gamepad rate ≤ MaxGamepadDegreesPerSecond, both finite.
        /// Post: yaw accumulates modulo 360, so pending yaw stays in (-360, 360). Pitch accumulates and **saturates**
        ///       at ±180°: input that pushes past a half turn in one direction within one frame is dropped, and later
        ///       opposite input starts from the saturated value. Any |pitch| ≥ 160 already drives the orbit to its
        ///       ±80° limit from every starting pitch, so saturation only differs from exact summation when one frame
        ///       mixes a > 180° swing with its partial reversal (SOL P1 r6 N10). The queued look is always finite.
        /// Side effects: mutates pending look.
        /// </remarks>
        public void AddLook(Vector2 pointerDelta, Vector2 stick, float deltaTime)
        {
            Contract.Requires(math.all(math.isfinite(new float4(pointerDelta.x, pointerDelta.y, stick.x, stick.y))),
                "look deltas must be finite");
            Contract.Requires(math.abs(pointerDelta.x) <= MaxPointerDelta && math.abs(pointerDelta.y) <= MaxPointerDelta,
                "pointer delta exceeds MaxPointerDelta");
            Contract.Requires(math.abs(stick.x) <= 1.001f && math.abs(stick.y) <= 1.001f, "stick components must be in [-1, 1]");
            Contract.Requires(deltaTime >= 0f && deltaTime <= MaxLookDeltaTime, "deltaTime must be in [0, MaxLookDeltaTime]");
            RequireMultipliers();
            float2 look = _pendingLook + (float2)pointerDelta * _sensitivityDegrees
                + (float2)stick * (_gamepadDegreesPerSecond * deltaTime);
            // Bound the queue (sol P1 r5 N8): finite inputs could still multiply/accumulate to ±Infinity and wedge
            // LateUpdate, which only clears the queue after Advance succeeds. Yaw is periodic, so the wrap is exact;
            // the pitch saturation is the documented Post, not a no-op (sol P1 r6 N10).
            look.x = math.fmod(look.x, 360f);
            look.y = math.clamp(look.y, -180f, 180f);
            Contract.Ensures(math.all(math.isfinite(look)), "pending look must stay finite");
            _pendingLook = look;
        }

        /// <summary>Pending look queued for the next LateUpdate, (yaw, pitch) in degrees.</summary>
        /// <remarks>Pre: none. Post: yaw in (-360, 360), pitch in [-180, 180]. Side effects: none.</remarks>
        internal float2 PendingLook => _pendingLook;

        /// <summary>Largest |mouse sensitivity| (degrees per pixel) the rig accepts.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none. 10°/px is ~80× the default; with MaxPointerDelta it
        /// keeps one call's contribution at 1e7°, far from float overflow (SOL P1 r6 N8-R6).</remarks>
        public const float MaxSensitivityDegrees = 10f;

        /// <summary>Largest gamepad look rate (degrees per second) the rig accepts.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none. Ten turns per second; 20× the default.</remarks>
        public const float MaxGamepadDegreesPerSecond = 3600f;

        /// <summary>Largest per-call mouse delta accepted by <see cref="AddLook"/>, in pixels.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none. Far above any real frame's mouse motion; keeps
        /// delta × sensitivity finite for any sensitivity below 1e30.</remarks>
        public const float MaxPointerDelta = 1e6f;

        /// <summary>Largest per-call deltaTime accepted by <see cref="AddLook"/>, in seconds.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none. A frame longer than this is a stall, not input.</remarks>
        public const float MaxLookDeltaTime = 1f;

        /// <summary>Switches between third and first person.</summary>
        /// <remarks>Pre: Configure called. Post: IsFirstPerson toggled; exactly one camera has the higher priority. Side effects: priorities.</remarks>
        public void ToggleView()
        {
            Contract.Requires(_configured, "Configure has not been called");
            SetFirstPerson(!IsFirstPerson);
        }

        // Serialized fields can be edited in the inspector at any time, so they are checked where used, not only once.
        private void RequireMultipliers()
        {
            Contract.Requires(math.isfinite(_sensitivityDegrees) && math.abs(_sensitivityDegrees) <= MaxSensitivityDegrees,
                "sensitivity must be finite and within ±MaxSensitivityDegrees");
            Contract.Requires(math.isfinite(_gamepadDegreesPerSecond) && _gamepadDegreesPerSecond >= 0f
                && _gamepadDegreesPerSecond <= MaxGamepadDegreesPerSecond,
                "gamepad look rate must be finite and within [0, MaxGamepadDegreesPerSecond]");
        }

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
            _worldUp.SetPositionAndRotation(position, _orbit.LevelRotation());
        }

        private void OnDestroy()
        {
            if (_worldUp != null)
            {
                Destroy(_worldUp.gameObject);
            }
        }
    }
}
