using System.Collections.Generic;
using Cosmimera.Core.Contracts;

namespace Cosmimera.WorldGen
{
    /// <summary>Result of a regional weather-control request.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum WeatherControlResult
    {
        Applied,
        MissingSkill,
        OnCooldown,
        /// <summary>The weather cannot be summoned here (exotic not native to the terrain, or base weather indoors).</summary>
        NotHere,
    }

    /// <summary>Pins a region's weather attributes for a while, with a run-wide cooldown.</summary>
    /// <remarks>
    /// REQ: docs/design/field-utilities.md#主動操縱天氣, docs/design/weather.md#天氣操縱.
    /// Pre: one instance per run; callers supply the same simulation clock as the weather queries.
    /// Post: queries do not advance time or draw randomness. Side effects: see members.
    /// </remarks>
    public sealed class WeatherControl
    {
        private static readonly IReadOnlyList<WeatherOverride> None = new WeatherOverride[0];

        // Every override is kept, not only the latest: ground deposits integrate history, and a recompute from t = 0
        // must see the same pins the live run saw.
        private readonly Dictionary<int, List<WeatherOverride>> _regions = new Dictionary<int, List<WeatherOverride>>();
        private readonly int _cooldownMs;
        private bool _hasControlled;
        private long _lastControlMs;

        /// <summary>Creates a controller with the configured cooldown.</summary>
        /// <remarks>Pre: cooldownMs greater than zero. Post: no overrides or cooldown. Side effects: allocates.</remarks>
        public WeatherControl(int cooldownMs)
        {
            Contract.Requires(cooldownMs > 0, "cooldownMs must be > 0");
            _cooldownMs = cooldownMs;
        }

        /// <summary>Summons <paramref name="weather"/> in the region for <see cref="WeatherField.OverrideHoldMs"/>.</summary>
        /// <remarks>
        /// Pre: region &gt;= 0; weather defined; nowMs nonnegative and not before the last successful request.
        /// Post: MissingSkill when canControl is false; NotHere when an exotic is not native (nativeExotic false) or a base
        ///       weather is requested indoors; otherwise OnCooldown while cooldown remains, else Applied. Refusals change
        ///       nothing. A base summon pins the eight base attributes to its preset; an exotic summon pins that exotic to
        ///       100 and the other exotics to 0. Applied starts the shared cooldown.
        /// Side effects: on Applied appends an override and updates the timestamp.
        /// </remarks>
        public WeatherControlResult TryApply(int region, WeatherKind weather, long nowMs, bool canControl, bool nativeExotic,
            bool indoor)
        {
            Contract.Requires(region >= 0, "region must be >= 0");
            Contract.Requires(weather >= WeatherKind.Clear && weather <= WeatherKind.Miasma, "weather must be defined");
            RequireTime(nowMs);
            if (!canControl) return WeatherControlResult.MissingSkill;
            bool exotic = WeatherProfiles.IsExotic(weather);
            if (exotic ? !nativeExotic : indoor) return WeatherControlResult.NotHere;
            if (CooldownRemainingMs(nowMs) > 0) return WeatherControlResult.OnCooldown;

            var values = new int[WeatherField.AttrCount];
            uint mask;
            if (exotic)
            {
                mask = ExoticMask;
                values[(int)weather] = 1000;
            }
            else
            {
                mask = (1u << WeatherTables.BaseCount) - 1;
                int[] preset = WeatherTables.Presets[WeatherTables.PresetFor(weather)].values;
                for (int i = 0; i < WeatherTables.BaseCount; i++) values[i] = preset[i] * 10;
            }

            Add(region, new WeatherOverride(nowMs, mask, values));
            return WeatherControlResult.Applied;
        }

        /// <summary>Clears every exotic in the region for <see cref="WeatherField.OverrideHoldMs"/>.</summary>
        /// <remarks>
        /// Pre: region &gt;= 0; nowMs nonnegative and not before the last successful request.
        /// Post: MissingSkill / OnCooldown as <see cref="TryApply"/>, else Applied and the shared cooldown starts.
        /// Side effects: on Applied appends an override and updates the timestamp.
        /// </remarks>
        public WeatherControlResult TryDispelExotics(int region, long nowMs, bool canControl)
        {
            Contract.Requires(region >= 0, "region must be >= 0");
            RequireTime(nowMs);
            if (!canControl) return WeatherControlResult.MissingSkill;
            if (CooldownRemainingMs(nowMs) > 0) return WeatherControlResult.OnCooldown;
            Add(region, new WeatherOverride(nowMs, ExoticMask, new int[WeatherField.AttrCount]));
            return WeatherControlResult.Applied;
        }

        /// <summary>All overrides ever applied to <paramref name="region"/>, oldest first.</summary>
        /// <remarks>Pre: region &gt;= 0. Post: never null; StartMs non-decreasing. Side effects: none.</remarks>
        public IReadOnlyList<WeatherOverride> OverridesFor(int region)
        {
            Contract.Requires(region >= 0, "region must be >= 0");
            return _regions.TryGetValue(region, out List<WeatherOverride> list) ? list : None;
        }

        /// <summary>Milliseconds the region's latest override still pins its weather (0 when natural).</summary>
        /// <remarks>
        /// Pre: region &gt;= 0; nowMs &gt;= 0.
        /// Post: result in 0..OverrideHoldMs. Side effects: none.
        /// </remarks>
        public int PinnedRemainingMs(int region, long nowMs)
        {
            Contract.Requires(region >= 0 && nowMs >= 0, "region and nowMs must be >= 0");
            if (!_regions.TryGetValue(region, out List<WeatherOverride> list)) return 0;
            long end = list[list.Count - 1].StartMs + WeatherField.OverrideHoldMs;
            return nowMs >= end || nowMs < list[list.Count - 1].StartMs ? 0 : (int)(end - nowMs);
        }

        /// <summary>Milliseconds until another weather-control request is permitted.</summary>
        /// <remarks>
        /// Pre: nowMs nonnegative and not before the last successful request.
        /// Post: result in 0..configured cooldown, zero when ready. Side effects: none.
        /// </remarks>
        public int CooldownRemainingMs(long nowMs)
        {
            RequireTime(nowMs);
            if (!_hasControlled || nowMs - _lastControlMs >= _cooldownMs) return 0;
            return _cooldownMs - (int)(nowMs - _lastControlMs);
        }

        private const uint ExoticMask = ((1u << WeatherField.AttrCount) - 1) & ~((1u << WeatherTables.BaseCount) - 1);

        private void Add(int region, WeatherOverride value)
        {
            if (!_regions.TryGetValue(region, out List<WeatherOverride> list))
            {
                list = new List<WeatherOverride>();
                _regions[region] = list;
            }

            list.Add(value);
            _lastControlMs = value.StartMs;
            _hasControlled = true;
        }

        private void RequireTime(long nowMs) =>
            Contract.Requires(nowMs >= 0 && (!_hasControlled || nowMs >= _lastControlMs),
                "nowMs must be nonnegative and not precede the last successful request");
    }
}
