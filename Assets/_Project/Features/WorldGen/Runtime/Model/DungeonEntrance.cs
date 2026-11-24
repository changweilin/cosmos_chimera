using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Picks the one advanced terrain whose dungeon entrance a planet holds, and the landmark kind that marks it.
    /// REQ: docs/design/planet-world.md#進階地形地城 (Q-W41: one entrance per planet, on a landmark; Q-W43: weighted by
    /// the planet's element shares among the terrains its size allows, each terrain with its own landmark).
    /// </summary>
    /// <remarks>
    /// Determinism: own stream Pcg32(seed, EntranceStream), one draw, so it never shifts any other generator's sequence.
    /// </remarks>
    public static class DungeonEntrance
    {
        private const ulong EntranceStream = 0x444E5452UL;

        /// <summary>Landmark kind that marks the entrance of <paramref name="terrainId"/>.</summary>
        /// <remarks>
        /// Pre: terrainId names an advanced terrain (id or alias).
        /// Post: a distinct ground-reachable kind per terrain (space uses the SpaceElevator). Side effects: none.
        /// </remarks>
        public static LandmarkKind KindFor(string terrainId)
        {
            Contract.Requires(AdvancedTerrains.TryGet(terrainId, out AdvancedTerrainSpec spec), "unknown advanced terrain");
            switch (spec.Id)
            {
                case "laboratory": return LandmarkKind.ComputeCenter;
                case "rainforest": return LandmarkKind.GiantTree;
                case "deep_sea": return LandmarkKind.TrenchMouth;
                case "volcano": return LandmarkKind.Crater;
                case "cave": return LandmarkKind.MineShaft;
                case "ice_cap": return LandmarkKind.IceCastle;
                case "tower": return LandmarkKind.Tower;
                case "space": return LandmarkKind.SpaceElevator;
                case "labyrinth": return LandmarkKind.Labyrinth;
                default: return LandmarkKind.Temple;
            }
        }

        /// <summary>The advanced terrain whose entrance a planet of <paramref name="size"/> holds.</summary>
        /// <remarks>
        /// Pre: size defined.
        /// Post: false for Asteroid (no advanced terrain). Otherwise true and terrain appears on size; each eligible
        ///       terrain is drawn with weight elements.PercentOf(its element); when elements is null or no eligible
        ///       element has a share, the draw is uniform over the eligible terrains. Same inputs → same terrain.
        /// Side effects: none.
        /// </remarks>
        public static bool TryDraw(ulong seed, PlanetSize size, PlanetElements elements, out AdvancedTerrainSpec terrain)
        {
            Contract.Requires(Enum.IsDefined(typeof(PlanetSize), size), "size must be defined");
            terrain = default;
            if (size == PlanetSize.Asteroid)
            {
                return false;
            }

            var eligible = new List<AdvancedTerrainSpec>(10);
            uint total = 0;
            foreach (AdvancedTerrainSpec spec in AdvancedTerrains.All)
            {
                if (AdvancedTerrains.AppearsOn(spec, size))
                {
                    eligible.Add(spec);
                    total += elements != null ? (uint)elements.PercentOf(spec.Element) : 0u;
                }
            }

            var rng = new Pcg32(seed, EntranceStream);
            if (total == 0)
            {
                terrain = eligible[(int)rng.NextUInt((uint)eligible.Count)];
                return true;
            }

            uint pick = rng.NextUInt(total);
            foreach (AdvancedTerrainSpec spec in eligible)
            {
                uint w = (uint)elements.PercentOf(spec.Element);
                if (pick < w)
                {
                    terrain = spec;
                    return true;
                }

                pick -= w;
            }

            throw new ContractException("weighted draw fell through");
        }
    }
}
