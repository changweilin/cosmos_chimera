using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Turns a run's region graph into a dungeon on the planet surface: regions are rooms, region borders are walls,
    /// roads pass through gates, the boss waits in a locked arena, dead ends hold treasure and shortcuts close loops.
    /// REQ: docs/design/planet-world.md#地表地牢 (Q-W2, user 2026-11-13: 地表即地牢、開放程度隨機、實體牆、鎖鑰門／死路寶藏房／環路).
    /// </summary>
    /// <remarks>
    /// Determinism: one Pcg32(seed, 'G') stream in a fixed order (openness, dead-end protection, shortcut shuffle,
    /// open-border rolls). Decisions read region sites, road vertices and isLand only, never terrain height (ADR-0007).
    /// Float comparisons decide which cell a road vertex is in; a vertex within float error of a border can flip between
    /// hosts, the same known risk RunPlanner.RegionAt already carries.
    /// </remarks>
    public static class DungeonPlanner
    {
        /// <summary>Bump when any output for the same inputs changes (snapshot tests pin it).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 1;

        /// <summary>Wall height (m): ten character jumps.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float WallHeightM = 12f;

        /// <summary>Distance (m) from a border at which its wall reaches the ground.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float WallHalfWidthM = 7f;

        /// <summary>Half width (m) of the wall's flat top.</summary>
        /// <remarks>
        /// Pre: none. Post: constant; the face between it and WallHalfWidthM rises 12 m over 4.5 m (≈ 69°), steeper than
        /// the character's 50° walkable slope. Side effects: none.
        /// </remarks>
        public const float WallTopHalfWidthM = 2.5f;

        /// <summary>Radius (m) of the boss arena wall around the boss spawn; the lair's pillar ring (16 m) stands inside.</summary>
        /// <remarks>Pre: none. Post: constant > WallHalfWidthM. Side effects: none.</remarks>
        public const float ArenaRadiusM = 36f;

        /// <summary>Distance (m) from the arena gate inside which the arena wall is fully removed.</summary>
        /// <remarks>Pre: none. Post: constant > WallHalfWidthM, so a road crossing the wall squarely stays in the opening. Side effects: none.</remarks>
        public const float ArenaGateInnerM = 9f;

        /// <summary>Distance (m) from the arena gate at which the arena wall is back to full height; the seal has this radius.</summary>
        /// <remarks>Pre: none. Post: constant > ArenaGateInnerM. Side effects: none.</remarks>
        public const float ArenaGateOuterM = 13f;

        // Spawns keep this far beyond the wall foot, so a pack (members ≤ 5 m around its centre) stays off the slope.
        private const float SpawnMarginM = 3f;
        private const float PassInnerMinM = 5f;
        private const float PassInnerSpanM = 4f;
        private const float PassFadeM = 4f;
        // A shortcut's stub reaches this far past the wall foot on both sides.
        private const float StubMarginM = 4f;
        // Nothing but the boss road may come this close to the arena wall's centre line, or the wall would stand in it.
        private const float ArenaKeepOutM = WallHalfWidthM + 5f;

        /// <summary>The borderClearanceRad to give RunPlanner.Generate so nothing spawns on or inside a room wall.</summary>
        /// <remarks>Pre: planet valid. Post: (WallHalfWidthM + 3 m) / Radius, within RunPlanner's [0, 0.1]. Side effects: none.</remarks>
        public static float SpawnBorderClearanceRad(in PlanetConfig planet)
        {
            Contract.Requires(planet.IsValid, "planet must be valid");
            return (WallHalfWidthM + SpawnMarginM) / planet.Radius;
        }

        /// <summary>Generates the dungeon of <paramref name="plan"/>.</summary>
        /// <remarks>
        /// Pre: planet valid and the one plan and layout were generated for; plan and layout non-null, layout generated
        ///      from plan; isLand null (all land) or deterministic.
        /// Post: one gate at every point where a road crosses a region border; room walls are removed along every road, so
        ///       every road is walkable end to end and every room is reachable from the landing room.
        ///       HasLock: the boss road enters the arena circle exactly once, at ArenaGate, and no other road, nor the key
        ///       elite, nor the landing point lies within ArenaRadiusM + 12 m of ArenaCenter.
        ///       Shortcuts (Road == −1) join Gabriel neighbours not yet connected, never touch a protected dead end, and sit
        ///       on land away from other borders; their number and the number of open borders grow with OpennessPercent.
        ///       Rooms[r] == Treasure iff r has exactly one connected neighbour and is not the landing, boss or key room.
        ///       Same inputs → same layout.
        /// Side effects: allocates; calls isLand.
        /// </remarks>
        public static DungeonLayout Generate(in PlanetConfig planet, RunPlan plan, RunLayout layout, Func<float3, bool> isLand)
        {
            Contract.Requires(planet.IsValid, "planet must be valid");
            Contract.Requires(plan != null && layout != null, "plan and layout must not be null");
            IReadOnlyList<RunRegion> regions = plan.Regions;
            int n = regions.Count;
            float radius = planet.Radius;
            int boss = plan.BossRegion;
            int landing = plan.LandingRegion;
            var sites = new float3[n];
            for (int i = 0; i < n; i++)
            {
                sites[i] = regions[i].Center;
            }

            var rng = new Pcg32(plan.Seed, 'G');
            int openness = (int)rng.NextUInt(101);
            float passInner = PassInnerMinM + PassInnerSpanM * openness / 100f;
            float passOuter = passInner + PassFadeM;

            var gates = new List<DungeonGate>();
            var corridors = new List<float3x2>();
            var connected = new bool[n * n];
            // A road segment matters to the walls only where a wall can be: within reach of some border.
            float corridorReach = (WallHalfWidthM + passOuter + 8f) / radius;
            for (int r = 0; r < layout.Roads.Count; r++)
            {
                RunRoad road = layout.Roads[r];
                int prev = RunPlanner.RegionAt(plan, road.Points[0]);
                for (int i = 1; i < road.Points.Count; i++)
                {
                    float3 p0 = road.Points[i - 1];
                    float3 p1 = road.Points[i];
                    if (RunPlanner.BorderDistanceRad(sites, p0) <= corridorReach ||
                        RunPlanner.BorderDistanceRad(sites, p1) <= corridorReach)
                    {
                        corridors.Add(new float3x2(p0, p1));
                    }

                    int cur = RunPlanner.RegionAt(plan, p1);
                    if (cur == prev)
                    {
                        continue;
                    }

                    float3 normal = sites[prev] - sites[cur];
                    float f0 = math.dot(p0, normal);
                    float f1 = math.dot(p1, normal);
                    float t = f0 - f1 > 0f ? math.saturate(f0 / (f0 - f1)) : 0.5f;
                    AddGate(gates, connected, n, math.normalize(math.lerp(p0, p1, t)), prev, cur, r);
                    prev = cur;
                }
            }

            int keySpawn = -1;
            foreach (RunQuestStep step in plan.Quest)
            {
                if (step.Kind == QuestStepKind.DefeatElite)
                {
                    keySpawn = step.TargetSpawn;
                }
            }

            Contract.Ensures(keySpawn >= 0, "the quest chain must have a DefeatElite target");
            int keyRegion = RunPlanner.RegionAt(plan, plan.Spawns[keySpawn].Direction);
            float3 arenaCenter = plan.LandingDirection;
            foreach (RunSpawn s in plan.Spawns)
            {
                if (s.Role == SpawnRole.Boss)
                {
                    arenaCenter = s.Direction;
                }
            }

            if (!TrySealArena(plan, layout, arenaCenter, plan.Spawns[keySpawn].Direction, radius, out float3 arenaGate))
            {
                keySpawn = -1;
            }

            var degree = new int[n];
            Degrees(connected, n, degree);
            var protectedRoom = new bool[n];
            var deadEnds = new List<int>();
            for (int r = 0; r < n; r++)
            {
                if (degree[r] == 1 && r != landing && r != boss && r != keyRegion)
                {
                    deadEnds.Add(r);
                    protectedRoom[r] = rng.NextUInt(100) >= (uint)(openness / 2);
                }
            }

            if (deadEnds.Count > 0)
            {
                // At least one dead end always survives the shortcuts (Q-W2: 死路寶藏房).
                protectedRoom[deadEnds[(int)rng.NextUInt((uint)deadEnds.Count)]] = true;
            }

            var candidates = new List<int2>();
            for (int a = 0; a < n; a++)
            {
                foreach (int b in regions[a].Neighbors)
                {
                    if (b > a && !connected[a * n + b] && !protectedRoom[a] && !protectedRoom[b])
                    {
                        candidates.Add(new int2(a, b));
                    }
                }
            }

            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = (int)rng.NextUInt((uint)(i + 1));
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            // Up to half of the candidates at full openness; one even at zero, so every planet has a loop when it can.
            int wanted = math.max(1, (openness * candidates.Count + 100) / 200);
            float stub = (WallHalfWidthM + StubMarginM) / radius;
            float arenaChord = Chord((ArenaRadiusM + ArenaKeepOutM + passOuter) / radius);
            for (int i = 0; i < candidates.Count && wanted > 0; i++)
            {
                int a = candidates[i].x;
                int b = candidates[i].y;
                // Gabriel neighbours have no third site inside their diametral sphere, so this point's two nearest sites
                // are exactly a and b: it lies on their shared border, not in some other room.
                float3 mid = math.normalize(sites[a] + sites[b]);
                float3 across = math.normalize(sites[b] - sites[a] - mid * math.dot(sites[b] - sites[a], mid));
                float3 endA = math.normalize(mid - across * stub);
                float3 endB = math.normalize(mid + across * stub);
                // Both ends must come out in open ground: clear of every other border (a three-room corner would put a
                // second wall across the exit), on land, and away from the arena wall, which no corridor cuts.
                bool clear = RunPlanner.BorderDistanceRad(sites, endA) >= stub * 0.9f &&
                             RunPlanner.BorderDistanceRad(sites, endB) >= stub * 0.9f &&
                             (isLand == null || (isLand(mid) && isLand(endA) && isLand(endB))) &&
                             (keySpawn < 0 || math.distance(mid, arenaCenter) > arenaChord);
                if (!clear)
                {
                    continue;
                }

                AddGate(gates, connected, n, mid, a, b, -1);
                corridors.Add(new float3x2(endA, endB));
                wanted--;
            }

            var open = new bool[n * n];
            for (int a = 0; a < n; a++)
            {
                for (int b = a + 1; b < n; b++)
                {
                    if (connected[a * n + b] && rng.NextUInt(100) < (uint)(openness / 2))
                    {
                        open[a * n + b] = true;
                        open[b * n + a] = true;
                    }
                }
            }

            Degrees(connected, n, degree);
            var rooms = new RoomRole[n];
            for (int r = 0; r < n; r++)
            {
                rooms[r] = r == landing ? RoomRole.Landing
                    : r == boss ? RoomRole.Boss
                    : r == keyRegion && keySpawn >= 0 ? RoomRole.Key
                    : degree[r] == 1 && r != keyRegion ? RoomRole.Treasure
                    : RoomRole.Normal;
            }

            return new DungeonLayout(openness, rooms, gates.ToArray(), open, connected, corridors.ToArray(), keySpawn,
                arenaCenter, arenaGate, passInner, passOuter);
        }

        /// <summary>Builds the terrain walls of <paramref name="dungeon"/>.</summary>
        /// <remarks>
        /// Pre: planet valid; plan and dungeon non-null, dungeon generated from plan on this planet.
        /// Post: walls on every region border that is not an open border, removed along every road and shortcut; when
        ///       dungeon.HasLock, also the arena ring with its gate.
        /// Side effects: allocates persistent native memory (Dispose required).
        /// </remarks>
        public static PlanetWalls BuildWalls(in PlanetConfig planet, RunPlan plan, DungeonLayout dungeon)
        {
            Contract.Requires(planet.IsValid, "planet must be valid");
            Contract.Requires(plan != null && dungeon != null, "plan and dungeon must not be null");
            Contract.Requires(dungeon.Rooms.Count == plan.Regions.Count, "dungeon must belong to plan");
            var sites = new float3[plan.Regions.Count];
            for (int i = 0; i < sites.Length; i++)
            {
                sites[i] = plan.Regions[i].Center;
            }

            WallRing? ring = null;
            if (dungeon.HasLock)
            {
                ring = new WallRing
                {
                    Center = dungeon.ArenaCenter,
                    RadiusM = ArenaRadiusM,
                    Gate = dungeon.ArenaGate,
                    GateInnerM = ArenaGateInnerM,
                    GateOuterM = ArenaGateOuterM,
                };
            }

            return PlanetWalls.Create(planet, sites, dungeon.OpenPairs, dungeon.Corridors, dungeon.PassInnerM,
                dungeon.PassOuterM, ring, WallHeightM, WallHalfWidthM, WallTopHalfWidthM);
        }

        /// <summary>Order-sensitive FNV-1a digest of the dungeon's discrete content (no float directions).</summary>
        /// <remarks>
        /// Pre: dungeon non-null. Post: same dungeon → same value; covers openness, key, room roles, gate pairs/roads,
        ///      corridor count and open borders. Side effects: none.
        /// </remarks>
        public static ulong Hash(DungeonLayout dungeon)
        {
            Contract.Requires(dungeon != null, "dungeon must not be null");
            ulong h = 14695981039346656037UL;
            Mix(ref h, (ulong)dungeon.OpennessPercent);
            Mix(ref h, (ulong)(dungeon.KeySpawn + 1));
            foreach (RoomRole role in dungeon.Rooms)
            {
                Mix(ref h, (ulong)role);
            }

            Mix(ref h, (ulong)dungeon.Gates.Count);
            foreach (DungeonGate g in dungeon.Gates)
            {
                Mix(ref h, (ulong)g.RegionA);
                Mix(ref h, (ulong)g.RegionB);
                Mix(ref h, (ulong)(g.Road + 1));
            }

            Mix(ref h, (ulong)dungeon.Corridors.Length);
            foreach (bool open in dungeon.OpenPairs)
            {
                Mix(ref h, open ? 1UL : 0UL);
            }

            return h;
        }

        // The arena is a circle around the boss spawn. It can be locked only when its wall has exactly one hole to plug:
        // the boss road must enter once and end inside, and nothing else may need to pass through or stand in the wall.
        private static bool TrySealArena(RunPlan plan, RunLayout layout, float3 center, float3 key, float radius,
            out float3 gate)
        {
            gate = center;
            float ring = Chord(ArenaRadiusM / radius);
            float keepOut = Chord((ArenaRadiusM + ArenaKeepOutM) / radius);
            if (math.distance(key, center) <= keepOut || math.distance(plan.LandingDirection, center) <= keepOut)
            {
                return false;
            }

            int crossings = 0;
            foreach (RunRoad road in layout.Roads)
            {
                // The boss room is the BFS-farthest region, so it is never a road's From end.
                bool bossRoad = road.ToRegion == plan.BossRegion;
                for (int i = 0; i < road.Points.Count; i++)
                {
                    float d1 = math.distance(road.Points[i], center);
                    if (!bossRoad && d1 <= keepOut)
                    {
                        return false;
                    }

                    if (i == 0)
                    {
                        continue;
                    }

                    float d0 = math.distance(road.Points[i - 1], center);
                    if ((d0 < ring) == (d1 < ring))
                    {
                        continue;
                    }

                    crossings++;
                    gate = math.normalize(math.lerp(road.Points[i - 1], road.Points[i], (d0 - ring) / (d0 - d1)));
                }

                if (bossRoad && math.distance(road.Points[road.Points.Count - 1], center) >= ring)
                {
                    return false;
                }
            }

            return crossings == 1;
        }

        private static float Chord(float angleRad) => 2f * math.sin(0.5f * angleRad);

        private static void AddGate(List<DungeonGate> gates, bool[] connected, int n, float3 center, int a, int b, int road)
        {
            gates.Add(new DungeonGate(center, math.min(a, b), math.max(a, b), road));
            connected[a * n + b] = true;
            connected[b * n + a] = true;
        }

        private static void Degrees(bool[] connected, int n, int[] degree)
        {
            for (int a = 0; a < n; a++)
            {
                degree[a] = 0;
                for (int b = 0; b < n; b++)
                {
                    if (connected[a * n + b])
                    {
                        degree[a]++;
                    }
                }
            }
        }

        private static void Mix(ref ulong h, ulong v)
        {
            for (int i = 0; i < 8; i++)
            {
                h ^= (v >> (8 * i)) & 0xFF;
                h *= 1099511628211UL;
            }
        }
    }
}
