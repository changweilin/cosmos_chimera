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
        public void Invalid_preconditions_throw_contract_exception()
        {
            Assert.Throws<ContractException>(() => LobbyPlanetPlanner.BuildTemplates(default));
            Assert.Throws<ContractException>(() => LobbyPlanetPlanner.BuildPlan(default, _type, _pool, _vocab, null));
            Assert.Throws<ContractException>(() => LobbyPlanetPlanner.BuildLayout(_lobbyConfig, null, null));
            Assert.Throws<ContractException>(() => LobbyPlanetPlanner.BuildLandmarks(_lobbyConfig, null, null, null));
        }
    }
}
