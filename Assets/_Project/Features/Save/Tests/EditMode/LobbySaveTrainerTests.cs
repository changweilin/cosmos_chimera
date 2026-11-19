using System.IO;
using Cosmimera.Codex;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Save.Tests
{
    // REQ: docs/design/trainer-battles.md#職業圖鑑 (P4-10, format 15).
    public sealed class LobbySaveTrainerTests
    {
        private static readonly Progression Progression = Progression.FromJson(
            File.ReadAllText(Path.Combine(Application.dataPath, "../Data/balance.json")));

        private static LobbySave Load(string json) =>
            LobbySave.FromJson(json, Progression, _ => CreatureStage.Juvenile);

        [Test]
        public void Format_version_is_15_and_trainer_battles_unlock_codex_tiers()
        {
            Assert.That(LobbySave.FormatVersion, Is.EqualTo(15));
            Assert.That(LobbySave.ProfessionCompleteBattles, Is.EqualTo(10));

            var save = new LobbySave();
            Assert.That(save.TrainerBattleCountOf("trv_hiker"), Is.Zero);
            Assert.That(save.Codex.Get(CodexKind.Character, "trv_hiker"), Is.EqualTo(CodexLevel.Unknown));

            save.AddTrainerBattles("trv_hiker", 1);
            Assert.That(save.TrainerBattleCountOf("trv_hiker"), Is.EqualTo(1));
            Assert.That(save.Codex.Get(CodexKind.Character, "trv_hiker"), Is.EqualTo(CodexLevel.Basic));

            save.AddTrainerBattles("trv_hiker", 8);
            Assert.That(save.TrainerBattleCountOf("trv_hiker"), Is.EqualTo(9));
            Assert.That(save.Codex.Get(CodexKind.Character, "trv_hiker"), Is.EqualTo(CodexLevel.Basic));

            save.AddTrainerBattles("trv_hiker", 1);
            Assert.That(save.TrainerBattleCountOf("trv_hiker"), Is.EqualTo(10));
            Assert.That(save.Codex.Get(CodexKind.Character, "trv_hiker"), Is.EqualTo(CodexLevel.Complete));

            Assert.That(save.RecordTrialCompleted("verdant_trial"), Is.True);
            Assert.That(save.RecordTrialCompleted("verdant_trial"), Is.False);
            Assert.That(save.HasTrialBadge("verdant_trial"), Is.True);
            Assert.That(save.HasTrialBadge("lava_trial"), Is.False);

            string json = save.ToJson();
            LobbySave reloaded = Load(json);
            Assert.That(reloaded.LoadedFormatVersion, Is.EqualTo(15));
            Assert.That(reloaded.TrainerBattleCountOf("trv_hiker"), Is.EqualTo(10));
            Assert.That(reloaded.Codex.Get(CodexKind.Character, "trv_hiker"), Is.EqualTo(CodexLevel.Complete));
            Assert.That(reloaded.HasTrialBadge("verdant_trial"), Is.True);
            CollectionAssert.AreEqual(new[] { "verdant_trial" }, reloaded.CompletedTrialsSnapshot());
        }

        [Test]
        public void Clone_preserves_tutorial_trainer_battles_and_completed_trials()
        {
            var save = new LobbySave();
            save.CompleteTutorial("char_a", "char_b", "char_c", "starter_fire", "starter_wood");
            save.AddTrainerBattles("trv_angler", 3);
            save.AddTrainerBattles("era_future_orbital_ranger", 12);
            save.RecordTrialCompleted("frost_trial");

            LobbySave clone = save.Clone();
            Assert.That(clone.ToJson(), Is.EqualTo(save.ToJson()));
            Assert.That(clone.CharacterId, Is.EqualTo("char_a"));
            Assert.That(clone.FriendId, Is.EqualTo("char_b"));
            Assert.That(clone.RivalId, Is.EqualTo("char_c"));
            Assert.That(clone.RivalStarterId, Is.EqualTo("starter_fire"));
            Assert.That(clone.FriendStarterId, Is.EqualTo("starter_wood"));
            Assert.That(clone.TutorialDone, Is.True);
            Assert.That(clone.TrainerBattleCountOf("trv_angler"), Is.EqualTo(3));
            Assert.That(clone.TrainerBattleCountOf("era_future_orbital_ranger"), Is.EqualTo(12));
            Assert.That(clone.Codex.Get(CodexKind.Character, "trv_angler"), Is.EqualTo(CodexLevel.Basic));
            Assert.That(clone.Codex.Get(CodexKind.Character, "era_future_orbital_ranger"), Is.EqualTo(CodexLevel.Complete));
            Assert.That(clone.HasTrialBadge("frost_trial"), Is.True);

            // Mutating clone leaves original untouched.
            clone.AddTrainerBattles("trv_angler", 1);
            clone.RecordTrialCompleted("lava_trial");
            Assert.That(save.TrainerBattleCountOf("trv_angler"), Is.EqualTo(3));
            Assert.That(save.HasTrialBadge("lava_trial"), Is.False);
        }

        [Test]
        public void Format_14_migrates_to_v15_with_empty_trainer_counts_and_trials()
        {
            LobbySave v14 = Load("{\"version\":14,\"next_instance_id\":1,\"codex\":[]}");
            Assert.That(v14.LoadedFormatVersion, Is.EqualTo(14));
            Assert.That(v14.TrainerBattlesSnapshot(), Is.Empty);
            Assert.That(v14.CompletedTrialsSnapshot(), Is.Empty);
            Assert.That(v14.ToJson(), Does.Contain("\"version\": 15"));
        }

        [TestCase("\"trainer_battles\":[{\"id\":\"BadId\",\"count\":1}]")]
        [TestCase("\"trainer_battles\":[{\"id\":\"trv_hiker\",\"count\":0}]")]
        [TestCase("\"trainer_battles\":[{\"id\":\"trv_hiker\",\"count\":1},{\"id\":\"trv_hiker\",\"count\":2}]")]
        [TestCase("\"completed_trials\":[\"BadTrial\"]")]
        [TestCase("\"completed_trials\":[\"lava_trial\",\"lava_trial\"]")]
        public void Corrupt_v15_trainer_or_trial_fields_are_refused(string fields)
        {
            Assert.Throws<InvalidDataException>(() =>
                Load("{\"version\":15,\"next_instance_id\":1,\"codex\":[]," + fields + "}"));
        }

        [Test]
        public void AddTrainerBattles_and_RecordTrialCompleted_enforce_contracts()
        {
            var save = new LobbySave();
            Assert.Throws<ContractException>(() => save.AddTrainerBattles("InvalidId", 1));
            Assert.Throws<ContractException>(() => save.AddTrainerBattles("trv_hiker", 0));
            Assert.Throws<ContractException>(() => save.RecordTrialCompleted("Bad-Id"));
        }
    }
}
