using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// A run's weather: vectors per site, ground deposits, and the resolved profile at any point and time.
    /// REQ: docs/design/weather.md.
    /// </summary>
    /// <remarks>
    /// Pre: one instance per run; queries use the run's simulation clock.
    /// Post: results depend only on (seed, planet type, season, regions, terrain, control history, time), so every client
    ///       computes the same weather without network sync.
    /// Side effects: caches per site; integrates deposits forward (recomputes from t = 0 if asked about an earlier time).
    /// </remarks>
    public sealed class WeatherService
    {
        private const int DepositCount = 4;
        // Fill / clear seconds per GroundDeposit (Water, Snow, Sand, Ash); REQ: weather.md#地表累積 (數值暫定).
        private static readonly int[] FillS = { 90, 150, 180, 180 };
        private static readonly int[] ClearS = { 120, 240, 180, 360 };
        private const int CoverDelayPermille = 80;

        private sealed class Site
        {
            internal WeatherSite Where;
            internal readonly int[] Vector = new int[WeatherField.AttrCount];
            internal long VectorMs = -1;
            internal int OverrideCount = -1;
            internal WeatherState State;
            internal readonly int[] Deposit = new int[DepositCount];
            internal readonly int[] Cover = new int[DepositCount];
            internal long IntegratedS;
            internal readonly WeatherProfile[] Profiles = new WeatherProfile[4];
            internal readonly long[] ProfileMs = { -1, -1, -1, -1 };
        }

        private readonly ulong _seed;
        private readonly string _planetTypeId;
        private readonly Season? _season;
        private readonly Func<float3, int> _regionAt;
        private readonly Func<int, float3> _regionCenter;
        private readonly Func<float3, string> _terrainAt;
        private readonly WeatherControl _control;
        private readonly Dictionary<(int, string), Site> _sites = new Dictionary<(int, string), Site>();
        private readonly int[] _scratch = new int[WeatherField.AttrCount];

        /// <summary>Creates the run's weather.</summary>
        /// <remarks>
        /// Pre: planetTypeId non-empty; regionAt, regionCenter, terrainAt, control non-null and deterministic.
        /// Post: no sites cached. Side effects: allocates.
        /// </remarks>
        public WeatherService(ulong seed, string planetTypeId, Season? season, Func<float3, int> regionAt,
            Func<int, float3> regionCenter, Func<float3, string> terrainAt, WeatherControl control)
        {
            Contract.Requires(!string.IsNullOrEmpty(planetTypeId), "planetTypeId required");
            Contract.Requires(regionAt != null && regionCenter != null && terrainAt != null && control != null,
                "lookups and control required");
            _seed = seed;
            _planetTypeId = planetTypeId;
            _season = season;
            _regionAt = regionAt;
            _regionCenter = regionCenter;
            _terrainAt = terrainAt;
            _control = control;
        }

        /// <summary>The run's weather control (shared cooldown and overrides).</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public WeatherControl Control => _control;

        /// <summary>Rule-layer weather at a surface point.</summary>
        /// <remarks>
        /// Pre: direction a unit vector; timeMs &gt;= 0.
        /// Post: see <see cref="WeatherDynamics.Compose"/>; same arguments give the same profile on every client.
        /// Side effects: may integrate deposits and fill caches.
        /// </remarks>
        public WeatherProfile ProfileAt(float3 direction, long timeMs)
        {
            Site site = Refresh(direction, timeMs);
            DayPhase phase = SolarClock.PhaseAt(direction, timeMs, site.Where.TerrainId);
            int p = (int)phase;
            if (site.ProfileMs[p] != timeMs)
            {
                site.Profiles[p] = WeatherDynamics.Compose(site.State, phase, site.Where.Indoor, site.Cover);
                site.ProfileMs[p] = timeMs;
            }

            return site.Profiles[p];
        }

        /// <summary>Effective weather strengths at a surface point (for HUD and presentation).</summary>
        /// <remarks>Pre: direction a unit vector; timeMs &gt;= 0. Post: as resolved. Side effects: as <see cref="ProfileAt"/>.</remarks>
        public WeatherState StateAt(float3 direction, long timeMs) => Refresh(direction, timeMs).State;

        /// <summary>Local day phase at a surface point (fixed on eternal_day / eternal_night).</summary>
        /// <remarks>Pre: direction a unit vector; timeMs &gt;= 0. Post: defined phase. Side effects: none.</remarks>
        public DayPhase PhaseAt(float3 direction, long timeMs) =>
            SolarClock.PhaseAt(direction, timeMs, _terrainAt(direction));

        /// <summary>Whether <paramref name="kind"/> may appear (and be summoned) at a surface point.</summary>
        /// <remarks>Pre: direction a unit vector; kind exotic. Post: true iff the terrain gives it a nonzero tendency. Side effects: none.</remarks>
        public bool IsNativeExotic(float3 direction, WeatherKind kind)
        {
            Contract.Requires(WeatherProfiles.IsExotic(kind), "kind must be exotic");
            return WeatherTables.ExoticBias(kind, _planetTypeId, _terrainAt(direction)) > 0;
        }

        /// <summary>Whether a surface point is inside a room-module dungeon.</summary>
        /// <remarks>Pre: direction a unit vector. Post: as TerrainProfiles. Side effects: none.</remarks>
        public bool IsIndoor(float3 direction) =>
            TerrainProfiles.For(_terrainAt(direction)).Dungeon == DungeonKind.RoomModule;

        /// <summary>Region index of a surface point (the weather-control scope).</summary>
        /// <remarks>Pre: direction a unit vector. Post: &gt;= 0. Side effects: none.</remarks>
        public int RegionAt(float3 direction) => _regionAt(direction);

        private Site Refresh(float3 direction, long timeMs)
        {
            Contract.Requires(timeMs >= 0, "timeMs must be >= 0");
            int region = _regionAt(direction);
            string terrain = _terrainAt(direction);
            if (!_sites.TryGetValue((region, terrain), out Site site))
            {
                site = new Site { Where = new WeatherSite(_seed, _planetTypeId, _season, region, _regionCenter(region), terrain) };
                _sites[(region, terrain)] = site;
            }

            IReadOnlyList<WeatherOverride> overrides = _control.OverridesFor(region);
            // A weather-control request lands within the same tick it was cast, so the cache key includes the pins.
            if (site.VectorMs == timeMs && site.OverrideCount == overrides.Count) return site;
            if (site.OverrideCount != overrides.Count && site.IntegratedS * 1000 > OldestChange(overrides, site.OverrideCount))
            {
                // A pin that starts before the integrated time changes history: recompute deposits from the start.
                site.IntegratedS = 0;
                Array.Clear(site.Deposit, 0, DepositCount);
            }

            Integrate(site, timeMs, overrides);
            WeatherField.Evaluate(site.Where, timeMs, overrides, site.Vector);
            site.State = WeatherDynamics.Resolve(site.Vector);
            site.VectorMs = timeMs;
            site.OverrideCount = overrides.Count;
            for (int i = 0; i < site.ProfileMs.Length; i++) site.ProfileMs[i] = -1;
            return site;
        }

        private static long OldestChange(IReadOnlyList<WeatherOverride> overrides, int seen)
        {
            long oldest = long.MaxValue;
            for (int i = Math.Max(0, seen); i < overrides.Count; i++) oldest = Math.Min(oldest, overrides[i].StartMs);
            return oldest;
        }

        // REQ: weather.md#地表累積 — 1 s fixed steps in integer permille from t = 0, so a late joiner reproduces the
        // host's deposits exactly instead of receiving them over the network.
        private void Integrate(Site site, long timeMs, IReadOnlyList<WeatherOverride> overrides)
        {
            if (site.Where.Indoor) return;
            long target = timeMs / 1000;
            if (target < site.IntegratedS)
            {
                Array.Clear(site.Deposit, 0, DepositCount);
                site.IntegratedS = 0;
            }

            for (long s = site.IntegratedS + 1; s <= target; s++)
            {
                WeatherField.Evaluate(site.Where, s * 1000, overrides, _scratch);
                int wind = WeatherDynamics.Effective(_scratch, (int)WeatherAttr.Wind);
                Step(site.Deposit, (int)GroundDeposit.Water, WeatherDynamics.Effective(_scratch, (int)WeatherAttr.Rain), 1000);
                int snow = WeatherDynamics.Effective(_scratch, (int)WeatherAttr.Snow);
                // Snow keeps piling while it falls (capacity 1000), unlike rain/sand/ash which settle at their intensity.
                Step(site.Deposit, (int)GroundDeposit.Snow, snow, 1000, snow > 0 ? 1000 : 0);
                int windClear = 1000 + 4 * wind;
                Step(site.Deposit, (int)GroundDeposit.Sand, WeatherDynamics.Effective(_scratch, (int)WeatherAttr.Sand), windClear);
                Step(site.Deposit, (int)GroundDeposit.Ash, WeatherDynamics.Effective(_scratch, (int)WeatherKind.VolcanicAsh), windClear);
            }

            site.IntegratedS = math.max(site.IntegratedS, target);
            for (int i = 0; i < DepositCount; i++)
            {
                site.Cover[i] = math.max(0, (site.Deposit[i] - CoverDelayPermille) * 1000 / (1000 - CoverDelayPermille));
            }
        }

        private static void Step(int[] deposit, int index, int input, int clearSpeedPermille, int capacity = -1)
        {
            int x = deposit[index];
            int cap = capacity >= 0 ? capacity : input;
            if (input > 0)
            {
                long num = (long)(cap - x) * input;
                long den = (long)FillS[index] * 1000;
                // Round away from zero so a small but nonzero input still moves the deposit every second.
                long delta = num >= 0 ? (num + den - 1) / den : (num - den + 1) / den;
                x += (int)delta;
            }
            else if (x > 0)
            {
                long den = (long)ClearS[index] * 1000;
                x -= (int)(((long)x * clearSpeedPermille + den - 1) / den);
            }

            deposit[index] = math.clamp(x, 0, 1000);
        }
    }
}
