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

        /// <summary>Sea-level radius in metres.</summary>
        /// <remarks>Pre: none. Post: 0 &lt; Radius ≤ <see cref="MaxSupportedRadius"/> for a constructed value. Side effects: none.</remarks>
        public readonly float Radius;

        /// <summary>World-space position of the planet centre.</summary>
        /// <remarks>Pre: none. Post: finite for a constructed value. Side effects: none.</remarks>
        public readonly float3 Center;

        /// <summary>Creates a planet description.</summary>
        /// <remarks>
        /// Pre: radius is finite and 0 &lt; radius ≤ <see cref="MaxSupportedRadius"/>; center is finite.
        /// Post: fields equal the arguments.
        /// Side effects: none.
        /// </remarks>
        public PlanetConfig(float radius, float3 center)
        {
            Contract.Requires(math.isfinite(radius) && radius > 0f && radius <= MaxSupportedRadius,
                "radius must be finite and in (0, MaxSupportedRadius]");
            Contract.Requires(math.all(math.isfinite(center)), "center must be finite");
            Radius = radius;
            Center = center;
        }

        /// <summary>True for a value built by the constructor (guards against <c>default</c>).</summary>
        /// <remarks>Pre: none. Post: result == (Radius &gt; 0). Side effects: none.</remarks>
        public bool IsValid => Radius > 0f;

        /// <summary>Distance along the ground to the visible horizon for an eye at <paramref name="eyeHeight"/> above sea level.</summary>
        /// <remarks>
        /// Pre: IsValid; eyeHeight is finite and ≥ 0.
        /// Post: result ≥ 0; result == 0 iff eyeHeight == 0.
        /// Side effects: none.
        /// REQ: docs/architecture/planet-tech.md#地平線 — sqrt(2Rh + h²) (straight-line tangent distance).
        /// </remarks>
        public float HorizonDistance(float eyeHeight)
        {
            Contract.Requires(IsValid, "PlanetConfig is default/uninitialised");
            Contract.Requires(math.isfinite(eyeHeight) && eyeHeight >= 0f, "eyeHeight must be finite and >= 0");
            return math.sqrt(2f * Radius * eyeHeight + eyeHeight * eyeHeight);
        }

        /// <summary>World position at <paramref name="altitude"/> metres above sea level along <paramref name="direction"/>.</summary>
        /// <remarks>
        /// Pre: IsValid; direction is unit length (±1e-3); altitude is finite and altitude &gt; −Radius.
        /// Post: distance(result, Center) == Radius + altitude (within float rounding).
        /// Side effects: none.
        /// </remarks>
        public float3 ToWorld(float3 direction, float altitude)
        {
            Contract.Requires(IsValid, "PlanetConfig is default/uninitialised");
            Contract.Requires(math.abs(math.lengthsq(direction) - 1f) < 2e-3f, "direction must be unit length");
            Contract.Requires(math.isfinite(altitude) && altitude > -Radius, "altitude must be finite and above the centre");
            return Center + direction * (Radius + altitude);
        }

        /// <summary>Unit direction from the centre towards <paramref name="worldPosition"/>; this is the local up.</summary>
        /// <remarks>
        /// Pre: IsValid; worldPosition is finite and not within 1e-3 m of Center.
        /// Post: result is unit length.
        /// Side effects: none.
        /// </remarks>
        public float3 ToDirection(float3 worldPosition)
        {
            Contract.Requires(IsValid, "PlanetConfig is default/uninitialised");
            float3 offset = worldPosition - Center;
            Contract.Requires(math.all(math.isfinite(worldPosition)) && math.lengthsq(offset) > 1e-6f,
                "worldPosition must be finite and away from the planet centre");
            // Subtract the centre before normalising: normalising a world position directly is only right for
            // a planet at the origin, and that assumption would silently tilt up once Center moves.
            return math.normalize(offset);
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
