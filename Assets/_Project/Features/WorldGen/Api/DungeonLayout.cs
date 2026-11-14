using System.Collections.Generic;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>What a region is in the planet-as-dungeon layout (planet-world.md#地表地牢).</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum RoomRole
    {
        /// <summary>Ordinary room.</summary>
        Normal = 0,
        /// <summary>Where the player lands.</summary>
        Landing = 1,
        /// <summary>Boss room: holds the walled arena around the boss, whose gate is locked.</summary>
        Boss = 2,
        /// <summary>Holds the key: the elite whose knock-out opens the arena gate.</summary>
        Key = 3,
        /// <summary>Dead end (one way in) holding treasure.</summary>
        Treasure = 4,
    }

    /// <summary>One opening in the wall between two rooms.</summary>
    /// <remarks>Pre: produced by DungeonPlanner. Post: immutable. Side effects: none.</remarks>
    public readonly struct DungeonGate
    {
        internal DungeonGate(float3 center, int regionA, int regionB, int road)
        {
            Center = center;
            RegionA = regionA;
            RegionB = regionB;
            Road = road;
        }

        /// <summary>Centre of the opening (unit direction) on the border between the two rooms.</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 Center { get; }

        /// <summary>Smaller region index of the pair.</summary>
        /// <remarks>Pre: none. Post: valid index &lt; RegionB. Side effects: none.</remarks>
        public int RegionA { get; }

        /// <summary>Larger region index of the pair.</summary>
        /// <remarks>Pre: none. Post: valid index > RegionA. Side effects: none.</remarks>
        public int RegionB { get; }

        /// <summary>Index in RunLayout.Roads of the road through this gate, or −1 for a shortcut without a road.</summary>
        /// <remarks>Pre: none. Post: −1 or a valid road index. Side effects: none.</remarks>
        public int Road { get; }
    }

    /// <summary>The planet surface as a dungeon: rooms (regions), walls on their borders, gates, lock and key.</summary>
    /// <remarks>
    /// Pre: produced by <see cref="DungeonPlanner.Generate"/>. Post: immutable; same inputs → same layout.
    /// Side effects: none.
    /// </remarks>
    public sealed class DungeonLayout
    {
        private readonly bool[] _open;
        private readonly bool[] _connected;

        internal DungeonLayout(int opennessPercent, RoomRole[] rooms, DungeonGate[] gates, bool[] open, bool[] connected,
            float3x2[] corridors, int keySpawn, float3 arenaCenter, float3 arenaGate, float passInnerM, float passOuterM)
        {
            OpennessPercent = opennessPercent;
            Rooms = rooms;
            Gates = gates;
            _open = open;
            _connected = connected;
            Corridors = corridors;
            KeySpawn = keySpawn;
            ArenaCenter = arenaCenter;
            ArenaGate = arenaGate;
            PassInnerM = passInnerM;
            PassOuterM = passOuterM;
        }

        /// <summary>How open this planet is: 0 = a maze of narrow passes, 100 = many shortcuts, wide passes, missing walls.</summary>
        /// <remarks>Pre: none. Post: 0..100, drawn per planet. Side effects: none.</remarks>
        public int OpennessPercent { get; }

        /// <summary>Role of each region.</summary>
        /// <remarks>Pre: none. Post: Count == plan.Regions.Count; exactly one Landing and one Boss. Side effects: none.</remarks>
        public IReadOnlyList<RoomRole> Rooms { get; }

        /// <summary>Gates: road crossings in road order, then shortcuts.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public IReadOnlyList<DungeonGate> Gates { get; }

        /// <summary>Whether the boss stands in a walled arena whose gate is locked until the key elite is knocked out.</summary>
        /// <remarks>Pre: none. Post: KeySpawn ≥ 0. Side effects: none.</remarks>
        public bool HasLock => KeySpawn >= 0;

        /// <summary>Spawn index of the elite that opens the arena gate, or −1 when this planet has no lock.</summary>
        /// <remarks>
        /// Pre: none. Post: −1 or the plan's DefeatElite target. −1 only when the arena cannot be sealed cleanly: the boss
        ///      road does not enter it exactly once, another road, the key elite or the landing point is too close to it.
        /// Side effects: none.
        /// </remarks>
        public int KeySpawn { get; }

        /// <summary>Centre of the boss arena: the boss spawn direction.</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 ArenaCenter { get; }

        /// <summary>Centre of the arena gate, where the boss road crosses the arena wall.</summary>
        /// <remarks>Pre: HasLock. Post: unit length, DungeonPlanner.ArenaRadiusM from ArenaCenter. Side effects: none.</remarks>
        public float3 ArenaGate { get; }

        /// <summary>Half width (m) of the strip along a road or shortcut inside which room walls are fully removed.</summary>
        /// <remarks>Pre: none. Post: > 0; grows with OpennessPercent. Side effects: none.</remarks>
        public float PassInnerM { get; }

        /// <summary>Distance (m) from a road or shortcut at which room walls are back to full height.</summary>
        /// <remarks>Pre: none. Post: > PassInnerM. Side effects: none.</remarks>
        public float PassOuterM { get; }

        /// <summary>Whether the border between two rooms has no wall at all.</summary>
        /// <remarks>Pre: valid region indices. Post: symmetric; false for a == b; implies IsConnected. Side effects: none.</remarks>
        public bool IsOpenBorder(int a, int b) => _open[a * Rooms.Count + b];

        /// <summary>Whether two rooms are joined directly by a gate or an open border.</summary>
        /// <remarks>Pre: valid region indices. Post: symmetric; false for a == b. Side effects: none.</remarks>
        public bool IsConnected(int a, int b) => _connected[a * Rooms.Count + b];

        internal bool[] OpenPairs => _open;

        // Segments along which room walls are removed: the stretches of each road near a border, and one short stub
        // across the border per shortcut.
        internal float3x2[] Corridors { get; }
    }
}
