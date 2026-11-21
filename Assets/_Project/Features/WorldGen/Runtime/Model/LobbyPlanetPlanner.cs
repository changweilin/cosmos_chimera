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
    /// Deterministic generator for the fixed single-player lobby planet (R = 2560 m):
    /// all 24 general landforms, 8 connected settlements (1 CityState, 2 Cities, 3 Towns, 2 Tribes),
    /// and all 10 advanced terrain wonders / fixed dungeon entrances (the 10 trial masters).
    /// REQ: docs/design/lobby-meta.md#地球大廳.
    /// </summary>
    /// <remarks>
    /// Pure and deterministic: every step uses its own fixed Pcg32 stream derived from <paramref name="seed"/>.
    /// Does not modify any random-planet planner or snapshot.
    /// </remarks>
    public static class LobbyPlanetPlanner
    {
        /// <summary>Bump when any lobby planet output changes.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 2;

        private const int AttemptsPerRegion = 40;
        private const int LobbyLoopRoadCount = 6;
        private const float GoldenAngleRad = 2.39996323f;

        private static readonly HashSet<string> PolarKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "tundra", "glacier", "cirque", "moraine", "ice_field",
        };

        /// <summary>Builds all 24 general landform stamps covering the lobby planet.</summary>
        /// <remarks>
        /// Pre: config valid.
        /// Post: exactly LandformSynth.Kinds.Count (24) distinct stamps, one per landform kind; the 5 cold/polar kinds
        ///       occupy the 5 highest-|y| Fibonacci lattice points. Same inputs → same stamps.
        /// Side effects: allocates persistent native memory owned by the result (Dispose required).
        /// </remarks>
        public static PlanetTemplates BuildTemplates(in PlanetConfig config, ulong seed = LobbyPlanet.Seed)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            IReadOnlyList<string> allKinds = LandformSynth.Kinds;
            int count = allKinds.Count;
            var rng = new Pcg32(seed, 0x4C4F4254UL); // "LOBT"

            // Evenly spaced Fibonacci sphere centres, sorted by |y| descending so polar landforms sit at the poles.
            var centers = new float3[count];
            var orderByLat = new int[count];
            for (int i = 0; i < count; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / count;
                float r = math.sqrt(math.max(0f, 1f - y * y));
                float angle = GoldenAngleRad * i;
                centers[i] = math.normalize(new float3(math.cos(angle) * r, y, math.sin(angle) * r));
                orderByLat[i] = i;
            }

            Array.Sort(orderByLat, (a, b) =>
            {
                int cmp = math.abs(centers[b].y).CompareTo(math.abs(centers[a].y));
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            var polar = new List<string>(5);
            var temperate = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                string kind = allKinds[i];
                (PolarKinds.Contains(kind) ? polar : temperate).Add(kind);
            }

            var sources = new List<TemplateSource>(count);
            var placements = new List<TemplatePlacement>(count);
            int polarCursor = 0;
            int tempCursor = 0;
            for (int slot = 0; slot < count; slot++)
            {
                int pointIdx = orderByLat[slot];
                string kind = polarCursor < polar.Count ? polar[polarCursor++] : temperate[tempCursor++];
                float rot = rng.NextFloat() * 2f * math.PI;
                sources.Add(LandformSynth.Generate(kind, seed, slot));
                placements.Add(new TemplatePlacement(slot, centers[pointIdx], rot, LobbyPlanet.StampHalfAngleRad));
            }

            return PlanetTemplates.Create(config, sources, placements);
        }

        /// <summary>Builds the region graph and wild creature spawns for the lobby planet.</summary>
        /// <remarks>
        /// Pre: config valid; planetType, wildPool (non-empty, ≥ 1 mastery each), vocabulary non-null; isLand null or
        ///      deterministic with at least one land cap of radius RunPlanner.MinLandCapRad.
        /// Post: 32 connected regions; LandingDirection satisfies isLand; wild spawns distributed on land across regions;
        ///       same inputs → same plan.
        /// Side effects: allocates; calls isLand.
        /// </remarks>
        public static RunPlan BuildPlan(in PlanetConfig config, PlanetTypeDef planetType,
            IReadOnlyList<CreatureDef> wildPool, RunPlanVocabulary vocabulary, Func<float3, bool> isLand,
            ulong seed = LobbyPlanet.Seed)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(planetType != null && vocabulary != null, "planetType and vocabulary are required");
            Contract.Requires(wildPool != null && wildPool.Count > 0, "wildPool must be non-empty");

            // Use Adult stage as cap when available so the lobby hosts rich wild variety; fall back to highest stage in pool.
            CreatureStage cap = CreatureStage.Juvenile;
            for (int i = 0; i < wildPool.Count; i++)
            {
                Contract.Requires(wildPool[i] != null && wildPool[i].Mastery.Count > 0, "wild entry needs mastery");
                if (wildPool[i].Stage <= CreatureStage.Adult && wildPool[i].Stage > cap)
                {
                    cap = wildPool[i].Stage;
                }
            }

            return RunPlanner.Generate(config, seed, planetType, cap, wildPool, vocabulary, isLand);
        }

        /// <summary>
        /// Builds the 8 major settlements (1 CityState, 2 Cities, 3 Towns, 2 Tribes) and a connected road network
        /// linking all settlements across the lobby planet.
        /// </summary>
        /// <remarks>
        /// Pre: config valid; plan non-null; isLand null or deterministic; groundAltitude null or deterministic.
        /// Post: Settlements contains exactly 8 major settlements matching <see cref="LobbyPlanet.SettlementRoster"/>
        ///       (1 CityState, 2 Cities, 3 Towns, 2 Tribes), all in SettlementDomain.Land; Roads forms a connected graph
        ///       spanning every region (and therefore every settlement), with Main roads forming the backbone connecting
        ///       all 8 settlements. Same inputs → same layout.
        /// Side effects: allocates; calls isLand and groundAltitude.
        /// </remarks>
        public static RunLayout BuildLayout(in PlanetConfig config, RunPlan plan, Func<float3, bool> isLand,
            Func<float3, float> groundAltitude = null)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(plan != null, "plan must not be null");

            var rng = new Pcg32(plan.Seed, 0x4C4F4253UL); // "LOBS"
            float radius = config.Radius;
            IReadOnlyList<RunRegion> regions = plan.Regions;
            int regionCount = regions.Count;
            Contract.Requires(regionCount >= LobbyPlanet.SettlementRoster.Count,
                "lobby planet requires at least 8 regions");

            // 1. Pick 8 well-separated regions for the 8 major settlements (index 0 = LandingRegion = CityState).
            int[] townRegions = PickTownRegions(regions, plan.LandingRegion, isLand, LobbyPlanet.SettlementRoster.Count);

            // 2. Build a spanning tree of all regions rooted at LandingRegion using BFS parent edges, then mark every
            //    edge lying on the tree paths between the 8 towns as RoadClass.Main so all 8 towns connect via Main roads.
            var parent = new int[regionCount];
            for (int r = 0; r < regionCount; r++)
            {
                parent[r] = r == plan.LandingRegion ? -1 : BfsParent(regions, r);
            }

            var mainEdges = new HashSet<long>();
            for (int t = 1; t < townRegions.Length; t++)
            {
                int cur = townRegions[t];
                while (cur != plan.LandingRegion && cur >= 0 && parent[cur] >= 0)
                {
                    int p = parent[cur];
                    mainEdges.Add(EdgeKey(p, cur));
                    cur = p;
                }
            }

            var roads = new List<RunRoad>();
            var roadEdges = new HashSet<long>();
            for (int r = 0; r < regionCount; r++)
            {
                int p = parent[r];
                if (p < 0)
                {
                    continue;
                }

                long key = EdgeKey(p, r);
                roadEdges.Add(key);
                RoadClass cls = mainEdges.Contains(key) ? RoadClass.Main : RoadClass.Trail;
                roads.Add(SettlementPlanner.Road(cls, p, r, regions, radius, isLand, ref rng, groundAltitude, plan.Seed,
                    roads.Count));
            }

            // 3. Add cross-connecting loop roads between Gabriel neighbours so travel between towns has alternate routes.
            int loopsAdded = 0;
            for (int a = 0; a < regionCount && loopsAdded < LobbyLoopRoadCount; a++)
            {
                foreach (int b in regions[a].Neighbors)
                {
                    if (b <= a || roadEdges.Contains(EdgeKey(a, b)))
                    {
                        continue;
                    }

                    roadEdges.Add(EdgeKey(a, b));
                    roads.Add(SettlementPlanner.Road(RoadClass.Loop, a, b, regions, radius, isLand, ref rng,
                        groundAltitude, plan.Seed, roads.Count));
                    loopsAdded++;
                    if (loopsAdded >= LobbyLoopRoadCount)
                    {
                        break;
                    }
                }
            }

            // 4. Place the 8 major settlements along their connected road tangents on land.
            var settlements = new List<RunSettlement>(LobbyPlanet.SettlementRoster.Count);
            var buildings = new List<RunBuilding>();
            for (int i = 0; i < LobbyPlanet.SettlementRoster.Count; i++)
            {
                int r = townRegions[i];
                SettlementKind kind = LobbyPlanet.SettlementRoster[i];
                float3 anchor = i == 0 ? plan.LandingDirection : EnsureLandAnchor(regions[r].Center, isLand);
                int neighbor = FindConnectedNeighbor(r, roads, regions);
                float3 tangent = neighbor >= 0
                    ? SettlementPlanner.Tangent(anchor, regions[neighbor].Center)
                    : PlanetFrame.FromUp(anchor).East;
                int lotCount = SettlementPlanner.TownLotCount(kind, ref rng);
                SettlementPlanner.AddLots(settlements, buildings, r, kind, anchor, tangent,
                    SettlementPlanner.MainWidthM, lotCount, radius, isLand, ref rng);
            }

            return new RunLayout(roads.ToArray(), settlements.ToArray(), buildings.ToArray());
        }

        /// <summary>
        /// Places all 10 advanced terrain wonders and registers all 10 as fixed dungeon entrances (the 10 trial masters),
        /// plus the space station atop the space elevator.
        /// </summary>
        /// <remarks>
        /// Pre: config valid; plan and layout non-null; isBuildable null or deterministic.
        /// Post: Entrances contains one binding per advanced terrain in AdvancedTerrains.All (10 total when buildable
        ///       land exists); every entrance landmark matches DungeonEntrance.KindFor(terrainId); SpaceStation sits at
        ///       1.25 R above SpaceElevator. Same inputs → same layout.
        /// Side effects: allocates; calls isBuildable.
        /// </remarks>
        public static LandmarkLayout BuildLandmarks(in PlanetConfig config, RunPlan plan, RunLayout layout,
            Func<float3, bool> isBuildable)
        {
            Contract.Requires(config.IsValid, "config must be valid");
            Contract.Requires(plan != null, "plan must not be null");
            Contract.Requires(layout != null, "layout must not be null");

            var rng = new Pcg32(plan.Seed, 0x4C4F424CUL); // "LOBL"
            float radius = config.Radius;
            IReadOnlyList<RunRegion> regions = plan.Regions;
            IReadOnlyList<AdvancedTerrainSpec> allTerrains = AdvancedTerrains.All;

            var clearance = new LayoutClearance(config, layout, LandmarkPlanner.LayoutGapM);
            var placed = new List<RunLandmark>(allTerrains.Count + 1);
            var reserves = new List<float>(allTerrains.Count + 1);
            var entrances = new List<DungeonEntranceSite>(allTerrains.Count);

            // Spread the 10 wonders around their designated hub settlements (#3 City for 5, #7 City for 3, #8 Tribe for 2),
            // preferring distinct non-town regions closest to each hub before reusing regions or falling back to town regions.
            var townRegionSet = new HashSet<int>();
            for (int i = 0; i < layout.Settlements.Count; i++)
            {
                townRegionSet.Add(layout.Settlements[i].Region);
            }

            var usedWonderRegions = new HashSet<int>();
            var candidateRegions = new List<int>(regions.Count);

            for (int i = 0; i < allTerrains.Count; i++)
            {
                AdvancedTerrainSpec spec = allTerrains[i];
                LandmarkKind kind = DungeonEntrance.KindFor(spec.Id);
                int variant = (int)rng.NextUInt((uint)LandmarkPlanner.VariantCount(kind));
                LandmarkPlanner.Size(kind, radius, ref rng, out float footprint, out float height, out float altitude);
                bool solid = kind != LandmarkKind.SkyIsland;

                int hubSettlementIdx = LobbyPlanet.HubSettlementIndexForTerrain(spec.Id);
                BuildHubOrderedRegions(regions, layout, townRegionSet, usedWonderRegions, hubSettlementIdx, candidateRegions);

                bool found = TryPlaceWonder(kind, variant, footprint, height, altitude, solid,
                    candidateRegions, regions, plan, layout, hubSettlementIdx, placed, reserves, clearance,
                    isBuildable, radius, ref rng, out RunLandmark lm);
                if (!found)
                {
                    // Fallback 1: relax building/road clearance within the hub's Voronoi neighborhood.
                    found = TryPlaceWonder(kind, variant, footprint, height, altitude, solid,
                        candidateRegions, regions, plan, layout, hubSettlementIdx, placed, reserves, null,
                        isBuildable, radius, ref rng, out lm);
                }
                if (!found)
                {
                    // Fallback 2: relax hub Voronoi boundary to any buildable land so no trial master is ever lost.
                    found = TryPlaceWonder(kind, variant, footprint, height, altitude, solid,
                        candidateRegions, regions, plan, layout, null, placed, reserves, null,
                        isBuildable, radius, ref rng, out lm);
                }

                if (!found)
                {
                    continue;
                }

                usedWonderRegions.Add(lm.Region);
                int lmIndex = placed.Count;
                placed.Add(lm);
                reserves.Add(footprint);
                entrances.Add(new DungeonEntranceSite(lmIndex, spec.Id));

                if (kind == LandmarkKind.SpaceElevator)
                {
                    int stationVariant = (int)rng.NextUInt((uint)LandmarkPlanner.VariantCount(LandmarkKind.SpaceStation));
                    placed.Add(new RunLandmark(placed.Count, LandmarkKind.SpaceStation, stationVariant, lm.Region,
                        lm.Direction, lm.Facing, 0f, LandmarkPlanner.StationHeightM, lm.HeightM, false));
                    reserves.Add(0f);
                }
            }

            return new LandmarkLayout(placed.ToArray(), radius, entrances.ToArray());
        }

        private static int NearestHubIndex(float3 dir, RunLayout layout, float3 fallback)
        {
            float3 hub3 = layout.Settlements.Count > 2 ? layout.Settlements[2].Center : fallback;
            float3 hub7 = layout.Settlements.Count > 6 ? layout.Settlements[6].Center : fallback;
            float3 hub8 = layout.Settlements.Count > 7 ? layout.Settlements[7].Center : fallback;
            float d3 = math.dot(dir, hub3);
            float d7 = math.dot(dir, hub7);
            float d8 = math.dot(dir, hub8);
            return d3 >= d7 && d3 >= d8 ? 2 : (d7 >= d8 ? 6 : 7);
        }

        private static void BuildHubOrderedRegions(IReadOnlyList<RunRegion> regions, RunLayout layout,
            HashSet<int> townRegionSet, HashSet<int> usedWonderRegions, int hubSettlementIdx, List<int> output)
        {
            output.Clear();
            float3 hubCenter = hubSettlementIdx < layout.Settlements.Count
                ? layout.Settlements[hubSettlementIdx].Center
                : regions[0].Center;

            var localUnusedNonTown = new List<int>(regions.Count);
            var localUsedNonTown = new List<int>(regions.Count);
            var localTown = new List<int>(townRegionSet.Count);
            var outside = new List<int>(regions.Count);

            for (int r = 0; r < regions.Count; r++)
            {
                bool isLocal = NearestHubIndex(regions[r].Center, layout, regions[0].Center) == hubSettlementIdx;
                if (!isLocal)
                {
                    outside.Add(r);
                    continue;
                }

                if (townRegionSet.Contains(r))
                {
                    localTown.Add(r);
                }
                else if (usedWonderRegions.Contains(r))
                {
                    localUsedNonTown.Add(r);
                }
                else
                {
                    localUnusedNonTown.Add(r);
                }
            }

            int CompareByProximity(int a, int b)
            {
                int cmp = math.dot(regions[b].Center, hubCenter).CompareTo(math.dot(regions[a].Center, hubCenter));
                return cmp != 0 ? cmp : a.CompareTo(b);
            }

            localUnusedNonTown.Sort(CompareByProximity);
            localUsedNonTown.Sort(CompareByProximity);
            localTown.Sort(CompareByProximity);
            outside.Sort(CompareByProximity);

            output.AddRange(localUnusedNonTown);
            output.AddRange(localUsedNonTown);
            output.AddRange(localTown);
            output.AddRange(outside);
        }

        private static bool TryPlaceWonder(LandmarkKind kind, int variant, float footprint, float height, float altitude,
            bool solid, List<int> candidateRegions, IReadOnlyList<RunRegion> regions, RunPlan plan, RunLayout layout,
            int? requiredHubIdx, List<RunLandmark> placed, List<float> reserves, LayoutClearance clearance,
            Func<float3, bool> isBuildable, float radius, ref Pcg32 rng, out RunLandmark result)
        {
            int totalAttempts = candidateRegions.Count * AttemptsPerRegion;
            for (int attempt = 0; attempt < totalAttempts; attempt++)
            {
                int r = candidateRegions[attempt / AttemptsPerRegion];
                float3 site = regions[r].Center;
                float gap = LandmarkPlanner.NeighbourGapM(regions, r, radius);
                float minM = footprint + 24f;
                float maxM = math.max(minM + 20f, gap * 0.45f);
                float dist = minM + rng.NextFloat() * (maxM - minM);
                float angle = rng.NextFloat() * 2f * math.PI;
                PlanetFrame frame = PlanetFrame.FromUp(site);
                float3 pos = math.normalize(site + (frame.East * math.cos(angle) + frame.North * math.sin(angle)) * (dist / radius));
                if (requiredHubIdx.HasValue &&
                    NearestHubIndex(pos, layout, regions[0].Center) != requiredHubIdx.Value)
                {
                    continue;
                }

                if (!LandmarkPlanner.Fits(pos, footprint, false, solid, plan, placed, reserves, clearance, isBuildable, radius))
                {
                    continue;
                }

                int owner = LandmarkPlanner.Nearest(regions, pos);
                float3 towardSite = regions[owner].Center - pos * math.dot(regions[owner].Center, pos);
                float3 facing = math.lengthsq(towardSite) > 1e-8f
                    ? math.normalize(towardSite)
                    : PlanetFrame.FromUp(pos).East;
                result = new RunLandmark(placed.Count, kind, variant, owner, pos, facing, footprint, height, altitude, solid);
                return true;
            }

            result = default;
            return false;
        }

        private static int[] PickTownRegions(IReadOnlyList<RunRegion> regions, int landingRegion,
            Func<float3, bool> isLand, int targetCount)
        {
            var chosen = new List<int>(targetCount) { landingRegion };
            var used = new HashSet<int> { landingRegion };

            while (chosen.Count < targetCount)
            {
                int bestLand = -1;
                float bestLandDot = float.PositiveInfinity;
                int bestAny = -1;
                float bestAnyDot = float.PositiveInfinity;

                for (int r = 0; r < regions.Count; r++)
                {
                    if (used.Contains(r))
                    {
                        continue;
                    }

                    // Max-min angular distance: minimize the largest dot product to any already chosen town.
                    float maxDot = -2f;
                    for (int c = 0; c < chosen.Count; c++)
                    {
                        float d = math.dot(regions[r].Center, regions[chosen[c]].Center);
                        if (d > maxDot)
                        {
                            maxDot = d;
                        }
                    }

                    if (maxDot < bestAnyDot)
                    {
                        bestAnyDot = maxDot;
                        bestAny = r;
                    }

                    if ((isLand == null || isLand(regions[r].Center)) && maxDot < bestLandDot)
                    {
                        bestLandDot = maxDot;
                        bestLand = r;
                    }
                }

                int pick = bestLand >= 0 ? bestLand : bestAny;
                chosen.Add(pick);
                used.Add(pick);
            }

            int[] picked = chosen.ToArray();
            if (picked.Length != 8)
            {
                return picked;
            }

            // REQ: docs/design/lobby-meta.md#地球大廳 — map picked land hubs to #1..#8 by latitude and sequence:
            // #8 Tribe (index 7) takes the highest-|y| polar frontier, #7 City (index 6) the next highest-|y|,
            // while #3 City (index 2) and #5 CityState (index 4) occupy lower latitudes (never polar).
            var nonLanding = new List<int>(7);
            for (int i = 1; i < picked.Length; i++)
            {
                nonLanding.Add(picked[i]);
            }

            nonLanding.Sort((a, b) =>
            {
                int cmp = math.abs(regions[b].Center.y).CompareTo(math.abs(regions[a].Center.y));
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            var ordered = new int[8];
            ordered[0] = landingRegion; // #1 1號小鎮
            ordered[7] = nonLanding[0]; // #8 8號部落（最高緯度極地邊緣）
            ordered[6] = nonLanding[1]; // #7 7號城市（次高緯度極境樞紐）

            // Assign the two lowest-latitude hubs to #3 City (closer to #1) and #5 CityState.
            int lowA = nonLanding[5];
            int lowB = nonLanding[6];
            float3 landDir = regions[landingRegion].Center;
            if (math.dot(regions[lowA].Center, landDir) >= math.dot(regions[lowB].Center, landDir))
            {
                ordered[2] = lowA; // #3 3號城市
                ordered[4] = lowB; // #5 5號城邦
            }
            else
            {
                ordered[2] = lowB;
                ordered[4] = lowA;
            }

            // Remaining 3 mid-latitude regions fill #2 Tribe (closest to #1), #4 Town (closer to #3), #6 Town.
            var mid = new List<int>(3) { nonLanding[2], nonLanding[3], nonLanding[4] };
            int bestFor2Idx = 0;
            for (int k = 1; k < mid.Count; k++)
            {
                if (math.dot(regions[mid[k]].Center, landDir) > math.dot(regions[mid[bestFor2Idx]].Center, landDir))
                {
                    bestFor2Idx = k;
                }
            }

            ordered[1] = mid[bestFor2Idx]; // #2 2號部落
            mid.RemoveAt(bestFor2Idx);

            float3 city3Dir = regions[ordered[2]].Center;
            if (math.dot(regions[mid[0]].Center, city3Dir) >= math.dot(regions[mid[1]].Center, city3Dir))
            {
                ordered[3] = mid[0]; // #4 4號小鎮
                ordered[5] = mid[1]; // #6 6號小鎮
            }
            else
            {
                ordered[3] = mid[1];
                ordered[5] = mid[0];
            }

            return ordered;
        }

        private static int BfsParent(IReadOnlyList<RunRegion> regions, int r)
        {
            int hops = regions[r].Hops;
            foreach (int n in regions[r].Neighbors)
            {
                if (regions[n].Hops == hops - 1)
                {
                    return n;
                }
            }

            return regions[r].Neighbors.Count > 0 ? regions[r].Neighbors[0] : 0;
        }

        private static long EdgeKey(int a, int b)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            return ((long)lo << 32) | (uint)hi;
        }

        private static int FindConnectedNeighbor(int region, List<RunRoad> roads, IReadOnlyList<RunRegion> regions)
        {
            for (int i = 0; i < roads.Count; i++)
            {
                if (roads[i].Class == RoadClass.Main)
                {
                    if (roads[i].FromRegion == region) return roads[i].ToRegion;
                    if (roads[i].ToRegion == region) return roads[i].FromRegion;
                }
            }

            for (int i = 0; i < roads.Count; i++)
            {
                if (roads[i].FromRegion == region) return roads[i].ToRegion;
                if (roads[i].ToRegion == region) return roads[i].FromRegion;
            }

            return regions[region].Neighbors.Count > 0 ? regions[region].Neighbors[0] : -1;
        }

        private static float3 EnsureLandAnchor(float3 center, Func<float3, bool> isLand)
        {
            if (isLand == null || isLand(center))
            {
                return center;
            }

            PlanetFrame frame = PlanetFrame.FromUp(center);
            for (int ring = 1; ring <= 40; ring++)
            {
                float rad = ring * 0.01f;
                int samples = math.max(8, ring * 6);
                for (int i = 0; i < samples; i++)
                {
                    float a = i * (2f * math.PI / samples);
                    float3 p = math.normalize(center + (frame.East * math.cos(a) + frame.North * math.sin(a)) * rad);
                    if (isLand(p))
                    {
                        return p;
                    }
                }
            }

            return center;
        }
    }
}
