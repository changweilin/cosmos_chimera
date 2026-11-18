using System.IO;
using Cosmimera.Codex;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.Save.Tests
{
    public sealed class CodexSaveTests
    {
        private static LobbySave Parse(string json)
        {
            var progression = Progression.FromJson(File.ReadAllText(Path.Combine(Application.dataPath, "../Data/balance.json")));
            return LobbySave.FromJson(json, progression, _ => CreatureStage.Juvenile);
        }

        [Test]
        public void Characters_and_creatures_start_unknown_until_unlocked()
        {
            var save = new LobbySave();
            Assert.That(save.Codex.Get(CodexKind.Character, "chr_yeh_chengxi"), Is.EqualTo(CodexLevel.Unknown));
            Assert.That(save.Codex.Get(CodexKind.Character, "chr_roen"), Is.EqualTo(CodexLevel.Unknown));
            Assert.That(save.Codex.Get(CodexKind.Character, "chr_amemori_sumiha"), Is.EqualTo(CodexLevel.Unknown));
            Assert.That(save.Codex.Get(CodexKind.Creature, "cr_aqua_slime_juvenile"), Is.EqualTo(CodexLevel.Unknown));
        }

        [Test]
        public void Knowledge_round_trips_and_clone_does_not_share_mutable_progress()
        {
            var save = new LobbySave();
            save.Codex.Record(CodexEvent.Encounter, "cr_aqua_slime_juvenile");
            save.Codex.Record(CodexEvent.SkillSeen, "retired_move");
            save.Codex.Record(CodexEvent.CharacterQuest, "chr_amemori_sumiha");
            var loaded = Parse(save.ToJson());
            Assert.That(loaded.Codex.Get(CodexKind.Creature, "cr_aqua_slime_juvenile"), Is.EqualTo(CodexLevel.Basic));
            Assert.That(loaded.Codex.Get(CodexKind.Skill, "retired_move"), Is.EqualTo(CodexLevel.Basic));
            Assert.That(loaded.Codex.Get(CodexKind.Character, "chr_amemori_sumiha"), Is.EqualTo(CodexLevel.Complete));
            var clone = loaded.Clone();
            clone.Codex.Record(CodexEvent.Capture, "cr_aqua_slime_juvenile");
            Assert.That(loaded.Codex.Get(CodexKind.Creature, "cr_aqua_slime_juvenile"), Is.EqualTo(CodexLevel.Basic));
        }

        [Test]
        public void Legacy_saves_recover_owned_forms_without_reconstructing_unseen_encounters()
        {
            var save = new LobbySave();
            save.Add("cr_aqua_slime_juvenile", VesselKind.GlassBead, 0);
            save.Codex.Record(CodexEvent.Encounter, "never_captured");
            // Tie the downgrade to the live format version: a hard-coded "10" silently stopped matching at v11.
            string json = save.ToJson();
            string current = "\"version\": " + LobbySave.FormatVersion;
            Assert.That(json, Does.Contain(current), "precondition: the save must be rewritten as a pre-codex (v9) file");
            var loaded = Parse(json.Replace(current, "\"version\": 9"));
            Assert.That(loaded.Codex.Get(CodexKind.Creature, "cr_aqua_slime_juvenile"), Is.EqualTo(CodexLevel.Complete));
            Assert.That(loaded.Codex.Get(CodexKind.Creature, "never_captured"), Is.EqualTo(CodexLevel.Unknown));
        }

        [Test]
        public void Undefined_saved_category_fails_instead_of_silently_losing_knowledge()
        {
            var save = new LobbySave();
            save.Codex.Record(CodexEvent.Dialogue, "chr_yeh_chengxi");
            string json = save.ToJson().Replace("\"kind\": 1", "\"kind\": 99");
            Assert.Throws<InvalidDataException>(() => Parse(json));
        }
    }
}
