using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class PopulacePlannerTests
    {
        // Pinned discrete digest of seed 12345 on the SettlementPlannerTests fixture, neutral temper, isStandable null
        // (PopulacePlanner v1).
        private const ulong SnapshotSeed12345V1 = 4311297554248266902UL;

        private static readonly string[] PlanetTypes =
        {
            "verdant_planet", "lava_planet", "ice_crystal_planet", "storm_planet", "mineral_planet", "twilight_planet",
        };

        private readonly List<Object> _created = new List<Object>();
        private List<CreatureDef> _pool;
        private RunPlanVocabulary _vocab;

        [SetUp]
        public void SetUp()
        {
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

        private (PlanetConfig planet, RunPlan plan, RunLayout layout, RunPopulace people) Make(ulong seed, float radius = 640f,
            string planetType = "verdant_planet", PopulaceTemper temper = default, System.Func<float3, bool> isStandable = null)
        {
            var type = ScriptableObject.CreateInstance<PlanetTypeDef>();
            _created.Add(type);
            type.SetId(planetType);
            type.Set(new List<string> { "forest", "river_valley" }, new List<Element> { Element.Wood, Element.Water });
            var planet = new PlanetConfig(radius, float3.zero);
            RunPlan plan = RunPlanner.Generate(planet, seed, type, CreatureStage.Adult, _pool, _vocab, null);
            RunLayout layout = SettlementPlanner.Generate(planet, plan, null);
            return (planet, plan, layout, PopulacePlanner.Generate(planet, plan, layout, temper, isStandable));
        }

        [Test]
        public void Every_inhabited_settlement_gets_a_profile_and_ruins_and_lairs_get_none()
        {
            for (ulong seed = 0; seed < 20; seed++)
            {
                var (_, _, layout, people) = Make(seed);
                var expected = new List<int>();
                foreach (RunSettlement s in layout.Settlements)
                {
                    if (s.Kind != SettlementKind.Ruins && s.Kind != SettlementKind.Lair)
                    {
                        expected.Add(s.Index);
                    }
                }

                Assert.That(people.Settlements.Count, Is.EqualTo(expected.Count));
                int next = 0;
                for (int i = 0; i < expected.Count; i++)
                {
                    SettlementPopulace p = people.Settlements[i];
                    Assert.That(p.Settlement, Is.EqualTo(expected[i]));
                    Assert.That(p.SecondaryRace, Is.Not.EqualTo(p.PrimaryRace));
                    Assert.That(p.SecondarySharePermille, Is.InRange(0, 480));
                    Assert.That(p.MinoritySharePermille, Is.InRange(0, 250));
                    Assert.That(p.MeanAgePermille, Is.InRange(200, 650));
                    Assert.That(p.MeanHealthPermille, Is.InRange(300, 950));
                    Assert.That(p.FirstResident, Is.EqualTo(next));
                    Assert.That(p.ResidentCount, Is.InRange(0, PopulacePlanner.MaxPerSettlement));
                    next += p.ResidentCount;
                }

                Assert.That(next, Is.EqualTo(people.Residents.Count));
                Assert.That(people.SecondaryRace, Is.Not.EqualTo(people.PrimaryRace));
            }
        }

        [Test]
        public void Every_resident_keeps_its_field_contracts()
        {
            int total = 0;
            for (ulong seed = 0; seed < 20; seed++)
            {
                var (_, _, layout, people) = Make(seed);
                foreach (SettlementPopulace p in people.Settlements)
                {
                    for (int i = p.FirstResident; i < p.FirstResident + p.ResidentCount; i++)
                    {
                        RunResident r = people.Residents[i];
                        total++;
                        Assert.That(r.Settlement, Is.EqualTo(p.Settlement));
                        Assert.That(layout.Buildings[r.Building].Settlement, Is.EqualTo(p.Settlement));
                        Assert.That(System.Enum.IsDefined(typeof(NpcRace), r.Race) && (int)r.Race < PopulacePlanner.RaceCount);
                        Assert.That(System.Enum.IsDefined(typeof(NpcRole), r.Role) && (int)r.Role < PopulacePlanner.RoleCount);
                        Assert.That(r.AgePermille, Is.InRange(40, 980));
                        Assert.That(r.LifeStage, Is.EqualTo(PopulacePlanner.StageFor(r.AgePermille)));
                        Assert.That(r.AgeYears, Is.EqualTo(math.max(1, r.AgePermille * PopulacePlanner.LifespanYears(r.Race) / 1000)));
                        Assert.That(r.HealthPermille, Is.InRange(50, 1000));
                        Assert.That(r.StatureZMilli, Is.InRange(-3464, 3464));
                        Assert.That(r.BulkZMilli, Is.InRange(-3464, 3464));
                        Assert.That(r.StyleZMilli, Is.InRange(-3464, 3464));
                        Assert.That(math.length(r.Direction), Is.EqualTo(1f).Within(1e-4f));
                        Assert.That(math.length(r.Facing), Is.EqualTo(1f).Within(1e-4f));
                        Assert.That(math.abs(math.dot(r.Facing, r.Direction)), Is.LessThan(1e-3f));
                        if (r.LifeStage == NpcLifeStage.Child)
                        {
                            Assert.That(r.Role, Is.EqualTo(NpcRole.None));
                        }
                    }
                }
            }

            Assert.That(total, Is.GreaterThan(200), "the fixture must actually produce people");
        }

        [Test]
        public void Residents_stand_outside_buildings_apart_from_each_other_and_block_bodies()
        {
            for (ulong seed = 0; seed < 12; seed++)
            {
                var (planet, _, layout, people) = Make(seed);
                var clearance = new LayoutClearance(planet, layout, 0f);
                for (int i = 0; i < people.Residents.Count; i++)
                {
                    RunResident r = people.Residents[i];
                    Assert.That(clearance.IsInsideBuilding(r.Direction, PopulacePlanner.BodyRadiusM), Is.False);
                    Assert.That(people.IsBlocked(r.Direction, 0f), Is.True);
                    for (int k = i + 1; k < people.Residents.Count; k++)
                    {
                        if (people.Residents[k].Settlement == r.Settlement)
                        {
                            Assert.That(math.distance(people.Residents[k].Direction, r.Direction) * planet.Radius,
                                Is.GreaterThanOrEqualTo(0.99f));
                        }
                    }
                }

                if (people.Residents.Count > 0)
                {
                    Assert.That(people.IsBlocked(-people.Residents[0].Direction, 5f), Is.False);
                }
            }
        }

        [Test]
        public void Unstandable_ground_is_never_used()
        {
            var (_, _, _, people) = Make(3, isStandable: d => d.y > 0f);
            foreach (RunResident r in people.Residents)
            {
                Assert.That(r.Direction.y, Is.GreaterThan(0f));
            }

            Assert.That(Make(3, isStandable: _ => false).people.Residents.Count, Is.Zero);
        }

        [Test]
        public void Where_people_can_stand_never_changes_who_they_are()
        {
            RunPopulace all = Make(5).people, half = Make(5, isStandable: d => d.y > 0f).people;
            var bySeed = new Dictionary<ulong, RunResident>();
            foreach (RunResident r in all.Residents)
            {
                bySeed[r.Seed] = r;
            }

            Assert.That(half.Residents.Count, Is.LessThanOrEqualTo(all.Residents.Count));
            foreach (RunResident r in half.Residents)
            {
                Assert.That(bySeed.TryGetValue(r.Seed, out RunResident same), Is.True);
                Assert.That((same.Race, same.Role, same.Sex, same.AgePermille, same.HealthPermille, same.Building),
                    Is.EqualTo((r.Race, r.Role, r.Sex, r.AgePermille, r.HealthPermille, r.Building)));
            }
        }

        [Test]
        public void A_settlement_is_mostly_its_primary_race_with_a_visible_minority()
        {
            int primary = 0, secondary = 0, total = 0;
            for (ulong seed = 0; seed < 60; seed++)
            {
                RunPopulace people = Make(seed, planetType: PlanetTypes[seed % 6]).people;
                foreach (SettlementPopulace p in people.Settlements)
                {
                    for (int i = p.FirstResident; i < p.FirstResident + p.ResidentCount; i++)
                    {
                        NpcRace race = people.Residents[i].Race;
                        total++;
                        primary += race == p.PrimaryRace ? 1 : 0;
                        secondary += race == p.SecondaryRace ? 1 : 0;
                    }
                }
            }

            Assert.That(primary, Is.GreaterThan(total / 2));
            Assert.That(secondary, Is.GreaterThan(total / 10));
            Assert.That(total - primary - secondary, Is.GreaterThan(0), "strangers of a third race turn up");
        }

        [Test]
        public void Age_and_health_cluster_around_the_settlement_mean_with_rare_outliers()
        {
            int near = 0, far = 0, total = 0;
            long healthGap = 0;
            for (ulong seed = 0; seed < 60; seed++)
            {
                RunPopulace people = Make(seed).people;
                foreach (SettlementPopulace p in people.Settlements)
                {
                    for (int i = p.FirstResident; i < p.FirstResident + p.ResidentCount; i++)
                    {
                        RunResident r = people.Residents[i];
                        int off = math.abs(r.AgePermille - p.MeanAgePermille);
                        total++;
                        near += off <= 170 ? 1 : 0;
                        far += off > 340 ? 1 : 0;
                        healthGap += r.HealthPermille - p.MeanHealthPermille;
                    }
                }
            }

            // A normal keeps ≈ 68 % within one σ and ≈ 5 % beyond two; a uniform over the same range would not.
            Assert.That(near, Is.InRange(total * 58 / 100, total * 78 / 100));
            Assert.That(far, Is.InRange(1, total * 10 / 100));
            Assert.That(healthGap / total, Is.InRange(-80, 20), "age wears health down a little, never up");
        }

        [Test]
        public void Every_race_and_role_and_life_stage_appears_across_seeds()
        {
            var races = new HashSet<NpcRace>();
            var roles = new HashSet<NpcRole>();
            var stages = new HashSet<NpcLifeStage>();
            var planetPrimaries = new HashSet<NpcRace>();
            for (ulong seed = 0; seed < 240; seed++)
            {
                RunPopulace people = Make(seed, planetType: PlanetTypes[seed % 6]).people;
                planetPrimaries.Add(people.PrimaryRace);
                foreach (RunResident r in people.Residents)
                {
                    races.Add(r.Race);
                    roles.Add(r.Role);
                    stages.Add(r.LifeStage);
                }
            }

            Assert.That(races.Count, Is.EqualTo(PopulacePlanner.RaceCount));
            Assert.That(roles.Count, Is.EqualTo(PopulacePlanner.RoleCount));
            Assert.That(stages.Count, Is.EqualTo(5));
            Assert.That(planetPrimaries.Count, Is.GreaterThanOrEqualTo(10), "planets differ in who built them");
        }

        [Test]
        public void Planet_type_favours_its_own_peoples()
        {
            int[] lava = PopulacePlanner.RaceWeights("lava_planet"), plain = PopulacePlanner.RaceWeights(null);
            Assert.That(lava.Length, Is.EqualTo(PopulacePlanner.RaceCount));
            Assert.That(lava[(int)NpcRace.Dragonkin], Is.GreaterThan(plain[(int)NpcRace.Dragonkin]));
            Assert.That(lava[(int)NpcRace.Human], Is.EqualTo(plain[(int)NpcRace.Human]));
            foreach (string type in PlanetTypes)
            {
                Assert.That(PopulacePlanner.RaceWeights(type), Has.All.GreaterThan(0));
            }
        }

        [Test]
        public void The_era_temper_shifts_settlement_means()
        {
            RunPopulace plain = Make(9).people, harsh = Make(9, temper: new PopulaceTemper(-60, -150)).people;
            Assert.That(harsh.Settlements.Count, Is.EqualTo(plain.Settlements.Count));
            for (int i = 0; i < plain.Settlements.Count; i++)
            {
                Assert.That(harsh.Settlements[i].MeanHealthPermille, Is.LessThan(plain.Settlements[i].MeanHealthPermille));
                Assert.That(harsh.Settlements[i].MeanAgePermille, Is.LessThanOrEqualTo(plain.Settlements[i].MeanAgePermille));
                Assert.That(harsh.Settlements[i].PrimaryRace, Is.EqualTo(plain.Settlements[i].PrimaryRace));
            }
        }

        [Test]
        public void Life_stages_ascend_with_age()
        {
            NpcLifeStage last = NpcLifeStage.Child;
            for (int age = 0; age <= 1000; age++)
            {
                NpcLifeStage stage = PopulacePlanner.StageFor(age);
                Assert.That(stage, Is.GreaterThanOrEqualTo(last));
                last = stage;
            }

            Assert.That(PopulacePlanner.StageFor(0), Is.EqualTo(NpcLifeStage.Child));
            Assert.That(last, Is.EqualTo(NpcLifeStage.Elder));
            Assert.That(PopulacePlanner.LifespanYears(NpcRace.Elf), Is.GreaterThan(PopulacePlanner.LifespanYears(NpcRace.Human)));
        }

        [Test]
        public void Same_inputs_give_the_same_populace()
        {
            for (ulong seed = 0; seed < 30; seed++)
            {
                RunPopulace a = Make(seed).people, b = Make(seed).people;
                Assert.That(PopulacePlanner.Hash(a), Is.EqualTo(PopulacePlanner.Hash(b)));
                Assert.That(a.Residents.Count, Is.EqualTo(b.Residents.Count));
                for (int i = 0; i < a.Residents.Count; i++)
                {
                    Assert.That(math.distance(a.Residents[i].Direction, b.Residents[i].Direction), Is.Zero);
                    Assert.That(math.distance(a.Residents[i].Facing, b.Residents[i].Facing), Is.Zero);
                }
            }

            Assert.That(PopulacePlanner.Hash(Make(1).people), Is.Not.EqualTo(PopulacePlanner.Hash(Make(2).people)));
        }

        [Test]
        public void Contract_violations_throw()
        {
            var (planet, plan, layout, people) = Make(1);
            Assert.Throws<ContractException>(() => PopulacePlanner.Generate(default, plan, layout, default, null));
            Assert.Throws<ContractException>(() => PopulacePlanner.Generate(planet, null, layout, default, null));
            Assert.Throws<ContractException>(() => PopulacePlanner.Generate(planet, plan, null, default, null));
            Assert.Throws<ContractException>(() => PopulacePlanner.Hash(null));
            Assert.Throws<ContractException>(() => PopulacePlanner.StageFor(-1));
            Assert.Throws<ContractException>(() => PopulacePlanner.StageFor(1001));
            Assert.Throws<ContractException>(() => PopulacePlanner.LifespanYears((NpcRace)99));
            Assert.Throws<ContractException>(() => new PopulaceTemper(201, 0));
            Assert.Throws<ContractException>(() => new PopulaceTemper(0, -201));
            Assert.Throws<ContractException>(() => people.IsBlocked(float3.zero, 0f));
            Assert.Throws<ContractException>(() => people.IsBlocked(new float3(0f, 1f, 0f), -1f));
        }

        [Test]
        public void Snapshot_seed_12345_is_pinned()
        {
            Assert.That(PopulacePlanner.GeneratorVersion, Is.EqualTo(1), "bump the pin with the version");
            ulong h = PopulacePlanner.Hash(Make(12345).people);
            Assert.That(h, Is.EqualTo(SnapshotSeed12345V1), "actual " + h);
        }
    }
}
