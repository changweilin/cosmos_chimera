using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class LobbyPlanetPlannerTests
    {
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        private PlanetConfig _lobbyConfig;
        private PlanetTypeDef _type;
        private List<CreatureDef> _pool;
        private RunPlanVocabulary _vocab;

        [SetUp]
        public void SetUp()
        {
            _lobbyConfig = new PlanetConfig(LobbyPlanet.RadiusM, float3.zero);
            _type = ScriptableObject.CreateInstance<PlanetTypeDef>();
            _created.Add(_type);
            _type.SetId(LobbyPlanet.PlanetTypeId);
            _type.Set(new List<string> { "forest", "river_valley" },
                new List<Element> { Element.Wood, Element.Water, Element.Wind });

            _pool = new List<CreatureDef>();
            string[] lines = { "wood", "fire", "water" };
            foreach (string line in lines)
            {
                for (int stage = 0; stage < 3; stage++)
                {
                    var c = ScriptableObject.CreateInstance<CreatureDef>();
                    _created.Add(c);
                    c.SetId(line + "_" + stage);
                    Element e = line == "wood" ? Element.Wood : line == "fire" ? Element.Fire : Element.Water;
                    c.Set(line, (CreatureStage)stage, CreatureSizeClass.M, AiStyle.Charge, null, null, 0,
                        new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(e, 15) },
                        new CreatureStats(78, 25, 25, 25, 25, 25), null, null, "{}", false);
                    _pool.Add(c);
                }
            }

            _vocab = new RunPlanVocabulary(
                new[] { "ruins", "forest", "riverbank", "highland", "coast", "crater", "ice_shelf", "canyon" },
                new[] { "affix_frenzy", "affix_pack", "affix_fog", "affix_bounty" },
                new[] { "chain_a", "chain_b", "chain_c" });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object o in _created)
            {
                UnityEngine.Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        [Test]
        public void BuildTemplates_includes_all_24_landforms_with_polar_kinds_at_poles_and_is_deterministic()
        {
            using PlanetTemplates a = LobbyPlanetPlanner.BuildTemplates(_lobbyConfig);
            using PlanetTemplates b = LobbyPlanetPlanner.BuildTemplates(_lobbyConfig);

            Assert.That(a.StampIds.Count, Is.EqualTo(LandformSynth.Kinds.Count));
            Assert.That(b.StampIds.Count, Is.EqualTo(a.StampIds.Count));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            float minPolarAbsY = float.PositiveInfinity;
            float maxNonPolarAbsY = 0f;
            var polarSet = new HashSet<string>(StringComparer.Ordinal)
            {
                "tundra", "glacier", "cirque", "moraine", "ice_field",
            };

            for (int i = 0; i < a.StampIds.Count; i++)
            {
                Assert.That(a.StampIds[i], Is.EqualTo(b.StampIds[i]));
                string kind = LandformSynth.KindOf(a.StampIds[i]);
                Assert.That(seen.Add(kind), Is.True, "duplicate landform kind " + kind);

                float absY = math.abs(a.View.Stamp(i).Center.y);
                if (polarSet.Contains(kind))
                {
                    minPolarAbsY = math.min(minPolarAbsY, absY);
                }
                else
                {
                    maxNonPolarAbsY = math.max(maxNonPolarAbsY, absY);
                }
            }

            foreach (string expected in LandformSynth.Kinds)
            {
                Assert.That(seen.Contains(expected), Is.True, "missing landform kind " + expected);
            }

            Assert.That(minPolarAbsY, Is.GreaterThanOrEqualTo(maxNonPolarAbsY),
                "all 5 polar landforms must sit at higher |y| than non-polar landforms");
        }

        [Test]
        public void BuildLayout_places_exact_8_settlement_roster_and_connects_all_towns_by_main_roads()
        {
            using PlanetTemplates templates = LobbyPlanetPlanner.BuildTemplates(_lobbyConfig);
            TemplateView view = templates.View;
            Func<float3, bool> isLand = d => !view.IsSea(d);

            RunPlan plan = LobbyPlanetPlanner.BuildPlan(_lobbyConfig, _type, _pool, _vocab, isLand);
            RunLayout layout = LobbyPlanetPlanner.BuildLayout(_lobbyConfig, plan, isLand);
            RunLayout layout2 = LobbyPlanetPlanner.BuildLayout(_lobbyConfig, plan, isLand);

            Assert.That(SettlementPlanner.Hash(layout), Is.EqualTo(SettlementPlanner.Hash(layout2)));
            Assert.That(layout.Settlements.Count, Is.EqualTo(8));

            int tribes = 0, towns = 0, cities = 0, cityStates = 0;
            var townRegions = new HashSet<int>();
            foreach (RunSettlement s in layout.Settlements)
            {
                Assert.That(s.Domain, Is.EqualTo(SettlementDomain.Land));
                Assert.That(townRegions.Add(s.Region), Is.True, "each settlement owns a distinct region");
                switch (s.Kind)
                {
                    case SettlementKind.Tribe: tribes++; break;
                    case SettlementKind.Town: towns++; break;
                    case SettlementKind.City: cities++; break;
                    case SettlementKind.CityState: cityStates++; break;
                    default: Assert.Fail("unexpected lobby settlement kind: " + s.Kind); break;
                }
            }

            Assert.That(tribes, Is.EqualTo(2));
            Assert.That(towns, Is.EqualTo(3));
            Assert.That(cityStates, Is.EqualTo(1));
            Assert.That(cities, Is.EqualTo(2));

            // Verify exact #1..#8 progression roster order (Town, Tribe, City, Town, CityState, Town, City, Tribe)
            // and latitude placement (#8 Tribe at polar frontier, #7 City second highest, #3 City & #5 CityState non-polar).
            SettlementKind[] expectedRoster =
            {
                SettlementKind.Town, SettlementKind.Tribe, SettlementKind.City, SettlementKind.Town,
                SettlementKind.CityState, SettlementKind.Town, SettlementKind.City, SettlementKind.Tribe,
            };
            for (int i = 0; i < expectedRoster.Length; i++)
            {
                Assert.That(layout.Settlements[i].Kind, Is.EqualTo(expectedRoster[i]), $"settlement #{i + 1}");
            }

            float tribe8AbsY = math.abs(layout.Settlements[7].Center.y);
            float city7AbsY = math.abs(layout.Settlements[6].Center.y);
            float city3AbsY = math.abs(layout.Settlements[2].Center.y);
            float cityState5AbsY = math.abs(layout.Settlements[4].Center.y);
            Assert.That(tribe8AbsY, Is.GreaterThanOrEqualTo(city7AbsY), "#8 Tribe must sit at highest polar latitude");
            Assert.That(city7AbsY, Is.GreaterThanOrEqualTo(city3AbsY), "#7 City must sit at higher latitude than #3 City");
            Assert.That(city7AbsY, Is.GreaterThanOrEqualTo(cityState5AbsY), "#7 City must sit at higher latitude than #5 CityState");

            // Verify all 8 settlements are mutually reachable along Main roads alone.
            var mainAdj = new Dictionary<int, List<int>>();
            foreach (RunRoad r in layout.Roads)
            {
                if (r.Class != RoadClass.Main)
                {
                    continue;
                }

                if (!mainAdj.TryGetValue(r.FromRegion, out List<int> fromList))
                {
                    fromList = new List<int>();
                    mainAdj[r.FromRegion] = fromList;
                }

                if (!mainAdj.TryGetValue(r.ToRegion, out List<int> toList))
                {
                    toList = new List<int>();
                    mainAdj[r.ToRegion] = toList;
                }

                fromList.Add(r.ToRegion);
                toList.Add(r.FromRegion);
            }

            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            int start = layout.Settlements[0].Region;
            visited.Add(start);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                if (!mainAdj.TryGetValue(cur, out List<int> neighbors))
                {
                    continue;
                }

                foreach (int next in neighbors)
                {
                    if (visited.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            foreach (int townRegion in townRegions)
            {
                Assert.That(visited.Contains(townRegion), Is.True,
                    $"town in region {townRegion} is not connected to the main road network");
            }
        }

        [Test]
        public void BuildLandmarks_places_all_10_advanced_terrain_dungeon_entrances_and_space_station()
        {
            using PlanetTemplates templates = LobbyPlanetPlanner.BuildTemplates(_lobbyConfig);
            TemplateView view = templates.View;
            Func<float3, bool> isLand = d => !view.IsSea(d);

            RunPlan plan = LobbyPlanetPlanner.BuildPlan(_lobbyConfig, _type, _pool, _vocab, isLand);
            RunLayout layout = LobbyPlanetPlanner.BuildLayout(_lobbyConfig, plan, isLand);
            LandmarkLayout landmarks = LobbyPlanetPlanner.BuildLandmarks(_lobbyConfig, plan, layout, isLand);
            LandmarkLayout landmarks2 = LobbyPlanetPlanner.BuildLandmarks(_lobbyConfig, plan, layout, isLand);

            Assert.That(LandmarkPlanner.Hash(landmarks), Is.EqualTo(LandmarkPlanner.Hash(landmarks2)));
            Assert.That(landmarks.Entrances.Count, Is.EqualTo(AdvancedTerrains.All.Count));
            Assert.That(landmarks.EntranceIndex, Is.EqualTo(landmarks.Entrances[0].LandmarkIndex));
            Assert.That(landmarks.EntranceTerrainId, Is.EqualTo(landmarks.Entrances[0].TerrainId));

            var terrains = new HashSet<string>(StringComparer.Ordinal);
            foreach (DungeonEntranceSite entry in landmarks.Entrances)
            {
                Assert.That(terrains.Add(entry.TerrainId), Is.True, "duplicate dungeon entrance " + entry.TerrainId);
                RunLandmark lm = landmarks.Landmarks[entry.LandmarkIndex];
                Assert.That(lm.Kind, Is.EqualTo(DungeonEntrance.KindFor(entry.TerrainId)));
            }

            foreach (AdvancedTerrainSpec spec in AdvancedTerrains.All)
            {
                Assert.That(terrains.Contains(spec.Id), Is.True, "missing fixed dungeon entrance for " + spec.Id);
            }

            // Verify each wonder is placed closer to its designated hub settlement (#3 City, #7 City, or #8 Tribe)
            // than to the other two hubs.
            float3 hub3 = layout.Settlements[2].Center;
            float3 hub7 = layout.Settlements[6].Center;
            float3 hub8 = layout.Settlements[7].Center;
            foreach (DungeonEntranceSite entry in landmarks.Entrances)
            {
                int expectedHub = LobbyPlanet.HubSettlementIndexForTerrain(entry.TerrainId);
                float3 lmDir = landmarks.Landmarks[entry.LandmarkIndex].Direction;
                float dot3 = math.dot(lmDir, hub3);
                float dot7 = math.dot(lmDir, hub7);
                float dot8 = math.dot(lmDir, hub8);
                int nearestHub = dot3 >= dot7 && dot3 >= dot8 ? 2 : (dot7 >= dot8 ? 6 : 7);
                Assert.That(nearestHub, Is.EqualTo(expectedHub),
                    $"wonder {entry.TerrainId} should cluster around hub settlement index {expectedHub}");
            }

            bool hasStation = false;
            foreach (RunLandmark lm in landmarks.Landmarks)
            {
                if (lm.Kind == LandmarkKind.SpaceStation)
                {
                    hasStation = true;
                    break;
                }
            }

            Assert.That(hasStation, Is.True, "SpaceStation must accompany the SpaceElevator wonder");
        }

        [Test]
        public void Gatekeeper_and_terrain_hub_rules_match_main_story_schedule()
        {
            Assert.That(LobbyPlanet.GatekeeperForSettlement(0), Is.EqualTo(LobbyGatekeeper.None));
            Assert.That(LobbyPlanet.GatekeeperForSettlement(1), Is.EqualTo(LobbyGatekeeper.Friend));
            Assert.That(LobbyPlanet.GatekeeperForSettlement(2), Is.EqualTo(LobbyGatekeeper.Rival));
            Assert.That(LobbyPlanet.GatekeeperForSettlement(3), Is.EqualTo(LobbyGatekeeper.Friend));
            Assert.That(LobbyPlanet.GatekeeperForSettlement(4), Is.EqualTo(LobbyGatekeeper.Rival));
            Assert.That(LobbyPlanet.GatekeeperForSettlement(5), Is.EqualTo(LobbyGatekeeper.Friend));
            Assert.That(LobbyPlanet.GatekeeperForSettlement(6), Is.EqualTo(LobbyGatekeeper.Rival));
            Assert.That(LobbyPlanet.GatekeeperForSettlement(7), Is.EqualTo(LobbyGatekeeper.Friend));

            foreach (string id in new[] { "laboratory", "rainforest", "deep_sea", "volcano", "cave" })
            {
                Assert.That(LobbyPlanet.HubSettlementIndexForTerrain(id), Is.EqualTo(2));
            }

            foreach (string id in new[] { "ice_cap", "tower", "space" })
            {
                Assert.That(LobbyPlanet.HubSettlementIndexForTerrain(id), Is.EqualTo(6));
            }

            foreach (string id in new[] { "labyrinth", "temple" })
            {
                Assert.That(LobbyPlanet.HubSettlementIndexForTerrain(id), Is.EqualTo(7));
            }

            Assert.Throws<ContractException>(() => LobbyPlanet.GatekeeperForSettlement(-1));
            Assert.Throws<ContractException>(() => LobbyPlanet.GatekeeperForSettlement(8));
            Assert.Throws<ContractException>(() => LobbyPlanet.HubSettlementIndexForTerrain(""));
            Assert.Throws<ContractException>(() => LobbyPlanet.HubSettlementIndexForTerrain("unknown"));
        }

        [Test]
        public void MotherPlanet_and_CradleMoon_have_no_submerged_settlements_or_buildings()
        {
            // 1. Mother Planet (R = 2560 m)
            PlanetHeight lobbyHeight = PlanetHeight.ForPlanet(_lobbyConfig, LobbyPlanet.Seed);
            using PlanetTemplates lobbyTemplates = LobbyPlanetPlanner.BuildTemplates(_lobbyConfig);
            TemplateView lobbyView = lobbyTemplates.View;
            Func<float3, float> lobbyGround = d => lobbyTemplates.Sample(lobbyHeight, d);
            Func<float3, bool> lobbyDry = d => !lobbyView.IsSea(d) && lobbyView.River(d) <= 0.05f &&
                                               lobbyGround(d) >= LobbyPlanetPlanner.MinDryAltitudeM;

            RunPlan lobbyPlan = LobbyPlanetPlanner.BuildPlan(_lobbyConfig, _type, _pool, _vocab, lobbyDry);
            RunLayout lobbyLayout = LobbyPlanetPlanner.BuildLayout(_lobbyConfig, lobbyPlan, lobbyDry, lobbyGround);

            Assert.That(lobbyLayout.Settlements.Count, Is.EqualTo(LobbyPlanet.SettlementRoster.Count));
            foreach (RunSettlement s in lobbyLayout.Settlements)
            {
                Assert.That(lobbyDry(s.Center), Is.True, $"Mother settlement #{s.Index + 1} center in water");
                Assert.That(s.BuildingCount, Is.GreaterThan(0), $"Mother settlement #{s.Index + 1} lost all buildings");
            }

            foreach (RunBuilding b in lobbyLayout.Buildings)
            {
                Assert.That(LobbyPlanetPlanner.IsDryBuildingFootprint(b.Direction, b.Facing, b.WidthM, b.DepthM,
                    _lobbyConfig.Radius, lobbyDry, lobbyGround), Is.True,
                    $"Mother building {b.Kind} in settlement {b.Settlement} has submerged corner");
            }

            // 2. Cradle Moon (R = 320 m, cradle_planet)
            var cradleConfig = new PlanetConfig(320f, float3.zero);
            var cradleType = ScriptableObject.CreateInstance<PlanetTypeDef>();
            _created.Add(cradleType);
            cradleType.SetId("cradle_planet");
            cradleType.Set(new List<string> { "forest", "river_valley", "plateau" },
                new List<Element> { Element.Wood, Element.Water, Element.Wind });
            const ulong cradleSeed = 20261117UL;
            PlanetHeight cradleHeight = PlanetHeight.ForPlanet(cradleConfig, cradleSeed);
            using PlanetTemplates cradleTemplates = TemplatePlanner.Build(cradleConfig, cradleSeed,
                cradleType.TerrainTags, PlanetSize.Asteroid);
            TemplateView cradleView = cradleTemplates.View;
            Func<float3, float> cradleGround = d => cradleTemplates.Sample(cradleHeight, d);
            Func<float3, bool> cradleDry = d => !cradleView.IsSea(d) && cradleView.River(d) <= 0.05f &&
                                                cradleGround(d) >= LobbyPlanetPlanner.MinDryAltitudeM;

            RunPlan cradlePlan = RunPlanner.Generate(cradleConfig, cradleSeed, cradleType, CreatureStage.Juvenile,
                _pool, _vocab, cradleDry);
            RunLayout cradleLayout = SettlementPlanner.Generate(cradleConfig, cradlePlan, cradleDry, cradleGround);

            Assert.That(cradleDry(cradlePlan.LandingDirection), Is.True, "Cradle Moon landing spot in water");
            Assert.That(cradleLayout.Settlements.Count, Is.GreaterThan(0));
            Assert.That(cradleLayout.Settlements[0].BuildingCount, Is.GreaterThan(0),
                "Cradle Moon starting town must keep dry buildings");
            foreach (RunBuilding b in cradleLayout.Buildings)
            {
                Assert.That(LobbyPlanetPlanner.IsDryBuildingFootprint(b.Direction, b.Facing, b.WidthM, b.DepthM,
                    cradleConfig.Radius, cradleDry, cradleGround), Is.True,
                    $"Cradle building {b.Kind} in settlement {b.Settlement} has submerged corner");
            }
        }

        [Test]
        public void Invalid_preconditions_throw_contract_exception()
        {
            Assert.Throws<ContractException>(() => LobbyPlanetPlanner.BuildTemplates(default));
            Assert.Throws<ContractException>(() => LobbyPlanetPlanner.BuildPlan(default, _type, _pool, _vocab, null));
            Assert.Throws<ContractException>(() => LobbyPlanetPlanner.BuildLayout(_lobbyConfig, null, null));
            Assert.Throws<ContractException>(() => LobbyPlanetPlanner.BuildLandmarks(_lobbyConfig, null, null, null));
        }
    }
}
