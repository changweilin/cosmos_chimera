#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Combat;
using Cosmimera.Core.Random;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    /// <summary>
    /// One terrain trial end to end on the real rig: talk to the master at the planet's dungeon entrance, walk into the
    /// indoor room dungeon, knock out the guard elite, reach the arena, beat master + dungeon boss, capture it, leave.
    /// REQ: docs/design/trainer-battles.md#地形試煉, planet-world.md#進階地形地城, ADR-0011.
    /// </summary>
    public sealed class RunSessionTrialTests
    {
        private const string Copy = "Assets/_Project/Data/Copy/";
        private readonly List<GameObject> _live = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _live)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            _live.Clear();
        }

        private static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(a, Is.Not.Null, path);
            return a;
        }

        [UnityTest]
        public IEnumerator A_room_dungeon_trial_runs_from_the_master_to_the_captured_boss_and_back()
        {
            var db = Load<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            var copy = new DemoCopy("zh-TW", Load<TextAsset>(Copy + "strings.json").text, Load<TextAsset>(Copy + "names.json").text,
                Load<TextAsset>(Copy + "affixes.json").text, Load<TextAsset>(Copy + "region-themes.json").text,
                Load<TextAsset>(Copy + "quest-templates.json").text, Load<TextAsset>(Copy + "boss-epithets.json").text);
            string balanceJson = Load<TextAsset>(Copy + "balance.json").text;
            CombatBalance balance = CombatBalance.FromJson(balanceJson);
            Progression progression = Progression.FromJson(balanceJson);
            var wild = new List<CreatureDef>();
            var party = new List<CreatureDef>();
            PlanetTypeDef type = null;
            foreach (GameDef d in db.All)
            {
                if (d is CreatureDef c)
                {
                    (c.LineId.StartsWith("starter_") ? party : wild).Add(c);
                }
                else if (d is PlanetTypeDef t && t.Id == "mineral_planet")
                {
                    type = t;
                }
            }

            Assert.That(type, Is.Not.Null, "mineral_planet");
            // Metal / earth planet on the moon tier: its entrance is the laboratory or the mine, both room dungeons.
            party.RemoveAll(c => c.Stage != CreatureStage.Adult);
            CreatureStage stage = CreatureStage.Adolescent;
            float radius = DemoGame.RadiusFor(BossDraw.SizeFor(stage));

            RunSession s = null;
            for (ulong seed = 11; seed < 17 && s == null; seed++)
            {
                var session = new GameObject("RunTrial").AddComponent<RunSession>();
                _live.Add(session.gameObject);
                session.Configure(copy, balance, Load<InputActionAsset>("Assets/_Project/Settings/Input/InputSystem_Actions.inputactions"),
                    Load<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat"),
                    new RunSetup
                    {
                        PlanetType = type, Seed = seed, Radius = radius, BossStage = stage, Party = party, Landforms = false,
                        PartyIds = Enumerable.Range(1, party.Count).ToList(),
                        PartyExp = Enumerable.Repeat(progression.XpThreshold(60), party.Count).ToList(),
                        Progression = progression, Materials = MaterialDrops.FromJson(balanceJson),
                    }, wild, o => { }, Load<Shader>("Assets/_Project/Art/Shaders/ToonTerrain.shader"),
                    Load<Shader>("Assets/_Project/Art/Shaders/ToonOutlined.shader"));
                if (session.HasTrialForTests)
                {
                    s = session;
                }
                else
                {
                    Object.Destroy(session.gameObject);
                    yield return null;
                }
            }

            Assert.That(s, Is.Not.Null, "a mineral moon in seeds 11..16 opens a room-dungeon trial");
            Assert.That(AdvancedTerrains.TryGet(s.TrialTerrainForTests, out AdvancedTerrainSpec spec) &&
                        spec.Form == AdvancedDungeonForm.Rooms, Is.True);
            yield return WaitUntil(() => s.StartedForTests, 60f, "landing");
            s.SuspendCameraForTests();

            // --- Talk: the master leads the party indoors -------------------------------------------------------
            Assert.That(s.TrialStepForTests, Is.EqualTo(TrialStep.NotStarted));
            s.PlaceOnCurrentGroundForTests(s.TrialMasterDirectionForTests);
            yield return null;
            yield return WaitUntil(() => !s.InCombatForTests, 30f, "quiet at the entrance");
            Assert.That(s.InteractTrialForTests(), Is.True);
            Assert.That(s.TrialStepForTests, Is.EqualTo(TrialStep.Guided));
            Assert.That(s.TrialInsideForTests, Is.True);
            Assert.That(s.SurfaceTerrainActiveForTests, Is.False, "the surface is not drawn indoors");
            yield return WaitUntil(() => !s.TrialTransitPendingForTests, 60f, "indoor floor colliders");
            RoomDungeonGeometry g = s.TrialGeometryForTests;
            Assert.That(g.RoomAt(s.DirectionForTests(s.PlayerPositionForTests)), Is.EqualTo(g.Dungeon.EntranceRoom));
            Assert.That(s.TrialInteriorUnitsForTests.Count, Is.GreaterThan(0));

            // --- Step 1: the guard elite unseals the arena -----------------------------------------------------
            foreach (int id in s.TrialInteriorUnitsForTests)
            {
                Kill(s, balance, id);
            }

            yield return WaitUntil(() => s.TrialStepForTests == TrialStep.GuardDefeated, 10f, "guard knock-out noticed");

            // --- Step 2: the arena starts the final battle -----------------------------------------------------
            yield return WaitUntil(() => !s.InCombatForTests, 60f, "battle with the dungeon wilds over");
            s.PlaceOnCurrentGroundForTests(g.RoomCenter(g.Dungeon.BossRoom));
            yield return WaitUntil(() => s.TrialStepForTests == TrialStep.FinalBattle, 20f, "arena reached");
            Assert.That(s.TrialFinalUnitsForTests.Count, Is.EqualTo(1 + spec.TrialTeamSize), "boss + master team");

            float deadline = Time.realtimeSinceStartup + 90f;
            while (s.TrialStepForTests == TrialStep.FinalBattle && Time.realtimeSinceStartup < deadline)
            {
                foreach (int id in s.TrialFinalUnitsForTests)
                {
                    if (s.UnitForTests(id).Fielded && !s.UnitForTests(id).IsFainted)
                    {
                        Kill(s, balance, id);
                    }
                }

                yield return null;
            }

            Assert.That(s.TrialStepForTests, Is.EqualTo(TrialStep.Won));
            Assert.That(s.OutcomeForTests.CompletedTrials, Does.Contain(spec.Id), "first win earns the badge");
            Assert.That(s.OutcomeForTests.Captured.Any(c => c.speciesId.Length > 0 &&
                                                           db.All.OfType<CreatureDef>().First(d => d.Id == c.speciesId)
                                                               .FieldUtility == spec.Immunity),
                Is.True, "the dungeon boss is captured");

            // --- Leave through the exit pad -------------------------------------------------------------------
            yield return WaitUntil(() => !s.InCombatForTests, 30f, "trial battle over");
            s.PlaceOnCurrentGroundForTests(g.Exit);
            yield return null;
            Assert.That(s.InteractTrialForTests(), Is.True);
            Assert.That(s.TrialInsideForTests, Is.False);
            Assert.That(s.SurfaceTerrainActiveForTests, Is.True);
            yield return WaitUntil(() => !s.TrialTransitPendingForTests, 30f, "surface colliders");
        }

        // REQ: planet-world.md#進階地形地城 (Q-W45) — the same trial on the planet surface: the maze stands around the
        // entrance wonder, its outer door opens when the master is talked to, the final battle is fought round the wonder.
        [UnityTest]
        public IEnumerator A_surface_maze_trial_runs_round_the_wonder_without_leaving_the_planet()
        {
            var db = Load<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            var copy = new DemoCopy("zh-TW", Load<TextAsset>(Copy + "strings.json").text, Load<TextAsset>(Copy + "names.json").text,
                Load<TextAsset>(Copy + "affixes.json").text, Load<TextAsset>(Copy + "region-themes.json").text,
                Load<TextAsset>(Copy + "quest-templates.json").text, Load<TextAsset>(Copy + "boss-epithets.json").text);
            string balanceJson = Load<TextAsset>(Copy + "balance.json").text;
            CombatBalance balance = CombatBalance.FromJson(balanceJson);
            Progression progression = Progression.FromJson(balanceJson);
            var wild = new List<CreatureDef>();
            var party = new List<CreatureDef>();
            PlanetTypeDef type = null;
            foreach (GameDef d in db.All)
            {
                if (d is CreatureDef c)
                {
                    (c.LineId.StartsWith("starter_") ? party : wild).Add(c);
                }
                else if (d is PlanetTypeDef t && t.Id == "lava_planet")
                {
                    type = t;
                }
            }

            // Fire / earth moon: its entrance is the volcano (surface maze) or the mine (rooms).
            party.RemoveAll(c => c.Stage != CreatureStage.Adult);
            CreatureStage stage = CreatureStage.Adolescent;
            float radius = DemoGame.RadiusFor(BossDraw.SizeFor(stage));
            RunSession s = null;
            for (ulong seed = 11; seed < 41 && s == null; seed++)
            {
                var session = new GameObject("RunMaze").AddComponent<RunSession>();
                _live.Add(session.gameObject);
                session.Configure(copy, balance, Load<InputActionAsset>("Assets/_Project/Settings/Input/InputSystem_Actions.inputactions"),
                    Load<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat"),
                    new RunSetup
                    {
                        PlanetType = type, Seed = seed, Radius = radius, BossStage = stage, Party = party, Landforms = false,
                        PartyIds = Enumerable.Range(1, party.Count).ToList(),
                        PartyExp = Enumerable.Repeat(progression.XpThreshold(60), party.Count).ToList(),
                        Progression = progression, Materials = MaterialDrops.FromJson(balanceJson),
                    }, wild, o => { }, Load<Shader>("Assets/_Project/Art/Shaders/ToonTerrain.shader"),
                    Load<Shader>("Assets/_Project/Art/Shaders/ToonOutlined.shader"));
                if (session.HasTrialForTests && session.TrialIsMazeForTests)
                {
                    s = session;
                }
                else
                {
                    Object.Destroy(session.gameObject);
                    yield return null;
                }
            }

            Assert.That(s, Is.Not.Null, "a lava moon in seeds 11..40 opens a surface-maze trial");
            Assert.That(s.TrialTerrainForTests, Is.EqualTo("volcano"));
            yield return WaitUntil(() => s.StartedForTests, 60f, "landing");
            s.SuspendCameraForTests();
            RoomDungeonGeometry g = s.TrialGeometryForTests;
            float3 wonder = g.RoomCenter(g.Dungeon.BossRoom);
            Assert.That(s.SurfaceOffWallsForTests(wonder), Is.False, "the maze area is cleared of scatter and residents");
            Assert.That(g.Contains(s.DirectionForTests(s.PlayerPositionForTests)), Is.False, "landed outside the maze");

            // --- The outer door opens only once the master has spoken -------------------------------------------
            Assert.That(s.SurfaceBlockedForTests(g.OuterGate, 0.5f), Is.True, "outer door sealed before the talk");
            s.PlaceOnCurrentGroundForTests(s.TrialMasterDirectionForTests);
            yield return null;
            yield return WaitUntil(() => !s.InCombatForTests, 30f, "quiet at the door");
            Assert.That(s.InteractTrialForTests(), Is.True);
            Assert.That(s.TrialStepForTests, Is.EqualTo(TrialStep.Guided));
            Assert.That(s.TrialInsideForTests, Is.False, "no scene swap for a surface maze");
            Assert.That(s.SurfaceTerrainActiveForTests, Is.True);
            Assert.That(s.SurfaceBlockedForTests(g.OuterGate, 0.5f), Is.False, "outer door open");
            Assert.That(s.SurfaceBlockedForTests(g.ArenaGate, 0.5f), Is.True, "arena still sealed");

            // --- Guard elite, then the arena round the wonder ------------------------------------------------
            foreach (int id in s.TrialInteriorUnitsForTests)
            {
                Kill(s, balance, id);
            }

            yield return WaitUntil(() => s.TrialStepForTests == TrialStep.GuardDefeated, 10f, "guard knock-out noticed");
            Assert.That(s.SurfaceBlockedForTests(g.ArenaGate, 0.5f), Is.False, "arena unsealed");
            yield return WaitUntil(() => !s.InCombatForTests, 60f, "battle with the maze wilds over");
            s.PlaceOnCurrentGroundForTests(g.SpawnPoint(g.Dungeon.BossRoom, 4));
            yield return WaitUntil(() => s.TrialStepForTests == TrialStep.FinalBattle, 20f, "arena reached");
            foreach (int id in s.TrialFinalUnitsForTests)
            {
                Assert.That(math.distance(s.DirectionForTests(s.UnitForTests(id).Position), wonder) * radius,
                    Is.GreaterThan(5f), "nobody spawns inside the wonder");
            }

            float deadline = Time.realtimeSinceStartup + 90f;
            while (s.TrialStepForTests == TrialStep.FinalBattle && Time.realtimeSinceStartup < deadline)
            {
                foreach (int id in s.TrialFinalUnitsForTests)
                {
                    if (s.UnitForTests(id).Fielded && !s.UnitForTests(id).IsFainted)
                    {
                        Kill(s, balance, id);
                    }
                }

                yield return null;
            }

            Assert.That(s.TrialStepForTests, Is.EqualTo(TrialStep.Won));
            Assert.That(s.OutcomeForTests.CompletedTrials, Does.Contain("volcano"));
            Assert.That(s.OutcomeForTests.Captured.Any(c =>
                    db.All.OfType<CreatureDef>().First(d => d.Id == c.speciesId).FieldUtility == FieldUtility.ImmuneVolcano),
                Is.True, "the maze boss is captured");
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, float seconds, string what)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(condition(), Is.True, what + " within " + seconds + " s");
        }

        // Same overwhelming hit as RunSessionProgressionTests.Kill: max power in each element until the unit faints.
        private static void Kill(RunSession s, CombatBalance balance, int unitId)
        {
            long now = s.NowMsForTests;
            var mastery = new int[10];
            for (int i = 0; i < mastery.Length; i++)
            {
                mastery[i] = DamageModel.MaxMasteryPercent;
            }

            Pcg32 rng = new Pcg32(99UL, 'T');
            for (int e = 0; e < 10 && !s.UnitForTests(unitId).IsFainted; e++)
            {
                var attacker = new Combatant(balance.Stats, balance.Effects, new CreatureStats(0, 0, 10000, 10000, 0, 0),
                    mastery, new int[10], new EffectBook(), 1);
                var move = new CastMove("test_nuke", (Element)e, MoveKind.Damage, DamageModel.MaxPower, 100, 0, false, default, 0);
                Assert.That(attacker.BeginCast(0, move, now, out CastContext cast), Is.EqualTo(CastResult.Accepted));
                for (int hit = 0; hit < 64 && !s.UnitForTests(unitId).IsFainted; hit++)
                {
                    s.ReceiveHostileForTests(unitId, cast, ref rng);
                }
            }

            Assert.That(s.UnitForTests(unitId).IsFainted, Is.True, "unit " + unitId + " taken to 0 HP");
        }
    }
}
#endif
