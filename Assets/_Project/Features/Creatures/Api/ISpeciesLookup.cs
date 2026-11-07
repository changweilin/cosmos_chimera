using Cosmimera.Core.Contracts;

namespace Cosmimera.Creatures
{
    /// <summary>The slice of a creature form the partner rules need: what a sub-partner can provide.</summary>
    /// <remarks>
    /// Pre: construct through the public constructor. Post: immutable; all ids non-empty.
    /// Side effects: none.
    /// </remarks>
    public readonly struct SpeciesInfo
    {
        /// <summary>Creates the info.</summary>
        /// <remarks>Pre: all three ids non-empty. Post: properties equal the arguments. Side effects: none.</remarks>
        public SpeciesInfo(string speciesId, string signatureMoveId, string traitId)
        {
            Contract.Requires(!string.IsNullOrEmpty(speciesId), "speciesId must be non-empty");
            Contract.Requires(!string.IsNullOrEmpty(signatureMoveId), "signatureMoveId must be non-empty");
            Contract.Requires(!string.IsNullOrEmpty(traitId), "traitId must be non-empty");
            SpeciesId = speciesId;
            SignatureMoveId = signatureMoveId;
            TraitId = traitId;
        }

        /// <summary>CreatureDef id.</summary>
        /// <remarks>Pre: none. Post: non-empty unless default(SpeciesInfo) (then null). Side effects: none.</remarks>
        public string SpeciesId { get; }

        /// <summary>The species' only move in data (CreatureDef.SignatureMove).</summary>
        /// <remarks>Pre: none. Post: non-empty unless default(SpeciesInfo) (then null). Side effects: none.</remarks>
        public string SignatureMoveId { get; }

        /// <summary>The species' trait (CreatureDef.Trait).</summary>
        /// <remarks>Pre: none. Post: non-empty unless default(SpeciesInfo) (then null). Side effects: none.</remarks>
        public string TraitId { get; }
    }

    /// <summary>Species data access for the partner rules; an interface so tests need no ScriptableObjects.</summary>
    /// <remarks>
    /// Pre: implementations are read-only views over data.
    /// Post: see <see cref="TryGet"/>.
    /// Side effects: none.
    /// </remarks>
    public interface ISpeciesLookup
    {
        /// <summary>Finds a species by id.</summary>
        /// <remarks>
        /// Pre: speciesId non-null.
        /// Post: true iff the species exists, then info describes it; false (info default) otherwise.
        /// Side effects: none (may build a cache on first use).
        /// </remarks>
        bool TryGet(string speciesId, out SpeciesInfo info);
    }
}
