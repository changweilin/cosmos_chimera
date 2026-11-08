using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Creatures
{
    /// <summary><see cref="IRelationGraph"/> over the imported relation table (<see cref="GameDatabase.GetRelations"/>).</summary>
    /// <remarks>
    /// Pre: the database outlives the adapter and is only used on the main thread (it is a ScriptableObject).
    /// Post: answers reflect the database's last import.
    /// Side effects: the database builds its relation index on first query.
    /// </remarks>
    public sealed class GameDatabaseRelationGraph : IRelationGraph
    {
        private readonly GameDatabase _database;

        /// <summary>Wraps a database.</summary>
        /// <remarks>Pre: database != null. Post: queries read it live. Side effects: none.</remarks>
        public GameDatabaseRelationGraph(GameDatabase database)
        {
            Contract.Requires(database != null, "database is null");
            _database = database;
        }

        /// <summary>Whether an edge of any kind joins the two forms.</summary>
        /// <remarks>
        /// Pre: both ids non-null.
        /// Post: see <see cref="IRelationGraph.AreRelated"/>; ids unknown to the table return false.
        /// Side effects: none beyond the database's lazy index.
        /// </remarks>
        public bool AreRelated(string speciesIdA, string speciesIdB)
        {
            Contract.Requires(speciesIdA != null && speciesIdB != null, "species ids must not be null");
            // GetRelations expands each stored edge to both endpoints, so looking from A's side is enough.
            IReadOnlyList<RelationLink> links = _database.GetRelations(speciesIdA);
            for (int i = 0; i < links.Count; i++)
            {
                if (links[i].Other.Id == speciesIdB)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary><see cref="ISpeciesLookup"/> over the imported creature definitions.</summary>
    /// <remarks>
    /// Pre: the database outlives the adapter and is only used on the main thread.
    /// Post: answers reflect the database's last import.
    /// Side effects: the database builds its id index on first query.
    /// </remarks>
    public sealed class GameDatabaseSpeciesLookup : ISpeciesLookup
    {
        private readonly GameDatabase _database;

        /// <summary>Wraps a database.</summary>
        /// <remarks>Pre: database != null. Post: queries read it live. Side effects: none.</remarks>
        public GameDatabaseSpeciesLookup(GameDatabase database)
        {
            Contract.Requires(database != null, "database is null");
            _database = database;
        }

        /// <summary>Finds a creature form by id.</summary>
        /// <remarks>
        /// Pre: speciesId non-null; for a found creature, SignatureMove and Trait are non-null (CreatureDef Post).
        /// Post: true iff a CreatureDef with that id exists (ids of other kinds do not match).
        /// Side effects: none beyond the database's lazy index.
        /// </remarks>
        public bool TryGet(string speciesId, out SpeciesInfo info)
        {
            Contract.Requires(speciesId != null, "speciesId is null");
            if (!_database.TryGet(speciesId, out CreatureDef def))
            {
                info = default;
                return false;
            }

            Contract.Requires(def.SignatureMove != null && def.Trait != null, "creature '" + speciesId + "' has no signature move or trait; re-run the data importer");
            info = new SpeciesInfo(def.Id, def.SignatureMove.Id, def.Trait.Id, def.Stage);
            return true;
        }
    }
}
