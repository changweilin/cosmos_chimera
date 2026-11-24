using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>One straight piece of full-height room wall, on a <see cref="RoomDungeonGeometry"/> tangent plane (m).</summary>
    /// <remarks>Pre: none. Post: From != To. Side effects: none.</remarks>
    public readonly struct WallRun
    {
        /// <summary>Start of the wall's centre line.</summary>
        public readonly float2 From;

        /// <summary>End of the wall's centre line.</summary>
        public readonly float2 To;

        /// <summary>A run from <paramref name="from"/> to <paramref name="to"/>.</summary>
        /// <remarks>Pre: from != to. Post: as given. Side effects: none.</remarks>
        public WallRun(float2 from, float2 to)
        {
            Contract.Requires(math.distancesq(from, to) > 0f, "a run needs length");
            From = from;
            To = to;
        }
    }

    /// <summary>
    /// A <see cref="RoomDungeon"/> laid on a sphere — the indoor sphere of a room dungeon or the planet surface around a
    /// surface maze's wonder: where each room is, which wall stops a body, where the boss door is sealed, and where
    /// things stand. REQ: docs/decisions/ADR-0011-interior-dungeon-sphere.md, planet-world.md#進階地形地城.
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

        /// <summary>Extra distance (m) beyond a door's open half width over which the wall rises back to full height.</summary>
        public const float DoorRampM = 2f;

        /// <summary>Distance (m) from the arena gate within which the ring wall is fully open.</summary>
        public const float ArenaGateInnerM = 3f;

        /// <summary>Distance (m) from the arena gate at which the ring wall is back; the seal plug has this radius.</summary>
        public const float ArenaGateOuterM = 4.5f;

        // A door segment reaches this far into each room: past the wall's foot (3.5 m) so the gap is clean on both sides.
        private const float DoorReachM = 5f;

        // Entrance room of an indoor dungeon: the player arrives a little south of the centre, the exit pad is at the
        // south wall foot.
        private const float ArrivalOffsetM = 4f;
        private const float ExitOffsetM = 8.5f;

        // Surface maze: the master and the way out stand this far outside the outer door's wall line.
        private const float OuterDoorOffsetM = 5f;

        // Spawn points stay this far inside a room's wall foot: a 1.5 m body plus slack.
        private const float SpawnInsetM = 3f;
        // Inside the indoor arena ring's foot (8.5 − 3.5 = 5 m) and farther than seal plug + body (4.5 + 1.5 m) from the
        // gate at 8.5 m: 2 m keeps both with margin.
        private const float ArenaSpawnRingM = 2f;
        private const float GoldenAngleRad = 2.39996323f;

        private readonly float3 _east;
        private readonly float3 _north;
        private readonly float3[] _sites;
        private readonly bool[] _openPairs;
        private readonly float3x2[] _corridors;
        private readonly WallRing _arena;
        private readonly float _bossSpawnRingM;
        private List<WallRun> _wallRuns;

        private RoomDungeonGeometry(RoomDungeon dungeon, float radius, float3 anchor, float3 east, float3 north,
            float3[] sites, bool[] openPairs, float3x2[] corridors, WallRing arena, float bossSpawnRingM)
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
            _bossSpawnRingM = bossSpawnRingM;
        }

        /// <summary>The stitched dungeon this geometry lays out.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public RoomDungeon Dungeon { get; }

        /// <summary>Radius (m) of the sphere: the original planet's PlanetConfig.Radius (ADR-0011).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public float Radius { get; }

        /// <summary>Unit direction under the grid centre (the boss plaza centre of a surface maze).</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 Anchor { get; }

        /// <summary>Number of wall sites (room cells plus the empty cells around them).</summary>
        /// <remarks>Pre: none. Post: &gt; Dungeon.Rooms.Count. Side effects: none.</remarks>
        public int SiteCount => _sites.Length;

        /// <summary>Radius (m) of the arena ring inside the boss room.</summary>
        /// <remarks>Pre: none. Post: ArenaRadiusFor(Dungeon.BossHalfCells, Dungeon.CellSizeM). Side effects: none.</remarks>
        public float ArenaRadiusM => _arena.RadiusM;

        /// <summary>Half edge (m) of the grid square around <see cref="Anchor"/>, walls included.</summary>
        /// <remarks>Pre: none. Post: 0.5 × max(Width, Height) × CellSizeM + WallHalfWidthM. Side effects: none.</remarks>
        public float HalfExtentM => 0.5f * math.max(Dungeon.Width, Dungeon.Height) * Dungeon.CellSizeM + WallHalfWidthM;

        /// <summary>Radius (m) of the arena ring for a boss room of half size k cells of <paramref name="cellSizeM"/>.</summary>
        /// <remarks>Pre: k ≥ 0; cellSizeM ≥ 10. Post: (k + 0.5)·cell − WallHalfWidthM − 2 (8.5 m indoors). Side effects: none.</remarks>
        public static float ArenaRadiusFor(int k, float cellSizeM)
        {
            Contract.Requires(k >= 0 && cellSizeM >= 10f, "need k >= 0 and a cell of at least 10 m");
            return (k + 0.5f) * cellSizeM - WallHalfWidthM - 2f;
        }

        /// <summary>Lays <paramref name="dungeon"/> on a sphere of <paramref name="radius"/> centred on <paramref name="anchor"/>.</summary>
        /// <remarks>
        /// Pre: dungeon non-null; radius &gt; CellSizeM × (max(Width, Height) + 2), so the grid stays well inside the near
        ///      hemisphere; anchor unit length; bossObstacleRadiusM ≥ 0 and, when &gt; 0 (a wonder in the plaza), at most
        ///      ArenaRadiusM − 10.
        /// Post: one wall site per room cell and per empty cell touching one (8-neighbourhood); walls stand on every
        ///       border of a room cell except at its doors (and, with Style.OpenPlan, along the whole border of two rooms
        ///       joined by a door), never inside the boss plaza; a surface maze's entrance has a door to the outside; the
        ///       boss room holds the arena ring whose gate faces the guard room. Same inputs → same geometry.
        /// Side effects: allocates.
        /// </remarks>
        public static RoomDungeonGeometry Build(RoomDungeon dungeon, float radius, float3 anchor, float bossObstacleRadiusM = 0f)
        {
            Contract.Requires(dungeon != null, "dungeon must not be null");
            Contract.Requires(math.abs(math.lengthsq(anchor) - 1f) < 2e-3f, "anchor must be unit length");
            float cellM = dungeon.CellSizeM;
            Contract.Requires(radius > cellM * (math.max(dungeon.Width, dungeon.Height) + 2),
                "the grid must fit well inside the sphere");
            float arenaRadius = ArenaRadiusFor(dungeon.BossHalfCells, cellM);
            Contract.Requires(bossObstacleRadiusM >= 0f && (bossObstacleRadiusM == 0f || bossObstacleRadiusM <= arenaRadius - 10f),
                "the wonder must leave room to fight inside the arena ring");
            PlanetFrame frame = PlanetFrame.FromUp(anchor);
            float3 up = frame.Up;
            float3 east = frame.East;
            float3 north = frame.North;

            var cells = new List<int2>();
            var owners = new List<int>();
            for (int y = -1; y <= dungeon.Height; y++)
            {
                for (int x = -1; x <= dungeon.Width; x++)
                {
                    int owner = dungeon.RoomAt(x, y);
                    if (owner < 0 && !TouchesRoom(dungeon, x, y))
                    {
                        continue;
                    }

                    cells.Add(new int2(x, y));
                    owners.Add(owner);
                }
            }

            int n = cells.Count;
            var sites = new float3[n];
            for (int i = 0; i < n; i++)
            {
                sites[i] = ToDirection(up, east, north, radius, CellLocal(dungeon, cells[i].x, cells[i].y));
            }

            // Void cells are never reachable, so no wall is drawn between two of them; the plaza's cells share one room.
            var open = new bool[n * n];
            for (int a = 0; a < n; a++)
            {
                for (int b = a + 1; b < n; b++)
                {
                    bool join = NoWallBetween(dungeon, cells[a], owners[a], cells[b], owners[b]);
                    open[a * n + b] = join;
                    open[b * n + a] = join;
                }
            }

            // Every door is listed once: as the North or East door of one of its two rooms.
            var corridors = new List<float3x2>();
            for (int r = 0; r < dungeon.Rooms.Count; r++)
            {
                DungeonRoom room = dungeon.Rooms[r];
                if ((room.Doors & RoomDungeon.North) != 0)
                {
                    corridors.Add(DoorSegment(dungeon, up, east, north, radius, r, RoomDungeon.North));
                }

                if ((room.Doors & RoomDungeon.East) != 0)
                {
                    corridors.Add(DoorSegment(dungeon, up, east, north, radius, r, RoomDungeon.East));
                }
            }

            if (dungeon.EntranceSide != 0)
            {
                corridors.Add(DoorSegment(dungeon, up, east, north, radius, dungeon.EntranceRoom, dungeon.EntranceSide));
            }

            DungeonRoom boss = dungeon.Rooms[dungeon.BossRoom];
            int bossDoor = boss.Doors;
            float2 bossLocal = CellLocal(dungeon, boss.X, boss.Y);
            float2 gateLocal = bossLocal + new float2(RoomDungeon.DeltaX(bossDoor), RoomDungeon.DeltaY(bossDoor)) * arenaRadius;
            var arena = new WallRing
            {
                Center = ToDirection(up, east, north, radius, bossLocal),
                RadiusM = arenaRadius,
                Gate = ToDirection(up, east, north, radius, gateLocal),
                GateInnerM = ArenaGateInnerM,
                GateOuterM = ArenaGateOuterM,
            };

            // Around a wonder: midway between its footprint (+2 m body) and the farthest point clear of the ring foot and
            // the seal plug (arena − 6 m).
            float bossRing = bossObstacleRadiusM > 0f
                ? 0.5f * (bossObstacleRadiusM + 2f + arenaRadius - 6f)
                : dungeon.BossHalfCells == 0 ? ArenaSpawnRingM : 0.5f * (arenaRadius - 6f);
            return new RoomDungeonGeometry(dungeon, radius, up, east, north, sites, open, corridors.ToArray(), arena, bossRing);
        }

        /// <summary>Builds the walls the terrain and the rule layer share.</summary>
        /// <remarks>
        /// Pre: config valid with config.Radius == Radius.
        /// Post: View.Height is WallHeightM on room borders without a door, 0 within Style.DoorInnerM of a door line and
        ///       on the floor; with locksClosed the arena gate plug blocks (PlanetWalls ring semantics).
        /// Side effects: allocates persistent native memory (caller disposes).
        /// </remarks>
        public PlanetWalls BuildWalls(in PlanetConfig config)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(math.abs(config.Radius - Radius) < 1e-3f, "config radius must equal the geometry radius");
            float door = Dungeon.Style.DoorInnerM;
            return PlanetWalls.Create(config, _sites, _openPairs, _corridors, door, door + DoorRampM, _arena,
                WallHeightM, WallHalfWidthM, WallTopHalfWidthM);
        }

        /// <summary>Unit direction at the centre of room <paramref name="room"/> (the plaza centre for a surface maze's boss).</summary>
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
            if (!TryGrid(direction, out float2 g))
            {
                return -1;
            }

            return Dungeon.RoomAt((int)math.floor(g.x), (int)math.floor(g.y));
        }

        /// <summary>Whether <paramref name="direction"/> lies within the grid square grown by <paramref name="marginM"/>.</summary>
        /// <remarks>Pre: direction unit length; marginM ≥ 0. Post: true for every room point and wall. Side effects: none.</remarks>
        public bool Contains(float3 direction, float marginM = WallHalfWidthM)
        {
            if (!TryGrid(direction, out float2 g))
            {
                return false;
            }

            float m = marginM / Dungeon.CellSizeM;
            return g.x >= -m && g.y >= -m && g.x <= Dungeon.Width + m && g.y <= Dungeon.Height + m;
        }

        /// <summary>Where the party arrives from the surface in an indoor dungeon: just south of the entrance room centre.</summary>
        /// <remarks>Pre: none. Post: RoomAt(result) == EntranceRoom; clear of walls. Side effects: none.</remarks>
        public float3 Arrival
        {
            get
            {
                DungeonRoom r = Dungeon.Rooms[Dungeon.EntranceRoom];
                return Direction(CellLocal(Dungeon, r.X, r.Y) - new float2(0f, ArrivalOffsetM));
            }
        }

        /// <summary>The exit pad of an indoor dungeon, at the foot of the entrance room's south wall.</summary>
        /// <remarks>Pre: none. Post: RoomAt(result) == EntranceRoom. Side effects: none.</remarks>
        public float3 Exit
        {
            get
            {
                DungeonRoom r = Dungeon.Rooms[Dungeon.EntranceRoom];
                return Direction(CellLocal(Dungeon, r.X, r.Y) - new float2(0f, ExitOffsetM));
            }
        }

        /// <summary>Centre of a surface maze's door to the outside, on the entrance room's outer wall line.</summary>
        /// <remarks>Pre: Dungeon.EntranceSide != 0. Post: on the grid edge. Side effects: none.</remarks>
        public float3 OuterGate
        {
            get
            {
                Contract.Requires(Dungeon.EntranceSide != 0, "only a surface maze has an outer door");
                return Direction(DoorBorder(Dungeon, Dungeon.EntranceRoom, Dungeon.EntranceSide));
            }
        }

        /// <summary>Just outside a surface maze's outer door: where the trial master stands and a beaten party returns.</summary>
        /// <remarks>Pre: Dungeon.EntranceSide != 0. Post: RoomAt(result) == −1, clear of the outer wall. Side effects: none.</remarks>
        public float3 OuterDoor
        {
            get
            {
                Contract.Requires(Dungeon.EntranceSide != 0, "only a surface maze has an outer door");
                int s = Dungeon.EntranceSide;
                float2 step = new float2(RoomDungeon.DeltaX(s), RoomDungeon.DeltaY(s));
                return Direction(DoorBorder(Dungeon, Dungeon.EntranceRoom, s) + step * (WallHalfWidthM + OuterDoorOffsetM));
            }
        }

        /// <summary>Unit tangent pointing out of a surface maze through its outer door.</summary>
        /// <remarks>Pre: Dungeon.EntranceSide != 0. Post: unit, tangent at OuterDoor. Side effects: none.</remarks>
        public float3 OuterFacing
        {
            get
            {
                Contract.Requires(Dungeon.EntranceSide != 0, "only a surface maze has an outer door");
                int s = Dungeon.EntranceSide;
                return math.normalize(_east * RoomDungeon.DeltaX(s) + _north * RoomDungeon.DeltaY(s));
            }
        }

        /// <summary>Spawn point <paramref name="k"/> of room <paramref name="room"/>: 0 is the centre, then a golden-angle ring.</summary>
        /// <remarks>
        /// Pre: room valid; k ≥ 0; k ≥ 1 for a plaza with a wonder in its centre.
        /// Post: RoomAt(result) == room and clear of its walls for a 1.5 m body (inside the arena ring and outside the
        ///       wonder for the boss room). Side effects: none.
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

            float ring = room == Dungeon.BossRoom ? _bossSpawnRingM : 0.5f * Dungeon.CellSizeM - WallHalfWidthM - SpawnInsetM;
            float angle = k * GoldenAngleRad;
            return Direction(c + new float2(math.cos(angle), math.sin(angle)) * ring);
        }

        /// <summary>Unit direction at the arena gate (the seal) of the boss room.</summary>
        /// <remarks>Pre: none. Post: in the boss room, ArenaRadiusM from its centre toward the guard room. Side effects: none.</remarks>
        public float3 ArenaGate => _arena.Gate;

        /// <summary>
        /// Straight full-height wall pieces on the grid's tangent plane (metres east/north of <see cref="Anchor"/>, as
        /// <see cref="LocalToDirection"/> takes them): every room border without a door, split around each door at the
        /// end of its ramp. The arena ring is not included (<see cref="ArenaCenterLocal"/>).
        /// </summary>
        /// <remarks>
        /// Pre: none. Post: every run's centre line is a cell border where BuildWalls stands at WallHeightM; no run
        ///      comes within DoorInnerM + DoorRampM of a door's midpoint along its border; same geometry → same runs,
        ///      same order.
        /// Side effects: computed once and cached.
        /// </remarks>
        public IReadOnlyList<WallRun> WallRuns => _wallRuns ??= ComputeWallRuns(Dungeon);

        /// <summary>Unit direction of a point on the grid's tangent plane, metres east/north of <see cref="Anchor"/>.</summary>
        /// <remarks>Pre: local finite. Post: unit length; LocalToDirection(0) == Anchor. Side effects: none.</remarks>
        public float3 LocalToDirection(float2 local) => Direction(local);

        /// <summary>Arena ring centre on the tangent plane (the boss room centre).</summary>
        /// <remarks>Pre: none. Post: LocalToDirection(result) == RoomCenter(BossRoom). Side effects: none.</remarks>
        public float2 ArenaCenterLocal
        {
            get
            {
                DungeonRoom r = Dungeon.Rooms[Dungeon.BossRoom];
                return CellLocal(Dungeon, r.X, r.Y);
            }
        }

        /// <summary>Unit vector on the tangent plane from the arena centre toward its gate.</summary>
        /// <remarks>Pre: none. Post: unit and axis-aligned (the boss room's one door). Side effects: none.</remarks>
        public float2 ArenaGateAxis
        {
            get
            {
                int door = Dungeon.Rooms[Dungeon.BossRoom].Doors;
                return new float2(RoomDungeon.DeltaX(door), RoomDungeon.DeltaY(door));
            }
        }

        private bool TryGrid(float3 direction, out float2 grid)
        {
            float d = math.dot(direction, Anchor);
            if (d <= 0.1f)
            {
                grid = default;
                return false;
            }

            float3 p = direction * (Radius / d);
            grid = new float2(math.dot(p, _east) / Dungeon.CellSizeM + 0.5f * Dungeon.Width,
                math.dot(p, _north) / Dungeon.CellSizeM + 0.5f * Dungeon.Height);
            return true;
        }

        private float3 Direction(float2 local) => ToDirection(Anchor, _east, _north, Radius, local);

        private static float3 ToDirection(float3 up, float3 east, float3 north, float radius, float2 local) =>
            math.normalize(up * radius + east * local.x + north * local.y);

        private static float2 CellLocal(RoomDungeon dungeon, int x, int y) =>
            new float2(x + 0.5f - 0.5f * dungeon.Width, y + 0.5f - 0.5f * dungeon.Height) * dungeon.CellSizeM;

        // Midpoint of the wall line a door of `room` on `side` crosses; the plaza's door is at its block edge.
        private static float2 DoorBorder(RoomDungeon dungeon, int room, int side)
        {
            DungeonRoom r = dungeon.Rooms[room];
            int half = room == dungeon.BossRoom ? dungeon.BossHalfCells : 0;
            var step = new float2(RoomDungeon.DeltaX(side), RoomDungeon.DeltaY(side));
            return CellLocal(dungeon, r.X, r.Y) + step * ((half + 0.5f) * dungeon.CellSizeM);
        }

        private static float3x2 DoorSegment(RoomDungeon dungeon, float3 up, float3 east, float3 north, float radius,
            int room, int side)
        {
            var step = new float2(RoomDungeon.DeltaX(side), RoomDungeon.DeltaY(side));
            float2 border = DoorBorder(dungeon, room, side);
            return new float3x2(ToDirection(up, east, north, radius, border - step * DoorReachM),
                ToDirection(up, east, north, radius, border + step * DoorReachM));
        }

        // Void cells are never reachable, so no wall stands between two of them; the plaza's cells share one room; an
        // open-plan style drops the whole border of two rooms joined by a door.
        private static bool NoWallBetween(RoomDungeon dungeon, int2 cellA, int ownerA, int2 cellB, int ownerB) =>
            ownerA == ownerB ||
            (dungeon.Style.OpenPlan && ownerA >= 0 && ownerB >= 0 &&
             ownerA != dungeon.BossRoom && ownerB != dungeon.BossRoom &&
             math.abs(cellA.x - cellB.x) + math.abs(cellA.y - cellB.y) == 1 &&
             Joined(dungeon, ownerA, ownerB));

        private static bool IsSite(RoomDungeon dungeon, int x, int y) =>
            dungeon.RoomAt(x, y) >= 0 || TouchesRoom(dungeon, x, y);

        private static List<WallRun> ComputeWallRuns(RoomDungeon dungeon)
        {
            float cell = dungeon.CellSizeM;
            float gap = dungeon.Style.DoorInnerM + DoorRampM;
            var doors = new List<float2>();
            for (int r = 0; r < dungeon.Rooms.Count; r++)
            {
                if ((dungeon.Rooms[r].Doors & RoomDungeon.North) != 0)
                {
                    doors.Add(DoorBorder(dungeon, r, RoomDungeon.North));
                }

                if ((dungeon.Rooms[r].Doors & RoomDungeon.East) != 0)
                {
                    doors.Add(DoorBorder(dungeon, r, RoomDungeon.East));
                }
            }

            if (dungeon.EntranceSide != 0)
            {
                doors.Add(DoorBorder(dungeon, dungeon.EntranceRoom, dungeon.EntranceSide));
            }

            var runs = new List<WallRun>();
            for (int y = -1; y <= dungeon.Height; y++)
            {
                for (int x = -1; x <= dungeon.Width; x++)
                {
                    if (!IsSite(dungeon, x, y))
                    {
                        continue;
                    }

                    int owner = dungeon.RoomAt(x, y);
                    float2 c = CellLocal(dungeon, x, y);
                    // East border (shared with x + 1) and north border (shared with y + 1): every border once.
                    for (int k = 0; k < 2; k++)
                    {
                        var other = new int2(x + (k == 0 ? 1 : 0), y + (k == 0 ? 0 : 1));
                        if (!IsSite(dungeon, other.x, other.y) ||
                            NoWallBetween(dungeon, new int2(x, y), owner, other, dungeon.RoomAt(other.x, other.y)))
                        {
                            continue;
                        }

                        float2 from = k == 0 ? c + new float2(0.5f, -0.5f) * cell : c + new float2(-0.5f, 0.5f) * cell;
                        float2 to = c + new float2(0.5f, 0.5f) * cell;
                        AddWithGaps(runs, from, to, doors, gap);
                    }
                }
            }

            return runs;
        }

        // A door on this border (its midpoint on the segment) leaves two pieces, each ending where the door ramp is back
        // at full height.
        private static void AddWithGaps(List<WallRun> runs, float2 from, float2 to, List<float2> doors, float gap)
        {
            float len = math.distance(from, to);
            float2 axis = (to - from) / len;
            foreach (float2 d in doors)
            {
                float t = math.dot(d - from, axis);
                float off = math.length(d - from - axis * t);
                if (off > 0.01f || t <= 0f || t >= len)
                {
                    continue;
                }

                if (t - gap > 0.5f)
                {
                    runs.Add(new WallRun(from, from + axis * (t - gap)));
                }

                if (len - (t + gap) > 0.5f)
                {
                    runs.Add(new WallRun(from + axis * (t + gap), to));
                }

                return;
            }

            runs.Add(new WallRun(from, to));
        }

        private static bool Joined(RoomDungeon dungeon, int a, int b)
        {
            int doors = dungeon.Rooms[a].Doors;
            for (int s = RoomDungeon.North; s <= RoomDungeon.West; s <<= 1)
            {
                if ((doors & s) != 0 && dungeon.Neighbour(a, s) == b)
                {
                    return true;
                }
            }

            return false;
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
    }
}
