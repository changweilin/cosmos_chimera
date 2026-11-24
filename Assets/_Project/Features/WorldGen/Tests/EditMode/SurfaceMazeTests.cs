using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.WorldGen.Tests
{
    // REQ: planet-world.md#進階地形地城 (Q-W45) — surface mazes: wonder plaza = arena in the centre, entrance on the edge,
    // per-terrain grid openness.
    public sealed class SurfaceMazeTests
    {
        private static readonly string[] Affixes = { "elite_swift", "elite_shielded", "elite_frenzy", "elite_aura" };
        private static readonly int[] Sides = { RoomDungeon.North, RoomDungeon.East, RoomDungeon.South, RoomDungeon.West };
        private static readonly PlanetSize[] Sizes = { PlanetSize.Moon, PlanetSize.Planet, PlanetSize.Supergiant };

        // Largest ground footprint of each terrain's entrance wonder (LandmarkPlanner sizes): the tightest plaza.
        private static float FootprintFor(string id) =>
            id == "rainforest" ? 11f : id == "volcano" ? 18f : id == "ice_cap" ? 15f : 18f;

        private static float RadiusFor(PlanetSize size) =>
            size == PlanetSize.Moon ? 640f : size == PlanetSize.Planet ? 960f : 1280f;

        private static IEnumerable<(ulong seed, AdvancedTerrainSpec spec, PlanetSize size)> Cases(int seeds)
        {
            for (ulong seed = 0; seed < (ulong)seeds; seed++)
            {
                foreach (AdvancedTerrainSpec spec in AdvancedTerrains.All)
                {
                    if (spec.Form != AdvancedDungeonForm.SurfaceMaze)
                    {
                        continue;
                    }

                    foreach (PlanetSize size in Sizes)
                    {
                        if (AdvancedTerrains.AppearsOn(spec, size))
                        {
                            yield return (seed, spec, size);
                        }
                    }
                }
            }
        }

        [Test]
        public void The_plaza_is_the_centre_boss_room_and_the_entrance_opens_on_the_edge()
        {
            foreach (var (seed, spec, size) in Cases(60))
            {
                float foot = FootprintFor(spec.Id);
                RoomDungeon d = RoomDungeonPlanner.GenerateSurfaceMaze(seed, spec.Id, size, Affixes, foot);
                string at = $"seed {seed} {spec.Id} {size}";
                int k = RoomDungeonPlanner.PlazaHalfCells(d.CellSizeM, foot);
                Assert.That(d.BossHalfCells, Is.EqualTo(k), at);
                Assert.That(d.Width, Is.EqualTo(RoomDungeonPlanner.SurfaceMazeSide(size, k)).And.EqualTo(d.Height));
                Assert.That(d.Rooms.Count, Is.GreaterThanOrEqualTo(RoomDungeonPlanner.BaseRoomCount(size)));
                Assert.That(d.BossRoom, Is.EqualTo(0));
                Assert.That(d.GuardRoom, Is.EqualTo(1));
                int c = d.Width / 2;
                for (int dy = -k; dy <= k; dy++)
                {
                    for (int dx = -k; dx <= k; dx++)
                    {
                        Assert.That(d.RoomAt(c + dx, c + dy), Is.EqualTo(d.BossRoom), at + " plaza block");
                    }
                }

                foreach (int s in Sides)
                {
                    int nb = d.Neighbour(d.BossRoom, s);
                    Assert.That(nb == -1 || nb == d.GuardRoom, Is.True, at + " only the guard leads to the plaza");
                }

                DungeonRoom e = d.Rooms[d.EntranceRoom];
                Assert.That(e.Role, Is.EqualTo(DungeonRoomRole.Entrance));
                Assert.That(d.RoomAt(e.X + RoomDungeon.DeltaX(d.EntranceSide), e.Y + RoomDungeon.DeltaY(d.EntranceSide)),
                    Is.EqualTo(-1), at + " outer door leads off the grid");
                Assert.That(e.X + RoomDungeon.DeltaX(d.EntranceSide) < 0 || e.Y + RoomDungeon.DeltaY(d.EntranceSide) < 0 ||
                            e.X + RoomDungeon.DeltaX(d.EntranceSide) >= d.Width ||
                            e.Y + RoomDungeon.DeltaY(d.EntranceSide) >= d.Height, Is.True, at);

                // Every room reachable from the entrance through matching doors.
                var seen = new bool[d.Rooms.Count];
                var queue = new Queue<int>();
                queue.Enqueue(d.EntranceRoom);
                seen[d.EntranceRoom] = true;
                int reached = 1;
                while (queue.Count > 0)
                {
                    int r = queue.Dequeue();
                    foreach (int s in Sides)
                    {
                        int nb = d.Neighbour(r, s);
                        if (nb >= 0 && !seen[nb])
                        {
                            seen[nb] = true;
                            reached++;
                            queue.Enqueue(nb);
                        }
                    }
                }

                Assert.That(reached, Is.EqualTo(d.Rooms.Count), at + " sealed room");
            }
        }

        [Test]
        public void Walls_doors_plaza_and_outer_door_on_the_surface()
        {
            foreach (var (seed, spec, size) in Cases(12))
            {
                float foot = FootprintFor(spec.Id);
                float radius = RadiusFor(size);
                RoomDungeon d = RoomDungeonPlanner.GenerateSurfaceMaze(seed, spec.Id, size, Affixes, foot);
                var anchor = math.normalize(new float3(0.2f, 0.9f, -0.4f));
                var g = RoomDungeonGeometry.Build(d, radius, anchor, foot);
                using PlanetWalls walls = g.BuildWalls(new PlanetConfig(radius, float3.zero));
                WallView view = walls.View;
                string at = $"seed {seed} {spec.Id} {size}";
                float body = 1.5f / radius;
                Assert.That(math.distance(g.RoomCenter(d.BossRoom), anchor) * radius, Is.LessThan(0.01f), at + " plaza on the wonder");
                Assert.That(view.IsBlocked(g.OuterGate, 0.5f / radius, false), Is.False, at + " outer door open");
                Assert.That(g.RoomAt(g.OuterDoor), Is.EqualTo(-1), at);
                Assert.That(view.IsBlocked(g.OuterDoor, body, false), Is.False, at + " room to stand outside");
                Assert.That(g.Contains(g.OuterGate), Is.True);
                for (int r = 0; r < d.Rooms.Count; r++)
                {
                    for (int k = 1; k < 6; k++)
                    {
                        float3 p = g.SpawnPoint(r, k);
                        Assert.That(g.RoomAt(p), Is.EqualTo(r), $"{at} room {r} k {k}");
                        Assert.That(view.IsBlocked(p, body, true), Is.False, $"{at} spawn in a wall: room {r} k {k}");
                        if (r == d.BossRoom)
                        {
                            Assert.That(math.distance(p, anchor) * radius, Is.GreaterThan(foot + 1.5f), at + " clear of the wonder");
                        }
                    }
                }

                // Inside the plaza no wall stands between its cells (a point half a cell off the centre, any side).
                foreach (int s in Sides)
                {
                    float3 inner = Offset(g, anchor, s, 0.5f * d.CellSizeM);
                    Assert.That(view.IsBlocked(inner, body, false), Is.False, at + " plaza floor");
                }

                Assert.That(view.IsBlocked(g.ArenaGate, 0.5f / radius, true), Is.True, at + " arena sealed");
                Assert.That(view.IsBlocked(g.ArenaGate, 0.5f / radius, false), Is.False, at + " arena open");
            }
        }

        [Test]
        public void Grid_styles_make_ice_open_and_the_labyrinth_tight()
        {
            RoomGridStyle ice = AdvancedTerrains.GridStyle(Get("ice_cap"));
            RoomGridStyle maze = AdvancedTerrains.GridStyle(Get("labyrinth"));
            RoomGridStyle lab = AdvancedTerrains.GridStyle(Get("laboratory"));
            Assert.That(ice.OpenPlan, Is.True);
            Assert.That(ice.CellSizeM, Is.GreaterThan(lab.CellSizeM));
            Assert.That(maze.OpennessMax, Is.Zero);
            Assert.That(maze.CellSizeM, Is.LessThan(lab.CellSizeM));
            foreach (AdvancedTerrainSpec spec in AdvancedTerrains.All)
            {
                RoomGridStyle st = AdvancedTerrains.GridStyle(spec);
                Assert.That(st.CellSizeM, Is.GreaterThanOrEqualTo(20f));
                Assert.That(st.DoorInnerM, Is.GreaterThan(0f).And.LessThan(0.25f * st.CellSizeM));
                Assert.That(st.OpennessMin, Is.InRange(0, st.OpennessMax));
                Assert.That(st.OpennessMax, Is.InRange(0, 100));
                if (spec.Form == AdvancedDungeonForm.Rooms)
                {
                    Assert.That(st.CellSizeM, Is.EqualTo(RoomDungeonPlanner.CellSizeM), "indoor grids unchanged");
                }
            }

            // Open plan: the whole border between two door-joined rooms is open, not just the door.
            for (ulong seed = 0; seed < 20; seed++)
            {
                RoomDungeon d = RoomDungeonPlanner.GenerateSurfaceMaze(seed, "ice_cap", PlanetSize.Planet, Affixes, 15f);
                var g = RoomDungeonGeometry.Build(d, 960f, new float3(0f, 0f, 1f), 15f);
                using PlanetWalls walls = g.BuildWalls(new PlanetConfig(960f, float3.zero));
                for (int r = 1; r < d.Rooms.Count; r++)
                {
                    foreach (int s in Sides)
                    {
                        int nb = d.Neighbour(r, s);
                        if (nb <= 0)
                        {
                            continue;
                        }

                        // A third of the way along the shared border from the door: wall-free only under open plan.
                        float3 door = Offset(g, g.RoomCenter(r), s, 0.5f * d.CellSizeM);
                        float3 along = Offset(g, door, s == RoomDungeon.North || s == RoomDungeon.South
                            ? RoomDungeon.East : RoomDungeon.North, 0.33f * d.CellSizeM);
                        Assert.That(walls.View.IsBlocked(along, 0.5f / 960f, false), Is.False, $"seed {seed} room {r} side {s}");
                    }
                }
            }
        }

        [Test]
        public void Half_extent_bounds_the_generated_maze_and_contracts_hold()
        {
            foreach (var (seed, spec, size) in Cases(10))
            {
                float foot = FootprintFor(spec.Id);
                RoomDungeon d = RoomDungeonPlanner.GenerateSurfaceMaze(seed, spec.Id, size, Affixes, foot);
                Assert.That(0.5f * d.Width * d.CellSizeM,
                    Is.EqualTo(RoomDungeonPlanner.SurfaceMazeHalfExtentM(spec, size, foot)).Within(1e-3f));
                Assert.That(LandmarkPlanner.MazeReserveM(spec, size, foot),
                    Is.GreaterThan(1.414f * RoomDungeonPlanner.SurfaceMazeHalfExtentM(spec, size, foot)));
                Assert.That(RoomDungeonPlanner.Hash(d),
                    Is.EqualTo(RoomDungeonPlanner.Hash(RoomDungeonPlanner.GenerateSurfaceMaze(seed, spec.Id, size, Affixes, foot))));
            }

            Assert.Throws<ContractException>(() =>
                RoomDungeonPlanner.GenerateSurfaceMaze(1, "laboratory", PlanetSize.Moon, Affixes, 10f), "rooms terrain");
            Assert.Throws<ContractException>(() =>
                RoomDungeonPlanner.GenerateSurfaceMaze(1, "ice_cap", PlanetSize.Moon, Affixes, 10f), "too small a planet");
            Assert.Throws<ContractException>(() =>
                RoomDungeonPlanner.GenerateSurfaceMaze(1, "volcano", PlanetSize.Moon, Affixes, -1f));
            Assert.Throws<ContractException>(() =>
                RoomDungeonPlanner.GenerateSurfaceMaze(1, "volcano", PlanetSize.Moon, new string[0], 10f));
            RoomDungeon v = RoomDungeonPlanner.GenerateSurfaceMaze(1, "volcano", PlanetSize.Moon, Affixes, 18f);
            Assert.Throws<ContractException>(() => RoomDungeonGeometry.Build(v, 640f, new float3(0f, 0f, 1f), 500f),
                "a wonder too large for the arena");
            RoomDungeon indoor = RoomDungeonPlanner.Generate(1, "cave", PlanetSize.Moon, Affixes);
            var gi = RoomDungeonGeometry.Build(indoor, 640f, new float3(0f, 0f, 1f));
            Assert.Throws<ContractException>(() => _ = gi.OuterDoor, "indoor dungeons have no outer door");
            Assert.That(RoomDungeonPlanner.PlazaHalfCells(20f, 18f), Is.EqualTo(2));
            Assert.That(RoomDungeonPlanner.PlazaHalfCells(28f, 0f), Is.EqualTo(1));
        }

        private static AdvancedTerrainSpec Get(string id)
        {
            Assert.That(AdvancedTerrains.TryGet(id, out AdvancedTerrainSpec spec), Is.True);
            return spec;
        }

        // Moves `from` by `meters` along grid side s on the tangent plane of the geometry's anchor.
        private static float3 Offset(RoomDungeonGeometry g, float3 from, int s, float meters)
        {
            PlanetFrame f = PlanetFrame.FromUp(g.Anchor);
            float3 p = from * (g.Radius / math.dot(from, g.Anchor));
            float3 step = f.East * (s == RoomDungeon.East ? 1f : s == RoomDungeon.West ? -1f : 0f) +
                          f.North * (s == RoomDungeon.North ? 1f : s == RoomDungeon.South ? -1f : 0f);
            return math.normalize(p + step * meters);
        }
    }
}
