using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.Player
{
    /// <summary>Camera heading (tangent) + pitch on a sphere, transported with local up so it never flips at the poles.</summary>
    /// <remarks>
    /// Pre: create through <see cref="Create"/>; <c>default</c> is invalid.
    /// Post (after every call): Up unit; Heading unit and ⟂ Up (±1e-4); MinPitch ≤ PitchDegrees ≤ MaxPitch.
    /// Side effects: none (value type, pure methods).
    /// Yaw is stored as a tangent vector, not an angle from North: an angle would need East/North, which rotate a full
    /// turn while circling a pole within a few metres (PlanetFrame remarks).
    /// </remarks>
    internal struct ViewOrbit
    {
        /// <summary>Lowest allowed pitch (looking down), degrees.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float MinPitch = -80f;

        /// <summary>Highest allowed pitch (looking up), degrees.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float MaxPitch = 80f;

        /// <summary>Unit tangent the camera faces horizontally.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float3 Heading;

        /// <summary>Local up the heading is tangent to.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float3 Up;

        /// <summary>Positive looks up, degrees.</summary>
        /// <remarks>Pre: none. Post: see the declaring type's Pre/Post (validated where it is consumed). Side effects: none.</remarks>
        public float PitchDegrees;

        /// <summary>Orbit at <paramref name="up"/> looking along <paramref name="heading"/>, level.</summary>
        /// <remarks>
        /// Pre: up unit (±1e-3); heading finite and not parallel to up.
        /// Post: type invariant; PitchDegrees == 0. Side effects: none.
        /// </remarks>
        public static ViewOrbit Create(float3 up, float3 heading)
        {
            MotorState s = SphericalMotor.Initial(up, heading);
            return new ViewOrbit { Heading = s.Facing, Up = s.Up, PitchDegrees = 0f };
        }

        /// <summary>Re-bases the orbit on a new local up and applies look input.</summary>
        /// <remarks>
        /// Pre: type invariant (Up unit, Heading unit tangent of Up, pitch in range); up unit (±1e-3) strictly within 90° of Up;
        ///      yawDeltaDegrees and pitchDeltaDegrees each finite (degrees, yaw positive = turn right).
        /// Post: type invariant; Up == normalize(up); with zero deltas the heading only changes by the transport rotation.
        /// Side effects: mutates this value.
        /// </remarks>
        public void Advance(float3 up, float yawDeltaDegrees, float pitchDeltaDegrees)
        {
            RequireInvariant();
            Contract.Requires(math.abs(math.lengthsq(up) - 1f) < 2e-3f, "up must be unit length");
            Contract.Requires(math.dot(up, Up) > 0f, "up moved more than 90° in one step");
            Contract.Requires(math.isfinite(yawDeltaDegrees) && math.isfinite(pitchDeltaDegrees), "look deltas must be finite");

            up = math.normalize(up);
            float3 transported = math.mul(SphericalMotor.FromTo(Up, up), Heading);
            float3 heading = SphericalMotor.TangentOr(transported, up, Heading);
            // A positive angle about up turns heading towards cross(up, heading), which is screen-right in Unity.
            heading = math.mul(quaternion.AxisAngle(up, math.radians(yawDeltaDegrees)), heading);
            Heading = SphericalMotor.TangentOr(heading, up, Heading);
            Up = up;
            PitchDegrees = math.clamp(PitchDegrees + pitchDeltaDegrees, MinPitch, MaxPitch);
        }

        /// <summary>World rotation of the camera: forward = heading pitched about the local right axis, up ≈ local up.</summary>
        /// <remarks>
        /// Pre: type invariant (Up unit, Heading unit tangent, pitch in range).
        /// Post: unit quaternion; mul(result, forward) has dot with Up == sin(pitch); mul(result, up) is tilted from
        ///       Up by the pitch — use <see cref="LevelRotation"/> for anything that needs the local world up.
        /// Side effects: none.
        /// </remarks>
        public readonly quaternion Rotation()
        {
            RequireInvariant();
            float3 right = math.cross(Up, Heading);
            // A positive angle about right turns forward towards cross(right, heading) == −Up, so negate to look up.
            float3 forward = math.mul(quaternion.AxisAngle(right, -math.radians(PitchDegrees)), Heading);
            return quaternion.LookRotationSafe(forward, Up);
        }

        /// <summary>World rotation without pitch: forward = Heading, up = Up.</summary>
        /// <remarks>
        /// Pre: type invariant (Up unit, Heading unit tangent, pitch in range). Post: unit quaternion; mul(result, (0,1,0)) == Up (within rounding).
        /// Side effects: none.
        /// REQ: docs/reviews/P1-05-sol.md#F2 — Cinemachine reads WorldUpOverride.up; a pitched pivot tilted it by the pitch.
        /// </remarks>
        public readonly quaternion LevelRotation()
        {
            RequireInvariant();
            return quaternion.LookRotationSafe(Heading, Up);
        }

        // REQ: docs/reviews/P1-review-r4-sol.md#F7 — Rotation/LevelRotation used to check Up only, so a NaN Heading
        // silently produced the identity rotation (LookRotationSafe's fallback) instead of failing.
        private readonly void RequireInvariant()
        {
            Contract.Requires(math.abs(math.lengthsq(Up) - 1f) < 2e-3f, "ViewOrbit is uninitialised (use Create)");
            Contract.Requires(math.abs(math.lengthsq(Heading) - 1f) < 2e-3f && math.abs(math.dot(Heading, Up)) <= 1e-3f,
                "Heading must be a unit tangent");
            Contract.Requires(PitchDegrees >= MinPitch && PitchDegrees <= MaxPitch, "PitchDegrees must be in [MinPitch, MaxPitch]");
        }
    }
}
