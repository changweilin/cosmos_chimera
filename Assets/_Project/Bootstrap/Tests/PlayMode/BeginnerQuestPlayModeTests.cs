#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Combat;
using Cosmimera.Core.Random;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class BeginnerQuestPlayModeTests
    {
        private const string Copy = "Assets/_Project/Data/Copy/";

        private static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(a, Is.Not.Null, path);
            return a;
        }

        [UnityTest]
        public IEnumerator Stage4_friend_1v1_loss_heals_grants_supplies_and_continues_to_stage5()
        {
            RunOutcome outcome = null;
            var db = Load<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            var copy = new DemoCopy("zh-TW", Load<TextAsset>(Copy + "strings.json").text,
                Load<TextAsset>(Copy + "names.json").text, Load<TextAsset>(Copy + "affixes.json").text,
                Load<TextAsset>(Copy + "region-themes.json").text, Load<TextAsset>(Copy + "quest-templates.json").text,
                Load<TextAsset>(Copy + "boss-epithets.json").text);
            CombatBalance balance = CombatBalance.FromJson(Load<TextAsset>(Copy + "balance.json").text);
            Progression progression = Progression.FromJson(Load<TextAsset>(Copy + "balance.json").text);
            var actions = Load<InputActionAsset>("Assets/_Project/Settings/Input/InputSystem_Actions.inputactions");

            var wild = new List<CreatureDef>();
            CreatureDef sun = null, moon = null, star = null;
            PlanetTypeDef planetType = null;
            foreach (GameDef d in db.All)
            {
                if (d is CreatureDef c)
                {
                    if (c.Stage == CreatureStage.Juvenile && c.LineId == "starter_sun_line") sun = c;
                    else if (c.Stage == CreatureStage.Juvenile && c.LineId == "starter_moon_line") moon = c;
                    else if (c.Stage == CreatureStage.Juvenile && c.LineId == "starter_star_line") star = c;
                    else if (!c.LineId.StartsWith("starter_")) wild.Add(c);
                }
                else if (d is PlanetTypeDef t && (planetType == null || t.Id == "verdant_planet"))
                {
                    planetType = t;
                }
            }

            var go = new GameObject("BeginnerQuestLossRun");
            var session = go.AddComponent<RunSession>();
            try
            {
                int starterExp = progression.XpThreshold(progression.StarterLevel);
                session.Configure(copy, balance, actions,
                    Load<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat"),
                    new RunSetup
                    {
                        PlanetType = planetType,
                        Seed = DemoGame.BeginnerQuestSeed,
                        Radius = 320f,
                        BossStage = CreatureStage.Juvenile,
                        Party = new List<CreatureDef> { sun },
                        PartyIds = new List<int> { 1 },
                        PartyExp = new List<int> { starterExp },
                        Progression = progression,
                        Materials = MaterialDrops.FromJson(Load<TextAsset>(Copy + "balance.json").text),
                        EnvMaterials = EnvMaterials.FromJson(Load<TextAsset>(Copy + "env-materials.json").text),
                        IsBeginnerQuest = true,
                        FriendId = "luo_en",
                        FriendStarter = star,
                        RivalId = "yumori_sumiha",
                        RivalStarter = moon,
                        PlayerStarterVessel = VesselKind.GlassBead,
                    }, wild, o => outcome = o,
                    Load<Shader>("Assets/_Project/Art/Shaders/ToonTerrain.shader"),
                    Load<Shader>("Assets/_Project/Art/Shaders/ToonOutlined.shader"),
                    Load<Shader>("Assets/_Project/Art/Shaders/Stars.shader"));

                float deadline = Time.realtimeSinceStartup + 60f;
                while (!session.StartedForTests && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.That(session.StartedForTests, Is.True);
                Assert.That(session.BeginnerTrackerForTests.Stage, Is.EqualTo(BeginnerQuestStage.FriendBattleTutorial));
                Assert.That(session.BeginnerFriendBattleTrainerIdForTests, Is.GreaterThanOrEqualTo(0));
                Assert.That(session.VesselForTests, Is.EqualTo(VesselKind.GlassBead));

                // Lose Stage 4 by fainting the player's starter.
                Kill(session, balance, session.PartyUnitsForTests[0]);
                session.StepCombatForTests();

                Assert.That(session.EndedForTests, Is.False, "Stage 4 loss must continue rather than end the run");
                Assert.That(outcome, Is.Null);
                Assert.That(session.BeginnerTrackerForTests.Stage, Is.EqualTo(BeginnerQuestStage.FriendFieldTutorial));
                Assert.That(session.BeginnerTrackerForTests.FriendBattleWon, Is.False);
                Assert.That(session.BeginnerVesselsRemainingForTests, Is.EqualTo(BeginnerQuestRules.StarterVesselGrantCount));
                Assert.That(session.BeginnerHealFoodCountForTests, Is.EqualTo(BeginnerQuestRules.TutorialHealGrantCount));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [UnityTest]
        public IEnumerator Full_beginner_quest_stage4_win_stage5_steps_stage6_wipeout_and_2v2_victory()
        {
            RunOutcome outcome = null;
            var db = Load<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            var copy = new DemoCopy("zh-TW", Load<TextAsset>(Copy + "strings.json").text,
                Load<TextAsset>(Copy + "names.json").text, Load<TextAsset>(Copy + "affixes.json").text,
                Load<TextAsset>(Copy + "region-themes.json").text, Load<TextAsset>(Copy + "quest-templates.json").text,
                Load<TextAsset>(Copy + "boss-epithets.json").text);
            CombatBalance balance = CombatBalance.FromJson(Load<TextAsset>(Copy + "balance.json").text);
            Progression progression = Progression.FromJson(Load<TextAsset>(Copy + "balance.json").text);
            var actions = Load<InputActionAsset>("Assets/_Project/Settings/Input/InputSystem_Actions.inputactions");

            var wild = new List<CreatureDef>();
            CreatureDef sun = null, moon = null, star = null;
            PlanetTypeDef planetType = null;
            foreach (GameDef d in db.All)
            {
                if (d is CreatureDef c)
                {
                    if (c.Stage == CreatureStage.Juvenile && c.LineId == "starter_sun_line") sun = c;
                    else if (c.Stage == CreatureStage.Juvenile && c.LineId == "starter_moon_line") moon = c;
                    else if (c.Stage == CreatureStage.Juvenile && c.LineId == "starter_star_line") star = c;
                    else if (!c.LineId.StartsWith("starter_")) wild.Add(c);
                }
                else if (d is PlanetTypeDef t && (planetType == null || t.Id == "verdant_planet"))
                {
                    planetType = t;
                }
            }

            var go = new GameObject("BeginnerQuestFullRun");
            var session = go.AddComponent<RunSession>();
            try
            {
                int starterExp = progression.XpThreshold(progression.StarterLevel);
                session.Configure(copy, balance, actions,
                    Load<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat"),
                    new RunSetup
                    {
                        PlanetType = planetType,
                        Seed = DemoGame.BeginnerQuestSeed,
                        Radius = 320f,
                        BossStage = CreatureStage.Juvenile,
                        Party = new List<CreatureDef> { sun },
                        PartyIds = new List<int> { 1 },
                        PartyExp = new List<int> { starterExp },
                        Progression = progression,
                        Materials = MaterialDrops.FromJson(Load<TextAsset>(Copy + "balance.json").text),
                        EnvMaterials = EnvMaterials.FromJson(Load<TextAsset>(Copy + "env-materials.json").text),
                        IsBeginnerQuest = true,
                        FriendId = "luo_en",
                        FriendStarter = star,
                        RivalId = "yumori_sumiha",
                        RivalStarter = moon,
                        PlayerStarterVessel = VesselKind.GlassBead,
                    }, wild, o => outcome = o,
                    Load<Shader>("Assets/_Project/Art/Shaders/ToonTerrain.shader"),
                    Load<Shader>("Assets/_Project/Art/Shaders/ToonOutlined.shader"),
                    Load<Shader>("Assets/_Project/Art/Shaders/Stars.shader"));

                float deadline = Time.realtimeSinceStartup + 60f;
                while (!session.StartedForTests && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.That(session.StartedForTests, Is.True);

                // Stage 4 win: faint friend's opponent.
                int friendOppId = session.BeginnerFriendOpponentUnitIdForTests;
                Assert.That(friendOppId, Is.GreaterThanOrEqualTo(0));
                Kill(session, balance, friendOppId);
                session.StepCombatForTests();

                Assert.That(session.BeginnerTrackerForTests.Stage, Is.EqualTo(BeginnerQuestStage.FriendFieldTutorial));
                Assert.That(session.BeginnerTrackerForTests.FriendBattleWon, Is.True);

                // Stage 5: adventure -> 1v1 -> capture -> heal -> ReachBossLair.
                session.CompleteBeginnerFieldStepsForTests();
                Assert.That(session.BeginnerTrackerForTests.FieldStep, Is.EqualTo(BeginnerFieldStep.ReachBossLair));

                // Stage 6: Boss + Rival vs Player + Friend (2vs2).
                session.TriggerBeginnerBoss2v2ForTests();
                Assert.That(session.BeginnerTrackerForTests.Stage, Is.EqualTo(BeginnerQuestStage.BossAndRival2v2));
                Assert.That(session.BeginnerBossTrainerIdForTests, Is.GreaterThanOrEqualTo(0));
                Assert.That(session.BeginnerFriendAllyUnitIdForTests, Is.GreaterThanOrEqualTo(0));
                Assert.That(session.BeginnerBossUnitIdForTests, Is.GreaterThanOrEqualTo(0));
                Assert.That(session.BeginnerRivalUnitIdForTests, Is.GreaterThanOrEqualTo(0));

                // Wipeout during Stage 6 recovers at camp without ending the run.
                Kill(session, balance, session.PartyUnitsForTests[0]);
                session.StepCombatForTests();
                Assert.That(session.EndedForTests, Is.False);
                Assert.That(session.BeginnerTrackerForTests.Stage, Is.EqualTo(BeginnerQuestStage.FriendFieldTutorial));
                Assert.That(session.BeginnerTrackerForTests.FieldStep, Is.EqualTo(BeginnerFieldStep.ReachBossLair));

                // Re-trigger Stage 6 and defeat both Boss and Rival.
                session.TriggerBeginnerBoss2v2ForTests();
                Assert.That(session.BeginnerTrackerForTests.Stage, Is.EqualTo(BeginnerQuestStage.BossAndRival2v2));
                Kill(session, balance, session.BeginnerBossUnitIdForTests);
                Kill(session, balance, session.BeginnerRivalUnitIdForTests);
                session.StepCombatForTests();

                Assert.That(session.EndedForTests, Is.True);
                Assert.That(outcome, Is.Not.Null);
                Assert.That(outcome.Victory, Is.True);
                Assert.That(outcome.Captured.Count, Is.GreaterThanOrEqualTo(1), "juvenile BOSS must be captured on Stage 6 victory");
                Assert.That(session.BeginnerTrackerForTests.Stage, Is.EqualTo(BeginnerQuestStage.MainWorldLobby));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

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
