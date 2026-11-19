using System;
using System.Collections.Generic;
using System.IO;
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

        [Test]
        public void MaxStage_caps_trainer_team_and_falls_back_when_none_match()
        {
            var (p, pl, ly) = MakePlanet(123);
            var mixedPool = new List<CreatureDef>(_pool);
            var juv = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(juv);
            juv.SetId("juv_wood");
            juv.Set("line_juv", CreatureStage.Juvenile, CreatureSizeClass.S, AiStyle.Charge, null, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Wood, 20) },
                new CreatureStats(60, 20, 20, 20, 20, 20), null, null, "{}", false);
            mixedPool.Add(juv);

            RunTrainers capped = TrainerPlanner.Generate(p, pl, ly, mixedPool, MultiHabitat, null, 12, 24,
                maxStage: CreatureStage.Juvenile);
            foreach (RunTrainer t in capped.Trainers)
            {
                foreach (TrainerTeamMember m in t.Team)
                {
                    Assert.That(m.CreatureId, Is.EqualTo("juv_wood"));
                }
            }

            // Adult-only pool with maxStage Juvenile falls back to the full pool cleanly.
            RunTrainers fallback = TrainerPlanner.Generate(p, pl, ly, _pool, MultiHabitat, null, 12, 24,
                maxStage: CreatureStage.Juvenile);
            Assert.That(fallback.Trainers.Count, Is.EqualTo(capped.Trainers.Count));
            Assert.Throws<ContractException>(() =>
                TrainerPlanner.Generate(p, pl, ly, _pool, MultiHabitat, null, 12, 24, maxStage: (CreatureStage)99));
        }

        [Test]
        public void Sight_pursuit_evasion_and_observant_interaction_behave_per_spec()
        {
            RunTrainers trainers = null;
            int chaserIdx = -1;
            int obsIdx = -1;
            for (ulong seed = 100; seed < 140; seed++)
            {
                var (p, pl, ly) = MakePlanet(seed);
                RunTrainers candidate = TrainerPlanner.Generate(p, pl, ly, _pool, MultiHabitat, null, 12, 24);
                int cIdx = -1;
                int oIdx = -1;
                for (int i = 0; i < candidate.Trainers.Count; i++)
                {
                    TrainerFunction fn = candidate.Trainers[i].Function;
                    if (cIdx < 0 && fn != TrainerFunction.Observant && fn != TrainerFunction.Champion)
                    {
                        cIdx = i;
                    }
                    else if (oIdx < 0 && fn == TrainerFunction.Observant)
                    {
                        oIdx = i;
                    }
                }

                if (cIdx >= 0 && oIdx >= 0)
                {
                    trainers = candidate;
                    chaserIdx = cIdx;
                    obsIdx = oIdx;
                    break;
                }
            }

            Assert.That(trainers, Is.Not.Null, "expected a seed containing both chasing and observant trainers");

            // Observant never spots, but is reachable via NearestInteractable within 3m.
            float3 obsDir = trainers.CurrentDirection(obsIdx);
            float3 obsFace = trainers.CurrentFacing(obsIdx);
            float3 nearObs = math.normalize(obsDir + obsFace * (1.5f / Radius));
            Assert.That(trainers.IsInSight(obsIdx, nearObs, Radius), Is.False);
            Assert.That(trainers.NearestInteractable(nearObs, Radius), Is.EqualTo(obsIdx));

            // Forward sight cone triggers spotting, behind does not.
            float3 tDir = trainers.CurrentDirection(chaserIdx);
            float3 tFace = trainers.CurrentFacing(chaserIdx);
            float sightDist = math.min(trainers.Trainers[chaserIdx].ChallengeRadiusMm * 0.0005f, 6f);
            float3 inFront = math.normalize(tDir + tFace * (sightDist / Radius));
            float3 behind = math.normalize(tDir - tFace * (sightDist / Radius));
            Assert.That(trainers.IsInSight(chaserIdx, behind, Radius), Is.False);
            Assert.That(trainers.IsInSight(chaserIdx, inFront, Radius), Is.True);

            TrainerPursuitResult spot = trainers.StepPursuit(inFront, Radius, 100, out int spottedIdx);
            Assert.That(spot, Is.EqualTo(TrainerPursuitResult.Spotted));
            Assert.That(spottedIdx, Is.EqualTo(chaserIdx));
            Assert.That(trainers.PursuingTrainer, Is.EqualTo(chaserIdx));

            // Evading past EvadeRadiusM (25m) drops pursuit.
            float3 farAway = math.normalize(tDir + tFace * ((RunTrainers.EvadeRadiusM + 3f) / Radius));
            TrainerPursuitResult evade = trainers.StepPursuit(farAway, Radius, 100, out int evadedIdx);
            Assert.That(evade, Is.EqualTo(TrainerPursuitResult.Evaded));
            Assert.That(evadedIdx, Is.EqualTo(chaserIdx));
            Assert.That(trainers.PursuingTrainer, Is.EqualTo(-1));

            // Spot again and chase until Engaged.
            Assert.That(trainers.StepPursuit(inFront, Radius, 100, out _), Is.EqualTo(TrainerPursuitResult.Spotted));
            TrainerPursuitResult last = TrainerPursuitResult.Spotted;
            for (int tick = 0; tick < 80 && last != TrainerPursuitResult.Engaged; tick++)
            {
                last = trainers.StepPursuit(inFront, Radius, 100, out int engagedIdx);
                if (last == TrainerPursuitResult.Engaged)
                {
                    Assert.That(engagedIdx, Is.EqualTo(chaserIdx));
                }
            }

            Assert.That(last, Is.EqualTo(TrainerPursuitResult.Engaged));
            Assert.That(trainers.PursuingTrainer, Is.EqualTo(-1));
        }

        [Test]
        public void Generate_does_not_perturb_existing_planner_streams_and_matches_snapshot()
        {
            const ulong seed = 123;
            NicheDef wideNiche = ScriptableObject.CreateInstance<NicheDef>();
            _created.Add(wideNiche);
            wideNiche.SetId("niche_wide");
            wideNiche.Set("niche_wide",
                new List<string>(TrainerPlanner.DutySpecFor(TrainerDutyId.RovingMinstrel).HabitatKinds),
                new List<HabitWeight> { new HabitWeight(Habit.Tolerant, 1) },
                WanderMode.BoundedRandom, 10000, 0,
                new WanderDistribution(300, 40, 50, 50, 900),
                new WanderDistribution(5000, 1000, 1500, 500, 60000),
                new WanderDistribution(5000, 1200, 2000, 250, 60000));

            var ecoPool = new List<CreatureDef>(_pool.Count);
            for (int i = 0; i < _pool.Count; i++)
            {
                var c = ScriptableObject.CreateInstance<CreatureDef>();
                _created.Add(c);
                c.SetId("eco_" + _pool[i].Id);
                c.Set("line_" + i, CreatureStage.Adult, CreatureSizeClass.M, AiStyle.Charge,
                    new List<NicheCandidate> { new NicheCandidate(wideNiche, 1) }, null, 0,
                    new List<EvolutionMaterial>(), new int[10], new List<ElementValue>(_pool[i].Mastery),
                    new CreatureStats(80, 30, 30, 30, 30, 30), null, null, "{}", false);
                ecoPool.Add(c);
            }

            PlanetTypeDef type = ScriptableObject.CreateInstance<PlanetTypeDef>();
            _created.Add(type);
            type.SetId("verdant_planet");
            var planet = new PlanetConfig(Radius, float3.zero);

            RunPlan plA = RunPlanner.Generate(planet, seed, type, CreatureStage.Adult, ecoPool, _vocab, null);
            RunLayout lyA = SettlementPlanner.Generate(planet, plA, null);
            var clearanceA = new LayoutClearance(planet, lyA, 0f);
            RunPlan ecoPlanA = RunEcology.Apply(plA, ecoPool, planet.Radius, MultiHabitat, null, clearanceA.IsInsideSettlement);
            RunPopulace popA = PopulacePlanner.Generate(planet, ecoPlanA, lyA, default, null);

            var actualLines = new List<string>(3);
            foreach (ulong s in new ulong[] { 123, 456, 789 })
            {
                var (ps, pls, lys) = MakePlanet(s);
                RunTrainers tr = TrainerPlanner.Generate(ps, pls, lys, _pool, MultiHabitat, null, 12, 24);
                actualLines.Add($"{s}:0x{TrainerPlanner.Hash(tr):X16}");
            }

            // Running TrainerPlanner above must leave every prior generator's output identical.
            RunPlan plB = RunPlanner.Generate(planet, seed, type, CreatureStage.Adult, ecoPool, _vocab, null);
            RunLayout lyB = SettlementPlanner.Generate(planet, plB, null);
            var clearanceB = new LayoutClearance(planet, lyB, 0f);
            RunPlan ecoPlanB = RunEcology.Apply(plB, ecoPool, planet.Radius, MultiHabitat, null, clearanceB.IsInsideSettlement);
            RunPopulace popB = PopulacePlanner.Generate(planet, ecoPlanB, lyB, default, null);

            Assert.That(RunPlanner.Hash(plB), Is.EqualTo(RunPlanner.Hash(plA)));
            Assert.That(SettlementPlanner.Hash(lyB), Is.EqualTo(SettlementPlanner.Hash(lyA)));
            Assert.That(RunEcology.Hash(ecoPlanB), Is.EqualTo(RunEcology.Hash(ecoPlanA)));
            Assert.That(PopulacePlanner.Hash(popB), Is.EqualTo(PopulacePlanner.Hash(popA)));

            string snapPath = Path.Combine(Application.dataPath, "_Project/Features/WorldGen/Tests/Snapshots/trainer-planner-v1.txt");
            if (!File.Exists(snapPath))
            {
                string dump = Path.Combine(Application.dataPath, "../Logs/trainer-planner-v1.actual.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(dump));
                File.WriteAllLines(dump, actualLines);
                Assert.Fail($"snapshot missing; actual written to {dump}");
            }

            string[] expected = File.ReadAllLines(snapPath);
            CollectionAssert.AreEqual(expected, actualLines);
        }
    }
}
