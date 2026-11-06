using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.Player
{
    /// <summary>Tuning for <see cref="SphericalMotor"/>. Speeds in m/s, accelerations in m/s², angles in degrees.</summary>
    /// <remarks>
    /// Pre: build through <see cref="Default"/> or set every field so that <see cref="IsValid"/> holds: every value
    ///      within its Max* bound; speeds, accelerations, gravity, jump height and turn rate &gt; 0;
    ///      WalkSpeed ≤ SprintSpeed; 0 &lt; MaxSlopeDegrees &lt; 90; SnapDistance, StickSpeed ≥ 0.
    /// Post: n/a (plain data). Side effects: none.
    /// Numbers are P1 placeholders (walk 5 m/s comes from planet-world.md's lap-time table), not design final.
    /// </remarks>
    internal struct MotorSettings
    {
        /// <summary>Target planar speed without sprint.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float WalkSpeed;
        /// <summary>Target planar speed while sprinting.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float SprintSpeed;
        /// <summary>Planar acceleration towards the target while grounded.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float GroundAcceleration;
        /// <summary>Planar acceleration towards the target while airborne (air control).</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float AirAcceleration;
        /// <summary>Acceleration towards the planet centre.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float Gravity;
        /// <summary>Apex height of a standing jump (m).</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float JumpHeight;
        /// <summary>Steepest walkable ground, measured from local up.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float MaxSlopeDegrees;
        /// <summary>Maximum body turn speed (degrees per second).</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float TurnRateDegrees;
        /// <summary>Ground within this distance below the feet counts as standing (absorbs collider chord sag).</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float SnapDistance;
        /// <summary>Downward speed held while grounded so the body follows convex ground instead of skipping off it.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float StickSpeed;

        /// <summary>Largest accepted speed (Walk, Sprint, Stick), m/s.</summary>
        /// <remarks>
        /// Pre: none. Post: constant. Side effects: none.
        /// REQ: docs/reviews/P1-05-sol.md#F4 — Gravity = float.MaxValue passed IsValid and 2·g·h overflowed to a NaN
        /// velocity. The Max* bounds are generous (20× the defaults or more) and keep every product in Step finite.
        /// </remarks>
        public const float MaxSpeed = 100f;
        /// <summary>Largest accepted acceleration (Ground, Air, Gravity), m/s².</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float MaxAcceleration = 1000f;
        /// <summary>Largest accepted JumpHeight, m.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float MaxJumpHeight = 100f;
        /// <summary>Largest accepted TurnRateDegrees, degrees per second.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float MaxTurnRateDegrees = 36000f;
        /// <summary>Largest accepted SnapDistance, m.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float MaxSnapDistance = 10f;

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
        /// <remarks>Pre: none. Post: see type Pre (NaN fails every comparison, so it is rejected). Side effects: none.</remarks>
        public bool IsValid =>
            InRange(WalkSpeed, MaxSpeed) && InRange(SprintSpeed, MaxSpeed) && WalkSpeed <= SprintSpeed &&
            InRange(GroundAcceleration, MaxAcceleration) && InRange(AirAcceleration, MaxAcceleration) &&
            InRange(Gravity, MaxAcceleration) && InRange(JumpHeight, MaxJumpHeight) &&
            InRange(TurnRateDegrees, MaxTurnRateDegrees) &&
            SnapDistance >= 0f && SnapDistance <= MaxSnapDistance && StickSpeed >= 0f && StickSpeed <= MaxSpeed &&
            MaxSlopeDegrees > 0f && MaxSlopeDegrees < 90f;

        private static bool InRange(float value, float max) => value > 0f && value <= max;
    }

    /// <summary>One fixed step of player intent.</summary>
    /// <remarks>
    /// Pre: |Move| ≤ 1 (+1e-3); ViewForward finite (any length, scale-invariant; projected onto the tangent plane;
    ///      zero or within 1e-3 rad of ±up = "use facing").
    /// Post: n/a. Side effects: none.
    /// </remarks>
    internal struct MotorInput
    {
        /// <summary>x = right, y = forward, relative to <see cref="ViewForward"/>.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float2 Move;
        /// <summary>World-space camera heading.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float3 ViewForward;
        /// <summary>Jump requested this step (the glue latches presses between fixed steps).</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public bool Jump;
        /// <summary>Sprint held.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
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
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public bool Hit;
        /// <summary>Surface normal at the contact.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float3 Normal;
        /// <summary>Gap between the feet and the contact (m).</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float Distance;
    }

    /// <summary>Motor state carried between fixed steps.</summary>
    /// <remarks>
    /// Pre: create through <see cref="SphericalMotor.Initial"/>.
    /// Post (after every <see cref="SphericalMotor.Step"/>): Up unit; Facing unit and ⟂ Up (±1e-4); PlanarVelocity ⟂ Up
    ///       within 1e-4·max(1, |PlanarVelocity|) — relative, because at 1e4 m/s float spacing alone leaves ~1e-3 m/s
    ///       of dot residual (sol P1 r5 N7); Velocity finite; VerticalSpeed == 0 while Grounded; JumpStarted ⇒ ¬Grounded;
    ///       lengthsq(PlanarVelocity) ≤ MaxStateSpeed², |VerticalSpeed| ≤ MaxStateSpeed, |Velocity| ≤ 2·MaxStateSpeed
    ///       (so every Step output is accepted by the next Step's Pre).
    /// Side effects: none.
    /// </remarks>
    internal struct MotorState
    {
        /// <summary>Velocity to hand to the body this step (world, m/s).</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float3 Velocity;
        /// <summary>Controlled horizontal velocity, tangent to the sphere. Kept separately from Velocity because
        /// Velocity follows the ground slope: re-deriving the tangent part from it loses cos(slope) every step
        /// (measured: 2.5 m/s instead of 5 m/s on 25° slopes).</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float3 PlanarVelocity;
        /// <summary>Speed along Up while airborne (positive = rising); the glue may overwrite it with the measured value.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float VerticalSpeed;
        /// <summary>Body forward, tangent to the sphere. Carried (not rebuilt from East/North) so it survives the poles.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float3 Facing;
        /// <summary>Local up at the end of the previous step; the reference that PlanarVelocity and Facing are transported from.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float3 Up;
        /// <summary>Standing on walkable ground after this step.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public bool Grounded;
        /// <summary>A jump began during this step. The glue consumes its jump buffer on this, not on "left the ground",
        /// which also fires when walking off a ledge and misses a jump on the landing step (sol P1-05 F1).</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public bool JumpStarted;
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

        /// <summary>Largest planar or vertical speed a MotorState may carry, m/s.</summary>
        /// <remarks>
        /// Pre: none. Post: constant. Side effects: none.
        /// REQ: docs/reviews/P1-review-r4-sol.md#N4 — a finite tangent PlanarVelocity of 2e19 overflowed lengthsq and the
        /// length restore produced NaN. 1e4 m/s is 100× MaxSpeed and ~1000× any fall on R ≤ 8000 m, while every
        /// squared length in Step stays below 1e9. Falling speed is clamped here (an engine-free terminal velocity).
        /// </remarks>
        public const float MaxStateSpeed = 1e4f;

        /// <summary>State at rest at local up <paramref name="up"/> facing <paramref name="facing"/>.</summary>
        /// <remarks>
        /// Pre: up unit (±1e-3); facing finite (any length), not zero and not within 1e-3 rad of ±up.
        /// Post: result satisfies the MotorState invariant; Velocity == 0; Grounded == false.
        /// Side effects: none.
        /// </remarks>
        public static MotorState Initial(float3 up, float3 facing)
        {
            Contract.Requires(math.abs(math.lengthsq(up) - 1f) < 2e-3f, "up must be unit length");
            Contract.Requires(math.all(math.isfinite(facing)), "facing must be finite");
            up = math.normalize(up);
            float3 tangent = TangentOr(facing, up, float3.zero);
            Contract.Requires(math.any(tangent != 0f), "facing must not be zero or parallel to up");
            return new MotorState { Up = up, Facing = tangent, Grounded = false };
        }

        /// <summary>Advances <paramref name="prev"/> by <paramref name="dt"/> seconds at the new local up <paramref name="up"/>.</summary>
        /// <remarks>
        /// Pre: settings.IsValid; prev satisfies the MotorState invariant (checked with 1e-3 slack: Facing unit and
        ///      ⟂ prev.Up, PlanarVelocity ⟂ prev.Up relative to its length, all fields finite, speeds within
        ///      MaxStateSpeed bounds, VerticalSpeed == 0 while Grounded, ¬(JumpStarted ∧ Grounded)); up unit (±1e-3) and
        ///      strictly less than 90° from prev.Up;
        ///      0 &lt; dt ≤ 0.1; input satisfies the MotorInput precondition; ground satisfies GroundContact's.
        /// Post: MotorState invariant holds; result.Up == normalize(up);
        ///       Grounded ⇔ ground.Hit ∧ Distance ≤ SnapDistance ∧ slope ≤ MaxSlope ∧ prev.VerticalSpeed ≤ 0.5 ∧ no jump;
        ///       JumpStarted ⇔ a jump started this step;
        ///       |PlanarVelocity| ≤ min(MaxStateSpeed, max(|prev.PlanarVelocity|, SprintSpeed) (+ rounding)); transport alone preserves
        ///       |PlanarVelocity| except at the domain edge, where lengthsq ≥ MaxStateSpeed²·(1−2e-6) is rescaled to
        ///       MaxStateSpeed·(1−1e-6) (≤ ~0.011 m/s at 1e4 m/s, sol P1 r6); while grounded, Velocity runs along the
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
            Contract.Requires(math.abs(math.lengthsq(prev.Facing) - 1f) < 2e-3f && math.abs(math.dot(prev.Facing, prev.Up)) <= 1e-3f,
                "prev.Facing must be a unit tangent");
            Contract.Requires(math.all(math.isfinite(prev.PlanarVelocity))
                && math.abs(math.dot(prev.PlanarVelocity, prev.Up)) <= 1e-3f * math.max(1f, math.length(prev.PlanarVelocity)),
                "prev.PlanarVelocity must be a finite tangent");
            Contract.Requires(math.all(math.isfinite(prev.Velocity)) && math.isfinite(prev.VerticalSpeed),
                "prev velocities must be finite");
            // Component-wise first so lengthsq below cannot overflow; then the Euclidean bounds of the type invariant.
            Contract.Requires(math.cmax(math.abs(prev.PlanarVelocity)) <= MaxStateSpeed
                && math.lengthsq(prev.PlanarVelocity) <= MaxStateSpeed * MaxStateSpeed
                && math.abs(prev.VerticalSpeed) <= MaxStateSpeed
                && math.cmax(math.abs(prev.Velocity)) <= 2f * MaxStateSpeed
                && math.lengthsq(prev.Velocity) <= 4f * MaxStateSpeed * MaxStateSpeed,
                "prev speeds exceed MaxStateSpeed");
            Contract.Requires(!prev.Grounded || (prev.VerticalSpeed == 0f && !prev.JumpStarted),
                "prev is Grounded but carries vertical speed or a jump start");
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
            // A rotation preserves length; restoring it stops rounding from compounding while nothing else corrects
            // the speed (sol P1-05 F5: +0.28 % over 1e5 free-gliding steps).
            float transportedLen = math.length(vTan);
            if (transportedLen > 0f)
            {
                vTan *= math.length(prev.PlanarVelocity) / transportedLen;
            }

            float3 facing = TangentOr(math.mul(transport, prev.Facing), up, prev.Facing);
            float vUp = prev.VerticalSpeed;

            float3 viewForward = TangentOr(input.ViewForward, up, facing);
            // Unity is left-handed (Vector3.right == cross(up, forward) for up=Y, forward=Z), so screen-right is
            // cross(up, forward). Do not derive it from PlanetFrame.East: that frame is right-handed by formula.
            float3 viewRight = math.cross(up, viewForward);
            // The Pre tolerates |Move| up to ~1.001 (stick calibration); limit it to the unit disc so that tolerance
            // cannot push speed past SprintSpeed and break the speed Post (sol P1 r8 N15: +0.09 m/s at 100 m/s).
            float2 move = math.lengthsq(input.Move) > 1f ? math.normalize(input.Move) : input.Move;
            float3 wishDir = viewRight * move.x + viewForward * move.y;
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

            // Close the speed domain over Step (sol P1 r5 N6): restoring the transported length can round
            // lengthsq(vTan) a few ulp above MaxStateSpeed², and the next Step's Pre would then reject this output.
            // The 1e-6 relative headroom is ~10 float ulp, enough for lengthsq of the rescaled vector to stay below.
            if (math.lengthsq(vTan) > MaxStateSpeed * MaxStateSpeed * (1f - 2e-6f))
            {
                vTan *= MaxStateSpeed * (1f - 1e-6f) / math.length(vTan);
            }

            float3 velocity;
            bool jumpStarted = false;
            if (grounded && input.Jump)
            {
                jumpStarted = true;
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
                vUp = math.max(vUp - settings.Gravity * dt, -MaxStateSpeed);
                velocity = vTan + up * vUp;
            }

            if (math.lengthsq(wishDir) > 1e-6f)
            {
                facing = RotateTowards(facing, math.normalize(wishDir), up, math.radians(settings.TurnRateDegrees) * dt);
            }

            return new MotorState
            {
                Velocity = velocity, PlanarVelocity = vTan, VerticalSpeed = vUp, Facing = facing, Up = up, Grounded = grounded,
                JumpStarted = jumpStarted,
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
        /// <remarks>
        /// Pre: up unit; v finite.
        /// Post: result ⟂ up (≤ 1e-6) and unit, or == fallback when v is zero or within 1e-3 rad of ±up;
        ///       independent of |v|.
        /// Side effects: none.
        /// </remarks>
        internal static float3 TangentOr(float3 v, float3 up, float3 fallback)
        {
            // Scale by the largest component first: lengthsq of 1e20 overflows and of 1e-20 underflows, which made
            // a legal ViewForward turn 90° (sol P1-05 F3). The degeneracy test is then an angle, as in
            // PlanetFrame.ProjectOnTangent (its helper is internal to Planet, hence a copy of the policy).
            float m = math.cmax(math.abs(v));
            if (!(m > 0f))
            {
                return fallback;
            }

            float3 d = math.normalize(v / m);
            float3 t = d - up * math.dot(d, up);
            if (math.lengthsq(t) <= DegenerateSq)
            {
                return fallback;
            }

            // Second Gram-Schmidt pass: near the 1e-3 rad cut-off one pass left |dot| ≈ 3.5e-4 (sol F3 repro B).
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
