using System;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Creatures
{
    /// <summary>One creature individual in the player's collection. REQ: docs/design/lobby-meta.md#收服道具 (vessel is per individual).</summary>
    /// <remarks>
    /// Pre: construct only through the public constructor; <c>default(OwnedCreature)</c> has InstanceId 0 and is
    ///      rejected by every rule entry point.
    /// Post: immutable; InstanceId &gt;= 1, SpeciesId non-empty, Vessel defined, Exp &gt;= 0, Habit defined, NicheId non-null.
    /// Side effects: none.
    /// </remarks>
    public readonly struct OwnedCreature
    {
        /// <summary>Creates an individual whose visual and functional vessels match.</summary>
        /// <remarks>
        /// Pre: instanceId &gt;= 1; speciesId non-empty (the CreatureDef id of its current form);
        ///      vessel is a defined VesselKind; exp &gt;= 0.
        /// Post: properties equal the arguments, VisualVessel == vessel, NicheId == "", Habit == Tolerant.
        /// Side effects: none.
        /// </remarks>
        public OwnedCreature(int instanceId, string speciesId, VesselKind vessel, int exp)
            : this(instanceId, speciesId, vessel, exp, vessel, "", Habit.Tolerant)
        {
        }

        /// <summary>Creates an individual with explicit functional and visual vessels.</summary>
        /// <remarks>
        /// Pre: instanceId &gt;= 1; speciesId non-empty (the CreatureDef id of its current form);
        ///      vessel and visualVessel are defined VesselKind values; exp &gt;= 0.
        /// Post: properties equal the arguments, NicheId == "", Habit == Tolerant.
        /// Side effects: none.
        /// </remarks>
        public OwnedCreature(int instanceId, string speciesId, VesselKind vessel, int exp, VesselKind visualVessel)
            : this(instanceId, speciesId, vessel, exp, visualVessel, "", Habit.Tolerant)
        {
        }

        /// <summary>Creates an individual with explicit functional/visual vessels and ecology (niche and habit).</summary>
        /// <remarks>
        /// Pre: instanceId &gt;= 1; speciesId non-empty; vessel and visualVessel are defined VesselKind values;
        ///      exp &gt;= 0; habit is a defined Habit.
        /// Post: properties equal the arguments (null nicheId is normalized to "").
        /// Side effects: none.
        /// </remarks>
        public OwnedCreature(int instanceId, string speciesId, VesselKind vessel, int exp, VesselKind visualVessel,
            string nicheId, Habit habit)
        {
            Contract.Requires(instanceId >= 1, "instanceId must be >= 1");
            Contract.Requires(!string.IsNullOrEmpty(speciesId), "speciesId must be non-empty");
            Contract.Requires(Enum.IsDefined(typeof(VesselKind), vessel), "vessel must be a defined VesselKind");
            Contract.Requires(Enum.IsDefined(typeof(VesselKind), visualVessel), "visualVessel must be a defined VesselKind");
            Contract.Requires(exp >= 0, "exp must be >= 0");
            Contract.Requires(Enum.IsDefined(typeof(Habit), habit), "habit must be a defined Habit");
            InstanceId = instanceId;
            SpeciesId = speciesId;
            Vessel = vessel;
            VisualVessel = visualVessel;
            Exp = exp;
            NicheId = nicheId ?? "";
            Habit = habit;
        }

        /// <summary>Stable id of this individual, unique within the collection.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1 unless default(OwnedCreature). Side effects: none.</remarks>
        public int InstanceId { get; }

        /// <summary>CreatureDef id of the form this individual currently has.</summary>
        /// <remarks>Pre: none. Post: non-empty unless default(OwnedCreature). Side effects: none.</remarks>
        public string SpeciesId { get; }

        /// <summary>Functional capture-item category used for partner provisions and field utilities.</summary>
        /// <remarks>Pre: none. Post: a defined VesselKind. Side effects: none.</remarks>
        public VesselKind Vessel { get; }

        /// <summary>Appearance capture-item category recorded at capture/hatch time; preserved on late-game vessel conversion.</summary>
        /// <remarks>Pre: none. Post: a defined VesselKind. Side effects: none.</remarks>
        public VesselKind VisualVessel { get; }

        /// <summary>Accumulated experience; a threshold for evolution, never consumed by it.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int Exp { get; }

        /// <summary>Individual ecological niche id, or empty string before one-time migration. REQ: docs/design/creature-ecology.md.</summary>
        /// <remarks>Pre: none. Post: non-null (empty when unset). Side effects: none.</remarks>
        public string NicheId { get; }

        /// <summary>Individual behavioural habit (independent of species). REQ: docs/design/creature-ecology.md#習性.</summary>
        /// <remarks>Pre: none. Post: a defined Habit. Side effects: none.</remarks>
        public Habit Habit { get; }

        /// <summary>Whether this individual already has a drawn niche and habit.</summary>
        /// <remarks>Pre: none. Post: true iff NicheId is non-empty. Side effects: none.</remarks>
        public bool HasEcology => !string.IsNullOrEmpty(NicheId);

        /// <summary>The same individual in another form, keeping its current ecology.</summary>
        /// <remarks>
        /// Pre: this is a constructed value (InstanceId &gt;= 1); newSpeciesId non-empty.
        /// Post: InstanceId, Vessel, VisualVessel, Exp, NicheId and Habit unchanged; SpeciesId == newSpeciesId.
        /// Side effects: none (returns a new value).
        /// </remarks>
        public OwnedCreature WithSpecies(string newSpeciesId)
        {
            Contract.Requires(InstanceId >= 1, "cannot change the species of default(OwnedCreature)");
            return new OwnedCreature(InstanceId, newSpeciesId, Vessel, Exp, VisualVessel, NicheId, Habit);
        }

        /// <summary>The same individual in another form with redrawn ecology (evolution redraws niche and habit).</summary>
        /// <remarks>
        /// Pre: this is a constructed value (InstanceId &gt;= 1); newSpeciesId and nicheId non-empty; habit is a defined Habit.
        /// Post: InstanceId, Vessel, VisualVessel and Exp unchanged; SpeciesId, NicheId and Habit updated.
        /// Side effects: none (returns a new value).
        /// </remarks>
        public OwnedCreature WithSpecies(string newSpeciesId, string nicheId, Habit habit)
        {
            Contract.Requires(InstanceId >= 1, "cannot change the species of default(OwnedCreature)");
            Contract.Requires(!string.IsNullOrEmpty(nicheId), "nicheId must be non-empty");
            return new OwnedCreature(InstanceId, newSpeciesId, Vessel, Exp, VisualVessel, nicheId, habit);
        }

        /// <summary>The same individual with ecology assigned (e.g. one-time migration of a legacy save).</summary>
        /// <remarks>
        /// Pre: this is a constructed value (InstanceId &gt;= 1); nicheId non-empty; habit is a defined Habit.
        /// Post: InstanceId, SpeciesId, Vessel, VisualVessel and Exp unchanged; NicheId == nicheId; Habit == habit.
        /// Side effects: none (returns a new value).
        /// </remarks>
        public OwnedCreature WithEcology(string nicheId, Habit habit)
        {
            Contract.Requires(InstanceId >= 1, "cannot assign ecology on default(OwnedCreature)");
            Contract.Requires(!string.IsNullOrEmpty(nicheId), "nicheId must be non-empty");
            return new OwnedCreature(InstanceId, SpeciesId, Vessel, Exp, VisualVessel, nicheId, habit);
        }

        /// <summary>The same individual with a converted functional capture-item category (appearance and ecology unchanged).</summary>
        /// <remarks>
        /// Pre: this is a constructed value (InstanceId &gt;= 1); newVessel is a defined VesselKind.
        /// Post: InstanceId, SpeciesId, Exp, VisualVessel, NicheId and Habit unchanged; Vessel == newVessel.
        /// Side effects: none (returns a new value).
        /// </remarks>
        public OwnedCreature WithVessel(VesselKind newVessel)
        {
            Contract.Requires(InstanceId >= 1, "cannot change the vessel of default(OwnedCreature)");
            Contract.Requires(Enum.IsDefined(typeof(VesselKind), newVessel), "vessel must be a defined VesselKind");
            return new OwnedCreature(InstanceId, SpeciesId, newVessel, Exp, VisualVessel, NicheId, Habit);
        }
    }
}
