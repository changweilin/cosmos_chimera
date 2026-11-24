using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.WorldGen.Tests
{
    // REQ: docs/design/trainer-battles.md#地形試煉 (Q-G10: four forms, boss form = planet tier, capturable and evolvable)
    public sealed class TerrainTrialTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private CreatureDef Make(string id, string line, CreatureStage stage, Element element,
            FieldUtility utility = FieldUtility.None)
        {
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            c.Set(line, stage, CreatureSizeClass.M, AiStyle.Charge, null, null, 0, new List<EvolutionMaterial>(), new int[10],
                new List<ElementValue> { new ElementValue(element, 40) }, new CreatureStats(78, 25, 25, 25, 25, 25), null,
                null, utility, "{}", false);
            return c;
        }

        // Fire trial boss line (adolescent and adult hold immune_volcano; juvenile does not) plus fire specialists.
        private List<CreatureDef> FirePool()
        {
            var pool = new List<CreatureDef>
            {
                Make("fire_boss_0", "line_fire_boss", CreatureStage.Juvenile, Element.Fire),
                Make("fire_boss_1", "line_fire_boss", CreatureStage.Adolescent, Element.Fire, FieldUtility.ImmuneVolcano),
                Make("fire_boss_2", "line_fire_boss", CreatureStage.Adult, Element.Fire, FieldUtility.ImmuneVolcano),
                Make("water_x_1", "line_water_x", CreatureStage.Adolescent, Element.Water),
            };
            foreach (string l in new[] { "a", "b", "c", "d", "e" })
            {
                pool.Add(Make("fire_" + l + "_1", "line_fire_" + l, CreatureStage.Adolescent, Element.Fire));
                pool.Add(Make("fire_" + l + "_2", "line_fire_" + l, CreatureStage.Adult, Element.Fire));
            }

            return pool;
        }

        [Test]
        public void Catalog_has_ten_terrains_with_unique_ids_elements_and_aliases()
        {
            Assert.That(AdvancedTerrains.All.Count, Is.EqualTo(10));
            var ids = new HashSet<string>();
            var elements = new HashSet<Element>();
            foreach (AdvancedTerrainSpec s in AdvancedTerrains.All)
            {
                Assert.That(ids.Add(s.Id), Is.True);
                Assert.That(elements.Add(s.Element), Is.True);
                Assert.That(AdvancedTerrains.ForElement(s.Element).Id, Is.EqualTo(s.Id));
                Assert.That(s.TrialMasterId, Is.EqualTo("trial_master_" + s.Id));
                int expectedTeam = s.MinSize == PlanetSize.Moon ? 2 : s.MinSize == PlanetSize.Planet ? 3 : 4;
                Assert.That(s.TrialTeamSize, Is.EqualTo(expectedTeam));
                foreach (string alias in s.Aliases)
                {
                    Assert.That(ids.Add(alias), Is.True, "alias collides");
                    Assert.That(AdvancedTerrains.TryGet(alias, out AdvancedTerrainSpec viaAlias), Is.True);
                    Assert.That(viaAlias.Id, Is.EqualTo(s.Id));
                }
            }

            Assert.That(AdvancedTerrains.TryGet("forest", out _), Is.False);
            Assert.That(AdvancedTerrains.TryGet(null, out _), Is.False);
        }

        [TestCase(PlanetSize.Moon, CreatureStage.Adolescent, false)]
        [TestCase(PlanetSize.Planet, CreatureStage.Adult, false)]
        [TestCase(PlanetSize.Supergiant, CreatureStage.Adult, true)]
        public void Trial_form_follows_the_planet_tier(PlanetSize size, CreatureStage stage, bool overload)
        {
            Assert.That(AdvancedTerrains.TrialStageFor(size), Is.EqualTo(stage));
            Assert.That(AdvancedTerrains.TrialBossOverloadsOn(size), Is.EqualTo(overload));

            Assert.That(TrialPlanner.TryPlan(7, "volcano", size, FirePool(), 30, out TerrainTrialPlan plan), Is.True);
            Assert.That(plan.Stage, Is.EqualTo(stage));
            Assert.That(plan.BossOverloads, Is.EqualTo(overload));
            Assert.That(plan.BossCreatureId, Is.EqualTo(stage == CreatureStage.Adolescent ? "fire_boss_1" : "fire_boss_2"));
        }

        [Test]
        public void Master_team_is_same_element_same_form_same_level_without_the_boss_line()
        {
            List<CreatureDef> pool = FirePool();
            var byId = new Dictionary<string, CreatureDef>();
            foreach (CreatureDef c in pool) byId[c.Id] = c;
            for (ulong seed = 0; seed < 30; seed++)
            {
                Assert.That(TrialPlanner.TryPlan(seed, "volcano", PlanetSize.Moon, pool, 25, out TerrainTrialPlan plan), Is.True);
                Assert.That(plan.Level, Is.EqualTo(25));
                Assert.That(plan.MasterTeam.Count, Is.EqualTo(2));
                var seen = new HashSet<string>();
                foreach (TrainerTeamMember m in plan.MasterTeam)
                {
                    CreatureDef c = byId[m.CreatureId];
                    Assert.That(RunPlanner.MainElement(c), Is.EqualTo(Element.Fire));
                    Assert.That(c.Stage, Is.EqualTo(CreatureStage.Adolescent));
                    Assert.That(c.LineId, Is.Not.EqualTo("line_fire_boss"));
                    Assert.That(m.Level, Is.EqualTo(25));
                    Assert.That(seen.Add(m.CreatureId), Is.True, "no repeats while candidates last");
                }
            }
        }

        [Test]
        public void Team_repeats_species_only_when_candidates_run_out()
        {
            var pool = new List<CreatureDef>
            {
                Make("dark_boss_2", "line_dark_boss", CreatureStage.Adult, Element.Shadow, FieldUtility.ImmuneLabyrinth),
                Make("dark_a_2", "line_dark_a", CreatureStage.Adult, Element.Shadow),
            };
            Assert.That(TrialPlanner.TryPlan(1, "labyrinth", PlanetSize.Supergiant, pool, 60, out TerrainTrialPlan plan), Is.True);
            Assert.That(plan.MasterTeam.Count, Is.EqualTo(4));
            foreach (TrainerTeamMember m in plan.MasterTeam)
            {
                Assert.That(m.CreatureId, Is.EqualTo("dark_a_2"));
            }
        }

        [Test]
        public void Plan_is_independent_of_pool_order_and_reproducible()
        {
            List<CreatureDef> pool = FirePool();
            var reversed = new List<CreatureDef>(pool);
            reversed.Reverse();
            for (ulong seed = 0; seed < 20; seed++)
            {
                TrialPlanner.TryPlan(seed, "volcano", PlanetSize.Planet, pool, 40, out TerrainTrialPlan a);
                TrialPlanner.TryPlan(seed, "caldera", PlanetSize.Planet, reversed, 40, out TerrainTrialPlan b);
                Assert.That(b.BossCreatureId, Is.EqualTo(a.BossCreatureId));
                Assert.That(b.Terrain.Id, Is.EqualTo("volcano"));
                for (int i = 0; i < a.MasterTeam.Count; i++)
                {
                    Assert.That(b.MasterTeam[i].CreatureId, Is.EqualTo(a.MasterTeam[i].CreatureId));
                }
            }
        }

        [Test]
        public void Master_look_is_the_same_on_every_planet()
        {
            TrialPlanner.TryPlan(1, "volcano", PlanetSize.Moon, FirePool(), 20, out TerrainTrialPlan a);
            TrialPlanner.TryPlan(999, "volcano", PlanetSize.Supergiant, FirePool(), 80, out TerrainTrialPlan b);
            Assert.That(b.MasterLookSeed, Is.EqualTo(a.MasterLookSeed));
        }

        [Test]
        public void Missing_boss_form_or_specialists_yield_no_trial()
        {
            var noAdolescentBoss = new List<CreatureDef>
            {
                Make("fire_boss_2", "line_fire_boss", CreatureStage.Adult, Element.Fire, FieldUtility.ImmuneVolcano),
                Make("fire_a_1", "line_fire_a", CreatureStage.Adolescent, Element.Fire),
            };
            Assert.That(TrialPlanner.TryPlan(1, "volcano", PlanetSize.Moon, noAdolescentBoss, 20, out TerrainTrialPlan p1), Is.False);
            Assert.That(p1, Is.Null);

            var noSpecialists = new List<CreatureDef>
            {
                Make("fire_boss_1", "line_fire_boss", CreatureStage.Adolescent, Element.Fire, FieldUtility.ImmuneVolcano),
            };
            Assert.That(TrialPlanner.TryPlan(1, "volcano", PlanetSize.Moon, noSpecialists, 20, out TerrainTrialPlan p2), Is.False);
            Assert.That(p2, Is.Null);
        }

        [Test]
        public void Planner_contract_violations_throw()
        {
            List<CreatureDef> pool = FirePool();
            Assert.Throws<ContractException>(() => TrialPlanner.TryPlan(1, "forest", PlanetSize.Moon, pool, 20, out _));
            Assert.Throws<ContractException>(() => TrialPlanner.TryPlan(1, "ice_cap", PlanetSize.Moon, pool, 20, out _));
            Assert.Throws<ContractException>(() => TrialPlanner.TryPlan(1, "volcano", PlanetSize.Moon, null, 20, out _));
            Assert.Throws<ContractException>(() => TrialPlanner.TryPlan(1, "volcano", PlanetSize.Moon, pool, 0, out _));
            Assert.Throws<ContractException>(() => TrialPlanner.TryPlan(1, "volcano", PlanetSize.Moon, pool, 101, out _));
            Assert.Throws<ContractException>(() =>
                TrialPlanner.TryPlan(1, "volcano", PlanetSize.Moon, new List<CreatureDef> { null }, 20, out _));
            Assert.Throws<ContractException>(() => AdvancedTerrains.TrialStageFor(PlanetSize.Asteroid));
            Assert.Throws<ContractException>(() => AdvancedTerrains.TrialBossOverloadsOn(PlanetSize.Asteroid));
            Assert.Throws<ContractException>(() => AdvancedTerrains.ForElement((Element)99));
            Assert.Throws<ContractException>(() => new TrialProgress(null));
        }

        private TrialProgress NewProgress()
        {
            Assert.That(TrialPlanner.TryPlan(3, "volcano", PlanetSize.Moon, FirePool(), 20, out TerrainTrialPlan plan), Is.True);
            return new TrialProgress(plan);
        }

        [Test]
        public void Trial_runs_talk_guard_arena_final_win_capture()
        {
            TrialProgress t = NewProgress();
            Assert.That(t.Step, Is.EqualTo(TrialStep.NotStarted));
            Assert.That(t.WaivesImmunity, Is.False);
            Assert.That(t.OnGuardDefeated(), Is.False, "no trial started yet");
            Assert.That(t.OnArenaReached(), Is.False);

            Assert.That(t.Talk(), Is.True);
            Assert.That(t.Talk(), Is.False);
            Assert.That(t.WaivesImmunity && t.ArenaSealed, Is.True);
            Assert.That(t.OnArenaReached(), Is.False, "arena sealed until the guard falls");

            Assert.That(t.OnGuardDefeated(), Is.True);
            Assert.That(t.ArenaSealed, Is.False);
            Assert.That(t.OnArenaReached(), Is.True);
            Assert.That(t.Step, Is.EqualTo(TrialStep.FinalBattle));
            Assert.That(t.WaivesImmunity, Is.True);
            Assert.That(t.TryCaptureBoss(), Is.False, "not before winning");

            TrialVictory? v = t.OnFinalBattleEnded(true, false);
            Assert.That(v.HasValue, Is.True);
            Assert.That(v.Value.FirstWin, Is.True);
            Assert.That(v.Value.VesselElement, Is.EqualTo(Element.Fire));
            Assert.That(v.Value.BadgeTerrainId, Is.EqualTo("volcano"));
            Assert.That(t.Step, Is.EqualTo(TrialStep.Won));
            Assert.That(t.WaivesImmunity, Is.False, "outside the trial the immunity is needed again");

            Assert.That(t.TryCaptureBoss(), Is.True);
            Assert.That(t.BossCaptured, Is.True);
            Assert.That(t.TryCaptureBoss(), Is.False, "one guaranteed capture");
            Assert.That(t.Talk(), Is.False, "a won trial is not re-run in the same run");
        }

        [Test]
        public void Loss_keeps_completed_steps_and_allows_a_rematch()
        {
            TrialProgress t = NewProgress();
            t.Talk();
            t.OnGuardDefeated();
            t.OnArenaReached();

            Assert.That(t.OnFinalBattleEnded(false, false).HasValue, Is.False);
            Assert.That(t.Step, Is.EqualTo(TrialStep.GuardDefeated));
            Assert.That(t.Losses, Is.EqualTo(1));
            Assert.That(t.WaivesImmunity, Is.True);

            Assert.That(t.OnArenaReached(), Is.True);
            TrialVictory? v = t.OnFinalBattleEnded(true, true);
            Assert.That(v.Value.FirstWin, Is.False, "repeat wins grant no first-win reward");
            Assert.That(t.TryCaptureBoss(), Is.True, "the boss is capturable on every win");
        }

        [Test]
        public void Ending_a_final_battle_that_is_not_running_throws()
        {
            TrialProgress t = NewProgress();
            Assert.Throws<ContractException>(() => t.OnFinalBattleEnded(true, false));
            t.Talk();
            Assert.Throws<ContractException>(() => t.OnFinalBattleEnded(false, false));
        }
    }
}
