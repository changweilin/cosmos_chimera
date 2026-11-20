using System.Collections.Generic;
using Cosmimera.Core.Contracts;

namespace Cosmimera.WorldGen
{
    /// <summary>What a room of a stitched dungeon is for (planet-world.md#進階地形地城, trainer-battles.md#地形試煉).</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum DungeonRoomRole
    {
        /// <summary>Where the surface entrance leads; never holds enemies.</summary>
        Entrance = 0,
        /// <summary>Ordinary room with wild creatures.</summary>
        Normal = 1,
        /// <summary>Room with an affixed elite pack.</summary>
        Elite = 2,
        /// <summary>Antechamber whose guard elite unseals the boss door (trial step 1).</summary>
        Guard = 3,
        /// <summary>Boss arena: a dead end reached only through the Guard room (trial step 2).</summary>
        Boss = 4,
        /// <summary>Dead end holding a guard elite and a breakable container (Q-W3).</summary>
        Treasure = 5,
    }

    /// <summary>Outline of a room module, fixed by which of its four sides have doors.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum RoomShape
    {
        /// <summary>One door (canonical: north).</summary>
        DeadEnd = 0,
        /// <summary>Two opposite doors (canonical: north, south).</summary>
        Straight = 1,
        /// <summary>Two adjacent doors (canonical: north, east).</summary>
        Corner = 2,
        /// <summary>Three doors (canonical: north, east, south).</summary>
        Tee = 3,
        /// <summary>Four doors.</summary>
        Cross = 4,
    }

    /// <summary>One placed room module.</summary>
    /// <remarks>Pre: produced by RoomDungeonPlanner. Post: immutable. Side effects: none.</remarks>
    public readonly struct DungeonRoom
    {
        internal DungeonRoom(int x, int y, DungeonRoomRole role, int doors, RoomShape shape, int rotation,
            string moduleId, string[] affixes)
        {
            X = x;
            Y = y;
            Role = role;
            Doors = doors;
            Shape = shape;
            Rotation = rotation;
            ModuleId = moduleId;
            Affixes = affixes;
        }

        /// <summary>Grid column (east is +x).</summary>
        /// <remarks>Pre: none. Post: 0 &lt;= X &lt; RoomDungeon.Width. Side effects: none.</remarks>
        public int X { get; }

        /// <summary>Grid row (north is +y).</summary>
        /// <remarks>Pre: none. Post: 0 &lt;= Y &lt; RoomDungeon.Height. Side effects: none.</remarks>
        public int Y { get; }

        /// <summary>What the room is for.</summary>
        /// <remarks>Pre: none. Post: defined value. Side effects: none.</remarks>
        public DungeonRoomRole Role { get; }

        /// <summary>Door bit mask: <see cref="RoomDungeon.North"/> | East | South | West.</summary>
        /// <remarks>Pre: none. Post: non-zero; each set bit has a matching door in the neighbouring room. Side effects: none.</remarks>
        public int Doors { get; }

        /// <summary>Module outline implied by <see cref="Doors"/>.</summary>
        /// <remarks>Pre: none. Post: matches the number and layout of door bits. Side effects: none.</remarks>
        public RoomShape Shape { get; }

        /// <summary>Quarter turns clockwise (seen from above) applied to the canonical shape to give <see cref="Doors"/>.</summary>
        /// <remarks>Pre: none. Post: 0..3. Side effects: none.</remarks>
        public int Rotation { get; }

        /// <summary>Hand-made module id: "&lt;terrain&gt;_&lt;role or shape&gt;_&lt;variant&gt;".</summary>
        /// <remarks>Pre: none. Post: non-empty snake_case. Side effects: none.</remarks>
        public string ModuleId { get; }

        /// <summary>Elite affix ids of this room's elite pack, in draw order; empty when the room has no affixed elite.</summary>
        /// <remarks>Pre: none. Post: non-null; distinct ids from the vocabulary; non-empty iff Role is Elite, Guard or Treasure. Side effects: none.</remarks>
        public IReadOnlyList<string> Affixes { get; }
    }

    /// <summary>An indoor / underground dungeon stitched from room modules on a square grid.</summary>
    /// <remarks>
    /// Pre: produced by <see cref="RoomDungeonPlanner.Generate"/>. Post: immutable; same inputs → same dungeon.
    /// Side effects: none.
    /// </remarks>
    public sealed class RoomDungeon
    {
        /// <summary>Door bit for the +y side.</summary>
        public const int North = 1;
        /// <summary>Door bit for the +x side.</summary>
        public const int East = 2;
        /// <summary>Door bit for the −y side.</summary>
        public const int South = 4;
        /// <summary>Door bit for the −x side.</summary>
        public const int West = 8;

        private readonly int[] _cellToRoom;

        internal RoomDungeon(string terrainId, int width, int height, DungeonRoom[] rooms, int[] cellToRoom,
            int entrance, int guard, int boss, RoomGridStyle style, int bossHalfCells = 0, int entranceSide = 0)
        {
            TerrainId = terrainId;
            Style = style;
            BossHalfCells = bossHalfCells;
            EntranceSide = entranceSide;
            Width = width;
            Height = height;
            Rooms = rooms;
            _cellToRoom = cellToRoom;
            EntranceRoom = entrance;
            GuardRoom = guard;
            BossRoom = boss;
        }

        /// <summary>Canonical advanced terrain id the dungeon belongs to.</summary>
        /// <remarks>Pre: none. Post: AdvancedTerrains.TryGet(TerrainId) succeeds. Side effects: none.</remarks>
        public string TerrainId { get; }

        /// <summary>Openness of this dungeon's grid (AdvancedTerrains.GridStyle of its terrain).</summary>
        /// <remarks>Pre: none. Post: as drawn. Side effects: none.</remarks>
        public RoomGridStyle Style { get; }

        /// <summary>Edge length (m) of one grid cell.</summary>
        /// <remarks>Pre: none. Post: Style.CellSizeM. Side effects: none.</remarks>
        public float CellSizeM => Style.CellSizeM;

        /// <summary>
        /// Half size k of the boss room in cells: it covers the (2k+1)² block centred on its cell. 0 for indoor dungeons;
        /// ≥ 1 for surface mazes, whose boss room is the plaza around the entrance wonder.
        /// </summary>
        /// <remarks>Pre: none. Post: ≥ 0; RoomAt maps every block cell to BossRoom. Side effects: none.</remarks>
        public int BossHalfCells { get; }

        /// <summary>
        /// Side of the entrance room that opens to the outside (one door bit) for a surface maze; 0 for indoor dungeons,
        /// which are entered through the exit pad instead.
        /// </summary>
        /// <remarks>Pre: none. Post: 0 or one door bit pointing off the grid. Side effects: none.</remarks>
        public int EntranceSide { get; }

        /// <summary>Grid width in cells.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int Width { get; }

        /// <summary>Grid height in cells.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int Height { get; }

        /// <summary>Placed rooms in placement order; index 0 is the entrance of an indoor dungeon and the boss plaza of a surface maze.</summary>
        /// <remarks>Pre: none. Post: Count &gt;= 4; every room reachable from the entrance. Side effects: none.</remarks>
        public IReadOnlyList<DungeonRoom> Rooms { get; }

        /// <summary>Index of the entrance room.</summary>
        /// <remarks>Pre: none. Post: 0 for indoor dungeons; a room on the grid edge for surface mazes. Side effects: none.</remarks>
        public int EntranceRoom { get; }

        /// <summary>Index of the guard (antechamber) room.</summary>
        /// <remarks>Pre: none. Post: the boss room's only neighbour. Side effects: none.</remarks>
        public int GuardRoom { get; }

        /// <summary>Index of the boss arena.</summary>
        /// <remarks>Pre: none. Post: a dead end, the deepest one in the growth tree before loop doors were added. Side effects: none.</remarks>
        public int BossRoom { get; }

        /// <summary>Index of the room at grid cell (x, y), or −1 for an empty cell or one outside the grid.</summary>
        /// <remarks>Pre: none. Post: −1 or a valid room index whose X == x and Y == y. Side effects: none.</remarks>
        public int RoomAt(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return -1;
            }

            return _cellToRoom[y * Width + x];
        }

        /// <summary>Room through door <paramref name="side"/> of room <paramref name="room"/>, or −1 when that side has no door.</summary>
        /// <remarks>
        /// Pre: room valid; side is exactly one of North, East, South, West.
        /// Post: −1, or a room whose door on the opposite side leads back to <paramref name="room"/>.
        /// Side effects: none.
        /// </remarks>
        public int Neighbour(int room, int side)
        {
            Contract.Requires(room >= 0 && room < Rooms.Count, "invalid room");
            Contract.Requires(side == North || side == East || side == South || side == West, "side must be one door bit");
            DungeonRoom r = Rooms[room];
            if ((r.Doors & side) == 0)
            {
                return -1;
            }

            int reach = room == BossRoom ? BossHalfCells + 1 : 1;
            return RoomAt(r.X + DeltaX(side) * reach, r.Y + DeltaY(side) * reach);
        }

        internal static int DeltaX(int side) => side == East ? 1 : side == West ? -1 : 0;

        internal static int DeltaY(int side) => side == North ? 1 : side == South ? -1 : 0;

        internal static int Opposite(int side) => side == North ? South : side == South ? North : side == East ? West : East;
    }
}
