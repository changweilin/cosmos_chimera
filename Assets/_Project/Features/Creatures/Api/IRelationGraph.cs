namespace Cosmimera.Creatures
{
    /// <summary>The creature relation table seen as a yes/no question. REQ: docs/design/creatures.md#關係表.</summary>
    /// <remarks>
    /// Pre: implementations are read-only views over data.
    /// Post: see <see cref="AreRelated"/>.
    /// Side effects: none.
    /// </remarks>
    public interface IRelationGraph
    {
        /// <summary>Whether the relation table has an edge (of any kind) between two creature forms.</summary>
        /// <remarks>
        /// Pre: both ids non-null. Edges are per form id, so the ids must be the CURRENT form of each creature.
        /// Post: symmetric — AreRelated(a, b) == AreRelated(b, a); false when no edge exists, including a == b
        ///       (the table has no self-edges); never throws for ids it does not know (returns false).
        /// Side effects: none (may build a cache on first use).
        /// </remarks>
        bool AreRelated(string speciesIdA, string speciesIdB);
    }
}
