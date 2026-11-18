using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.Crafting;
using Cosmimera.GameData;

namespace Cosmimera.Save
{
    /// <summary>One creature's chosen appearance inside a <see cref="LoadoutPreset"/>.</summary>
    /// <remarks>Pre: instanceId >= 1; vessel defined. Post: immutable. Side effects: none.</remarks>
    public readonly struct VisualChoice
    {
        /// <summary>Creates a choice.</summary>
        /// <remarks>Pre: instanceId >= 1; vessel is a defined VesselKind. Post: properties echo the arguments. Side effects: none.</remarks>
        public VisualChoice(int instanceId, VesselKind vessel)
        {
            Contract.Requires(instanceId >= 1, "instance id must be >= 1");
            Contract.Requires(Enum.IsDefined(typeof(VesselKind), vessel), "vessel must be defined");
            InstanceId = instanceId;
            Vessel = vessel;
        }

        /// <summary>Creature the appearance belongs to.</summary>
        /// <remarks>Pre: none. Post: >= 1 unless default. Side effects: none.</remarks>
        public int InstanceId { get; }

        /// <summary>Appearance (VisualVessel) to restore.</summary>
        /// <remarks>Pre: none. Post: defined VesselKind. Side effects: none.</remarks>
        public VesselKind Vessel { get; }
    }

    /// <summary>
    /// A favourite set-up: main party, sub-partners, equipment of the player and every covered creature, and appearances.
    /// </summary>
    /// <remarks>
    /// Stores ids only, never content, so evolution or a retired item cannot leave a stale copy; whatever no longer
    /// resolves is skipped at apply time (see <see cref="PresetApplyResult"/>).
    /// Equipment covers owner 0 (the player), every party main and every sub in the preset; an empty slot of a covered
    /// owner is meaningful (applying empties it). Creatures outside that set are never touched.
    /// Pre: construct through the constructor. Post: immutable; lists are independent copies. Side effects: none.
    /// </remarks>
    public sealed class LoadoutPreset
    {
        /// <summary>Longest preset name, in UTF-16 units.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxNameLength = 24;

        /// <summary>Creates a preset.</summary>
        /// <remarks>
        /// Pre: name 1..MaxNameLength non-blank; party 1..LobbySave.MaxParty distinct ids >= 1; partners/equipment/visuals
        /// non-null with no duplicate sub, owner+slot or visual id; no sub inside party; every partner main in party;
        /// at most PartnerLoadout.MaxSubsPerMain subs per main; equipment owners are 0, a party main or a sub; items valid ids.
        /// Post: independent copies of every list. Side effects: allocates.
        /// </remarks>
        public LoadoutPreset(string name, IReadOnlyList<int> party, IReadOnlyList<PartnerPair> partners,
            IReadOnlyList<EquippedItem> equipment, IReadOnlyList<VisualChoice> visuals)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name) && name.Length <= MaxNameLength, "invalid preset name");
            Contract.Requires(party != null && partners != null && equipment != null && visuals != null, "lists are required");
            Contract.Requires(party.Count >= 1 && party.Count <= LobbySave.MaxParty, "party must hold 1..6");
            var mains = new HashSet<int>();
            foreach (int id in party) Contract.Requires(id >= 1 && mains.Add(id), "party ids must be distinct and >= 1");

            var subs = new HashSet<int>();
            var perMain = new Dictionary<int, int>();
            foreach (PartnerPair p in partners)
            {
                Contract.Requires(mains.Contains(p.MainInstanceId), "partner main must be in preset party");
                Contract.Requires(!mains.Contains(p.SubInstanceId) && subs.Add(p.SubInstanceId), "sub must be unique and outside party");
                perMain.TryGetValue(p.MainInstanceId, out int n);
                Contract.Requires(n < PartnerLoadout.MaxSubsPerMain, "main has too many subs");
                perMain[p.MainInstanceId] = n + 1;
            }

            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (EquippedItem e in equipment)
            {
                Contract.Requires(e.Owner == 0 || mains.Contains(e.Owner) || subs.Contains(e.Owner), "equipment owner outside preset");
                Contract.Requires(slots.Add(e.Owner + ":" + e.Slot), "duplicate equipment slot");
            }

            var seen = new HashSet<int>();
            foreach (VisualChoice v in visuals) Contract.Requires(v.InstanceId >= 1 && seen.Add(v.InstanceId), "duplicate or default visual");

            Name = name;
            Party = new List<int>(party).AsReadOnly();
            Partners = new List<PartnerPair>(partners).AsReadOnly();
            Equipment = new List<EquippedItem>(equipment).AsReadOnly();
            Visuals = new List<VisualChoice>(visuals).AsReadOnly();
        }

        /// <summary>Player-facing label.</summary>
        /// <remarks>Pre: none. Post: 1..MaxNameLength chars. Side effects: none.</remarks>
        public string Name { get; }

        /// <summary>Main party in order.</summary>
        /// <remarks>Pre: none. Post: 1..6 distinct ids. Side effects: none.</remarks>
        public IReadOnlyList<int> Party { get; }

        /// <summary>Sub-partner pairs.</summary>
        /// <remarks>Pre: none. Post: mains in Party, subs unique and outside Party. Side effects: none.</remarks>
        public IReadOnlyList<PartnerPair> Partners { get; }

        /// <summary>Equipment of the player (owner 0) and of every covered creature.</summary>
        /// <remarks>Pre: none. Post: unique owner+slot. Side effects: none.</remarks>
        public IReadOnlyList<EquippedItem> Equipment { get; }

        /// <summary>Appearances to restore.</summary>
        /// <remarks>Pre: none. Post: unique instance ids. Side effects: none.</remarks>
        public IReadOnlyList<VisualChoice> Visuals { get; }
    }

    /// <summary>Outcome of applying a <see cref="LoadoutPreset"/>.</summary>
    /// <remarks>Pre: none. Post: immutable. Side effects: none.</remarks>
    public readonly struct PresetApplyResult
    {
        /// <summary>Creates a result.</summary>
        /// <remarks>Pre: counts >= 0. Post: echoes arguments. Side effects: none.</remarks>
        public PresetApplyResult(bool applied, int missingCreatures, int missingItems)
        {
            Applied = applied;
            MissingCreatures = missingCreatures;
            MissingItems = missingItems;
        }

        /// <summary>True when the save changed. False leaves it untouched.</summary>
        /// <remarks>Pre: none. Post: as stated. Side effects: none.</remarks>
        public bool Applied { get; }

        /// <summary>Party, sub or appearance entries skipped because the creature is gone or the pairing no longer fits.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int MissingCreatures { get; }

        /// <summary>Equipment entries skipped: item not in stock, retired, or refused by wearer/grip rules.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int MissingItems { get; }
    }
}
