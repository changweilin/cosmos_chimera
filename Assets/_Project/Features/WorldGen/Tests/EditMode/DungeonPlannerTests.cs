using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class DungeonPlannerTests
    {
        // Pinned discrete digest of seed 12345 at R = 640 on this fixture, isLand null (DungeonPlanner v1).
        private const ulong SnapshotSeed12345V1 = 2481595102866819357UL;

        private static readonly float[] Radii = { 320f, 640f, 960f };
        private readonly List<Object> _created = new List<Object>();
        private PlanetTypeDef _type;
        private List<CreatureDef> _pool;
        private RunPlanVocabulary _vocab;

        [SetUp]
        public void SetUp()
        {
            _type = ScriptableObject.CreateInstance<PlanetTypeDef>();
            _created.Add(_type);
            _type.SetId("verdant_planet");
            _type.Set(new List<string> { "forest", "river_valley" }, new List<Element> { Element.Wood, Element.Water });
            _pool = new List<CreatureDef>();
            foreach (string line in new[] { "wood", "fire", "water" })
            {
                for (int stage = 0; stage < 3; stage++)
                {
                    var c = ScriptableObject.CreateInstance<CreatureDef>();
                    _created.Add(c);
                    c.SetId(line + "_" + stage);
                    Element e = line == "wood" ? Element.Wood : line == "fire" ? Element.Fire : Element.Water;
                    c.Set(line, (CreatureStage)stage, CreatureSizeClass.M, AiStyle.Charge, Temperament.Normal, null, 0,
                        new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(e, 15) },
                        new CreatureStats(25, 25, 25, 25), null, null, "{}", false);
                    _pool.Add(c);
                }
            }

            _vocab = new RunPlanVocabulary(
                new[] { "ruins", "forest", "riverbank", "highland", "coast", "crater", "ice_shelf", "canyon" },
                new[] { "affix_frenzy", "affix_pack", "affix_fog", "affix_bounty" },
                new[] { "chain_a", "chain_b", "chain_c" });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private sealed class World
        {
            public PlanetConfig Planet;
            public RunPlan Plan;
            public RunLayout Layout;
            public DungeonLayout Dungeon;
        }

        private World Build(ulong seed, float radius, System.Func<float3, bool> isLand = null)
        {
            var planet = new PlanetConfig(radius, float3.zero);
            RunPlan plan = RunPlanner.Generate(planet, seed, _type, CreatureStage.Adult, _pool, _vocab, null,
                DungeonPlanner.SpawnBorderClearanceRad(planet));
            RunLayout layout = SettlementPlanner.Generate(planet, plan, null);
            return new World { Planet = planet, Plan = plan, Layout = layout, Dungeon = DungeonPlanner.Generate(planet, plan, layout, isLand) };
        }

        private static float3 BossSpawn(RunPlan plan)
        {
            foreach (RunSpawn s in plan.Spawns)
            {
                if (s.Role == SpawnRole.Boss)
                {
                    return s.Direction;
                }
            }

            return plan.LandingDirection;
        }

        [Test]
        public void Every_room_is_reachable_from_the_landing_room()
        {
            foreach (float radius in Radii)
            {
                for (ulong seed = 0; seed < 300; seed++)
                {
                    World w = Build(seed, radius);
                    int n = w.Plan.Regions.Count;
                    var seen = new bool[n];
                    var stack = new Stack<int>();
                    seen[w.Plan.LandingRegion] = true;
                    stack.Push(w.Plan.LandingRegion);
                    int count = 1;
                    while (stack.Count > 0)
                    {
                        int a = stack.Pop();
                        for (int b = 0; b < n; b++)
                        {
                            if (!seen[b] && w.Dungeon.IsConnected(a, b))
                            {
                                seen[b] = true;
                                count++;
                                stack.Push(b);
                            }
                        }
                    }

                    Assert.That(count, Is.EqualTo(n), $"R {radius} seed {seed}");
                }
            }
        }

        [Test]
        public void Lock_is_the_quest_elite_and_the_arena_gate_lies_on_the_boss_road()
        {
            foreach (float radius in Radii)
            {
                int locked = 0;
                for (ulong seed = 0; seed < 300; seed++)
                {
                    World w = Build(seed, radius);
                    float3 boss = BossSpawn(w.Plan);
                    Assert.That(math.distance(w.Dungeon.ArenaCenter, boss), Is.LessThan(1e-6f), $"R {radius} seed {seed}");
                    Assert.That(w.Dungeon.HasLock, Is.EqualTo(w.Dungeon.KeySpawn >= 0));
                    if (!w.Dungeon.HasLock)
                    {
                        continue;
                    }

                    locked++;
                    RunSpawn key = w.Plan.Spawns[w.Dungeon.KeySpawn];
                    Assert.That(key.Role, Is.EqualTo(SpawnRole.Elite), $"R {radius} seed {seed}");
                    float keepOut = (DungeonPlanner.ArenaRadiusM + 12f) / radius;
                    Assert.That(math.distance(key.Direction, boss), Is.GreaterThan(keepOut * 0.99f), $"R {radius} seed {seed}: key inside the arena");
                    Assert.That(math.distance(w.Plan.LandingDirection, boss), Is.GreaterThan(keepOut * 0.99f), $"R {radius} seed {seed}");
                    Assert.That(math.distance(w.Dungeon.ArenaGate, boss) * radius, Is.EqualTo(DungeonPlanner.ArenaRadiusM).Within(0.1f),
                        $"R {radius} seed {seed}: gate not on the arena wall");

                    RunRoad bossRoad = null;
                    foreach (RunRoad road in w.Layout.Roads)
                    {
                        if (road.ToRegion == w.Plan.BossRegion)
                        {
                            bossRoad = road;
                        }
                    }

                    float nearest = float.MaxValue;
                    foreach (float3 p in bossRoad.Points)
                    {
                        nearest = math.min(nearest, math.distance(p, w.Dungeon.ArenaGate) * radius);
                    }

                    Assert.That(nearest, Is.LessThan(4.5f), $"R {radius} seed {seed}: gate off the boss road");
                }

                Assert.That(locked, Is.GreaterThan(290), $"R {radius}: all-land planets should practically always have a lock");
            }
        }

        [Test]
        public void Rooms_have_one_landing_one_boss_and_treasure_only_in_dead_ends()
        {
            foreach (float radius in Radii)
            {
                for (ulong seed = 0; seed < 300; seed++)
                {
                    World w = Build(seed, radius);
                    int n = w.Plan.Regions.Count;
                    int landings = 0, bosses = 0, treasures = 0;
                    for (int r = 0; r < n; r++)
                    {
                        RoomRole role = w.Dungeon.Rooms[r];
                        landings += role == RoomRole.Landing ? 1 : 0;
                        bosses += role == RoomRole.Boss ? 1 : 0;
                        int degree = 0;
                        for (int b = 0; b < n; b++)
                        {
                            degree += w.Dungeon.IsConnected(r, b) ? 1 : 0;
                            Assert.That(w.Dungeon.IsConnected(r, b), Is.EqualTo(w.Dungeon.IsConnected(b, r)));
                            Assert.That(w.Dungeon.IsOpenBorder(r, b), Is.EqualTo(w.Dungeon.IsOpenBorder(b, r)));
                            Assert.That(!w.Dungeon.IsOpenBorder(r, b) || w.Dungeon.IsConnected(r, b), "open borders are connections");
                        }

                        if (role == RoomRole.Treasure)
                        {
                            treasures++;
                            Assert.That(degree, Is.EqualTo(1), $"R {radius} seed {seed}: treasure room {r} is not a dead end");
                        }
                    }

                    Assert.That(landings, Is.EqualTo(1), $"R {radius} seed {seed}");
                    Assert.That(bosses, Is.EqualTo(1), $"R {radius} seed {seed}");
                    Assert.That(w.Dungeon.Rooms[w.Plan.LandingRegion], Is.EqualTo(RoomRole.Landing));
                    Assert.That(w.Dungeon.Rooms[w.Plan.BossRegion], Is.EqualTo(RoomRole.Boss));
                    Assert.That(treasures, Is.GreaterThanOrEqualTo(1), $"R {radius} seed {seed}: no treasure dead end");
                }
            }
        }

        [Test]
        public void Shortcuts_close_loops_between_unconnected_neighbours()
        {
            int withLoop = 0;
            for (ulong seed = 0; seed < 300; seed++)
            {
                World w = Build(seed, 640f);
                int shortcuts = 0;
                var pairs = new HashSet<long>();
                foreach (DungeonGate g in w.Dungeon.Gates)
                {
                    Assert.That(g.RegionA, Is.LessThan(g.RegionB), $"seed {seed}");
                    if (g.Road >= 0)
                    {
                        Assert.That(g.Road, Is.LessThan(w.Layout.Roads.Count));
                        pairs.Add(((long)g.RegionA << 32) | (uint)g.RegionB);
                    }
                }

                foreach (DungeonGate g in w.Dungeon.Gates)
                {
                    if (g.Road >= 0)
                    {
                        continue;
                    }

                    shortcuts++;
                    Assert.That(w.Plan.Regions[g.RegionA].Neighbors, Does.Contain(g.RegionB), $"seed {seed}");
                    // The road gates already connect every room, so a gate on any other pair closes a loop.
                    Assert.That(pairs.Add(((long)g.RegionA << 32) | (uint)g.RegionB), Is.True, $"seed {seed}: duplicate connection");
                }

                withLoop += shortcuts > 0 ? 1 : 0;
            }

            // A planet has none only when every free neighbour pair touches a protected dead end or a three-room corner.
            Assert.That(withLoop, Is.GreaterThan(240), "most planets have at least one loop");
        }

        [Test]
        public void Openness_varies_per_planet_and_opens_the_layout()
        {
            int min = 100, max = 0;
            long lowLinks = 0, lowCount = 0, highLinks = 0, highCount = 0;
            for (ulong seed = 0; seed < 300; seed++)
            {
                World w = Build(seed, 640f);
                int o = w.Dungeon.OpennessPercent;
                Assert.That(o, Is.InRange(0, 100));
                min = math.min(min, o);
                max = math.max(max, o);
                int n = w.Plan.Regions.Count;
                int links = 0;
                for (int a = 0; a < n; a++)
                {
                    for (int b = a + 1; b < n; b++)
                    {
                        links += (w.Dungeon.IsConnected(a, b) ? 1 : 0) + (w.Dungeon.IsOpenBorder(a, b) ? 1 : 0);
                    }
                }

                if (o < 30)
                {
                    lowLinks += links;
                    lowCount++;
                }
                else if (o > 70)
                {
                    highLinks += links;
                    highCount++;
                }

                Assert.That(w.Dungeon.PassInnerM, Is.EqualTo(5f + 4f * o / 100f).Within(1e-4f));
                Assert.That(w.Dungeon.PassOuterM, Is.GreaterThan(w.Dungeon.PassInnerM));
            }

            Assert.That(min, Is.LessThan(10));
            Assert.That(max, Is.GreaterThan(90));
            Assert.That((double)highLinks / highCount, Is.GreaterThan((double)lowLinks / lowCount), "open planets have more links");
        }

        [Test]
        public void Every_road_is_walkable_through_the_walls_once_the_lock_is_open()
        {
            foreach (float radius in Radii)
            {
                for (ulong seed = 0; seed < 300; seed++)
                {
                    World w = Build(seed, radius);
                    using PlanetWalls walls = DungeonPlanner.BuildWalls(w.Planet, w.Plan, w.Dungeon);
                    WallView view = walls.View;
                    float body = 1f / radius;
                    foreach (RunRoad road in w.Layout.Roads)
                    {
                        bool pluggedSomewhere = false;
                        for (int i = 0; i < road.Points.Count; i++)
                        {
                            Assert.That(view.IsBlocked(road.Points[i], body, false), Is.False,
                                $"R {radius} seed {seed}: road {road.FromRegion}->{road.ToRegion} vertex {i} is inside a wall");
                            pluggedSomewhere |= view.IsBlocked(road.Points[i], body, true);
                        }

                        bool bossRoad = road.ToRegion == w.Plan.BossRegion;
                        Assert.That(pluggedSomewhere, Is.EqualTo(bossRoad && w.Dungeon.HasLock),
                            $"R {radius} seed {seed}: road {road.FromRegion}->{road.ToRegion} and the closed lock");
                    }
                }
            }
        }

        [Test]
        public void Spawns_and_landing_stand_clear_of_the_walls()
        {
            foreach (float radius in Radii)
            {
                for (ulong seed = 0; seed < 100; seed++)
                {
                    World w = Build(seed, radius);
                    using PlanetWalls walls = DungeonPlanner.BuildWalls(w.Planet, w.Plan, w.Dungeon);
                    WallView view = walls.View;
                    float clearance = DungeonPlanner.SpawnBorderClearanceRad(w.Planet);
                    Assert.That(view.BorderDistance(w.Plan.LandingDirection), Is.GreaterThan(clearance), $"R {radius} seed {seed}");
                    Assert.That(view.IsBlocked(w.Plan.LandingDirection, 2f / radius, true), Is.False, $"R {radius} seed {seed}");
                    foreach (RunSpawn s in w.Plan.Spawns)
                    {
                        Assert.That(view.BorderDistance(s.Direction), Is.GreaterThan(clearance), $"R {radius} seed {seed}");
                    }
                }
            }
        }

        // Ground truth for "實體牆": flood the sphere itself (not the room graph) on a ~2 m grid.
        [Test]
        public void Flooding_the_surface_reaches_every_room_but_the_boss_only_after_the_lock_opens()
        {
            const float Radius = 320f;
            const int Rows = 500;
            const int Cols = 1000;
            int checkedPlanets = 0;
            for (ulong seed = 0; seed < 12; seed++)
            {
                World w = Build(seed, Radius);
                using PlanetWalls walls = DungeonPlanner.BuildWalls(w.Planet, w.Plan, w.Dungeon);
                WallView view = walls.View;
                float3 boss = BossSpawn(w.Plan);
                bool[] closed = Flood(view, w.Plan.LandingDirection, true, Rows, Cols);
                bool[] opened = Flood(view, w.Plan.LandingDirection, false, Rows, Cols);
                Assert.That(opened[Cell(boss, Rows, Cols)], Is.True, $"seed {seed}: boss unreachable with the lock open");
                for (int r = 0; r < w.Plan.Regions.Count; r++)
                {
                    if (r != w.Plan.BossRegion)
                    {
                        Assert.That(closed[Cell(w.Plan.Regions[r].Center, Rows, Cols)], Is.True, $"seed {seed}: room {r} walled off");
                    }
                }

                if (w.Dungeon.HasLock)
                {
                    checkedPlanets++;
                    Assert.That(closed[Cell(boss, Rows, Cols)], Is.False, $"seed {seed}: boss reachable past the closed lock");
                }
            }

            Assert.That(checkedPlanets, Is.GreaterThan(8));
        }

        private static int Cell(float3 d, int rows, int cols)
        {
            float lat = math.asin(math.clamp(d.y, -1f, 1f));
            float lon = math.atan2(d.z, d.x);
            int row = math.clamp((int)((lat / math.PI + 0.5f) * rows), 0, rows - 1);
            int col = ((int)((lon / (2f * math.PI) + 0.5f) * cols) % cols + cols) % cols;
            return row * cols + col;
        }

        private static bool[] Flood(WallView view, float3 start, bool locksClosed, int rows, int cols)
        {
            var blocked = new bool[rows * cols];
            for (int row = 0; row < rows; row++)
            {
                float lat = ((row + 0.5f) / rows - 0.5f) * math.PI;
                for (int col = 0; col < cols; col++)
                {
                    float lon = ((col + 0.5f) / cols - 0.5f) * 2f * math.PI;
                    var d = new float3(math.cos(lat) * math.cos(lon), math.sin(lat), math.cos(lat) * math.sin(lon));
                    blocked[row * cols + col] = view.IsBlocked(d, 0f, locksClosed);
                }
            }

            var seen = new bool[rows * cols];
            var queue = new Queue<int>();
            int s = Cell(start, rows, cols);
            seen[s] = true;
            queue.Enqueue(s);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int row = c / cols;
                int col = c % cols;
                Visit(row, (col + 1) % cols);
                Visit(row, (col + cols - 1) % cols);
                if (row + 1 < rows)
                {
                    Visit(row + 1, col);
                }

                if (row > 0)
                {
                    Visit(row - 1, col);
                }
            }

            return seen;

            void Visit(int row, int col)
            {
                int i = row * cols + col;
                if (!seen[i] && !blocked[i])
                {
                    seen[i] = true;
                    queue.Enqueue(i);
                }
            }
        }

        [Test]
        public void Same_inputs_give_the_same_dungeon()
        {
            World a = Build(777, 640f);
            World b = Build(777, 640f);
            Assert.That(DungeonPlanner.Hash(a.Dungeon), Is.EqualTo(DungeonPlanner.Hash(b.Dungeon)));
            for (int g = 0; g < a.Dungeon.Gates.Count; g++)
            {
                Assert.That(math.all(a.Dungeon.Gates[g].Center == b.Dungeon.Gates[g].Center), Is.True);
            }

            Assert.That(math.all(a.Dungeon.ArenaGate == b.Dungeon.ArenaGate), Is.True);
            Assert.That(DungeonPlanner.Hash(Build(778, 640f).Dungeon), Is.Not.EqualTo(DungeonPlanner.Hash(a.Dungeon)));
        }

        [Test]
        public void Snapshot_seed_12345()
        {
            Assert.That(DungeonPlanner.GeneratorVersion, Is.EqualTo(1), "update the snapshot when the version changes");
            Assert.That(DungeonPlanner.Hash(Build(12345, 640f).Dungeon), Is.EqualTo(SnapshotSeed12345V1));
        }

        [Test]
        public void Shortcuts_keep_off_the_sea_and_road_gates_do_not_depend_on_it()
        {
            for (ulong seed = 0; seed < 50; seed++)
            {
                World land = Build(seed, 640f);
                World sea = Build(seed, 640f, _ => false);
                int roadGates = 0;
                foreach (DungeonGate g in sea.Dungeon.Gates)
                {
                    Assert.That(g.Road, Is.GreaterThanOrEqualTo(0), $"seed {seed}: shortcut on the sea");
                    roadGates++;
                }

                int landRoadGates = 0;
                foreach (DungeonGate g in land.Dungeon.Gates)
                {
                    landRoadGates += g.Road >= 0 ? 1 : 0;
                }

                Assert.That(roadGates, Is.EqualTo(landRoadGates), $"seed {seed}");
                Assert.That(sea.Dungeon.OpennessPercent, Is.EqualTo(land.Dungeon.OpennessPercent), $"seed {seed}");
            }
        }

        [Test]
        public void Preconditions_are_checked()
        {
            World w = Build(1, 640f);
            Assert.Throws<ContractException>(() => DungeonPlanner.Generate(default, w.Plan, w.Layout, null));
            Assert.Throws<ContractException>(() => DungeonPlanner.Generate(w.Planet, null, w.Layout, null));
            Assert.Throws<ContractException>(() => DungeonPlanner.Generate(w.Planet, w.Plan, null, null));
            Assert.Throws<ContractException>(() => DungeonPlanner.BuildWalls(default, w.Plan, w.Dungeon));
            Assert.Throws<ContractException>(() => DungeonPlanner.BuildWalls(w.Planet, null, w.Dungeon));
            Assert.Throws<ContractException>(() => DungeonPlanner.BuildWalls(w.Planet, w.Plan, null));
            Assert.Throws<ContractException>(() => DungeonPlanner.Hash(null));
            Assert.Throws<ContractException>(() => DungeonPlanner.SpawnBorderClearanceRad(default));
        }

        [Test]
        public void Border_clearance_zero_leaves_the_run_plan_unchanged_and_out_of_range_is_rejected()
        {
            var planet = new PlanetConfig(640f, float3.zero);
            RunPlan plain = RunPlanner.Generate(planet, 42, _type, CreatureStage.Adult, _pool, _vocab, null);
            RunPlan zero = RunPlanner.Generate(planet, 42, _type, CreatureStage.Adult, _pool, _vocab, null, 0f);
            Assert.That(RunPlanner.Hash(zero), Is.EqualTo(RunPlanner.Hash(plain)));
            Assert.Throws<ContractException>(() => RunPlanner.Generate(planet, 42, _type, CreatureStage.Adult, _pool, _vocab, null, -0.01f));
            Assert.Throws<ContractException>(() => RunPlanner.Generate(planet, 42, _type, CreatureStage.Adult, _pool, _vocab, null, 0.11f));
        }
    }
}
