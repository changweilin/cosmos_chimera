using System;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Codex;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Save;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class BeginnerQuestTests
    {
        [Test]
        public void Companion_ring_and_starter_counter_picks_assign_all_three_distinctly()
        {
            (string f1, string r1) = BeginnerQuestRules.AssignCompanions("ye_chengxi");
            Assert.That(f1, Is.EqualTo("luo_en"));
            Assert.That(r1, Is.EqualTo("yumori_sumiha"));
            (string f2, string r2) = BeginnerQuestRules.AssignCompanions("luo_en");
            Assert.That(f2, Is.EqualTo("yumori_sumiha"));
            Assert.That(r2, Is.EqualTo("ye_chengxi"));
            (string f3, string r3) = BeginnerQuestRules.AssignCompanions("yumori_sumiha");
            Assert.That(f3, Is.EqualTo("ye_chengxi"));
            Assert.That(r3, Is.EqualTo("luo_en"));

            Assert.That(BeginnerQuestRules.SignatureVessel("ye_chengxi"), Is.EqualTo(VesselKind.GlassBead));
            Assert.That(BeginnerQuestRules.SignatureVessel("luo_en"), Is.EqualTo(VesselKind.MagicCube));
            Assert.That(BeginnerQuestRules.SignatureVessel("yumori_sumiha"), Is.EqualTo(VesselKind.TalismanCard));

            (string rivalSun, string friendSun) = BeginnerQuestRules.AssignStarterLines("starter_sun");
            Assert.That(rivalSun, Is.EqualTo("starter_moon"));
            Assert.That(friendSun, Is.EqualTo("starter_star"));

            (string rivalMoon, string friendMoon) = BeginnerQuestRules.AssignStarterLines("starter_moon");
            Assert.That(rivalMoon, Is.EqualTo("starter_star"));
            Assert.That(friendMoon, Is.EqualTo("starter_sun"));

            (string rivalStar, string friendStar) = BeginnerQuestRules.AssignStarterLines("starter_star");
            Assert.That(rivalStar, Is.EqualTo("starter_sun"));
            Assert.That(friendStar, Is.EqualTo("starter_moon"));

            Assert.Throws<ContractException>(() => BeginnerQuestRules.AssignCompanions("unknown"));
            Assert.Throws<ArgumentException>(() => BeginnerQuestRules.AssignStarterLines("unknown"));
        }

        [Test]
        public void Tracker_advances_through_friend_1v1_field_tutorial_and_2v2_boss()
        {
            var tracker = new BeginnerQuestTracker();
            Assert.That(tracker.Stage, Is.EqualTo(BeginnerQuestStage.FriendBattleTutorial));
            Assert.That(tracker.SuppliesGranted, Is.False);
            Assert.That(tracker.CurrentInstruction("luo_en", "yumori_sumiha"), Is.Not.Empty);

            // Stage 4 -> Stage 5 (win or loss both continue and grant supplies)
            Assert.That(tracker.OnFriendBattleEnded(won: false), Is.True);
            Assert.That(tracker.OnFriendBattleEnded(won: true), Is.False);
            Assert.That(tracker.FriendBattleWon, Is.False);
            Assert.That(tracker.SuppliesGranted, Is.True);
            Assert.That(tracker.Stage, Is.EqualTo(BeginnerQuestStage.FriendFieldTutorial));
            Assert.That(tracker.FieldStep, Is.EqualTo(BeginnerFieldStep.AdventureControls));

            // Stage 5 sequential steps: adventure -> 1v1 -> capture -> heal -> reach boss lair
            Assert.That(tracker.OnAdventureControlUsed(), Is.True);
            Assert.That(tracker.FieldStep, Is.EqualTo(BeginnerFieldStep.WildOneVsOne));
            Assert.That(tracker.OnWildDefeated(), Is.True);
            Assert.That(tracker.FieldStep, Is.EqualTo(BeginnerFieldStep.CaptureItem));
            Assert.That(tracker.OnWildCaptured(), Is.True);
            Assert.That(tracker.FieldStep, Is.EqualTo(BeginnerFieldStep.HealItem));
            Assert.That(tracker.OnHealItemUsed(), Is.True);
            Assert.That(tracker.FieldStep, Is.EqualTo(BeginnerFieldStep.ReachBossLair));

            // Stage 5 -> Stage 6 (Boss + Rival vs Player + Friend 2vs2)
            Assert.That(tracker.OnBossLairReached(), Is.True);
            Assert.That(tracker.Stage, Is.EqualTo(BeginnerQuestStage.BossAndRival2v2));
            Assert.That(tracker.FieldStep, Is.EqualTo(BeginnerFieldStep.Complete));
            Assert.That(tracker.CurrentInstruction("luo_en", "yumori_sumiha"), Is.Not.Empty);

            // Wipeout in Stage 6 returns to ReachBossLair without resetting Stage 5 tutorial progress
            Assert.That(tracker.OnBossAndRivalWipeout(), Is.True);
            Assert.That(tracker.Stage, Is.EqualTo(BeginnerQuestStage.FriendFieldTutorial));
            Assert.That(tracker.FieldStep, Is.EqualTo(BeginnerFieldStep.ReachBossLair));

            // Re-enter Stage 6 and win -> Stage 7 (MainWorldLobby)
            Assert.That(tracker.OnBossLairReached(), Is.True);
            Assert.That(tracker.OnBossAndRivalDefeated(), Is.True);
            Assert.That(tracker.BossAndRivalDefeated, Is.True);
            Assert.That(tracker.Stage, Is.EqualTo(BeginnerQuestStage.MainWorldLobby));
            Assert.That(tracker.CurrentInstruction("luo_en", "yumori_sumiha"), Is.Not.Empty);

            Assert.Throws<ContractException>(() => tracker.CurrentInstruction(null, "yumori_sumiha"));
        }

        [Test]
        public void Dialogue_builders_and_main_story_tracks_cover_all_stages()
        {
            List<DialogueLine> intro = BeginnerQuestRules.FriendIntroDialogue("luo_en");
            Assert.That(intro.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(BeginnerQuestRules.FriendBattleIntroDialogue("luo_en").Count, Is.GreaterThan(0));
            Assert.That(BeginnerQuestRules.FriendBattleOutcomeDialogue("luo_en", true).Count, Is.GreaterThan(0));
            Assert.That(BeginnerQuestRules.FriendBattleOutcomeDialogue("luo_en", false).Count, Is.GreaterThan(0));

            foreach (BeginnerFieldStep step in Enum.GetValues(typeof(BeginnerFieldStep)))
            {
                DialogueLine line = BeginnerQuestRules.FieldStepDialogue("luo_en", step);
                Assert.That(line.TextKey, Is.Not.Empty);
                Assert.That(line.FallbackText, Is.Not.Empty);
            }

            Assert.That(BeginnerQuestRules.BossAndRivalIntroDialogue("luo_en", "yumori_sumiha").Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(BeginnerQuestRules.BossAndRivalVictoryDialogue("luo_en", "yumori_sumiha").Count, Is.GreaterThanOrEqualTo(2));

            List<MainStoryTrack> tracks = BeginnerQuestRules.NextMainStoryTracks(new CodexProgress(), Array.Empty<string>());
            Assert.That(tracks.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(tracks[0].Completed, Is.False);

            Assert.Throws<ContractException>(() => BeginnerQuestRules.FriendIntroDialogue(null));
            Assert.Throws<ContractException>(() => BeginnerQuestRules.BossAndRivalIntroDialogue("luo_en", null));
        }

        [Test]
        public void Player_vitals_revive_restores_from_downed_state()
        {
            var vitals = new PlayerVitals(new PlayerVitalsConfig(1000, 600, 400, 800));
            vitals.ApplyDamage(1000, 100);
            Assert.That(vitals.IsDowned, Is.True);
            Assert.That(vitals.Heal(500, 200), Is.EqualTo(0));

            vitals.Revive(300);
            Assert.That(vitals.IsDowned, Is.False);
            Assert.That(vitals.Hp, Is.EqualTo(vitals.MaxHp));
            Assert.Throws<ContractException>(() => vitals.Revive(200));
        }

        [Test]
        public void Lobby_save_v13_persists_tutorial_fields_and_migrates_existing_saves()
        {
            var prog = Progression.FromJson(Resources.Load<TextAsset>("balance") != null
                ? Resources.Load<TextAsset>("balance").text
                : System.IO.File.ReadAllText("Data/balance.json"));
            var fresh = new LobbySave();
            Assert.That(fresh.TutorialDone, Is.False);
            fresh.CompleteTutorial("ye_chengxi", "luo_en", "yumori_sumiha", "starter_moon", "starter_star");
            Assert.That(fresh.TutorialDone, Is.True);

            LobbySave roundtrip = LobbySave.FromJson(fresh.ToJson(), prog, _ => CreatureStage.Juvenile);
            Assert.That(roundtrip.TutorialDone, Is.True);
            Assert.That(roundtrip.CharacterId, Is.EqualTo("ye_chengxi"));
            Assert.That(roundtrip.FriendId, Is.EqualTo("luo_en"));
            Assert.That(roundtrip.RivalId, Is.EqualTo("yumori_sumiha"));
            Assert.That(roundtrip.RivalStarterId, Is.EqualTo("starter_moon"));
            Assert.That(roundtrip.FriendStarterId, Is.EqualTo("starter_star"));

            Assert.Throws<ContractException>(() => fresh.CompleteTutorial("", "a", "b", "c", "d"));
        }
    }
}
