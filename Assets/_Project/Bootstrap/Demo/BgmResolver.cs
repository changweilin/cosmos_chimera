using System.Collections.Generic;
using Cosmimera.GameData;
using Cosmimera.Planet;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Defines unlock conditions and elemental move requirements for advanced terrains.</summary>
    /// <remarks>Pre: immutable data. Post: provides query methods for terrain gating. Side effects: none.</remarks>
    public readonly struct AdvancedTerrainRule
    {
        public string TerrainId { get; }
        public Element RequiredElement { get; }
        public PlanetSize MinPlanetTier { get; }
        public string BgmTrackId { get; }

        public AdvancedTerrainRule(string terrainId, Element requiredElement, PlanetSize minPlanetTier, string bgmTrackId)
        {
            TerrainId = terrainId;
            RequiredElement = requiredElement;
            MinPlanetTier = minPlanetTier;
            BgmTrackId = bgmTrackId;
        }
    }

    /// <summary>
    /// Pure logic mapping planet regions, boss encounters, and terrain tags to BGM track ids,
    /// as well as handling advanced terrain gating requirements.
    /// REQ: docs/roadmap/phase-7.md#P7-03, docs/design/planet-world.md.
    /// </summary>
    /// <remarks>
    /// Pure C# (no UnityEngine.Audio dependencies) to allow fast, deterministic EditMode test execution.
    /// </remarks>
    public static class BgmResolver
    {
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
        public const string Space = "bgm_space";
        public const string Temple = "bgm_temple";
        public const string BossAsteroid = "bgm_boss_asteroid";
        public const string BossDwarf = "bgm_boss_dwarf_planet";
        public const string BossPlanet = "bgm_boss_planet";
        public const string BossSupergiant = "bgm_boss_supergiant";

        public const string Lobby = Town;

        private static readonly Dictionary<string, AdvancedTerrainRule> AdvancedTerrains =
            new Dictionary<string, AdvancedTerrainRule>
            {
                // Dwarf Planet (Adolescent / Moon, R=640)
                { "laboratory", new AdvancedTerrainRule("laboratory", Element.Metal, PlanetSize.Moon, Laboratory) },
                { "factory", new AdvancedTerrainRule("laboratory", Element.Metal, PlanetSize.Moon, Laboratory) },
                { "server_room", new AdvancedTerrainRule("laboratory", Element.Metal, PlanetSize.Moon, Laboratory) },
                { "rainforest", new AdvancedTerrainRule("rainforest", Element.Wood, PlanetSize.Moon, Rainforest) },
                { "jungle", new AdvancedTerrainRule("rainforest", Element.Wood, PlanetSize.Moon, Rainforest) },
                { "deep_sea", new AdvancedTerrainRule("deep_sea", Element.Water, PlanetSize.Moon, DeepSea) },
                { "trench", new AdvancedTerrainRule("deep_sea", Element.Water, PlanetSize.Moon, DeepSea) },
                { "volcano", new AdvancedTerrainRule("volcano", Element.Fire, PlanetSize.Moon, Volcano) },
                { "caldera", new AdvancedTerrainRule("volcano", Element.Fire, PlanetSize.Moon, Volcano) },
                { "cave", new AdvancedTerrainRule("cave", Element.Earth, PlanetSize.Moon, Cave) },
                { "mine", new AdvancedTerrainRule("cave", Element.Earth, PlanetSize.Moon, Cave) },
                { "underground", new AdvancedTerrainRule("cave", Element.Earth, PlanetSize.Moon, Cave) },

                // Planet (Adult / Planet, R=960)
                { "ice_cap", new AdvancedTerrainRule("ice_cap", Element.Ice, PlanetSize.Planet, IceCap) },
                { "permafrost", new AdvancedTerrainRule("ice_cap", Element.Ice, PlanetSize.Planet, IceCap) },
                { "tower", new AdvancedTerrainRule("tower", Element.Thunder, PlanetSize.Planet, Tower) },
                { "spire", new AdvancedTerrainRule("tower", Element.Thunder, PlanetSize.Planet, Tower) },
                { "skyscraper", new AdvancedTerrainRule("tower", Element.Thunder, PlanetSize.Planet, Tower) },
                { "space", new AdvancedTerrainRule("space", Element.Wind, PlanetSize.Planet, Space) },
                { "floating_island", new AdvancedTerrainRule("space", Element.Wind, PlanetSize.Planet, Space) },
                { "space_facility", new AdvancedTerrainRule("space", Element.Wind, PlanetSize.Planet, Space) },

                // Supergiant (Overload / Supergiant, R=1280)
                { "labyrinth", new AdvancedTerrainRule("labyrinth", Element.Shadow, PlanetSize.Supergiant, Labyrinth) },
                { "maze", new AdvancedTerrainRule("labyrinth", Element.Shadow, PlanetSize.Supergiant, Labyrinth) },
                { "temple", new AdvancedTerrainRule("temple", Element.Light, PlanetSize.Supergiant, Temple) },
                { "shrine", new AdvancedTerrainRule("temple", Element.Light, PlanetSize.Supergiant, Temple) },
            };

        /// <summary>Queries the advanced terrain rule for <paramref name="terrainOrTheme"/> if one exists.</summary>
        /// <remarks>Pre: none. Post: true iff terrain is an advanced terrain. Side effects: none.</remarks>
        public static bool TryGetAdvancedTerrainRule(string terrainOrTheme, out AdvancedTerrainRule rule)
        {
            if (string.IsNullOrEmpty(terrainOrTheme))
            {
                rule = default;
                return false;
            }
            return AdvancedTerrains.TryGetValue(terrainOrTheme, out rule);
        }

        /// <summary>Checks whether a player party can enter the given terrain based on required elemental moves.</summary>
        /// <remarks>Pre: none. Post: true if open or if party carries a creature with the required move element. Side effects: none.</remarks>
        public static bool CanPartyEnterAdvancedTerrain(string terrainOrTheme, IReadOnlyList<CreatureDef> party, out Element requiredElement)
        {
            if (!TryGetAdvancedTerrainRule(terrainOrTheme, out AdvancedTerrainRule rule))
            {
                requiredElement = default;
                return true;
            }

            requiredElement = rule.RequiredElement;
            if (party == null || party.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < party.Count; i++)
            {
                CreatureDef c = party[i];
                if (c == null) continue;
                if (c.SignatureMove != null && c.SignatureMove.Element == rule.RequiredElement)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Checks whether the advanced terrain can appear on a planet of size <paramref name="planetSize"/>.</summary>
        /// <remarks>Pre: none. Post: true iff planet tier is at or above required minimum tier. Side effects: none.</remarks>
        public static bool IsTerrainUnlockedOnPlanet(string terrainOrTheme, PlanetSize planetSize)
        {
            if (!TryGetAdvancedTerrainRule(terrainOrTheme, out AdvancedTerrainRule rule))
            {
                return true;
            }

            return planetSize >= rule.MinPlanetTier;
        }

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
                case "rainforest":
                case "jungle":
                    return Rainforest;
                case "canyon":
                    return Canyon;
                case "ruins":
                    return Ruins;
                case "coast":
                case "beach":
                    return Beach;
                case "riverbank":
                    return Village;
                case "highland":
                case "grassland":
                    return Grassland;
                case "mountain":
                case "deep_mountain":
                case "plateau":
                    return Mountain;
                case "crater":
                case "desert":
                    return Desert;
                case "ice_shelf":
                case "ice_cap":
                case "permafrost":
                    return IceCap;
                case "ice_field":
                    return Icefield;
                case "swamp":
                case "lake":
                case "wetland":
                    return Swamp;
                case "cave":
                case "mine":
                case "underground":
                    return Cave;
                case "tower":
                case "spire":
                case "skyscraper":
                    return Tower;
                case "laboratory":
                case "server_room":
                case "facility":
                case "factory":
                    return Laboratory;
                case "space":
                case "floating_island":
                case "space_facility":
                    return Space;
                case "labyrinth":
                case "maze":
                    return Labyrinth;
                case "temple":
                case "shrine":
                    return Temple;
                case "deep_sea":
                case "trench":
                    return DeepSea;
                case "volcano":
                case "caldera":
                    return Volcano;
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
                    case "caldera":
                        return Volcano;
                    case "ice_field":
                    case "eternal_night":
                        return Icefield;
                    case "ice_cap":
                        return IceCap;
                    case "fjord":
                    case "archipelago":
                    case "eternal_day":
                    case "beach":
                        return Beach;
                    case "canyon":
                        return Canyon;
                    case "cave":
                    case "mine":
                    case "underground":
                        return Cave;
                    case "forest":
                        return Forest;
                    case "rainforest":
                    case "jungle":
                        return Rainforest;
                    case "plateau":
                    case "mountain":
                    case "deep_mountain":
                        return Mountain;
                    case "swamp":
                    case "lake":
                    case "wetland":
                        return Swamp;
                    case "river_valley":
                        return Village;
                    case "laboratory":
                    case "factory":
                    case "server_room":
                        return Laboratory;
                    case "tower":
                    case "skyscraper":
                        return Tower;
                    case "space":
                    case "floating_island":
                        return Space;
                    case "labyrinth":
                        return Labyrinth;
                    case "temple":
                        return Temple;
                    case "deep_sea":
                        return DeepSea;
                }
            }
            return null;
        }
    }
}
