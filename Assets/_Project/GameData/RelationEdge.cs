using System;
using UnityEngine;

namespace Cosmimera.GameData
{
    /// <summary>One undirected relation edge as stored in data (A sorts before B by id).</summary>
    /// <remarks>Pre: none. Post: A and B non-null and distinct for imported data. Side effects: none.</remarks>
    [Serializable]
    public struct RelationEdge
    {
        [SerializeField] private CreatureDef a;
        [SerializeField] private CreatureDef b;
        [SerializeField] private RelationKind kind;
        [SerializeField] private string note;

        internal RelationEdge(CreatureDef a, CreatureDef b, RelationKind kind, string note)
        {
            this.a = a;
            this.b = b;
            this.kind = kind;
            this.note = note;
        }

        /// <summary>Endpoint whose id sorts first.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public CreatureDef A => a;

        /// <summary>Endpoint whose id sorts second.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public CreatureDef B => b;

        /// <summary>Relation kind.</summary>
        /// <remarks>Pre: none. Post: a defined RelationKind. Side effects: none.</remarks>
        public RelationKind Kind => kind;

        /// <summary>Optional author note.</summary>
        /// <remarks>Pre: none. Post: never null (empty when absent). Side effects: none.</remarks>
        public string Note => note ?? string.Empty;
    }

    /// <summary>A relation seen from one creature: the other endpoint plus the edge data.</summary>
    /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
    public readonly struct RelationLink
    {
        /// <summary>Creates a link.</summary>
        /// <remarks>Pre: other non-null. Post: fields set. Side effects: none.</remarks>
        public RelationLink(CreatureDef other, RelationKind kind, string note)
        {
            Other = other;
            Kind = kind;
            Note = note;
        }

        /// <summary>The creature on the other end.</summary>
        public CreatureDef Other { get; }

        /// <summary>Relation kind.</summary>
        public RelationKind Kind { get; }

        /// <summary>Author note (may be empty).</summary>
        public string Note { get; }
    }
}
