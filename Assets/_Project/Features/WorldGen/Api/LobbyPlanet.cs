using System.Collections.Generic;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Immutable constants describing the fixed single-player lobby planet (main world).
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

        /// <summary>Fixed roster of the 8 major settlements on the lobby planet: 1 CityState, 2 Cities, 3 Towns, 2 Tribes.</summary>
        /// <remarks>
        /// Pre: none. Post: length 8; index 0 is the landing CityState.
        /// Side effects: none.
        /// </remarks>
        public static readonly IReadOnlyList<SettlementKind> SettlementRoster = new[]
        {
            SettlementKind.CityState,
            SettlementKind.City,
            SettlementKind.City,
            SettlementKind.Town,
            SettlementKind.Town,
            SettlementKind.Town,
            SettlementKind.Tribe,
            SettlementKind.Tribe,
        };
    }
}
