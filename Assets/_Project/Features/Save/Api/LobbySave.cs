using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
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
    /// Pre: main thread. Side effects: instance methods mutate this object only; Load/Write do file I/O.
    /// </remarks>
    public sealed class LobbySave
    {
        /// <summary>Current save format version.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int FormatVersion = 1;

        /// <summary>Party size cap (creatures.md#隊伍規則: 6 主力).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxParty = 6;

        private readonly List<OwnedCreature> _owned = new List<OwnedCreature>();
        private readonly List<int> _party = new List<int>();
        private int _nextInstanceId = 1;

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
        /// Pre: StarterLine == null; lineId and speciesId non-empty.
        /// Post: StarterLine == lineId; the starter is owned (vessel magic_cube is irrelevant for a main creature,
        ///       lobby-meta.md: "作為主力 無差別"; talisman_card chosen as default) and in the party.
        /// Side effects: mutates this save.
        /// </remarks>
        public OwnedCreature ChooseStarter(string lineId, string speciesId)
        {
            Contract.Requires(StarterLine == null, "starter already chosen");
            Contract.Requires(!string.IsNullOrEmpty(lineId), "lineId must be non-empty");
            StarterLine = lineId;
            return Add(speciesId, VesselKind.TalismanCard);
        }

        /// <summary>Adds a captured creature; it joins the party if there is room.</summary>
        /// <remarks>
        /// Pre: speciesId non-empty; vessel defined.
        /// Post: one more owned creature with a fresh InstanceId; in Party iff Party had &lt; 6 before.
        /// Side effects: mutates this save.
        /// </remarks>
        public OwnedCreature Add(string speciesId, VesselKind vessel)
        {
            var c = new OwnedCreature(_nextInstanceId++, speciesId, vessel, 0);
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
            var seen = new HashSet<int>();
            foreach (int id in instanceIds)
            {
                Contract.Requires(seen.Add(id), "party ids must be distinct");
                Contract.Requires(_owned.Exists(o => o.InstanceId == id), "party ids must be owned");
            }

            _party.Clear();
            _party.AddRange(instanceIds);
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

        /// <summary>Parses a save.</summary>
        /// <remarks>
        /// Pre: json non-empty.
        /// Post: the save it encodes; ContractException for a newer FormatVersion, duplicate or non-positive instance
        ///       ids, next id not above every owned id, or a party entry that is not owned.
        /// Side effects: allocates.
        /// </remarks>
        public static LobbySave FromJson(string json)
        {
            Contract.Requires(!string.IsNullOrEmpty(json), "save json must not be empty");
            Dto dto = JsonUtility.FromJson<Dto>(json);
            Contract.Requires(dto != null && dto.version >= 1, "not a lobby save");
            Contract.Requires(dto.version <= FormatVersion, "save is from a newer game version");
            var save = new LobbySave
            {
                StarterLine = string.IsNullOrEmpty(dto.starter_line) ? null : dto.starter_line,
                _nextInstanceId = dto.next_instance_id,
            };
            var ids = new HashSet<int>();
            foreach (CreatureDto c in dto.owned ?? new CreatureDto[0])
            {
                Contract.Requires(ids.Add(c.instance_id), "duplicate instance id");
                Contract.Requires(c.instance_id < dto.next_instance_id, "next_instance_id must exceed every owned id");
                save._owned.Add(new OwnedCreature(c.instance_id, c.species_id, (VesselKind)c.vessel, c.exp));
            }

            foreach (int p in dto.party ?? new int[0])
            {
                Contract.Requires(ids.Contains(p), "party entry is not owned");
            }

            save._party.AddRange(dto.party ?? new int[0]);
            return save;
        }

        /// <summary>Loads from <paramref name="path"/>, or returns a new empty save when the file does not exist.</summary>
        /// <remarks>Pre: path non-empty. Post: see FromJson. Side effects: reads the file.</remarks>
        public static LobbySave Load(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path), "path must not be empty");
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new LobbySave();
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
