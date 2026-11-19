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

            int openness = (int)rng.NextUInt(101);
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

            return new RoomDungeon(spec.Id, width, height, rooms, cell, 0, guard, boss);
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
