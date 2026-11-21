using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using UnityEngine;

namespace Cosmimera.GameData
{
    /// <summary>Combat move or trait paired with a non-none <see cref="FieldUtility"/>. REQ: Data/field-utilities/field-utilities.json.</summary>
    /// <remarks>Pre: constructed by the importer. Post: exactly one of Move or Trait is non-null. Side effects: none.</remarks>
    [Serializable]
    public struct FieldUtilityCombatEntry
    {
        [SerializeField] private FieldUtility utility;
        [SerializeField] private MoveDef move;
        [SerializeField] private TraitDef trait;

        internal FieldUtilityCombatEntry(FieldUtility utility, MoveDef move, TraitDef trait)
        {
            this.utility = utility;
            this.move = move;
            this.trait = trait;
        }

        /// <summary>The field utility this entry belongs to.</summary>
        /// <remarks>Pre: none. Post: not FieldUtility.None. Side effects: none.</remarks>
        public FieldUtility Utility => utility;

        /// <summary>Combat move for move-kind utilities, else null.</summary>
        /// <remarks>Pre: none. Post: non-null iff utility is move-kind. Side effects: none.</remarks>
        public MoveDef Move => move;

        /// <summary>Combat trait for trait-kind utilities, else null.</summary>
        /// <remarks>Pre: none. Post: non-null iff utility is trait-kind. Side effects: none.</remarks>
        public TraitDef Trait => trait;
    }

    /// <summary>Registry of every imported definition, queried by id. REQ: docs/architecture/overview.md#資料流.</summary>
    /// <remarks>
    /// Pre: the asset is written only by the data importer; ids are globally unique (tools/validate-data).
    /// Post: lookups reflect the contents of the last import.
    /// Side effects: builds an in-memory index on first query (main thread only, like all Unity objects).
    /// </remarks>
    public sealed class GameDatabase : ScriptableObject
    {
        private static readonly RelationLink[] NoLinks = new RelationLink[0];

        [SerializeField] private List<GameDef> defs = new List<GameDef>();
        [SerializeField] private List<RelationEdge> relations = new List<RelationEdge>();
        [SerializeField] private List<FieldUtilityCombatEntry> fieldUtilities = new List<FieldUtilityCombatEntry>();

        [NonSerialized] private Dictionary<string, GameDef> _byId;
        [NonSerialized] private Dictionary<string, List<RelationLink>> _linksById;
        [NonSerialized] private Dictionary<FieldUtility, FieldUtilityCombatEntry> _fieldUtilitiesByUtility;

        /// <summary>All definitions, ordered by kind then id.</summary>
        /// <remarks>Pre: none. Post: no null entries for an importer-written asset. Side effects: none.</remarks>
        public IReadOnlyList<GameDef> All => defs;

        /// <summary>All relation edges as stored (each undirected edge once).</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public IReadOnlyList<RelationEdge> Relations => relations;

        /// <summary>Finds the definition with <paramref name="id"/> if it exists and is a <typeparamref name="T"/>.</summary>
        /// <remarks>
        /// Pre: id is not null.
        /// Post: returns true iff a definition with that id exists and is a T; def is null when false.
        /// Side effects: builds the index on first call.
        /// </remarks>
        public bool TryGet<T>(string id, out T def) where T : GameDef
        {
            Contract.Requires(id != null, "id must not be null");
            EnsureIndex();
            if (_byId.TryGetValue(id, out GameDef found) && found is T typed)
            {
                def = typed;
                return true;
            }

            def = null;
            return false;
        }

        /// <summary>Relations of the creature with <paramref name="creatureId"/>, from that creature's side.</summary>
        /// <remarks>
        /// Edges are stored once (a &lt; b) so both ends cannot disagree (data-content.md); this expands them.
        /// Pre: creatureId is not null.
        /// Post: every returned link's Other is the opposite endpoint; empty (never null) when there are none.
        /// Side effects: builds the index on first call.
        /// </remarks>
        public IReadOnlyList<RelationLink> GetRelations(string creatureId)
        {
            Contract.Requires(creatureId != null, "creatureId must not be null");
            EnsureIndex();
            return _linksById.TryGetValue(creatureId, out List<RelationLink> links) ? links : (IReadOnlyList<RelationLink>)NoLinks;
        }

        /// <summary>Finds the combat <see cref="MoveDef"/> paired with <paramref name="utility"/>, if any.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: returns true iff field-utilities data was imported and utility is a move-kind utility with a combat move.
        /// Side effects: builds the index on first call.
        /// </remarks>
        public bool TryGetFieldUtilityMove(FieldUtility utility, out MoveDef move)
        {
            EnsureIndex();
            if (_fieldUtilitiesByUtility.TryGetValue(utility, out FieldUtilityCombatEntry entry) && entry.Move != null)
            {
                move = entry.Move;
                return true;
            }

            move = null;
            return false;
        }

        /// <summary>Finds the combat <see cref="TraitDef"/> paired with <paramref name="utility"/>, if any.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: returns true iff field-utilities data was imported and utility is a trait-kind utility with a combat trait.
        /// Side effects: builds the index on first call.
        /// </remarks>
        public bool TryGetFieldUtilityTrait(FieldUtility utility, out TraitDef trait)
        {
            EnsureIndex();
            if (_fieldUtilitiesByUtility.TryGetValue(utility, out FieldUtilityCombatEntry entry) && entry.Trait != null)
            {
                trait = entry.Trait;
                return true;
            }

            trait = null;
            return false;
        }

        internal void SetContents(List<GameDef> newDefs, List<RelationEdge> newRelations,
            List<FieldUtilityCombatEntry> newFieldUtilities = null)
        {
            defs = newDefs;
            relations = newRelations;
            fieldUtilities = newFieldUtilities ?? new List<FieldUtilityCombatEntry>();
            _byId = null;
            _linksById = null;
            _fieldUtilitiesByUtility = null;
        }

        private void OnEnable()
        {
            // Unity may deserialize new contents into this same instance (reimport, undo); never trust an old index.
            _byId = null;
            _linksById = null;
            _fieldUtilitiesByUtility = null;
        }

        private void EnsureIndex()
        {
            if (_byId != null)
            {
                return;
            }

            var byId = new Dictionary<string, GameDef>(defs.Count, StringComparer.Ordinal);
            foreach (GameDef def in defs)
            {
                // A null here means an asset was deleted by hand; failing beats silently answering "not found".
                Contract.Requires(def != null, "GameDatabase has a missing definition; re-run the data importer");
                Contract.Requires(!byId.ContainsKey(def.Id), "duplicate id '" + def.Id + "'; re-run tools/validate-data");
                byId.Add(def.Id, def);
            }

            var links = new Dictionary<string, List<RelationLink>>(StringComparer.Ordinal);
            foreach (RelationEdge edge in relations)
            {
                Contract.Requires(edge.A != null && edge.B != null, "relation endpoint missing; re-run the data importer");
                AddLink(links, edge.A.Id, new RelationLink(edge.B, edge.Kind, edge.Note));
                AddLink(links, edge.B.Id, new RelationLink(edge.A, edge.Kind, edge.Note));
            }

            var byUtility = new Dictionary<FieldUtility, FieldUtilityCombatEntry>(fieldUtilities.Count);
            foreach (FieldUtilityCombatEntry entry in fieldUtilities)
            {
                byUtility[entry.Utility] = entry;
            }

            _byId = byId;
            _linksById = links;
            _fieldUtilitiesByUtility = byUtility;
        }

        private static void AddLink(Dictionary<string, List<RelationLink>> links, string id, RelationLink link)
        {
            if (!links.TryGetValue(id, out List<RelationLink> list))
            {
                list = new List<RelationLink>();
                links.Add(id, list);
            }

            list.Add(link);
        }
    }
}
