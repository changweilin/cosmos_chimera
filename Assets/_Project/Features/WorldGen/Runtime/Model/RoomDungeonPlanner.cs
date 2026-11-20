using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Stitches hand-made room modules into a Diablo-style indoor / underground dungeon for one advanced terrain:
    /// a tree of rooms grown on a grid, the farthest dead end becomes the boss arena behind a guard antechamber, other
    /// dead ends hold treasure, and openness adds loop doors between non-dead-end rooms.
    /// REQ: docs/design/planet-world.md#進階地形地城 (form 2), docs/design/trainer-battles.md#地形試煉 (guard → arena),
    /// Q-W3 (dead-end treasure = guard elite + container).
    /// </summary>
    /// <remarks>
    /// Determinism: one Pcg32(seed, RoomStreamBase + terrain index) stream drawn in a fixed order: room count, growth,
    /// openness, loop rolls (row-major, east then north), elite rolls, affixes, module variants (room order). Integer-only
    /// decisions, so every host builds the same dungeon. Numbers (room counts, chances, variants) are provisional.
    /// </remarks>
    public static class RoomDungeonPlanner
    {
        /// <summary>Bump when any output for the same inputs changes (snapshot tests pin it).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 1;

        /// <summary>Hand-made variants per module slot (shape or special role).</summary>
        /// <remarks>Pre: none. Post: constant &gt;= 1. Side effects: none.</remarks>
        public const int ModuleVariants = 3;

        /// <summary>Edge length (m) of one grid cell; every module fits inside it.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float CellSizeM = 28f;

        private const ulong RoomStreamBase = 0x524D0000UL;
        private const ulong SurfaceStreamBase = 0x534D0000UL;
        private const int EliteChancePercent = 30;
        private const int SecondAffixChancePercent = 50;
        // At openness 100 a candidate loop door opens 40 % of the time: enough shortcuts without dissolving the tree.
        private const int LoopPermilleAtFullOpenness = 400;
        // Growing from the newest room 60 % of the time makes corridors instead of a blob around the entrance.
        private const int ExtendNewestPercent = 60;

        private static readonly int[] Sides = { RoomDungeon.North, RoomDungeon.East, RoomDungeon.South, RoomDungeon.West };

        /// <summary>Base room count before the 0–2 random extra (provisional; planet-world.md#進階地形地城).</summary>
        /// <remarks>Pre: size is Moon, Planet or Supergiant. Post: 9, 13 or 17. Side effects: none.</remarks>
        public static int BaseRoomCount(PlanetSize size)
        {
            Contract.Requires(size >= PlanetSize.Moon && size <= PlanetSize.Supergiant,
                "advanced terrains appear on moon, planet and supergiant only");
            return size == PlanetSize.Moon ? 9 : size == PlanetSize.Planet ? 13 : 17;
        }

        /// <summary>Generates the room dungeon of advanced terrain <paramref name="terrainId"/> for one run.</summary>
        /// <remarks>
        /// Pre: terrainId names an advanced terrain (id or alias) that appears on <paramref name="size"/>;
        ///      eliteAffixIds non-null, non-empty, without null/empty or duplicate entries.
        /// Post: BaseRoomCount(size) .. +2 rooms; room 0 is the Entrance at the grid's south edge; every room is reachable
        ///       from the entrance through doors that match on both sides; BossRoom is the dead end deepest in the growth
        ///       tree (before loop doors), its only door leads to GuardRoom (≠ EntranceRoom); every other dead end except the entrance
        ///       is Treasure; Elite / Guard / Treasure rooms carry 1–2 / 2 / 1 distinct affixes (capped by the pool);
        ///       TerrainId is canonical. Same inputs → same dungeon.
        /// Side effects: allocates.
        /// </remarks>
        public static RoomDungeon Generate(ulong seed, string terrainId, PlanetSize size, IReadOnlyList<string> eliteAffixIds)
        {
            Contract.Requires(AdvancedTerrains.TryGet(terrainId, out AdvancedTerrainSpec spec), "unknown advanced terrain");
            Contract.Requires(AdvancedTerrains.AppearsOn(spec, size), "terrain does not appear on this planet size");
            Contract.Requires(eliteAffixIds != null && eliteAffixIds.Count > 0, "need at least one elite affix");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string a in eliteAffixIds)
            {
                Contract.Requires(!string.IsNullOrEmpty(a) && seen.Add(a), "elite affixes must be non-empty and distinct");
            }

            int terrainIndex = AdvancedTerrains.IndexOf(spec.Id);
            var rng = new Pcg32(seed, RoomStreamBase + (ulong)terrainIndex);
            int count = BaseRoomCount(size) + (int)rng.NextUInt(3);
            // Side 2 cells wider than a square of `count` keeps growth from boxing itself in.
            int side = (int)Math.Ceiling(Math.Sqrt(count)) + 2;
            int width = side;
            int height = side;

            var cell = new int[width * height];
            for (int i = 0; i < cell.Length; i++)
            {
                cell[i] = -1;
            }

            var xs = new List<int>(count);
            var ys = new List<int>(count);
            var doors = new List<int>(count);
            var parent = new List<int>(count);
            var depth = new List<int>(count);
            AddRoom(cell, width, xs, ys, doors, parent, depth, width / 2, 0, -1, 0);

            var free = new List<int>(4);
            while (xs.Count < count)
            {
                int from = (int)rng.NextUInt(100) < ExtendNewestPercent ? xs.Count - 1 : (int)rng.NextUInt((uint)xs.Count);
                // Fall back through the other rooms in a fixed rotation when `from` is boxed in.
                int grownFrom = -1;
                int grownSide = 0;
                for (int k = 0; k < xs.Count && grownFrom < 0; k++)
                {
                    int r = (from + k) % xs.Count;
                    free.Clear();
                    foreach (int s in Sides)
                    {
                        int nx = xs[r] + RoomDungeon.DeltaX(s);
                        int ny = ys[r] + RoomDungeon.DeltaY(s);
                        if (nx >= 0 && ny >= 0 && nx < width && ny < height && cell[ny * width + nx] < 0)
                        {
                            free.Add(s);
                        }
                    }

                    if (free.Count > 0)
                    {
                        grownFrom = r;
                        grownSide = free[(int)rng.NextUInt((uint)free.Count)];
                    }
                }

                Contract.Ensures(grownFrom >= 0, "grid too small for room count");
                int x = xs[grownFrom] + RoomDungeon.DeltaX(grownSide);
                int y = ys[grownFrom] + RoomDungeon.DeltaY(grownSide);
                doors[grownFrom] |= grownSide;
                AddRoom(cell, width, xs, ys, doors, parent, depth, x, y, grownFrom, RoomDungeon.Opposite(grownSide));
            }

            int n = xs.Count;
            int boss = -1;
            for (int r = 1; r < n; r++)
            {
                if (IsDeadEnd(doors[r]) && (boss < 0 || depth[r] > depth[boss]))
                {
                    boss = r;
                }
            }

            // The entrance sits on the south edge (≤ 3 neighbours), so ≥ 5 rooms force the deepest leaf to depth ≥ 2.
            int guard = parent[boss];
            Contract.Ensures(guard > 0, "boss must sit behind a guard room that is not the entrance");

            var roles = new DungeonRoomRole[n];
            for (int r = 0; r < n; r++)
            {
                roles[r] = r == 0 ? DungeonRoomRole.Entrance
                    : r == boss ? DungeonRoomRole.Boss
                    : r == guard ? DungeonRoomRole.Guard
                    : IsDeadEnd(doors[r]) ? DungeonRoomRole.Treasure
                    : DungeonRoomRole.Normal;
            }

            // The indoor generator lays every terrain on the indoor grid; the four surface mazes get their own styles
            // only through GenerateSurfaceMaze (room-dungeon-v1.txt pins all ten terrains on this grid).
            RoomGridStyle style = spec.Form == AdvancedDungeonForm.Rooms ? AdvancedTerrains.GridStyle(spec) : AdvancedTerrains.IndoorGridStyle;
            return Finish(spec, style, ref rng, cell, width, height, xs, ys, doors, roles,
                eliteAffixIds, 0, guard, boss, 0, 0);
        }

        /// <summary>
        /// Half size k (in cells) of a surface maze's boss plaza: the smallest k ≥ 1 whose arena ring leaves room to fight
        /// around a wonder of <paramref name="wonderFootprintM"/> (planet-world.md#進階地形地城, Q-W45).
        /// </summary>
        /// <remarks>
        /// Pre: cellSizeM ≥ 10; wonderFootprintM finite and ≥ 0.
        /// Post: ≥ 1; RoomDungeonGeometry.ArenaRadiusFor(k, cell) − 6 ≥ wonderFootprintM + 4. Side effects: none.
        /// </remarks>
        public static int PlazaHalfCells(float cellSizeM, float wonderFootprintM)
        {
            Contract.Requires(cellSizeM >= 10f, "cell must be at least 10 m");
            Contract.Requires(!float.IsNaN(wonderFootprintM) && !float.IsInfinity(wonderFootprintM) && wonderFootprintM >= 0f,
                "footprint must be >= 0");
            // (k + 0.5)·cell − 5.5 (arena ring) − 6 (seal plug + body) ≥ footprint + 4 (body + slack).
            int k = (int)Math.Ceiling((wonderFootprintM + 15.5f) / cellSizeM - 0.5f);
            return Math.Max(1, k);
        }

        /// <summary>Grid side (cells, odd) of a surface maze on a planet of <paramref name="size"/> with plaza half size k.</summary>
        /// <remarks>
        /// Pre: size Moon..Supergiant; bossHalfCells ≥ 1.
        /// Post: odd; at least two rings of cells around the plaza; the ring holds ≥ 2 × the largest room count.
        /// Side effects: none.
        /// </remarks>
        public static int SurfaceMazeSide(PlanetSize size, int bossHalfCells)
        {
            Contract.Requires(bossHalfCells >= 1, "plaza half size must be >= 1");
            int plaza = 2 * bossHalfCells + 1;
            int maxRooms = BaseRoomCount(size) + 2;
            int side = plaza + 4;
            while (side * side - plaza * plaza < 2 * maxRooms)
            {
                side += 2;
            }

            return side;
        }

        /// <summary>Half the edge length (m) of the surface maze square, known before the room count is drawn.</summary>
        /// <remarks>
        /// Pre: spec is a SurfaceMaze terrain appearing on size; wonderFootprintM finite and ≥ 0.
        /// Post: == 0.5 × cell × SurfaceMazeSide(size, PlazaHalfCells(cell, wonderFootprintM)): the maze square of
        ///       GenerateSurfaceMaze with that footprint has exactly this half edge. Side effects: none.
        /// </remarks>
        public static float SurfaceMazeHalfExtentM(in AdvancedTerrainSpec spec, PlanetSize size, float wonderFootprintM)
        {
            Contract.Requires(spec.Form == AdvancedDungeonForm.SurfaceMaze, "terrain is not a surface maze");
            Contract.Requires(AdvancedTerrains.AppearsOn(spec, size), "terrain does not appear on this planet size");
            float cell = AdvancedTerrains.GridStyle(spec).CellSizeM;
            return 0.5f * cell * SurfaceMazeSide(size, PlazaHalfCells(cell, wonderFootprintM));
        }

        /// <summary>
        /// Generates the surface maze of advanced terrain <paramref name="terrainId"/>: a plaza around the entrance wonder
        /// (the boss arena) in the grid centre, a guard room at its one door, and a room tree grown outward until it
        /// reaches the grid edge, where the deepest edge room is the entrance with a door to the outside.
        /// REQ: planet-world.md#進階地形地城 (Q-W45), trainer-battles.md#地形試煉.
        /// </summary>
        /// <remarks>
        /// Pre: terrainId names a SurfaceMaze terrain appearing on size; eliteAffixIds as Generate; wonderFootprintM finite
        ///      and ≥ 0.
        /// Post: room 0 is the Boss plaza covering the (2k+1)² centre block, k == PlazaHalfCells(cell, footprint); its only
        ///       door leads to GuardRoom == 1; every room reachable; EntranceRoom sits on the grid edge with EntranceSide
        ///       pointing off the grid; other dead ends are Treasure; Width == Height == SurfaceMazeSide(size, k); room
        ///       count ≥ BaseRoomCount(size). Same inputs → same maze.
        /// Side effects: allocates.
        /// </remarks>
        public static RoomDungeon GenerateSurfaceMaze(ulong seed, string terrainId, PlanetSize size,
            IReadOnlyList<string> eliteAffixIds, float wonderFootprintM)
        {
            Contract.Requires(AdvancedTerrains.TryGet(terrainId, out AdvancedTerrainSpec spec), "unknown advanced terrain");
            Contract.Requires(spec.Form == AdvancedDungeonForm.SurfaceMaze, "terrain is not a surface maze");
            Contract.Requires(AdvancedTerrains.AppearsOn(spec, size), "terrain does not appear on this planet size");
            Contract.Requires(eliteAffixIds != null && eliteAffixIds.Count > 0, "need at least one elite affix");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string a in eliteAffixIds)
            {
                Contract.Requires(!string.IsNullOrEmpty(a) && seen.Add(a), "elite affixes must be non-empty and distinct");
            }

            RoomGridStyle style = AdvancedTerrains.GridStyle(spec);
            int k = PlazaHalfCells(style.CellSizeM, wonderFootprintM);
            int side = SurfaceMazeSide(size, k);
            int c = side / 2;
            var rng = new Pcg32(seed, SurfaceStreamBase + (ulong)AdvancedTerrains.IndexOf(spec.Id));
            int count = BaseRoomCount(size) + (int)rng.NextUInt(3);

            var cell = new int[side * side];
            for (int i = 0; i < cell.Length; i++)
            {
                cell[i] = -1;
            }

            var xs = new List<int>(count);
            var ys = new List<int>(count);
            var doors = new List<int>(count);
            var parent = new List<int>(count);
            var depth = new List<int>(count);
            AddRoom(cell, side, xs, ys, doors, parent, depth, c, c, -1, 0);
            for (int dy = -k; dy <= k; dy++)
            {
                for (int dx = -k; dx <= k; dx++)
                {
                    cell[(c + dy) * side + c + dx] = 0;
                }
            }

            int guardSide = Sides[rng.NextUInt(4)];
            doors[0] = guardSide;
            AddRoom(cell, side, xs, ys, doors, parent, depth, c + RoomDungeon.DeltaX(guardSide) * (k + 1),
                c + RoomDungeon.DeltaY(guardSide) * (k + 1), 0, RoomDungeon.Opposite(guardSide));

            var free = new List<int>(4);
            while (xs.Count < count || DeepestEdgeRoom(xs, ys, depth, side) < 0)
            {
                // Never grow from the plaza: its one door is the guard's.
                int grown = xs.Count - 1;
                int from = (int)rng.NextUInt(100) < ExtendNewestPercent ? xs.Count - 1 : 1 + (int)rng.NextUInt((uint)grown);
                int grownFrom = -1;
                int grownSide = 0;
                for (int t = 0; t < grown && grownFrom < 0; t++)
                {
                    int r = 1 + (from - 1 + t) % grown;
                    free.Clear();
                    foreach (int s in Sides)
                    {
                        int nx = xs[r] + RoomDungeon.DeltaX(s);
                        int ny = ys[r] + RoomDungeon.DeltaY(s);
                        if (nx >= 0 && ny >= 0 && nx < side && ny < side && cell[ny * side + nx] < 0)
                        {
                            free.Add(s);
                        }
                    }

                    if (free.Count > 0)
                    {
                        grownFrom = r;
                        grownSide = free[(int)rng.NextUInt((uint)free.Count)];
                    }
                }

                Contract.Ensures(grownFrom >= 0, "grid too small for room count");
                int x = xs[grownFrom] + RoomDungeon.DeltaX(grownSide);
                int y = ys[grownFrom] + RoomDungeon.DeltaY(grownSide);
                doors[grownFrom] |= grownSide;
                AddRoom(cell, side, xs, ys, doors, parent, depth, x, y, grownFrom, RoomDungeon.Opposite(grownSide));
            }

            int entrance = DeepestEdgeRoom(xs, ys, depth, side);
            int entranceSide = 0;
            foreach (int s in Sides)
            {
                int nx = xs[entrance] + RoomDungeon.DeltaX(s);
                int ny = ys[entrance] + RoomDungeon.DeltaY(s);
                if (nx < 0 || ny < 0 || nx >= side || ny >= side)
                {
                    entranceSide = s;
                    break;
                }
            }

            int n = xs.Count;
            var roles = new DungeonRoomRole[n];
            for (int r = 0; r < n; r++)
            {
                roles[r] = r == 0 ? DungeonRoomRole.Boss
                    : r == 1 ? DungeonRoomRole.Guard
                    : r == entrance ? DungeonRoomRole.Entrance
                    : IsDeadEnd(doors[r]) ? DungeonRoomRole.Treasure
                    : DungeonRoomRole.Normal;
            }

            return Finish(spec, style, ref rng, cell, side, side, xs, ys, doors, roles, eliteAffixIds, entrance, 1, 0, k,
                entranceSide);
        }

        // Deepest room on the grid edge (ties: lowest index), excluding the plaza and the guard; −1 when none yet.
        private static int DeepestEdgeRoom(List<int> xs, List<int> ys, List<int> depth, int side)
        {
            int best = -1;
            for (int r = 2; r < xs.Count; r++)
            {
                bool edge = xs[r] == 0 || ys[r] == 0 || xs[r] == side - 1 || ys[r] == side - 1;
                if (edge && (best < 0 || depth[r] > depth[best]))
                {
                    best = r;
                }
            }

            return best;
        }

        // Shared tail of both generators: loop doors, elites, affixes, module shapes. The draw order is the original
        // indoor order, so indoor dungeons are unchanged by the surface-maze generator.
        private static RoomDungeon Finish(in AdvancedTerrainSpec spec, RoomGridStyle style, ref Pcg32 rng, int[] cell,
            int width, int height, List<int> xs, List<int> ys, List<int> doors, DungeonRoomRole[] roles,
            IReadOnlyList<string> eliteAffixIds, int entrance, int guard, int boss, int bossHalfCells, int entranceSide)
        {
            int openness = style.OpennessMin + (int)rng.NextUInt((uint)(style.OpennessMax - style.OpennessMin + 1));
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int a = cell[y * width + x];
                    if (a < 0)
                    {
                        continue;
                    }

                    TryLoop(cell, width, height, doors, roles, a, x, y, RoomDungeon.East, openness, ref rng);
                    TryLoop(cell, width, height, doors, roles, a, x, y, RoomDungeon.North, openness, ref rng);
                }
            }

            int n = xs.Count;
            for (int r = 0; r < n; r++)
            {
                if (roles[r] == DungeonRoomRole.Normal && (int)rng.NextUInt(100) < EliteChancePercent)
                {
                    roles[r] = DungeonRoomRole.Elite;
                }
            }

            var affixes = new string[n][];
            for (int r = 0; r < n; r++)
            {
                int want = roles[r] == DungeonRoomRole.Guard ? 2
                    : roles[r] == DungeonRoomRole.Treasure ? 1
                    : roles[r] == DungeonRoomRole.Elite ? 1 + ((int)rng.NextUInt(100) < SecondAffixChancePercent ? 1 : 0)
                    : 0;
                affixes[r] = DrawAffixes(eliteAffixIds, want, ref rng);
            }

            var rooms = new DungeonRoom[n];
            for (int r = 0; r < n; r++)
            {
                RoomShape shape = ShapeOf(doors[r], out int rotation);
                int variant = (int)rng.NextUInt(ModuleVariants);
                string slot = roles[r] switch
                {
                    DungeonRoomRole.Entrance => "entrance",
                    DungeonRoomRole.Guard => "guard",
                    DungeonRoomRole.Boss => "boss",
                    DungeonRoomRole.Treasure => "treasure",
                    _ => ShapeSlot(shape),
                };
                rooms[r] = new DungeonRoom(xs[r], ys[r], roles[r], doors[r], shape, rotation,
                    spec.Id + "_" + slot + "_" + variant, affixes[r]);
            }

            return new RoomDungeon(spec.Id, width, height, rooms, cell, entrance, guard, boss, style, bossHalfCells,
                entranceSide);
        }

        /// <summary>Digest of everything a dungeon decides (snapshot tests and cross-host checks).</summary>
        /// <remarks>Pre: dungeon non-null. Post: equal dungeons → equal hashes. Side effects: none.</remarks>
        public static ulong Hash(RoomDungeon dungeon)
        {
            Contract.Requires(dungeon != null, "dungeon must not be null");
            ulong h = 14695981039346656037UL;
            Mix(ref h, dungeon.TerrainId);
            Mix(ref h, dungeon.Width);
            Mix(ref h, dungeon.Height);
            Mix(ref h, dungeon.GuardRoom);
            Mix(ref h, dungeon.BossRoom);
            // Surface mazes only, so indoor hashes (pinned by room-dungeon-v1.txt) are unchanged.
            if (dungeon.BossHalfCells > 0)
            {
                Mix(ref h, dungeon.BossHalfCells);
                Mix(ref h, dungeon.EntranceRoom);
                Mix(ref h, dungeon.EntranceSide);
            }

            foreach (DungeonRoom r in dungeon.Rooms)
            {
                Mix(ref h, r.X);
                Mix(ref h, r.Y);
                Mix(ref h, (int)r.Role);
                Mix(ref h, r.Doors);
                Mix(ref h, r.Rotation);
                Mix(ref h, r.ModuleId);
                foreach (string a in r.Affixes)
                {
                    Mix(ref h, a);
                }
            }

            return h;
        }

        private static void AddRoom(int[] cell, int width, List<int> xs, List<int> ys, List<int> doors, List<int> parent,
            List<int> depth, int x, int y, int from, int doorBack)
        {
            cell[y * width + x] = xs.Count;
            xs.Add(x);
            ys.Add(y);
            doors.Add(doorBack);
            parent.Add(from);
            depth.Add(from < 0 ? 0 : depth[from] + 1);
        }

        private static void TryLoop(int[] cell, int width, int height, List<int> doors, DungeonRoomRole[] roles, int a,
            int x, int y, int side, int openness, ref Pcg32 rng)
        {
            int nx = x + RoomDungeon.DeltaX(side);
            int ny = y + RoomDungeon.DeltaY(side);
            if (nx >= width || ny >= height)
            {
                return;
            }

            int b = cell[ny * width + nx];
            if (b < 0 || (doors[a] & side) != 0)
            {
                return;
            }

            // Boss and treasure rooms must stay dead ends: the boss behind its guard, treasure as a detour.
            if (!CanLoop(roles[a]) || !CanLoop(roles[b]))
            {
                return;
            }

            if ((int)rng.NextUInt(1000) < openness * LoopPermilleAtFullOpenness / 100)
            {
                doors[a] |= side;
                doors[b] |= RoomDungeon.Opposite(side);
            }
        }

        private static bool CanLoop(DungeonRoomRole role) =>
            role != DungeonRoomRole.Boss && role != DungeonRoomRole.Treasure;

        private static string[] DrawAffixes(IReadOnlyList<string> pool, int want, ref Pcg32 rng)
        {
            int take = Math.Min(want, pool.Count);
            if (take == 0)
            {
                return Array.Empty<string>();
            }

            var picked = new string[take];
            var used = new bool[pool.Count];
            for (int i = 0; i < take; i++)
            {
                int k = (int)rng.NextUInt((uint)(pool.Count - i));
                for (int j = 0; j < pool.Count; j++)
                {
                    if (used[j])
                    {
                        continue;
                    }

                    if (k == 0)
                    {
                        used[j] = true;
                        picked[i] = pool[j];
                        break;
                    }

                    k--;
                }
            }

            return picked;
        }

        private static bool IsDeadEnd(int doors) => doors == RoomDungeon.North || doors == RoomDungeon.East ||
                                                    doors == RoomDungeon.South || doors == RoomDungeon.West;

        private static int Rotate(int mask, int quarterTurns)
        {
            for (int i = 0; i < quarterTurns; i++)
            {
                mask = ((mask << 1) | (mask >> 3)) & 15;
            }

            return mask;
        }

        private static RoomShape ShapeOf(int doors, out int rotation)
        {
            RoomShape[] shapes = { RoomShape.DeadEnd, RoomShape.Straight, RoomShape.Corner, RoomShape.Tee, RoomShape.Cross };
            int[] canon = { RoomDungeon.North, RoomDungeon.North | RoomDungeon.South, RoomDungeon.North | RoomDungeon.East,
                RoomDungeon.North | RoomDungeon.East | RoomDungeon.South, 15 };
            for (int s = 0; s < canon.Length; s++)
            {
                for (int r = 0; r < 4; r++)
                {
                    if (Rotate(canon[s], r) == doors)
                    {
                        rotation = r;
                        return shapes[s];
                    }
                }
            }

            throw new ContractException("room without doors");
        }

        private static string ShapeSlot(RoomShape shape) => shape switch
        {
            RoomShape.DeadEnd => "dead_end",
            RoomShape.Straight => "straight",
            RoomShape.Corner => "corner",
            RoomShape.Tee => "tee",
            _ => "cross",
        };

        private static void Mix(ref ulong h, int v)
        {
            unchecked
            {
                h = (h ^ (uint)v) * 1099511628211UL;
            }
        }

        private static void Mix(ref ulong h, string s)
        {
            unchecked
            {
                foreach (char c in s)
                {
                    h = (h ^ c) * 1099511628211UL;
                }

                h = (h ^ 0xFFUL) * 1099511628211UL;
            }
        }
    }
}
