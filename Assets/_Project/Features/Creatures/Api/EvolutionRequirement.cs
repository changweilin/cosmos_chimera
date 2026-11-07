using System.Collections.Generic;
using System.Collections.ObjectModel;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Creatures
{
    /// <summary>Where an evolution is attempted. REQ: docs/design/creatures.md#進化 (Q-C9: lobby only).</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum EvolutionVenue
    {
        /// <summary>The Earth lobby — the only place evolution is allowed.</summary>
        Lobby = 0,

        /// <summary>Anywhere on a generated planet (exploration, combat).</summary>
        Planet = 1,
    }

    /// <summary>What one form needs in order to evolve into the next. REQ: docs/design/creatures.md#進化.</summary>
    /// <remarks>
    /// Pre: construct through the constructor or <see cref="FromDef"/>.
    /// Post: immutable. <c>default(EvolutionRequirement)</c> is the "final stage" requirement (IsFinal, no
    ///       materials, NextSpeciesId null), so an unset value can never allow an evolution.
    /// Side effects: none.
    /// </remarks>
    public readonly struct EvolutionRequirement
    {
        private static readonly ReadOnlyCollection<MaterialStack> NoMaterials = new List<MaterialStack>().AsReadOnly();

        private readonly ReadOnlyCollection<MaterialStack> _materials;

        /// <summary>Creates the requirement for a non-final form.</summary>
        /// <remarks>
        /// Pre: requiredExp &gt;= 0; materials != null with every entry Qty &gt;= 1 and a constructed key; nextSpeciesId
        ///      non-empty; the summed Qty of repeated keys fits in int.
        /// Post: IsFinal false; Materials has unique keys (repeats summed) in first-seen order; the input list is copied.
        /// Side effects: allocates.
        /// </remarks>
        public EvolutionRequirement(int requiredExp, IReadOnlyList<MaterialStack> materials, string nextSpeciesId)
        {
            Contract.Requires(requiredExp >= 0, "requiredExp must be >= 0");
            Contract.Requires(materials != null, "materials is null");
            Contract.Requires(!string.IsNullOrEmpty(nextSpeciesId), "nextSpeciesId must be non-empty");

            // Merged here so Check reports one line per material and consumption never double-counts a key
            // if the data ever lists the same (element, tier) twice.
            var merged = new List<MaterialStack>(materials.Count);
            var indexOfKey = new Dictionary<MaterialKey, int>();
            for (int i = 0; i < materials.Count; i++)
            {
                MaterialStack stack = materials[i];
                Contract.Requires(stack.Key.Tier >= 1 && stack.Qty >= 1, "every material needs a constructed key and Qty >= 1");
                if (indexOfKey.TryGetValue(stack.Key, out int at))
                {
                    long sum = (long)merged[at].Qty + stack.Qty;
                    Contract.Requires(sum <= int.MaxValue, "summed material quantity overflows int");
                    merged[at] = new MaterialStack(stack.Key, checked((int)sum));
                }
                else
                {
                    indexOfKey.Add(stack.Key, merged.Count);
                    merged.Add(stack);
                }
            }

            RequiredExp = requiredExp;
            _materials = merged.AsReadOnly();
            NextSpeciesId = nextSpeciesId;
        }

        /// <summary>Experience the creature must have reached (a threshold; not consumed).</summary>
        /// <remarks>Pre: none. Post: &gt;= 0; 0 for the final requirement. Side effects: none.</remarks>
        public int RequiredExp { get; }

        /// <summary>Materials consumed by a successful evolution.</summary>
        /// <remarks>Pre: none. Post: never null; unique keys; read-only; empty when IsFinal. Side effects: none.</remarks>
        public IReadOnlyList<MaterialStack> Materials => _materials ?? NoMaterials;

        /// <summary>CreatureDef id of the form it evolves into; null iff <see cref="IsFinal"/>.</summary>
        /// <remarks>Pre: none. Post: non-empty when !IsFinal. Side effects: none.</remarks>
        public string NextSpeciesId { get; }

        /// <summary>True for adults: there is no next form.</summary>
        /// <remarks>Pre: none. Post: true iff NextSpeciesId == null. Side effects: none.</remarks>
        public bool IsFinal => NextSpeciesId == null;

        /// <summary>Reads the requirement off a creature form.</summary>
        /// <remarks>
        /// Pre: def != null; for a non-adult, def.EvolutionExp &gt;= 0 and each material has Tier &gt;= 1, Qty &gt;= 1
        ///      and the Qty summed per (element, tier)
    ///      fits in int (all guaranteed by validate-data and the importer; a violation is a data bug and throws).
        /// Post: IsFinal iff def.EvolvesTo == null (adults); otherwise RequiredExp, Materials and NextSpeciesId mirror the def.
        /// Side effects: allocates.
        /// </remarks>
        public static EvolutionRequirement FromDef(CreatureDef def)
        {
            Contract.Requires(def != null, "def is null");
            if (def.EvolvesTo == null)
            {
                return default;
            }

            var stacks = new List<MaterialStack>(def.EvolutionMaterials.Count);
            for (int i = 0; i < def.EvolutionMaterials.Count; i++)
            {
                EvolutionMaterial m = def.EvolutionMaterials[i];
                stacks.Add(new MaterialStack(new MaterialKey(m.Element, m.Tier), m.Qty));
            }

            return new EvolutionRequirement(def.EvolutionExp, stacks, def.EvolvesTo.Id);
        }
    }
}
