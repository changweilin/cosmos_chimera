using System;
using System.Collections.Generic;
using System.Text;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Deterministically populates outdoor battle trainers from professions (in-game, planet-era and cultural-timeline),
    /// assigning each trainer a habitat-matched duty, tactical function, two-layer wander parameters, non-overlapping
    /// duty activity zone (competitive exclusion) and settlement-excluding placement.
    /// REQ: docs/design/trainer-battles.md#職務與職能.
    /// </summary>
    /// <remarks>Pre: valid planet, plan, layout, wildPool and habitatAt. Post: deterministic RunTrainers. Side effects: allocates.</remarks>
    public static class TrainerPlanner
    {
        /// <summary>Bump whenever outputs for the same inputs change.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 1;

        private const float SettlementMarginM = 2f;
        private const float BossClearanceM = 20f;
        private const float BuildingMarginM = 1f;
        private const float TrainerSpacingM = 6f;

        private static readonly TrainerDutySpec[] Duties =
        {
            new TrainerDutySpec(
                TrainerDutyId.RoadCourier, "duty_road_courier", "古道驛傳", WanderMode.Shuttle, 22000, 22000,
                new WanderDistribution(1050, 90, 80, 750, 1400),
                new WanderDistribution(2800, 600, 500, 1000, 6000),
                new WanderDistribution(1800, 500, 400, 600, 4500),
                new[] { "forest", "river_valley", "plateau", "grassland", "desert", "canyon", "eternal_day", "tundra" },
                new[]
                {
                    new TrainerFunctionWeight(TrainerFunction.Observant, 3),
                    new TrainerFunctionWeight(TrainerFunction.Escort, 4),
                    new TrainerFunctionWeight(TrainerFunction.Sparring, 3),
                }),
            new TrainerDutySpec(
                TrainerDutyId.CircuitPatrol, "duty_circuit_patrol", "山林巡守", WanderMode.Loop, 26000, 26000,
                new WanderDistribution(950, 80, 70, 700, 1250),
                new WanderDistribution(3200, 650, 550, 1200, 6500),
                new WanderDistribution(2200, 600, 500, 800, 5000),
                new[] { "forest", "river_valley", "plateau", "archipelago", "mountain", "moraine", "granite_domes" },
                new[]
                {
                    new TrainerFunctionWeight(TrainerFunction.Escort, 3),
                    new TrainerFunctionWeight(TrainerFunction.Sparring, 3),
                    new TrainerFunctionWeight(TrainerFunction.Warden, 4),
                }),
            new TrainerDutySpec(
                TrainerDutyId.WatersideAngling, "duty_waterside_angling", "水域垂釣", WanderMode.BoundedRandom, 12000, 12000,
                new WanderDistribution(550, 60, 50, 350, 800),
                new WanderDistribution(1600, 400, 350, 600, 3500),
                new WanderDistribution(4200, 900, 800, 1500, 9000),
                new[] { "river_valley", "ice_field", "fjord", "archipelago", "swamp", "tundra", "glacier", "cirque", "moraine" },
                new[]
                {
                    new TrainerFunctionWeight(TrainerFunction.Observant, 5),
                    new TrainerFunctionWeight(TrainerFunction.Escort, 2),
                    new TrainerFunctionWeight(TrainerFunction.Sparring, 3),
                }),
            new TrainerDutySpec(
                TrainerDutyId.ProspectMining, "duty_prospect_mining", "礦脈勘探", WanderMode.Shuttle, 16000, 16000,
                new WanderDistribution(750, 70, 60, 500, 1050),
                new WanderDistribution(2200, 500, 400, 800, 4800),
                new WanderDistribution(3000, 700, 600, 1000, 7000),
                new[]
                {
                    "volcano", "rift_valley", "plateau", "canyon", "cave", "desert", "mountain",
                    "cirque", "karst", "basalt_terrace", "granite_domes", "slate_cliffs",
                },
                new[]
                {
                    new TrainerFunctionWeight(TrainerFunction.Observant, 4),
                    new TrainerFunctionWeight(TrainerFunction.Escort, 3),
                    new TrainerFunctionWeight(TrainerFunction.Warden, 3),
                }),
            new TrainerDutySpec(
                TrainerDutyId.BotanicalForaging, "duty_botanical_foraging", "植群採集", WanderMode.Loop, 18000, 18000,
                new WanderDistribution(780, 80, 70, 520, 1100),
                new WanderDistribution(2400, 500, 450, 900, 5200),
                new WanderDistribution(2600, 650, 550, 900, 6000),
                new[] { "forest", "river_valley", "plateau", "archipelago", "eternal_day", "grassland", "swamp", "karst" },
                new[]
                {
                    new TrainerFunctionWeight(TrainerFunction.Observant, 5),
                    new TrainerFunctionWeight(TrainerFunction.Sparring, 5),
                }),
            new TrainerDutySpec(
                TrainerDutyId.RuinExcavation, "duty_ruin_excavation", "遺蹟考據", WanderMode.BoundedRandom, 11000, 11000,
                new WanderDistribution(650, 60, 55, 450, 900),
                new WanderDistribution(1800, 450, 350, 700, 4000),
                new WanderDistribution(3600, 800, 700, 1200, 8000),
                new[]
                {
                    "rift_valley", "canyon", "cave", "eternal_day", "eternal_night",
                    "desert", "karst", "basalt_terrace", "slate_cliffs",
                },
                new[]
                {
                    new TrainerFunctionWeight(TrainerFunction.Observant, 5),
                    new TrainerFunctionWeight(TrainerFunction.Escort, 3),
                    new TrainerFunctionWeight(TrainerFunction.Warden, 2),
                }),
            new TrainerDutySpec(
                TrainerDutyId.MartialTraining, "duty_martial_training", "荒野苦修", WanderMode.BoundedRandom, 14000, 14000,
                new WanderDistribution(1050, 90, 80, 750, 1400),
                new WanderDistribution(2400, 550, 450, 900, 5000),
                new WanderDistribution(2000, 500, 450, 600, 4800),
                new[]
                {
                    "volcano", "rift_valley", "plateau", "canyon", "desert", "mountain",
                    "basalt_terrace", "granite_domes", "slate_cliffs",
                },
                new[]
                {
                    new TrainerFunctionWeight(TrainerFunction.Sparring, 4),
                    new TrainerFunctionWeight(TrainerFunction.Warden, 3),
                    new TrainerFunctionWeight(TrainerFunction.Pursuit, 3),
                }),
            new TrainerDutySpec(
                TrainerDutyId.ShrinePilgrimage, "duty_shrine_pilgrimage", "聖地巡禮", WanderMode.Loop, 24000, 24000,
                new WanderDistribution(800, 70, 60, 550, 1100),
                new WanderDistribution(2800, 600, 500, 1000, 6000),
                new WanderDistribution(2800, 700, 600, 1000, 6500),
                new[]
                {
                    "forest", "river_valley", "ice_field", "fjord", "plateau", "eternal_day",
                    "eternal_night", "grassland", "mountain", "cirque",
                },
                new[]
                {
                    new TrainerFunctionWeight(TrainerFunction.Observant, 4),
                    new TrainerFunctionWeight(TrainerFunction.Sparring, 3),
                    new TrainerFunctionWeight(TrainerFunction.Warden, 3),
                }),
            new TrainerDutySpec(
                TrainerDutyId.RovingMinstrel, "duty_roving_minstrel", "雲遊傳唱", WanderMode.UnboundedRandom, 0, 20000,
                new WanderDistribution(880, 80, 70, 600, 1200),
                new WanderDistribution(3000, 700, 600, 1000, 6500),
                new WanderDistribution(2400, 600, 500, 800, 5500),
                new[]
                {
                    "forest", "river_valley", "volcano", "rift_valley", "ice_field", "fjord", "plateau", "archipelago",
                    "canyon", "cave", "eternal_day", "eternal_night", "grassland", "desert", "swamp", "mountain",
                    "tundra", "glacier", "cirque", "moraine", "karst", "basalt_terrace", "granite_domes", "slate_cliffs",
                },
                new[]
                {
                    new TrainerFunctionWeight(TrainerFunction.Observant, 6),
                    new TrainerFunctionWeight(TrainerFunction.Sparring, 4),
                }),
            new TrainerDutySpec(
                TrainerDutyId.NightWatch, "duty_night_watch", "暗夜潛行", WanderMode.BoundedRandom, 15000, 15000,
                new WanderDistribution(950, 85, 75, 650, 1300),
                new WanderDistribution(2500, 550, 450, 900, 5200),
                new WanderDistribution(2200, 550, 450, 700, 5000),
                new[]
                {
                    "forest", "rift_valley", "ice_field", "canyon", "cave", "eternal_night",
                    "swamp", "tundra", "glacier", "karst", "slate_cliffs",
                },
                new[]
                {
                    new TrainerFunctionWeight(TrainerFunction.Escort, 2),
                    new TrainerFunctionWeight(TrainerFunction.Warden, 4),
                    new TrainerFunctionWeight(TrainerFunction.Pursuit, 4),
                }),
            new TrainerDutySpec(
                TrainerDutyId.BountyHunting, "duty_bounty_hunting", "懸賞追獵", WanderMode.UnboundedRandom, 0, 28000,
                new WanderDistribution(1150, 100, 90, 850, 1500),
                new WanderDistribution(3200, 700, 600, 1200, 7000),
                new WanderDistribution(1500, 400, 350, 500, 3800),
                new[]
                {
                    "forest", "river_valley", "volcano", "rift_valley", "ice_field", "fjord", "plateau", "archipelago",
                    "canyon", "cave", "eternal_day", "eternal_night", "grassland", "desert", "swamp", "mountain",
                    "tundra", "glacier", "cirque", "moraine", "karst", "basalt_terrace", "granite_domes", "slate_cliffs",
                },
                new[]
                {
                    new TrainerFunctionWeight(TrainerFunction.Warden, 3),
                    new TrainerFunctionWeight(TrainerFunction.Pursuit, 5),
                    new TrainerFunctionWeight(TrainerFunction.Champion, 2),
                }),
            new TrainerDutySpec(
                TrainerDutyId.FrontierTrailblazing, "duty_frontier_trailblazing", "荒原拓荒", WanderMode.UnboundedRandom, 0, 22000,
                new WanderDistribution(980, 90, 80, 700, 1350),
                new WanderDistribution(3000, 650, 550, 1100, 6400),
                new WanderDistribution(2000, 500, 450, 700, 4800),
                new[]
                {
                    "volcano", "rift_valley", "ice_field", "fjord", "plateau", "archipelago", "canyon", "eternal_day",
                    "eternal_night", "desert", "swamp", "mountain", "tundra", "glacier", "cirque", "moraine",
                    "basalt_terrace", "granite_domes",
                },
                new[]
                {
                    new TrainerFunctionWeight(TrainerFunction.Sparring, 4),
                    new TrainerFunctionWeight(TrainerFunction.Warden, 3),
                    new TrainerFunctionWeight(TrainerFunction.Pursuit, 3),
                }),
            new TrainerDutySpec(
                TrainerDutyId.LeylineTuning, "duty_leyline_tuning", "以太調律", WanderMode.BoundedRandom, 13000, 13000,
                new WanderDistribution(680, 65, 55, 450, 950),
                new WanderDistribution(1900, 450, 350, 700, 4200),
                new WanderDistribution(3400, 800, 700, 1100, 7500),
                new[]
                {
                    "volcano", "rift_valley", "ice_field", "plateau", "cave",
                    "eternal_day", "eternal_night", "mountain", "glacier", "basalt_terrace",
                },
                new[]
                {
                    new TrainerFunctionWeight(TrainerFunction.Observant, 4),
                    new TrainerFunctionWeight(TrainerFunction.Escort, 3),
                    new TrainerFunctionWeight(TrainerFunction.Warden, 3),
                }),
            new TrainerDutySpec(
                TrainerDutyId.TrialWardenship, "duty_trial_wardenship", "試煉鎮守", WanderMode.BoundedRandom, 10000, 10000,
                new WanderDistribution(720, 70, 60, 500, 1000),
                new WanderDistribution(2000, 450, 400, 800, 4400),
                new WanderDistribution(2800, 650, 550, 1000, 6200),
                new[]
                {
                    "forest", "river_valley", "volcano", "rift_valley", "ice_field", "fjord", "plateau", "archipelago",
                    "canyon", "cave", "eternal_day", "eternal_night", "grassland", "desert", "swamp", "mountain",
                    "tundra", "glacier", "cirque", "moraine", "karst", "basalt_terrace", "granite_domes", "slate_cliffs",
                },
                new[]
                {
                    new TrainerFunctionWeight(TrainerFunction.Warden, 6),
                    new TrainerFunctionWeight(TrainerFunction.Champion, 4),
                }),
        };

        private static readonly TrainerProfessionSpec[] Professions =
        {
            // 12 In-game professions
            new TrainerProfessionSpec("trv_rookie", "見習旅人", TrainerOriginKind.InGame, null, null,
                Array.Empty<Element>(), 200, TrainerFunction.Sparring, NpcRole.Laborer,
                new[] { D(TrainerDutyId.RoadCourier, 3), D(TrainerDutyId.RovingMinstrel, 2), D(TrainerDutyId.BotanicalForaging, 1) }, 10),
            new TrainerProfessionSpec("trv_hiker", "登山客", TrainerOriginKind.InGame, null, null,
                new[] { Element.Earth, Element.Metal }, 500, TrainerFunction.Sparring, NpcRole.Miner,
                new[] { D(TrainerDutyId.ProspectMining, 3), D(TrainerDutyId.FrontierTrailblazing, 2), D(TrainerDutyId.CircuitPatrol, 1) }, 8),
            new TrainerProfessionSpec("trv_angler", "釣客", TrainerOriginKind.InGame, null, null,
                new[] { Element.Water, Element.Ice }, 500, TrainerFunction.Observant, NpcRole.Fisher,
                new[] { D(TrainerDutyId.WatersideAngling, 4), D(TrainerDutyId.RovingMinstrel, 1), D(TrainerDutyId.RoadCourier, 1) }, 8),
            new TrainerProfessionSpec("trv_ranger", "森林巡守", TrainerOriginKind.InGame, null, null,
                new[] { Element.Wood, Element.Wind }, 500, TrainerFunction.Warden, NpcRole.Forager,
                new[] { D(TrainerDutyId.CircuitPatrol, 3), D(TrainerDutyId.BotanicalForaging, 2), D(TrainerDutyId.FrontierTrailblazing, 1) }, 8),
            new TrainerProfessionSpec("trv_brawler", "拳術家", TrainerOriginKind.InGame, null, null,
                new[] { Element.Fire, Element.Earth }, 500, TrainerFunction.Pursuit, NpcRole.Guard,
                new[] { D(TrainerDutyId.MartialTraining, 4), D(TrainerDutyId.FrontierTrailblazing, 1), D(TrainerDutyId.RoadCourier, 1) }, 8),
            new TrainerProfessionSpec("trv_scholar", "學者", TrainerOriginKind.InGame, null, null,
                new[] { Element.Thunder, Element.Metal }, 500, TrainerFunction.Observant, NpcRole.Scholar,
                new[] { D(TrainerDutyId.RuinExcavation, 3), D(TrainerDutyId.LeylineTuning, 2), D(TrainerDutyId.RovingMinstrel, 1) }, 8),
            new TrainerProfessionSpec("trv_bard", "吟遊詩人", TrainerOriginKind.InGame, null, null,
                new[] { Element.Wind, Element.Light }, 500, TrainerFunction.Observant, NpcRole.Performer,
                new[] { D(TrainerDutyId.RovingMinstrel, 3), D(TrainerDutyId.RoadCourier, 2), D(TrainerDutyId.ShrinePilgrimage, 1) }, 8),
            new TrainerProfessionSpec("trv_pilgrim", "巡禮祭司", TrainerOriginKind.InGame, null, null,
                new[] { Element.Light, Element.Shadow }, 500, TrainerFunction.Sparring, NpcRole.Cleric,
                new[] { D(TrainerDutyId.ShrinePilgrimage, 3), D(TrainerDutyId.LeylineTuning, 2), D(TrainerDutyId.RovingMinstrel, 1) }, 8),
            new TrainerProfessionSpec("trv_nightwalker", "夜行者", TrainerOriginKind.InGame, null, null,
                new[] { Element.Shadow, Element.Ice }, 500, TrainerFunction.Pursuit, NpcRole.Guard,
                new[] { D(TrainerDutyId.NightWatch, 3), D(TrainerDutyId.BountyHunting, 2), D(TrainerDutyId.RuinExcavation, 1) }, 8),
            new TrainerProfessionSpec("trv_bounty", "賞金獵人", TrainerOriginKind.InGame, null, null,
                Array.Empty<Element>(), 800, TrainerFunction.Champion, NpcRole.Guard,
                new[] { D(TrainerDutyId.BountyHunting, 4), D(TrainerDutyId.FrontierTrailblazing, 2), D(TrainerDutyId.NightWatch, 1) }, 6),
            new TrainerProfessionSpec("trv_trial_master", "地形試煉主", TrainerOriginKind.InGame, null, null,
                new[] { Element.Metal, Element.Fire, Element.Thunder }, 850, TrainerFunction.Champion, NpcRole.Leader,
                new[] { D(TrainerDutyId.TrialWardenship, 4), D(TrainerDutyId.LeylineTuning, 2) }, 4),
            new TrainerProfessionSpec("trv_artisan_mentor", "生活技能導師", TrainerOriginKind.InGame, null, null,
                new[] { Element.Wood, Element.Water, Element.Earth }, 600, TrainerFunction.Observant, NpcRole.Artisan,
                new[] { D(TrainerDutyId.BotanicalForaging, 2), D(TrainerDutyId.ProspectMining, 2), D(TrainerDutyId.WatersideAngling, 2), D(TrainerDutyId.RovingMinstrel, 1) }, 5),

            // 8 Planet-era professions
            new TrainerProfessionSpec("era_primitive_totem_hunter", "原始圖騰獵師", TrainerOriginKind.PlanetEra, null, "primitive",
                new[] { Element.Wood, Element.Earth, Element.Fire }, 500, TrainerFunction.Warden, NpcRole.Forager,
                new[] { D(TrainerDutyId.CircuitPatrol, 2), D(TrainerDutyId.FrontierTrailblazing, 2), D(TrainerDutyId.ShrinePilgrimage, 2) }, 6),
            new TrainerProfessionSpec("era_agrarian_field_warden", "農耕巡田武者", TrainerOriginKind.PlanetEra, null, "agrarian",
                new[] { Element.Wood, Element.Water, Element.Earth }, 500, TrainerFunction.Escort, NpcRole.Farmer,
                new[] { D(TrainerDutyId.BotanicalForaging, 3), D(TrainerDutyId.RoadCourier, 2), D(TrainerDutyId.MartialTraining, 1) }, 6),
            new TrainerProfessionSpec("era_mercantile_caravan_guard", "商貿護隊鏢師", TrainerOriginKind.PlanetEra, null, "mercantile",
                new[] { Element.Metal, Element.Earth, Element.Wind }, 550, TrainerFunction.Escort, NpcRole.Merchant,
                new[] { D(TrainerDutyId.RoadCourier, 3), D(TrainerDutyId.CircuitPatrol, 2), D(TrainerDutyId.BountyHunting, 1) }, 6),
            new TrainerProfessionSpec("era_industrial_rail_inspector", "工業鐵道巡檢", TrainerOriginKind.PlanetEra, null, "industrial",
                new[] { Element.Metal, Element.Fire, Element.Thunder }, 600, TrainerFunction.Warden, NpcRole.Miner,
                new[] { D(TrainerDutyId.ProspectMining, 3), D(TrainerDutyId.RoadCourier, 2), D(TrainerDutyId.FrontierTrailblazing, 1) }, 6),
            new TrainerProfessionSpec("era_steampunk_airship_navigator", "蒸汽飛艇領航", TrainerOriginKind.PlanetEra, null, "steampunk",
                new[] { Element.Wind, Element.Fire, Element.Metal }, 600, TrainerFunction.Sparring, NpcRole.Artisan,
                new[] { D(TrainerDutyId.FrontierTrailblazing, 3), D(TrainerDutyId.LeylineTuning, 2), D(TrainerDutyId.RovingMinstrel, 1) }, 6),
            new TrainerProfessionSpec("era_future_orbital_ranger", "未來星軌巡航", TrainerOriginKind.PlanetEra, null, "future",
                new[] { Element.Thunder, Element.Light, Element.Metal }, 700, TrainerFunction.Warden, NpcRole.Guard,
                new[] { D(TrainerDutyId.LeylineTuning, 3), D(TrainerDutyId.BountyHunting, 2), D(TrainerDutyId.CircuitPatrol, 1) }, 6),
            new TrainerProfessionSpec("era_wasteland_salvage_hunter", "廢土拾荒獵人", TrainerOriginKind.PlanetEra, null, "wasteland",
                new[] { Element.Shadow, Element.Fire, Element.Earth }, 650, TrainerFunction.Pursuit, NpcRole.Forager,
                new[] { D(TrainerDutyId.BountyHunting, 3), D(TrainerDutyId.RuinExcavation, 2), D(TrainerDutyId.ProspectMining, 1) }, 6),
            new TrainerProfessionSpec("era_ancient_relic_keeper", "遠古遺蹟守衛", TrainerOriginKind.PlanetEra, null, "ancient",
                new[] { Element.Earth, Element.Light, Element.Shadow }, 700, TrainerFunction.Warden, NpcRole.Cleric,
                new[] { D(TrainerDutyId.RuinExcavation, 3), D(TrainerDutyId.TrialWardenship, 2), D(TrainerDutyId.ShrinePilgrimage, 1) }, 5),

            // 12 Cultural-region x timeline professions
            new TrainerProfessionSpec("cul_east_asia_talisman_swordsman", "東亞符劍修者", TrainerOriginKind.CultureTimeline, "east_asia", null,
                new[] { Element.Metal, Element.Wind, Element.Light }, 600, TrainerFunction.Sparring, NpcRole.Scholar,
                new[] { D(TrainerDutyId.ShrinePilgrimage, 2), D(TrainerDutyId.MartialTraining, 3), D(TrainerDutyId.RovingMinstrel, 1) }, 6),
            new TrainerProfessionSpec("cul_south_asia_prana_ascetic", "南亞氣脈苦行者", TrainerOriginKind.CultureTimeline, "south_asia", null,
                new[] { Element.Fire, Element.Light, Element.Wind }, 600, TrainerFunction.Observant, NpcRole.Cleric,
                new[] { D(TrainerDutyId.ShrinePilgrimage, 2), D(TrainerDutyId.LeylineTuning, 2), D(TrainerDutyId.MartialTraining, 2) }, 5),
            new TrainerProfessionSpec("cul_southeast_asia_monsoon_wayfinder", "東南亞季風領航者", TrainerOriginKind.CultureTimeline, "southeast_asia", null,
                new[] { Element.Water, Element.Wind, Element.Wood }, 550, TrainerFunction.Sparring, NpcRole.Fisher,
                new[] { D(TrainerDutyId.WatersideAngling, 2), D(TrainerDutyId.BotanicalForaging, 2), D(TrainerDutyId.FrontierTrailblazing, 2) }, 5),
            new TrainerProfessionSpec("cul_middle_east_astrolabe_scholar", "西亞星盤學者", TrainerOriginKind.CultureTimeline, "middle_east", null,
                new[] { Element.Light, Element.Fire, Element.Metal }, 550, TrainerFunction.Observant, NpcRole.Scholar,
                new[] { D(TrainerDutyId.RuinExcavation, 2), D(TrainerDutyId.LeylineTuning, 2), D(TrainerDutyId.RoadCourier, 2) }, 5),
            new TrainerProfessionSpec("cul_africa_solarpunk_geomancer", "非洲太陽能地脈師", TrainerOriginKind.CultureTimeline, "africa", null,
                new[] { Element.Earth, Element.Wood, Element.Light }, 600, TrainerFunction.Warden, NpcRole.Healer,
                new[] { D(TrainerDutyId.LeylineTuning, 2), D(TrainerDutyId.BotanicalForaging, 2), D(TrainerDutyId.FrontierTrailblazing, 2) }, 5),
            new TrainerProfessionSpec("cul_europe_high_fantasy_mage_knight", "歐洲高奇幻魔導騎士", TrainerOriginKind.CultureTimeline, "europe", null,
                new[] { Element.Metal, Element.Fire, Element.Light }, 650, TrainerFunction.Warden, NpcRole.Guard,
                new[] { D(TrainerDutyId.CircuitPatrol, 2), D(TrainerDutyId.MartialTraining, 2), D(TrainerDutyId.BountyHunting, 2) }, 6),
            new TrainerProfessionSpec("cul_americas_canyon_sky_rider", "美洲峽谷空騎手", TrainerOriginKind.CultureTimeline, "americas", null,
                new[] { Element.Wind, Element.Earth, Element.Thunder }, 600, TrainerFunction.Sparring, NpcRole.Forager,
                new[] { D(TrainerDutyId.FrontierTrailblazing, 2), D(TrainerDutyId.ProspectMining, 2), D(TrainerDutyId.RovingMinstrel, 2) }, 5),
            new TrainerProfessionSpec("cul_oceania_star_navigator", "大洋洲星象航海師", TrainerOriginKind.CultureTimeline, "oceania", null,
                new[] { Element.Water, Element.Wind, Element.Light }, 550, TrainerFunction.Sparring, NpcRole.Fisher,
                new[] { D(TrainerDutyId.WatersideAngling, 2), D(TrainerDutyId.RovingMinstrel, 2), D(TrainerDutyId.FrontierTrailblazing, 2) }, 5),
            new TrainerProfessionSpec("cul_central_asia_steppe_falconer", "中亞草原獵鷹師", TrainerOriginKind.CultureTimeline, "central_asia", null,
                new[] { Element.Wind, Element.Earth, Element.Metal }, 600, TrainerFunction.Pursuit, NpcRole.Forager,
                new[] { D(TrainerDutyId.BountyHunting, 2), D(TrainerDutyId.CircuitPatrol, 2), D(TrainerDutyId.RoadCourier, 2) }, 5),
            new TrainerProfessionSpec("cul_polar_aurora_sled_scout", "極地極光雪橇斥候", TrainerOriginKind.CultureTimeline, "polar", null,
                new[] { Element.Ice, Element.Water, Element.Wind }, 550, TrainerFunction.Escort, NpcRole.Fisher,
                new[] { D(TrainerDutyId.FrontierTrailblazing, 2), D(TrainerDutyId.NightWatch, 2), D(TrainerDutyId.WatersideAngling, 2) }, 5),
            new TrainerProfessionSpec("cul_cosmopolis_cyber_netrunner", "世界都市賽博駭客", TrainerOriginKind.CultureTimeline, "cosmopolis", null,
                new[] { Element.Thunder, Element.Shadow, Element.Metal }, 700, TrainerFunction.Pursuit, NpcRole.Artisan,
                new[] { D(TrainerDutyId.NightWatch, 2), D(TrainerDutyId.LeylineTuning, 2), D(TrainerDutyId.BountyHunting, 2) }, 6),
            new TrainerProfessionSpec("cul_interstellar_warp_pioneer", "星際曲率開拓者", TrainerOriginKind.CultureTimeline, "interstellar", null,
                new[] { Element.Thunder, Element.Light, Element.Shadow }, 750, TrainerFunction.Champion, NpcRole.Guard,
                new[] { D(TrainerDutyId.FrontierTrailblazing, 2), D(TrainerDutyId.LeylineTuning, 2), D(TrainerDutyId.BountyHunting, 2) }, 5),
        };

        private static readonly IReadOnlyList<TrainerDutySpec> ReadOnlyDuties = Array.AsReadOnly(Duties);
        private static readonly IReadOnlyList<TrainerProfessionSpec> ReadOnlyProfessions = Array.AsReadOnly(Professions);

        /// <summary>All 14 outdoor trainer duty specifications.</summary>
        /// <remarks>Pre: none. Post: 14 entries in TrainerDutyId order. Side effects: none.</remarks>
        public static IReadOnlyList<TrainerDutySpec> AllDuties => ReadOnlyDuties;

        /// <summary>All 32 trainer profession specifications across in-game, planet-era and cultural timelines.</summary>
        /// <remarks>Pre: none. Post: 32 entries. Side effects: none.</remarks>
        public static IReadOnlyList<TrainerProfessionSpec> AllProfessions => ReadOnlyProfessions;

        /// <summary>Active challenge distance in mm for <paramref name="function"/> (1:1 with creature <see cref="Habit"/>).</summary>
        /// <remarks>Pre: function defined. Post: 0 / 4000 / 8000 / 14000 / 20000 / 28000. Side effects: none.</remarks>
        public static int FunctionRadiusMm(TrainerFunction function)
        {
            Contract.Requires(Enum.IsDefined(typeof(TrainerFunction), function), "function must be defined");
            switch (function)
            {
                case TrainerFunction.Observant: return 0;
                case TrainerFunction.Escort: return 4000;
                case TrainerFunction.Sparring: return 8000;
                case TrainerFunction.Warden: return 14000;
                case TrainerFunction.Pursuit: return 20000;
                case TrainerFunction.Champion: return 28000;
                default: return 0;
            }
        }

        /// <summary>Rule-layer duty specification for <paramref name="duty"/>.</summary>
        /// <remarks>Pre: duty defined. Post: spec with Duty == duty. Side effects: none.</remarks>
        public static TrainerDutySpec DutySpecFor(TrainerDutyId duty)
        {
            Contract.Requires(Enum.IsDefined(typeof(TrainerDutyId), duty), "duty must be defined");
            return Duties[(int)duty];
        }

        /// <summary>Looks up a profession specification by <paramref name="professionId"/>.</summary>
        /// <remarks>Pre: professionId registered. Post: matching TrainerProfessionSpec. Side effects: none.</remarks>
        public static TrainerProfessionSpec ProfessionSpecFor(string professionId)
        {
            Contract.Requires(!string.IsNullOrEmpty(professionId), "professionId must be non-empty");
            for (int i = 0; i < Professions.Length; i++)
            {
                if (string.Equals(Professions[i].Id, professionId, StringComparison.Ordinal))
                {
                    return Professions[i];
                }
            }

            throw new ContractException("unknown trainer profession: " + professionId);
        }

        /// <summary>Target trainer count for a planet of radius <paramref name="planetRadius"/>.</summary>
        /// <remarks>
        /// REQ: docs/design/trainer-battles.md#生成與遭遇 (Asteroid 3 / Moon 6 / Planet 10 / Supergiant 14).
        /// Pre: planetRadius &gt; 0. Post: 3, 6, 10 or 14. Side effects: none.
        /// </remarks>
        public static int TargetCountForRadius(float planetRadius)
        {
            Contract.Requires(math.isfinite(planetRadius) && planetRadius > 0f, "planetRadius must be > 0");
            if (planetRadius <= 480f) return 3;
            if (planetRadius <= 800f) return 6;
            if (planetRadius <= 1120f) return 10;
            return 14;
        }

        /// <summary>
        /// Generates all outdoor wandering trainers for a planet run, matching habitats to duties, duties to professions,
        /// excluding overlaps between different professions sharing a duty, and keeping all trainers outside settlements.
        /// </summary>
        /// <remarks>
        /// Pre: planet valid; plan, layout, wildPool and habitatAt non-null; wildPool non-empty; wildNormalMaxLevel &gt;= 1;
        ///      bossLevel &gt;= 1; planet.IsValid.
        /// Post: Trainers.Count == TargetCountForRadius(planet.Radius); every trainer lies outside all inhabited
        ///       settlements and buildings, allows habitatAt(HomeDirection) in its Duty, has a valid team in wildPool, and
        ///       no two trainers with different ProfessionId and the same Duty overlap activity caps.
        /// Side effects: allocates RunTrainers and its entries.
        /// </remarks>
        public static RunTrainers Generate(in PlanetConfig planet, RunPlan plan, RunLayout layout,
            IReadOnlyList<CreatureDef> wildPool, Func<float3, string> habitatAt, Func<float3, bool> isLand,
            int wildNormalMaxLevel, int bossLevel, string eraTag = null)
        {
            Contract.Requires(planet.IsValid, "planet must be valid");
            Contract.Requires(plan != null && layout != null && wildPool != null && habitatAt != null,
                "plan, layout, wildPool and habitatAt are required");
            Contract.Requires(wildPool.Count > 0, "wildPool must be non-empty");
            Contract.Requires(wildNormalMaxLevel >= 1 && bossLevel >= 1, "levels must be >= 1");

            var clearance = new LayoutClearance(planet, layout, 0f);
            float3 bossDir = FindBossDirection(plan);
            int targetCount = TargetCountForRadius(planet.Radius);
            var anchors = BuildAnchors(plan, layout, targetCount);
            var rng = new Pcg32(plan.Seed, 'V');
            var placed = new List<RunTrainer>(targetCount);

            for (int i = 0; i < targetCount; i++)
            {
                float3 anchor = anchors[i % anchors.Length];
                if (!FindClearAnchor(anchor, bossDir, planet.Radius, clearance, isLand, placed, out float3 initialDir))
                {
                    throw new ContractException(new StringBuilder("no clear trainer placement for slot ").Append(i).ToString());
                }

                string habitat = habitatAt(initialDir);
                TrainerProfessionSpec prof = DrawProfession(habitat, eraTag, ref rng);
                TrainerDutySpec dutySpec = DrawDuty(prof, habitat, ref rng);
                TrainerFunction fn = DrawFunction(in dutySpec, ref rng);

                ulong trainerSeed = WildIndividual.KeyFor(plan.Seed, 0x54520000UL + (ulong)i);
                var indRng = new Pcg32(trainerSeed, 'I');
                int speedMean = BoundedNormal.Sample(ref indRng, dutySpec.SpeedPermille.Mean,
                    dutySpec.SpeedPermille.IndividualSd, dutySpec.SpeedPermille.Min, dutySpec.SpeedPermille.Max);
                int moveMean = BoundedNormal.Sample(ref indRng, dutySpec.MoveMs.Mean,
                    dutySpec.MoveMs.IndividualSd, dutySpec.MoveMs.Min, dutySpec.MoveMs.Max);
                int pauseMean = BoundedNormal.Sample(ref indRng, dutySpec.PauseMs.Mean,
                    dutySpec.PauseMs.IndividualSd, dutySpec.PauseMs.Min, dutySpec.PauseMs.Max);

                float3 dir = initialDir;
                if (HasExclusionConflict(placed, dir, in dutySpec, prof.Id, planet.Radius))
                {
                    TrainerDutySpec dSpec = dutySpec;
                    string profId = prof.Id;
                    float radius = planet.Radius;
                    bool ClearDuty(float3 p) =>
                        (isLand == null || isLand(p)) &&
                        !clearance.IsInsideSettlement(p, SettlementMarginM) &&
                        !clearance.IsInsideBuilding(p, BuildingMarginM) &&
                        ClearOfBoss(p, bossDir, radius) &&
                        ClearOfTrainers(placed, p, radius) &&
                        dSpec.AllowsHabitat(habitatAt(p)) &&
                        !HasExclusionConflict(placed, p, in dSpec, profId, radius);

                    if (!RunPlanner.FirstInRings(dir, ClearDuty, out dir))
                    {
                        throw new ContractException(new StringBuilder("no non-overlapping trainer spot for ")
                            .Append(prof.Id).Append(" (").Append(dutySpec.Id).Append(") slot ").Append(i).ToString());
                    }
                }

                int region = RunPlanner.RegionAt(plan, dir);
                PlanetFrame frame = PlanetFrame.FromUp(dir);
                float turn = rng.NextFloat() * 2f * math.PI;
                float3 facing = math.normalize(frame.North * math.cos(turn) + frame.East * math.sin(turn));
                TrainerTeamMember[] team = BuildTeam(planet.Radius, prof, wildPool, wildNormalMaxLevel, bossLevel, ref rng);

                placed.Add(new RunTrainer(i, region, prof.Id, prof.Origin, dutySpec.Duty, fn, dutySpec.Mode,
                    dutySpec.RadiusMm, dutySpec.ExclusionRadiusMm, speedMean, moveMean, pauseMean,
                    dir, facing, trainerSeed, prof.PropRole, team));
            }

            return new RunTrainers(placed.ToArray(), planet.Radius, clearance, isLand);
        }

        private static TrainerProfessionDuty D(TrainerDutyId duty, int weight) => new TrainerProfessionDuty(duty, weight);

        private static float3 FindBossDirection(RunPlan plan)
        {
            for (int i = 0; i < plan.Spawns.Count; i++)
            {
                if (plan.Spawns[i].Role == SpawnRole.Boss)
                {
                    return plan.Spawns[i].Direction;
                }
            }

            return plan.Regions.Count > plan.BossRegion && plan.BossRegion >= 0
                ? plan.Regions[plan.BossRegion].Center
                : plan.LandingDirection;
        }

        private static float3[] BuildAnchors(RunPlan plan, RunLayout layout, int targetCount)
        {
            var list = new List<float3>(targetCount * 2);
            for (int i = 0; i < layout.Roads.Count; i++)
            {
                IReadOnlyList<float3> pts = layout.Roads[i].Points;
                if (pts.Count >= 2)
                {
                    int mid = pts.Count / 2;
                    list.Add(pts[mid]);
                }
            }

            for (int i = 0; i < plan.Regions.Count; i++)
            {
                list.Add(plan.Regions[i].Center);
                if (i + 1 < plan.Regions.Count)
                {
                    float3 mid = plan.Regions[i].Center + plan.Regions[i + 1].Center;
                    if (math.lengthsq(mid) > 1e-6f)
                    {
                        list.Add(math.normalize(mid));
                    }
                }
            }

            if (list.Count == 0)
            {
                list.Add(plan.LandingDirection);
            }

            return list.ToArray();
        }

        private static bool FindClearAnchor(float3 anchor, float3 bossDir, float radius, LayoutClearance clearance,
            Func<float3, bool> isLand, List<RunTrainer> placed, out float3 dir)
        {
            bool Clear(float3 p) =>
                (isLand == null || isLand(p)) &&
                !clearance.IsInsideSettlement(p, SettlementMarginM) &&
                !clearance.IsInsideBuilding(p, BuildingMarginM) &&
                ClearOfBoss(p, bossDir, radius) &&
                ClearOfTrainers(placed, p, radius);

            if (Clear(anchor))
            {
                dir = anchor;
                return true;
            }

            return RunPlanner.FirstInRings(anchor, Clear, out dir);
        }

        private static bool ClearOfBoss(float3 p, float3 bossDir, float radius) =>
            math.distance(p, bossDir) * radius >= BossClearanceM;

        private static bool ClearOfTrainers(List<RunTrainer> placed, float3 p, float radius)
        {
            float minChord = TrainerSpacingM / radius;
            float minSq = minChord * minChord;
            for (int i = 0; i < placed.Count; i++)
            {
                if (math.distancesq(placed[i].HomeDirection, p) < minSq)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasExclusionConflict(List<RunTrainer> placed, float3 dir, in TrainerDutySpec spec,
            string professionId, float radius)
        {
            float myRadiusM = spec.ActivityRadiusMm * 0.001f;
            for (int i = 0; i < placed.Count; i++)
            {
                RunTrainer other = placed[i];
                if (other.Duty != spec.Duty || string.Equals(other.ProfessionId, professionId, StringComparison.Ordinal))
                {
                    continue;
                }

                float sumM = myRadiusM + other.ActivityRadiusMm * 0.001f;
                if (math.distance(dir, other.HomeDirection) * radius < sumM)
                {
                    return true;
                }
            }

            return false;
        }

        private static TrainerProfessionSpec DrawProfession(string habitat, string eraTag, ref Pcg32 rng)
        {
            int total = 0;
            for (int i = 0; i < Professions.Length; i++)
            {
                if (HasMatchingDuty(in Professions[i], habitat))
                {
                    total += EffectiveProfessionWeight(in Professions[i], eraTag);
                }
            }

            if (total <= 0)
            {
                throw new ContractException("no trainer profession supports habitat: " + habitat);
            }

            int pick = (int)rng.NextUInt((uint)total);
            int acc = 0;
            for (int i = 0; i < Professions.Length; i++)
            {
                if (!HasMatchingDuty(in Professions[i], habitat))
                {
                    continue;
                }

                acc += EffectiveProfessionWeight(in Professions[i], eraTag);
                if (pick < acc)
                {
                    return Professions[i];
                }
            }

            return Professions[0];
        }

        private static int EffectiveProfessionWeight(in TrainerProfessionSpec p, string eraTag)
        {
            if (!string.IsNullOrEmpty(eraTag) &&
                (string.Equals(p.TimelineTag, eraTag, StringComparison.Ordinal) ||
                 string.Equals(p.CultureRegion, eraTag, StringComparison.Ordinal)))
            {
                return p.Weight * 3;
            }

            return p.Weight;
        }

        private static bool HasMatchingDuty(in TrainerProfessionSpec p, string habitat)
        {
            for (int i = 0; i < p.CandidateDuties.Count; i++)
            {
                if (DutySpecFor(p.CandidateDuties[i].Duty).AllowsHabitat(habitat))
                {
                    return true;
                }
            }

            return false;
        }

        private static TrainerDutySpec DrawDuty(in TrainerProfessionSpec prof, string habitat, ref Pcg32 rng)
        {
            int total = 0;
            for (int i = 0; i < prof.CandidateDuties.Count; i++)
            {
                TrainerProfessionDuty cand = prof.CandidateDuties[i];
                if (DutySpecFor(cand.Duty).AllowsHabitat(habitat))
                {
                    total += cand.Weight;
                }
            }

            int pick = (int)rng.NextUInt((uint)total);
            int acc = 0;
            for (int i = 0; i < prof.CandidateDuties.Count; i++)
            {
                TrainerProfessionDuty cand = prof.CandidateDuties[i];
                TrainerDutySpec spec = DutySpecFor(cand.Duty);
                if (!spec.AllowsHabitat(habitat))
                {
                    continue;
                }

                acc += cand.Weight;
                if (pick < acc)
                {
                    return spec;
                }
            }

            return DutySpecFor(prof.CandidateDuties[0].Duty);
        }

        private static TrainerFunction DrawFunction(in TrainerDutySpec spec, ref Pcg32 rng)
        {
            int total = 0;
            for (int i = 0; i < spec.Functions.Count; i++)
            {
                total += spec.Functions[i].Weight;
            }

            int pick = (int)rng.NextUInt((uint)total);
            int acc = 0;
            for (int i = 0; i < spec.Functions.Count; i++)
            {
                acc += spec.Functions[i].Weight;
                if (pick < acc)
                {
                    return spec.Functions[i].Function;
                }
            }

            return spec.Functions[0].Function;
        }

        private static TrainerTeamMember[] BuildTeam(float planetRadius, in TrainerProfessionSpec prof,
            IReadOnlyList<CreatureDef> wildPool, int wildNormalMaxLevel, int bossLevel, ref Pcg32 rng)
        {
            int count;
            if (planetRadius <= 480f)
            {
                count = 1 + (int)rng.NextUInt(2);
            }
            else if (planetRadius <= 800f)
            {
                count = 2;
            }
            else if (planetRadius <= 1120f)
            {
                count = 2 + (int)rng.NextUInt(2);
            }
            else
            {
                count = 3 + (int)rng.NextUInt(2);
            }

            int lo = math.max(1, math.min(wildNormalMaxLevel, bossLevel));
            int hi = math.max(lo, math.max(wildNormalMaxLevel, bossLevel));
            int level = lo + (int)math.round(prof.StrengthPermille * 0.001f * (hi - lo));
            level = math.clamp(level, lo, hi);

            int totalWeight = 0;
            var weights = new int[wildPool.Count];
            for (int i = 0; i < wildPool.Count; i++)
            {
                weights[i] = ElementMatch(wildPool[i], prof.ElementBias) ? 5 : 1;
                totalWeight += weights[i];
            }

            var team = new TrainerTeamMember[count];
            for (int m = 0; m < count; m++)
            {
                int pick = (int)rng.NextUInt((uint)totalWeight);
                int acc = 0;
                int chosen = 0;
                for (int i = 0; i < wildPool.Count; i++)
                {
                    acc += weights[i];
                    if (pick < acc)
                    {
                        chosen = i;
                        break;
                    }
                }

                team[m] = new TrainerTeamMember(wildPool[chosen].Id, level);
            }

            return team;
        }

        private static bool ElementMatch(CreatureDef def, IReadOnlyList<Element> bias)
        {
            if (bias == null || bias.Count == 0 || def.Mastery == null)
            {
                return false;
            }

            for (int b = 0; b < bias.Count; b++)
            {
                for (int m = 0; m < def.Mastery.Count; m++)
                {
                    if (def.Mastery[m].Element == bias[b])
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
