using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Codex;
using Cosmimera.Crafting;
using Cosmimera.GameData;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Cosmimera.Bootstrap.Demo
{
    internal sealed class EncyclopediaCatalog
    {
        private readonly Dictionary<string, JObject> _byGameId = new Dictionary<string, JObject>(StringComparer.Ordinal);
        internal readonly List<JObject> Entries = new List<JObject>();
        internal readonly JObject Values;

        internal EncyclopediaCatalog()
        {
            var table = Resources.Load<EncyclopediaTable>("Encyclopedia/Table");
            if (table == null) throw new InvalidDataException("Import the encyclopedia before running the demo");
            foreach (JObject entry in JArray.Parse(table.EntriesJson))
            {
                Entries.Add(entry);
                string id = (string)entry["game_id"];
                if (!string.IsNullOrEmpty(id)) _byGameId.Add(Key(Kind(entry), id), entry);
            }
            Values = JObject.Parse(table.ValuesJson);
        }

        internal static CodexKind Kind(JObject entry) => (string)entry["kind"] == "creature" ? CodexKind.Creature
            : (string)entry["kind"] == "character" ? CodexKind.Character : CodexKind.Skill;

        internal static string Subject(JObject entry) => (string)entry["game_id"] ?? (string)entry["subject_id"] + ":" + (string)entry["stage"];
        private static string Key(CodexKind kind, string id) => ((int)kind) + ":" + id;
        internal JObject Find(CodexKind kind, string id) => _byGameId.TryGetValue(Key(kind, id), out JObject entry) ? entry : null;

        internal static void RecordOwned(CodexProgress progress, CreatureDef creature)
        {
            progress.Record(CodexEvent.Capture, creature.Id);
            progress.Record(CodexEvent.SkillAcquired, creature.SignatureMove.Id);
            progress.Record(CodexEvent.SkillAcquired, creature.Trait.Id);
            if (creature.FieldUtility != FieldUtility.None)
                progress.Record(CodexEvent.SkillAcquired, CraftCatalog.UtilityId(creature.FieldUtility));
        }
    }
}
