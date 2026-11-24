using System;
using System.Collections.Generic;
using Cosmimera.Capture;
using Cosmimera.Codex;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;

namespace Cosmimera.Save
{
    /// <summary>Descriptor for a built-in developer test save preset at a key story milestone.</summary>
    /// <remarks>
    /// REQ: docs/design/lobby-meta.md#進度保存.
    /// Pre: constructed with non-empty fields and builder delegate.
    /// Post: immutable descriptor. Side effects: none.
    /// </remarks>
    public sealed class DevTestSavePreset
    {
        /// <summary>Stable preset identifier.</summary>
        /// <remarks>Pre: none. Post: non-empty string. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Display label in developer menu.</summary>
        /// <remarks>Pre: none. Post: non-empty string. Side effects: none.</remarks>
        public string Label { get; }

        /// <summary>JSON file name under Data/TestSaves/.</summary>
        /// <remarks>Pre: none. Post: non-empty .json file name. Side effects: none.</remarks>
        public string FileName { get; }

        private readonly Func<LobbySave> _builder;

        /// <summary>Creates a developer test save preset.</summary>
        /// <remarks>
        /// Pre: id, label, fileName non-whitespace; builder != null.
        /// Post: properties initialized. Side effects: none.
        /// </remarks>
        public DevTestSavePreset(string id, string label, string fileName, Func<LobbySave> builder)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(id), "id must be non-empty");
            Contract.Requires(!string.IsNullOrWhiteSpace(label), "label must be non-empty");
            Contract.Requires(!string.IsNullOrWhiteSpace(fileName), "fileName must be non-empty");
            Contract.Requires(builder != null, "builder must be non-null");
            Id = id;
            Label = label;
            FileName = fileName;
            _builder = builder;
        }

        /// <summary>Builds a fresh <see cref="LobbySave"/> instance for this milestone.</summary>
        /// <remarks>Pre: none. Post: valid LobbySave at FormatVersion. Side effects: allocates LobbySave.</remarks>
        public LobbySave Build() => _builder();
    }

    /// <summary>
    /// Registry of developer test saves covering every key story milestone.
    /// </summary>
    /// <remarks>
    /// REQ: docs/design/lobby-meta.md#進度保存.
    /// Pre: none. Post: All contains all 6 milestone presets in story order. Side effects: none.
    /// </remarks>
    public static class DevTestSaves
    {
        private const string PlayerCharId = "ye_chengxi";
        private const string FriendCharId = "luo_en";
        private const string RivalCharId = "yumori_sumiha";
        private const string StarterLineId = "starter_sun_line";
        private const string FriendStarterId = "star_blaze_foal";
        private const string RivalStarterId = "moon_night_pup";
        private const ulong CradleSeed = 0x435241444C453031UL;
        private const ulong LobbySeed = 0x4C4F424259303031UL;

        private static readonly string[] LowerFiveTrials =
        {
            "laboratory", "rainforest", "deep_sea", "volcano", "cave",
        };

        private static readonly string[] AllTenTrials =
        {
            "laboratory", "rainforest", "deep_sea", "volcano", "cave",
            "ice_cap", "tower", "space", "labyrinth", "temple",
        };

        /// <summary>All built-in developer test save presets in story progression order.</summary>
        /// <remarks>Pre: none. Post: 6 non-null presets. Side effects: none.</remarks>
        public static IReadOnlyList<DevTestSavePreset> All { get; } = new[]
        {
            new DevTestSavePreset(
                "cradle_quest_unlocked",
                "1. 搖籃星：新手任務解鎖（可自由探索／BOSS 戰前）",
                "01_cradle_quest_unlocked.json",
                BuildCradleQuestUnlocked),
            new DevTestSavePreset(
                "cradle_tutorial_done",
                "2. 搖籃星：新手任務完成（母星 1 號小鎮／小行星解鎖）",
                "02_cradle_tutorial_done.json",
                BuildCradleTutorialDone),
            new DevTestSavePreset(
                "after_food_quest",
                "3. 完成食物任務後（2 號部落／矮行星解鎖）",
                "03_after_food_quest.json",
                BuildAfterFoodQuest),
            new DevTestSavePreset(
                "before_migratory_return",
                "4. 取得候鳥歸途前（5 關主完成／4 號小鎮前）",
                "04_before_migratory_return.json",
                BuildBeforeMigratoryReturn),
            new DevTestSavePreset(
                "before_final_battle",
                "5. 終戰前（10 關主完成／8 號部落宿敵終戰前）",
                "05_before_final_battle.json",
                BuildBeforeFinalBattle),
            new DevTestSavePreset(
                "ng_plus_unlocked",
                "6. 解鎖二周目（一週目完成／超巨星解鎖）",
                "06_ng_plus_unlocked.json",
                BuildNgPlusUnlocked),
        };

        private static LobbySave BuildCradleQuestUnlocked()
        {
            var save = new LobbySave();
            save.SetCharacters(PlayerCharId, FriendCharId, RivalCharId);
            save.ChooseStarter(StarterLineId, "sun_mirror_chick", 1000);
            save.Add("cr_aqua_slime_juvenile", VesselKind.GlassBead, 1000);
            save.SetCheckpoint(new RunCheckpointSave(
                SaveWorldKind.CradlePlanet,
                SaveCheckpointKind.OverworldAuto,
                CradleSeed,
                "cradle_planet",
                PlanetSize.Asteroid,
                CreatureStage.Juvenile,
                0f, 1f, 0f,
                beginnerQuestPhase: 5,
                discoveredSettlements: new[] { 0 }));
            return save;
        }

        private static LobbySave BuildCradleTutorialDone()
        {
            var save = new LobbySave();
            save.ChooseStarter(StarterLineId, "sun_mirror_chick", 1200);
            save.Add("cr_aqua_slime_juvenile", VesselKind.GlassBead, 1100);
            save.SetRawPlayerExp(1200);
            save.CompleteTutorial(PlayerCharId, FriendCharId, RivalCharId, RivalStarterId, FriendStarterId);
            save.Codex.Record(CodexEvent.Capture, PlayerCharId);
            save.Materials.Add(new MaterialKey(Element.Wood, 1), 5);
            save.Materials.Add(new MaterialKey(Element.Water, 1), 5);
            save.SetCheckpoint(new RunCheckpointSave(
                SaveWorldKind.LobbyPlanet,
                SaveCheckpointKind.OverworldAuto,
                LobbySeed,
                "verdant_planet",
                PlanetSize.Supergiant,
                CreatureStage.Adult,
                0f, 1f, 0f,
                discoveredSettlements: new[] { 0 }));
            return save;
        }

        private static LobbySave BuildAfterFoodQuest()
        {
            var save = new LobbySave();
            save.ChooseStarter(StarterLineId, "sun_heliostat_heron", 5000);
            save.Add("cr_aqua_slime_juvenile", VesselKind.GlassBead, 4200);
            save.Add("cr_earth_golem_juvenile", VesselKind.GlassBead, 3800);
            save.SetRawPlayerExp(6000);
            save.CompleteTutorial(PlayerCharId, FriendCharId, RivalCharId, RivalStarterId, FriendStarterId);
            save.CompleteFoodQuest();
            save.RecordRecipeDiscovered("recipe_food_steamed_sea_bass");
            save.Codex.Record(CodexEvent.Capture, PlayerCharId);
            save.Materials.Add(new MaterialKey(Element.Water, 1), 10);
            save.Materials.Add(new MaterialKey(Element.Fire, 1), 10);
            save.SetCheckpoint(new RunCheckpointSave(
                SaveWorldKind.LobbyPlanet,
                SaveCheckpointKind.OverworldAuto,
                LobbySeed,
                "verdant_planet",
                PlanetSize.Supergiant,
                CreatureStage.Adult,
                0f, 1f, 0f,
                discoveredSettlements: new[] { 0, 1 }));
            return save;
        }

        private static LobbySave BuildBeforeMigratoryReturn()
        {
            var save = new LobbySave();
            save.ChooseStarter(StarterLineId, "sun_heliostat_heron", 18000);
            save.Add("cr_fire_wisp_adolescent", VesselKind.GlassBead, 16000);
            save.Add("cr_aqua_slime_adolescent", VesselKind.GlassBead, 15000);
            save.Add("cr_earth_golem_adolescent", VesselKind.TalismanCard, 14500);
            save.SetRawPlayerExp(22000);
            save.CompleteTutorial(PlayerCharId, FriendCharId, RivalCharId, RivalStarterId, FriendStarterId);
            save.CompleteFoodQuest();
            foreach (string trial in LowerFiveTrials)
            {
                save.RecordTrialCompleted(trial);
            }

            save.Codex.Record(CodexEvent.Capture, PlayerCharId);
            save.Materials.Add(new MaterialKey(Element.Wind, 1), 15);
            save.Materials.Add(new MaterialKey(Element.Metal, 1), 10);
            save.SetCheckpoint(new RunCheckpointSave(
                SaveWorldKind.LobbyPlanet,
                SaveCheckpointKind.OverworldAuto,
                LobbySeed,
                "verdant_planet",
                PlanetSize.Supergiant,
                CreatureStage.Adult,
                0f, 1f, 0f,
                discoveredSettlements: new[] { 0, 1, 2 }));
            return save;
        }

        private static LobbySave BuildBeforeFinalBattle()
        {
            var save = new LobbySave();
            save.ChooseStarter(StarterLineId, "sun_array_skyheron", 65000);
            save.Add("cr_frostblade_penguin_adult", VesselKind.GlassBead, 62000);
            save.Add("cr_chrono_beetle_adult", VesselKind.TalismanCard, 60000);
            save.Add("cr_fire_wisp_adult", VesselKind.MagicCube, 58000);
            save.SetRawPlayerExp(70000);
            save.CompleteTutorial(PlayerCharId, FriendCharId, RivalCharId, RivalStarterId, FriendStarterId);
            save.CompleteFoodQuest();
            save.CompleteMigratoryReturn();
            foreach (string trial in AllTenTrials)
            {
                save.RecordTrialCompleted(trial);
            }

            save.Codex.Record(CodexEvent.Capture, PlayerCharId);
            save.Codex.Record(CodexEvent.Capture, FriendCharId);
            save.Materials.Add(new MaterialKey(Element.Light, 1), 20);
            save.Materials.Add(new MaterialKey(Element.Shadow, 1), 20);
            save.SetCheckpoint(new RunCheckpointSave(
                SaveWorldKind.LobbyPlanet,
                SaveCheckpointKind.OverworldAuto,
                LobbySeed,
                "verdant_planet",
                PlanetSize.Supergiant,
                CreatureStage.Adult,
                0f, 1f, 0f,
                discoveredSettlements: new[] { 0, 1, 2, 3, 4, 5, 6, 7 }));
            return save;
        }

        private static LobbySave BuildNgPlusUnlocked()
        {
            LobbySave save = BuildBeforeFinalBattle();
            save.CompleteRivalQuest();
            save.Codex.Record(CodexEvent.Capture, RivalCharId);
            return save;
        }
    }
}
