using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.Player
{
    /// <summary>Tuning for <see cref="SphericalMotor"/>. Speeds in m/s, accelerations in m/s², angles in degrees.</summary>
    /// <remarks>
    /// Pre: build through <see cref="Default"/> or set every field; all values finite; speeds, accelerations, gravity,
    ///      jump height and turn rate &gt; 0; 0 &lt; MaxSlopeDegrees &lt; 90; SnapDistance &gt;= 0.
    /// Post: n/a (plain data). Side effects: none.
    /// Numbers are P1 placeholders (walk 5 m/s comes from planet-world.md's lap-time table), not design final.
    /// </remarks>
    internal struct MotorSettings
    {
        /// <summary>Target planar speed without sprint.</summary>
        public float WalkSpeed;
        /// <summary>Target planar speed while sprinting.</summary>
        public float SprintSpeed;
        /// <summary>Planar acceleration towards the target while grounded.</summary>
        public float GroundAcceleration;
        /// <summary>Planar acceleration towards the target while airborne (air control).</summary>
        public float AirAcceleration;
        /// <summary>Acceleration towards the planet centre.</summary>
        public float Gravity;
        /// <summary>Apex height of a standing jump (m).</summary>
        public float JumpHeight;
        /// <summary>Steepest walkable ground, measured from local up.</summary>
        public float MaxSlopeDegrees;
        /// <summary>Maximum body turn speed (degrees per second).</summary>
        public float TurnRateDegrees;
        /// <summary>Ground within this distance below the feet counts as standing (absorbs collider chord sag).</summary>
        public float SnapDistance;
        /// <summary>Downward speed held while grounded so the body follows convex ground instead of skipping off it.</summary>
        public float StickSpeed;

        /// <summary>P1 placeholder tuning.</summary>
        /// <remarks>Pre: none. Post: <see cref="IsValid"/>. Side effects: none.</remarks>
        public static MotorSettings Default => new MotorSettings
        {
            WalkSpeed = 5f,
            SprintSpeed = 8f,
            GroundAcceleration = 40f,
            AirAcceleration = 8f,
            Gravity = 9.81f,
            JumpHeight = 1.2f,
            MaxSlopeDegrees = 50f,
            TurnRateDegrees = 720f,
            SnapDistance = 0.3f,
            StickSpeed = 2f,
        };

        /// <summary>True when every field satisfies the type precondition.</summary>
        /// <remarks>Pre: none. Post: see type Pre. Side effects: none.</remarks>
        public bool IsValid =>
            WalkSpeed > 0f && SprintSpeed > 0f && GroundAcceleration > 0f && AirAcceleration > 0f &&
            Gravity > 0f && JumpHeight > 0f && TurnRateDegrees > 0f && SnapDistance >= 0f && StickSpeed >= 0f &&
            MaxSlopeDegrees > 0f && MaxSlopeDegrees < 90f &&
            math.all(math.isfinite(new float4(WalkSpeed, SprintSpeed, GroundAcceleration, AirAcceleration))) &&
            math.all(math.isfinite(new float4(Gravity, JumpHeight, TurnRateDegrees, SnapDistance))) &&
            math.isfinite(StickSpeed);
    }

    /// <summary>One fixed step of player intent.</summary>
    /// <remarks>
    /// Pre: |Move| ≤ 1 (+1e-3); ViewForward finite (any length; projected onto the tangent plane, zero = "use facing").
    /// Post: n/a. Side effects: none.
    /// </remarks>
    internal struct MotorInput
    {
        /// <summary>x = right, y = forward, relative to <see cref="ViewForward"/>.</summary>
        public float2 Move;
        /// <summary>World-space camera heading.</summary>
        public float3 ViewForward;
        /// <summary>Jump requested this step (the glue latches presses between fixed steps).</summary>
        public bool Jump;
        /// <summary>Sprint held.</summary>
        public bool Sprint;
    }

    /// <summary>Result of the ground probe below the feet.</summary>
    /// <remarks>
    /// Pre: when Hit, Normal is unit length and Distance ≥ 0 (gap between feet and ground).
    /// Post: n/a. Side effects: none.
    /// </remarks>
    internal struct GroundContact
    {
        /// <summary>Something was found below the feet within the probe range.</summary>
        public bool Hit;
        /// <summary>Surface normal at the contact.</summary>
        public float3 Normal;
        /// <summary>Gap between the feet and the contact (m).</summary>
        public float Distance;
    }

    /// <summary>Motor state carried between fixed steps.</summary>
    /// <remarks>
    /// Pre: create through <see cref="SphericalMotor.Initial"/>.
    /// Post (after every <see cref="SphericalMotor.Step"/>): Up unit; Facing and PlanarVelocity ⟂ Up (±1e-4), Facing
    ///       unit; Velocity finite; VerticalSpeed == 0 while Grounded.
    /// Side effects: none.
    /// </remarks>
    internal struct MotorState
    {
        /// <summary>Velocity to hand to the body this step (world, m/s).</summary>
        public float3 Velocity;
        /// <summary>Controlled horizontal velocity, tangent to the sphere. Kept separately from Velocity because
        /// Velocity follows the ground slope: re-deriving the tangent part from it loses cos(slope) every step
        /// (measured: 2.5 m/s instead of 5 m/s on 25° slopes).</summary>
        public float3 PlanarVelocity;
        /// <summary>Speed along Up while airborne (positive = rising); the glue may overwrite it with the measured value.</summary>
        public float VerticalSpeed;
        /// <summary>Body forward, tangent to the sphere. Carried (not rebuilt from East/North) so it survives the poles.</summary>
        public float3 Facing;
        /// <summary>Local up at the end of the previous step; the reference that PlanarVelocity and Facing are transported from.</summary>
        public float3 Up;
        /// <summary>Standing on walkable ground after this step.</summary>
        public bool Grounded;
    }

    /// <summary>Pure character kinematics on a sphere: walk, sprint, jump, slope limit and heading transport.</summary>
    /// <remarks>
    /// Rules layer only — the Rigidbody glue (<c>SphericalCharacter</c>) owns collision and integration. Every
    /// direction is relative to the caller-supplied local up, never world Y. REQ: CLAUDE.md#硬性規則.
    /// </remarks>
    internal static class SphericalMotor
    {
        // A heading within ~1e-3 rad of ±up is noise (PlanetFrame.ProjectOnTangent's degeneracy threshold).
        private const float DegenerateSq = 1e-6f;

        /// <summary>State at rest at local up <paramref name="up"/> facing <paramref name="facing"/>.</summary>
        /// <remarks>
        /// Pre: up unit (±1e-3); facing finite and not parallel to up.
        /// Post: result satisfies the MotorState invariant; Velocity == 0; Grounded == false.
        /// Side effects: none.
        /// </remarks>
        public static MotorState Initial(float3 up, float3 facing)
        {
            Contract.Requires(math.abs(math.lengthsq(up) - 1f) < 2e-3f, "up must be unit length");
            Contract.Requires(math.all(math.isfinite(facing)), "facing must be finite");
            up = math.normalize(up);
            float3 tangent = facing - up * math.dot(facing, up);
            Contract.Requires(math.lengthsq(tangent) > DegenerateSq * math.max(math.lengthsq(facing), 1e-30f),
                "facing must not be parallel to up");
            return new MotorState { Up = up, Facing = math.normalize(tangent), Grounded = false };
        }

        /// <summary>Advances <paramref name="prev"/> by <paramref name="dt"/> seconds at the new local up <paramref name="up"/>.</summary>
        /// <remarks>
        /// Pre: settings.IsValid; prev satisfies the MotorState invariant; up unit (±1e-3) and within 90° of prev.Up;
        ///      0 &lt; dt ≤ 0.1; input satisfies the MotorInput precondition; ground satisfies GroundContact's.
        /// Post: MotorState invariant holds; result.Up == normalize(up);
        ///       Grounded ⇔ ground.Hit ∧ Distance ≤ SnapDistance ∧ slope ≤ MaxSlope ∧ prev.VerticalSpeed ≤ 0.5 ∧ no jump;
        ///       |PlanarVelocity| ≤ max(|prev.PlanarVelocity|, SprintSpeed); while grounded, Velocity runs along the
        ///       ground plane at |PlanarVelocity| plus StickSpeed into the ground;
        ///       a jump starts only from Grounded and sets the vertical speed to sqrt(2·Gravity·JumpHeight).
        /// Side effects: none (pure).
        /// </remarks>
        public static MotorState Step(in MotorSettings settings, in MotorState prev, in MotorInput input,
            in GroundContact ground, float3 up, float dt)
        {
            Contract.Requires(settings.IsValid, "invalid MotorSettings");
            Contract.Requires(dt > 0f && dt <= 0.1f, "dt must be in (0, 0.1]");
            Contract.Requires(math.abs(math.lengthsq(up) - 1f) < 2e-3f, "up must be unit length");
            Contract.Requires(math.abs(math.lengthsq(prev.Up) - 1f) < 2e-3f, "prev is uninitialised (use Initial)");
            Contract.Requires(math.dot(up, prev.Up) > 0f, "up moved more than 90° in one step");
            Contract.Requires(math.lengthsq(input.Move) <= 1.002f, "|Move| must be ≤ 1");
            Contract.Requires(math.all(math.isfinite(input.ViewForward)), "ViewForward must be finite");
            Contract.Requires(!ground.Hit || (math.abs(math.lengthsq(ground.Normal) - 1f) < 2e-3f && ground.Distance >= 0f),
                "ground normal must be unit and distance ≥ 0");

            up = math.normalize(up);

            // Parallel-transport planar velocity and facing by the minimal rotation prev.Up → up. Re-projecting alone
            // would shave cos(Δθ) off the speed every step; rebuilding from East/North would spin the view at the poles.
            quaternion transport = FromTo(prev.Up, up);
            float3 vTan = math.mul(transport, prev.PlanarVelocity);
            vTan -= up * math.dot(vTan, up);
            float3 facing = TangentOr(math.mul(transport, prev.Facing), up, prev.Facing);
            float vUp = prev.VerticalSpeed;

            float3 viewForward = TangentOr(input.ViewForward, up, facing);
            // Unity is left-handed (Vector3.right == cross(up, forward) for up=Y, forward=Z), so screen-right is
            // cross(up, forward). Do not derive it from PlanetFrame.East: that frame is right-handed by formula.
            float3 viewRight = math.cross(up, viewForward);
            float3 wishDir = viewRight * input.Move.x + viewForward * input.Move.y;
            float wishSpeed = input.Sprint ? settings.SprintSpeed : settings.WalkSpeed;
            float3 wishVelocity = wishDir * wishSpeed;

            bool slopeOk = ground.Hit && math.dot(ground.Normal, up) >= math.cos(math.radians(settings.MaxSlopeDegrees));
            // Still rising means a jump is in progress, even if the probe still sees ground.
            bool grounded = slopeOk && ground.Distance <= settings.SnapDistance && vUp <= 0.5f;

            float accel = grounded ? settings.GroundAcceleration : settings.AirAcceleration;
            vTan = MoveTowards(vTan, wishVelocity, accel * dt);

            if (ground.Hit && !slopeOk && ground.Distance <= settings.SnapDistance)
            {
                // Too steep: drop any push into the wall so input cannot climb it; gravity below slides the body down.
                float3 wallOut = TangentOr(ground.Normal, up, float3.zero);
                float into = math.dot(vTan, wallOut);
                if (into < 0f)
                {
                    vTan -= wallOut * into;
                }
            }

            float3 velocity;
            if (grounded && input.Jump)
            {
                vUp = math.sqrt(2f * settings.Gravity * settings.JumpHeight);
                velocity = vTan + up * vUp;
                grounded = false;
            }
            else if (grounded)
            {
                // Follow the ground plane at the same speed so slopes neither slow the body nor launch it.
                float3 along = vTan - ground.Normal * math.dot(vTan, ground.Normal);
                float alongLenSq = math.lengthsq(along);
                if (alongLenSq > 1e-12f)
                {
                    along *= math.length(vTan) / math.sqrt(alongLenSq);
                }

                velocity = along - ground.Normal * settings.StickSpeed;
                vUp = 0f;
            }
            else
            {
                vUp -= settings.Gravity * dt;
                velocity = vTan + up * vUp;
            }

            if (math.lengthsq(wishDir) > 1e-6f)
            {
                facing = RotateTowards(facing, math.normalize(wishDir), up, math.radians(settings.TurnRateDegrees) * dt);
            }

            return new MotorState
            {
                Velocity = velocity, PlanarVelocity = vTan, VerticalSpeed = vUp, Facing = facing, Up = up, Grounded = grounded,
            };
        }

        /// <summary>Shortest-arc rotation taking unit <paramref name="from"/> to unit <paramref name="to"/>.</summary>
        /// <remarks>Pre: from, to unit; dot(from, to) &gt; −1. Post: mul(result, from) ≈ to. Side effects: none.</remarks>
        internal static quaternion FromTo(float3 from, float3 to)
        {
            // (cross, 1 + dot) normalised is the half-angle quaternion; stable here because callers keep dot > 0.
            float4 q = new float4(math.cross(from, to), 1f + math.dot(from, to));
            return new quaternion(math.normalize(q));
        }

        /// <summary>Unit tangent of <paramref name="v"/> at <paramref name="up"/>, or <paramref name="fallback"/> if degenerate.</summary>
        /// <remarks>Pre: up unit. Post: result ⟂ up and unit, or == fallback. Side effects: none.</remarks>
        internal static float3 TangentOr(float3 v, float3 up, float3 fallback)
        {
            float3 t = v - up * math.dot(v, up);
            float lenSq = math.lengthsq(t);
            if (lenSq <= DegenerateSq * math.max(math.lengthsq(v), 1e-30f) || lenSq < 1e-30f)
            {
                return fallback;
            }

            t = math.normalize(t);
            return math.normalize(t - up * math.dot(t, up));
        }

        private static float3 MoveTowards(float3 current, float3 target, float maxDelta)
        {
            float3 delta = target - current;
            float distSq = math.lengthsq(delta);
            if (distSq <= maxDelta * maxDelta)
            {
                return target;
            }

            return current + delta * (maxDelta / math.sqrt(distSq));
        }

        /// <summary>Rotates tangent <paramref name="from"/> about <paramref name="up"/> towards <paramref name="to"/> by at most <paramref name="maxRadians"/>.</summary>
        /// <remarks>Pre: from, to unit tangents at up; maxRadians ≥ 0. Post: unit tangent. Side effects: none.</remarks>
        internal static float3 RotateTowards(float3 from, float3 to, float3 up, float maxRadians)
        {
            float angle = math.atan2(math.dot(math.cross(from, to), up), math.dot(from, to));
            float step = math.clamp(angle, -maxRadians, maxRadians);
            float3 rotated = math.mul(quaternion.AxisAngle(up, step), from);
            return TangentOr(rotated, up, from);
        }
    }
}
