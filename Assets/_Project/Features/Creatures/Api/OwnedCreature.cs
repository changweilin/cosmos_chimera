using System;
using Cosmimera.Core.Contracts;

namespace Cosmimera.Creatures
{
    /// <summary>One creature individual in the player's collection. REQ: docs/design/lobby-meta.md#收服道具 (vessel is per individual).</summary>
    /// <remarks>
    /// Pre: construct only through the public constructor; <c>default(OwnedCreature)</c> has InstanceId 0 and is
    ///      rejected by every rule entry point.
    /// Post: immutable; InstanceId &gt;= 1, SpeciesId non-empty, Vessel defined, Exp &gt;= 0.
    /// Side effects: none.
    /// </remarks>
    public readonly struct OwnedCreature
    {
        /// <summary>Creates an individual whose visual and functional vessels match.</summary>
        /// <remarks>
        /// Pre: instanceId &gt;= 1; speciesId non-empty (the CreatureDef id of its current form);
        ///      vessel is a defined VesselKind; exp &gt;= 0.
        /// Post: properties equal the arguments and VisualVessel == vessel.
        /// Side effects: none.
        /// </remarks>
        public OwnedCreature(int instanceId, string speciesId, VesselKind vessel, int exp)
            : this(instanceId, speciesId, vessel, exp, vessel)
        {
        }

        /// <summary>Creates an individual with explicit functional and visual vessels.</summary>
        /// <remarks>
        /// Pre: instanceId &gt;= 1; speciesId non-empty (the CreatureDef id of its current form);
        ///      vessel and visualVessel are defined VesselKind values; exp &gt;= 0.
        /// Post: properties equal the arguments.
        /// Side effects: none.
        /// </remarks>
        public OwnedCreature(int instanceId, string speciesId, VesselKind vessel, int exp, VesselKind visualVessel)
        {
            Contract.Requires(instanceId >= 1, "instanceId must be >= 1");
            Contract.Requires(!string.IsNullOrEmpty(speciesId), "speciesId must be non-empty");
            Contract.Requires(Enum.IsDefined(typeof(VesselKind), vessel), "vessel must be a defined VesselKind");
            Contract.Requires(Enum.IsDefined(typeof(VesselKind), visualVessel), "visualVessel must be a defined VesselKind");
            Contract.Requires(exp >= 0, "exp must be >= 0");
            InstanceId = instanceId;
            SpeciesId = speciesId;
            Vessel = vessel;
            VisualVessel = visualVessel;
            Exp = exp;
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

        /// <summary>The same individual in another form.</summary>
        /// <remarks>
        /// Pre: this is a constructed value (InstanceId &gt;= 1); newSpeciesId non-empty.
        /// Post: InstanceId, Vessel, VisualVessel and Exp unchanged; SpeciesId == newSpeciesId.
        /// Side effects: none (returns a new value).
        /// </remarks>
        public OwnedCreature WithSpecies(string newSpeciesId)
        {
            Contract.Requires(InstanceId >= 1, "cannot change the species of default(OwnedCreature)");
            return new OwnedCreature(InstanceId, newSpeciesId, Vessel, Exp, VisualVessel);
        }

        /// <summary>The same individual with a converted functional capture-item category (appearance unchanged).</summary>
        /// <remarks>
        /// Pre: this is a constructed value (InstanceId &gt;= 1); newVessel is a defined VesselKind.
        /// Post: InstanceId, SpeciesId, Exp and VisualVessel unchanged; Vessel == newVessel.
        /// Side effects: none (returns a new value).
        /// </remarks>
        public OwnedCreature WithVessel(VesselKind newVessel)
        {
            Contract.Requires(InstanceId >= 1, "cannot change the vessel of default(OwnedCreature)");
            Contract.Requires(Enum.IsDefined(typeof(VesselKind), newVessel), "vessel must be a defined VesselKind");
            return new OwnedCreature(InstanceId, SpeciesId, newVessel, Exp, VisualVessel);
        }
    }
}
