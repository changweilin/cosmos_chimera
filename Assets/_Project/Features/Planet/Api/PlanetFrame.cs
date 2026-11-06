using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.Planet
{
    /// <summary>Local east / north / up basis at one point on the planet; the only legal source of "up".</summary>
    /// <remarks>
    /// Pre: construct through <see cref="At"/> or <see cref="FromUp"/>; <c>default(PlanetFrame)</c> is all-zero and invalid.
    /// Post: East, North, Up are unit length, mutually orthogonal, and right-handed: cross(East, North) == Up.
    /// Side effects: none (immutable value).
    /// REQ: CLAUDE.md#硬性規則 — never use Vector3.up / math.up() / Physics.gravity as "up" on the sphere.
    /// Convention: North points towards <see cref="NorthPole"/> along the meridian; longitude increases towards East
    /// (see <see cref="LatLon"/>). Near the poles East/North rotate quickly with position, so controllers and cameras
    /// must carry their own heading and re-project it each frame (<see cref="ProjectOnTangent"/>) instead of
    /// rebuilding orientation from East/North — otherwise the view spins when walking over a pole.
    /// </remarks>
    public readonly struct PlanetFrame
    {
        /// <summary>Planet-local spin axis (unit). Defines North and latitude; not a gravity direction.</summary>
        /// <remarks>Pre: none. Post: constant (0, 1, 0). Side effects: none.</remarks>
        public static readonly float3 NorthPole = new float3(0f, 1f, 0f);

        // East at the exact poles, where cross(NorthPole, up) vanishes. (0,0,-1) is the limit of East when
        // approaching either pole along longitude 0, so the fallback is continuous along that meridian.
        private static readonly float3 PoleEastFallback = new float3(0f, 0f, -1f);

        // cross(NorthPole, up) has length cos(latitude). Below ~1e-5 (≈ 6 mm from the pole at R=640 m) its direction
        // is dominated by the rounding error in `up`, so the fallback is used instead.
        private const float PoleThresholdSq = 1e-10f;

        /// <summary>Unit vector pointing east, tangent to the sphere.</summary>
        /// <remarks>Pre: none. Post: see type invariant. Side effects: none.</remarks>
        public readonly float3 East;

        /// <summary>Unit vector pointing north, tangent to the sphere.</summary>
        /// <remarks>Pre: none. Post: see type invariant. Side effects: none.</remarks>
        public readonly float3 North;

        /// <summary>Unit vector pointing away from the planet centre (opposite to gravity).</summary>
        /// <remarks>Pre: none. Post: see type invariant. Side effects: none.</remarks>
        public readonly float3 Up;

        private PlanetFrame(float3 east, float3 north, float3 up)
        {
            East = east;
            North = north;
            Up = up;
        }

        /// <summary>True for a value built by a factory (guards against <c>default</c>).</summary>
        /// <remarks>Pre: none. Post: result == (Up is non-zero). Side effects: none.</remarks>
        public bool IsValid => math.lengthsq(Up) > 0.5f;

        /// <summary>Frame at <paramref name="worldPosition"/> on or around the planet described by <paramref name="config"/>.</summary>
        /// <remarks>
        /// Pre: same as <see cref="PlanetConfig.ToDirection"/>.
        /// Post: Up == config.ToDirection(worldPosition); type invariant holds.
        /// Side effects: none.
        /// </remarks>
        public static PlanetFrame At(in PlanetConfig config, float3 worldPosition)
        {
            return FromUp(config.ToDirection(worldPosition));
        }

        /// <summary>Frame whose Up is <paramref name="up"/>.</summary>
        /// <remarks>
        /// Pre: up is finite and unit length (±1e-3).
        /// Post: Up == normalize(up) — the tolerance applies to the caller's input, never to the output; type
        ///       invariant holds; at the exact poles East == (0,0,−1).
        /// Side effects: none.
        /// </remarks>
        public static PlanetFrame FromUp(float3 up)
        {
            Contract.Requires(math.abs(math.lengthsq(up) - 1f) < 2e-3f, "up must be unit length");
            // REQ: docs/reviews/P1-01-P1-02-sol.md#F2 — storing a 0.999-long up made North 0.999 long and skewed
            // every LocalToWorld by 0.1 %.
            up = math.normalize(up);
            float3 eastRaw = math.cross(NorthPole, up);
            float3 east = math.lengthsq(eastRaw) > PoleThresholdSq
                ? math.normalize(eastRaw)
                : math.normalize(PoleEastFallback - up * math.dot(PoleEastFallback, up));
            // Derived rather than computed independently so orthogonality is exact up to rounding even when `up`
            // carries normalisation error.
            float3 north = math.cross(up, east);
            return new PlanetFrame(east, north, up);
        }

        /// <summary>Converts a planet-local (east, north, up) vector into world space.</summary>
        /// <remarks>Pre: IsValid; local is finite. Post: length preserved (orthonormal basis). Side effects: none.</remarks>
        public float3 LocalToWorld(float3 local)
        {
            Contract.Requires(IsValid, "PlanetFrame is default/uninitialised");
            Contract.Requires(math.all(math.isfinite(local)), "local must be finite");
            return East * local.x + North * local.y + Up * local.z;
        }

        /// <summary>Converts a world-space vector into planet-local (east, north, up) components.</summary>
        /// <remarks>Pre: IsValid; world is finite. Post: LocalToWorld(WorldToLocal(v)) == v (within rounding). Side effects: none.</remarks>
        public float3 WorldToLocal(float3 world)
        {
            Contract.Requires(IsValid, "PlanetFrame is default/uninitialised");
            Contract.Requires(math.all(math.isfinite(world)), "world must be finite");
            return new float3(math.dot(world, East), math.dot(world, North), math.dot(world, Up));
        }

        /// <summary>Projects <paramref name="direction"/> onto the tangent plane and normalises it.</summary>
        /// <remarks>
        /// Pre: IsValid; direction is finite.
        /// Post: result is unit length and |dot(result, Up)| ≤ 1e-6; it does not depend on the length of direction.
        ///       If direction is zero or within 1e-3 rad of ±Up, returns North — callers carrying a heading should
        ///       treat that as "heading lost" and keep their previous value.
        /// Side effects: none.
        /// </remarks>
        public float3 ProjectOnTangent(float3 direction)
        {
            Contract.Requires(IsValid, "PlanetFrame is default/uninitialised");
            Contract.Requires(math.all(math.isfinite(direction)), "direction must be finite");
            if (math.all(direction == 0f))
            {
                return North;
            }

            // Normalise first so the degeneracy test is an angle, not a length. REQ: docs/reviews/P1-01-P1-02-sol.md#F1
            // — with an absolute 1e-12 threshold, Up*100 left a ~1e-5 rounding residue that passed and was returned
            // as a "tangent" 7° from Up.
            float3 d = math.normalize(VectorMath.Rescale(direction));
            float3 tangent = d - Up * math.dot(d, Up);
            // |tangent| == sin(angle to Up); below 1e-3 its direction is rounding noise (error ≈ ulp / sin).
            if (math.lengthsq(tangent) < 1e-6f)
            {
                return North;
            }

            tangent = math.normalize(tangent);
            // One Gram-Schmidt pass removes the Up component left by the first projection's rounding.
            return math.normalize(tangent - Up * math.dot(tangent, Up));
        }
    }
}
