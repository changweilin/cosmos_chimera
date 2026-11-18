using System;
using System.Collections.Generic;
using Cosmimera.Capture;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>Stages 0..7 of the P7-02 beginner quest (docs/design/beginner-quest.md).</summary>
    public enum BeginnerQuestStage
    {
        PlanetGen = 0,
        CharacterSelect = 1,
        FriendChat = 2,
        RivalAndStarter = 3,
        FirstBattle = 4,
        FieldAdventure = 5,
        BossBattle = 6,
        Complete = 7,
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

        public string Speaker { get; }
        public string TextKey { get; }
        public string FallbackText { get; }
    }

    /// <summary>Pure rules for character ring assignment, starter counter-picking, and stage progression (P7-02).</summary>
    public static class BeginnerQuestRules
    {
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
            "ye_chengxi" => VesselKind.GlassOrb,
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
        /// Post: rival counters player; friend takes the remaining line; all three distinct.
        /// Side effects: none.
        /// </remarks>
        public static (string rivalLine, string friendLine) AssignStarterLines(string playerStarterLine) => playerStarterLine switch
        {
            "starter_sun" => ("starter_moon", "starter_star"),
            "starter_moon" => ("starter_star", "starter_sun"),
            "starter_star" => ("starter_sun", "starter_moon"),
            _ => throw new ArgumentException("unknown starter line: " + playerStarterLine, nameof(playerStarterLine)),
        };

        /// <summary>Builds the placeholder dialogue lines for stage 2 (friend chat).</summary>
        /// <remarks>Pre: friendId non-null. Post: non-empty list. Side effects: allocates list.</remarks>
        public static List<DialogueLine> FriendIntroDialogue(string friendId)
        {
            string name = CharacterDisplayName(friendId);
            return new List<DialogueLine>
            {
                new DialogueLine(name, "ui.bq.friend_intro_1",
                    "歡迎來到搖籃星！這顆小行星是我們出發前最後的試煉場。"),
                new DialogueLine(name, "ui.bq.friend_intro_2",
                    "星海裡到處都是未知的宙獸與奇觀，先選好你的第一隻夥伴吧！"),
            };
        }

        /// <summary>Builds the placeholder dialogue lines for stage 4 outcome (win or loss both continue).</summary>
        /// <remarks>Pre: rivalId non-null. Post: non-empty list. Side effects: allocates list.</remarks>
        public static List<DialogueLine> FirstBattleOutcomeDialogue(string rivalId, bool won)
        {
            string name = CharacterDisplayName(rivalId);
            return new List<DialogueLine>
            {
                new DialogueLine(name, won ? "ui.bq.rival_lose" : "ui.bq.rival_win",
                    won ? "哼……算你厲害，下次在星海見面我可不會輸！"
                        : "這就是實戰的差距！好好跟你的宙獸磨合吧，我在前面等你。"),
            };
        }
    }
}
