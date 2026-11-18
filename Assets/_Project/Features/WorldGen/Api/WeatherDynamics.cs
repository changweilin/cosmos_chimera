using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.WorldGen
{
    /// <summary>What the weather vector means right now: effective strengths and the dominant labels.</summary>
    /// <remarks>REQ: docs/design/weather.md#生效. Pre: built by <see cref="WeatherDynamics.Resolve"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct WeatherState
    {
        internal WeatherState(WeatherKind baseKind, int baseStrength, WeatherKind exotic, int exoticStrength, bool overcast,
            int rain, int snow, int sand, int fog, int thunder, int wind, int ash, int clouds)
        {
            BaseKind = baseKind;
            BaseStrengthPermille = baseStrength;
            Exotic = exotic;
            ExoticStrengthPermille = exoticStrength;
            Overcast = overcast;
            RainPermille = rain;
            SnowPermille = snow;
            SandPermille = sand;
            FogPermille = fog;
            ThunderPermille = thunder;
            WindPermille = wind;
            AshPermille = ash;
            CloudsPermille = clouds;
        }

        /// <summary>Dominant base weather (Clear when none reaches 400 ‰).</summary>
        /// <remarks>Pre: none. Post: Clear..Gale. Side effects: none.</remarks>
        public WeatherKind BaseKind { get; }

        /// <summary>Effective strength of <see cref="BaseKind"/>.</summary>
        /// <remarks>Pre: none. Post: 0 when Clear, else 400..1000. Side effects: none.</remarks>
        public int BaseStrengthPermille { get; }

        /// <summary>Strongest active exotic, or Clear.</summary>
        /// <remarks>Pre: none. Post: Clear or RadDust..Miasma. Side effects: none.</remarks>
        public WeatherKind Exotic { get; }

        /// <summary>Effective strength of <see cref="Exotic"/>.</summary>
        /// <remarks>Pre: none. Post: 0 when Clear, else 400..1000. Side effects: none.</remarks>
        public int ExoticStrengthPermille { get; }

        /// <summary>Cloud cover above 50 (HUD "overcast"; rules treat it as clear).</summary>
        /// <remarks>Pre: none. Post: as resolved. Side effects: none.</remarks>
        public bool Overcast { get; }

        /// <summary>Effective rain (needs dark cloud), 0..1000.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int RainPermille { get; }

        /// <summary>Effective snow (needs dark cloud), 0..1000.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int SnowPermille { get; }

        /// <summary>Effective sand, 0..1000.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int SandPermille { get; }

        /// <summary>Effective fog, 0..1000.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int FogPermille { get; }

        /// <summary>Effective thunder, 0..1000.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int ThunderPermille { get; }

        /// <summary>Effective wind, 0..1000.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int WindPermille { get; }

        /// <summary>Effective volcanic ash, 0..1000.</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int AshPermille { get; }

        /// <summary>Raw cloud cover, 0..1000 (presentation darkens the sky by it).</summary>
        /// <remarks>Pre: none. Post: 0..1000. Side effects: none.</remarks>
        public int CloudsPermille { get; }
    }

    /// <summary>
    /// Turns weather vectors into effective strengths and composes the rule-layer <see cref="WeatherProfile"/>.
    /// REQ: docs/design/weather.md#生效, #時段, #地表累積.
    /// </summary>
    /// <remarks>Pre: none. Post: pure and deterministic. Side effects: none.</remarks>
    public static class WeatherDynamics
    {
        /// <summary>Attribute value (tenths) at which an attribute starts to take effect.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int ThresholdTenths = 750;

        /// <summary>Effective strength a weather needs to become dominant.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int DominantPermille = 400;

        /// <summary>Night visibility cap in metres.</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const float NightVisibilityM = 40f;

        // Base priority (weather.md#生效): blizzard → sandstorm → thunderstorm → rain → fog → heatwave → gale.
        private static readonly (WeatherKind kind, int attr)[] BasePriority =
        {
            (WeatherKind.Blizzard, (int)WeatherAttr.Snow),
            (WeatherKind.Sandstorm, (int)WeatherAttr.Sand),
            (WeatherKind.Thunderstorm, (int)WeatherAttr.Thunder),
            (WeatherKind.Rain, (int)WeatherAttr.Rain),
            (WeatherKind.Fog, (int)WeatherAttr.Fog),
            (WeatherKind.Heatwave, (int)WeatherAttr.Heat),
            (WeatherKind.Gale, (int)WeatherAttr.Wind),
        };

        /// <summary>Effective strength (0..1000) of attribute <paramref name="attr"/> in <paramref name="vector"/>.</summary>
        /// <remarks>
        /// Pre: vector holds WeatherField.AttrCount entries in 0..1000; 0 &lt;= attr &lt; AttrCount.
        /// Post: 0 below the 75 threshold; rain and snow also scale by cloud darkness (0 at clouds &lt;= 50). Side effects: none.
        /// </remarks>
        public static int Effective(int[] vector, int attr)
        {
            Contract.Requires(vector != null && vector.Length >= WeatherField.AttrCount, "vector must hold AttrCount entries");
            Contract.Requires(attr >= 0 && attr < WeatherField.AttrCount, "attr out of range");
            int v = vector[attr];
            int eff = v >= ThresholdTenths ? (v - ThresholdTenths) * 1000 / (1000 - ThresholdTenths) : 0;
            if (attr == (int)WeatherAttr.Rain || attr == (int)WeatherAttr.Snow)
            {
                int clouds = vector[(int)WeatherAttr.Clouds];
                int dark = clouds > 500 ? (clouds - 500) * 1000 / 500 : 0;
                eff = eff * dark / 1000;
            }

            return eff;
        }

        /// <summary>Resolves dominant base weather, strongest exotic, and effective strengths.</summary>
        /// <remarks>
        /// Pre: vector holds WeatherField.AttrCount entries in 0..1000.
        /// Post: BaseKind is the first of the priority list at &gt;= 400 ‰ (else Clear); Exotic is the strongest exotic at
        ///       &gt;= 400 ‰ (ties → lower kind), else Clear. Side effects: none.
        /// </remarks>
        public static WeatherState Resolve(int[] vector)
        {
            Contract.Requires(vector != null && vector.Length >= WeatherField.AttrCount, "vector must hold AttrCount entries");
            WeatherKind baseKind = WeatherKind.Clear;
            int baseStrength = 0;
            foreach ((WeatherKind kind, int attr) in BasePriority)
            {
                int eff = Effective(vector, attr);
                if (eff < DominantPermille) continue;
                baseKind = kind;
                baseStrength = eff;
                break;
            }

            WeatherKind exotic = WeatherKind.Clear;
            int exoticStrength = 0;
            for (int i = (int)WeatherProfiles.FirstExotic; i < WeatherField.AttrCount; i++)
            {
                int eff = Effective(vector, i);
                if (eff < DominantPermille || eff <= exoticStrength) continue;
                exotic = (WeatherKind)i;
                exoticStrength = eff;
            }

            return new WeatherState(baseKind, baseStrength, exotic, exoticStrength, vector[(int)WeatherAttr.Clouds] > 500,
                Effective(vector, (int)WeatherAttr.Rain), Effective(vector, (int)WeatherAttr.Snow),
                Effective(vector, (int)WeatherAttr.Sand), Effective(vector, (int)WeatherAttr.Fog),
                Effective(vector, (int)WeatherAttr.Thunder), Effective(vector, (int)WeatherAttr.Wind),
                Effective(vector, (int)WeatherKind.VolcanicAsh), vector[(int)WeatherAttr.Clouds]);
        }

        /// <summary>Composes the rule-layer profile for one place: weather scaled by strength, stacked, plus day phase.</summary>
        /// <remarks>
        /// REQ: docs/design/weather.md#生效 — multipliers 1 + (row − 1) × s, percents round(row × s); base and exotic stack
        ///      (multipliers multiply, percents add, visibility takes the smaller); #時段 — day/night Light and Shadow shifts and
        ///      the 40 m night cap, both skipped indoors.
        /// Pre: coverPermille null or 4 entries in 0..1000 (GroundDeposit order).
        /// Post: all multipliers &gt; 0; power deltas in [-90, 100]; CD deltas in [-50, 50]; Kind = Exotic when one is active.
        /// Side effects: allocates two small scratch arrays.
        /// </remarks>
        public static WeatherProfile Compose(in WeatherState state, DayPhase phase, bool indoor, int[] coverPermille)
        {
            Contract.Requires(coverPermille == null || coverPermille.Length == 4, "coverPermille must hold 4 entries");
            WeatherProfile b = WeatherProfiles.For(state.BaseKind);
            WeatherProfile x = WeatherProfiles.For(state.Exotic);
            float sb = state.BaseStrengthPermille / 1000f;
            float sx = state.ExoticStrengthPermille / 1000f;

            var power = new int[WeatherProfiles.ElementCount];
            var cd = new int[WeatherProfiles.ElementCount];
            for (int i = 0; i < WeatherProfiles.ElementCount; i++)
            {
                var e = (Element)i;
                power[i] = Scale(b.PowerDeltaPercent(e), state.BaseStrengthPermille) + Scale(x.PowerDeltaPercent(e), state.ExoticStrengthPermille);
                cd[i] = Scale(b.CdDeltaPercent(e), state.BaseStrengthPermille) + Scale(x.CdDeltaPercent(e), state.ExoticStrengthPermille);
            }

            float visibility = System.Math.Min(Lerp(WeatherProfiles.ClearVisibilityM, b.PlayerVisibilityRadiusM, sb),
                Lerp(WeatherProfiles.ClearVisibilityM, x.PlayerVisibilityRadiusM, sx));
            if (!indoor)
            {
                // REQ: weather.md#時段 — day favours Light, night favours Shadow; dawn and dusk are neutral.
                int sign = phase == DayPhase.Day ? 1 : phase == DayPhase.Night ? -1 : 0;
                power[(int)Element.Light] += 10 * sign;
                cd[(int)Element.Light] -= 5 * sign;
                power[(int)Element.Shadow] -= 10 * sign;
                cd[(int)Element.Shadow] += 5 * sign;
                if (phase == DayPhase.Night) visibility = System.Math.Min(visibility, NightVisibilityM);
            }

            for (int i = 0; i < WeatherProfiles.ElementCount; i++)
            {
                power[i] = System.Math.Clamp(power[i], -90, 100);
                cd[i] = System.Math.Clamp(cd[i], -50, 50);
            }

            int territory = (int)((long)LerpInt(1000, b.TerritoryRadiusPermille, state.BaseStrengthPermille)
                * LerpInt(1000, x.TerritoryRadiusPermille, state.ExoticStrengthPermille) / 1000);
            WeatherKind kind = state.Exotic != WeatherKind.Clear ? state.Exotic : state.BaseKind;
            return new WeatherProfile(kind, state.Exotic,
                Lerp(1f, b.PlayerMoveSpeedMultiplier, sb) * Lerp(1f, x.PlayerMoveSpeedMultiplier, sx),
                Lerp(1f, b.PlayerSprintSpeedMultiplier, sb) * Lerp(1f, x.PlayerSprintSpeedMultiplier, sx),
                Lerp(1f, b.PlayerRollCooldownMultiplier, sb) * Lerp(1f, x.PlayerRollCooldownMultiplier, sx),
                visibility,
                Lerp(1f, b.CreatureMoveSpeedMultiplier, sb) * Lerp(1f, x.CreatureMoveSpeedMultiplier, sx),
                Lerp(1f, b.AdaptedCreatureMoveSpeedMultiplier, sb) * Lerp(1f, x.AdaptedCreatureMoveSpeedMultiplier, sx),
                System.Math.Max(1, territory),
                b.StealthEnabled && state.BaseStrengthPermille > 0,
                b.ProjectileDriftDegrees * sb + x.ProjectileDriftDegrees * sx,
                power, cd, coverPermille);
        }

        private static int Scale(int percent, int strengthPermille)
        {
            // Round half away from zero so +25 % at full strength never drifts to +24 under scaling.
            long scaled = (long)percent * strengthPermille;
            return (int)(scaled >= 0 ? (scaled + 500) / 1000 : (scaled - 500) / 1000);
        }

        private static float Lerp(float from, float to, float t) => from + (to - from) * t;

        private static int LerpInt(int from, int to, int permille) => from + (to - from) * permille / 1000;
    }
}
