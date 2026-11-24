using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;

namespace Cosmimera.WorldGen
{
    /// <summary>Required companion gatekeeper battle before entering each numbered settlement on the lobby planet.</summary>
    /// <remarks>
    /// REQ: docs/design/lobby-meta.md#地球大廳.
    /// Pre: none. Post: defined gatekeeper role. Side effects: none.
    /// </remarks>
    public enum LobbyGatekeeper
    {
        None = 0,
        Friend = 1,
        Rival = 2,
    }

    /// <summary>
    /// Immutable constants and rules describing the fixed single-player lobby planet (mother planet, R = 2560 m).
    /// REQ: docs/design/lobby-meta.md#地球大廳.
    /// </summary>
    /// <remarks>
    /// Pre: none. Post: constants only; every derived dimension flows from <see cref="RadiusM"/>.
    /// Side effects: none.
    /// </remarks>
    public static class LobbyPlanet
    {
        /// <summary>Sea-level radius of the fixed lobby planet (m) — 2× Supergiant (82.4 km²).</summary>
        /// <remarks>Pre: none. Post: constant 2560 m within PlanetConfig.MaxSupportedRadius. Side effects: none.</remarks>
        public const float RadiusM = 2560f;

        /// <summary>Fixed deterministic seed of the single-player lobby planet.</summary>
        /// <remarks>Pre: none. Post: constant non-zero seed. Side effects: none.</remarks>
        public const ulong Seed = 20261121UL;

        /// <summary>Default planet type id used for the lobby planet's climate, sky and vegetation palette.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const string PlanetTypeId = "verdant_planet";

        /// <summary>Angular half-extent (rad) of each of the 24 landform stamps (≈ 717 m half-width at R = 2560 m).</summary>
        /// <remarks>Pre: none. Post: constant in (0, 0.6]. Side effects: none.</remarks>
        public const float StampHalfAngleRad = 0.28f;

        /// <summary>
        /// Fixed ordered roster of the 8 major settlements (#1..#8) on the lobby planet:
        /// #1 Town (landing) → #2 Tribe (food) → #3 City (5 trials) → #4 Town (migratory return) →
        /// #5 CityState (weapons) → #6 Town (armor) → #7 City (3 trials) → #8 Tribe (polar day/night, 2 trials &amp; rival finale).
        /// </summary>
        /// <remarks>
        /// Pre: none. Post: length 8 (3 Towns, 2 Tribes, 2 Cities, 1 CityState); index 0 is the landing Town (#1).
        /// Side effects: none.
        /// </remarks>
        public static readonly IReadOnlyList<SettlementKind> SettlementRoster = new[]
        {
            SettlementKind.Town,      // #1 1號小鎮
            SettlementKind.Tribe,     // #2 2號部落
            SettlementKind.City,      // #3 3號城市
            SettlementKind.Town,      // #4 4號小鎮
            SettlementKind.CityState, // #5 5號城邦
            SettlementKind.Town,      // #6 6號小鎮
            SettlementKind.City,      // #7 7號城市
            SettlementKind.Tribe,     // #8 8號部落
        };

        /// <summary>Returns the companion gatekeeper battle required before entering settlement <paramref name="settlementIndex"/> (0..7).</summary>
        /// <remarks>
        /// Pre: 0 &lt;= settlementIndex &lt; SettlementRoster.Count (8).
        /// Post: index 0 (#1 Town) → None (rival battle already took place in beginner quest);
        ///       indices 1/3/5/7 (#2/#4/#6/#8) → Friend; indices 2/4/6 (#3/#5/#7) → Rival.
        /// Side effects: none.
        /// </remarks>
        public static LobbyGatekeeper GatekeeperForSettlement(int settlementIndex)
        {
            Contract.Requires(settlementIndex >= 0 && settlementIndex < SettlementRoster.Count,
                "settlementIndex must be within 0..7");
            if (settlementIndex == 0)
            {
                return LobbyGatekeeper.None;
            }

            return (settlementIndex % 2) == 1 ? LobbyGatekeeper.Friend : LobbyGatekeeper.Rival;
        }

        /// <summary>Returns the 0-based hub settlement index around which <paramref name="terrainId"/> is placed on the lobby planet.</summary>
        /// <remarks>
        /// Pre: terrainId is a canonical or alias id in <see cref="AdvancedTerrains"/>.
        /// Post: 2 (#3 City) for metal/wood/water/fire/earth; 6 (#7 City) for ice/thunder/wind; 7 (#8 Tribe) for shadow/light.
        /// Side effects: none.
        /// </remarks>
        public static int HubSettlementIndexForTerrain(string terrainId)
        {
            Contract.Requires(!string.IsNullOrEmpty(terrainId), "terrainId must be non-empty");
            bool known = AdvancedTerrains.TryGet(terrainId, out AdvancedTerrainSpec spec);
            Contract.Requires(known, "unknown advanced terrain: " + terrainId);
            switch (spec.Id)
            {
                case "laboratory":
                case "rainforest":
                case "deep_sea":
                case "volcano":
                case "cave":
                    return 2; // #3 3號城市
                case "ice_cap":
                case "tower":
                case "space":
                    return 6; // #7 7號城市
                case "labyrinth":
                case "temple":
                    return 7; // #8 8號部落
                default:
                    throw new ArgumentOutOfRangeException(nameof(terrainId), spec.Id, "unmapped advanced terrain hub");
            }
        }
    }
}
