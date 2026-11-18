using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>Local time of day on the planet surface.</summary>
    /// <remarks>Pre: none. Post: stable values (peers and saves compare them). Side effects: none.</remarks>
    public enum DayPhase
    {
        Dawn = 0,
        Day = 1,
        Dusk = 2,
        Night = 3,
    }

    /// <summary>
    /// Deterministic sun position and local time-of-day phase.
    /// REQ: docs/design/weather.md#時段.
    /// </summary>
    /// <remarks>
    /// Pre: the planet spins about +Y (the same axis the presentation sun turns about).
    /// Post: pure; integer math only, so every client gets the same phase for the same (direction, timeMs).
    /// Side effects: none.
    /// </remarks>
    public static class SolarClock
    {
        /// <summary>Length of one planet day in milliseconds (600 s).</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const int DayLengthMs = 600_000;

        /// <summary>Half-width of the dawn/dusk band in degrees of sun elevation.</summary>
        /// <remarks>Pre: none. Post: constant in (0, 90). Side effects: none.</remarks>
        public const int TwilightBandDegrees = 30;

        // Angles are tenths of a degree so a 600 s day steps every 1/6 s — finer than the 50 ms combat tick needs.
        private const int FullTurnTenths = 3600;
        private const int HalfTurnTenths = 1800;

        /// <summary>Sine of an angle given in tenths of a degree, in permille.</summary>
        /// <remarks>
        /// Pre: none (any integer angle).
        /// Post: result in [-1000, 1000]; within 3 permille of the true sine; odd and 3600-periodic.
        /// Side effects: none.
        /// </remarks>
        public static int SinPermille(int tenthsOfDegree)
        {
            int a = ((tenthsOfDegree % FullTurnTenths) + FullTurnTenths) % FullTurnTenths;
            int sign = 1;
            if (a >= HalfTurnTenths)
            {
                a -= HalfTurnTenths;
                sign = -1;
            }

            // Bhaskara I's rational approximation, exact at 0°, 30°, 90°, 150°, 180°; integer-only so it is bit-identical
            // on every platform, which Math.Sin does not guarantee.
            long p = (long)a * (HalfTurnTenths - a);
            long value = 4L * p * 1000L / (4_050_000L - p);
            return sign * (int)value;
        }

        /// <summary>Cosine of an angle given in tenths of a degree, in permille.</summary>
        /// <remarks>Pre: none. Post: result in [-1000, 1000]; equals SinPermille(angle + 900). Side effects: none.</remarks>
        public static int CosPermille(int tenthsOfDegree) => SinPermille(tenthsOfDegree + 900);

        /// <summary>Sun hour angle at <paramref name="timeMs"/>, in tenths of a degree.</summary>
        /// <remarks>Pre: timeMs &gt;= 0. Post: result in [0, 3600). Side effects: none.</remarks>
        public static int SunAngleTenths(long timeMs)
        {
            Contract.Requires(timeMs >= 0, "timeMs must be >= 0");
            return (int)(timeMs % DayLengthMs * FullTurnTenths / DayLengthMs);
        }

        /// <summary>Unit direction from the planet centre toward the sun, for presentation.</summary>
        /// <remarks>Pre: timeMs &gt;= 0. Post: unit vector in the XZ plane, same angle the rule layer uses. Side effects: none.</remarks>
        public static float3 SunDirection(long timeMs)
        {
            int angle = SunAngleTenths(timeMs);
            return math.normalize(new float3(CosPermille(angle), 0f, SinPermille(angle)));
        }

        /// <summary>Sine of the sun's elevation above the local horizon, in permille.</summary>
        /// <remarks>
        /// Pre: direction is a unit surface direction; timeMs &gt;= 0.
        /// Post: result in [-1000, 1000]; positive when the sun is above the horizon. Side effects: none.
        /// </remarks>
        public static int SunElevationPermille(float3 direction, long timeMs)
        {
            RequireDirection(direction);
            int angle = SunAngleTenths(timeMs);
            Quantize(direction, out int x, out int z);
            return math.clamp((x * CosPermille(angle) + z * SinPermille(angle)) / 1000, -1000, 1000);
        }

        /// <summary>Local day phase from the sun alone (no terrain override).</summary>
        /// <remarks>
        /// Pre: direction is a unit surface direction; timeMs &gt;= 0.
        /// Post: Day above +band, Night at or below -band, otherwise Dawn while the sun rises and Dusk while it sets.
        /// Side effects: none.
        /// </remarks>
        public static DayPhase SunPhaseAt(float3 direction, long timeMs)
        {
            RequireDirection(direction);
            int angle = SunAngleTenths(timeMs);
            Quantize(direction, out int x, out int z);
            int sin = SinPermille(angle), cos = CosPermille(angle);
            int elevation = (x * cos + z * sin) / 1000;
            int band = SinPermille(TwilightBandDegrees * 10);
            if (elevation > band) return DayPhase.Day;
            if (elevation <= -band) return DayPhase.Night;
            // d(elevation)/d(angle): the sun is rising when advancing time increases the elevation.
            return -x * sin + z * cos >= 0 ? DayPhase.Dawn : DayPhase.Dusk;
        }

        /// <summary>Local day phase, honouring terrains with a fixed sky.</summary>
        /// <remarks>
        /// REQ: docs/design/weather.md#時段 — eternal_day is always Day, eternal_night always Night.
        /// Pre: direction is a unit surface direction; timeMs &gt;= 0; terrainId may be null.
        /// Post: a defined DayPhase. Side effects: none.
        /// </remarks>
        public static DayPhase PhaseAt(float3 direction, long timeMs, string terrainId)
        {
            if (terrainId == "eternal_day")
            {
                RequireDirection(direction);
                Contract.Requires(timeMs >= 0, "timeMs must be >= 0");
                return DayPhase.Day;
            }

            if (terrainId == "eternal_night")
            {
                RequireDirection(direction);
                Contract.Requires(timeMs >= 0, "timeMs must be >= 0");
                return DayPhase.Night;
            }

            return SunPhaseAt(direction, timeMs);
        }

        private static void Quantize(float3 direction, out int x, out int z)
        {
            // IEEE multiply and round are exact-rounded everywhere, so quantizing to permille is the last float step.
            x = (int)math.round(direction.x * 1000f);
            z = (int)math.round(direction.z * 1000f);
        }

        private static void RequireDirection(float3 direction) =>
            Contract.Requires(math.all(math.isfinite(direction)) && math.abs(math.lengthsq(direction) - 1f) < 1e-3f,
                "direction must be a unit vector");
    }
}
