using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.WorldGen
{
    /// <summary>Weather labels: eight base weathers and ten terrain-only phenomena (one per element).</summary>
    /// <remarks>
    /// REQ: docs/design/weather.md#屬性.
    /// Pre: none. Post: stable values (saves and peers compare them); base kinds are 0..7, exotic kinds 8..17.
    /// Side effects: none.
    /// </remarks>
    public enum WeatherKind
    {
        /// <summary>Baseline clear sky with no weather modifiers.</summary>
        Clear = 0,
        /// <summary>Dense mist shrinking wild territory and enabling slow-walk stealth (Q-C37).</summary>
        Fog = 1,
        Rain = 2,
        Thunderstorm = 3,
        Blizzard = 4,
        Sandstorm = 5,
        Heatwave = 6,
        Gale = 7,
        /// <summary>輻射塵 (Metal).</summary>
        RadDust = 8,
        /// <summary>孢子霧 (Wood).</summary>
        SporeHaze = 9,
        /// <summary>鹽霧 (Water).</summary>
        SaltSpray = 10,
        /// <summary>火山灰 (Fire).</summary>
        VolcanicAsh = 11,
        /// <summary>晶塵 (Earth).</summary>
        CrystalDust = 12,
        /// <summary>鑽石塵 (Ice).</summary>
        DiamondDust = 13,
        /// <summary>龍捲風 (Wind).</summary>
        Tornado = 14,
        /// <summary>離子風暴 (Thunder).</summary>
        IonStorm = 15,
        /// <summary>太陽閃焰 (Light).</summary>
        SolarFlare = 16,
        /// <summary>瘴氣 (Shadow).</summary>
        Miasma = 17,
    }

    /// <summary>Material that weather leaves on the ground.</summary>
    /// <remarks>REQ: docs/design/weather.md#地表累積. Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum GroundDeposit
    {
        Water = 0,
        Snow = 1,
        Sand = 2,
        Ash = 3,
    }

    /// <summary>
    /// Resolved weather modifiers for players, creatures, wild territory, and elemental moves at one place and time.
    /// REQ: docs/design/planet-world.md#天氣影響, docs/design/weather.md#生效.
    /// </summary>
    /// <remarks>
    /// Pre: obtained from <see cref="WeatherProfiles"/> or <see cref="WeatherDynamics"/>.
    /// Post: immutable value; never allocates. Side effects: none.
    /// </remarks>
    public readonly struct WeatherProfile
    {
        private const int DepositCount = 4;

        // Ten signed byte deltas packed into 8 + 2 bytes so the struct stays allocation-free on the per-unit tick path.
        private readonly ulong _powerLo, _cdLo;
        private readonly ushort _powerHi, _cdHi;
        private readonly uint _cover;

        internal WeatherProfile(
            WeatherKind kind,
            WeatherKind exotic,
            float playerMoveSpeedMultiplier,
            float playerSprintSpeedMultiplier,
            float playerRollCooldownMultiplier,
            float playerVisibilityRadiusM,
            float creatureMoveSpeedMultiplier,
            float adaptedCreatureMoveSpeedMultiplier,
            int territoryRadiusPermille,
            bool stealthEnabled,
            float projectileDriftDegrees,
            int[] powerDelta,
            int[] cdDelta,
            int[] coverPermille)
        {
            Kind = kind;
            Exotic = exotic;
            PlayerMoveSpeedMultiplier = playerMoveSpeedMultiplier;
            PlayerSprintSpeedMultiplier = playerSprintSpeedMultiplier;
            PlayerRollCooldownMultiplier = playerRollCooldownMultiplier;
            PlayerVisibilityRadiusM = playerVisibilityRadiusM;
            CreatureMoveSpeedMultiplier = creatureMoveSpeedMultiplier;
            AdaptedCreatureMoveSpeedMultiplier = adaptedCreatureMoveSpeedMultiplier;
            TerritoryRadiusPermille = territoryRadiusPermille;
            StealthEnabled = stealthEnabled;
            ProjectileDriftDegrees = projectileDriftDegrees;
            Pack(powerDelta, out _powerLo, out _powerHi);
            Pack(cdDelta, out _cdLo, out _cdHi);
            uint cover = 0;
            for (int i = 0; i < DepositCount; i++)
            {
                // Permille / 4 fits a byte; the 4 ‰ resolution is far below the 12.5 % maximum slow it drives.
                int c = coverPermille == null ? 0 : coverPermille[i];
                cover |= (uint)(System.Math.Clamp(c, 0, 1000) / 4) << (8 * i);
            }

            _cover = cover;
        }

        /// <summary>Dominant weather label (exotic when one is active, else the base weather).</summary>
        /// <remarks>Pre: none. Post: defined <see cref="WeatherKind"/>. Side effects: none.</remarks>
        public WeatherKind Kind { get; }

        /// <summary>Active terrain-only phenomenon, or <see cref="WeatherKind.Clear"/> when none.</summary>
        /// <remarks>Pre: none. Post: Clear or an exotic kind (&gt;= RadDust). Side effects: none.</remarks>
        public WeatherKind Exotic { get; }

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
        /// <remarks>Pre: none. Post: true only while fog is the base weather. Side effects: none.</remarks>
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
            RequireElement(element);
            return Unpack(_powerLo, _powerHi, (int)element);
        }

        /// <summary>Relative percentage modifier on move cooldown for <paramref name="element"/> (negative = faster CD).</summary>
        /// <remarks>
        /// Pre: <paramref name="element"/> is a defined <see cref="Element"/>.
        /// Post: result in [-50, 50].
        /// Side effects: none.
        /// </remarks>
        public int CdDeltaPercent(Element element)
        {
            RequireElement(element);
            return Unpack(_cdLo, _cdHi, (int)element);
        }

        /// <summary>True when <paramref name="element"/> is favoured by this weather (positive power or faster CD).</summary>
        /// <remarks>
        /// Pre: <paramref name="element"/> is a defined <see cref="Element"/>.
        /// Post: true iff PowerDeltaPercent(element) &gt; 0 || CdDeltaPercent(element) &lt; 0.
        /// Side effects: none.
        /// </remarks>
        public bool IsFavouredElement(Element element)
        {
            RequireElement(element);
            return PowerDeltaPercent(element) > 0 || CdDeltaPercent(element) < 0;
        }

        /// <summary>How much of the ground <paramref name="deposit"/> covers, in permille.</summary>
        /// <remarks>
        /// REQ: docs/design/weather.md#地表累積.
        /// Pre: deposit is a defined <see cref="GroundDeposit"/>. Post: result in [0, 1000], multiple of 4. Side effects: none.
        /// </remarks>
        public int GroundCoverPermille(GroundDeposit deposit)
        {
            Contract.Requires(deposit >= GroundDeposit.Water && deposit <= GroundDeposit.Ash, "undefined GroundDeposit");
            return (int)((_cover >> (8 * (int)deposit)) & 0xFFu) * 4;
        }

        /// <summary>Ground move and jump-height multiplier from deposits for the player (no immunities).</summary>
        /// <remarks>Pre: none. Post: result in [0.875, 1]. Side effects: none.</remarks>
        public float PlayerGroundMultiplier => GroundMultiplier(null);

        /// <summary>Ground move multiplier from deposits for a creature with <paramref name="mastery"/>.</summary>
        /// <remarks>
        /// REQ: docs/design/weather.md#地表累積 — Ice ignores snow, Earth ignores sand and ash, Water ignores standing water.
        /// Pre: mastery may be null (no immunities). Post: result in [0.875, 1]. Side effects: none.
        /// </remarks>
        public float CreatureGroundMultiplier(IReadOnlyList<ElementValue> mastery) => GroundMultiplier(mastery);

        private float GroundMultiplier(IReadOnlyList<ElementValue> mastery)
        {
            int cover = 0;
            for (int i = 0; i < DepositCount; i++)
            {
                var deposit = (GroundDeposit)i;
                if (mastery != null && Ignores(mastery, deposit)) continue;
                cover = System.Math.Max(cover, GroundCoverPermille(deposit));
            }

            return 1f - 0.125f * cover / 1000f;
        }

        private static bool Ignores(IReadOnlyList<ElementValue> mastery, GroundDeposit deposit)
        {
            for (int i = 0; i < mastery.Count; i++)
            {
                Element e = mastery[i].Element;
                if (deposit == GroundDeposit.Snow && e == Element.Ice) return true;
                if ((deposit == GroundDeposit.Sand || deposit == GroundDeposit.Ash) && e == Element.Earth) return true;
                if (deposit == GroundDeposit.Water && e == Element.Water) return true;
            }

            return false;
        }

        private static void Pack(int[] values, out ulong lo, out ushort hi)
        {
            lo = 0;
            hi = 0;
            if (values == null) return;
            for (int i = 0; i < WeatherProfiles.ElementCount; i++)
            {
                ulong b = (byte)(sbyte)values[i];
                if (i < 8) lo |= b << (8 * i);
                else hi |= (ushort)(b << (8 * (i - 8)));
            }
        }

        private static int Unpack(ulong lo, ushort hi, int index) =>
            index < 8 ? (sbyte)(byte)(lo >> (8 * index)) : (sbyte)(byte)(hi >> (8 * (index - 8)));

        private static void RequireElement(Element element) =>
            Contract.Requires(element >= Element.Metal && element <= Element.Shadow, "undefined Element");
    }

    /// <summary>
    /// Canonical full-strength <see cref="WeatherProfile"/> rows, one per <see cref="WeatherKind"/>.
    /// REQ: docs/design/planet-world.md#天氣影響, docs/design/weather.md#屬性 (數值暫定).
    /// </summary>
    /// <remarks>Pre: none. Post: pure and deterministic. Side effects: none.</remarks>
    public static class WeatherProfiles
    {
        /// <summary>Number of <see cref="Element"/> values the per-element tables cover.</summary>
        /// <remarks>Pre: none. Post: constant 10. Side effects: none.</remarks>
        public const int ElementCount = 10;

        /// <summary>Number of <see cref="WeatherKind"/> values.</summary>
        /// <remarks>Pre: none. Post: constant 18. Side effects: none.</remarks>
        public const int KindCount = 18;

        /// <summary>First exotic (terrain-only) <see cref="WeatherKind"/>.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const WeatherKind FirstExotic = WeatherKind.RadDust;

        /// <summary>Visibility of a clear sky in metres.</summary>
        /// <remarks>Pre: none. Post: constant &gt; 0. Side effects: none.</remarks>
        public const float ClearVisibilityM = 1000f;

        private static WeatherProfile Row(WeatherKind kind, float move, float sprint, float roll, float visibility,
            float creature, float adapted, int territory, bool stealth, float drift, params (Element e, int power, int cd)[] deltas)
        {
            var power = new int[ElementCount];
            var cd = new int[ElementCount];
            foreach ((Element e, int p, int c) in deltas)
            {
                power[(int)e] = p;
                cd[(int)e] = c;
            }

            WeatherKind exotic = kind >= FirstExotic ? kind : WeatherKind.Clear;
            return new WeatherProfile(kind, exotic, move, sprint, roll, visibility, creature, adapted, territory, stealth, drift,
                power, cd, null);
        }

        /// <summary>Clear sky baseline.</summary>
        public static readonly WeatherProfile Clear = Row(WeatherKind.Clear, 1.00f, 1.00f, 1.00f, ClearVisibilityM, 1.00f, 1.00f, 1000, false, 0f);

        /// <summary>Dense fog shrinking wild territory and enabling slow-walk concealment (Q-C37).</summary>
        public static readonly WeatherProfile Fog = Row(WeatherKind.Fog, 0.95f, 0.95f, 1.00f, 18f, 0.95f, 1.05f, 600, true, 0f,
            (Element.Shadow, 15, -10), (Element.Light, -15, 10));

        /// <summary>Rainfall.</summary>
        public static readonly WeatherProfile Rain = Row(WeatherKind.Rain, 0.96f, 0.94f, 1.00f, 45f, 0.95f, 1.08f, 900, false, 0f,
            (Element.Water, 20, -15), (Element.Wood, 10, -10), (Element.Fire, -20, 15));

        /// <summary>Thunderstorm.</summary>
        public static readonly WeatherProfile Thunderstorm = Row(WeatherKind.Thunderstorm, 0.92f, 0.90f, 1.05f, 35f, 0.90f, 1.10f, 850, false, 5f,
            (Element.Thunder, 25, -15), (Element.Water, 15, 0), (Element.Earth, -10, 0));

        /// <summary>Blizzard.</summary>
        public static readonly WeatherProfile Blizzard = Row(WeatherKind.Blizzard, 0.80f, 0.78f, 1.15f, 22f, 0.82f, 1.10f, 700, false, 8f,
            (Element.Ice, 25, -15), (Element.Fire, -20, 15), (Element.Wood, -15, 0));

        /// <summary>Sandstorm.</summary>
        public static readonly WeatherProfile Sandstorm = Row(WeatherKind.Sandstorm, 0.82f, 0.80f, 1.10f, 20f, 0.84f, 1.06f, 650, false, 8f,
            (Element.Earth, 20, -10), (Element.Metal, 15, 0), (Element.Light, -15, 0), (Element.Water, -10, 0));

        /// <summary>Heatwave.</summary>
        public static readonly WeatherProfile Heatwave = Row(WeatherKind.Heatwave, 0.92f, 0.85f, 1.15f, 60f, 0.94f, 1.08f, 1000, false, 0f,
            (Element.Fire, 25, -15), (Element.Light, 10, 0), (Element.Ice, -20, 15), (Element.Water, -15, 0));

        /// <summary>Strong wind / gale.</summary>
        public static readonly WeatherProfile Gale = Row(WeatherKind.Gale, 0.88f, 0.85f, 1.00f, 70f, 0.86f, 1.12f, 900, false, 12f,
            (Element.Wind, 25, -15));

        /// <summary>輻射塵.</summary>
        public static readonly WeatherProfile RadDust = Row(WeatherKind.RadDust, 1f, 1f, 1f, ClearVisibilityM, 1f, 1f, 1000, false, 0f,
            (Element.Metal, 20, -10), (Element.Wood, -20, 10));

        /// <summary>孢子霧.</summary>
        public static readonly WeatherProfile SporeHaze = Row(WeatherKind.SporeHaze, 1f, 1f, 1f, 30f, 1f, 1f, 1000, false, 0f,
            (Element.Wood, 20, -10), (Element.Wind, -10, 0));

        /// <summary>鹽霧.</summary>
        public static readonly WeatherProfile SaltSpray = Row(WeatherKind.SaltSpray, 1f, 1f, 1f, ClearVisibilityM, 1f, 1f, 1000, false, 0f,
            (Element.Water, 20, -10), (Element.Thunder, 10, 0), (Element.Metal, -15, 0));

        /// <summary>火山灰.</summary>
        public static readonly WeatherProfile VolcanicAsh = Row(WeatherKind.VolcanicAsh, 1f, 1f, 1f, 25f, 1f, 1f, 1000, false, 0f,
            (Element.Fire, 20, -10), (Element.Earth, 10, 0), (Element.Light, -15, 0));

        /// <summary>晶塵.</summary>
        public static readonly WeatherProfile CrystalDust = Row(WeatherKind.CrystalDust, 1f, 1f, 1f, ClearVisibilityM, 1f, 1f, 1000, false, 0f,
            (Element.Earth, 15, 0), (Element.Light, 15, -10), (Element.Shadow, -15, 0));

        /// <summary>鑽石塵.</summary>
        public static readonly WeatherProfile DiamondDust = Row(WeatherKind.DiamondDust, 1f, 0.9f, 1f, ClearVisibilityM, 1f, 1f, 1000, false, 0f,
            (Element.Ice, 20, -10), (Element.Light, 10, 0), (Element.Fire, -15, 10));

        /// <summary>龍捲風.</summary>
        public static readonly WeatherProfile Tornado = Row(WeatherKind.Tornado, 1f, 1f, 1f, ClearVisibilityM, 1f, 1f, 1000, false, 15f,
            (Element.Wind, 25, -15));

        /// <summary>離子風暴.</summary>
        public static readonly WeatherProfile IonStorm = Row(WeatherKind.IonStorm, 1f, 1f, 1f, ClearVisibilityM, 1f, 1f, 1000, false, 0f,
            (Element.Thunder, 25, -15), (Element.Metal, -10, 0));

        /// <summary>太陽閃焰.</summary>
        public static readonly WeatherProfile SolarFlare = Row(WeatherKind.SolarFlare, 1f, 1f, 1f, ClearVisibilityM, 1f, 1f, 1000, false, 0f,
            (Element.Light, 25, -15), (Element.Fire, 10, 0), (Element.Shadow, -20, 10));

        /// <summary>瘴氣.</summary>
        public static readonly WeatherProfile Miasma = Row(WeatherKind.Miasma, 1f, 1f, 1f, ClearVisibilityM, 1f, 1f, 1000, false, 0f,
            (Element.Shadow, 25, -15), (Element.Light, -15, 10));

        private static readonly WeatherProfile[] AllProfiles =
        {
            Clear, Fog, Rain, Thunderstorm, Blizzard, Sandstorm, Heatwave, Gale,
            RadDust, SporeHaze, SaltSpray, VolcanicAsh, CrystalDust, DiamondDust, Tornado, IonStorm, SolarFlare, Miasma,
        };

        private static readonly WeatherProfile[] BaseProfiles =
        {
            Clear, Fog, Rain, Thunderstorm, Blizzard, Sandstorm, Heatwave, Gale,
        };

        /// <summary>All canonical weather profiles indexed by <see cref="WeatherKind"/>.</summary>
        /// <remarks>Pre: none. Post: 18 profiles; All[i].Kind == (WeatherKind)i. Side effects: none.</remarks>
        public static IReadOnlyList<WeatherProfile> All => AllProfiles;

        /// <summary>The eight base weathers (Clear..Gale), indexed by <see cref="WeatherKind"/>.</summary>
        /// <remarks>Pre: none. Post: 8 profiles. Side effects: none.</remarks>
        public static IReadOnlyList<WeatherProfile> Base => BaseProfiles;

        /// <summary>True when <paramref name="kind"/> is a terrain-only phenomenon.</summary>
        /// <remarks>Pre: none. Post: true iff RadDust &lt;= kind &lt;= Miasma. Side effects: none.</remarks>
        public static bool IsExotic(WeatherKind kind) => kind >= FirstExotic && kind <= WeatherKind.Miasma;

        /// <summary>Returns the full-strength <see cref="WeatherProfile"/> for <paramref name="kind"/>.</summary>
        /// <remarks>
        /// Pre: <paramref name="kind"/> is a defined <see cref="WeatherKind"/>.
        /// Post: returned profile has <see cref="WeatherProfile.Kind"/> == <paramref name="kind"/>.
        /// Side effects: none.
        /// </remarks>
        public static WeatherProfile For(WeatherKind kind)
        {
            Contract.Requires(kind >= WeatherKind.Clear && kind <= WeatherKind.Miasma, "undefined WeatherKind");
            return AllProfiles[(int)kind];
        }
    }
}
