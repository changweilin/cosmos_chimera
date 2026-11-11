using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>
    /// Lays out the road network and settlements of a run from its region graph.
    /// REQ: docs/design/planet-world.md#道路與建築 (user 2026-11-11: 道路、建築全部遊戲隨機生成).
    /// </summary>
    /// <remarks>
    /// Determinism: one Pcg32(seed, 'S') stream drawn in a fixed order (road wiggles in road order, then settlements in
    /// settlement order). Every random number is drawn whether or not its lot survives the land check, so a land
    /// predicate only removes buildings and never shifts the rest of the layout. Decisions read the region graph and
    /// isLand only, never terrain height (ADR-0007 rule 3); isLand carries the same known float-direction risk as spawns.
    /// </remarks>
    public static class SettlementPlanner
    {
        /// <summary>Bump when any output for the same inputs changes (snapshot tests pin it).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 1;

        /// <summary>Paved width of a main road (m).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float MainWidthM = 4f;

        /// <summary>Paved width of a trail (m).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float TrailWidthM = 2.5f;

        /// <summary>Spacing between lots on the same side of a road (m); wider than any building, so lots never overlap.</summary>
        /// <remarks>Pre: none. Post: constant > largest building width (7 m). Side effects: none.</remarks>
        public const float LotSpacingM = 9f;

        private const float SegmentM = 7f;
        private const float SetbackM = 2f;
        private const float OutpostOffsetM = 20f;
        private const float LairRadiusM = 16f;
        private const int LairPillars = 6;
        private const uint RuinsChancePercent = 50;

        /// <summary>Generates the layout of <paramref name="plan"/>.</summary>
        /// <remarks>
        /// Pre: planet valid and the one plan was generated for; plan non-null; isLand null (all land) or deterministic.
        /// Post: Roads form a spanning tree of the region graph: one Main road per consecutive MainRoute pair, one Trail
        ///       from every off-route region to its BFS parent (fewer hops, smallest index). Every road vertex is unit, the
        ///       first/last equal the end sites, and Bridge[i] == (isLand != null &amp;&amp; !isLand(Points[i])).
        ///       Settlements: one Outpost (landing region) beside LandingDirection, one Village per route region strictly
        ///       between landing and boss, one Lair of 6 pillars around the boss spawn, and Ruins on about half the
        ///       off-route regions. Every building satisfies isLand; buildings of one settlement do not overlap.
        ///       Same inputs → same layout.
        /// Side effects: allocates; calls isLand.
        /// </remarks>
        public static RunLayout Generate(in PlanetConfig planet, RunPlan plan, Func<float3, bool> isLand)
        {
            Contract.Requires(planet.IsValid, "planet must be valid");
            Contract.Requires(plan != null, "plan must not be null");
            var rng = new Pcg32(plan.Seed, 'S');
            float radius = planet.Radius;
            IReadOnlyList<RunRegion> regions = plan.Regions;
            IReadOnlyList<int> route = plan.MainRoute;
            var onRoute = new HashSet<int>(route);

            var roads = new List<RunRoad>();
            for (int i = 0; i + 1 < route.Count; i++)
            {
                roads.Add(Road(RoadClass.Main, route[i], route[i + 1], regions, radius, isLand, ref rng));
            }

            for (int r = 0; r < regions.Count; r++)
            {
                if (!onRoute.Contains(r))
                {
                    roads.Add(Road(RoadClass.Trail, Parent(regions, r), r, regions, radius, isLand, ref rng));
                }
            }

            var settlements = new List<RunSettlement>();
            var buildings = new List<RunBuilding>();

            float3 landing = plan.LandingDirection;
            float3 toNext = Tangent(landing, regions[route[1]].Center);
            float3 outpost = Offset(landing, toNext * OutpostOffsetM, radius);
            int tents = 3 + (int)rng.NextUInt(2);
            AddLots(settlements, buildings, plan.LandingRegion, SettlementKind.Outpost, outpost, Tangent(outpost, regions[route[1]].Center),
                MainWidthM, tents, radius, isLand, ref rng);

            for (int i = 1; i + 1 < route.Count; i++)
            {
                float3 site = regions[route[i]].Center;
                int count = 3 + (int)rng.NextUInt(4);
                AddLots(settlements, buildings, route[i], SettlementKind.Village, site, Tangent(site, regions[route[i + 1]].Center),
                    MainWidthM, count, radius, isLand, ref rng);
            }

            AddLair(settlements, buildings, plan, radius, isLand, ref rng);

            for (int r = 0; r < regions.Count; r++)
            {
                if (onRoute.Contains(r) || rng.NextUInt(100) >= RuinsChancePercent)
                {
                    continue;
                }

                float3 site = regions[r].Center;
                int count = 1 + (int)rng.NextUInt(4);
                AddLots(settlements, buildings, r, SettlementKind.Ruins, site, Tangent(site, regions[Parent(regions, r)].Center),
                    TrailWidthM, count, radius, isLand, ref rng);
            }

            return new RunLayout(roads.ToArray(), settlements.ToArray(), buildings.ToArray());
        }

        /// <summary>Order-sensitive FNV-1a digest of the layout's discrete content (no float directions).</summary>
        /// <remarks>
        /// Pre: layout non-null. Post: same layout → same value; covers road classes, ends, vertex counts and bridge flags,
        ///      settlement kinds/regions/counts and building kinds/storeys.
        /// Side effects: none.
        /// </remarks>
        public static ulong Hash(RunLayout layout)
        {
            Contract.Requires(layout != null, "layout must not be null");
            ulong h = 14695981039346656037UL;
            Mix(ref h, (ulong)layout.Roads.Count);
            foreach (RunRoad road in layout.Roads)
            {
                Mix(ref h, (ulong)road.Class);
                Mix(ref h, (ulong)road.FromRegion);
                Mix(ref h, (ulong)road.ToRegion);
                Mix(ref h, (ulong)road.Points.Count);
                foreach (bool b in road.Bridge)
                {
                    Mix(ref h, b ? 1UL : 0UL);
                }
            }

            Mix(ref h, (ulong)layout.Settlements.Count);
            foreach (RunSettlement s in layout.Settlements)
            {
                Mix(ref h, (ulong)s.Kind);
                Mix(ref h, (ulong)s.Region);
                Mix(ref h, (ulong)s.BuildingCount);
            }

            foreach (RunBuilding b in layout.Buildings)
            {
                Mix(ref h, (ulong)b.Kind);
                Mix(ref h, (ulong)b.Storeys);
                Mix(ref h, (ulong)b.Settlement);
            }

            return h;
        }

        // BFS parent: the smallest-index neighbor one hop closer to landing. Regions' Hops come from RunPlanner's BFS, so
        // such a neighbor exists for every region except landing (which is always on the route).
        private static int Parent(IReadOnlyList<RunRegion> regions, int r)
        {
            int hops = regions[r].Hops;
            foreach (int n in regions[r].Neighbors)
            {
                if (regions[n].Hops == hops - 1)
                {
                    return n;
                }
            }

            throw new ContractException("region " + r + " has no neighbor closer to landing");
        }

        private static RunRoad Road(RoadClass roadClass, int from, int to, IReadOnlyList<RunRegion> regions, float radius,
            Func<float3, bool> isLand, ref Pcg32 rng)
        {
            float3 a = regions[from].Center;
            float3 b = regions[to].Center;
            float theta = math.acos(math.clamp(math.dot(a, b), -1f, 1f));
            int segments = math.max(2, (int)math.ceil(theta * radius / SegmentM));
            float3 axis = math.normalize(math.cross(a, b));
            // Two sine lobes vanish at both ends, so roads bend gently yet still meet the sites exactly. Amplitudes stay
            // ≤ 12 % of the span: enough to stop roads reading as ruler lines, small enough not to swing into neighbours.
            float a1 = (rng.NextFloat() * 2f - 1f) * 0.08f * theta;
            float a2 = (rng.NextFloat() * 2f - 1f) * 0.04f * theta;
            float sinTheta = math.sin(theta);
            var points = new float3[segments + 1];
            var bridge = new bool[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float3 p;
                if (i == 0)
                {
                    p = a;
                }
                else if (i == segments)
                {
                    p = b;
                }
                else
                {
                    float t = (float)i / segments;
                    float3 arc = (math.sin((1f - t) * theta) * a + math.sin(t * theta) * b) / sinTheta;
                    float side = a1 * math.sin(math.PI * t) + a2 * math.sin(2f * math.PI * t);
                    p = math.normalize(arc + axis * side);
                }

                points[i] = p;
                bridge[i] = isLand != null && !isLand(p);
            }

            return new RunRoad(roadClass, from, to, roadClass == RoadClass.Main ? MainWidthM : TrailWidthM, points, bridge);
        }

        // Lots line both sides of a road through the centre: even lots on one side, odd on the other, centred on it.
        private static void AddLots(List<RunSettlement> settlements, List<RunBuilding> buildings, int region,
            SettlementKind kind, float3 center, float3 roadAxis, float roadWidthM, int count, float radius,
            Func<float3, bool> isLand, ref Pcg32 rng)
        {
            int index = settlements.Count;
            int first = buildings.Count;
            float3 normal = math.normalize(math.cross(center, roadAxis));
            int rows = (count + 1) / 2;
            for (int k = 0; k < count; k++)
            {
                BuildingKind bk = KindFor(kind, k, ref rng);
                Footprint(bk, ref rng, out float width, out float depth, out int storeys);
                float side = (k & 1) == 0 ? 1f : -1f;
                float along = (k / 2 - (rows - 1) * 0.5f) * LotSpacingM;
                float lateral = side * (roadWidthM * 0.5f + SetbackM + depth * 0.5f);
                float3 pos = Offset(center, roadAxis * along + normal * lateral, radius);
                if (isLand != null && !isLand(pos))
                {
                    continue;
                }

                buildings.Add(new RunBuilding(index, bk, pos, Tangent(pos, -side * normal), width, depth, storeys));
            }

            settlements.Add(new RunSettlement(index, region, kind, center, first, buildings.Count - first));
        }

        private static void AddLair(List<RunSettlement> settlements, List<RunBuilding> buildings, RunPlan plan, float radius,
            Func<float3, bool> isLand, ref Pcg32 rng)
        {
            float3 center = plan.LandingDirection;
            foreach (RunSpawn s in plan.Spawns)
            {
                if (s.Role == SpawnRole.Boss)
                {
                    center = s.Direction;
                }
            }

            int index = settlements.Count;
            int first = buildings.Count;
            PlanetFrame frame = PlanetFrame.FromUp(center);
            float phase = rng.NextFloat() * 2f * math.PI;
            for (int k = 0; k < LairPillars; k++)
            {
                int storeys = 2 + (int)rng.NextUInt(3);
                float angle = phase + k * (2f * math.PI / LairPillars);
                float3 radial = frame.East * math.cos(angle) + frame.North * math.sin(angle);
                float3 pos = Offset(center, radial * LairRadiusM, radius);
                if (isLand != null && !isLand(pos))
                {
                    continue;
                }

                buildings.Add(new RunBuilding(index, BuildingKind.Pillar, pos, Tangent(pos, -radial), 1.2f, 1.2f, storeys));
            }

            settlements.Add(new RunSettlement(index, plan.BossRegion, SettlementKind.Lair, center, first, buildings.Count - first));
        }

        private static BuildingKind KindFor(SettlementKind kind, int lot, ref Pcg32 rng)
        {
            uint roll = rng.NextUInt(100);
            switch (kind)
            {
                case SettlementKind.Outpost:
                    return BuildingKind.Tent;
                case SettlementKind.Village:
                    return lot == 0 ? BuildingKind.Hall : roll < 20 ? BuildingKind.Tower : BuildingKind.House;
                default:
                    return roll < 50 ? BuildingKind.Tower : BuildingKind.House;
            }
        }

        // Sizes are human scale in metres, independent of planet radius (doors do not grow with the planet).
        private static void Footprint(BuildingKind kind, ref Pcg32 rng, out float width, out float depth, out int storeys)
        {
            uint a = rng.NextUInt(3);
            uint b = rng.NextUInt(3);
            switch (kind)
            {
                case BuildingKind.Tent:
                    width = 3f;
                    depth = 3f;
                    storeys = 1;
                    break;
                case BuildingKind.Hall:
                    width = 7f;
                    depth = 6f;
                    storeys = 2;
                    break;
                case BuildingKind.Tower:
                    width = 3f;
                    depth = 3f;
                    storeys = 3 + (int)a;
                    break;
                default:
                    width = 4f + a;
                    depth = 4f + (b & 1u);
                    storeys = 1 + (int)(b >> 1);
                    break;
            }
        }

        // Unit tangent at p along v's tangential part (toward v along the great circle when v is a direction).
        private static float3 Tangent(float3 p, float3 v) => math.normalize(v - p * math.dot(v, p));

        // Moves a unit direction by a tangent displacement given in metres (small-angle, then back onto the sphere).
        private static float3 Offset(float3 p, float3 tangentM, float radius) => math.normalize(p + tangentM / radius);

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
