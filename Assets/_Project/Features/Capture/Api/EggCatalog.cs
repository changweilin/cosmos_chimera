using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using UnityEngine;

namespace Cosmimera.Capture
{
    /// <summary>Egg definitions loaded from the egg data table (egg.schema.json). REQ: lobby-meta.md#收服與蛋.</summary>
    /// <remarks>
    /// Immutable after construction. Species ids are not resolved here (Capture does not know creatures); the data
    /// validator rejects dangling and non-juvenile species, and the hatching caller owns the lookup.
    /// </remarks>
    public sealed class EggCatalog
    {
        private readonly Dictionary<string, EggDef> _eggs = new Dictionary<string, EggDef>();

        /// <summary>Looks up an egg definition.</summary>
        /// <remarks>
        /// Pre: none. Post: true and def set iff the id is known; a retired id returns false so an old save keeps its
        /// egg instead of crashing. Side effects: none.
        /// </remarks>
        public bool TryGet(string eggId, out EggDef def)
        {
            def = null;
            return eggId != null && _eggs.TryGetValue(eggId, out def);
        }

        /// <summary>Number of definitions.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int Count => _eggs.Count;

        /// <summary>Parses the egg table (a JSON array of <c>{id, outcomes:[{species, weight}]}</c>).</summary>
        /// <remarks>
        /// Pre: json non-empty and already passed tools/validate-data (integers only).
        /// Post: one EggDef per entry; throws InvalidDataException on malformed input, duplicate id, empty or
        ///       duplicate-species pool, or non-positive weight. Side effects: allocates.
        /// </remarks>
        public static EggCatalog FromJson(string json)
        {
            Contract.Requires(!string.IsNullOrEmpty(json), "json required");
            Wrapper wrapper;
            try
            {
                // JsonUtility cannot read a top-level array; wrapping keeps the data file a plain schema-valid array.
                wrapper = JsonUtility.FromJson<Wrapper>("{\"eggs\":" + json + "}");
            }
            catch (ArgumentException e)
            {
                throw new System.IO.InvalidDataException("egg table is not valid JSON: " + e.Message, e);
            }

            if (wrapper?.eggs == null)
            {
                throw new System.IO.InvalidDataException("egg table must be a JSON array");
            }

            var catalog = new EggCatalog();
            foreach (EggDto dto in wrapper.eggs)
            {
                if (dto == null || string.IsNullOrEmpty(dto.id) || dto.outcomes == null || dto.outcomes.Length == 0)
                {
                    throw new System.IO.InvalidDataException("egg entry needs an id and at least one outcome");
                }

                var outcomes = new List<EggOutcome>();
                var seen = new HashSet<string>();
                foreach (OutcomeDto o in dto.outcomes)
                {
                    if (o == null || string.IsNullOrEmpty(o.species) || o.weight <= 0 || !seen.Add(o.species))
                    {
                        throw new System.IO.InvalidDataException("egg '" + dto.id + "' has an invalid or duplicate outcome");
                    }

                    outcomes.Add(new EggOutcome(o.species, o.weight));
                }

                if (catalog._eggs.ContainsKey(dto.id))
                {
                    throw new System.IO.InvalidDataException("duplicate egg id '" + dto.id + "'");
                }

                catalog._eggs.Add(dto.id, new EggDef(dto.id, outcomes));
            }

            return catalog;
        }

        [Serializable] private sealed class Wrapper { public EggDto[] eggs; }

        [Serializable] private sealed class EggDto
        {
            public string id;
            public OutcomeDto[] outcomes;
        }

        [Serializable] private sealed class OutcomeDto
        {
            public string species;
            public int weight;
        }
    }
}
