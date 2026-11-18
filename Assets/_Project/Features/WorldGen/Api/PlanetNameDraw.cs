using System;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;

namespace Cosmimera.WorldGen
{
    /// <summary>Deterministic indices into a planet type's weather and terrain affix libraries.</summary>
    /// <remarks>Pre: constructed by <see cref="PlanetNameDraw.Draw"/>. Post: non-negative indices. Side effects: none.</remarks>
    public readonly struct PlanetNameIndices
    {
        /// <summary>Creates a pair of affix indices.</summary>
        /// <remarks>Pre: weatherIndex >= 0; terrainIndex >= 0. Post: properties return the arguments. Side effects: none.</remarks>
        public PlanetNameIndices(int weatherIndex, int terrainIndex)
        {
            Contract.Requires(weatherIndex >= 0 && terrainIndex >= 0, "affix indices must be >= 0");
            WeatherIndex = weatherIndex;
            TerrainIndex = terrainIndex;
        }

        /// <summary>Index into the planet type's weather adjective affix list.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int WeatherIndex { get; }

        /// <summary>Index into the planet type's terrain noun affix list.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int TerrainIndex { get; }
    }

    /// <summary>
    /// Deterministically draws a planet's weather adjective and terrain noun affix indices from its type and seed
    /// so every client formats the same "XX的OO星" name.
    /// </summary>
    /// <remarks>Pre: none. Post: pure and deterministic across platforms. Side effects: none.</remarks>
    public static class PlanetNameDraw
    {
        private const ulong StreamId = 0x504E414Dul; // "PNAM"

        /// <summary>Draws weather and terrain affix indices for a planet.</summary>
        /// <remarks>
        /// Pre: weatherCount > 0; terrainCount > 0.
        /// Post: WeatherIndex in [0, weatherCount), TerrainIndex in [0, terrainCount); identical inputs yield identical output.
        /// Side effects: none.
        /// </remarks>
        public static PlanetNameIndices Draw(string planetTypeId, ulong seed, int weatherCount, int terrainCount)
        {
            Contract.Requires(weatherCount > 0, "weatherCount must be > 0");
            Contract.Requires(terrainCount > 0, "terrainCount must be > 0");
            ulong typeSalt = HashId(planetTypeId);
            var rng = new Pcg32(seed ^ typeSalt, StreamId);
            int weatherIndex = (int)rng.NextUInt((uint)weatherCount);
            int terrainIndex = (int)rng.NextUInt((uint)terrainCount);
            return new PlanetNameIndices(weatherIndex, terrainIndex);
        }

        // FNV-1a 64-bit so identical planetTypeId strings hash identically across .NET runtimes (unlike string.GetHashCode).
        private static ulong HashId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return 0ul;
            }

            ulong hash = 14695981039346656037ul;
            for (int i = 0; i < id.Length; i++)
            {
                hash ^= id[i];
                hash *= 1099511628211ul;
            }

            return hash;
        }
    }
}
