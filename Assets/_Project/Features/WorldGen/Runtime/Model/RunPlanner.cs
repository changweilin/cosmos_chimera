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
    /// Builds the region graph, wild spawns, quest chain and boss of one planet run.
    /// REQ: docs/design/planet-world.md#2–5 程序化層, docs/roadmap/demo.md (P3-05, P3-06, P3-08 D0 subset).
    /// </summary>
    /// <remarks>
    /// Determinism: every random choice comes from one Pcg32(seed, 'R') stream in a fixed order. Geometry uses float
    /// math; Q-T6 (cross-backend bit equality) is open, so decisions only compare distances between sites that are
    /// far apart relative to float error (sites are ≥ ~0.2 rad apart), never thresholds on terrain height.
    /// </remarks>
    public static class RunPlanner
    {
        /// <summary>Bump when any output for the same inputs changes (snapshot tests pin it).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int GeneratorVersion = 4;

        /// <summary>Smallest land cap (rad) the nearest-land sweep is guaranteed to find; isLand must contain one.</summary>
        /// <remarks>
        /// SOL Q-D1-IMPL-R2-01: finite sampling cannot prove a black-box predicate all-sea, so the guarantee is stated as
        /// a Pre instead. Demo masks always satisfy it (SOL Q-D1-IMPL-R3-01): sea exists only inside ≤ 6 template stamps,
        /// each a gnomonic square of edge half angle 0.45 rad inside a cap of radius atan(√2·tan 0.45) ≈ 0.5993 rad. Six
        /// caps grown by 0.02 rad cover at most 55.7 % of the sphere, so some point lies outside all of them and its whole
        /// 0.02 rad cap is outside every stamp, where IsSea is false.
        /// Pre: none. Post: constant == 2 × the sweep step. Side effects: none.
        /// </remarks>
        public const float MinLandCapRad = 2f * LandSweepStepRad;

        private const float LandSweepStepRad = 0.01f;

        // v2 (2026-11-09): the DefeatWild element is restricted to elements that have enough non-boss spawns.
        // v3 (2026-11-10, D0-B2): the boss stage is an input; every spawn's form is capped at it. Adult-boss plans are
        //     bit-identical to v2 (the cap never binds and the draw order is unchanged).
        // v4 (2026-11-11, Q-D1-R1): a spawn or landing point whose 8 random tries and region site are all sea moves to the
        //     nearest land found by a deterministic ring sweep instead of staying at sea. Plans without such a point (and
        //     every isLand == null plan) are bit-identical to v3.
        // (2026-11-13, Q-W2): borderClearanceRad is a new input, not a new version: 0 (the default) is bit-identical to v4.
        private const int DefeatWildCount = 3;
        private const double SurfaceKm2PerRegion = 0.42;
        private const int MinRegions = 8;
        private const int MaxRegions = 32;

        // REQ: planet-world.md 星球類型 — terrain tag → preferred region themes (Terra region-themes.json ids).
        private static readonly Dictionary<string, string[]> ThemesByTag = new Dictionary<string, string[]>
        {
            ["forest"] = new[] { "forest" },
            ["river_valley"] = new[] { "riverbank", "forest" },
            ["volcano"] = new[] { "crater" },
            ["rift_valley"] = new[] { "canyon", "crater" },
            ["ice_field"] = new[] { "ice_shelf" },
            ["fjord"] = new[] { "coast", "ice_shelf" },
            ["plateau"] = new[] { "highland" },
            ["archipelago"] = new[] { "coast" },
            ["canyon"] = new[] { "canyon" },
            ["cave"] = new[] { "ruins", "canyon" },
            ["eternal_day"] = new[] { "highland", "ruins" },
            ["eternal_night"] = new[] { "ruins", "highland" },
        };

        /// <summary>Generates the run plan.</summary>
        /// <remarks>
        /// Pre: planet valid; planetType non-null; wildPool non-empty, no nulls, each with ≥ 1 mastery entry, and at
        ///      least one form of exactly bossStage (Juvenile..Adult); vocabulary non-null; isLand null (all land) or
        ///      deterministic and accepting every direction of at least one spherical cap of angular radius
        ///      <see cref="MinLandCapRad"/> (land narrower than that may be skipped); 0 ≤ borderClearanceRad ≤ 0.1, and
        ///      when > 0 that land cap must also lie farther than it from every region border.
        /// Post: regions in [8, 32] scaled by surface area; region graph connected; MainRoute is a shortest path from
        ///       LandingRegion to BossRegion; BossRegion maximizes hops from landing; exactly one Boss spawn (a bossStage
        ///       form whose main element is in the type's bias when one exists) in the boss region; no spawn's form is
        ///       above bossStage (REQ: planet-world.md#boss-尺寸, user 2026-11-10: a juvenile boss is never outclassed); one Elite per
        ///       route region strictly between landing and boss (at least one overall); quest ends with DefeatBoss;
        ///       the DefeatWild step's Element has ≥ its Count (1..3) spawns that are neither the boss nor the
        ///       DefeatElite target, preferring the type's bias.
        ///       Every spawn direction and LandingDirection satisfies isLand (REQ: combat.md#只在陸地); a spawn that found
        ///       no land near its region site takes the nearest land, which may lie across a Voronoi border.
        ///       When borderClearanceRad > 0, every spawn direction and LandingDirection is also farther than it from
        ///       every region border (REQ: planet-world.md#地表地牢 — nothing spawns on or inside a room wall).
        ///       Same inputs → same plan.
        /// Side effects: allocates. Throws ContractException in every build when the land sweep finds no land (the isLand
        ///          Pre is violated).
        /// </remarks>
        public static RunPlan Generate(in PlanetConfig planet, ulong seed, PlanetTypeDef planetType, CreatureStage bossStage,
            IReadOnlyList<CreatureDef> wildPool, RunPlanVocabulary vocabulary, Func<float3, bool> isLand,
            float borderClearanceRad = 0f)
        {
            Contract.Requires(borderClearanceRad >= 0f && borderClearanceRad <= 0.1f, "borderClearanceRad must be in [0, 0.1]");
            Contract.Requires(planet.IsValid, "planet must be valid");
            Contract.Requires(planetType != null && vocabulary != null, "planetType and vocabulary must not be null");
            Contract.Requires(wildPool != null && wildPool.Count > 0, "wild pool must not be empty");
            Contract.Requires(bossStage >= CreatureStage.Juvenile && bossStage <= CreatureStage.Adult,
                "bossStage must be juvenile..adult");
            bool hasBossForm = false;
            foreach (CreatureDef c in wildPool)
            {
                Contract.Requires(c != null && c.Mastery.Count > 0, "wild pool entries need a main element");
                hasBossForm |= c.Stage == bossStage;
            }

            Contract.Requires(hasBossForm, "wild pool needs a form of the boss stage");
            var rng = new Pcg32(seed, 'R');

            float3[] sites = Sites(planet, ref rng);
            if (borderClearanceRad > 0f)
            {
                Func<float3, bool> land = isLand;
                isLand = d => (land == null || land(d)) && BorderDistanceRad(sites, d) > borderClearanceRad;
            }

            int[][] adjacency = Gabriel(sites);
            int landing = Nearest(sites, RandomUnit(ref rng));
            int[] hops = Bfs(adjacency, landing, out int[] parent);
            int boss = landing;
            for (int i = 0; i < sites.Length; i++)
            {
                if (hops[i] > hops[boss]) // ascending scan: ties keep the smaller index
                {
                    boss = i;
                }
            }

            int[] route = Path(parent, landing, boss);
            int maxHops = hops[boss];
            var routeSet = new HashSet<int>(route);

            var regions = new RunRegion[sites.Length];
            for (int i = 0; i < sites.Length; i++)
            {
                int tier = i == boss ? 2 : (int)math.min(2, 3 * hops[i] / (maxHops + 1));
                string theme = PickTheme(planetType, vocabulary, ref rng);
                string affix = null;
                if (i != landing && (i == boss || rng.NextUInt(100) < 40))
                {
                    affix = PickAffix(planetType, vocabulary, ref rng);
                }

                regions[i] = new RunRegion(i, sites[i], adjacency[i], theme, affix, tier, hops[i]);
            }

            float spacing = math.sqrt(4f * math.PI / sites.Length); // mean angular site spacing (rad)
            var spawns = new List<RunSpawn>();
            int group = 0;
            for (int i = 0; i < sites.Length; i++)
            {
                if (i == boss)
                {
                    continue;
                }

                int packs = 2 + (int)rng.NextUInt(2);
                for (int p = 0; p < packs; p++)
                {
                    float3 packCenter = Scatter(sites[i], spacing * 0.3f, isLand, ref rng);
                    int members = 1 + (int)rng.NextUInt(regions[i].Tier == 0 ? 2u : 3u);
                    for (int m = 0; m < members; m++)
                    {
                        CreatureDef species = PickSpecies(wildPool, planetType, (CreatureStage)regions[i].Tier, bossStage,
                            ref rng);
                        float3 dir = Scatter(packCenter, 5f / planet.Radius, isLand, ref rng);
                        spawns.Add(new RunSpawn(i, group, dir, species.Id, SpawnRole.Normal));
                    }

                    group++;
                }
            }

            var eliteByRegion = new Dictionary<int, int>();
            for (int r = 1; r < route.Length - 1; r++)
            {
                int region = route[r];
                CreatureStage stage = (CreatureStage)math.min(2, regions[region].Tier + 1);
                CreatureDef species = PickSpecies(wildPool, planetType, stage, bossStage, ref rng);
                eliteByRegion[region] = spawns.Count;
                spawns.Add(new RunSpawn(region, group++, Scatter(sites[region], spacing * 0.2f, isLand, ref rng),
                    species.Id, SpawnRole.Elite));
            }

            if (eliteByRegion.Count == 0)
            {
                // A two-region route has no middle region; the elite then guards the landing region's edge.
                CreatureDef species = PickSpecies(wildPool, planetType, CreatureStage.Adolescent, bossStage, ref rng);
                eliteByRegion[landing] = spawns.Count;
                spawns.Add(new RunSpawn(landing, group++, Scatter(sites[landing], spacing * 0.3f, isLand, ref rng),
                    species.Id, SpawnRole.Elite));
            }

            CreatureDef bossSpecies = PickSpecies(wildPool, planetType, bossStage, bossStage, ref rng);
            int bossSpawn = spawns.Count;
            spawns.Add(new RunSpawn(boss, group, Scatter(sites[boss], 0f, isLand, ref rng), bossSpecies.Id, SpawnRole.Boss));

            // REQ: docs/roadmap/log/phase-2.md (SOL P2-12 D0 calibration, 2026-11-09): picking straight from ElementBias made
            // DefeatWild impossible when no wild form has that element (twilight light/shadow, storm thunder). The
            // element must have ≥ DefeatWildCount non-boss spawns; the boss ends the run, so it can never count.
            // The next step's elite is excluded too (SOL P3-10 S1): QuestTracker gives no pre-credit, so killing it to
            // fill DefeatWild would leave DefeatElite with no target.
            int firstStop = route.Length > 2 ? route[1] : landing;
            int middle = route[route.Length / 2];
            int eliteRegion = eliteByRegion.ContainsKey(middle) ? middle : FirstKey(eliteByRegion, route);
            int eliteTarget = eliteByRegion[eliteRegion];
            var nonBossByElement = new Dictionary<Element, int>();
            for (int i = 0; i < spawns.Count; i++)
            {
                RunSpawn s = spawns[i];
                if (s.Role == SpawnRole.Boss || i == eliteTarget)
                {
                    continue;
                }

                Element e = MainElement(FindById(wildPool, s.CreatureId));
                nonBossByElement.TryGetValue(e, out int n);
                nonBossByElement[e] = n + 1;
            }

            var questCandidates = new List<Element>();
            foreach (Element e in planetType.ElementBias)
            {
                if (nonBossByElement.TryGetValue(e, out int n) && n >= DefeatWildCount && !questCandidates.Contains(e))
                {
                    questCandidates.Add(e);
                }
            }

            Element questElement = questCandidates.Count > 0
                ? questCandidates[(int)rng.NextUInt((uint)questCandidates.Count)]
                : MostCommon(nonBossByElement, MainElement(bossSpecies));
            int defeatCount = math.min(DefeatWildCount, nonBossByElement.TryGetValue(questElement, out int have) ? have : 0);
            var quest = new[]
            {
                new RunQuestStep(QuestStepKind.ReachPoi, firstStop, 1, questElement, -1),
                new RunQuestStep(QuestStepKind.DefeatWild, firstStop, defeatCount, questElement, -1),
                new RunQuestStep(QuestStepKind.DefeatElite, eliteRegion, 1, questElement, eliteTarget),
                new RunQuestStep(QuestStepKind.CaptureOne, eliteRegion, 1, questElement, -1),
                new RunQuestStep(QuestStepKind.DefeatBoss, boss, 1, questElement, bossSpawn),
            };

            string chain = vocabulary.ChainIds[(int)rng.NextUInt((uint)vocabulary.ChainIds.Count)];
            float3 landingDir = Scatter(sites[landing], spacing * 0.1f, isLand, ref rng);
            return new RunPlan(seed, planetType.Id, chain, regions, landing, boss, route, spawns.ToArray(), quest,
                landingDir);
        }

        /// <summary>Order-sensitive FNV-1a digest of a plan for snapshot tests (directions quantized to 1e-4).</summary>
        /// <remarks>Pre: plan non-null. Post: same plan → same value. Side effects: none.</remarks>
        public static ulong Hash(RunPlan plan)
        {
            Contract.Requires(plan != null, "plan must not be null");
            ulong h = 14695981039346656037UL;
            Mix(ref h, plan.Seed);
            Mix(ref h, plan.PlanetTypeId);
            Mix(ref h, plan.ChainId);
            Mix(ref h, (ulong)plan.LandingRegion);
            Mix(ref h, (ulong)plan.BossRegion);
            foreach (RunRegion r in plan.Regions)
            {
                Mix(ref h, r.Center);
                Mix(ref h, r.ThemeId);
                Mix(ref h, r.AffixId ?? "-");
                Mix(ref h, (ulong)r.Tier);
                foreach (int n in r.Neighbors)
                {
                    Mix(ref h, (ulong)n);
                }
            }

            foreach (RunSpawn s in plan.Spawns)
            {
                Mix(ref h, (ulong)s.Region);
                Mix(ref h, (ulong)s.Group);
                Mix(ref h, s.Direction);
                Mix(ref h, s.CreatureId);
                Mix(ref h, (ulong)s.Role);
            }

            foreach (RunQuestStep q in plan.Quest)
            {
                Mix(ref h, (ulong)q.Kind);
                Mix(ref h, (ulong)q.Region);
                Mix(ref h, (ulong)q.Count);
                Mix(ref h, (ulong)q.Element);
                Mix(ref h, (ulong)(q.TargetSpawn + 1));
            }

            return h;
        }

        /// <summary>Index of the region whose site is nearest to <paramref name="direction"/> (Voronoi lookup).</summary>
        /// <remarks>Pre: plan non-null; direction unit length. Post: valid index; ties → smaller index. Side effects: none.</remarks>
        public static int RegionAt(RunPlan plan, float3 direction)
        {
            Contract.Requires(plan != null, "plan must not be null");
            int best = 0;
            float bestDot = -2f;
            for (int i = 0; i < plan.Regions.Count; i++)
            {
                float d = math.dot(plan.Regions[i].Center, direction);
                if (d > bestDot)
                {
                    bestDot = d;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Angular distance (rad) from <paramref name="direction"/> to the nearest border of its Voronoi cell.</summary>
        /// <remarks>Pre: sites has ≥ 2 unit entries; direction unit length. Post: ≥ 0. Side effects: none.</remarks>
        internal static float BorderDistanceRad(float3[] sites, float3 direction)
        {
            int a = Nearest(sites, direction);
            float best = float.MaxValue;
            for (int k = 0; k < sites.Length; k++)
            {
                if (k != a)
                {
                    // sine of the distance to the bisector great circle of (a, k); equal to the angle at wall scale.
                    float3 diff = sites[a] - sites[k];
                    best = math.min(best, math.dot(direction, diff) / math.length(diff));
                }
            }

            return best;
        }

        /// <summary>Main element of a creature form: its highest mastery entry (first on ties).</summary>
        /// <remarks>Pre: creature has ≥ 1 mastery entry. Post: defined element. Side effects: none.</remarks>
        public static Element MainElement(CreatureDef creature)
        {
            Contract.Requires(creature != null && creature.Mastery.Count > 0, "creature needs a mastery entry");
            ElementValue best = creature.Mastery[0];
            for (int i = 1; i < creature.Mastery.Count; i++)
            {
                if (creature.Mastery[i].Value > best.Value)
                {
                    best = creature.Mastery[i];
                }
            }

            return best.Element;
        }

        private static float3[] Sites(in PlanetConfig planet, ref Pcg32 rng)
        {
            double areaKm2 = 4.0 * math.PI_DBL * planet.Radius * planet.Radius / 1e6;
            int n = (int)math.clamp(math.round(areaKm2 / SurfaceKm2PerRegion), MinRegions, MaxRegions);
            var sites = new float3[n];
            // Fibonacci lattice keeps sites evenly spaced (no sliver regions); a random rotation + small jitter
            // makes each seed's layout different without breaking that spacing.
            quaternion rot = quaternion.AxisAngle(RandomUnit(ref rng), rng.NextFloat() * 2f * math.PI);
            float golden = math.PI * (3f - math.sqrt(5f));
            float jitter = 0.25f * math.sqrt(4f * math.PI / n);
            for (int i = 0; i < n; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / n;
                float r = math.sqrt(1f - y * y);
                float3 p = new float3(math.cos(golden * i) * r, y, math.sin(golden * i) * r);
                p = math.normalize(math.mul(rot, p) + RandomUnit(ref rng) * (jitter * rng.NextFloat()));
                sites[i] = p;
            }

            return sites;
        }

        // Gabriel graph: i–j adjacent iff no other site lies inside the sphere with diameter ij. It is a subgraph of the
        // Delaunay triangulation that still contains the minimum spanning tree, so it is connected and its edges are
        // real Voronoi neighbors (walkable borders).
        private static int[][] Gabriel(float3[] s)
        {
            int n = s.Length;
            var lists = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                lists[i] = new List<int>();
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    float3 mid = (s[i] + s[j]) * 0.5f;
                    float r2 = math.distancesq(s[i], s[j]) * 0.25f;
                    bool empty = true;
                    for (int k = 0; k < n && empty; k++)
                    {
                        empty = k == i || k == j || math.distancesq(s[k], mid) >= r2;
                    }

                    if (empty)
                    {
                        lists[i].Add(j);
                        lists[j].Add(i);
                    }
                }
            }

            var result = new int[n][];
            for (int i = 0; i < n; i++)
            {
                lists[i].Sort();
                result[i] = lists[i].ToArray();
            }

            return result;
        }

        private static int[] Bfs(int[][] adj, int start, out int[] parent)
        {
            var hops = new int[adj.Length];
            parent = new int[adj.Length];
            for (int i = 0; i < hops.Length; i++)
            {
                hops[i] = -1;
                parent[i] = -1;
            }

            var queue = new Queue<int>();
            hops[start] = 0;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                foreach (int v in adj[u])
                {
                    if (hops[v] < 0)
                    {
                        hops[v] = hops[u] + 1;
                        parent[v] = u;
                        queue.Enqueue(v);
                    }
                }
            }

            for (int i = 0; i < hops.Length; i++)
            {
                Contract.Ensures(hops[i] >= 0, "region graph must be connected");
            }

            return hops;
        }

        private static int[] Path(int[] parent, int from, int to)
        {
            var path = new List<int>();
            for (int v = to; v != -1; v = parent[v])
            {
                path.Add(v);
            }

            path.Reverse();
            Contract.Ensures(path[0] == from, "path must start at the landing region");
            return path.ToArray();
        }

        private static string PickTheme(PlanetTypeDef type, RunPlanVocabulary vocab, ref Pcg32 rng)
        {
            var preferred = new List<string>();
            foreach (string tag in type.TerrainTags)
            {
                if (ThemesByTag.TryGetValue(tag, out string[] themes))
                {
                    foreach (string t in themes)
                    {
                        if (!preferred.Contains(t) && Contains(vocab.ThemeIds, t))
                        {
                            preferred.Add(t);
                        }
                    }
                }
            }

            uint roll = rng.NextUInt(100);
            if (preferred.Count > 0 && roll < 60)
            {
                return preferred[(int)rng.NextUInt((uint)preferred.Count)];
            }

            return vocab.ThemeIds[(int)rng.NextUInt((uint)vocab.ThemeIds.Count)];
        }

        private static string PickAffix(PlanetTypeDef type, RunPlanVocabulary vocab, ref Pcg32 rng)
        {
            if (type.ElementBias.Count > 0 && rng.NextUInt(100) < 70)
            {
                return RunPlanVocabulary.SurgeAffixId(type.ElementBias[(int)rng.NextUInt((uint)type.ElementBias.Count)]);
            }

            return vocab.GenericAffixIds[(int)rng.NextUInt((uint)vocab.GenericAffixIds.Count)];
        }

        private static CreatureDef PickSpecies(IReadOnlyList<CreatureDef> pool, PlanetTypeDef type, CreatureStage stage,
            CreatureStage cap, ref Pcg32 rng)
        {
            // Weight 3 for the planet's elements, 1 otherwise; fall back to the nearest available stage not above cap.
            // Asking for min(stage, cap) first keeps a capped tier on the boss stage instead of drifting further down.
            // Generate's Pre guarantees a cap-stage form, so some delta in 0..2 always has a positive total.
            if (stage > cap)
            {
                stage = cap;
            }

            for (int delta = 0; delta <= 2; delta++)
            {
                uint total = 0;
                foreach (CreatureDef c in pool)
                {
                    total += Weight(c, type, stage, cap, delta);
                }

                if (total == 0)
                {
                    continue;
                }

                uint pick = rng.NextUInt(total);
                foreach (CreatureDef c in pool)
                {
                    uint w = Weight(c, type, stage, cap, delta);
                    if (pick < w)
                    {
                        return c;
                    }

                    pick -= w;
                }
            }

            return pool[0];
        }

        private static uint Weight(CreatureDef c, PlanetTypeDef type, CreatureStage stage, CreatureStage cap, int delta)
        {
            if (c.Stage > cap || math.abs((int)c.Stage - (int)stage) != delta)
            {
                return 0;
            }

            Element main = MainElement(c);
            foreach (Element e in type.ElementBias)
            {
                if (e == main)
                {
                    return 3;
                }
            }

            return 1;
        }

        private static float3 Scatter(float3 center, float maxAngle, Func<float3, bool> isLand, ref Pcg32 rng)
        {
            float3 t = PlanetFrame.FromUp(center).East;
            float3 b = math.cross(center, t);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float a = rng.NextFloat() * 2f * math.PI;
                float d = math.sqrt(rng.NextFloat()) * maxAngle;
                float3 p = math.normalize(center * math.cos(d) + (t * math.cos(a) + b * math.sin(a)) * math.sin(d));
                if (isLand == null || isLand(p))
                {
                    return p;
                }
            }

            if (isLand(center))
            {
                return center;
            }

            return NearestLand(center, isLand);
        }

        // REQ: docs/design/combat.md#只在陸地, SOL Q-D1-R320 (Q-D1-R1): v3 returned the last sea sample here, and a boss
        // (maxAngle 0) on a sea site could never move. The sweep draws no random numbers, so plans that found land in the
        // 8 random attempts keep their exact RNG stream and output (v4 only changes plans that used to put a spawn at sea).
        // Coverage (SOL Q-D1-IMPL-R3): every direction is within ~1 Step of a sample, so a cap of radius 2·Step
        // (MinLandCapRad) always contains one. Mid-sphere: nearest ring ≤ Step/2 away, then along it to the nearest point
        // ≤ Step/2. Within Step/2 of the centre: the first ring (n ≥ 7) is ≤ ~1.5 Step away. Near the antipode the last
        // ring degenerates to points inside the cap. SOL measured a float bound of 0.0137 rad < 0.02 over 6e6 positions.
        // Cost per exhausted sweep ≈ 4π/Step² ≈ 1.3e5 predicate calls.
        /// <summary>First land direction met by rings of increasing angle around <paramref name="center"/>.</summary>
        /// <remarks>
        /// Pre: center unit length; isLand deterministic and accepting some cap of radius MinLandCapRad.
        /// Post: isLand(result); result is the first land sample in ring order (not necessarily the closest land). Throws
        ///       ContractException (every build) when the sweep finds no land.
        /// Side effects: none besides isLand calls.
        /// </remarks>
        internal static float3 NearestLand(float3 center, Func<float3, bool> isLand)
        {
            const float Step = LandSweepStepRad;
            float3 t = PlanetFrame.FromUp(center).East;
            float3 b = math.cross(center, t);
            int rings = (int)math.ceil(math.PI / Step);
            for (int k = 1; k <= rings; k++)
            {
                float d = math.min(k * Step, math.PI);
                int n = math.max(6, (int)math.ceil(2f * math.PI * math.sin(d) / Step));
                for (int j = 0; j < n; j++)
                {
                    float a = 2f * math.PI * j / n;
                    float3 p = math.normalize(center * math.cos(d) + (t * math.cos(a) + b * math.sin(a)) * math.sin(d));
                    if (isLand(p))
                    {
                        return p;
                    }
                }
            }

            // Not a Contract.Requires: that is stripped in Release, and a sea spawn there is the silent failure this replaces.
            throw new ContractException("Precondition failed: isLand must contain a land cap of radius MinLandCapRad");
        }

        private static float3 RandomUnit(ref Pcg32 rng)
        {
            float z = rng.NextFloat() * 2f - 1f;
            float a = rng.NextFloat() * 2f * math.PI;
            float r = math.sqrt(math.max(0f, 1f - z * z));
            return new float3(r * math.cos(a), r * math.sin(a), z);
        }

        private static int Nearest(float3[] sites, float3 dir)
        {
            int best = 0;
            for (int i = 1; i < sites.Length; i++)
            {
                if (math.dot(sites[i], dir) > math.dot(sites[best], dir))
                {
                    best = i;
                }
            }

            return best;
        }

        private static CreatureDef FindById(IReadOnlyList<CreatureDef> pool, string id)
        {
            foreach (CreatureDef c in pool)
            {
                if (c.Id == id)
                {
                    return c;
                }
            }

            Contract.Ensures(false, "every spawn comes from the wild pool");
            return null;
        }

        // Iterates the enum in value order, not the dictionary, so ties resolve the same on every runtime.
        private static Element MostCommon(Dictionary<Element, int> counts, Element fallback)
        {
            Element best = fallback;
            int bestCount = 0;
            for (int v = 0; v <= (int)Element.Shadow; v++)
            {
                if (counts.TryGetValue((Element)v, out int n) && n > bestCount)
                {
                    best = (Element)v;
                    bestCount = n;
                }
            }

            return best;
        }

        private static int FirstKey(Dictionary<int, int> map, int[] route)
        {
            foreach (int r in route)
            {
                if (map.ContainsKey(r))
                {
                    return r;
                }
            }

            Contract.Ensures(false, "an elite region must exist");
            return -1;
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            foreach (string s in list)
            {
                if (s == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Mix(ref ulong h, ulong v)
        {
            for (int i = 0; i < 8; i++)
            {
                h ^= (v >> (i * 8)) & 0xFF;
                h *= 1099511628211UL;
            }
        }

        private static void Mix(ref ulong h, string s)
        {
            foreach (char c in s)
            {
                Mix(ref h, c);
            }

            Mix(ref h, 0xFFFFUL);
        }

        private static void Mix(ref ulong h, float3 v)
        {
            Mix(ref h, (ulong)(long)math.round(v.x * 1e4f));
            Mix(ref h, (ulong)(long)math.round(v.y * 1e4f));
            Mix(ref h, (ulong)(long)math.round(v.z * 1e4f));
        }
    }
}
