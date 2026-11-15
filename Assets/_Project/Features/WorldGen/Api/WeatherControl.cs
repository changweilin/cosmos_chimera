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
    }

    /// <summary>Overrides a region's weather until the next natural slot, with a run-wide cooldown.</summary>
    /// <remarks>
    /// REQ: docs/design/field-utilities.md#主動操縱天氣.
    /// Pre: one instance per run; callers supply the same simulation clock as WeatherPlanner.
    /// Post: queries do not advance time or draw randomness. Side effects: see members.
    /// </remarks>
    public sealed class WeatherControl
    {
        /// <summary>Canonical weather and the natural slot in which it was applied.</summary>
        /// <remarks>Pre: kind defined; slot nonnegative. Post: immutable. Side effects: none.</remarks>
        private readonly struct Override
        {
            internal readonly WeatherKind Kind;
            internal readonly long Slot;
            internal Override(WeatherKind kind, long slot) { Kind = kind; Slot = slot; }
        }

        private readonly Dictionary<int, Override> _regions = new Dictionary<int, Override>();
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

        /// <summary>Sets weather in the selected region until the next natural slot.</summary>
        /// <remarks>
        /// Pre: region at least zero; weather defined; nowMs nonnegative and not before the last successful request.
        /// Post: MissingSkill when canControl is false; otherwise OnCooldown while cooldown remains, else Applied.
        ///       Refusals leave overrides and cooldown unchanged. Applied starts the shared cooldown at nowMs.
        /// Side effects: on Applied updates the region override and timestamp; may grow the dictionary.
        /// </remarks>
        public WeatherControlResult TryApply(int region, WeatherKind weather, long nowMs, bool canControl)
        {
            Contract.Requires(region >= 0, "region must be >= 0");
            Contract.Requires(weather >= WeatherKind.Clear && weather <= WeatherKind.Gale, "weather must be defined");
            RequireTime(nowMs);
            if (!canControl) return WeatherControlResult.MissingSkill;
            if (CooldownRemainingMs(nowMs) > 0) return WeatherControlResult.OnCooldown;
            _regions[region] = new Override(weather, nowMs / WeatherPlanner.DefaultSlotDurationMs);
            _lastControlMs = nowMs;
            _hasControlled = true;
            return WeatherControlResult.Applied;
        }

        /// <summary>Returns an unexpired regional override.</summary>
        /// <remarks>
        /// Pre: region at least zero; nowMs nonnegative and not before the last successful request.
        /// Post: true only in the slot when this region was controlled; weather is its canonical profile, else Clear.
        /// Side effects: none.
        /// </remarks>
        public bool TryGetOverride(int region, long nowMs, out WeatherProfile weather)
        {
            Contract.Requires(region >= 0, "region must be >= 0");
            RequireTime(nowMs);
            if (_regions.TryGetValue(region, out Override value) && value.Slot == nowMs / WeatherPlanner.DefaultSlotDurationMs)
            {
                weather = WeatherProfiles.For(value.Kind);
                return true;
            }
            weather = WeatherProfiles.Clear;
            return false;
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

        private void RequireTime(long nowMs) =>
            Contract.Requires(nowMs >= 0 && (!_hasControlled || nowMs >= _lastControlMs),
                "nowMs must be nonnegative and not precede the last successful request");
    }
}
