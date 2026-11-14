using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.Planet
{
    /// <summary>Geographic coordinate in radians on the unit sphere; used for real-map templates, never for chunking.</summary>
    /// <remarks>
    /// Pre: none.
    /// Post: values built by <see cref="FromDirection"/> satisfy −π/2 ≤ Latitude ≤ π/2 and −π &lt; Longitude ≤ π.
    /// Side effects: none (immutable value).
    /// Convention (matches <see cref="PlanetFrame"/>): (0,0) is +X, latitude +π/2 is <see cref="PlanetFrame.NorthPole"/>,
    /// longitude +π/2 is +Z. At the poles longitude is undefined and <see cref="FromDirection"/> returns 0.
    /// </remarks>
    public readonly struct LatLon
    {
        /// <summary>Latitude in radians, positive towards the north pole.</summary>
        /// <remarks>Pre: none. Post: see type. Side effects: none.</remarks>
        public readonly float Latitude;

        /// <summary>Longitude in radians, positive towards East.</summary>
        /// <remarks>Pre: none. Post: see type. Side effects: none.</remarks>
        public readonly float Longitude;

        /// <summary>Creates a coordinate.</summary>
        /// <remarks>
        /// Pre: both finite; |latitude| ≤ π/2 (+1e-6 tolerance).
        /// Post: fields equal the arguments.
        /// Side effects: none.
        /// </remarks>
        public LatLon(float latitude, float longitude)
        {
            Contract.Requires(math.isfinite(latitude) && math.abs(latitude) <= math.PI * 0.5f + 1e-6f,
                "latitude must be finite and within [-pi/2, pi/2]");
            Contract.Requires(math.isfinite(longitude), "longitude must be finite");
            Latitude = latitude;
            Longitude = longitude;
        }

        /// <summary>Coordinate of a unit direction.</summary>
        /// <remarks>
        /// Pre: direction is unit length (±1e-3).
        /// Post: ToDirection(result) ≈ normalize(direction) (except longitude at the poles); −π &lt; Longitude ≤ π.
        /// Side effects: none.
        /// </remarks>
        public static LatLon FromDirection(float3 direction)
        {
            Contract.Requires(math.abs(math.lengthsq(direction) - 1f) < 2e-3f, "direction must be unit length");
            // asin is ill-conditioned near ±1; atan2 on (y, horizontal length) keeps full precision at the poles.
            float horizontal = math.sqrt(direction.x * direction.x + direction.z * direction.z);
            float latitude = math.atan2(direction.y, horizontal);
            float longitude = horizontal > 0f ? math.atan2(direction.z, direction.x) : 0f;
            // atan2(+0, −x) is +π but atan2(−0, −x) is −π; fold the antimeridian onto +π so the documented range
            // (−π, π] holds for both signed zeros (sol review F7).
            if (longitude <= -math.PI)
            {
                longitude = math.PI;
            }

            return new LatLon(latitude, longitude);
        }

        /// <summary>Unit direction of this coordinate.</summary>
        /// <remarks>Pre: none. Post: result is unit length. Side effects: none.</remarks>
        public float3 ToDirection()
        {
            math.sincos(Latitude, out float sinLat, out float cosLat);
            math.sincos(Longitude, out float sinLon, out float cosLon);
            return new float3(cosLat * cosLon, sinLat, cosLat * sinLon);
        }
    }
}
