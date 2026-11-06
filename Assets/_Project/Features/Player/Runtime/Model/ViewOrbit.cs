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
        public const float MinPitch = -80f;

        /// <summary>Highest allowed pitch (looking up), degrees.</summary>
        public const float MaxPitch = 80f;

        /// <summary>Unit tangent the camera faces horizontally.</summary>
        public float3 Heading;

        /// <summary>Local up the heading is tangent to.</summary>
        public float3 Up;

        /// <summary>Positive looks up, degrees.</summary>
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
        /// Pre: Up unit (initialised); up unit (±1e-3) within 90° of Up; yaw/pitch deltas finite (degrees, yaw positive = turn right).
        /// Post: type invariant; Up == normalize(up); with zero deltas the heading only changes by the transport rotation.
        /// Side effects: mutates this value.
        /// </remarks>
        public void Advance(float3 up, float yawDeltaDegrees, float pitchDeltaDegrees)
        {
            Contract.Requires(math.abs(math.lengthsq(Up) - 1f) < 2e-3f, "ViewOrbit is uninitialised (use Create)");
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
        /// <remarks>Pre: initialised. Post: unit quaternion; mul(result, forward) has dot with Up == sin(pitch). Side effects: none.</remarks>
        public readonly quaternion Rotation()
        {
            float3 right = math.cross(Up, Heading);
            // A positive angle about right turns forward towards cross(right, heading) == −Up, so negate to look up.
            float3 forward = math.mul(quaternion.AxisAngle(right, -math.radians(PitchDegrees)), Heading);
            return quaternion.LookRotationSafe(forward, Up);
        }
    }
}
