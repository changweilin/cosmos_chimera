using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>Weather attributes: eight base dimensions, then one dimension per exotic <see cref="WeatherKind"/>.</summary>
    /// <remarks>
    /// REQ: docs/design/weather.md#屬性.
    /// Pre: none. Post: stable values; exotic attribute index == (int) of its WeatherKind (8..17). Side effects: none.
    /// </remarks>
    public enum WeatherAttr
    {
        Clouds = 0,
        Fog = 1,
        Wind = 2,
        Rain = 3,
        Snow = 4,
        Sand = 5,
        Thunder = 6,
        Heat = 7,
    }

    /// <summary>Where a weather vector lives: one terrain inside one region of one planet.</summary>
    /// <remarks>Pre: built with <see cref="WeatherSite(ulong, string, Season?, int, float3, string)"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct WeatherSite
    {
        /// <summary>Describes a weather site.</summary>
        /// <remarks>
        /// Pre: planetTypeId and terrainId non-empty; region &gt;= 0; regionCenter a unit direction.
        /// Post: Indoor is true for room-module dungeons; Fixed is true on the cradle planet. Side effects: none.
        /// </remarks>
        public WeatherSite(ulong seed, string planetTypeId, Season? season, int region, float3 regionCenter, string terrainId)
        {
            Contract.Requires(!string.IsNullOrEmpty(planetTypeId) && !string.IsNullOrEmpty(terrainId), "ids required");
            Contract.Requires(region >= 0, "region must be >= 0");
            Contract.Requires(math.all(math.isfinite(regionCenter)) && math.abs(math.lengthsq(regionCenter) - 1f) < 1e-3f,
                "regionCenter must be a unit vector");
            Seed = seed;
            PlanetTypeId = planetTypeId;
            Season = season;
            Region = region;
            RegionCenter = regionCenter;
            TerrainId = terrainId;
            Indoor = TerrainProfiles.For(terrainId).Dungeon == DungeonKind.RoomModule;
            Fixed = planetTypeId == "cradle_planet";
        }

        /// <summary>Run seed.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public ulong Seed { get; }

        /// <summary>Planet type id.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string PlanetTypeId { get; }

        /// <summary>Planet season, or null on planets without seasons.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public Season? Season { get; }

        /// <summary>Region index (RunPlanner.RegionAt).</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int Region { get; }

        /// <summary>Region centre, where the site's day phase is read.</summary>
        /// <remarks>Pre: none. Post: unit vector. Side effects: none.</remarks>
        public float3 RegionCenter { get; }

        /// <summary>Terrain id at the queried point.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string TerrainId { get; }

        /// <summary>True inside room-module dungeons: base weather is off, only the terrain's exotic remains.</summary>
        /// <remarks>Pre: none. Post: as derived. Side effects: none.</remarks>
        public bool Indoor { get; }

        /// <summary>True on the cradle planet, whose weather is always clear.</summary>
        /// <remarks>Pre: none. Post: as derived. Side effects: none.</remarks>
        public bool Fixed { get; }
    }

    /// <summary>A weather-control request: attributes pinned for a while, then relaxed back to nature.</summary>
    /// <remarks>REQ: docs/design/weather.md#天氣操縱. Pre: built by WeatherControl. Post: immutable. Side effects: none.</remarks>
    public sealed class WeatherOverride
    {
        private readonly int[] _values;

        internal WeatherOverride(long startMs, uint mask, int[] values)
        {
            StartMs = startMs;
            Mask = mask;
            _values = values;
        }

        /// <summary>Simulation time the override was applied.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public long StartMs { get; }

        /// <summary>Bit i set when attribute i is pinned.</summary>
        /// <remarks>Pre: none. Post: only bits 0..17. Side effects: none.</remarks>
        public uint Mask { get; }

        /// <summary>Pinned value (tenths, 0..1000) of attribute <paramref name="attr"/>.</summary>
        /// <remarks>Pre: 0 &lt;= attr &lt; WeatherField.AttrCount. Post: 0..1000. Side effects: none.</remarks>
        public int Value(int attr)
        {
            Contract.Requires(attr >= 0 && attr < WeatherField.AttrCount, "attr out of range");
            return _values[attr];
        }
    }

    /// <summary>
    /// Deterministic weather attribute vector of a site at a time.
    /// REQ: docs/design/weather.md#演化.
    /// </summary>
    /// <remarks>
    /// Pre: none. Post: pure (given the override list); integer-only so host and clients agree bit for bit.
    /// Side effects: none.
    /// </remarks>
    public static class WeatherField
    {
        /// <summary>Number of attributes (8 base + 10 exotic).</summary>
        /// <remarks>Pre: none. Post: constant 18. Side effects: none.</remarks>
        public const int AttrCount = 18;

        /// <summary>Version of the weather generator; bump whenever the same seed would give different weather.</summary>
        /// <remarks>Pre: none. Post: constant; 1 was the retired 180 s slot planner. Side effects: none.</remarks>
        public const int GeneratorVersion = 2;

        /// <summary>How long a weather-control request pins attributes before they relax.</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const int OverrideHoldMs = 180_000;

        private const long RelaxTauS = 240;
        private const int PhaseBlendHalfMs = 15_000;
        private const int PhaseBlendStepMs = 5_000;
        private static readonly int[] OctavePeriodMs = { 419_000, 170_000, 69_000 };
        private static readonly int[] OctaveAmplitude = { 75, 45, 25 };

        /// <summary>Writes the site's attribute vector (tenths, 0..1000) at <paramref name="timeMs"/> into <paramref name="into"/>.</summary>
        /// <remarks>
        /// Pre: timeMs &gt;= 0; into.Length &gt;= AttrCount; overrides may be null, else ordered by StartMs.
        /// Post: every entry in [0, 1000]; exotic entries are 0 wherever the terrain cannot host them; base entries are 0
        ///       indoors; a Fixed site always holds the clear preset. Same inputs give the same output on every platform.
        /// Side effects: writes into.
        /// </remarks>
        public static void Evaluate(in WeatherSite site, long timeMs, IReadOnlyList<WeatherOverride> overrides, int[] into)
        {
            Contract.Requires(timeMs >= 0, "timeMs must be >= 0");
            Contract.Requires(into != null && into.Length >= AttrCount, "into must hold AttrCount entries");
            if (site.Fixed)
            {
                int[] clear = WeatherTables.Presets[0].values;
                for (int i = 0; i < AttrCount; i++) into[i] = i < WeatherTables.BaseCount ? clear[i] * 10 : 0;
                return;
            }

            Natural(site, timeMs, into);
            ApplyOverride(site, overrides, timeMs, into);
            if (site.Indoor)
            {
                for (int i = 0; i < WeatherTables.BaseCount; i++) into[i] = 0;
            }
        }

        /// <summary>The 0..1000 relaxation progress after <paramref name="elapsedMs"/> (0 at start, toward 1000).</summary>
        /// <remarks>Pre: elapsedMs &gt;= 0. Post: monotone non-decreasing, in [0, 1000]; 1000 only once integer rounding leaves no remainder (after ~4 h). Side effects: none.</remarks>
        public static int RelaxPermille(long elapsedMs)
        {
            Contract.Requires(elapsedMs >= 0, "elapsedMs must be >= 0");
            // 1 - (τ/(τ+t))² instead of 1 - e^(-t/τ): integer-only, 75 % at t = τ and 99.6 % after an hour.
            long s = elapsedMs / 1000;
            long d = RelaxTauS + s;
            return (int)(1000 - 1000 * RelaxTauS * RelaxTauS / (d * d));
        }

        private static void Natural(in WeatherSite site, long timeMs, int[] into)
        {
            ulong key = SiteKey(site);
            int[] planet = WeatherTables.PlanetBase(site.PlanetTypeId);
            int[] terrain = WeatherTables.TerrainBias(site.TerrainId);
            int[] season = WeatherTables.SeasonBias(site.Season);

            // Box-filter the phase over ±15 s: a step averaged over a 30 s window is the linear 30 s blend the spec asks for.
            int samples = 2 * PhaseBlendHalfMs / PhaseBlendStepMs + 1;
            Span<int> phaseSum = stackalloc int[AttrCount];
            for (int k = 0; k < samples; k++)
            {
                long t = math.max(0L, timeMs - PhaseBlendHalfMs + (long)k * PhaseBlendStepMs);
                DayPhase phase = SolarClock.PhaseAt(site.RegionCenter, t, site.TerrainId);
                int[] bias = WeatherTables.PhaseBias(phase);
                for (int i = 0; i < WeatherTables.BaseCount; i++) phaseSum[i] += bias[i] * 10;
                for (int i = WeatherTables.BaseCount; i < AttrCount; i++)
                    phaseSum[i] += WeatherTables.ExoticPhasePermille((WeatherKind)i, phase);
            }

            int opening = OpeningPreset(site, key, planet, terrain, season);
            int relax = RelaxPermille(timeMs);
            for (int i = 0; i < AttrCount; i++)
            {
                int mu;
                int init;
                if (i < WeatherTables.BaseCount)
                {
                    mu = math.clamp((planet[i] + terrain[i] + season[i]) * 10 + phaseSum[i] / samples, 0, 1000);
                    init = WeatherTables.Presets[opening].values[i] * 10;
                }
                else
                {
                    int exotic = WeatherTables.ExoticBias((WeatherKind)i, site.PlanetTypeId, site.TerrainId) * 10;
                    mu = exotic * (phaseSum[i] / samples) / 1000;
                    // REQ: weather.md#屬性 — an exotic is impossible (not merely unlikely) where its tendency is zero.
                    if (mu == 0)
                    {
                        into[i] = 0;
                        continue;
                    }

                    init = mu;
                }

                into[i] = Attribute(key, i, timeMs, mu, init, relax);
            }
        }

        private static int Attribute(ulong key, int attr, long timeMs, int mu, int init, int relax)
        {
            int noise = 0;
            for (int o = 0; o < OctavePeriodMs.Length; o++)
            {
                noise += ValueNoise(key, attr, o, timeMs, OctavePeriodMs[o]) * OctaveAmplitude[o] / 1000;
            }

            int vBase = init + (mu - init) * relax / 1000 + noise * math.min(1000, relax + 150) / 1000;

            // Dwell rebound: a slow wave decides when a value parked high (> 55) or low (< 45) gets pulled back across.
            int dwell = ValueNoise(key, attr, 3, timeMs, (160 + 26 * attr) * 1000);
            int rebound = 0;
            if (vBase > 550)
            {
                int excess = math.min(1000, (vBase - 500) * 2);
                rebound = -Pow15(excess) * 160 / 1000 * math.max(0, dwell + 350) / 1000;
            }
            else if (vBase < 450)
            {
                int deficit = math.min(1000, (500 - vBase) * 2);
                rebound = Pow15(deficit) * 160 / 1000 * math.max(0, -dwell + 350) / 1000;
            }

            return math.clamp(vBase + rebound * math.min(1000, relax + 250) / 1000, 0, 1000);
        }

        private static void ApplyOverride(in WeatherSite site, IReadOnlyList<WeatherOverride> overrides, long timeMs, int[] into)
        {
            if (overrides == null) return;
            WeatherOverride active = null;
            for (int i = 0; i < overrides.Count; i++)
            {
                if (overrides[i].StartMs <= timeMs) active = overrides[i];
            }

            if (active == null) return;
            long sinceRelease = timeMs - active.StartMs - OverrideHoldMs;
            int relax = sinceRelease <= 0 ? 0 : RelaxPermille(sinceRelease);
            for (int i = 0; i < AttrCount; i++)
            {
                if ((active.Mask & (1u << i)) == 0) continue;
                int pinned = active.Value(i);
                // Overrides cover a whole region; an exotic summoned on its native terrain must not appear on the region's
                // other terrains (REQ: weather.md#天氣操縱 — 召喚只限原生地貌).
                if (i >= WeatherTables.BaseCount && pinned > 0
                    && WeatherTables.ExoticBias((WeatherKind)i, site.PlanetTypeId, site.TerrainId) == 0) continue;
                into[i] = math.clamp(pinned + (into[i] - pinned) * relax / 1000, 0, 1000);
            }
        }

        private static int OpeningPreset(in WeatherSite site, ulong key, int[] planet, int[] terrain, int[] season)
        {
            var rng = new Pcg32(key, 0x57454154UL); // "WEAT"
            int total = 0;
            Span<int> weights = stackalloc int[WeatherTables.Presets.Length];
            for (int p = 0; p < weights.Length; p++)
            {
                int attr = WeatherTables.Presets[p].weightAttr;
                weights[p] = attr < 0 ? 50 : 1 + math.max(0, planet[attr] + terrain[attr] + season[attr]);
                total += weights[p];
            }

            int roll = rng.NextInt(0, total);
            for (int p = 0; p < weights.Length; p++)
            {
                if (roll < weights[p]) return p;
                roll -= weights[p];
            }

            return 0;
        }

        // Value noise on an integer lattice with smoothstep blending; stands in for the reference's sine sums because
        // float trigonometry is not bit-identical across platforms.
        internal static int ValueNoise(ulong key, int attr, int octave, long timeMs, int periodMs)
        {
            long cell = timeMs / periodMs;
            int frac = (int)((timeMs % periodMs) * 1000 / periodMs);
            int a = Lattice(key, attr, octave, cell);
            int b = Lattice(key, attr, octave, cell + 1);
            int s = (int)((long)frac * frac * (3000 - 2 * frac) / 1_000_000);
            return a + (b - a) * s / 1000;
        }

        private static int Lattice(ulong key, int attr, int octave, long cell)
        {
            ulong stream = ((ulong)cell << 12) ^ ((ulong)attr << 4) ^ (ulong)octave;
            var rng = new Pcg32(key, stream);
            return rng.NextInt(-1000, 1001);
        }

        private static int Pow15(int permille) => (int)((long)permille * ISqrt((long)permille * 1000) / 1000);

        private static long ISqrt(long n)
        {
            if (n <= 0) return 0;
            long x = (long)System.Math.Sqrt(n);
            // Correct the double estimate so the result is the exact floor on every platform.
            while (x * x > n) x--;
            while ((x + 1) * (x + 1) <= n) x++;
            return x;
        }

        private static ulong SiteKey(in WeatherSite site)
        {
            // FNV-1a 64: string.GetHashCode is randomized per process, so it cannot key a reproducible stream.
            ulong h = 14695981039346656037UL;
            string id = site.TerrainId;
            for (int i = 0; i < id.Length; i++)
            {
                h ^= id[i];
                h *= 1099511628211UL;
            }

            h ^= (ulong)site.Region * 0x9E3779B97F4A7C15UL;
            return site.Seed ^ h;
        }
    }
}
