using System.Collections.Generic;
using Cosmimera.GameData;
using Cosmimera.Planet;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// Pure logic mapping planet regions, boss encounters, and terrain tags to BGM track ids.
    /// REQ: docs/roadmap/phase-7.md#P7-03, docs/design/planet-world.md.
    /// </summary>
    /// <remarks>
    /// Pure C# (no UnityEngine.Audio dependencies) to allow fast, deterministic EditMode test execution.
    /// </remarks>
    public static class BgmResolver
    {
        public const string Space = "bgm_space";
        public const string Town = "bgm_town";
        public const string Village = "bgm_village";
        public const string Forest = "bgm_forest";
        public const string Rainforest = "bgm_rainforest";
        public const string Mountain = "bgm_mountain";
        public const string Grassland = "bgm_grassland";
        public const string Desert = "bgm_desert";
        public const string Icefield = "bgm_icefield";
        public const string IceCap = "bgm_ice_cap";
        public const string Swamp = "bgm_swamp";
        public const string Beach = "bgm_beach";
        public const string Ruins = "bgm_ruins";
        public const string Canyon = "bgm_canyon";
        public const string Cave = "bgm_cave";
        public const string Labyrinth = "bgm_labyrinth";
        public const string DeepSea = "bgm_deep_sea";
        public const string Volcano = "bgm_volcano";
        public const string Tower = "bgm_tower";
        public const string Laboratory = "bgm_laboratory";
        public const string BossAsteroid = "bgm_boss_asteroid";
        public const string BossDwarf = "bgm_boss_dwarf_planet";
        public const string BossPlanet = "bgm_boss_planet";
        public const string BossSupergiant = "bgm_boss_supergiant";

        /// <summary>Resolves the boss battle BGM based on planet size.</summary>
        /// <remarks>Pre: size defined. Post: non-null track id. Side effects: none.</remarks>
        public static string ResolveBossBgm(PlanetSize size)
        {
            switch (size)
            {
                case PlanetSize.Asteroid:
                    return BossAsteroid;
                case PlanetSize.Moon:
                    return BossDwarf;
                case PlanetSize.Planet:
                    return BossPlanet;
                case PlanetSize.Supergiant:
                    return BossSupergiant;
                default:
                    return BossPlanet;
            }
        }

        /// <summary>Resolves the region exploration BGM based on region theme and optional planet terrain tags.</summary>
        /// <remarks>Pre: none. Post: non-null track id. Side effects: none.</remarks>
        public static string ResolveRegionBgm(string themeId, IReadOnlyList<string> terrainTags = null)
        {
            if (string.IsNullOrEmpty(themeId))
            {
                return ResolveTerrainTagBgm(terrainTags) ?? Grassland;
            }

            switch (themeId)
            {
                case "forest":
                    return Forest;
                case "canyon":
                    return Canyon;
                case "ruins":
                    return Ruins;
                case "coast":
                    return Beach;
                case "riverbank":
                    return Village;
                case "highland":
                    return Grassland;
                case "crater":
                    return Desert;
                case "ice_shelf":
                    return IceCap;
                default:
                    return ResolveTerrainTagBgm(terrainTags) ?? Grassland;
            }
        }

        /// <summary>Resolves fallback BGM from planet-level terrain tags.</summary>
        /// <remarks>Pre: none. Post: valid track id or null if none match. Side effects: none.</remarks>
        public static string ResolveTerrainTagBgm(IReadOnlyList<string> terrainTags)
        {
            if (terrainTags == null || terrainTags.Count == 0) return null;
            for (int i = 0; i < terrainTags.Count; i++)
            {
                string tag = terrainTags[i];
                switch (tag)
                {
                    case "volcano":
                    case "rift_valley":
                        return Volcano;
                    case "ice_field":
                    case "eternal_night":
                        return Icefield;
                    case "fjord":
                    case "archipelago":
                    case "eternal_day":
                        return Beach;
                    case "canyon":
                        return Canyon;
                    case "cave":
                        return Cave;
                    case "forest":
                        return Forest;
                    case "plateau":
                        return Mountain;
                    case "river_valley":
                        return Village;
                }
            }
            return null;
        }
    }
}
