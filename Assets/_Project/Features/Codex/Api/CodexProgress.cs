using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;

namespace Cosmimera.Codex
{
    /// <summary>Stable encyclopedia subject categories.</summary>
    /// <remarks>Pre: none. Post: values remain stable for saves. Side effects: none.</remarks>
    public enum CodexKind { Creature, Character, Skill }

    /// <summary>Monotonic player knowledge levels, independent of content approval.</summary>
    /// <remarks>Pre: none. Post: ordered from unknown to complete. Side effects: none.</remarks>
    public enum CodexLevel { Unknown, Basic, Complete }

    /// <summary>Gameplay facts that reveal encyclopedia content.</summary>
    /// <remarks>Pre: callers report facts only after successful gameplay actions. Post: stable values. Side effects: none.</remarks>
    public enum CodexEvent { Encounter, Capture, Dialogue, CharacterQuest, SkillSeen, SkillAcquired }

    /// <summary>One immutable saved knowledge record.</summary>
    /// <remarks>Pre: validated by CodexProgress.Restore. Post: immutable. Side effects: none.</remarks>
    public readonly struct CodexRecord
    {
        /// <summary>Creates a record for persistence or merging.</summary>
        /// <remarks>Pre: kind and level defined; id non-empty. Post: arguments preserved. Side effects: none.</remarks>
        public CodexRecord(CodexKind kind, string id, CodexLevel level)
        {
            Contract.Requires(Enum.IsDefined(typeof(CodexKind), kind), "kind must be defined");
            Contract.Requires(!string.IsNullOrWhiteSpace(id), "id is required");
            Contract.Requires(level == CodexLevel.Basic || level == CodexLevel.Complete, "saved level must be known");
            Kind = kind; Id = id; Level = level;
        }

        /// <summary>Category of this subject.</summary>
        /// <remarks>Pre: initialized. Post: defined. Side effects: none.</remarks>
        public CodexKind Kind { get; }
        /// <summary>Permanent gameplay subject ID, including the creature form.</summary>
        /// <remarks>Pre: initialized. Post: non-empty. Side effects: none.</remarks>
        public string Id { get; }
        /// <summary>Highest unlocked level.</summary>
        /// <remarks>Pre: initialized. Post: Basic or Complete. Side effects: none.</remarks>
        public CodexLevel Level { get; }
    }

    /// <summary>Player knowledge; REQ: docs/design/encyclopedia.md#玩家解鎖.</summary>
    /// <remarks>Pre: single-threaded use. Post: progress never regresses under Record or Merge. Side effects: in-memory only.</remarks>
    public sealed class CodexProgress
    {
        private Dictionary<string, CodexRecord> _records = new Dictionary<string, CodexRecord>(StringComparer.Ordinal);

        private static string Key(CodexKind kind, string id) => ((int)kind) + ":" + id;

        /// <summary>Returns the highest knowledge level without inferring ownership or other forms.</summary>
        /// <remarks>Pre: kind defined; id non-empty. Post: Unknown for an unrecorded subject. Side effects: none.</remarks>
        public CodexLevel Get(CodexKind kind, string id)
        {
            Contract.Requires(Enum.IsDefined(typeof(CodexKind), kind), "kind must be defined");
            Contract.Requires(!string.IsNullOrWhiteSpace(id), "id is required");
            return _records.TryGetValue(Key(kind, id), out CodexRecord record) ? record.Level : CodexLevel.Unknown;
        }

        /// <summary>Records an encounter, acquisition, dialogue or quest fact idempotently.</summary>
        /// <remarks>Pre: trigger defined; id non-empty. Post: matching category advances to Basic or Complete; true iff changed. Side effects: updates this progress.</remarks>
        public bool Record(CodexEvent trigger, string id)
        {
            Contract.Requires(Enum.IsDefined(typeof(CodexEvent), trigger), "trigger must be defined");
            Contract.Requires(!string.IsNullOrWhiteSpace(id), "id is required");
            CodexKind kind = trigger == CodexEvent.Encounter || trigger == CodexEvent.Capture ? CodexKind.Creature
                : trigger == CodexEvent.Dialogue || trigger == CodexEvent.CharacterQuest ? CodexKind.Character : CodexKind.Skill;
            CodexLevel level = trigger == CodexEvent.Capture || trigger == CodexEvent.CharacterQuest || trigger == CodexEvent.SkillAcquired
                ? CodexLevel.Complete : CodexLevel.Basic;
            if (Get(kind, id) >= level) return false;
            _records[Key(kind, id)] = new CodexRecord(kind, id, level);
            return true;
        }

        /// <summary>Returns a deterministic independent snapshot.</summary>
        /// <remarks>Pre: none. Post: sorted by kind then ordinal ID; excludes unknowns. Side effects: allocates.</remarks>
        public IReadOnlyList<CodexRecord> Snapshot()
        {
            var result = new List<CodexRecord>(_records.Values);
            result.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : string.CompareOrdinal(a.Id, b.Id));
            return result;
        }

        /// <summary>Merges another progress without losing any previously known subject.</summary>
        /// <remarks>Pre: other non-null. Post: per-subject maximum of both. Side effects: updates this progress.</remarks>
        public void Merge(CodexProgress other)
        {
            Contract.Requires(other != null, "other is required");
            if (ReferenceEquals(this, other)) return;
            foreach (CodexRecord record in other._records.Values)
                if (Get(record.Kind, record.Id) < record.Level) _records[Key(record.Kind, record.Id)] = record;
        }

        /// <summary>Restores a validated snapshot atomically, retaining unknown retired IDs.</summary>
        /// <remarks>Pre: records non-null; each initialized and unique by category plus ID. Post: equals the snapshot; invalid input leaves state unchanged. Side effects: replaces in-memory records.</remarks>
        public void Restore(IReadOnlyList<CodexRecord> records)
        {
            Contract.Requires(records != null, "records are required");
            var next = new Dictionary<string, CodexRecord>(StringComparer.Ordinal);
            foreach (CodexRecord record in records)
            {
                Contract.Requires(Enum.IsDefined(typeof(CodexKind), record.Kind) && !string.IsNullOrWhiteSpace(record.Id)
                    && (record.Level == CodexLevel.Basic || record.Level == CodexLevel.Complete), "invalid record");
                string key = Key(record.Kind, record.Id);
                Contract.Requires(!next.ContainsKey(key), "duplicate record");
                next.Add(key, record);
            }
            _records = next;
        }
    }
}
