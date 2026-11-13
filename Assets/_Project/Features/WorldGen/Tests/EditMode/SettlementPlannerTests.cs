using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class SettlementPlannerTests
    {
        // Pinned discrete digest of seed 12345 on the RunPlannerTests fixture, isLand null (SettlementPlanner v2).
        private const ulong SnapshotSeed12345V2 = 12544250319596664072UL;

        private const float Radius = 640f;
        private readonly List<Object> _created = new List<Object>();
        private PlanetTypeDef _type;
        private List<CreatureDef> _pool;
        private RunPlanVocabulary _vocab;
        private PlanetConfig _planet;

        [SetUp]
        public void SetUp()
        {
            _planet = new PlanetConfig(Radius, float3.zero);
            _type = ScriptableObject.CreateInstance<PlanetTypeDef>();
            _created.Add(_type);
            _type.SetId("verdant_planet");
            _type.Set(new List<string> { "forest", "river_valley" }, new List<Element> { Element.Wood, Element.Water });
            _pool = new List<CreatureDef>();
            foreach (string line in new[] { "wood", "fire", "water" })
            {
                for (int stage = 0; stage < 3; stage++)
                {
                    var c = ScriptableObject.CreateInstance<CreatureDef>();
                    _created.Add(c);
                    c.SetId(line + "_" + stage);
                    Element e = line == "wood" ? Element.Wood : line == "fire" ? Element.Fire : Element.Water;
                    c.Set(line, (CreatureStage)stage, CreatureSizeClass.M, AiStyle.Charge, Temperament.Normal, null, 0,
                        new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(e, 15) },
                        new CreatureStats(25, 25, 25, 25), null, null, "{}", false);
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
            foreach (Object o in _created)
            {
                Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private RunPlan Plan(ulong seed, System.Func<float3, bool> isLand = null) =>
            RunPlanner.Generate(_planet, seed, _type, CreatureStage.Adult, _pool, _vocab, isLand);

        private static long EdgeKey(int a, int b) => ((long)math.min(a, b) << 32) | (uint)math.max(a, b);

        [Test]
        public void Roads_form_a_spanning_tree_with_main_roads_on_the_route()
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                RunPlan plan = Plan(seed);
                RunLayout layout = SettlementPlanner.Generate(_planet, plan, null);
                Assert.That(layout.Roads.Count, Is.EqualTo(plan.Regions.Count - 1), $"seed {seed}");
                var parent = new int[plan.Regions.Count];
                for (int i = 0; i < parent.Length; i++)
                {
                    parent[i] = i;
                }

                int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
                int main = 0;
                foreach (RunRoad road in layout.Roads)
                {
                    CollectionAssert.Contains(plan.Regions[road.FromRegion].Neighbors, road.ToRegion, $"seed {seed}");
                    int ra = Find(road.FromRegion), rb = Find(road.ToRegion);
                    Assert.That(ra, Is.Not.EqualTo(rb), $"seed {seed}: road closes a cycle");
                    parent[ra] = rb;
                    if (road.Class == RoadClass.Main)
                    {
                        Assert.That(plan.MainRoute[main], Is.EqualTo(road.FromRegion), $"seed {seed}");
                        Assert.That(plan.MainRoute[main + 1], Is.EqualTo(road.ToRegion), $"seed {seed}");
                        Assert.That(road.WidthM, Is.EqualTo(SettlementPlanner.MainWidthM));
                        main++;
                    }
                    else
                    {
                        CollectionAssert.DoesNotContain(plan.MainRoute, road.ToRegion, $"seed {seed}");
                        Assert.That(plan.Regions[road.FromRegion].Hops, Is.EqualTo(plan.Regions[road.ToRegion].Hops - 1));
                        Assert.That(road.WidthM, Is.EqualTo(SettlementPlanner.TrailWidthM));
                    }
                }

                Assert.That(main, Is.EqualTo(plan.MainRoute.Count - 1), $"seed {seed}");
            }
        }

        [Test]
        public void Road_polylines_are_unit_dense_and_end_on_the_sites()
        {
            RunPlan plan = Plan(7);
            RunLayout layout = SettlementPlanner.Generate(_planet, plan, null);
            foreach (RunRoad road in layout.Roads)
            {
                Assert.That(road.Points.Count, Is.GreaterThanOrEqualTo(3));
                Assert.That(road.Bridge.Count, Is.EqualTo(road.Points.Count));
                Assert.That(road.Points[0], Is.EqualTo(plan.Regions[road.FromRegion].Center));
                Assert.That(road.Points[road.Points.Count - 1], Is.EqualTo(plan.Regions[road.ToRegion].Center));
                for (int i = 0; i < road.Points.Count; i++)
                {
                    Assert.That(math.length(road.Points[i]), Is.EqualTo(1f).Within(1e-4f));
                    Assert.That(road.Bridge[i], Is.False, "no land predicate → no bridges");
                    if (i > 0)
                    {
                        Assert.That(math.distance(road.Points[i - 1], road.Points[i]) * Radius, Is.LessThanOrEqualTo(8f));
                    }
                }
            }
        }

        [Test]
        public void Settlements_cover_landing_route_and_boss()
        {
            SettlementKind[] allowed = SettlementPlanner.AllowedTownKinds(PlanetSize.Moon);
            for (ulong seed = 0; seed < 100; seed++)
            {
                RunPlan plan = Plan(seed);
                RunLayout layout = SettlementPlanner.Generate(_planet, plan, null);
                CollectionAssert.Contains(allowed, layout.Settlements[0].Kind, $"seed {seed}");
                Assert.That(layout.Settlements[0].Region, Is.EqualTo(plan.LandingRegion));
                Assert.That(math.distance(layout.Settlements[0].Center, plan.LandingDirection), Is.LessThan(1e-5f));
                int villages = 0, lairs = 0;
                for (int i = 0; i < layout.Settlements.Count; i++)
                {
                    RunSettlement s = layout.Settlements[i];
                    Assert.That(s.Index, Is.EqualTo(i));
                    if (s.Kind == SettlementKind.Village)
                    {
                        Assert.That(s.Region, Is.EqualTo(plan.MainRoute[1 + villages]), $"seed {seed}");
                        Assert.That(s.BuildingCount, Is.InRange(3, 6));
                        villages++;
                    }
                    else if (s.Kind == SettlementKind.Lair)
                    {
                        Assert.That(s.Region, Is.EqualTo(plan.BossRegion));
                        Assert.That(s.BuildingCount, Is.EqualTo(6));
                        lairs++;
                    }
                    else if (s.Kind == SettlementKind.Ruins)
                    {
                        CollectionAssert.DoesNotContain(plan.MainRoute, s.Region);
                    }
                }

                Assert.That(villages, Is.EqualTo(plan.MainRoute.Count - 2), $"seed {seed}");
                Assert.That(lairs, Is.EqualTo(1), $"seed {seed}");
            }
        }

        [Test]
        public void Buildings_face_along_the_surface_and_do_not_overlap_within_a_settlement()
        {
            for (ulong seed = 0; seed < 50; seed++)
            {
                RunLayout layout = SettlementPlanner.Generate(_planet, Plan(seed), null);
                foreach (RunSettlement s in layout.Settlements)
                {
                    for (int i = s.FirstBuilding; i < s.FirstBuilding + s.BuildingCount; i++)
                    {
                        RunBuilding a = layout.Buildings[i];
                        Assert.That(a.Settlement, Is.EqualTo(s.Index));
                        Assert.That(math.length(a.Direction), Is.EqualTo(1f).Within(1e-4f));
                        Assert.That(math.length(a.Facing), Is.EqualTo(1f).Within(1e-4f));
                        Assert.That(math.abs(math.dot(a.Facing, a.Direction)), Is.LessThan(1e-3f));
                        Assert.That(a.Storeys, Is.GreaterThanOrEqualTo(1));
                        for (int j = s.FirstBuilding; j < i; j++)
                        {
                            RunBuilding b = layout.Buildings[j];
                            // Footprints are boxes; their inscribed circles must not intersect (a conservative check).
                            float gap = math.distance(a.Direction, b.Direction) * Radius;
                            Assert.That(gap, Is.GreaterThan(0.5f * (math.min(a.WidthM, a.DepthM) + math.min(b.WidthM, b.DepthM))),
                                $"seed {seed} settlement {s.Index}");
                        }
                    }
                }
            }
        }

        [Test]
        public void Land_predicate_drops_sea_lots_and_flags_bridges_without_shifting_the_rest()
        {
            // Sea = one hemisphere minus a polar land cap, so RunPlanner always finds land.
            System.Func<float3, bool> isLand = d => d.x > 0f || d.y > 0.9f;
            RunPlan plan = Plan(11, isLand);
            RunLayout dry = SettlementPlanner.Generate(_planet, plan, null);
            RunLayout wet = SettlementPlanner.Generate(_planet, plan, isLand);
            Assert.That(wet.Roads.Count, Is.EqualTo(dry.Roads.Count));
            Assert.That(wet.Settlements.Count, Is.EqualTo(dry.Settlements.Count), "the stream does not depend on land");
            int bridges = 0;
            for (int r = 0; r < wet.Roads.Count; r++)
            {
                CollectionAssert.AreEqual(dry.Roads[r].Points, wet.Roads[r].Points);
                for (int i = 0; i < wet.Roads[r].Points.Count; i++)
                {
                    Assert.That(wet.Roads[r].Bridge[i], Is.EqualTo(!isLand(wet.Roads[r].Points[i])));
                    bridges += wet.Roads[r].Bridge[i] ? 1 : 0;
                }
            }

            foreach (RunBuilding b in wet.Buildings)
            {
                Assert.That(isLand(b.Direction), Is.True);
            }

            Assert.That(wet.Buildings.Count, Is.LessThanOrEqualTo(dry.Buildings.Count));
            Assert.That(bridges, Is.GreaterThan(0), "half the planet is sea, some road must cross it");
        }

        [Test]
        public void Same_inputs_give_the_same_layout()
        {
            RunPlan plan = Plan(42);
            Assert.That(SettlementPlanner.Hash(SettlementPlanner.Generate(_planet, plan, null)),
                Is.EqualTo(SettlementPlanner.Hash(SettlementPlanner.Generate(_planet, plan, null))));
            Assert.That(SettlementPlanner.Hash(SettlementPlanner.Generate(_planet, plan, null)),
                Is.Not.EqualTo(SettlementPlanner.Hash(SettlementPlanner.Generate(_planet, Plan(43), null))));
        }

        [TestCase(PlanetSize.Asteroid, 320f, new[] { SettlementKind.Tribe })]
        [TestCase(PlanetSize.Moon, 640f, new[] { SettlementKind.Tribe, SettlementKind.Town })]
        [TestCase(PlanetSize.Planet, 960f, new[] { SettlementKind.Tribe, SettlementKind.Town, SettlementKind.City })]
        [TestCase(PlanetSize.Supergiant, 1280f, new[] { SettlementKind.Tribe, SettlementKind.Town, SettlementKind.City, SettlementKind.CityState })]
        public void Starting_town_kind_matches_planet_size(PlanetSize size, float radius, SettlementKind[] expected)
        {
            CollectionAssert.AreEqual(expected, SettlementPlanner.AllowedTownKinds(size));
            var planet = new PlanetConfig(radius, float3.zero);
            var seen = new HashSet<SettlementKind>();
            for (ulong seed = 0; seed < 60; seed++)
            {
                RunPlan plan = RunPlanner.Generate(planet, seed, _type, CreatureStage.Adult, _pool, _vocab, null);
                RunLayout layout = SettlementPlanner.Generate(planet, plan, null);
                RunSettlement start = layout.Settlements[0];
                Assert.That(start.Region, Is.EqualTo(plan.LandingRegion));
                Assert.That(math.distance(start.Center, plan.LandingDirection), Is.LessThan(1e-5f));
                CollectionAssert.Contains(expected, start.Kind, $"size {size} seed {seed}");
                seen.Add(start.Kind);
            }

            Assert.That(seen.Count, Is.EqualTo(expected.Length), $"size {size} should produce all allowed town kinds");
        }

        [Test]
        public void Contract_violations_throw()
        {
            RunPlan plan = Plan(1);
            Assert.Throws<ContractException>(() => SettlementPlanner.Generate(default, plan, null));
            Assert.Throws<ContractException>(() => SettlementPlanner.Generate(_planet, null, null));
            Assert.Throws<ContractException>(() => SettlementPlanner.Hash(null));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => SettlementPlanner.AllowedTownKinds((PlanetSize)99));
        }

        [Test]
        public void Snapshot_seed_12345_is_pinned()
        {
            Assert.That(SettlementPlanner.GeneratorVersion, Is.EqualTo(2), "bump the pin with the version");
            ulong h = SettlementPlanner.Hash(SettlementPlanner.Generate(_planet, Plan(12345), null));
            Assert.That(h, Is.EqualTo(SnapshotSeed12345V2), "actual " + h);
        }
    }
}
