using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.WorldGen
{
    /// <summary>Dynamic atmospheric weather states on a planet surface.</summary>
    /// <remarks>Pre: none. Post: stable enum values. Side effects: none.</remarks>
    public enum WeatherKind
    {
        /// <summary>Baseline clear sky with no weather modifiers.</summary>
        Clear = 0,
        /// <summary>Dense mist shrinking wild territory and enabling slow-walk stealth (Q-C37).</summary>
        Fog = 1,
        /// <summary>Rainfall boosting water/wood and suppressing fire.</summary>
        Rain = 2,
        /// <summary>Electrical storm boosting thunder/water and slightly drifting ranged shots.</summary>
        Thunderstorm = 3,
        /// <summary>Freezing snowstorm slowing non-ice movement and drifting ranged shots.</summary>
        Blizzard = 4,
        /// <summary>Abrasive dust storm slowing movement, shrinking territory, and boosting earth/metal.</summary>
        Sandstorm = 5,
        /// <summary>Scorching heat taxing player sprint/roll and boosting fire/light.</summary>
        Heatwave = 6,
        /// <summary>High winds boosting wind moves and strongly drifting ranged shots.</summary>
        Gale = 7,
    }

    /// <summary>
    /// Differentiated weather modifiers for players, creatures, wild territory, and elemental moves.
    /// REQ: docs/design/planet-world.md#天氣影響.
    /// </summary>
    /// <remarks>Pre: obtained from <see cref="WeatherProfiles"/>. Post: immutable value. Side effects: none.</remarks>
    public readonly struct WeatherProfile
    {
        internal WeatherProfile(
            WeatherKind kind,
            float playerMoveSpeedMultiplier,
            float playerSprintSpeedMultiplier,
            float playerRollCooldownMultiplier,
            float playerVisibilityRadiusM,
            float creatureMoveSpeedMultiplier,
            float adaptedCreatureMoveSpeedMultiplier,
            int territoryRadiusPermille,
            bool stealthEnabled,
            float projectileDriftDegrees)
        {
            Kind = kind;
            PlayerMoveSpeedMultiplier = playerMoveSpeedMultiplier;
            PlayerSprintSpeedMultiplier = playerSprintSpeedMultiplier;
            PlayerRollCooldownMultiplier = playerRollCooldownMultiplier;
            PlayerVisibilityRadiusM = playerVisibilityRadiusM;
            CreatureMoveSpeedMultiplier = creatureMoveSpeedMultiplier;
            AdaptedCreatureMoveSpeedMultiplier = adaptedCreatureMoveSpeedMultiplier;
            TerritoryRadiusPermille = territoryRadiusPermille;
            StealthEnabled = stealthEnabled;
            ProjectileDriftDegrees = projectileDriftDegrees;
        }

        /// <summary>Weather kind identifier.</summary>
        /// <remarks>Pre: none. Post: defined <see cref="WeatherKind"/>. Side effects: none.</remarks>
        public WeatherKind Kind { get; }

        /// <summary>Multiplier on player base walk speed (&gt; 0).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float PlayerMoveSpeedMultiplier { get; }

        /// <summary>Multiplier on player sprint speed (&gt; 0).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float PlayerSprintSpeedMultiplier { get; }

        /// <summary>Multiplier on player dodge-roll cooldown (&gt; 0; &gt; 1 slows roll recovery).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float PlayerRollCooldownMultiplier { get; }

        /// <summary>Player visibility cap in metres (&gt; 0).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float PlayerVisibilityRadiusM { get; }

        /// <summary>Multiplier on non-adapted creature movement speed (&gt; 0).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float CreatureMoveSpeedMultiplier { get; }

        /// <summary>Multiplier on movement speed for creatures with mastery in a favoured element (&gt; 0).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float AdaptedCreatureMoveSpeedMultiplier { get; }

        /// <summary>Permille scale applied to wild creature territory radius (&gt; 0; 1000 = 100%).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public int TerritoryRadiusPermille { get; }

        /// <summary>True when stationary or slow-moving players and allied creatures gain concealment (Q-C37).</summary>
        /// <remarks>Pre: none. Post: true only for Fog in the base table. Side effects: none.</remarks>
        public bool StealthEnabled { get; }

        /// <summary>Maximum angular drift (degrees, &gt;= 0) applied to mid/long-range attacks.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public float ProjectileDriftDegrees { get; }

        /// <summary>Relative percentage modifier on move power for <paramref name="element"/> (e.g. +20 or -20).</summary>
        /// <remarks>
        /// Pre: <paramref name="element"/> is a defined <see cref="Element"/>.
        /// Post: result in [-90, 100].
        /// Side effects: none.
        /// </remarks>
        public int PowerDeltaPercent(Element element)
        {
            Contract.Requires(element >= Element.Metal && element <= Element.Shadow, "undefined Element");
            return Kind switch
            {
                WeatherKind.Fog => element switch
                {
                    Element.Shadow => 15,
                    Element.Light => -15,
                    _ => 0,
                },
                WeatherKind.Rain => element switch
                {
                    Element.Water => 20,
                    Element.Wood => 10,
                    Element.Fire => -20,
                    _ => 0,
                },
                WeatherKind.Thunderstorm => element switch
                {
                    Element.Thunder => 25,
                    Element.Water => 15,
                    Element.Earth => -10,
                    _ => 0,
                },
                WeatherKind.Blizzard => element switch
                {
                    Element.Ice => 25,
                    Element.Fire => -20,
                    Element.Wood => -15,
                    _ => 0,
                },
                WeatherKind.Sandstorm => element switch
                {
                    Element.Earth => 20,
                    Element.Metal => 15,
                    Element.Light => -15,
                    Element.Water => -10,
                    _ => 0,
                },
                WeatherKind.Heatwave => element switch
                {
                    Element.Fire => 25,
                    Element.Light => 10,
                    Element.Ice => -20,
                    Element.Water => -15,
                    _ => 0,
                },
                WeatherKind.Gale => element == Element.Wind ? 25 : 0,
                _ => 0,
            };
        }

        /// <summary>Relative percentage modifier on move cooldown for <paramref name="element"/> (negative = faster CD).</summary>
        /// <remarks>
        /// Pre: <paramref name="element"/> is a defined <see cref="Element"/>.
        /// Post: result in [-50, 50].
        /// Side effects: none.
        /// </remarks>
        public int CdDeltaPercent(Element element)
        {
            Contract.Requires(element >= Element.Metal && element <= Element.Shadow, "undefined Element");
            return Kind switch
            {
                WeatherKind.Fog => element switch
                {
                    Element.Shadow => -10,
                    Element.Light => 10,
                    _ => 0,
                },
                WeatherKind.Rain => element switch
                {
                    Element.Water => -15,
                    Element.Wood => -10,
                    Element.Fire => 15,
                    _ => 0,
                },
                WeatherKind.Thunderstorm => element == Element.Thunder ? -15 : 0,
                WeatherKind.Blizzard => element switch
                {
                    Element.Ice => -15,
                    Element.Fire => 15,
                    _ => 0,
                },
                WeatherKind.Sandstorm => element == Element.Earth ? -10 : 0,
                WeatherKind.Heatwave => element switch
                {
                    Element.Fire => -15,
                    Element.Ice => 15,
                    _ => 0,
                },
                WeatherKind.Gale => element == Element.Wind ? -15 : 0,
                _ => 0,
            };
        }

        /// <summary>True when <paramref name="element"/> is favoured by this weather (positive power or faster CD).</summary>
        /// <remarks>
        /// Pre: <paramref name="element"/> is a defined <see cref="Element"/>.
        /// Post: true iff PowerDeltaPercent(element) &gt; 0 || CdDeltaPercent(element) &lt; 0.
        /// Side effects: none.
        /// </remarks>
        public bool IsFavouredElement(Element element)
        {
            Contract.Requires(element >= Element.Metal && element <= Element.Shadow, "undefined Element");
            return PowerDeltaPercent(element) > 0 || CdDeltaPercent(element) < 0;
        }
    }

    /// <summary>
    /// Lookup catalog for canonical <see cref="WeatherProfile"/> definitions.
    /// REQ: docs/design/planet-world.md#天氣影響.
    /// </summary>
    /// <remarks>Pre: none. Post: pure and deterministic. Side effects: none.</remarks>
    public static class WeatherProfiles
    {
        /// <summary>Clear sky baseline.</summary>
        public static readonly WeatherProfile Clear = new WeatherProfile(
            WeatherKind.Clear, 1.00f, 1.00f, 1.00f, 1000f, 1.00f, 1.00f, 1000, false, 0f);

        /// <summary>Dense fog shrinking wild territory and enabling slow-walk concealment (Q-C37).</summary>
        public static readonly WeatherProfile Fog = new WeatherProfile(
            WeatherKind.Fog, 0.95f, 0.95f, 1.00f, 18f, 0.95f, 1.05f, 600, true, 0f);

        /// <summary>Rainfall.</summary>
        public static readonly WeatherProfile Rain = new WeatherProfile(
            WeatherKind.Rain, 0.96f, 0.94f, 1.00f, 45f, 0.95f, 1.08f, 900, false, 0f);

        /// <summary>Thunderstorm.</summary>
        public static readonly WeatherProfile Thunderstorm = new WeatherProfile(
            WeatherKind.Thunderstorm, 0.92f, 0.90f, 1.05f, 35f, 0.90f, 1.10f, 850, false, 5f);

        /// <summary>Blizzard.</summary>
        public static readonly WeatherProfile Blizzard = new WeatherProfile(
            WeatherKind.Blizzard, 0.80f, 0.78f, 1.15f, 22f, 0.82f, 1.10f, 700, false, 8f);

        /// <summary>Sandstorm.</summary>
        public static readonly WeatherProfile Sandstorm = new WeatherProfile(
            WeatherKind.Sandstorm, 0.82f, 0.80f, 1.10f, 20f, 0.84f, 1.06f, 650, false, 8f);

        /// <summary>Heatwave.</summary>
        public static readonly WeatherProfile Heatwave = new WeatherProfile(
            WeatherKind.Heatwave, 0.92f, 0.85f, 1.15f, 60f, 0.94f, 1.08f, 1000, false, 0f);

        /// <summary>Strong wind / gale.</summary>
        public static readonly WeatherProfile Gale = new WeatherProfile(
            WeatherKind.Gale, 0.88f, 0.85f, 1.00f, 70f, 0.86f, 1.12f, 900, false, 12f);

        private static readonly WeatherProfile[] AllProfiles =
        {
            Clear, Fog, Rain, Thunderstorm, Blizzard, Sandstorm, Heatwave, Gale,
        };

        /// <summary>All canonical weather profiles indexed by <see cref="WeatherKind"/>.</summary>
        /// <remarks>Pre: none. Post: 8 profiles. Side effects: none.</remarks>
        public static IReadOnlyList<WeatherProfile> All => AllProfiles;

        /// <summary>Returns the <see cref="WeatherProfile"/> for <paramref name="kind"/>.</summary>
        /// <remarks>
        /// Pre: <paramref name="kind"/> is a defined <see cref="WeatherKind"/>.
        /// Post: returned profile has <see cref="WeatherProfile.Kind"/> == <paramref name="kind"/>.
        /// Side effects: none.
        /// </remarks>
        public static WeatherProfile For(WeatherKind kind)
        {
            Contract.Requires(kind >= WeatherKind.Clear && kind <= WeatherKind.Gale, "undefined WeatherKind");
            return AllProfiles[(int)kind];
        }
    }
}
