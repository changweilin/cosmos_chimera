using Cosmimera.Core.Contracts;

namespace Cosmimera.Creatures
{
    /// <summary>What a sub-partner gives its main. REQ: docs/design/creatures.md#隊伍規則, lobby-meta.md#收服道具.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum ProvisionKind
    {
        /// <summary>One move (符籙卡片); Id is a move id.</summary>
        Move = 0,

        /// <summary>The sub's trait (琉璃珠); Id is a trait id.</summary>
        Trait = 1,

        /// <summary>Unlocks the main's own overload (魔術方塊); Id is the main's species id. Overload itself is Q-C12.</summary>
        Overload = 2,
    }

    /// <summary>The single thing one sub-partner provides to its main.</summary>
    /// <remarks>
    /// Pre: construct through <see cref="Resolve"/> (or the constructor).
    /// Post: immutable.
    /// Side effects: none.
    /// </remarks>
    public readonly struct Provision
    {
        /// <summary>Creates a provision.</summary>
        /// <remarks>Pre: kind defined; id non-empty; sourceInstanceId &gt;= 1. Post: properties equal the arguments. Side effects: none.</remarks>
        public Provision(ProvisionKind kind, string id, int sourceInstanceId)
        {
            Contract.Requires(kind >= ProvisionKind.Move && kind <= ProvisionKind.Overload, "kind must be a defined ProvisionKind");
            Contract.Requires(!string.IsNullOrEmpty(id), "id must be non-empty");
            Contract.Requires(sourceInstanceId >= 1, "sourceInstanceId must be >= 1");
            Kind = kind;
            Id = id;
            SourceInstanceId = sourceInstanceId;
        }

        /// <summary>What is provided.</summary>
        /// <remarks>Pre: none. Post: a defined ProvisionKind. Side effects: none.</remarks>
        public ProvisionKind Kind { get; }

        /// <summary>Move id, trait id, or the main's species id, depending on <see cref="Kind"/>.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>The sub-partner individual that provides it.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int SourceInstanceId { get; }

        /// <summary>Decides what <paramref name="sub"/> provides from the vessel it was captured with.</summary>
        /// <remarks>
        /// Pre: sub.InstanceId &gt;= 1; mainSpeciesId non-empty (current form of the main); species != null; for
        ///      talisman_card / glass_bead subs, sub.SpeciesId exists in species (a missing species is a data/save
        ///      corruption, not a player action, so it throws).
        /// Post: TalismanCard → Move (the sub species' SignatureMove); GlassBead → Trait (the sub species' Trait);
        ///       MagicCube → Overload (Id == mainSpeciesId). Exactly one provision per sub; SourceInstanceId == sub.InstanceId.
        /// Side effects: none.
        /// </remarks>
        public static Provision Resolve(in OwnedCreature sub, string mainSpeciesId, ISpeciesLookup species)
        {
            Contract.Requires(sub.InstanceId >= 1, "sub must be a constructed OwnedCreature (InstanceId >= 1)");
            Contract.Requires(!string.IsNullOrEmpty(mainSpeciesId), "mainSpeciesId must be non-empty");
            Contract.Requires(species != null, "species is null");

            if (sub.Vessel == VesselKind.MagicCube)
            {
                return new Provision(ProvisionKind.Overload, mainSpeciesId, sub.InstanceId);
            }

            bool known = species.TryGet(sub.SpeciesId, out SpeciesInfo info);
            Contract.Requires(known, "sub species '" + sub.SpeciesId + "' is not in the species data");
            return sub.Vessel == VesselKind.TalismanCard
                ? new Provision(ProvisionKind.Move, info.SignatureMoveId, sub.InstanceId)
                : new Provision(ProvisionKind.Trait, info.TraitId, sub.InstanceId);
        }
    }
}
