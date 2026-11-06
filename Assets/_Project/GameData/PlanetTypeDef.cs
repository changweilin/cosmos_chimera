using System.Collections.Generic;
using UnityEngine;

namespace Cosmimera.GameData
{
    /// <summary>Imported planet type (structure only). REQ: docs/design/planet-world.md#星球類型.</summary>
    /// <remarks>Pre: filled by the importer. Post: TerrainTags >= 1; ElementBias 1..3, unique. Side effects: none.</remarks>
    public sealed class PlanetTypeDef : GameDef
    {
        [SerializeField] private PlanetSize size;
        [SerializeField] private List<string> terrainTags = new List<string>();
        [SerializeField] private List<Element> elementBias = new List<Element>();

        /// <summary>Size class.</summary>
        /// <remarks>Pre: none. Post: a defined PlanetSize. Side effects: none.</remarks>
        public PlanetSize Size => size;

        /// <summary>Terrain tag ids.</summary>
        /// <remarks>Pre: none. Post: Count >= 1. Side effects: none.</remarks>
        public IReadOnlyList<string> TerrainTags => terrainTags;

        /// <summary>Elements this planet type favours.</summary>
        /// <remarks>Pre: none. Post: 1..3 unique entries. Side effects: none.</remarks>
        public IReadOnlyList<Element> ElementBias => elementBias;

        internal void Set(PlanetSize size, List<string> terrainTags, List<Element> elementBias)
        {
            this.size = size;
            this.terrainTags = terrainTags;
            this.elementBias = elementBias;
        }
    }
}
