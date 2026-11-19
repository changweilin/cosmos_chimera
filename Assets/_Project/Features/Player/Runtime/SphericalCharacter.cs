using Cosmimera.Planet;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Player
{
    /// <summary>
    /// Runtime spatial traversal bounds pushed from active field utilities (Swim, Dive, Fly, Levitate) and water state.
    /// </summary>
    /// <remarks>
    /// Pre: speed scales and shallow depth finite and &gt;= 0; maxAltitudeM finite and &gt;= 0; seaLevelM finite.
    /// Post: immutable value used by <see cref="SphericalCharacter"/> in FixedUpdate without per-frame allocation.
    /// Side effects: none.
    /// </remarks>
    public readonly struct TraversalEnvelope
    {
        /// <summary>Neutral ground-only traversal envelope.</summary>
        /// <remarks>Pre: none. Post: all abilities inactive. Side effects: none.</remarks>
        public static TraversalEnvelope Default => default;

        /// <summary>Creates a validated spatial traversal envelope for <see cref="SphericalCharacter"/>.</summary>
        /// <remarks>
        /// Pre: all floats finite; maxAltitudeM, maxShallowDepthM, and speed scales &gt;= 0.
        /// Post: immutable envelope matching arguments.
        /// Side effects: none.
        /// </remarks>
        public TraversalEnvelope(
            bool aerialActive,
            float maxAltitudeM,
            float airSpeedScale,
            bool canSurfaceSwim,
            bool canDive,
            float seaLevelM,
            float maxShallowDepthM,
            float waterSurfaceSpeedScale,
            float underwaterSpeedScale,
            bool enforceWaterBoundary)
        {
            Cosmimera.Core.Contracts.Contract.Requires(
                math.isfinite(maxAltitudeM) && maxAltitudeM >= 0f,
                "maxAltitudeM must be finite and >= 0");
            Cosmimera.Core.Contracts.Contract.Requires(
                math.isfinite(airSpeedScale) && airSpeedScale >= 0f,
                "airSpeedScale must be finite and >= 0");
            Cosmimera.Core.Contracts.Contract.Requires(
                math.isfinite(seaLevelM),
                "seaLevelM must be finite");
            Cosmimera.Core.Contracts.Contract.Requires(
                math.isfinite(maxShallowDepthM) && maxShallowDepthM >= 0f,
                "maxShallowDepthM must be finite and >= 0");
            Cosmimera.Core.Contracts.Contract.Requires(
                math.isfinite(waterSurfaceSpeedScale) && waterSurfaceSpeedScale >= 0f,
                "waterSurfaceSpeedScale must be finite and >= 0");
            Cosmimera.Core.Contracts.Contract.Requires(
                math.isfinite(underwaterSpeedScale) && underwaterSpeedScale >= 0f,
                "underwaterSpeedScale must be finite and >= 0");

            AerialActive = aerialActive;
            MaxAltitudeM = maxAltitudeM;
            AirSpeedScale = airSpeedScale;
            CanSurfaceSwim = canSurfaceSwim;
            CanDive = canDive;
            SeaLevelM = seaLevelM;
            MaxShallowDepthM = maxShallowDepthM;
            WaterSurfaceSpeedScale = waterSurfaceSpeedScale;
            UnderwaterSpeedScale = underwaterSpeedScale;
            EnforceWaterBoundary = enforceWaterBoundary;
        }

        /// <summary>True when Fly or Levitate aerial motion is enabled.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool AerialActive { get; }

        /// <summary>Upper radial altitude limit above sea level in meters.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0f. Side effects: none.</remarks>
        public float MaxAltitudeM { get; }

        /// <summary>Horizontal/vertical air speed scale multiplier.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0f. Side effects: none.</remarks>
        public float AirSpeedScale { get; }

        /// <summary>True when the character can swim across water surface without sinking.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanSurfaceSwim { get; }

        /// <summary>True when the character can submerge deeper than <see cref="MaxShallowDepthM"/>.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CanDive { get; }

        /// <summary>Reference water surface altitude above planet radius (m).</summary>
        /// <remarks>Pre: none. Post: finite float. Side effects: none.</remarks>
        public float SeaLevelM { get; }

        /// <summary>Maximum permitted water depth (m) without Dive (typically 1 m).</summary>
        /// <remarks>Pre: none. Post: &gt;= 0f. Side effects: none.</remarks>
        public float MaxShallowDepthM { get; }

        /// <summary>Horizontal speed scale while swimming at the water surface.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0f. Side effects: none.</remarks>
        public float WaterSurfaceSpeedScale { get; }

        /// <summary>3D speed scale while submerged with Dive active.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0f. Side effects: none.</remarks>
        public float UnderwaterSpeedScale { get; }

        /// <summary>True when ocean surface/depth boundaries are enforced in FixedUpdate.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool EnforceWaterBoundary { get; }
    }

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

        private float _terrainMoveSpeedScale = 1f;
        private float _terrainSprintSpeedScale = 1f;
        private float _terrainGroundAccelScale = 1f;
        private float _jumpHeightScale = 1f;
        private TraversalEnvelope _traversal;
        private float _verticalInput;
        private float3 _lastSafeSurfaceDir;
        private bool _hasSafeSurfaceDir;

        /// <summary>Current radial altitude above <see cref="PlanetConfig.Radius"/> (m).</summary>
        /// <remarks>Pre: none. Post: finite altitude after Configure; 0 before Configure. Side effects: none.</remarks>
        public float AltitudeM => _configured ? _planet.Altitude(_body.position) : 0f;

        /// <summary>Updates active spatial traversal rules (Swim, Dive, Fly, Levitate, water boundaries).</summary>
        /// <remarks>Pre: none. Post: following FixedUpdate steps enforce <paramref name="envelope"/>. Side effects: mutates traversal state.</remarks>
        public void SetTraversalEnvelope(in TraversalEnvelope envelope) => _traversal = envelope;

        /// <summary>Sets vertical intent in [-1, 1] used during Fly, Levitate, or Dive movement.</summary>
        /// <remarks>Pre: <paramref name="vertical"/> is finite. Post: clamped vertical axis stored. Side effects: none.</remarks>
        public void SetVerticalInput(float vertical)
        {
            Cosmimera.Core.Contracts.Contract.Requires(math.isfinite(vertical), "vertical must be finite");
            _verticalInput = math.clamp(vertical, -1f, 1f);
        }

        /// <summary>Applies landform movement and friction multipliers (1 = neutral).</summary>
        /// <remarks>
        /// REQ: docs/design/planet-world.md#地貌行動影響.
        /// Pre: all multipliers finite and &gt; 0.
        /// Post: motor settings scale walk, sprint, and ground acceleration accordingly while remaining valid.
        /// Side effects: updates motor settings.
        /// </remarks>
        public void SetTerrainModifiers(float moveSpeedMultiplier, float sprintSpeedMultiplier, float groundAccelerationMultiplier)
        {
            Cosmimera.Core.Contracts.Contract.Requires(
                math.isfinite(moveSpeedMultiplier) && moveSpeedMultiplier > 0f &&
                math.isfinite(sprintSpeedMultiplier) && sprintSpeedMultiplier > 0f &&
                math.isfinite(groundAccelerationMultiplier) && groundAccelerationMultiplier > 0f,
                "terrain multipliers must be finite and > 0");
            _terrainMoveSpeedScale = moveSpeedMultiplier;
            _terrainSprintSpeedScale = sprintSpeedMultiplier;
            _terrainGroundAccelScale = groundAccelerationMultiplier;
            if (_configured)
            {
                ApplyMotorSettings();
            }
        }

        /// <summary>Scales jump height (e.g. by weather deposits on the ground).</summary>
        /// <remarks>
        /// REQ: docs/design/weather.md#地表累積.
        /// Pre: multiplier finite and in (0, 1].
        /// Post: the next jump rises to the serialized jump height × multiplier.
        /// Side effects: reapplies motor settings when configured.
        /// </remarks>
        public void SetJumpHeightMultiplier(float multiplier)
        {
            Cosmimera.Core.Contracts.Contract.Requires(math.isfinite(multiplier) && multiplier > 0f && multiplier <= 1f,
                "jump multiplier must be in (0, 1]");
            if (multiplier == _jumpHeightScale) return;
            _jumpHeightScale = multiplier;
            if (_configured)
            {
                ApplyMotorSettings();
            }
        }

        private void ApplyMotorSettings()
        {
            MotorSettings baseSettings = MotorSettings.Default;
            _settings = baseSettings;
            _settings.WalkSpeed = math.min(MotorSettings.MaxSpeed, _walkSpeed * _terrainMoveSpeedScale);
            _settings.SprintSpeed = math.clamp(_sprintSpeed * _terrainSprintSpeedScale, _settings.WalkSpeed, MotorSettings.MaxSpeed);
            _settings.GroundAcceleration = math.min(MotorSettings.MaxAcceleration, baseSettings.GroundAcceleration * _terrainGroundAccelScale);
            _settings.JumpHeight = _jumpHeight * _jumpHeightScale;
            _settings.MaxSlopeDegrees = _maxSlopeDegrees;
            Cosmimera.Core.Contracts.Contract.Requires(_settings.IsValid, "serialized motor tuning is out of range (MotorSettings.IsValid)");
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

            ApplyMotorSettings();

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

            if (_traversal.AerialActive)
            {
                StepAerial(position, up, ground);
                return;
            }

            if (_traversal.EnforceWaterBoundary)
            {
                float altitude = _planet.Altitude(position);
                float depth = _traversal.SeaLevelM - altitude;

                if (_traversal.CanDive && depth > 0.05f && (_verticalInput < 0f || depth > 0.35f))
                {
                    StepUnderwaterSwim(up, ground, altitude, depth);
                    return;
                }

                if (_traversal.CanSurfaceSwim && depth >= 0.2f)
                {
                    if (ground.Hit && altitude - ground.Distance >= _traversal.SeaLevelM - 0.15f && _verticalInput >= 0f)
                    {
                        _lastSafeSurfaceDir = up;
                        _hasSafeSurfaceDir = true;
                        StepGroundMotor(up, ground);
                        return;
                    }

                    StepSurfaceSwim(up, altitude, depth);
                    return;
                }

                if (depth <= _traversal.MaxShallowDepthM)
                {
                    _lastSafeSurfaceDir = up;
                    _hasSafeSurfaceDir = true;
                    StepGroundMotor(up, ground);
                    return;
                }

                ClampDeepWaterEntry(up);
                return;
            }

            StepGroundMotor(up, ground);
        }

        private void StepGroundMotor(float3 up, GroundContact ground)
        {
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

        private void StepAerial(float3 position, float3 up, GroundContact ground)
        {
            float altitude = _planet.Altitude(position);
            float baseSpeed = (_input.Sprint ? _sprintSpeed : _walkSpeed) * _traversal.AirSpeedScale;
            float climb = _verticalInput * baseSpeed;

            if (altitude >= _traversal.MaxAltitudeM && climb > 0f)
            {
                climb = 0f;
            }

            if (altitude > _traversal.MaxAltitudeM + 0.02f)
            {
                _body.position = _planet.ToWorld(up, _traversal.MaxAltitudeM);
            }

            float minFloor = _traversal.EnforceWaterBoundary && !_traversal.CanDive
                ? _traversal.SeaLevelM - _traversal.MaxShallowDepthM
                : float.NegativeInfinity;
            if (altitude <= minFloor && climb < 0f)
            {
                climb = 0f;
            }

            if (altitude < minFloor)
            {
                _body.position = _planet.ToWorld(up, minFloor);
            }

            if (ground.Hit && ground.Distance <= 0.03f && climb < 0f)
            {
                climb = 0f;
            }

            float3 moveDir = TangentMoveDirection(up);
            _state.Facing = math.lengthsq(moveDir) > 1e-6f
                ? RotateFacingToward(up, _state.Facing, moveDir, Time.fixedDeltaTime)
                : SphericalMotor.TangentOr(_state.Facing, up, PlanetFrame.FromUp(up).North);
            _state.Grounded = ground.Hit && ground.Distance <= 0.05f && climb <= 0f;
            _state.VerticalSpeed = climb;
            _state.Velocity = moveDir * baseSpeed + up * climb;
            _body.linearVelocity = _state.Velocity;
            ApplyRotation(up, _state.Facing, immediate: false);
        }

        private void StepSurfaceSwim(float3 up, float altitude, float depth)
        {
            float speed = (_input.Sprint ? _sprintSpeed : _walkSpeed) * _traversal.WaterSurfaceSpeedScale;
            float radialVel = math.clamp((_traversal.SeaLevelM - altitude) * 6f, -4f, 4f);
            float minAlt = _traversal.SeaLevelM - _traversal.MaxShallowDepthM;
            if (!_traversal.CanDive && altitude < minAlt)
            {
                _body.position = _planet.ToWorld(up, minAlt);
                radialVel = math.max(0f, radialVel);
            }

            float3 moveDir = TangentMoveDirection(up);
            _state.Facing = math.lengthsq(moveDir) > 1e-6f
                ? RotateFacingToward(up, _state.Facing, moveDir, Time.fixedDeltaTime)
                : SphericalMotor.TangentOr(_state.Facing, up, PlanetFrame.FromUp(up).North);
            _state.Grounded = false;
            _state.VerticalSpeed = radialVel;
            _state.Velocity = moveDir * speed + up * radialVel;
            _body.linearVelocity = _state.Velocity;
            _lastSafeSurfaceDir = up;
            _hasSafeSurfaceDir = true;
            ApplyRotation(up, _state.Facing, immediate: false);
        }

        private void StepUnderwaterSwim(float3 up, GroundContact ground, float altitude, float depth)
        {
            float speed = (_input.Sprint ? _sprintSpeed : _walkSpeed) * _traversal.UnderwaterSpeedScale;
            float climb = _verticalInput * speed;
            if (altitude >= _traversal.SeaLevelM && climb > 0f)
            {
                climb = 0f;
            }

            if (ground.Hit && ground.Distance <= 0.03f && climb < 0f)
            {
                climb = 0f;
            }

            float3 moveDir = TangentMoveDirection(up);
            _state.Facing = math.lengthsq(moveDir) > 1e-6f
                ? RotateFacingToward(up, _state.Facing, moveDir, Time.fixedDeltaTime)
                : SphericalMotor.TangentOr(_state.Facing, up, PlanetFrame.FromUp(up).North);
            _state.Grounded = ground.Hit && ground.Distance <= 0.05f && climb <= 0f;
            _state.VerticalSpeed = climb;
            _state.Velocity = moveDir * speed + up * climb;
            _body.linearVelocity = _state.Velocity;
            if (depth <= _traversal.MaxShallowDepthM)
            {
                _lastSafeSurfaceDir = up;
                _hasSafeSurfaceDir = true;
            }

            ApplyRotation(up, _state.Facing, immediate: false);
        }

        private void ClampDeepWaterEntry(float3 up)
        {
            float clampedAlt = _traversal.SeaLevelM - _traversal.MaxShallowDepthM;
            float3 pushTangent = _hasSafeSurfaceDir
                ? SphericalMotor.TangentOr(_lastSafeSurfaceDir - up, up, -_state.Facing)
                : -SphericalMotor.TangentOr(_state.Facing, up, PlanetFrame.FromUp(up).North);
            float3 safePos = _planet.ToWorld(up, clampedAlt) + pushTangent * 0.25f;
            _body.position = safePos;
            float3 safeUp = PlanetFrame.At(_planet, safePos).Up;
            _state.Facing = SphericalMotor.TangentOr(_state.Facing, safeUp, pushTangent);
            _state.Grounded = false;
            _state.VerticalSpeed = 0f;
            _state.Velocity = float3.zero;
            _body.linearVelocity = Vector3.zero;
            ApplyRotation(safeUp, _state.Facing, immediate: false);
        }

        private float3 TangentMoveDirection(float3 up)
        {
            float3 fallback = SphericalMotor.TangentOr(_state.Facing, up, PlanetFrame.FromUp(up).North);
            float3 camFwd = SphericalMotor.TangentOr(_input.ViewForward, up, fallback);
            float3 camRight = math.normalize(math.cross(up, camFwd));
            float3 desired = camFwd * _input.Move.y + camRight * _input.Move.x;
            return math.lengthsq(desired) > 1e-6f ? math.normalize(desired) : float3.zero;
        }

        private float3 RotateFacingToward(float3 up, float3 currentFacing, float3 desiredDir, float dt)
        {
            float3 cur = SphericalMotor.TangentOr(currentFacing, up, desiredDir);
            float3 tgt = SphericalMotor.TangentOr(desiredDir, up, cur);
            float maxRad = math.radians(_settings.TurnRateDegrees) * dt;
            float dot = math.clamp(math.dot(cur, tgt), -1f, 1f);
            float angle = math.acos(dot);
            if (angle <= maxRad || angle < 1e-4f)
            {
                return tgt;
            }

            float t = maxRad / angle;
            return SphericalMotor.TangentOr(math.lerp(cur, tgt, t), up, tgt);
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
