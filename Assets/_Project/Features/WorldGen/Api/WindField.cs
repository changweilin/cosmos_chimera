using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Deterministic wind heading and gust of a region at a time.
    /// REQ: docs/design/weather.md#風向.
    /// </summary>
    /// <remarks>
    /// Pre: none. Post: pure, integer-only, so host and clients agree bit for bit. Side effects: none.
    /// Heading is per region (not per terrain): wind must not turn at a stamp border.
    /// </remarks>
    public static class WindField
    {
        /// <summary>Version of the wind generator; bump whenever the same seed would give different wind.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 1;

        // Attribute slots past WeatherField.AttrCount, so the shared lattice never hands wind the same stream as weather.
        private const int SwingAttr = 40;
        private const int GustAttr = 41;
        private const int SlowSwingPeriodMs = 300_000;
        private const int FastSwingPeriodMs = 90_000;
        private const int SlowSwingTenths = 800;
        private const int FastSwingTenths = 250;
        private const int GustSlowPeriodMs = 7_000;
        private const int GustFastPeriodMs = 2_300;

        /// <summary>Wind heading in tenths of a degree, 0 = local east, increasing toward north.</summary>
        /// <remarks>
        /// Pre: region &gt;= 0; timeMs &gt;= 0.
        /// Post: result in [0, 3600); continuous in timeMs (changes at most ~SlowSwing + FastSwing around a fixed base).
        /// Side effects: none.
        /// </remarks>
        public static int HeadingTenths(ulong seed, int region, long timeMs)
        {
            Contract.Requires(region >= 0, "region must be >= 0");
            Contract.Requires(timeMs >= 0, "timeMs must be >= 0");
            ulong key = RegionKey(seed, region);
            var rng = new Pcg32(key, 0x57444952UL); // "WDIR"
            int heading = rng.NextInt(0, 3600)
                          + WeatherField.ValueNoise(key, SwingAttr, 0, timeMs, SlowSwingPeriodMs) * SlowSwingTenths / 1000
                          + WeatherField.ValueNoise(key, SwingAttr, 1, timeMs, FastSwingPeriodMs) * FastSwingTenths / 1000;
            return ((heading % 3600) + 3600) % 3600;
        }

        /// <summary>Gust strength 0..1000 under an effective wind of <paramref name="windPermille"/>.</summary>
        /// <remarks>
        /// Pre: region &gt;= 0; timeMs &gt;= 0; windPermille in [0, 1000].
        /// Post: result in [0, windPermille]; 0 when windPermille == 0 (gusts only matter in a real wind).
        /// Side effects: none.
        /// </remarks>
        public static int GustPermille(ulong seed, int region, long timeMs, int windPermille)
        {
            Contract.Requires(region >= 0, "region must be >= 0");
            Contract.Requires(timeMs >= 0, "timeMs must be >= 0");
            Contract.Requires(windPermille >= 0 && windPermille <= 1000, "windPermille must be in [0, 1000]");
            ulong key = RegionKey(seed, region);
            int raw = (WeatherField.ValueNoise(key, GustAttr, 0, timeMs, GustSlowPeriodMs) * 600
                       + WeatherField.ValueNoise(key, GustAttr, 1, timeMs, GustFastPeriodMs) * 400) / 1000;
            // Only the positive half: gusts are bursts above the steady wind, not lulls below it.
            return math.max(0, raw) * windPermille / 1000;
        }

        /// <summary>Unit tangent the wind blows along at <paramref name="up"/> for a heading.</summary>
        /// <remarks>
        /// Pre: up unit length (PlanetFrame.FromUp's Pre); headingTenths any integer (wrapped).
        /// Post: unit vector orthogonal to up; uses integer sine so equal inputs give equal results.
        /// Side effects: none.
        /// </remarks>
        public static float3 Tangent(float3 up, int headingTenths)
        {
            PlanetFrame frame = PlanetFrame.FromUp(up);
            float c = SolarClock.CosPermille(headingTenths) / 1000f;
            float s = SolarClock.SinPermille(headingTenths) / 1000f;
            return math.normalize(frame.East * c + frame.North * s);
        }

        private static ulong RegionKey(ulong seed, int region) =>
            seed ^ ((ulong)(region + 1) * 0x9E3779B97F4A7C15UL) ^ 0x57494E44UL; // "WIND"
    }
}
