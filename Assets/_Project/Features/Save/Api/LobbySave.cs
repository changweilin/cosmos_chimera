using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using UnityEngine;

namespace Cosmimera.Save
{
    /// <summary>
    /// Lobby save: owned creatures and party. REQ: lobby-meta.md#進度保存 — Q-M4 roguelite: no mid-planet state is
    /// ever saved; a run only adds creatures here when it ends (victory, defeat or abandon).
    /// </summary>
    /// <remarks>
    /// Format: UTF-8 JSON, <see cref="FormatVersion"/> at the top. P4-06 (migrations, retired ids) builds on this; a
    /// newer version than we know is refused instead of silently dropping fields.
    /// Version 2 (P2-12): <c>exp</c> is cumulative level XP. Version 1 stored the old evolution XP, whose meaning differs,
    /// so it is not converted: a v1 creature restarts at T(stage floor) (option A, level-progression.md#獎勵量).
    /// Pre: main thread. Side effects: instance methods mutate this object only; Load/Write do file I/O.
    /// </remarks>
    public sealed class LobbySave
    {
        /// <summary>Current save format version.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int FormatVersion = 2;

        /// <summary>Party size cap (creatures.md#隊伍規則: 6 主力).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxParty = 6;

        private readonly List<OwnedCreature> _owned = new List<OwnedCreature>();
        private readonly List<int> _party = new List<int>();
        private int _nextInstanceId = 1;

        /// <summary>Format version the save was read from (FormatVersion for a new save); lower means it was migrated.</summary>
        /// <remarks>Pre: none. Post: 1..FormatVersion. Side effects: none.</remarks>
        public int LoadedFormatVersion { get; private set; } = FormatVersion;

        /// <summary>Starter line chosen in the lobby, or null before the first choice.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public string StarterLine { get; private set; }

        /// <summary>Owned creatures in acquisition order.</summary>
        /// <remarks>Pre: none. Post: unique InstanceIds. Side effects: none.</remarks>
        public IReadOnlyList<OwnedCreature> Owned => _owned;

        /// <summary>Party instance ids (≤ 6, each owned, no duplicates).</summary>
        /// <remarks>Pre: none. Post: as stated. Side effects: none.</remarks>
        public IReadOnlyList<int> Party => _party;

        /// <summary>Records the starter choice and adds the starter.</summary>
        /// <remarks>
        /// Pre: StarterLine == null; lineId and speciesId non-empty; exp &gt;= 0 (T(starter level), LV10).
        /// Post: StarterLine == lineId; the starter is owned with that exp (vessel magic_cube is irrelevant for a main
        ///       creature, lobby-meta.md: "作為主力 無差別"; talisman_card chosen as default) and in the party.
        /// Side effects: mutates this save.
        /// </remarks>
        public OwnedCreature ChooseStarter(string lineId, string speciesId, int exp)
        {
            Contract.Requires(StarterLine == null, "starter already chosen");
            Contract.Requires(!string.IsNullOrEmpty(lineId), "lineId must be non-empty");
            StarterLine = lineId;
            return Add(speciesId, VesselKind.TalismanCard, exp);
        }

        /// <summary>Adds a captured creature; it joins the party if there is room.</summary>
        /// <remarks>
        /// Pre: speciesId non-empty; vessel defined; exp &gt;= 0 (a capture keeps its encounter level: T(L)).
        /// Post: one more owned creature with a fresh InstanceId and that exp; in Party iff Party had &lt; 6 before.
        ///       <see cref="InvalidOperationException"/> in every build, with the save unchanged, once instance ids are
        ///       exhausted (the next id would be int.MaxValue and the cursor could not advance past it).
        /// Side effects: mutates this save.
        /// </remarks>
        public OwnedCreature Add(string speciesId, VesselKind vessel, int exp)
        {
            // Checked before any mutation: incrementing first would overflow the cursor and write a save FromJson rejects
            // (SOL P2-12 R4).
            if (_nextInstanceId >= int.MaxValue)
            {
                throw new InvalidOperationException("lobby save: creature instance ids are exhausted");
            }

            var c = new OwnedCreature(_nextInstanceId, speciesId, vessel, exp);
            _nextInstanceId++;
            _owned.Add(c);
            if (_party.Count < MaxParty)
            {
                _party.Add(c.InstanceId);
            }

            return c;
        }

        /// <summary>Replaces the party.</summary>
        /// <remarks>
        /// Pre: 1..6 distinct owned instance ids.
        /// Post: Party equals instanceIds in order. Side effects: mutates this save.
        /// </remarks>
        public void SetParty(IReadOnlyList<int> instanceIds)
        {
            Contract.Requires(instanceIds != null && instanceIds.Count >= 1 && instanceIds.Count <= MaxParty,
                "party must hold 1..6 creatures");
            // Copy first: callers may pass Party itself, and clearing _party would empty the input (SOL D0 H1).
            var copy = new List<int>(instanceIds);
            var seen = new HashSet<int>();
            foreach (int id in copy)
            {
                Contract.Requires(seen.Add(id), "party ids must be distinct");
                Contract.Requires(_owned.Exists(o => o.InstanceId == id), "party ids must be owned");
            }

            _party.Clear();
            _party.AddRange(copy);
        }

        /// <summary>Replaces an owned creature's cumulative XP (run rewards are applied in the lobby, Q-M4).</summary>
        /// <remarks>
        /// Pre: instanceId owned; exp &gt;= 0. Post: Get(instanceId).Exp == exp; species, vessel and order unchanged.
        /// Side effects: mutates this save.
        /// </remarks>
        public void SetExp(int instanceId, int exp)
        {
            int i = _owned.FindIndex(o => o.InstanceId == instanceId);
            Contract.Requires(i >= 0, "instance id is not owned");
            OwnedCreature o = _owned[i];
            _owned[i] = new OwnedCreature(o.InstanceId, o.SpeciesId, o.Vessel, exp);
        }

        /// <summary>Owned creature by instance id.</summary>
        /// <remarks>Pre: id owned. Post: InstanceId == id. Side effects: none.</remarks>
        public OwnedCreature Get(int instanceId)
        {
            int i = _owned.FindIndex(o => o.InstanceId == instanceId);
            Contract.Requires(i >= 0, "instance id is not owned");
            return _owned[i];
        }

        /// <summary>Serializes to JSON.</summary>
        /// <remarks>Pre: none. Post: FromJson(ToJson()) reproduces this save. Side effects: allocates.</remarks>
        public string ToJson()
        {
            var dto = new Dto
            {
                version = FormatVersion,
                next_instance_id = _nextInstanceId,
                starter_line = StarterLine ?? string.Empty,
                party = _party.ToArray(),
                owned = new CreatureDto[_owned.Count],
            };
            for (int i = 0; i < _owned.Count; i++)
            {
                OwnedCreature o = _owned[i];
                dto.owned[i] = new CreatureDto
                {
                    instance_id = o.InstanceId, species_id = o.SpeciesId, vessel = (int)o.Vessel, exp = o.Exp,
                };
            }

            return JsonUtility.ToJson(dto, true);
        }

        /// <summary>Parses a save, migrating format 1 to the current format.</summary>
        /// <remarks>
        /// Pre: json non-empty; progression non-null; stageOf non-null (returns null for an unknown species).
        /// Post: the save it encodes with every Exp in 0..progression.MaxExp (higher values are clipped, never earned);
        ///       a format-1 creature gets progression.MigrationExp(stage of its species) and LoadedFormatVersion == 1.
        ///       <see cref="InvalidDataException"/> in every build (Contract checks are stripped from Release, and a
        ///       Release that loaded and re-wrote a newer save would silently drop its fields — SOL D0 H2) for: not a
        ///       save, a newer FormatVersion, duplicate/non-positive instance ids, next id below 1 or not above every owned
        ///       id, an undefined vessel, a negative exp, a party entry that is not owned or listed twice, a party over 6,
        ///       a party empty while creatures are owned (or non-empty without any), or a format-1 species stageOf
        ///       does not know (it cannot be migrated without guessing).
        /// Side effects: allocates.
        /// </remarks>
        public static LobbySave FromJson(string json, Progression progression, Func<string, CreatureStage?> stageOf)
        {
            Contract.Requires(progression != null && stageOf != null, "progression and stageOf are required");
            Check(!string.IsNullOrEmpty(json), "save json is empty");
            Dto dto = JsonUtility.FromJson<Dto>(json);
            Check(dto != null && dto.version >= 1, "not a lobby save");
            Check(dto.version <= FormatVersion, "save is from a newer game version");
            var save = new LobbySave
            {
                StarterLine = string.IsNullOrEmpty(dto.starter_line) ? null : dto.starter_line,
                _nextInstanceId = dto.next_instance_id,
                LoadedFormatVersion = dto.version,
            };
            // Independent of owned: with an empty roster the per-creature check below never runs (SOL P2-12 R4).
            Check(dto.next_instance_id >= 1, "next_instance_id must be >= 1");
            var ids = new HashSet<int>();
            foreach (CreatureDto c in dto.owned ?? new CreatureDto[0])
            {
                Check(c.instance_id >= 1 && ids.Add(c.instance_id), "duplicate or non-positive instance id");
                Check(c.instance_id < dto.next_instance_id, "next_instance_id must exceed every owned id");
                Check(Enum.IsDefined(typeof(VesselKind), c.vessel) && !string.IsNullOrEmpty(c.species_id),
                    "creature entry has an undefined vessel or empty species");
                Check(c.exp >= 0, "creature entry has a negative exp");
                int exp = c.exp;
                if (dto.version == 1)
                {
                    CreatureStage? stage = stageOf(c.species_id);
                    Check(stage.HasValue, "format-1 creature '" + c.species_id + "' is unknown and cannot be migrated");
                    exp = progression.MigrationExp(stage.Value);
                }

                save._owned.Add(new OwnedCreature(c.instance_id, c.species_id, (VesselKind)c.vessel,
                    Math.Min(exp, progression.MaxExp)));
            }

            // Party is the XP settlement roster, so a duplicate would be paid twice (SOL P2-12 R3). An owned roster always
            // has a party: ChooseStarter/Add put the first creature in it and SetParty never empties it.
            int[] party = dto.party ?? new int[0];
            Check(party.Length <= MaxParty, "party holds more than 6 creatures");
            Check(party.Length > 0 == ids.Count > 0, "party must be non-empty exactly when creatures are owned");
            var inParty = new HashSet<int>();
            foreach (int p in party)
            {
                Check(ids.Contains(p), "party entry is not owned");
                Check(inParty.Add(p), "party lists a creature twice");
            }

            save._party.AddRange(party);
            return save;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException("lobby save: " + message);
            }
        }

        /// <summary>Loads from <paramref name="path"/>, or returns a new empty save when the file does not exist.</summary>
        /// <remarks>
        /// Pre: path non-empty; see FromJson. Post: see FromJson; when the file was an older format, the original bytes
        ///      exist at <paramref name="path"/> + ".v{N}.bak" (an existing backup is never overwritten).
        /// Side effects: reads the file; may write the backup.
        /// </remarks>
        public static LobbySave Load(string path, Progression progression, Func<string, CreatureStage?> stageOf)
        {
            Contract.Requires(!string.IsNullOrEmpty(path), "path must not be empty");
            if (!File.Exists(path))
            {
                return new LobbySave();
            }

            LobbySave save = FromJson(File.ReadAllText(path), progression, stageOf);
            // Migration is lossy by design (old evolution XP is dropped), so keep the original before Write replaces it.
            string backup = path + ".v" + save.LoadedFormatVersion + ".bak";
            if (save.LoadedFormatVersion < FormatVersion && !File.Exists(backup))
            {
                File.Copy(path, backup);
            }

            return save;
        }

        /// <summary>Writes atomically to <paramref name="path"/> (temp file + replace, so a crash never leaves half a save).</summary>
        /// <remarks>Pre: path non-empty; directory writable. Post: Load(path) equals this. Side effects: writes files.</remarks>
        public void Write(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path), "path must not be empty");
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, ToJson());
            if (File.Exists(path))
            {
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

#pragma warning disable 0649, IDE1006
        [Serializable] private sealed class Dto
        {
            public int version;
            public int next_instance_id;
            public string starter_line;
            public int[] party;
            public CreatureDto[] owned;
        }

        [Serializable] private sealed class CreatureDto
        {
            public int instance_id;
            public string species_id;
            public int vessel;
            public int exp;
        }
#pragma warning restore 0649, IDE1006
    }
}
