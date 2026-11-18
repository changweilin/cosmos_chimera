namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Weather tendency tables (μ) per planet type, terrain, season and day phase, plus opening presets.
    /// REQ: docs/design/weather.md#演化 (數值暫定).
    /// </summary>
    /// <remarks>
    /// Pre: none. Post: values are attribute units 0..100 (callers scale to tenths). Side effects: none.
    /// Base attribute order: clouds, fog, wind, rain, snow, sand, thunder, heat (WeatherAttr 0..7).
    /// </remarks>
    internal static class WeatherTables
    {
        internal const int BaseCount = 8;

        // Cached arrays: these are read on every weather evaluation, which runs on the combat tick.
        private static readonly int[] Zero = new int[BaseCount];
        private static readonly int[] Verdant = { 40, 15, 25, 30, 0, 0, 15, 10 };
        private static readonly int[] Lava = { 45, 10, 30, 5, 0, 25, 30, 60 };
        private static readonly int[] IceCrystal = { 60, 25, 40, 0, 55, 0, 0, 0 };
        private static readonly int[] Storm = { 70, 15, 55, 45, 0, 0, 50, 10 };
        private static readonly int[] Mineral = { 30, 15, 35, 5, 0, 40, 10, 25 };
        private static readonly int[] Twilight = { 50, 45, 20, 20, 15, 0, 25, 0 };
        private static readonly int[] OtherPlanet = { 35, 15, 25, 25, 0, 0, 10, 10 };
        private static readonly int[] Wet = { 10, 25, 0, 20, 0, 0, 0, 0 };
        private static readonly int[] Dry = { -15, 0, 10, -20, 0, 40, 0, 25 };
        private static readonly int[] Cold = { 10, 0, 10, -20, 35, 0, 0, -30 };
        private static readonly int[] Volcanic = { 10, 0, 0, 0, -20, 0, 10, 30 };
        private static readonly int[] High = { 10, 0, 25, 0, 0, 0, 15, 0 };
        private static readonly int[] Coast = { 0, 15, 10, 20, 0, 0, 0, 0 };
        private static readonly int[] Murky = { 0, 25, 0, 0, 0, 0, 0, 0 };
        private static readonly int[] Gorge = { 0, 0, 15, 0, 0, 10, 0, 0 };
        private static readonly int[] SpringBias = { 0, 10, 0, 10, 0, 0, 0, 0 };
        private static readonly int[] SummerBias = { 0, 0, 0, 5, 0, 0, 15, 15 };
        private static readonly int[] AutumnBias = { 0, 5, 15, 0, 0, 5, 0, 0 };
        private static readonly int[] WinterBias = { 10, 0, 0, -10, 30, 0, 0, -25 };
        // Dawn radiation fog, daytime convection, dusk wind, night cooling (shape follows the reference's season×phase table).
        private static readonly int[] DawnBias = { 0, 25, -10, 0, 0, 0, 0, -10 };
        private static readonly int[] DayBias = { 5, -15, 0, 0, 0, 0, 10, 15 };
        private static readonly int[] DuskBias = { 0, 5, 10, 0, 0, 0, 0, 0 };
        private static readonly int[] NightBias = { 0, 10, -5, 0, 0, 0, 0, -20 };

        internal static int[] PlanetBase(string planetTypeId) => planetTypeId switch
        {
            "verdant_planet" => Verdant,
            "lava_planet" => Lava,
            "ice_crystal_planet" => IceCrystal,
            "storm_planet" => Storm,
            "mineral_planet" => Mineral,
            "twilight_planet" => Twilight,
            _ => OtherPlanet,
        };

        internal static int[] TerrainBias(string terrainId) => terrainId switch
        {
            "swamp" or "rainforest" => Wet,
            "desert" => Dry,
            "ice_field" or "ice_cap" or "tundra" or "glacier" or "cirque" or "moraine" => Cold,
            "volcano_foothills" or "volcano" => Volcanic,
            "plateau" or "mountain" => High,
            "archipelago" or "fjord" or "deep_sea" => Coast,
            "eternal_night" or "cave" or "karst" or "labyrinth" or "cavern_outer" => Murky,
            "canyon" => Gorge,
            _ => Zero,
        };

        internal static int[] SeasonBias(Season? season) => season switch
        {
            Season.Spring => SpringBias,
            Season.Summer => SummerBias,
            Season.Autumn => AutumnBias,
            Season.Winter => WinterBias,
            _ => Zero,
        };

        internal static int[] PhaseBias(DayPhase phase) => phase switch
        {
            DayPhase.Dawn => DawnBias,
            DayPhase.Day => DayBias,
            DayPhase.Dusk => DuskBias,
            _ => NightBias,
        };

        /// <summary>Exotic tendency (0..100) of <paramref name="kind"/> on a terrain; 0 = never appears there.</summary>
        internal static int ExoticBias(WeatherKind kind, string planetTypeId, string terrainId)
        {
            bool verdant = planetTypeId == "verdant_planet";
            bool mineral = planetTypeId == "mineral_planet";
            bool storm = planetTypeId == "storm_planet";
            switch (kind)
            {
                case WeatherKind.RadDust:
                    if (terrainId == "laboratory") return 80;
                    return mineral && terrainId is "cave" or "cavern_outer" ? 70 : 0;
                case WeatherKind.SporeHaze:
                    if (!verdant) return 0;
                    return terrainId switch { "rainforest" => 75, "forest" => 65, "swamp" => 65, _ => 0 };
                case WeatherKind.SaltSpray:
                    return terrainId switch { "deep_sea" => 75, "archipelago" => 65, "fjord" => 65, _ => 0 };
                case WeatherKind.VolcanicAsh:
                    int ash = terrainId switch
                    {
                        "volcano" => 80, "volcano_foothills" => 70, "rift_valley" => 60, "basalt_terrace" => 60, _ => 0,
                    };
                    return ash == 0 && planetTypeId == "lava_planet" ? 45 : ash;
                case WeatherKind.CrystalDust:
                    return mineral && terrainId is "canyon" or "karst" or "granite_domes" or "slate_cliffs" or "cave" ? 70 : 0;
                case WeatherKind.DiamondDust:
                    return terrainId switch
                    {
                        "ice_cap" => 75, "glacier" => 70, "ice_field" => 70, "cirque" => 65, "tundra" => 65, _ => 0,
                    };
                case WeatherKind.Tornado:
                    if (storm && terrainId == "mountain") return 65;
                    return terrainId switch { "space" => 70, "plateau" => 65, "desert" => 65, _ => 0 };
                case WeatherKind.IonStorm:
                    if (terrainId == "tower") return 75;
                    if (terrainId == "space") return 70;
                    if (!storm) return 0;
                    return terrainId switch { "plateau" => 70, "mountain" => 70, "archipelago" => 65, _ => 0 };
                case WeatherKind.SolarFlare:
                    return terrainId switch { "eternal_day" => 75, "temple" => 70, "space" => 65, _ => 0 };
                case WeatherKind.Miasma:
                    return terrainId switch { "eternal_night" => 75, "labyrinth" => 75, "swamp" => 65, _ => 0 };
                default:
                    return 0;
            }
        }

        /// <summary>Permille of an exotic's tendency kept in <paramref name="phase"/> (preferred 1000, other 600, forbidden 0).</summary>
        internal static int ExoticPhasePermille(WeatherKind kind, DayPhase phase)
        {
            const int Preferred = 1000, Other = 600, Forbidden = 0;
            switch (kind)
            {
                case WeatherKind.SporeHaze:
                    return phase is DayPhase.Dawn or DayPhase.Day ? Preferred : Other;
                case WeatherKind.CrystalDust:
                    return phase == DayPhase.Day ? Preferred : Other;
                case WeatherKind.DiamondDust:
                    return phase is DayPhase.Dawn or DayPhase.Night ? Preferred : Other;
                case WeatherKind.Tornado:
                    return phase is DayPhase.Day or DayPhase.Dusk ? Preferred : Other;
                case WeatherKind.IonStorm:
                    return phase is DayPhase.Dusk or DayPhase.Night ? Preferred : Other;
                case WeatherKind.SolarFlare:
                    return phase == DayPhase.Day ? Preferred : Forbidden;
                case WeatherKind.Miasma:
                    return phase is DayPhase.Dusk or DayPhase.Night ? Preferred : Forbidden;
                default:
                    return Preferred;
            }
        }

        /// <summary>Opening presets (base attributes) and the attribute whose tendency weights each draw (-1 = flat).</summary>
        internal static readonly (int[] values, int weightAttr)[] Presets =
        {
            (new[] { 10, 0, 12, 0, 0, 0, 0, 0 }, -1),    // clear
            (new[] { 68, 12, 22, 0, 0, 0, 0, 0 }, 0),    // cloudy
            (new[] { 88, 32, 58, 88, 0, 0, 0, 0 }, 3),   // heavy rain
            (new[] { 94, 38, 78, 0, 0, 0, 88, 0 }, 6),   // thunderstorm
            (new[] { 42, 90, 8, 0, 0, 0, 0, 0 }, 1),     // fog
            (new[] { 32, 0, 88, 0, 0, 0, 0, 0 }, 2),     // gale
            (new[] { 84, 28, 62, 0, 92, 0, 0, 0 }, 4),   // blizzard
            (new[] { 28, 18, 84, 0, 0, 88, 0, 0 }, 5),   // sandstorm
            (new[] { 5, 0, 10, 0, 0, 0, 0, 88 }, 7),     // heatwave
        };

        /// <summary>Preset index for each base <see cref="WeatherKind"/> a weather-control request may summon.</summary>
        internal static int PresetFor(WeatherKind kind) => kind switch
        {
            WeatherKind.Rain => 2,
            WeatherKind.Thunderstorm => 3,
            WeatherKind.Fog => 4,
            WeatherKind.Gale => 5,
            WeatherKind.Blizzard => 6,
            WeatherKind.Sandstorm => 7,
            WeatherKind.Heatwave => 8,
            _ => 0,
        };
    }
}
