using System;
using System.Collections.Generic;
using Cosmimera.Codex;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Stages 0..8 of the P7-02 beginner quest (docs/design/beginner-quest.md).</summary>
    public enum BeginnerQuestStage
    {
        PlanetGen = 0,
        CharacterSelect = 1,
        FriendWorldIntro = 2,
        StarterSelect = 3,
        FriendBattleTutorial = 4,
        FriendFieldTutorial = 5,
        BossAndRival2v2 = 6,
        MainWorldLobby = 7,
        FreeChoice = 8,
    }

    /// <summary>Sequential tutorial steps inside Stage 5 (FriendFieldTutorial).</summary>
    public enum BeginnerFieldStep
    {
        /// <summary>Move, roll, explore, or interact to start combat.</summary>
        AdventureControls = 0,
        /// <summary>Defeat one wild creature in a 1vs1 field battle.</summary>
        WildOneVsOne = 1,
        /// <summary>Throw a capture vessel to capture one wild creature.</summary>
        CaptureItem = 2,
        /// <summary>Use a healing item to recover a creature.</summary>
        HealItem = 3,
        /// <summary>Travel to the planet BOSS lair.</summary>
        ReachBossLair = 4,
        /// <summary>Field tutorial completed; ready for Stage 6 (2vs2).</summary>
        Complete = 5,
    }

    /// <summary>One dialogue line in the P7-02d minimal dialogue box.</summary>
    public readonly struct DialogueLine
    {
        /// <summary>Creates a dialogue line with localization key and fallback text.</summary>
        /// <remarks>Pre: speaker and textKey non-null. Post: fields assigned. Side effects: none.</remarks>
        public DialogueLine(string speaker, string textKey, string fallbackText)
        {
            Contract.Requires(speaker != null && textKey != null, "speaker and textKey are required");
            Speaker = speaker;
            TextKey = textKey;
            FallbackText = fallbackText ?? string.Empty;
        }

        /// <summary>Display name of the speaker.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public string Speaker { get; }

        /// <summary>Localization key for this line.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public string TextKey { get; }

        /// <summary>Fallback text when localization key is not yet populated.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public string FallbackText { get; }
    }

    /// <summary>A selectable main-story track offered in the Main World (Lobby) after the beginner quest (Stage 8).</summary>
    public readonly struct MainStoryTrack
    {
        /// <summary>Creates a main-story track entry.</summary>
        /// <remarks>Pre: id, title, and summary non-empty. Post: fields assigned. Side effects: none.</remarks>
        public MainStoryTrack(string id, string title, string summary, string preferredPlanetTypeId, bool completed)
        {
            Contract.Requires(!string.IsNullOrEmpty(id), "id is required");
            Contract.Requires(!string.IsNullOrEmpty(title), "title is required");
            Contract.Requires(!string.IsNullOrEmpty(summary), "summary is required");
            Id = id;
            Title = title;
            Summary = summary;
            PreferredPlanetTypeId = preferredPlanetTypeId ?? string.Empty;
            Completed = completed;
        }

        /// <summary>Stable track identifier.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Localized display title.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string Title { get; }

        /// <summary>Short description of the story track objective.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string Summary { get; }

        /// <summary>Suggested planet type id when launching into this story track, or empty for any.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public string PreferredPlanetTypeId { get; }

        /// <summary>Whether the player has already completed this main-story track.</summary>
        /// <remarks>Pre: none. Post: immutable snapshot. Side effects: none.</remarks>
        public bool Completed { get; }
    }

    /// <summary>
    /// Pure runtime state machine for the in-planet stages (4..6) of the P7-02 beginner quest.
    /// REQ: docs/design/beginner-quest.md.
    /// </summary>
    /// <remarks>
    /// Pre: created at the start of a beginner-quest run (Stage 4: FriendBattleTutorial).
    /// Post: advances monotonically through Stages 4 → 5 (5 field steps) → 6 → 7, except wipeout in Stage 6 resets to
    ///       Stage 5 ReachBossLair so the player can retry without losing field-tutorial progress.
    /// Side effects: mutates only its own state fields.
    /// </remarks>
    public sealed class BeginnerQuestTracker
    {
        /// <summary>Creates a tracker starting at Stage 4 (FriendBattleTutorial).</summary>
        /// <remarks>Pre: none. Post: Stage == FriendBattleTutorial, FieldStep == AdventureControls. Side effects: none.</remarks>
        public BeginnerQuestTracker()
        {
            Stage = BeginnerQuestStage.FriendBattleTutorial;
            FieldStep = BeginnerFieldStep.AdventureControls;
        }

        /// <summary>Current beginner quest stage (4..7 during a run).</summary>
        /// <remarks>Pre: none. Post: valid BeginnerQuestStage. Side effects: none.</remarks>
        public BeginnerQuestStage Stage { get; private set; }

        /// <summary>Current field tutorial sub-step while in Stage 5.</summary>
        /// <remarks>Pre: none. Post: valid BeginnerFieldStep. Side effects: none.</remarks>
        public BeginnerFieldStep FieldStep { get; private set; }

        /// <summary>Outcome of the Stage 4 friend 1vs1 battle, or null while still in progress.</summary>
        /// <remarks>Pre: none. Post: non-null once Stage >= FriendFieldTutorial. Side effects: none.</remarks>
        public bool? FriendBattleWon { get; private set; }

        /// <summary>Whether the Stage 5 starter supplies (5 vessels + healing items) have been granted.</summary>
        /// <remarks>Pre: none. Post: true after OnFriendBattleEnded. Side effects: none.</remarks>
        public bool SuppliesGranted { get; private set; }

        /// <summary>Whether the Stage 6 Boss + Rival 2vs2 battle has been won.</summary>
        /// <remarks>Pre: none. Post: true iff Stage == MainWorldLobby. Side effects: none.</remarks>
        public bool BossAndRivalDefeated { get; private set; }

        /// <summary>Records the end of the Stage 4 friend 1vs1 battle (win or loss both continue).</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: if Stage was FriendBattleTutorial, transitions to FriendFieldTutorial, grants supplies, returns true;
        ///       otherwise no-op and returns false.
        /// Side effects: mutates Stage, FieldStep, FriendBattleWon, SuppliesGranted.
        /// </remarks>
        public bool OnFriendBattleEnded(bool won)
        {
            if (Stage != BeginnerQuestStage.FriendBattleTutorial)
            {
                return false;
            }

            FriendBattleWon = won;
            SuppliesGranted = true;
            Stage = BeginnerQuestStage.FriendFieldTutorial;
            FieldStep = BeginnerFieldStep.AdventureControls;
            return true;
        }

        /// <summary>Records that the player performed a basic adventure action (roll or engage/interact).</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: advances AdventureControls → WildOneVsOne when active; returns true iff step changed.
        /// Side effects: may mutate FieldStep.
        /// </remarks>
        public bool OnAdventureControlUsed()
        {
            if (Stage != BeginnerQuestStage.FriendFieldTutorial || FieldStep != BeginnerFieldStep.AdventureControls)
            {
                return false;
            }

            FieldStep = BeginnerFieldStep.WildOneVsOne;
            return true;
        }

        /// <summary>Records that the player knocked out a wild creature in Stage 5.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: advances AdventureControls or WildOneVsOne → CaptureItem; returns true iff step changed.
        /// Side effects: may mutate FieldStep.
        /// </remarks>
        public bool OnWildDefeated()
        {
            if (Stage != BeginnerQuestStage.FriendFieldTutorial ||
                (FieldStep != BeginnerFieldStep.AdventureControls && FieldStep != BeginnerFieldStep.WildOneVsOne))
            {
                return false;
            }

            FieldStep = BeginnerFieldStep.CaptureItem;
            return true;
        }

        /// <summary>Records that the player captured a wild creature using a capture vessel in Stage 5.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: advances any step up to CaptureItem → HealItem; returns true iff step changed.
        /// Side effects: may mutate FieldStep.
        /// </remarks>
        public bool OnWildCaptured()
        {
            if (Stage != BeginnerQuestStage.FriendFieldTutorial || FieldStep > BeginnerFieldStep.CaptureItem)
            {
                return false;
            }

            FieldStep = BeginnerFieldStep.HealItem;
            return true;
        }

        /// <summary>Records that the player used a healing item/food in Stage 5.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: advances HealItem → ReachBossLair; returns true iff step changed.
        /// Side effects: may mutate FieldStep.
        /// </remarks>
        public bool OnHealItemUsed()
        {
            if (Stage != BeginnerQuestStage.FriendFieldTutorial || FieldStep != BeginnerFieldStep.HealItem)
            {
                return false;
            }

            FieldStep = BeginnerFieldStep.ReachBossLair;
            return true;
        }

        /// <summary>Records that the player reached the BOSS lair to begin the Stage 6 2vs2 battle.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: if in Stage 5, sets FieldStep = Complete and Stage = BossAndRival2v2, returns true; otherwise false.
        /// Side effects: may mutate Stage and FieldStep.
        /// </remarks>
        public bool OnBossLairReached()
        {
            if (Stage != BeginnerQuestStage.FriendFieldTutorial)
            {
                return false;
            }

            FieldStep = BeginnerFieldStep.Complete;
            Stage = BeginnerQuestStage.BossAndRival2v2;
            return true;
        }

        /// <summary>Recovers from a Stage 6 wipeout by returning to ReachBossLair so the 2vs2 can be retried.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: if Stage == BossAndRival2v2, sets Stage = FriendFieldTutorial and FieldStep = ReachBossLair, returns true.
        /// Side effects: may mutate Stage and FieldStep.
        /// </remarks>
        public bool OnBossAndRivalWipeout()
        {
            if (Stage != BeginnerQuestStage.BossAndRival2v2)
            {
                return false;
            }

            Stage = BeginnerQuestStage.FriendFieldTutorial;
            FieldStep = BeginnerFieldStep.ReachBossLair;
            return true;
        }

        /// <summary>Records victory in the Stage 6 Boss + Rival 2vs2 battle.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: if Stage == BossAndRival2v2, sets BossAndRivalDefeated = true and Stage = MainWorldLobby, returns true.
        /// Side effects: may mutate Stage and BossAndRivalDefeated.
        /// </remarks>
        public bool OnBossAndRivalDefeated()
        {
            if (Stage != BeginnerQuestStage.BossAndRival2v2)
            {
                return false;
            }

            BossAndRivalDefeated = true;
            Stage = BeginnerQuestStage.MainWorldLobby;
            return true;
        }

        /// <summary>Returns a concise HUD instruction for the current stage and step.</summary>
        /// <remarks>Pre: friendId and rivalId non-null. Post: non-empty string. Side effects: none.</remarks>
        public string CurrentInstruction(string friendId, string rivalId)
        {
            Contract.Requires(friendId != null && rivalId != null, "friendId and rivalId are required");
            string friend = BeginnerQuestRules.CharacterDisplayName(friendId);
            string rival = BeginnerQuestRules.CharacterDisplayName(rivalId);
            return Stage switch
            {
                BeginnerQuestStage.FriendBattleTutorial =>
                    $"階段 4/8：與好友「{friend}」1vs1 對戰＋對戰教學（移動／出招／鎖定目標／格擋與閃避）",
                BeginnerQuestStage.FriendFieldTutorial => FieldStep switch
                {
                    BeginnerFieldStep.AdventureControls =>
                        $"階段 5/8（1/5）：好友「{friend}」冒險操作教學 — 嘗試翻滾閃避或靠近野生宙獸互動／開戰",
                    BeginnerFieldStep.WildOneVsOne =>
                        "階段 5/8（2/5）：1vs1 野外對戰教學 — 擊倒 1 隻野生宙獸",
                    BeginnerFieldStep.CaptureItem =>
                        "階段 5/8（3/5）：捕捉道具教學 — 對倒地的野生宙獸投擲收服器皿",
                    BeginnerFieldStep.HealItem =>
                        "階段 5/8（4/5）：治療道具教學 — 開啟冒險手冊「道具」使用好友交付的治療道具",
                    BeginnerFieldStep.ReachBossLair =>
                        "階段 5/8（5/5）：前往 BOSS 巢穴，準備迎接首次 2vs2 試煉",
                    _ => "階段 5/8：前往 BOSS 巢穴",
                },
                BeginnerQuestStage.BossAndRival2v2 =>
                    $"階段 6/8：BOSS＋宿敵「{rival}」 vs 玩家＋好友「{friend}」（首次 2vs2 教學）— 擊倒雙方！",
                BeginnerQuestStage.MainWorldLobby =>
                    "階段 7/8：試煉完成！即將返回主世界（地球大廳）",
                _ => "新手任務進行中",
            };
        }
    }

    /// <summary>Pure rules for character ring assignment, starter counter-picking, dialogues, and main-story tracks (P7-02).</summary>
    public static class BeginnerQuestRules
    {
        /// <summary>Number of juvenile basic capture vessels granted by the friend in Stage 5.</summary>
        public const int StarterVesselGrantCount = 5;

        /// <summary>Healing food id granted by the friend for the Stage 5 healing-item tutorial.</summary>
        public const string TutorialHealItemId = "food_hearty_beast_stew";

        /// <summary>Number of healing items granted by the friend in Stage 5.</summary>
        public const int TutorialHealGrantCount = 2;

        public static readonly string[] CharacterRing = { "ye_chengxi", "luo_en", "yumori_sumiha" };

        /// <summary>Returns (friendId, rivalId) in ring order from <paramref name="characterId"/>.</summary>
        /// <remarks>
        /// Pre: characterId is one of CharacterRing.
        /// Post: friendId is next in ring, rivalId is second in ring; all three distinct.
        /// Side effects: none.
        /// </remarks>
        public static (string friendId, string rivalId) AssignCompanions(string characterId)
        {
            int idx = Array.IndexOf(CharacterRing, characterId);
            Contract.Requires(idx >= 0, "unknown beginner character id: " + characterId);
            string friendId = CharacterRing[(idx + 1) % CharacterRing.Length];
            string rivalId = CharacterRing[(idx + 2) % CharacterRing.Length];
            return (friendId, rivalId);
        }

        /// <summary>Returns the character's signature juvenile capture vessel (beginner-quest.md#流程).</summary>
        /// <remarks>Pre: characterId non-null. Post: valid VesselKind. Side effects: none.</remarks>
        public static VesselKind SignatureVessel(string characterId) => characterId switch
        {
            "ye_chengxi" => VesselKind.GlassBead,
            "luo_en" => VesselKind.MagicCube,
            "yumori_sumiha" => VesselKind.TalismanCard,
            _ => VesselKind.TalismanCard,
        };

        /// <summary>Display name for a preset character id.</summary>
        /// <remarks>Pre: characterId non-null. Post: non-empty string. Side effects: none.</remarks>
        public static string CharacterDisplayName(string characterId) => characterId switch
        {
            "ye_chengxi" => "葉澄汐",
            "luo_en" => "洛恩",
            "yumori_sumiha" => "雨森澄羽",
            _ => characterId,
        };

        /// <summary>
        /// Assigns rival and friend starter line ids from the player's choice (Sun &lt; Moon &lt; Star &lt; Sun).
        /// REQ: docs/design/beginner-quest.md#御三家剋制.
        /// </summary>
        /// <remarks>
        /// Pre: playerStarterLine is starter_sun, starter_moon, or starter_star.
        /// Post: rival counters player; friend takes the remaining line (countered by player); all three distinct.
        /// Side effects: none.
        /// </remarks>
        public static (string rivalLine, string friendLine) AssignStarterLines(string playerStarterLine) => playerStarterLine switch
        {
            "starter_sun" => ("starter_moon", "starter_star"),
            "starter_moon" => ("starter_star", "starter_sun"),
            "starter_star" => ("starter_sun", "starter_moon"),
            _ => throw new ArgumentException("unknown starter line: " + playerStarterLine, nameof(playerStarterLine)),
        };

        /// <summary>Builds the placeholder world-view intro dialogue for Stage 2.</summary>
        /// <remarks>Pre: friendId non-null. Post: non-empty list. Side effects: allocates list.</remarks>
        public static List<DialogueLine> FriendIntroDialogue(string friendId)
        {
            Contract.Requires(friendId != null, "friendId is required");
            string name = CharacterDisplayName(friendId);
            return new List<DialogueLine>
            {
                new DialogueLine(name, "ui.bq.friend_intro_1",
                    "歡迎來到搖籃星！在星海深處，每顆星球都孕育著不同屬性的宙獸與地貌奇觀。"),
                new DialogueLine(name, "ui.bq.friend_intro_2",
                    "冒險者會透過收服器皿與宙獸締結羈絆，回到地球觀測站大廳後就能自由探索星海！"),
                new DialogueLine(name, "ui.bq.friend_intro_3",
                    "出發前，先挑選你的第一隻御三家夥伴，我再陪你來場 1vs1 對戰教學吧！"),
            };
        }

        /// <summary>Builds the placeholder dialogue for the start of Stage 4 (Friend 1vs1 + combat tutorial).</summary>
        /// <remarks>Pre: friendId non-null. Post: non-empty list. Side effects: allocates list.</remarks>
        public static List<DialogueLine> FriendBattleIntroDialogue(string friendId)
        {
            Contract.Requires(friendId != null, "friendId is required");
            string name = CharacterDisplayName(friendId);
            return new List<DialogueLine>
            {
                new DialogueLine(name, "ui.bq.friend_battle_start",
                    "來一場 1vs1 對戰教學吧！試著直接操控主控宙獸移動、施放主動技，並用格擋或翻滾躲開攻擊。"),
            };
        }

        /// <summary>Builds the placeholder dialogue for Stage 4 outcome (win or loss both continue).</summary>
        /// <remarks>Pre: friendId non-null. Post: non-empty list. Side effects: allocates list.</remarks>
        public static List<DialogueLine> FriendBattleOutcomeDialogue(string friendId, bool won)
        {
            Contract.Requires(friendId != null, "friendId is required");
            string name = CharacterDisplayName(friendId);
            return new List<DialogueLine>
            {
                new DialogueLine(name, won ? "ui.bq.friend_battle_win" : "ui.bq.friend_battle_lose",
                    won ? "太棒了！你馬上就抓到對戰節奏了！我先幫你的宙獸回復體力。"
                        : "沒關係，實戰本來就要多練習！我先幫你的宙獸回復體力，我們繼續下一步。"),
                new DialogueLine(name, "ui.bq.friend_grant_supplies",
                    $"這 5 個收服器皿和 {TutorialHealGrantCount} 份治療道具給你！接下來我帶你練習冒險操作、野外 1vs1、捕捉與治療。"),
            };
        }

        /// <summary>Builds the placeholder guidance line when entering a Stage 5 field tutorial step.</summary>
        /// <remarks>Pre: friendId non-null. Post: non-empty DialogueLine. Side effects: none.</remarks>
        public static DialogueLine FieldStepDialogue(string friendId, BeginnerFieldStep step)
        {
            Contract.Requires(friendId != null, "friendId is required");
            string name = CharacterDisplayName(friendId);
            return step switch
            {
                BeginnerFieldStep.AdventureControls => new DialogueLine(name, "ui.bq.step_adventure",
                    "先熟悉冒險操作：試著翻滾閃避，或靠近野外宙獸按互動鍵觀察／挑釁開戰！"),
                BeginnerFieldStep.WildOneVsOne => new DialogueLine(name, "ui.bq.step_wild_1v1",
                    "很好！接著找一隻野外宙獸進行 1vs1 實戰，將牠擊倒吧。"),
                BeginnerFieldStep.CaptureItem => new DialogueLine(name, "ui.bq.step_capture",
                    "野外宙獸倒地了！靠近牠並投擲收服器皿，把牠收為夥伴吧。"),
                BeginnerFieldStep.HealItem => new DialogueLine(name, "ui.bq.step_heal",
                    "收服成功！現在打開冒險手冊的「道具」分頁，試著使用我給你的治療道具回復宙獸。"),
                BeginnerFieldStep.ReachBossLair => new DialogueLine(name, "ui.bq.step_boss_lair",
                    "所有基礎操作都完成了！朝著搖籃星的 BOSS 巢穴前進吧，我會在那裡與你會合！"),
                _ => new DialogueLine(name, "ui.bq.step_done", "準備好迎接最終試煉了！"),
            };
        }

        /// <summary>Builds the placeholder dialogue for Stage 6 start (Boss + Rival vs Player + Friend 2vs2 tutorial).</summary>
        /// <remarks>Pre: friendId and rivalId non-null. Post: non-empty list. Side effects: allocates list.</remarks>
        public static List<DialogueLine> BossAndRivalIntroDialogue(string friendId, string rivalId)
        {
            Contract.Requires(friendId != null && rivalId != null, "friendId and rivalId are required");
            string friend = CharacterDisplayName(friendId);
            string rival = CharacterDisplayName(rivalId);
            return new List<DialogueLine>
            {
                new DialogueLine(rival, "ui.bq.rival_boss_intro",
                    "慢了一步呢！這顆搖籃星的幼年 BOSS 就由我先聯手考驗你們的實力吧！"),
                new DialogueLine(friend, "ui.bq.friend_2v2_intro",
                    $"我來當你的支援！這是首次 2vs2 教學戰——你操作主控宙獸，我的夥伴會自動協同作戰，善用鎖定與支援模式一起擊倒 BOSS 和{rival}吧！"),
            };
        }

        /// <summary>Builds the placeholder dialogue for Stage 6 victory (entering Main World Lobby).</summary>
        /// <remarks>Pre: friendId and rivalId non-null. Post: non-empty list. Side effects: allocates list.</remarks>
        public static List<DialogueLine> BossAndRivalVictoryDialogue(string friendId, string rivalId)
        {
            Contract.Requires(friendId != null && rivalId != null, "friendId and rivalId are required");
            string friend = CharacterDisplayName(friendId);
            string rival = CharacterDisplayName(rivalId);
            return new List<DialogueLine>
            {
                new DialogueLine(rival, "ui.bq.rival_boss_defeated",
                    "哼……2vs2 配合得不錯嘛。到了真正的星海，我可不會再手下留情！"),
                new DialogueLine(friend, "ui.bq.friend_complete",
                    "太好了！我們收服了幼年 BOSS，現在一起前往地球觀測站（主世界大廳）吧！"),
            };
        }

        /// <summary>Builds the Stage 8 main-story track options offered in the Main World (Lobby).</summary>
        /// <remarks>
        /// REQ: docs/design/beginner-quest.md (Stage 8), encyclopedia.md#玩家解鎖, trainer-battles.md#地形試煉.
        /// Pre: completedTrials may be null.
        /// Post: returns non-empty list of main-story tracks with completion status.
        /// Side effects: allocates list.
        /// </remarks>
        public static List<MainStoryTrack> NextMainStoryTracks(CodexProgress codex, IReadOnlyCollection<string> completedTrials)
        {
            bool hasTrial = completedTrials != null && completedTrials.Count > 0;
            bool allTenTrials = completedTrials != null && completedTrials.Count >= 10;
            return new List<MainStoryTrack>
            {
                new MainStoryTrack(
                    "story_terrain_trials",
                    allTenTrials ? "主線：十屬性地形試煉（已完成）" : $"主線：進階地形試煉（{completedTrials?.Count ?? 0}/10）",
                    "前往星球的進階地形入口與試煉主對話，挑戰「試煉主＋地城 BOSS」並取得地形免疫與進階器皿配方。",
                    "verdant_planet",
                    allTenTrials),
                new MainStoryTrack(
                    "story_food_line",
                    "主線：食物製作路線（釣魚 → 冷凍 → 烹飪）",
                    "探索水域星球，體驗採集、加工與清蒸料理，解鎖十屬性生活技能主線。",
                    "verdant_planet",
                    false),
                new MainStoryTrack(
                    "story_weapon_line",
                    "主線：武器製作路線（採礦 → 電弧熔煉 → 鍛造）",
                    "前往礦脈豐富的星球採集原礦與煤，鍛造第一把無屬性武器。",
                    "mineral_planet",
                    false),
                new MainStoryTrack(
                    "story_armor_line",
                    "主線：防具製作路線（採集纖維 → 紡織 → 裁縫）",
                    "採集麻纖維與棉花加工成布料，製作無屬性上身戰衣或下身護腿。",
                    "verdant_planet",
                    false),
                new MainStoryTrack(
                    "story_flight_line",
                    hasTrial ? "主線：飛行技能任務（風屬性高空機動）" : "主線：飛行技能任務（推薦先完成首座試煉）",
                    "結識風屬性生活技能導師，解鎖高空滑翔與跨地形飛行能力。",
                    "storm_planet",
                    false),
            };
        }
    }
}
