using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
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
        public const int GeneratorVersion = 5;

        /// <summary>Minimum planet radius (m) where non-mainland settlements and space stations appear (Planet size+).</summary>
        /// <remarks>Pre: none. Post: constant 960 m. Side effects: none.</remarks>
        public const float PlanetMinRadiusM = 960f;

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
        private const float LairRadiusM = 16f;
        private const int LairPillars = 6;
        private const uint RuinsChancePercent = 50;

        /// <summary>Returns the allowed starting-town kinds for <paramref name="size"/>.</summary>
        /// <remarks>
        /// REQ: docs/design/planet-world.md#道路與建築 — Asteroid: Tribe; Moon: Tribe/Town; Planet: Tribe/Town/City;
        ///      Supergiant: Tribe/Town/City/CityState.
        /// Pre: size is a defined PlanetSize. Post: non-empty array of starting-town kinds. Side effects: none.
        /// </remarks>
        public static SettlementKind[] AllowedTownKinds(PlanetSize size)
        {
            switch (size)
            {
                case PlanetSize.Asteroid:
                    return new[] { SettlementKind.Tribe };
                case PlanetSize.Moon:
                    return new[] { SettlementKind.Tribe, SettlementKind.Town };
                case PlanetSize.Planet:
                    return new[] { SettlementKind.Tribe, SettlementKind.Town, SettlementKind.City };
                case PlanetSize.Supergiant:
                    return new[] { SettlementKind.Tribe, SettlementKind.Town, SettlementKind.City, SettlementKind.CityState };
                default:
                    throw new ArgumentOutOfRangeException(nameof(size), size, "undefined PlanetSize");
            }
        }

        /// <summary>Generates the layout of <paramref name="plan"/>.</summary>
        /// <remarks>
        /// Pre: planet valid and the one plan was generated for; plan non-null; isLand null (all land) or deterministic.
        /// Post: Main and Trail roads form a spanning tree of the region graph: one Main road per consecutive MainRoute
        ///       pair, one Trail from every off-route region to its BFS parent (fewer hops, smallest index). Then up to
        ///       1 + U{0,1} Loop roads between Gabriel neighbours not yet joined whose tree path is ≤ 3 roads (shortest
        ///       first), so each closes a cycle of ≤ 4 roads; neither end is the boss region or a leaf of the tree;
        ///       fewer when fewer candidates exist. Every road vertex is unit, the
        ///       first/last equal the end sites, and Bridge[i] == (isLand != null &amp;&amp; !isLand(Points[i])).
        ///       Settlements: one starting town (Tribe/Town/City/CityState allowed by planet size) centred at
        ///       LandingDirection, one Village per route region strictly between landing and boss, one Lair of 6
        ///       pillars around the boss spawn, and Ruins on about half the off-route regions. Every building satisfies
        ///       isLand; buildings of one settlement do not overlap. Same inputs → same layout.
        /// Side effects: allocates; calls isLand.
        /// </remarks>
        public static RunLayout Generate(in PlanetConfig planet, RunPlan plan, Func<float3, bool> isLand) =>
            Generate(planet, plan, isLand, null);

        /// <summary>Generates the layout of <paramref name="plan"/> with roads that follow the terrain.</summary>
        /// <remarks>
        /// REQ: planet-world.md#道路與建築 (道路依坡度繞行). Pre: as the three-argument overload; groundAltitude null or
        ///      deterministic (altitude in m of a unit direction).
        /// Post: as the three-argument overload, which this equals when groundAltitude is null. Otherwise every road
        ///       first gains a small natural meander and is then relaxed sideways along the contours: interior vertices
        ///       move along their own sideways axis by at most min(36 m, 18 % of the road's span) to lower the grade
        ///       and stay out of the sea, while the stretches through settlements (the first and last 40 m of a main
        ///       road, 24 m of a trail or loop) stay where the three-argument overload puts them, so lots still line
        ///       the road. The relaxation is a fixed number of gradient steps on a continuous cost, so heights a few
        ///       millimetres apart (ADR-0007 §2) move vertices millimetres, never to a different valley. Bridge flags
        ///       are taken after relaxing. Settlements are identical to the three-argument overload's.
        /// Side effects: allocates; calls isLand and groundAltitude.
        /// </remarks>
        public static RunLayout Generate(in PlanetConfig planet, RunPlan plan, Func<float3, bool> isLand,
            Func<float3, float> groundAltitude)
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
                roads.Add(Road(RoadClass.Main, route[i], route[i + 1], regions, radius, isLand, ref rng,
                    groundAltitude, plan.Seed, roads.Count));
            }

            for (int r = 0; r < regions.Count; r++)
            {
                if (!onRoute.Contains(r))
                {
                    roads.Add(Road(RoadClass.Trail, Parent(regions, r), r, regions, radius, isLand, ref rng,
                        groundAltitude, plan.Seed, roads.Count));
                }
            }

            var settlements = new List<RunSettlement>();
            var buildings = new List<RunBuilding>();

            float3 landing = plan.LandingDirection;
            float3 toNext = Tangent(landing, regions[route[1]].Center);
            SettlementKind townKind = PickTownKind(SizeForRadius(radius), ref rng);
            int townLots = TownLotCount(townKind, ref rng);
            AddLots(settlements, buildings, plan.LandingRegion, townKind, landing, toNext,
                MainWidthM, townLots, radius, isLand, ref rng);

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

            // REQ: docs/design/settlement-structures.md#交通工具 — loops for vehicles. Drawn last so the tree roads and
            // every settlement keep the exact v2 draws; only the appended loop roads differ.
            int loops = 1 + (int)rng.NextUInt(2);
            foreach ((int a, int b) in LoopCandidates(regions, roads, route[route.Count - 1]))
            {
                if (loops-- == 0)
                {
                    break;
                }

                roads.Add(Road(RoadClass.Loop, a, b, regions, radius, isLand, ref rng, groundAltitude, plan.Seed,
                    roads.Count));
            }

            if (radius >= PlanetMinRadiusM)
            {
                AddNonMainlandSettlements(settlements, buildings, plan, radius, isLand);
            }

            return new RunLayout(roads.ToArray(), settlements.ToArray(), buildings.ToArray());
        }

        private const int MaxLoopRoads = 4;

        // Neighbour pairs (a < b) not joined by a road whose tree path is short enough, nearest first, ties by (a, b).
        // Never at the boss region or a tree leaf: DungeonPlanner turns every road crossing into a gate, and a loop
        // there would bypass the sealed arena or erase the dead ends that hold treasure (planet-world.md#地表地牢).
        // Pure graph + site geometry: no RNG, so the candidate order never depends on earlier draws.
        private static List<(int, int)> LoopCandidates(IReadOnlyList<RunRegion> regions, List<RunRoad> tree, int boss)
        {
            var adjacent = new List<int>[regions.Count];
            for (int r = 0; r < regions.Count; r++)
            {
                adjacent[r] = new List<int>();
            }

            foreach (RunRoad road in tree)
            {
                adjacent[road.FromRegion].Add(road.ToRegion);
                adjacent[road.ToRegion].Add(road.FromRegion);
            }

            var found = new List<(int a, int b, float dot)>();
            var depth = new int[regions.Count];
            var queue = new Queue<int>();
            for (int a = 0; a < regions.Count; a++)
            {
                for (int i = 0; i < depth.Length; i++)
                {
                    depth[i] = -1;
                }

                depth[a] = 0;
                queue.Enqueue(a);
                while (queue.Count > 0)
                {
                    int at = queue.Dequeue();
                    if (depth[at] == MaxLoopRoads - 1)
                    {
                        continue;
                    }

                    foreach (int n in adjacent[at])
                    {
                        if (depth[n] < 0)
                        {
                            depth[n] = depth[at] + 1;
                            queue.Enqueue(n);
                        }
                    }
                }

                if (a == boss || adjacent[a].Count < 2)
                {
                    continue;
                }

                foreach (int b in regions[a].Neighbors)
                {
                    if (b == boss || adjacent[b].Count < 2)
                    {
                        continue;
                    }

                    // depth 1 = already a road; > 1 within the bound = closes a cycle of depth + 1 roads.
                    if (b > a && depth[b] > 1)
                    {
                        found.Add((a, b, math.dot(regions[a].Center, regions[b].Center)));
                    }
                }
            }

            found.Sort((x, y) => x.dot != y.dot ? y.dot.CompareTo(x.dot) : x.a != y.a ? x.a.CompareTo(y.a) : x.b.CompareTo(y.b));
            return found.ConvertAll(f => (f.a, f.b));
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
                Mix(ref h, (ulong)s.Domain);
                Mix(ref h, (ulong)s.Region);
                Mix(ref h, (ulong)s.BuildingCount);
                Mix(ref h, (ulong)math.round(s.AltitudeM * 100f));
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
            Func<float3, bool> isLand, ref Pcg32 rng, Func<float3, float> ground, ulong seed, int roadIndex)
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
            }

            if (ground != null)
            {
                FollowTerrain(points, roadClass, theta * radius, radius, ground, seed, roadIndex);
            }

            for (int i = 0; i <= segments; i++)
            {
                bridge[i] = isLand != null && !isLand(points[i]);
            }

            return new RunRoad(roadClass, from, to, roadClass == RoadClass.Main ? MainWidthM : TrailWidthM, points, bridge);
        }

        // REQ: planet-world.md#道路與建築 (道路依坡度繞行; 數值暫定).
        private const int RelaxSteps = 32;
        private const float RelaxProbeM = 1.5f;
        private const float RelaxStepCapM = 1.5f;
        private const float RelaxRate = 70f;
        private const float GradeWeight = 1f;
        private const float BendWeight = 0.004f;
        private const float AnchorWeight = 0.00012f;
        private const float SeaWeight = 0.08f;
        private const float MaxShiftM = 36f;
        private const float MaxShiftShare = 0.18f;
        private const float MainPinM = 40f;
        private const float TrailPinM = 24f;

        // Bends a road round hills instead of over them: lateral offsets x_i (m) along each vertex's own sideways axis
        // minimise Σ grade² + bend·(Δ²x)² + anchor·x² + sea·depth², by gradient steps with numeric height derivatives.
        // A separate stream ('MEAN', road index) seeds a small meander first, so tree roads and settlements keep their
        // v4 draws.
        private static void FollowTerrain(float3[] points, RoadClass roadClass, float spanM, float radius,
            Func<float3, float> ground, ulong seed, int roadIndex)
        {
            int n = points.Length;
            if (n < 3)
            {
                return;
            }

            var sideAxis = new float3[n];
            var weight = new float[n];
            var x = new float[n];
            var h = new float[n];
            var slope = new float[n];
            var grad = new float[n];
            float pinM = roadClass == RoadClass.Main ? MainPinM : TrailPinM;
            float maxShift = math.min(MaxShiftM, MaxShiftShare * spanM);
            float segM = spanM / (n - 1);
            for (int i = 0; i < n; i++)
            {
                float3 ahead = points[math.min(i + 1, n - 1)] - points[math.max(i - 1, 0)];
                sideAxis[i] = math.normalize(math.cross(points[i], ahead));
                float fromEnd = math.min(i, n - 1 - i) * segM;
                weight[i] = math.smoothstep(pinM, pinM + 20f, fromEnd);
            }

            // Natural meander: two short wavelengths (40–90 m) on top of the planner's two long lobes.
            var meander = new Pcg32(seed ^ 0x4D45414EUL, (ulong)roadIndex);
            float amp = math.min(6f, 0.03f * spanM);
            float w1 = spanM / (40f + meander.NextFloat() * 50f);
            float w2 = spanM / (25f + meander.NextFloat() * 30f);
            float p1 = meander.NextFloat() * math.PI * 2f;
            float p2 = meander.NextFloat() * math.PI * 2f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                x[i] = amp * (0.7f * math.sin(t * w1 * math.PI * 2f + p1) + 0.3f * math.sin(t * w2 * math.PI * 2f + p2)) * weight[i];
            }

            for (int step = 0; step < RelaxSteps; step++)
            {
                for (int i = 0; i < n; i++)
                {
                    h[i] = ground(At(points[i], sideAxis[i], x[i], radius));
                    if (weight[i] <= 0f)
                    {
                        slope[i] = 0f;
                        continue;
                    }

                    float up = ground(At(points[i], sideAxis[i], x[i] + RelaxProbeM, radius));
                    float down = ground(At(points[i], sideAxis[i], x[i] - RelaxProbeM, radius));
                    slope[i] = (up - down) / (2f * RelaxProbeM);
                }

                for (int i = 1; i < n - 1; i++)
                {
                    // d/dx_i of the grades of the two segments that share vertex i.
                    float g = 2f * GradeWeight / (segM * segM) * ((h[i] - h[i - 1]) - (h[i + 1] - h[i])) * slope[i];
                    g += 2f * BendWeight * (Bend(x, i - 1) - 2f * Bend(x, i) + Bend(x, i + 1));
                    g += 2f * AnchorWeight * x[i];
                    g -= 2f * SeaWeight * math.max(0f, -h[i]) * slope[i];
                    grad[i] = g;
                }

                // Jacobi update: every vertex moves on the same snapshot, so the result does not depend on scan order.
                for (int i = 1; i < n - 1; i++)
                {
                    float move = math.clamp(-RelaxRate * grad[i], -RelaxStepCapM, RelaxStepCapM) * weight[i];
                    x[i] = math.clamp(x[i] + move, -maxShift, maxShift);
                }
            }

            for (int i = 1; i < n - 1; i++)
            {
                points[i] = At(points[i], sideAxis[i], x[i], radius);
            }
        }

        // Second difference of the offsets; zero past either end (the ends never move).
        private static float Bend(float[] x, int i)
        {
            if (i <= 0 || i >= x.Length - 1)
            {
                return 0f;
            }

            return x[i - 1] - 2f * x[i] + x[i + 1];
        }

        private static float3 At(float3 point, float3 side, float offsetM, float radius) =>
            math.normalize(point + side * (offsetM / radius));

        private enum LotSurfaceRule
        {
            DryLandOnly,
            DeepSeaOnly,
            AirborneAny,
        }

        // Lots line both sides of a road through the centre: even lots on one side, odd on the other, centred on it.
        private static void AddLots(List<RunSettlement> settlements, List<RunBuilding> buildings, int region,
            SettlementKind kind, float3 center, float3 roadAxis, float roadWidthM, int count, float radius,
            Func<float3, bool> isLand, ref Pcg32 rng) =>
            AddLotsWithDomain(settlements, buildings, region, kind, SettlementDomain.Land, 0f, center, roadAxis,
                roadWidthM, count, radius, isLand, LotSurfaceRule.DryLandOnly, allowEmpty: true, ref rng);

        private static bool AddLotsWithDomain(List<RunSettlement> settlements, List<RunBuilding> buildings, int region,
            SettlementKind kind, SettlementDomain domain, float altitudeM, float3 center, float3 roadAxis,
            float roadWidthM, int count, float radius, Func<float3, bool> isLand, LotSurfaceRule surfaceRule,
            bool allowEmpty, ref Pcg32 rng)
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
                if (isLand != null)
                {
                    bool land = isLand(pos);
                    if (surfaceRule == LotSurfaceRule.DryLandOnly && !land) continue;
                    if (surfaceRule == LotSurfaceRule.DeepSeaOnly && land) continue;
                }

                buildings.Add(new RunBuilding(index, bk, pos, Tangent(pos, -side * normal), width, depth, storeys));
            }

            int built = buildings.Count - first;
            if (!allowEmpty && built == 0)
            {
                return false;
            }

            settlements.Add(new RunSettlement(index, region, kind, center, first, built, domain, altitudeM));
            return true;
        }

        // Places smaller non-mainland settlements (SkyTribe, DeepSeaCity, IslandSettlement) on Planet+ (R >= 960 m)
        // using an independent RNG stream ('O') so mainland roads, towns and ruins stay unaffected.
        private static void AddNonMainlandSettlements(List<RunSettlement> settlements, List<RunBuilding> buildings,
            RunPlan plan, float radius, Func<float3, bool> isLand)
        {
            var rng = new Pcg32(plan.Seed, 'O');
            IReadOnlyList<RunRegion> regions = plan.Regions;

            // 1. SkyTribe: floats inside [0.25 R, 0.85 R], strictly <= 1.0 R stratosphere ceiling.
            int skyRegion = PickNonLandingRegion(regions, plan.LandingRegion, plan.BossRegion, ref rng);
            float3 skySite = regions[skyRegion].Center;
            float skyAltM = math.round(math.lerp(radius * 0.25f, radius * 0.85f, rng.NextFloat()));
            int skyLots = 2 + (int)rng.NextUInt(2);
            AddLotsWithDomain(settlements, buildings, skyRegion, SettlementKind.SkyTribe, SettlementDomain.SkyIsland,
                skyAltM, skySite, RandomAxis(skySite, ref rng), TrailWidthM, skyLots, radius, isLand,
                LotSurfaceRule.AirborneAny, allowEmpty: false, ref rng);

            if (isLand == null)
            {
                return;
            }

            // 2. DeepSeaCity: anchored on ocean seafloor (> 1 m depth), reachable via Dive.
            if (TryFindOceanSite(settlements, regions, radius, isLand, ref rng, out float3 seaSite, out int seaRegion))
            {
                float seaDepthM = 12f + (int)rng.NextUInt(13);
                int seaLots = 3 + (int)rng.NextUInt(2);
                AddLotsWithDomain(settlements, buildings, seaRegion, SettlementKind.DeepSeaCity, SettlementDomain.DeepSea,
                    -seaDepthM, seaSite, RandomAxis(seaSite, ref rng), TrailWidthM, seaLots, radius, isLand,
                    LotSurfaceRule.DeepSeaOnly, allowEmpty: false, ref rng);
            }

            // 3. IslandSettlement: land site separated from starting town by sea, reachable via Swim or Dive.
            if (TryFindIslandSite(settlements, regions, plan.LandingDirection, radius, isLand, ref rng,
                out float3 islandSite, out int islandRegion))
            {
                int islandLots = 2 + (int)rng.NextUInt(2);
                AddLotsWithDomain(settlements, buildings, islandRegion, SettlementKind.IslandSettlement,
                    SettlementDomain.Island, 0f, islandSite, RandomAxis(islandSite, ref rng), TrailWidthM, islandLots,
                    radius, isLand, LotSurfaceRule.DryLandOnly, allowEmpty: false, ref rng);
            }
        }

        private static int PickNonLandingRegion(IReadOnlyList<RunRegion> regions, int landingRegion, int bossRegion,
            ref Pcg32 rng)
        {
            if (regions.Count <= 1) return landingRegion;
            int start = (int)rng.NextUInt((uint)regions.Count);
            for (int offset = 0; offset < regions.Count; offset++)
            {
                int r = (start + offset) % regions.Count;
                if (r != landingRegion && r != bossRegion) return r;
            }

            return (landingRegion + 1) % regions.Count;
        }

        private static bool TryFindOceanSite(List<RunSettlement> settlements, IReadOnlyList<RunRegion> regions, float radius,
            Func<float3, bool> isLand, ref Pcg32 rng, out float3 site, out int region)
        {
            float3 fallback = float3.zero;
            bool hasFallback = false;
            for (int attempt = 0; attempt < 64; attempt++)
            {
                float3 p = SampleSphere(ref rng);
                if (isLand(p) || HasSurfaceSettlementNear(settlements, p, radius, 48f))
                {
                    continue;
                }

                if (!hasFallback)
                {
                    fallback = p;
                    hasFallback = true;
                }

                PlanetFrame f = PlanetFrame.FromUp(p);
                if (!isLand(Offset(p, f.East * 24f, radius)) &&
                    !isLand(Offset(p, -f.East * 24f, radius)) &&
                    !isLand(Offset(p, f.North * 24f, radius)) &&
                    !isLand(Offset(p, -f.North * 24f, radius)))
                {
                    site = p;
                    region = NearestRegion(regions, p);
                    return true;
                }
            }

            if (hasFallback)
            {
                site = fallback;
                region = NearestRegion(regions, fallback);
                return true;
            }

            site = float3.zero;
            region = 0;
            return false;
        }

        private static bool TryFindIslandSite(List<RunSettlement> settlements, IReadOnlyList<RunRegion> regions,
            float3 landingDir, float radius, Func<float3, bool> isLand, ref Pcg32 rng, out float3 site, out int region)
        {
            for (int attempt = 0; attempt < 64; attempt++)
            {
                float3 p = SampleSphere(ref rng);
                if (!isLand(p) || HasSurfaceSettlementNear(settlements, p, radius, 45f))
                {
                    continue;
                }

                bool separatedByWater = false;
                for (int step = 1; step <= 3 && !separatedByWater; step++)
                {
                    float t = step * 0.25f;
                    float3 blended = math.lerp(landingDir, p, t);
                    if (math.lengthsq(blended) <= 1e-6f)
                    {
                        continue;
                    }

                    float3 arc = math.normalize(blended);
                    if (!isLand(arc))
                    {
                        separatedByWater = true;
                    }
                }

                if (separatedByWater)
                {
                    site = p;
                    region = NearestRegion(regions, p);
                    return true;
                }
            }

            site = float3.zero;
            region = 0;
            return false;
        }

        private static bool HasSurfaceSettlementNear(List<RunSettlement> settlements, float3 p, float radius, float minGapM)
        {
            float maxDist2 = (minGapM / radius) * (minGapM / radius);
            for (int i = 0; i < settlements.Count; i++)
            {
                RunSettlement s = settlements[i];
                if (s.Domain == SettlementDomain.SkyIsland) continue;
                if (math.distancesq(s.Center, p) < maxDist2) return true;
            }

            return false;
        }

        private static int NearestRegion(IReadOnlyList<RunRegion> regions, float3 dir)
        {
            int best = 0;
            float bestDot = float.MinValue;
            for (int i = 0; i < regions.Count; i++)
            {
                float d = math.dot(regions[i].Center, dir);
                if (d > bestDot)
                {
                    bestDot = d;
                    best = i;
                }
            }

            return best;
        }

        private static float3 SampleSphere(ref Pcg32 rng)
        {
            float z = rng.NextFloat() * 2f - 1f;
            float phi = rng.NextFloat() * 2f * math.PI;
            float r = math.sqrt(math.max(0f, 1f - z * z));
            return math.normalize(new float3(r * math.cos(phi), z, r * math.sin(phi)));
        }

        private static float3 RandomAxis(float3 up, ref Pcg32 rng)
        {
            PlanetFrame f = PlanetFrame.FromUp(up);
            float ang = rng.NextFloat() * 2f * math.PI;
            return math.normalize(f.East * math.cos(ang) + f.North * math.sin(ang));
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

        private static PlanetSize SizeForRadius(float radius)
        {
            if (radius <= 480f) return PlanetSize.Asteroid;
            if (radius <= 800f) return PlanetSize.Moon;
            if (radius <= 1120f) return PlanetSize.Planet;
            return PlanetSize.Supergiant;
        }

        private static SettlementKind PickTownKind(PlanetSize size, ref Pcg32 rng)
        {
            SettlementKind[] allowed = AllowedTownKinds(size);
            return allowed[(int)rng.NextUInt((uint)allowed.Length)];
        }

        private static int TownLotCount(SettlementKind kind, ref Pcg32 rng)
        {
            switch (kind)
            {
                case SettlementKind.Tribe:
                    return 4 + (int)rng.NextUInt(2); // 4-5
                case SettlementKind.Town:
                    return 6 + (int)rng.NextUInt(3); // 6-8
                case SettlementKind.City:
                    return 9 + (int)rng.NextUInt(4); // 9-12
                case SettlementKind.CityState:
                    return 13 + (int)rng.NextUInt(4); // 13-16
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a starting town kind");
            }
        }

        private static BuildingKind KindFor(SettlementKind kind, int lot, ref Pcg32 rng)
        {
            uint roll = rng.NextUInt(100);
            switch (kind)
            {
                case SettlementKind.Tribe:
                    return lot == 0 ? BuildingKind.Hall : roll < 60 ? BuildingKind.Tent : BuildingKind.House;
                case SettlementKind.Town:
                    return lot == 0 ? BuildingKind.Hall : roll < 25 ? BuildingKind.Tower : BuildingKind.House;
                case SettlementKind.City:
                    if (lot == 0) return BuildingKind.Hall;
                    if (lot == 1) return BuildingKind.Tower;
                    return roll < 40 ? BuildingKind.Tower : BuildingKind.House;
                case SettlementKind.CityState:
                    if (lot == 0 || lot == 1) return BuildingKind.Hall;
                    if (lot == 2) return BuildingKind.Tower;
                    return roll < 50 ? BuildingKind.Tower : BuildingKind.House;
                case SettlementKind.Village:
                    return lot == 0 ? BuildingKind.Hall : roll < 20 ? BuildingKind.Tower : BuildingKind.House;
                case SettlementKind.IslandSettlement:
                    return lot == 0 ? BuildingKind.Hall : BuildingKind.House;
                case SettlementKind.SkyTribe:
                    return lot == 0 ? BuildingKind.Hall : roll < 50 ? BuildingKind.Tent : BuildingKind.Tower;
                case SettlementKind.DeepSeaCity:
                    return lot == 0 ? BuildingKind.Hall : roll < 45 ? BuildingKind.Tower : BuildingKind.House;
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
