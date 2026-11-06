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
        /// <summary>Creates an individual.</summary>
        /// <remarks>
        /// Pre: instanceId &gt;= 1; speciesId non-empty (the CreatureDef id of its current form);
        ///      vessel is a defined VesselKind; exp &gt;= 0.
        /// Post: properties equal the arguments.
        /// Side effects: none.
        /// </remarks>
        public OwnedCreature(int instanceId, string speciesId, VesselKind vessel, int exp)
        {
            Contract.Requires(instanceId >= 1, "instanceId must be >= 1");
            Contract.Requires(!string.IsNullOrEmpty(speciesId), "speciesId must be non-empty");
            Contract.Requires(Enum.IsDefined(typeof(VesselKind), vessel), "vessel must be a defined VesselKind");
            Contract.Requires(exp >= 0, "exp must be >= 0");
            InstanceId = instanceId;
            SpeciesId = speciesId;
            Vessel = vessel;
            Exp = exp;
        }

        /// <summary>Stable id of this individual, unique within the collection.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1 unless default(OwnedCreature). Side effects: none.</remarks>
        public int InstanceId { get; }

        /// <summary>CreatureDef id of the form this individual currently has.</summary>
        /// <remarks>Pre: none. Post: non-empty unless default(OwnedCreature). Side effects: none.</remarks>
        public string SpeciesId { get; }

        /// <summary>Capture-item category recorded at capture time.</summary>
        /// <remarks>Pre: none. Post: a defined VesselKind. Side effects: none.</remarks>
        public VesselKind Vessel { get; }

        /// <summary>Accumulated experience; a threshold for evolution, never consumed by it.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int Exp { get; }

        /// <summary>The same individual in another form.</summary>
        /// <remarks>
        /// Pre: this is a constructed value (InstanceId &gt;= 1); newSpeciesId non-empty.
        /// Post: InstanceId, Vessel and Exp unchanged; SpeciesId == newSpeciesId.
        /// Side effects: none (returns a new value).
        /// </remarks>
        public OwnedCreature WithSpecies(string newSpeciesId)
        {
            Contract.Requires(InstanceId >= 1, "cannot change the species of default(OwnedCreature)");
            return new OwnedCreature(InstanceId, newSpeciesId, Vessel, Exp);
        }
    }
}
