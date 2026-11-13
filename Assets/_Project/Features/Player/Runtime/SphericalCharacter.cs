using Cosmimera.Planet;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Player
{
    /// <summary>Rigidbody glue that walks a capsule over the planet using <see cref="SphericalMotor"/>.</summary>
    /// <remarks>
    /// Pre: <see cref="Configure"/> is called once before the first FixedUpdate; the GameObject has a Rigidbody and a
    ///      CapsuleCollider whose axis is the transform's local Y.
    /// Post: after each FixedUpdate the body's up equals the local planet up and its forward equals the motor facing.
    /// Side effects: owns Rigidbody velocity and rotation; reads physics via one SphereCast per FixedUpdate.
    /// Gravity is integrated by the motor rather than a separate PlanetGravity force: the motor overwrites velocity
    /// every step, so an external force would be applied and then discarded.
    /// </remarks>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class SphericalCharacter : MonoBehaviour
    {
        [SerializeField] private float _walkSpeed = 5f;
        [SerializeField] private float _sprintSpeed = 8f;
        [SerializeField] private float _jumpHeight = 1.2f;
        [SerializeField] private float _maxSlopeDegrees = 50f;
        [SerializeField] private LayerMask _groundMask = ~0;

        private static PhysicsMaterial s_frictionless;

        private static PhysicsMaterial Frictionless => s_frictionless != null
            ? s_frictionless
            : s_frictionless = new PhysicsMaterial("SphericalCharacterFrictionless")
            {
                staticFriction = 0f,
                dynamicFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };

        private Rigidbody _body;
        private CapsuleCollider _capsule;
        private PlanetConfig _planet;
        private MotorSettings _settings;
        private MotorState _state;
        private MotorInput _input;
        private JumpBuffer _jump;
        private bool _configured;

        /// <summary>True while standing on walkable ground (updated each FixedUpdate).</summary>
        /// <remarks>Pre: none. Post: false before the first FixedUpdate. Side effects: none.</remarks>
        public bool IsGrounded => _state.Grounded;

        /// <summary>Current velocity in world space (m/s).</summary>
        /// <remarks>Pre: none. Post: finite. Side effects: none.</remarks>
        public Vector3 Velocity => _state.Velocity;

        /// <summary>Gravity acceleration (m/s²) the motor applies; loose objects (PlanetGravity) fall at the same rate.</summary>
        /// <remarks>Pre: none. Post: &gt; 0 (the motor default before Configure). Side effects: none.</remarks>
        public float Gravity => _configured ? _settings.Gravity : MotorSettings.Default.Gravity;

        /// <summary>Current body forward, tangent to the planet.</summary>
        /// <remarks>Pre: Configure called. Post: unit, ⟂ local up. Side effects: none.</remarks>
        public Vector3 Facing
        {
            get
            {
                Cosmimera.Core.Contracts.Contract.Requires(_configured, "Configure has not been called");
                return _state.Facing;
            }
        }

        /// <summary>Binds the character to <paramref name="planet"/> and resets motion, keeping the current position.</summary>
        /// <remarks>
        /// Pre: planet.IsValid; the transform is not at the planet centre.
        /// Post: Rigidbody has useGravity == false, rotation frozen (the motor rotates it), interpolation on; capsule is
        ///       frictionless; body up ==
        ///       local up; motor state at rest facing the current forward projected on the tangent plane.
        /// Side effects: mutates Rigidbody settings and transform rotation.
        /// </remarks>
        public void Configure(in PlanetConfig planet)
        {
            Cosmimera.Core.Contracts.Contract.Requires(planet.IsValid, "planet must be valid");
            _planet = planet;
            _body = GetComponent<Rigidbody>();
            _capsule = GetComponent<CapsuleCollider>();
            _body.useGravity = false;
            _body.freezeRotation = true;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // The motor presses the body into the ground (StickSpeed) and sets velocity itself; with default friction
            // that press becomes drag and halves walking speed (measured 9.7 m instead of 25 m in 5 s).
            _capsule.sharedMaterial = Frictionless;

            _settings = MotorSettings.Default;
            _settings.WalkSpeed = _walkSpeed;
            _settings.SprintSpeed = _sprintSpeed;
            _settings.JumpHeight = _jumpHeight;
            _settings.MaxSlopeDegrees = _maxSlopeDegrees;
            Cosmimera.Core.Contracts.Contract.Requires(_settings.IsValid, "serialized motor tuning is out of range (MotorSettings.IsValid)");

            float3 up = PlanetFrame.At(planet, transform.position).Up;
            float3 facing = SphericalMotor.TangentOr(transform.forward, up, PlanetFrame.FromUp(up).North);
            _state = SphericalMotor.Initial(up, facing);
            ApplyRotation(up, _state.Facing, immediate: true);
            _configured = true;
        }

        /// <summary>Sets the intent used by the following fixed steps.</summary>
        /// <remarks>
        /// Pre: move and viewForward finite (move longer than 1 is normalised; viewForward may have any length).
        /// Post: jumpPressed is buffered for JumpBuffer.WindowSeconds or until a jump starts, so a press in a frame
        ///       without a fixed step (or just before landing) is kept.
        /// Side effects: mutates pending input.
        /// </remarks>
        public void SetInput(Vector2 move, Vector3 viewForward, bool sprint, bool jumpPressed)
        {
            Cosmimera.Core.Contracts.Contract.Requires(math.all(math.isfinite((float2)move)) && math.all(math.isfinite((float3)viewForward)),
                "move and viewForward must be finite");
            _input.Move = math.lengthsq((float2)move) > 1f ? math.normalize((float2)move) : (float2)move;
            _input.ViewForward = viewForward;
            _input.Sprint = sprint;
            if (jumpPressed)
            {
                _jump.Press(Time.time);
            }
        }

        private void FixedUpdate()
        {
            if (!_configured)
            {
                return;
            }

            float3 position = _body.position;
            float3 up = PlanetFrame.At(_planet, position).Up;
            GroundContact ground = ProbeGround(position, up);

            if (!_state.Grounded)
            {
                // Feed back what physics actually did vertically: a ceiling stops a jump, a steep slope holds the body,
                // and without this the motor's own integration would keep accelerating a body that is not falling.
                // Clamped into the motor's state domain (sol r4 N4); only a broken physics step gets near 1e4 m/s.
                _state.VerticalSpeed = math.clamp(math.dot((float3)_body.linearVelocity, _state.Up),
                    -SphericalMotor.MaxStateSpeed, SphericalMotor.MaxStateSpeed);
            }

            // Expiry is decided before the step, so a stale press can never reach the motor (sol P1-05 F1).
            _input.Jump = _jump.IsPending(Time.time);
            _state = SphericalMotor.Step(_settings, _state, _input, ground, up, Time.fixedDeltaTime);
            if (_state.JumpStarted)
            {
                _jump.Consume();
            }

            _body.linearVelocity = _state.Velocity;
            ApplyRotation(up, _state.Facing, immediate: false);
        }

        private GroundContact ProbeGround(float3 position, float3 up)
        {
            float radius = _capsule.radius * 0.95f;
            float halfHeight = math.max(_capsule.height * 0.5f, _capsule.radius);
            // Start inside the lower hemisphere so ground already touching the capsule is still reported.
            float3 bottomSphere = position + up * (_capsule.center.y - halfHeight + _capsule.radius);
            float3 origin = bottomSphere + up * 0.1f;
            float maxDistance = 0.1f + _settings.SnapDistance + 0.5f;
            if (!Physics.SphereCast(origin, radius, -up, out RaycastHit hit, maxDistance, _groundMask, QueryTriggerInteraction.Ignore))
            {
                return default;
            }

            return new GroundContact
            {
                Hit = true,
                Normal = math.normalize((float3)hit.normal),
                // Gap between the capsule's bottom and the ground; the 0.05 radius shrink sinks the probe sphere.
                Distance = math.max(0f, hit.distance - 0.1f - (_capsule.radius - radius)),
            };
        }

        private void ApplyRotation(float3 up, float3 facing, bool immediate)
        {
            Quaternion rotation = quaternion.LookRotationSafe(facing, up);
            if (immediate)
            {
                transform.rotation = rotation;
                _body.rotation = rotation;
            }
            else
            {
                // Direct assignment, not MoveRotation: the body is dynamic with frozen rotation, and interpolation
                // already smooths the rendered transform between fixed steps.
                _body.rotation = rotation;
            }
        }
    }
}
