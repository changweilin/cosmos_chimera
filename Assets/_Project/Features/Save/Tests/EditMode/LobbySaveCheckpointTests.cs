using System;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Save.Tests
{
    public sealed class LobbySaveCheckpointTests
    {
        private static readonly Progression P = Progression.FromJson(
            File.ReadAllText(Path.Combine(Application.dataPath, "../Data/balance.json")));

        private static LobbySave Load(string json) =>
            LobbySave.FromJson(json, P, _ => CreatureStage.Juvenile);

        [Test]
        public void Overworld_auto_save_is_enabled_on_cradle_and_lobby_only_outside_dungeons()
        {
            Assert.That(SaveCheckpointPolicy.ShouldAutoSaveOnOverworld(SaveWorldKind.CradlePlanet, insideDungeon: false), Is.True);
            Assert.That(SaveCheckpointPolicy.ShouldAutoSaveOnOverworld(SaveWorldKind.LobbyPlanet, insideDungeon: false), Is.True);
            Assert.That(SaveCheckpointPolicy.ShouldAutoSaveOnOverworld(SaveWorldKind.CradlePlanet, insideDungeon: true), Is.False);
            Assert.That(SaveCheckpointPolicy.ShouldAutoSaveOnOverworld(SaveWorldKind.LobbyPlanet, insideDungeon: true), Is.False);
            Assert.That(SaveCheckpointPolicy.ShouldAutoSaveOnOverworld(SaveWorldKind.RandomPlanet, insideDungeon: false), Is.False);
            Assert.That(SaveCheckpointPolicy.ShouldAutoSaveOnOverworld(SaveWorldKind.RandomPlanet, insideDungeon: true), Is.False);
        }

        [Test]
        public void Random_planet_only_allows_village_wonder_and_pre_boss_checkpoints()
        {
            Assert.That(SaveCheckpointPolicy.IsAllowedRandomCheckpoint(SaveWorldKind.RandomPlanet, SaveCheckpointKind.OverworldAuto), Is.False);
            Assert.That(SaveCheckpointPolicy.IsAllowedRandomCheckpoint(SaveWorldKind.RandomPlanet, SaveCheckpointKind.RandomVillage), Is.True);
            Assert.That(SaveCheckpointPolicy.IsAllowedRandomCheckpoint(SaveWorldKind.RandomPlanet, SaveCheckpointKind.RandomWonderPreDungeon), Is.True);
            Assert.That(SaveCheckpointPolicy.IsAllowedRandomCheckpoint(SaveWorldKind.RandomPlanet, SaveCheckpointKind.RandomPreSurfaceBoss), Is.True);
            Assert.That(SaveCheckpointPolicy.IsAllowedRandomCheckpoint(SaveWorldKind.RandomPlanet, SaveCheckpointKind.RandomPreDungeonBoss), Is.True);

            Assert.That(SaveCheckpointPolicy.CanSaveCheckpoint(SaveWorldKind.LobbyPlanet, SaveCheckpointKind.RandomVillage, false), Is.False);
            Assert.That(SaveCheckpointPolicy.CanSaveCheckpoint(SaveWorldKind.CradlePlanet, SaveCheckpointKind.OverworldAuto, true), Is.False);
        }

        [Test]
        public void Policy_and_checkpoint_reject_invalid_arguments()
        {
            Assert.Throws<ContractException>(() =>
                SaveCheckpointPolicy.ShouldAutoSaveOnOverworld((SaveWorldKind)99, false));
            Assert.Throws<ContractException>(() =>
                SaveCheckpointPolicy.IsAllowedRandomCheckpoint(SaveWorldKind.RandomPlanet, (SaveCheckpointKind)99));
            Assert.Throws<ContractException>(() =>
                new RunCheckpointSave(SaveWorldKind.RandomPlanet, SaveCheckpointKind.OverworldAuto, 1UL, "verdant_planet",
                    PlanetSize.Asteroid, CreatureStage.Juvenile, 0f, 1f, 0f));
            Assert.Throws<ContractException>(() =>
                new RunCheckpointSave(SaveWorldKind.LobbyPlanet, SaveCheckpointKind.OverworldAuto, 1UL, "",
                    PlanetSize.Supergiant, CreatureStage.Adult, 0f, 1f, 0f));
        }

        [Test]
        public void Format_17_round_trips_story_milestones_and_active_checkpoint()
        {
            Assert.That(LobbySave.FormatVersion, Is.GreaterThanOrEqualTo(17));
            var save = new LobbySave();
            save.CompleteTutorial("ye_chengxi", "luo_en", "yumori_sumiha", "moon_night_pup", "star_blaze_foal");
            save.CompleteFoodQuest();
            save.CompleteMigratoryReturn();
            save.CompleteRivalQuest();
            save.SetCheckpoint(new RunCheckpointSave(
                SaveWorldKind.RandomPlanet,
                SaveCheckpointKind.RandomPreDungeonBoss,
                0x123456789ABCDEF0UL,
                "lava_planet",
                PlanetSize.Supergiant,
                CreatureStage.Overload,
                0.25f, 0.5f, 0.8f,
                beginnerQuestPhase: 0,
                discoveredSettlements: new[] { 2, 0, 2 },
                surfaceBossDefeated: true,
                dungeonBossDefeated: false));

            LobbySave reloaded = Load(save.ToJson());
            Assert.That(reloaded.FoodQuestDone, Is.True);
            Assert.That(reloaded.MigratoryReturnDone, Is.True);
            Assert.That(reloaded.RivalQuestDone, Is.True);
            Assert.That(reloaded.NgPlusUnlocked, Is.True);
            Assert.That(reloaded.ActiveCheckpoint, Is.Not.Null);
            Assert.That(reloaded.ActiveCheckpoint.WorldKind, Is.EqualTo(SaveWorldKind.RandomPlanet));
            Assert.That(reloaded.ActiveCheckpoint.CheckpointKind, Is.EqualTo(SaveCheckpointKind.RandomPreDungeonBoss));
            Assert.That(reloaded.ActiveCheckpoint.PlanetSeed, Is.EqualTo(0x123456789ABCDEF0UL));
            Assert.That(reloaded.ActiveCheckpoint.PlanetTypeId, Is.EqualTo("lava_planet"));
            Assert.That(reloaded.ActiveCheckpoint.PlanetSize, Is.EqualTo(PlanetSize.Supergiant));
            Assert.That(reloaded.ActiveCheckpoint.BossStage, Is.EqualTo(CreatureStage.Overload));
            Assert.That(reloaded.ActiveCheckpoint.SurfaceBossDefeated, Is.True);
            Assert.That(reloaded.ActiveCheckpoint.DungeonBossDefeated, Is.False);
            CollectionAssert.AreEqual(new[] { 0, 2 }, reloaded.ActiveCheckpoint.DiscoveredSettlements);

            LobbySave clone = reloaded.Clone();
            clone.ClearCheckpoint();
            Assert.That(clone.ActiveCheckpoint, Is.Null);
            Assert.That(reloaded.ActiveCheckpoint, Is.Not.Null);
        }

        [Test]
        public void Format_16_migrates_with_false_story_flags_and_null_checkpoint()
        {
            string v16 = new LobbySave().ToJson().Replace("\"version\": " + LobbySave.FormatVersion, "\"version\": 16");
            LobbySave migrated = Load(v16);
            Assert.That(migrated.LoadedFormatVersion, Is.EqualTo(16));
            Assert.That(migrated.FoodQuestDone, Is.False);
            Assert.That(migrated.MigratoryReturnDone, Is.False);
            Assert.That(migrated.RivalQuestDone, Is.False);
            Assert.That(migrated.NgPlusUnlocked, Is.False);
            Assert.That(migrated.ActiveCheckpoint, Is.Null);
        }

        [Test]
        public void Invalid_checkpoint_payload_is_rejected()
        {
            string bad = "{\"version\":" + LobbySave.FormatVersion +
                         ",\"next_instance_id\":1,\"has_checkpoint\":true}";
            Assert.Throws<InvalidDataException>(() => Load(bad));
        }

        [Test]
        public void All_dev_test_saves_build_and_round_trip_cleanly()
        {
            Assert.That(DevTestSaves.All.Count, Is.EqualTo(6));
            foreach (DevTestSavePreset preset in DevTestSaves.All)
            {
                LobbySave built = preset.Build();
                Assert.That(built, Is.Not.Null, preset.Id);
                string json = built.ToJson();
                LobbySave roundTrip = Load(json);
                Assert.That(roundTrip.ToJson(), Is.EqualTo(json), preset.Id);
            }

            LobbySave cradleUnlocked = DevTestSaves.All[0].Build();
            Assert.That(cradleUnlocked.TutorialDone, Is.False);
            Assert.That(cradleUnlocked.ActiveCheckpoint.WorldKind, Is.EqualTo(SaveWorldKind.CradlePlanet));

            LobbySave cradleDone = DevTestSaves.All[1].Build();
            Assert.That(cradleDone.TutorialDone, Is.True);
            Assert.That(cradleDone.FoodQuestDone, Is.False);

            LobbySave afterFood = DevTestSaves.All[2].Build();
            Assert.That(afterFood.FoodQuestDone, Is.True);
            Assert.That(afterFood.MigratoryReturnDone, Is.False);

            LobbySave beforeMigratory = DevTestSaves.All[3].Build();
            Assert.That(beforeMigratory.CompletedTrialsSnapshot().Count, Is.EqualTo(5));
            Assert.That(beforeMigratory.MigratoryReturnDone, Is.False);

            LobbySave beforeFinal = DevTestSaves.All[4].Build();
            Assert.That(beforeFinal.CompletedTrialsSnapshot().Count, Is.EqualTo(10));
            Assert.That(beforeFinal.MigratoryReturnDone, Is.True);
            Assert.That(beforeFinal.RivalQuestDone, Is.False);

            LobbySave ngPlus = DevTestSaves.All[5].Build();
            Assert.That(ngPlus.RivalQuestDone, Is.True);
            Assert.That(ngPlus.NgPlusUnlocked, Is.True);
        }

        [Test]
        public void Exported_data_test_saves_exist_and_round_trip()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Data", "TestSaves"));
            Assert.That(Directory.Exists(dir), Is.True, "Data/TestSaves directory must exist");
            foreach (DevTestSavePreset preset in DevTestSaves.All)
            {
                string path = Path.Combine(dir, preset.FileName);
                Assert.That(File.Exists(path), Is.True, $"Missing test save: {path}");
                LobbySave loaded = Load(File.ReadAllText(path));
                Assert.That(loaded.ToJson(), Is.EqualTo(preset.Build().ToJson()), preset.FileName);
            }
        }
    }
}
