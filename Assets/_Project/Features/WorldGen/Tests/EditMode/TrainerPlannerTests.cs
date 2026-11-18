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
    // REQ: docs/design/trainer-battles.md#職務與職能
    public sealed class TrainerPlannerTests
    {
        private const float Radius = 640f;
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        private List<CreatureDef> _pool;
        private RunPlanVocabulary _vocab;

        [SetUp]
        public void SetUp()
        {
            _pool = new List<CreatureDef>();
            var elements = new[]
            {
                Element.Metal, Element.Wood, Element.Water, Element.Fire, Element.Earth,
                Element.Ice, Element.Thunder, Element.Wind, Element.Light, Element.Shadow,
            };
            for (int i = 0; i < elements.Length; i++)
            {
                var c = ScriptableObject.CreateInstance<CreatureDef>();
                _created.Add(c);
                c.SetId("c_" + elements[i].ToString().ToLowerInvariant());
                c.Set("line_" + i, CreatureStage.Adult, CreatureSizeClass.M, AiStyle.Charge, null, null, 0,
                    new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(elements[i], 20) },
                    new CreatureStats(80, 30, 30, 30, 30, 30), null, null, "{}", false);
                _pool.Add(c);
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

        private (PlanetConfig planet, RunPlan plan, RunLayout layout) MakePlanet(ulong seed, float radius = Radius)
        {
            var type = ScriptableObject.CreateInstance<PlanetTypeDef>();
            _created.Add(type);
            type.SetId("verdant_planet");
            type.Set(new List<string> { "forest", "river_valley" }, new List<Element> { Element.Wood, Element.Water });
            var planet = new PlanetConfig(radius, float3.zero);
            RunPlan plan = RunPlanner.Generate(planet, seed, type, CreatureStage.Adult, _pool, _vocab, null);
            RunLayout layout = SettlementPlanner.Generate(planet, plan, null);
            return (planet, plan, layout);
        }

        private static string MultiHabitat(float3 d) =>
            d.y >= 0.35f ? "forest" : d.y <= -0.35f ? "volcano" : d.x >= 0f ? "river_valley" : "slate_cliffs";

        [Test]
        public void Duties_and_professions_cover_all_24_landforms_and_three_origins()
        {
            Assert.That(TrainerPlanner.AllDuties.Count, Is.EqualTo(14));
            Assert.That(TrainerPlanner.AllProfessions.Count, Is.EqualTo(32));

            var coveredByDuties = new HashSet<string>(StringComparer.Ordinal);
            foreach (TrainerDutySpec d in TrainerPlanner.AllDuties)
            {
                Assert.That(d.HabitatKinds, Is.Not.Empty);
                Assert.That(d.Functions, Is.Not.Empty);
                Assert.That(d.ExclusionRadiusMm, Is.GreaterThan(0));
                Assert.That(d.Mode == WanderMode.UnboundedRandom ? d.RadiusMm == 0 : d.RadiusMm > 0, Is.True);
                foreach (string k in d.HabitatKinds)
                {
                    coveredByDuties.Add(k);
                }
            }

            var coveredByProfessions = new HashSet<string>(StringComparer.Ordinal);
            var origins = new HashSet<TrainerOriginKind>();
            foreach (TrainerProfessionSpec p in TrainerPlanner.AllProfessions)
            {
                origins.Add(p.Origin);
                Assert.That(p.CandidateDuties, Is.Not.Empty);
                foreach (TrainerProfessionDuty cd in p.CandidateDuties)
                {
                    foreach (string k in TrainerPlanner.DutySpecFor(cd.Duty).HabitatKinds)
                    {
                        coveredByProfessions.Add(k);
                    }
                }
            }

            Assert.That(origins, Is.EquivalentTo(new[]
            {
                TrainerOriginKind.InGame, TrainerOriginKind.PlanetEra, TrainerOriginKind.CultureTimeline,
            }));
            foreach (string kind in LandformSynth.Kinds)
            {
                Assert.That(coveredByDuties.Contains(kind), Is.True, "missing duty coverage for " + kind);
                Assert.That(coveredByProfessions.Contains(kind), Is.True, "missing profession coverage for " + kind);
            }
        }

        [Test]
        public void Function_radii_and_target_counts_match_design_table()
        {
            Assert.That(TrainerPlanner.FunctionRadiusMm(TrainerFunction.Observant), Is.EqualTo(0));
            Assert.That(TrainerPlanner.FunctionRadiusMm(TrainerFunction.Escort), Is.EqualTo(4000));
            Assert.That(TrainerPlanner.FunctionRadiusMm(TrainerFunction.Sparring), Is.EqualTo(8000));
            Assert.That(TrainerPlanner.FunctionRadiusMm(TrainerFunction.Warden), Is.EqualTo(14000));
            Assert.That(TrainerPlanner.FunctionRadiusMm(TrainerFunction.Pursuit), Is.EqualTo(20000));
            Assert.That(TrainerPlanner.FunctionRadiusMm(TrainerFunction.Champion), Is.EqualTo(28000));

            Assert.That(TrainerPlanner.TargetCountForRadius(320f), Is.EqualTo(3));
            Assert.That(TrainerPlanner.TargetCountForRadius(640f), Is.EqualTo(6));
            Assert.That(TrainerPlanner.TargetCountForRadius(960f), Is.EqualTo(10));
            Assert.That(TrainerPlanner.TargetCountForRadius(1280f), Is.EqualTo(14));
        }

        [Test]
        public void Trainers_spawn_outside_settlements_observe_competitive_exclusion_and_stay_outside_when_stepping()
        {
            for (ulong seed = 0; seed < 8; seed++)
            {
                var (planet, plan, layout) = MakePlanet(seed);
                var clearance = new LayoutClearance(planet, layout, 0f);
                RunTrainers trainers = TrainerPlanner.Generate(planet, plan, layout, _pool, MultiHabitat, null, 12, 24,
                    seed % 2 == 0 ? "steampunk" : "east_asia");
                Assert.That(trainers.Trainers.Count, Is.EqualTo(6));

                for (int i = 0; i < trainers.Trainers.Count; i++)
                {
                    RunTrainer a = trainers.Trainers[i];
                    TrainerDutySpec dSpec = TrainerPlanner.DutySpecFor(a.Duty);
                    Assert.That(dSpec.AllowsHabitat(MultiHabitat(a.HomeDirection)), Is.True);
                    Assert.That(a.SpeedPermilleMean, Is.InRange(dSpec.SpeedPermille.Min, dSpec.SpeedPermille.Max));
                    Assert.That(a.MoveMsMean, Is.InRange(dSpec.MoveMs.Min, dSpec.MoveMs.Max));
                    Assert.That(a.PauseMsMean, Is.InRange(dSpec.PauseMs.Min, dSpec.PauseMs.Max));
                    Assert.That(clearance.IsInsideSettlement(a.HomeDirection, 1.5f), Is.False,
                        $"seed {seed} trainer {i} inside settlement");
                    Assert.That(clearance.IsInsideBuilding(a.HomeDirection, 0.5f), Is.False);
                    Assert.That(a.Team.Count, Is.GreaterThan(0));
                    foreach (TrainerTeamMember m in a.Team)
                    {
                        Assert.That(m.Level, Is.InRange(12, 24));
                    }

                    for (int j = 0; j < i; j++)
                    {
                        RunTrainer b = trainers.Trainers[j];
                        if (a.Duty == b.Duty && !string.Equals(a.ProfessionId, b.ProfessionId, StringComparison.Ordinal))
                        {
                            float distM = math.acos(math.clamp(math.dot(a.HomeDirection, b.HomeDirection), -1f, 1f)) * Radius;
                            float sumM = (a.ActivityRadiusMm + b.ActivityRadiusMm) * 0.001f;
                            Assert.That(distM, Is.GreaterThanOrEqualTo(sumM - 1e-2f),
                                $"seed {seed} trainers {j} and {i} share duty {a.Duty} with different professions and overlap");
                        }
                    }
                }
            }

            var (p0, pl0, ly0) = MakePlanet(11);
            var clr0 = new LayoutClearance(p0, ly0, 0f);
            RunTrainers live = TrainerPlanner.Generate(p0, pl0, ly0, _pool, MultiHabitat, null, 10, 20);
            long now = 0;
            for (int s = 0; s < 300; s++)
            {
                now += 100;
                live.Step(now, 100);
            }

            Assert.That(live.StepVersion, Is.EqualTo(300));
            int moved = 0;
            for (int i = 0; i < live.Trainers.Count; i++)
            {
                float3 pos = live.CurrentDirection(i);
                Assert.That(clr0.IsInsideSettlement(pos, PopulacePlanner.BodyRadiusM), Is.False,
                    $"trainer {i} wandered into settlement");
                Assert.That(clr0.IsInsideBuilding(pos, PopulacePlanner.BodyRadiusM), Is.False,
                    $"trainer {i} wandered into building");
                if (math.distance(pos, live.Trainers[i].HomeDirection) * Radius > 0.1f)
                {
                    moved++;
                }
            }

            Assert.That(moved, Is.GreaterThan(0), "at least some trainers wander outdoors over 30 s");
        }

        [Test]
        public void FindChallenger_skips_observant_and_defeated_trainers()
        {
            RunTrainers trainers = null;
            for (ulong seed = 0; seed < 40; seed++)
            {
                var (p, pl, ly) = MakePlanet(seed);
                RunTrainers cand = TrainerPlanner.Generate(p, pl, ly, _pool, MultiHabitat, null, 10, 20);
                bool hasObs = false, hasActive = false;
                for (int i = 0; i < cand.Trainers.Count; i++)
                {
                    hasObs |= cand.Trainers[i].Function == TrainerFunction.Observant;
                    hasActive |= cand.Trainers[i].Function != TrainerFunction.Observant;
                }

                if (hasObs && hasActive)
                {
                    trainers = cand;
                    break;
                }
            }

            Assert.That(trainers, Is.Not.Null);
            int obs = -1, act = -1;
            for (int i = 0; i < trainers.Trainers.Count; i++)
            {
                if (obs < 0 && trainers.Trainers[i].Function == TrainerFunction.Observant)
                {
                    obs = i;
                }
                else if (act < 0 && trainers.Trainers[i].Function != TrainerFunction.Observant)
                {
                    act = i;
                }
            }

            Assert.That(trainers.FindChallenger(trainers.CurrentDirection(obs), Radius), Is.Not.EqualTo(obs));
            Assert.That(trainers.FindChallenger(trainers.CurrentDirection(act), Radius), Is.EqualTo(act));
            trainers.MarkDefeated(act);
            Assert.That(trainers.IsDefeated(act), Is.True);
            Assert.That(trainers.FindChallenger(trainers.CurrentDirection(act), Radius), Is.Not.EqualTo(act));
        }

        [Test]
        public void Generation_is_deterministic_and_contracts_hold()
        {
            var (p, pl, ly) = MakePlanet(123);
            RunTrainers a = TrainerPlanner.Generate(p, pl, ly, _pool, MultiHabitat, null, 12, 24);
            RunTrainers b = TrainerPlanner.Generate(p, pl, ly, _pool, MultiHabitat, null, 12, 24);
            Assert.That(a.Trainers.Count, Is.EqualTo(b.Trainers.Count));
            for (int i = 0; i < a.Trainers.Count; i++)
            {
                RunTrainer x = a.Trainers[i];
                RunTrainer y = b.Trainers[i];
                Assert.That((x.ProfessionId, x.Duty, x.Function, x.Seed, x.SpeedPermilleMean),
                    Is.EqualTo((y.ProfessionId, y.Duty, y.Function, y.Seed, y.SpeedPermilleMean)));
                Assert.That(math.distance(x.HomeDirection, y.HomeDirection), Is.Zero);
            }

            Assert.Throws<ContractException>(() => TrainerPlanner.Generate(default, pl, ly, _pool, MultiHabitat, null, 10, 20));
            Assert.Throws<ContractException>(() => TrainerPlanner.Generate(p, null, ly, _pool, MultiHabitat, null, 10, 20));
            Assert.Throws<ContractException>(() => TrainerPlanner.Generate(p, pl, null, _pool, MultiHabitat, null, 10, 20));
            Assert.Throws<ContractException>(() => TrainerPlanner.Generate(p, pl, ly, Array.Empty<CreatureDef>(), MultiHabitat, null, 10, 20));
            Assert.Throws<ContractException>(() => TrainerPlanner.Generate(p, pl, ly, _pool, null, null, 10, 20));
            Assert.Throws<ContractException>(() => TrainerPlanner.Generate(p, pl, ly, _pool, _ => "unknown_habitat", null, 10, 20));
            Assert.Throws<ContractException>(() => TrainerPlanner.Generate(p, pl, ly, _pool, MultiHabitat, null, 0, 20));
            Assert.Throws<ContractException>(() => TrainerPlanner.ProfessionSpecFor("missing"));
            Assert.Throws<ContractException>(() => TrainerPlanner.DutySpecFor((TrainerDutyId)99));
            Assert.Throws<ContractException>(() => TrainerPlanner.FunctionRadiusMm((TrainerFunction)99));
            Assert.Throws<ContractException>(() => a.Step(-1, 50));
            Assert.Throws<ContractException>(() => a.Step(0, 0));
            Assert.Throws<ContractException>(() => a.CurrentDirection(-1));
            Assert.Throws<ContractException>(() => a.FindChallenger(float3.zero, Radius));
        }
    }
}
