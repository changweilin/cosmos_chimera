using System;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Creatures
{
    /// <summary>Identity of one stackable evolution material: element + tier. REQ: docs/design/creatures.md#進化.</summary>
    /// <remarks>
    /// Pre: construct through the public constructor. Post: immutable; value equality over (Element, Tier).
    /// Side effects: none.
    /// </remarks>
    public readonly struct MaterialKey : IEquatable<MaterialKey>
    {
        /// <summary>Creates a key.</summary>
        /// <remarks>
        /// Pre: element is a defined Element; tier &gt;= 1 (matches EvolutionMaterial.Tier).
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public MaterialKey(Element element, int tier)
        {
            Contract.Requires(Enum.IsDefined(typeof(Element), element), "element must be a defined Element");
            Contract.Requires(tier >= 1, "tier must be >= 1");
            Element = element;
            Tier = tier;
        }

        /// <summary>Material element.</summary>
        /// <remarks>Pre: none. Post: a defined Element. Side effects: none.</remarks>
        public Element Element { get; }

        /// <summary>Material tier.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1 unless default(MaterialKey). Side effects: none.</remarks>
        public int Tier { get; }

        /// <summary>Value equality.</summary>
        /// <remarks>Pre: none. Post: true iff Element and Tier match. Side effects: none.</remarks>
        public bool Equals(MaterialKey other) => Element == other.Element && Tier == other.Tier;

        /// <summary>Value equality against a boxed key.</summary>
        /// <remarks>Pre: none. Post: true iff obj is an equal MaterialKey. Side effects: none.</remarks>
        public override bool Equals(object obj) => obj is MaterialKey other && Equals(other);

        /// <summary>Hash consistent with <see cref="Equals(MaterialKey)"/>.</summary>
        /// <remarks>Pre: none. Post: equal keys give equal hashes. Side effects: none.</remarks>
        public override int GetHashCode() => ((int)Element * 397) ^ Tier;

        /// <summary>Debug text such as <c>Fire/T2</c>.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: allocates.</remarks>
        public override string ToString() => Element + "/T" + Tier;
    }

    /// <summary>A quantity of one material: a requirement line or an inventory stack.</summary>
    /// <remarks>
    /// Pre: construct through the public constructor. Post: immutable; Qty &gt;= 1 unless default(MaterialStack).
    /// Side effects: none.
    /// </remarks>
    public readonly struct MaterialStack
    {
        /// <summary>Creates a stack.</summary>
        /// <remarks>Pre: key.Tier &gt;= 1; qty &gt;= 1. Post: properties equal the arguments. Side effects: none.</remarks>
        public MaterialStack(MaterialKey key, int qty)
        {
            Contract.Requires(key.Tier >= 1, "key must be a constructed MaterialKey");
            Contract.Requires(qty >= 1, "qty must be >= 1");
            Key = key;
            Qty = qty;
        }

        /// <summary>Which material.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public MaterialKey Key { get; }

        /// <summary>How many.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1 unless default(MaterialStack). Side effects: none.</remarks>
        public int Qty { get; }
    }
}
