using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;

namespace Cosmimera.WorldGen
{
    /// <summary>How an advanced terrain dungeon is realized.</summary>
    /// <remarks>Pre: none. Post: stable enum values. Side effects: none.</remarks>
    public enum DungeonKind
    {
        /// <summary>Not a procedural dungeon; open general procedural terrain.</summary>
        None = 0,
        /// <summary>Enclosed maze carved on the planet surface (DungeonPlanner + PlanetWalls).</summary>
        SurfaceMaze = 1,
        /// <summary>Standalone room-module procedural dungeon entered from a surface portal (P3-07).</summary>
        RoomModule = 2,
    }

    /// <summary>
    /// Procedural morphology, obstacle density, and player/creature action modifiers for a landform or advanced terrain.
    /// REQ: docs/design/planet-world.md#1-骨架層程序化地貌, #地貌行動影響, #進階地形地城.
    /// </summary>
    /// <remarks>Pre: constructed via <see cref="TerrainProfiles"/>. Post: immutable value. Side effects: none.</remarks>
    public readonly struct TerrainProfile
    {
        internal TerrainProfile(string id, DungeonKind dungeon, float scatterDensity,
            float moveSpeedMultiplier, float sprintSpeedMultiplier, float groundAccelerationMultiplier,
            float cooldownMultiplier)
        {
            Id = id;
            Dungeon = dungeon;
            ScatterDensity = scatterDensity;
            MoveSpeedMultiplier = moveSpeedMultiplier;
            SprintSpeedMultiplier = sprintSpeedMultiplier;
            GroundAccelerationMultiplier = groundAccelerationMultiplier;
            CooldownMultiplier = cooldownMultiplier;
        }

        /// <summary>Canonical landform or advanced terrain id.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Whether this terrain is one of the 10 elemental advanced procedural dungeons.</summary>
        /// <remarks>Pre: none. Post: true iff <see cref="Dungeon"/> != <see cref="DungeonKind.None"/>. Side effects: none.</remarks>
        public bool IsAdvancedDungeon => Dungeon != DungeonKind.None;

        /// <summary>Dungeon realization mode (<see cref="DungeonKind.None"/> for general open terrain).</summary>
        /// <remarks>Pre: none. Post: defined enum value. Side effects: none.</remarks>
        public DungeonKind Dungeon { get; }

        /// <summary>Scatter obstacle placement probability in [0, 1] (low for open plains/ice/desert, high for forest/mountain).</summary>
        /// <remarks>Pre: none. Post: 0 &lt;= value &lt;= 1. Side effects: none.</remarks>
        public float ScatterDensity { get; }

        /// <summary>Multiplier on base walk/creature move speed (&gt; 0).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float MoveSpeedMultiplier { get; }

        /// <summary>Multiplier on player sprint speed (&gt; 0).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float SprintSpeedMultiplier { get; }

        /// <summary>Multiplier on grounded acceleration (&gt; 0; low values produce low-friction ice sliding).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float GroundAccelerationMultiplier { get; }

        /// <summary>Multiplier on move cooldown / environmental stamina drain (&gt; 0).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float CooldownMultiplier { get; }
    }

    /// <summary>
    /// Lookup catalog for general procedural landforms and the 10 elemental advanced terrain dungeons.
    /// REQ: docs/design/planet-world.md.
    /// </summary>
    /// <remarks>Pre: none. Post: pure and deterministic. Side effects: none.</remarks>
    public static class TerrainProfiles
    {
        private static readonly TerrainProfile DefaultProfile = new TerrainProfile(
            "grassland", DungeonKind.None, 0.15f, 1.0f, 1.0f, 1.0f, 1.0f);

        private static readonly Dictionary<string, TerrainProfile> ById = new Dictionary<string, TerrainProfile>(StringComparer.OrdinalIgnoreCase)
        {
            // ---- General open / low-obstacle landforms ----
            ["grassland"] = DefaultProfile,
            ["ice_field"] = new TerrainProfile("ice_field", DungeonKind.None, 0.16f, 1.05f, 1.0f, 0.25f, 1.10f),
            ["desert"] = new TerrainProfile("desert", DungeonKind.None, 0.14f, 0.88f, 0.85f, 0.80f, 1.10f),
            ["river_valley"] = new TerrainProfile("river_valley", DungeonKind.None, 0.35f, 1.0f, 1.0f, 1.0f, 1.0f),
            ["plateau"] = new TerrainProfile("plateau", DungeonKind.None, 0.30f, 1.0f, 1.0f, 1.0f, 1.0f),

            // ---- Wetland / aquatic general landforms ----
            ["swamp"] = new TerrainProfile("swamp", DungeonKind.None, 0.45f, 0.75f, 0.78f, 0.75f, 1.05f),
            ["archipelago"] = new TerrainProfile("archipelago", DungeonKind.None, 0.32f, 0.95f, 0.95f, 1.0f, 1.0f),
            ["fjord"] = new TerrainProfile("fjord", DungeonKind.None, 0.48f, 0.92f, 0.95f, 0.90f, 1.05f),

            // ---- Dense-obstacle / rugged general landforms ----
            ["forest"] = new TerrainProfile("forest", DungeonKind.None, 0.75f, 0.96f, 0.95f, 1.0f, 1.0f),
            ["mountain"] = new TerrainProfile("mountain", DungeonKind.None, 0.78f, 0.85f, 0.88f, 0.90f, 1.10f),
            ["canyon"] = new TerrainProfile("canyon", DungeonKind.None, 0.70f, 0.88f, 0.90f, 0.95f, 1.05f),
            ["rift_valley"] = new TerrainProfile("rift_valley", DungeonKind.None, 0.52f, 0.90f, 0.92f, 0.95f, 1.05f),
            ["volcano_foothills"] = new TerrainProfile("volcano_foothills", DungeonKind.None, 0.58f, 0.90f, 0.92f, 0.95f, 1.12f),
            ["cavern_outer"] = new TerrainProfile("cavern_outer", DungeonKind.None, 0.72f, 0.90f, 0.92f, 1.0f, 1.0f),
            ["eternal_day"] = new TerrainProfile("eternal_day", DungeonKind.None, 0.35f, 1.0f, 1.02f, 1.0f, 0.95f),
            ["eternal_night"] = new TerrainProfile("eternal_night", DungeonKind.None, 0.48f, 0.94f, 0.95f, 0.95f, 1.08f),

            // ---- 10 elemental advanced terrains (procedural dungeons) ----
            ["laboratory"] = new TerrainProfile("laboratory", DungeonKind.RoomModule, 0.35f, 1.0f, 1.0f, 1.0f, 1.0f),
            ["rainforest"] = new TerrainProfile("rainforest", DungeonKind.SurfaceMaze, 0.82f, 0.85f, 0.85f, 0.90f, 1.05f),
            ["deep_sea"] = new TerrainProfile("deep_sea", DungeonKind.RoomModule, 0.30f, 0.72f, 0.75f, 0.65f, 1.15f),
            ["volcano"] = new TerrainProfile("volcano", DungeonKind.SurfaceMaze, 0.60f, 0.86f, 0.88f, 0.90f, 1.18f),
            ["cave"] = new TerrainProfile("cave", DungeonKind.RoomModule, 0.70f, 0.90f, 0.90f, 1.0f, 1.0f),
            ["ice_cap"] = new TerrainProfile("ice_cap", DungeonKind.SurfaceMaze, 0.20f, 1.05f, 1.0f, 0.20f, 1.20f),
            ["tower"] = new TerrainProfile("tower", DungeonKind.RoomModule, 0.30f, 1.0f, 1.0f, 1.0f, 0.90f),
            ["space"] = new TerrainProfile("space", DungeonKind.RoomModule, 0.25f, 1.08f, 1.10f, 0.75f, 1.0f),
            ["labyrinth"] = new TerrainProfile("labyrinth", DungeonKind.SurfaceMaze, 0.45f, 0.95f, 0.95f, 1.0f, 1.10f),
            ["temple"] = new TerrainProfile("temple", DungeonKind.RoomModule, 0.35f, 1.0f, 1.0f, 1.0f, 0.92f),
        };

        private static readonly string[] AdvancedIds =
        {
            "laboratory", "rainforest", "deep_sea", "volcano", "cave",
            "ice_cap", "tower", "space", "labyrinth", "temple",
        };

        /// <summary>The 10 canonical elemental advanced terrain ids (only these are procedural dungeons).</summary>
        /// <remarks>Pre: none. Post: 10 distinct ids. Side effects: none.</remarks>
        public static IReadOnlyList<string> AdvancedTerrainIds => AdvancedIds;

        /// <summary>Returns the <see cref="TerrainProfile"/> for <paramref name="id"/>, or the default grassland profile if unknown.</summary>
        /// <remarks>Pre: none. Post: valid <see cref="TerrainProfile"/> with non-empty Id. Side effects: none.</remarks>
        public static TerrainProfile For(string id) =>
            !string.IsNullOrEmpty(id) && ById.TryGetValue(id, out TerrainProfile p) ? p : DefaultProfile;

        /// <summary>Maps a planet type id to its primary general surface <see cref="TerrainProfile"/>.</summary>
        /// <remarks>Pre: none. Post: non-dungeon general <see cref="TerrainProfile"/>. Side effects: none.</remarks>
        public static TerrainProfile ForPlanetType(string planetTypeId) => planetTypeId switch
        {
            "verdant_planet" => ById["forest"],
            "lava_planet" => ById["volcano_foothills"],
            "ice_crystal_planet" => ById["ice_field"],
            "storm_planet" => ById["plateau"],
            "mineral_planet" => ById["canyon"],
            "twilight_planet" => ById["eternal_night"],
            _ => DefaultProfile,
        };

        /// <summary>True when <paramref name="id"/> is one of the 10 elemental advanced dungeon terrains.</summary>
        /// <remarks>Pre: none. Post: matches <see cref="TerrainProfile.IsAdvancedDungeon"/>. Side effects: none.</remarks>
        public static bool IsAdvancedDungeon(string id) =>
            !string.IsNullOrEmpty(id) && ById.TryGetValue(id, out TerrainProfile p) && p.IsAdvancedDungeon;
    }
}
