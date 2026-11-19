using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class LandmarkPlannerTests
    {
        // Pinned discrete digest of seed 12345 on the SettlementPlannerTests fixture, isBuildable null (LandmarkPlanner v4: Q-W43 entrance landmark per planet).
        private const ulong SnapshotSeed12345V4 = 4762683139348853091UL;

        private readonly List<Object> _created = new List<Object>();
        private PlanetTypeDef _type;
        private List<CreatureDef> _pool;
        private RunPlanVocabulary _vocab;

        [SetUp]
        public void SetUp()
        {
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
            foreach (Object o in _created)
            {
                Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private (PlanetConfig planet, RunPlan plan, RunLayout layout, LandmarkLayout marks) Make(ulong seed, float radius = 640f,
            System.Func<float3, bool> isBuildable = null)
        {
            var planet = new PlanetConfig(radius, float3.zero);
            RunPlan plan = RunPlanner.Generate(planet, seed, _type, CreatureStage.Adult, _pool, _vocab, null);
            RunLayout layout = SettlementPlanner.Generate(planet, plan, null);
            return (planet, plan, layout, LandmarkPlanner.Generate(planet, plan, layout, isBuildable));
        }

        [Test]
        public void Ground_count_follows_the_planet_size()
        {
            Assert.That(LandmarkPlanner.GroundCountFor(320f), Is.EqualTo(2));
            Assert.That(LandmarkPlanner.GroundCountFor(640f), Is.EqualTo(2));
            Assert.That(LandmarkPlanner.GroundCountFor(960f), Is.EqualTo(3));
            Assert.That(LandmarkPlanner.GroundCountFor(1280f), Is.EqualTo(4));
        }

        [Test]
        public void Every_landmark_keeps_its_field_contracts()
        {
            foreach (float radius in new[] { 320f, 640f, 1280f })
            {
                for (ulong seed = 0; seed < 60; seed++)
                {
                    var (_, plan, _, marks) = Make(seed, radius);
                    Assert.That(marks.Landmarks.Count, Is.LessThanOrEqualTo(LandmarkPlanner.GroundCountFor(radius) + 2));
                    for (int i = 0; i < marks.Landmarks.Count; i++)
                    {
                        RunLandmark l = marks.Landmarks[i];
                        Assert.That(l.Index, Is.EqualTo(i));
                        Assert.That(l.Variant, Is.InRange(0, LandmarkPlanner.VariantCount(l.Kind) - 1));
                        Assert.That(l.Region, Is.InRange(0, plan.Regions.Count - 1));
                        Assert.That(math.length(l.Direction), Is.EqualTo(1f).Within(1e-4f));
                        Assert.That(math.length(l.Facing), Is.EqualTo(1f).Within(1e-4f));
                        Assert.That(math.abs(math.dot(l.Facing, l.Direction)), Is.LessThan(1e-3f));
                        Assert.That(l.HeightM, Is.GreaterThan(0f));
                        bool floats = l.Kind == LandmarkKind.SkyIsland || l.Kind == LandmarkKind.SpaceStation;
                        Assert.That(l.Solid, Is.EqualTo(!floats));
                        Assert.That(l.AltitudeM > 0f, Is.EqualTo(floats));
                        if (l.Kind == LandmarkKind.SkyIsland)
                        {
                            Assert.That(l.AltitudeM, Is.LessThanOrEqualTo(radius));
                        }
                        Assert.That(l.FootprintRadiusM > 0f, Is.EqualTo(l.Kind != LandmarkKind.SpaceStation));
                    }
                }
            }
        }

        [Test]
        public void The_station_sits_on_top_of_the_elevator_and_only_then()
        {
            foreach (float smallRadius in new[] { 320f, 640f })
            {
                for (ulong seed = 0; seed < 10; seed++)
                {
                    LandmarkLayout small = Make(seed, smallRadius).marks;
                    foreach (RunLandmark l in small.Landmarks)
                    {
                        Assert.That(l.Kind, Is.Not.EqualTo(LandmarkKind.SpaceElevator));
                        Assert.That(l.Kind, Is.Not.EqualTo(LandmarkKind.SpaceStation));
                    }
                }
            }

            int elevators = 0;
            for (ulong seed = 0; seed < 100; seed++)
            {
                LandmarkLayout marks = Make(seed, 960f).marks;
                int elevator = -1, station = -1;
                foreach (RunLandmark l in marks.Landmarks)
                {
                    if (l.Kind == LandmarkKind.SpaceElevator)
                    {
                        Assert.That(elevator, Is.EqualTo(-1), "one elevator at most");
                        elevator = l.Index;
                    }
                    else if (l.Kind == LandmarkKind.SpaceStation)
                    {
                        station = l.Index;
                    }
                }

                Assert.That(station >= 0, Is.EqualTo(elevator >= 0));
                if (elevator >= 0)
                {
                    elevators++;
                    Assert.That(station, Is.EqualTo(elevator + 1));
                    Assert.That(math.distance(marks.Landmarks[station].Direction, marks.Landmarks[elevator].Direction), Is.Zero);
                    Assert.That(marks.Landmarks[station].AltitudeM, Is.EqualTo(marks.Landmarks[elevator].HeightM));
                    Assert.That(marks.Landmarks[elevator].HeightM, Is.EqualTo(1200f));
                }
            }

            Assert.That(elevators, Is.GreaterThan(90), "an open planet nearly always has room for the elevator");
        }

        [Test]
        public void Landmarks_keep_clear_of_roads_buildings_spawns_landing_and_each_other()
        {
            for (ulong seed = 0; seed < 150; seed++)
            {
                var (planet, plan, layout, marks) = Make(seed);
                float r = planet.Radius;
                foreach (RunLandmark l in marks.Landmarks)
                {
                    if (l.Kind == LandmarkKind.SpaceStation)
                    {
                        continue;
                    }

                    if (l.Solid)
                    {
                        // 1 cm slack: the planner tested the same circle with the same clearance class.
                        var clearance = new LayoutClearance(planet, layout, l.FootprintRadiusM + LandmarkPlanner.LayoutGapM - 0.01f);
                        Assert.That(clearance.IsClear(l.Direction), "seed " + seed + " landmark " + l.Index);
                    }

                    Assert.That(math.distance(l.Direction, plan.LandingDirection) * r, Is.GreaterThanOrEqualTo(l.FootprintRadiusM + 25f));
                    foreach (RunSpawn s in plan.Spawns)
                    {
                        Assert.That(math.distance(l.Direction, s.Direction) * r,
                            Is.GreaterThanOrEqualTo(l.FootprintRadiusM + LandmarkPlanner.SpawnGapM));
                    }

                    foreach (RunLandmark o in marks.Landmarks)
                    {
                        if (o.Index != l.Index && o.Kind != LandmarkKind.SpaceStation)
                        {
                            Assert.That(math.distance(l.Direction, o.Direction) * r,
                                Is.GreaterThanOrEqualTo(l.FootprintRadiusM + o.FootprintRadiusM + LandmarkPlanner.LandmarkGapM));
                        }
                    }
                }
            }
        }

        [Test]
        public void Small_planets_show_distinct_ground_kinds()
        {
            for (ulong seed = 0; seed < 100; seed++)
            {
                var seen = new HashSet<LandmarkKind>();
                foreach (RunLandmark l in Make(seed, 320f).marks.Landmarks)
                {
                    if (l.Kind != LandmarkKind.SpaceElevator && l.Kind != LandmarkKind.SpaceStation)
                    {
                        Assert.That(seen.Add(l.Kind), "4 slots are dealt from a 6-kind deck without repeats; seed " + seed);
                    }
                }
            }
        }

        [Test]
        public void Every_kind_and_variant_appears_across_seeds()
        {
            var seen = new HashSet<int>();
            for (ulong seed = 0; seed < 300; seed++)
            {
                foreach (RunLandmark l in Make(seed, 960f).marks.Landmarks)
                {
                    seen.Add((int)l.Kind * 16 + l.Variant);
                }
            }

            foreach (LandmarkKind kind in System.Enum.GetValues(typeof(LandmarkKind)))
            {
                for (int v = 0; v < LandmarkPlanner.VariantCount(kind); v++)
                {
                    Assert.That(seen.Contains((int)kind * 16 + v), kind + " variant " + v);
                }
            }
        }

        [Test]
        public void Unbuildable_ground_is_never_used()
        {
            // Northern hemisphere only: a solid footprint must lie wholly inside it, a sky island's centre must.
            System.Func<float3, bool> north = d => d.y > 0f;
            for (ulong seed = 0; seed < 60; seed++)
            {
                var (planet, _, _, marks) = Make(seed, 640f, north);
                foreach (RunLandmark l in marks.Landmarks)
                {
                    Assert.That(l.Direction.y, Is.GreaterThan(0f));
                    if (l.Solid)
                    {
                        Assert.That(l.Direction.y * planet.Radius, Is.GreaterThan(l.FootprintRadiusM * 0.7f));
                    }
                }
            }

            Assert.That(Make(7, 640f, _ => false).marks.Landmarks.Count, Is.Zero);
        }

        [Test]
        public void Solid_landmarks_block_and_floating_ones_do_not()
        {
            var (planet, _, _, marks) = Make(12345);
            foreach (RunLandmark l in marks.Landmarks)
            {
                Assert.That(marks.IsBlocked(l.Direction, 0f), Is.EqualTo(l.Solid || StandsOnSolid(marks, l)));
                if (!l.Solid)
                {
                    continue;
                }

                PlanetFrame frame = PlanetFrame.FromUp(l.Direction);
                float3 inside = math.normalize(l.Direction + frame.East * ((l.FootprintRadiusM - 0.1f) / planet.Radius));
                float3 outside = math.normalize(l.Direction + frame.East * ((l.FootprintRadiusM + 1f) / planet.Radius));
                Assert.That(marks.IsBlocked(inside, 0f), Is.True);
                Assert.That(marks.IsBlocked(outside, 0f), Is.False);
                Assert.That(marks.IsBlocked(outside, 1.5f), Is.True, "a body's own radius widens the circle");
                Assert.That(marks.IsBlocked(-l.Direction, 0f), Is.False);
            }
        }

        private static bool StandsOnSolid(LandmarkLayout marks, RunLandmark l)
        {
            foreach (RunLandmark o in marks.Landmarks)
            {
                if (o.Solid && math.distance(o.Direction, l.Direction) == 0f)
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void Same_inputs_give_the_same_layout()
        {
            for (ulong seed = 0; seed < 40; seed++)
            {
                LandmarkLayout a = Make(seed).marks, b = Make(seed).marks;
                Assert.That(LandmarkPlanner.Hash(a), Is.EqualTo(LandmarkPlanner.Hash(b)));
                Assert.That(a.Landmarks.Count, Is.EqualTo(b.Landmarks.Count));
                for (int i = 0; i < a.Landmarks.Count; i++)
                {
                    Assert.That(math.distance(a.Landmarks[i].Direction, b.Landmarks[i].Direction), Is.Zero);
                }
            }

            Assert.That(LandmarkPlanner.Hash(Make(1).marks), Is.Not.EqualTo(LandmarkPlanner.Hash(Make(2).marks)));
        }

        [Test]
        public void Contract_violations_throw()
        {
            var (planet, plan, layout, marks) = Make(1);
            Assert.Throws<ContractException>(() => LandmarkPlanner.Generate(default, plan, layout, null));
            Assert.Throws<ContractException>(() => LandmarkPlanner.Generate(planet, null, layout, null));
            Assert.Throws<ContractException>(() => LandmarkPlanner.Generate(planet, plan, null, null));
            Assert.Throws<ContractException>(() => LandmarkPlanner.Hash(null));
            Assert.Throws<ContractException>(() => LandmarkPlanner.GroundCountFor(0f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => LandmarkPlanner.VariantCount((LandmarkKind)99));
            Assert.Throws<ContractException>(() => marks.IsBlocked(float3.zero, 0f));
            Assert.Throws<ContractException>(() => marks.IsBlocked(new float3(0f, 1f, 0f), -1f));
        }

        // REQ: planet-world.md#進階地形地城 (Q-W41, Q-W43) — one entrance per planet, on its terrain's landmark.
        [Test]
        public void Every_planet_but_an_asteroid_has_exactly_one_entrance_on_its_landmark()
        {
            foreach (float radius in new[] { 320f, 640f, 960f, 1280f })
            {
                PlanetSize size = radius < 480f ? PlanetSize.Asteroid : radius < 800f ? PlanetSize.Moon
                    : radius < 1120f ? PlanetSize.Planet : PlanetSize.Supergiant;
                for (ulong seed = 0; seed < 40; seed++)
                {
                    var (_, plan, _, marks) = Make(seed, radius);
                    if (size == PlanetSize.Asteroid)
                    {
                        Assert.That(marks.EntranceIndex, Is.EqualTo(-1));
                        Assert.That(marks.EntranceTerrainId, Is.Null);
                        continue;
                    }

                    Assert.That(marks.EntranceIndex, Is.GreaterThanOrEqualTo(0), $"no entrance R={radius} seed={seed}");
                    Assert.That(AdvancedTerrains.TryGet(marks.EntranceTerrainId, out AdvancedTerrainSpec spec), Is.True);
                    Assert.That(AdvancedTerrains.AppearsOn(spec, size), Is.True);
                    Assert.That(marks.Landmarks[marks.EntranceIndex].Kind, Is.EqualTo(DungeonEntrance.KindFor(spec.Id)));
                    bool anyShare = false;
                    foreach (AdvancedTerrainSpec t in AdvancedTerrains.All)
                    {
                        anyShare |= AdvancedTerrains.AppearsOn(t, size) && plan.Elements.PercentOf(t.Element) > 0;
                    }

                    if (anyShare)
                    {
                        Assert.That(plan.Elements.PercentOf(spec.Element), Is.GreaterThan(0), "drawn by element share");
                    }

                    int ground = 0;
                    foreach (RunLandmark l in marks.Landmarks)
                    {
                        ground += l.Kind == LandmarkKind.SpaceElevator || l.Kind == LandmarkKind.SpaceStation ? 0 : 1;
                    }

                    Assert.That(ground, Is.LessThanOrEqualTo(LandmarkPlanner.GroundCountFor(radius)));
                }
            }
        }

        [Test]
        public void Snapshot_seed_12345_is_pinned()
        {
            Assert.That(LandmarkPlanner.GeneratorVersion, Is.EqualTo(4), "bump the pin with the version");
            ulong h = LandmarkPlanner.Hash(Make(12345).marks);
            Assert.That(h, Is.EqualTo(SnapshotSeed12345V4), "actual " + h);
        }
    }
}
