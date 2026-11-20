using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// A <see cref="RoomDungeon"/> laid on the indoor sphere: where each room is, which wall stops a body, where the boss
    /// door is sealed, and where things stand. REQ: docs/decisions/ADR-0011-interior-dungeon-sphere.md,
    /// planet-world.md#進階地形地城.
    /// </summary>
    /// <remarks>
    /// The grid sits on the tangent plane at <see cref="Anchor"/> (gnomonic projection, grid centre on the anchor), so
    /// cell borders are great circles and the Voronoi walls of <see cref="PlanetWalls"/> come out as the square module
    /// edges. Pre: built with <see cref="Build"/>. Post: immutable; same inputs → same geometry. Side effects: none.
    /// </remarks>
    public sealed class RoomDungeonGeometry
    {
        /// <summary>Full height (m) of a room wall.</summary>
        public const float WallHeightM = 8f;

        /// <summary>Distance (m) from a cell border at which a wall is back down to the floor.</summary>
        public const float WallHalfWidthM = 3.5f;

        /// <summary>Distance (m) from a cell border within which a wall is at full height.</summary>
        public const float WallTopHalfWidthM = 1.5f;

        /// <summary>Half width (m) of a door: the wall is fully open within this distance of the door line.</summary>
        public const float DoorInnerM = 3f;

        /// <summary>Distance (m) from the door line at which the wall is back to full height.</summary>
        public const float DoorOuterM = 5f;

        /// <summary>Radius (m) of the arena ring inside the boss room.</summary>
        public const float ArenaRadiusM = 8.5f;

        /// <summary>Distance (m) from the arena gate within which the ring wall is fully open.</summary>
        public const float ArenaGateInnerM = 3f;

        /// <summary>Distance (m) from the arena gate at which the ring wall is back; the seal plug has this radius.</summary>
        public const float ArenaGateOuterM = 4.5f;

        // A door segment reaches this far into each room: past the wall's foot (3.5 m) so the gap is clean on both sides.
        private const float DoorReachM = 5f;

        // Entrance room: the player arrives a little south of the centre, the exit pad is at the south wall foot.
        private const float ArrivalOffsetM = 4f;
        private const float ExitOffsetM = 8.5f;

        // Spawn points stay inside the wall feet (14 − 3.5 = 10.5 m) with room for a 1.5 m body.
        private const float SpawnRingM = 5.5f;
        // Inside the arena ring's foot (8.5 − 3.5 = 5 m) and farther than seal plug + body (4.5 + 1.5 m) from the gate
        // at 8.5 m: 2 m keeps both with margin.
        private const float ArenaSpawnRingM = 2f;
        private const float GoldenAngleRad = 2.39996323f;

        private readonly float3 _east;
        private readonly float3 _north;
        private readonly float3[] _sites;
        private readonly bool[] _openPairs;
        private readonly float3x2[] _corridors;
        private readonly WallRing _arena;

        private RoomDungeonGeometry(RoomDungeon dungeon, float radius, float3 anchor, float3 east, float3 north,
            float3[] sites, bool[] openPairs, float3x2[] corridors, WallRing arena)
        {
            Dungeon = dungeon;
            Radius = radius;
            Anchor = anchor;
            _east = east;
            _north = north;
            _sites = sites;
            _openPairs = openPairs;
            _corridors = corridors;
            _arena = arena;
        }

        /// <summary>The stitched dungeon this geometry lays out.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public RoomDungeon Dungeon { get; }

        /// <summary>Radius (m) of the indoor sphere: the original planet's PlanetConfig.Radius (ADR-0011).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float Radius { get; }

        /// <summary>Unit direction under the grid centre.</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 Anchor { get; }

        /// <summary>Number of wall sites (room cells plus the empty cells around them).</summary>
        /// <remarks>Pre: none. Post: &gt; Dungeon.Rooms.Count. Side effects: none.</remarks>
        public int SiteCount => _sites.Length;

        /// <summary>Lays <paramref name="dungeon"/> on a sphere of <paramref name="radius"/> centred on <paramref name="anchor"/>.</summary>
        /// <remarks>
        /// Pre: dungeon non-null; radius &gt; CellSizeM × (max(Width, Height) + 2), so the grid stays well inside the near
        ///      hemisphere; anchor unit length.
        /// Post: one wall site per room cell and per empty cell touching one (8-neighbourhood); walls stand on every
        ///       border of a room cell except at its doors; the boss room holds the arena ring whose gate faces the guard
        ///       room. Same inputs → same geometry.
        /// Side effects: allocates.
        /// </remarks>
        public static RoomDungeonGeometry Build(RoomDungeon dungeon, float radius, float3 anchor)
        {
            Contract.Requires(dungeon != null, "dungeon must not be null");
            Contract.Requires(math.abs(math.lengthsq(anchor) - 1f) < 2e-3f, "anchor must be unit length");
            Contract.Requires(radius > RoomDungeonPlanner.CellSizeM * (math.max(dungeon.Width, dungeon.Height) + 2),
                "the grid must fit well inside the sphere");
            PlanetFrame frame = PlanetFrame.FromUp(anchor);
            float3 up = frame.Up;
            float3 east = frame.East;
            float3 north = frame.North;

            // Site k: room cells first (index == room index), then the empty ring cells in scan order.
            var cellSite = new Dictionary<int, int>();
            var cells = new List<int2>();
            for (int r = 0; r < dungeon.Rooms.Count; r++)
            {
                DungeonRoom room = dungeon.Rooms[r];
                cellSite[Key(room.X, room.Y)] = cells.Count;
                cells.Add(new int2(room.X, room.Y));
            }

            for (int y = -1; y <= dungeon.Height; y++)
            {
                for (int x = -1; x <= dungeon.Width; x++)
                {
                    if (cellSite.ContainsKey(Key(x, y)) || !TouchesRoom(dungeon, x, y))
                    {
                        continue;
                    }

                    cellSite[Key(x, y)] = cells.Count;
                    cells.Add(new int2(x, y));
                }
            }

            int rooms = dungeon.Rooms.Count;
            int n = cells.Count;
            var sites = new float3[n];
            for (int i = 0; i < n; i++)
            {
                sites[i] = ToDirection(up, east, north, radius, CellLocal(dungeon, cells[i].x, cells[i].y));
            }

            // Void cells are never reachable, so no wall is drawn between two of them: only room borders stand.
            var open = new bool[n * n];
            for (int a = rooms; a < n; a++)
            {
                for (int b = rooms; b < n; b++)
                {
                    open[a * n + b] = true;
                }
            }

            var corridors = new List<float3x2>();
            for (int r = 0; r < rooms; r++)
            {
                DungeonRoom room = dungeon.Rooms[r];
                if ((room.Doors & RoomDungeon.North) != 0)
                {
                    corridors.Add(DoorSegment(dungeon, up, east, north, radius, room, RoomDungeon.North));
                }

                if ((room.Doors & RoomDungeon.East) != 0)
                {
                    corridors.Add(DoorSegment(dungeon, up, east, north, radius, room, RoomDungeon.East));
                }
            }

            DungeonRoom boss = dungeon.Rooms[dungeon.BossRoom];
            int bossDoor = boss.Doors;
            float2 bossLocal = CellLocal(dungeon, boss.X, boss.Y);
            float2 gateLocal = bossLocal + new float2(RoomDungeon.DeltaX(bossDoor), RoomDungeon.DeltaY(bossDoor)) * ArenaRadiusM;
            var arena = new WallRing
            {
                Center = ToDirection(up, east, north, radius, bossLocal),
                RadiusM = ArenaRadiusM,
                Gate = ToDirection(up, east, north, radius, gateLocal),
                GateInnerM = ArenaGateInnerM,
                GateOuterM = ArenaGateOuterM,
            };

            return new RoomDungeonGeometry(dungeon, radius, up, east, north, sites, open, corridors.ToArray(), arena);
        }

        /// <summary>Builds the walls the indoor terrain and the rule layer share.</summary>
        /// <remarks>
        /// Pre: config valid with config.Radius == Radius.
        /// Post: View.Height is WallHeightM on room borders without a door, 0 within DoorInnerM of a door line and on the
        ///       floor; with locksClosed the arena gate plug blocks (PlanetWalls ring semantics).
        /// Side effects: allocates persistent native memory (caller disposes).
        /// </remarks>
        public PlanetWalls BuildWalls(in PlanetConfig config)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(math.abs(config.Radius - Radius) < 1e-3f, "config radius must equal the geometry radius");
            return PlanetWalls.Create(config, _sites, _openPairs, _corridors, DoorInnerM, DoorOuterM, _arena,
                WallHeightM, WallHalfWidthM, WallTopHalfWidthM);
        }

        /// <summary>Unit direction at the centre of room <paramref name="room"/>.</summary>
        /// <remarks>Pre: room valid. Post: RoomAt(result) == room. Side effects: none.</remarks>
        public float3 RoomCenter(int room)
        {
            Contract.Requires(room >= 0 && room < Dungeon.Rooms.Count, "invalid room");
            DungeonRoom r = Dungeon.Rooms[room];
            return Direction(CellLocal(Dungeon, r.X, r.Y));
        }

        /// <summary>Index of the room whose cell holds <paramref name="direction"/>, or −1 outside every room.</summary>
        /// <remarks>Pre: direction unit length. Post: −1 or a valid room index. Side effects: none.</remarks>
        public int RoomAt(float3 direction)
        {
            float d = math.dot(direction, Anchor);
            if (d <= 0.1f)
            {
                return -1;
            }

            float3 p = direction * (Radius / d);
            float gx = math.dot(p, _east) / RoomDungeonPlanner.CellSizeM + 0.5f * Dungeon.Width;
            float gy = math.dot(p, _north) / RoomDungeonPlanner.CellSizeM + 0.5f * Dungeon.Height;
            return Dungeon.RoomAt((int)math.floor(gx), (int)math.floor(gy));
        }

        /// <summary>Where the party arrives from the surface: just south of the entrance room centre.</summary>
        /// <remarks>Pre: none. Post: RoomAt(result) == EntranceRoom; clear of walls. Side effects: none.</remarks>
        public float3 Arrival
        {
            get
            {
                DungeonRoom r = Dungeon.Rooms[Dungeon.EntranceRoom];
                return Direction(CellLocal(Dungeon, r.X, r.Y) - new float2(0f, ArrivalOffsetM));
            }
        }

        /// <summary>The exit pad at the foot of the entrance room's south wall, which leads back to the surface.</summary>
        /// <remarks>Pre: none. Post: RoomAt(result) == EntranceRoom. Side effects: none.</remarks>
        public float3 Exit
        {
            get
            {
                DungeonRoom r = Dungeon.Rooms[Dungeon.EntranceRoom];
                return Direction(CellLocal(Dungeon, r.X, r.Y) - new float2(0f, ExitOffsetM));
            }
        }

        /// <summary>Spawn point <paramref name="k"/> of room <paramref name="room"/>: 0 is the centre, then a golden-angle ring.</summary>
        /// <remarks>
        /// Pre: room valid; k ≥ 0. Post: RoomAt(result) == room and within SpawnRingM of its centre (inside the arena
        ///      ring for the boss room). Side effects: none.
        /// </remarks>
        public float3 SpawnPoint(int room, int k)
        {
            Contract.Requires(room >= 0 && room < Dungeon.Rooms.Count, "invalid room");
            Contract.Requires(k >= 0, "k must be >= 0");
            DungeonRoom r = Dungeon.Rooms[room];
            float2 c = CellLocal(Dungeon, r.X, r.Y);
            if (k == 0)
            {
                return Direction(c);
            }

            float ring = room == Dungeon.BossRoom ? ArenaSpawnRingM : SpawnRingM;
            float angle = k * GoldenAngleRad;
            return Direction(c + new float2(math.cos(angle), math.sin(angle)) * ring);
        }

        /// <summary>Unit direction at the arena gate (the seal) of the boss room.</summary>
        /// <remarks>Pre: none. Post: in the boss room, ArenaRadiusM from its centre toward the guard room. Side effects: none.</remarks>
        public float3 ArenaGate => _arena.Gate;

        private float3 Direction(float2 local) => ToDirection(Anchor, _east, _north, Radius, local);

        private static float3 ToDirection(float3 up, float3 east, float3 north, float radius, float2 local) =>
            math.normalize(up * radius + east * local.x + north * local.y);

        private static float2 CellLocal(RoomDungeon dungeon, int x, int y) =>
            new float2(x + 0.5f - 0.5f * dungeon.Width, y + 0.5f - 0.5f * dungeon.Height) * RoomDungeonPlanner.CellSizeM;

        private static float3x2 DoorSegment(RoomDungeon dungeon, float3 up, float3 east, float3 north, float radius,
            DungeonRoom room, int side)
        {
            float2 c = CellLocal(dungeon, room.X, room.Y);
            var step = new float2(RoomDungeon.DeltaX(side), RoomDungeon.DeltaY(side));
            float2 border = c + step * (0.5f * RoomDungeonPlanner.CellSizeM);
            return new float3x2(ToDirection(up, east, north, radius, border - step * DoorReachM),
                ToDirection(up, east, north, radius, border + step * DoorReachM));
        }

        private static bool TouchesRoom(RoomDungeon dungeon, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dungeon.RoomAt(x + dx, y + dy) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int Key(int x, int y) => (y + 2) * 4096 + (x + 2);
    }
}
