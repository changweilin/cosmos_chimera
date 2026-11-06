using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.Planet
{
    /// <summary>Immutable size and placement of one planet; the single source of truth for its radius.</summary>
    /// <remarks>
    /// Pre: construct through <see cref="PlanetConfig(float, float3)"/>; <c>default(PlanetConfig)</c> has Radius 0 and is invalid.
    /// Post: Radius is finite and &gt; 0; Center is finite.
    /// Side effects: none (immutable value).
    /// REQ: CLAUDE.md#硬性規則 — every other planet dimension is derived from <see cref="Radius"/>.
    /// </remarks>
    public readonly struct PlanetConfig
    {
        /// <summary>Largest radius this build supports without a floating origin.</summary>
        /// <remarks>
        /// Pre: none. Post: constant. Side effects: none.
        /// REQ: docs/architecture/planet-tech.md#浮點精度 — beyond ~8 km float precision must be re-evaluated.
        /// </remarks>
        public const float MaxSupportedRadius = 8000f;

        /// <summary>Smallest supported radius.</summary>
        /// <remarks>
        /// Pre: none. Post: constant. Side effects: none.
        /// REQ: docs/reviews/P1-01-P1-02-sol.md#F3 — sub-metre radii (down to float.Epsilon) underflow derived sizes
        /// such as NodeEdgeLength to 0, breaking their "&gt; 0" postconditions.
        /// </remarks>
        public const float MinSupportedRadius = 1f;

        /// <summary>Largest |component| of <see cref="Center"/>.</summary>
        /// <remarks>
        /// Pre: none. Post: constant. Side effects: none.
        /// Keeps surface coordinates below 2^15 m, where float spacing is ≤ 4 mm (planet-tech.md#浮點精度).
        /// REQ: sol review F3 — a Center of 3.4e10 m made ToWorld return Center itself (the offset vanished).
        /// </remarks>
        public const float MaxCenterComponent = 16000f;

        /// <summary>Sea-level radius in metres.</summary>
        /// <remarks>Pre: none. Post: 0 &lt; Radius ≤ <see cref="MaxSupportedRadius"/> for a constructed value. Side effects: none.</remarks>
        public readonly float Radius;

        /// <summary>World-space position of the planet centre.</summary>
        /// <remarks>Pre: none. Post: finite for a constructed value. Side effects: none.</remarks>
        public readonly float3 Center;

        /// <summary>Creates a planet description.</summary>
        /// <remarks>
        /// Pre: radius finite and MinSupportedRadius ≤ radius ≤ MaxSupportedRadius; center finite with every
        ///      |component| ≤ MaxCenterComponent.
        /// Post: fields equal the arguments.
        /// Side effects: none.
        /// </remarks>
        public PlanetConfig(float radius, float3 center)
        {
            Contract.Requires(math.isfinite(radius) && radius >= MinSupportedRadius && radius <= MaxSupportedRadius,
                "radius must be finite and in [MinSupportedRadius, MaxSupportedRadius]");
            Contract.Requires(math.all(math.isfinite(center)) && math.cmax(math.abs(center)) <= MaxCenterComponent,
                "center must be finite and within MaxCenterComponent per axis");
            Radius = radius;
            Center = center;
        }

        /// <summary>True for a value built by the constructor (guards against <c>default</c>).</summary>
        /// <remarks>Pre: none. Post: result == (Radius &gt; 0). Side effects: none.</remarks>
        public bool IsValid => Radius > 0f;

        /// <summary>Distance along the ground to the visible horizon for an eye at <paramref name="eyeHeight"/> above sea level.</summary>
        /// <remarks>
        /// Pre: IsValid; eyeHeight finite and 0 ≤ eyeHeight ≤ MaxSupportedRadius.
        /// Post: result ≥ 0; result == 0 iff eyeHeight == 0.
        /// Side effects: none.
        /// REQ: docs/architecture/planet-tech.md#地平線 — sqrt(2Rh + h²) (straight-line tangent distance).
        /// </remarks>
        public float HorizonDistance(float eyeHeight)
        {
            Contract.Requires(IsValid, "PlanetConfig is default/uninitialised");
            Contract.Requires(math.isfinite(eyeHeight) && eyeHeight >= 0f && eyeHeight <= MaxSupportedRadius,
                "eyeHeight must be finite and within [0, MaxSupportedRadius]");
            return math.sqrt(2f * Radius * eyeHeight + eyeHeight * eyeHeight);
        }

        /// <summary>World position at <paramref name="altitude"/> metres above sea level along <paramref name="direction"/>.</summary>
        /// <remarks>
        /// Pre: IsValid; direction finite and unit length (±1e-3); altitude finite, −Radius &lt; altitude ≤ MaxSupportedRadius.
        /// Post: distance(result, Center) == Radius + altitude (within float rounding). Direction is normalised first,
        ///       so the input tolerance never shifts the radius (sol review F2: 0.64 m at R=640 before).
        /// Side effects: none.
        /// </remarks>
        public float3 ToWorld(float3 direction, float altitude)
        {
            Contract.Requires(IsValid, "PlanetConfig is default/uninitialised");
            Contract.Requires(math.abs(math.lengthsq(direction) - 1f) < 2e-3f, "direction must be unit length");
            Contract.Requires(math.isfinite(altitude) && altitude > -Radius && altitude <= MaxSupportedRadius,
                "altitude must be finite and within (-Radius, MaxSupportedRadius]");
            return Center + math.normalize(direction) * (Radius + altitude);
        }

        /// <summary>Unit direction from the centre towards <paramref name="worldPosition"/>; this is the local up.</summary>
        /// <remarks>
        /// Pre: IsValid; worldPosition finite; some |component| of (worldPosition − Center) exceeds 1e-3 m.
        /// Post: result is unit length, also far outside the planet (e.g. 1e20 m, whose lengthsq overflows).
        /// Side effects: none.
        /// </remarks>
        public float3 ToDirection(float3 worldPosition)
        {
            Contract.Requires(IsValid, "PlanetConfig is default/uninitialised");
            float3 offset = worldPosition - Center;
            Contract.Requires(math.all(math.isfinite(worldPosition)) && math.all(math.isfinite(offset))
                && math.cmax(math.abs(offset)) > 1e-3f, "worldPosition must be finite and away from the planet centre");
            // Subtract the centre before normalising: normalising a world position directly is only right for
            // a planet at the origin, and that assumption would silently tilt up once Center moves.
            return math.normalize(VectorMath.Rescale(offset));
        }

        /// <summary>Height of <paramref name="worldPosition"/> above sea level (negative below).</summary>
        /// <remarks>
        /// Pre: IsValid; worldPosition is finite.
        /// Post: result ≥ −Radius.
        /// Side effects: none.
        /// </remarks>
        public float Altitude(float3 worldPosition)
        {
            Contract.Requires(IsValid, "PlanetConfig is default/uninitialised");
            Contract.Requires(math.all(math.isfinite(worldPosition)), "worldPosition must be finite");
            return math.length(worldPosition - Center) - Radius;
        }
    }
}
