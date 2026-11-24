using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.WorldGen.Tests
{
    // REQ: docs/decisions/ADR-0011-interior-dungeon-sphere.md, planet-world.md#進階地形地城
    public sealed class RoomDungeonGeometryTests
    {
        private static readonly string[] Affixes = { "elite_swift", "elite_shielded", "elite_frenzy", "elite_aura" };
        private static readonly int[] Sides = { RoomDungeon.North, RoomDungeon.East, RoomDungeon.South, RoomDungeon.West };
        private static readonly float3[] Anchors =
        {
            math.normalize(new float3(0.3f, 0.2f, 0.93f)),
            math.normalize(new float3(-0.7f, 0.65f, 0.1f)),
            new float3(0f, -1f, 0f),
        };

        private static float RadiusFor(PlanetSize size) =>
            size == PlanetSize.Moon ? 640f : size == PlanetSize.Planet ? 960f : 1280f;

        private static PlanetSize SizeFor(AdvancedTerrainSpec spec) => spec.MinSize;

        private static float3 Toward(RoomDungeonGeometry g, float3 from, float3 to, float t) =>
            math.normalize(math.lerp(from, to, t));

        [Test]
        public void Rooms_spawns_arrival_and_exit_sit_in_their_room_clear_of_walls()
        {
            for (ulong seed = 0; seed < 12; seed++)
            {
                foreach (AdvancedTerrainSpec spec in AdvancedTerrains.All)
                {
                    PlanetSize size = SizeFor(spec);
                    float radius = RadiusFor(size);
                    RoomDungeon d = RoomDungeonPlanner.Generate(seed, spec.Id, size, Affixes);
                    var g = RoomDungeonGeometry.Build(d, radius, Anchors[seed % 3]);
                    var config = new PlanetConfig(radius, float3.zero);
                    using PlanetWalls walls = g.BuildWalls(config);
                    WallView view = walls.View;
                    float body = 1.5f / radius;
                    for (int r = 0; r < d.Rooms.Count; r++)
                    {
                        Assert.That(g.RoomAt(g.RoomCenter(r)), Is.EqualTo(r));
                        for (int k = 0; k < 6; k++)
                        {
                            float3 p = g.SpawnPoint(r, k);
                            Assert.That(g.RoomAt(p), Is.EqualTo(r), $"seed {seed} {spec.Id} room {r} k {k}");
                            Assert.That(view.IsBlocked(p, body, true), Is.False, $"spawn in a wall: seed {seed} {spec.Id} room {r} k {k}");
                        }
                    }

                    Assert.That(g.RoomAt(g.Arrival), Is.EqualTo(d.EntranceRoom));
                    Assert.That(g.RoomAt(g.Exit), Is.EqualTo(d.EntranceRoom));
                    Assert.That(view.IsBlocked(g.Arrival, body, true), Is.False);
                    Assert.That(view.IsBlocked(g.Exit, 0.5f / radius, true), Is.False);
                }
            }
        }

        [Test]
        public void Walls_stand_on_every_room_border_except_at_doors()
        {
            for (ulong seed = 0; seed < 12; seed++)
            {
                foreach (AdvancedTerrainSpec spec in AdvancedTerrains.All)
                {
                    PlanetSize size = SizeFor(spec);
                    float radius = RadiusFor(size);
                    RoomDungeon d = RoomDungeonPlanner.Generate(seed, spec.Id, size, Affixes);
                    var g = RoomDungeonGeometry.Build(d, radius, Anchors[(seed + 1) % 3]);
                    using PlanetWalls walls = g.BuildWalls(new PlanetConfig(radius, float3.zero));
                    WallView view = walls.View;
                    float body = 0.5f / radius;
                    for (int r = 0; r < d.Rooms.Count; r++)
                    {
                        DungeonRoom room = d.Rooms[r];
                        float3 c = g.RoomCenter(r);
                        foreach (int s in Sides)
                        {
                            // Midpoint of side s: halfway from this centre to the (possibly empty) neighbour cell's centre.
                            float3 beyond = DirectionOfCell(g, d, room.X + Dx(s), room.Y + Dy(s));
                            float3 border = Toward(g, c, beyond, 0.5f);
                            bool door = (room.Doors & s) != 0;
                            Assert.That(view.IsBlocked(border, body, false), Is.EqualTo(!door),
                                $"seed {seed} {spec.Id} room {r} side {s} door {door}");
                        }
                    }
                }
            }
        }

        [Test]
        public void The_arena_seal_blocks_only_while_locked()
        {
            foreach (AdvancedTerrainSpec spec in AdvancedTerrains.All)
            {
                PlanetSize size = SizeFor(spec);
                float radius = RadiusFor(size);
                RoomDungeon d = RoomDungeonPlanner.Generate(7, spec.Id, size, Affixes);
                var g = RoomDungeonGeometry.Build(d, radius, Anchors[0]);
                using PlanetWalls walls = g.BuildWalls(new PlanetConfig(radius, float3.zero));
                float body = 0.5f / radius;
                Assert.That(g.RoomAt(g.ArenaGate), Is.EqualTo(d.BossRoom));
                Assert.That(walls.View.IsBlocked(g.ArenaGate, body, true), Is.True);
                Assert.That(walls.View.IsBlocked(g.ArenaGate, body, false), Is.False);
            }
        }

        [Test]
        public void Outside_the_grid_is_no_room_and_same_inputs_give_the_same_layout()
        {
            RoomDungeon d = RoomDungeonPlanner.Generate(3, "cave", PlanetSize.Moon, Affixes);
            var a = RoomDungeonGeometry.Build(d, 640f, Anchors[1]);
            var b = RoomDungeonGeometry.Build(d, 640f, Anchors[1]);
            Assert.That(a.RoomAt(DirectionOfCell(a, d, -1, -1)), Is.EqualTo(-1));
            Assert.That(a.RoomAt(-Anchors[1]), Is.EqualTo(-1));
            Assert.That(a.SiteCount, Is.EqualTo(b.SiteCount));
            Assert.That(a.SiteCount, Is.GreaterThan(d.Rooms.Count));
            for (int r = 0; r < d.Rooms.Count; r++)
            {
                Assert.That(a.RoomCenter(r), Is.EqualTo(b.RoomCenter(r)));
            }
        }

        // REQ: planet-world.md#進階地形地城 牆與頂的美術 — wall panels are laid along WallRuns, so every run must sit on a
        // full-height ridge and leave every doorway clear.
        [Test]
        public void Wall_runs_lie_on_full_height_walls_and_keep_off_doors()
        {
            for (ulong seed = 0; seed < 8; seed++)
            {
                foreach (AdvancedTerrainSpec spec in AdvancedTerrains.All)
                {
                    PlanetSize size = SizeFor(spec);
                    float radius = RadiusFor(size);
                    bool maze = spec.Form == AdvancedDungeonForm.SurfaceMaze;
                    RoomDungeon d = maze
                        ? RoomDungeonPlanner.GenerateSurfaceMaze(seed, spec.Id, size, Affixes, 20f)
                        : RoomDungeonPlanner.Generate(seed, spec.Id, size, Affixes);
                    var g = RoomDungeonGeometry.Build(d, radius, Anchors[seed % 3], maze ? 20f : 0f);
                    using PlanetWalls walls = g.BuildWalls(new PlanetConfig(radius, float3.zero));
                    WallView view = walls.View;
                    string at = $"seed {seed} {spec.Id}";
                    Assert.That(g.WallRuns.Count, Is.GreaterThan(d.Rooms.Count), at);
                    foreach (WallRun run in g.WallRuns)
                    {
                        for (int k = 1; k < 10; k++)
                        {
                            float3 p = g.LocalToDirection(math.lerp(run.From, run.To, k / 10f));
                            Assert.That(view.Height(p), Is.GreaterThan(RoomDungeonGeometry.WallHeightM * 0.97f),
                                $"{at} run {run.From}→{run.To} at {k}/10");
                        }
                    }

                    // A body standing in each doorway touches no run's centre line.
                    for (int r = 0; r < d.Rooms.Count; r++)
                    {
                        foreach (int s in Sides)
                        {
                            if ((d.Rooms[r].Doors & s) == 0)
                            {
                                continue;
                            }

                            int n = d.Neighbour(r, s);
                            // The outer door has no room beyond; the plaza's door is at its block edge, not halfway.
                            if (n < 0 || r == d.BossRoom || n == d.BossRoom)
                            {
                                continue;
                            }

                            float3 door = math.normalize(math.lerp(g.SpawnPoint(r, 0), g.SpawnPoint(n, 0), 0.5f));

                            foreach (WallRun run in g.WallRuns)
                            {
                                float2 local = LocalOf(g, door);
                                Assert.That(DistanceToSegment(local, run.From, run.To),
                                    Is.GreaterThan(d.Style.DoorInnerM), $"{at} door room {r} side {s}");
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void Local_directions_round_trip_through_the_grid()
        {
            RoomDungeon d = RoomDungeonPlanner.Generate(3, "temple", PlanetSize.Supergiant, Affixes);
            var g = RoomDungeonGeometry.Build(d, 1280f, Anchors[1]);
            Assert.That(math.distance(g.LocalToDirection(float2.zero), g.Anchor), Is.LessThan(1e-5f));
            Assert.That(math.distance(g.LocalToDirection(g.ArenaCenterLocal), g.RoomCenter(d.BossRoom)), Is.LessThan(1e-5f));
            float2 axis = g.ArenaGateAxis;
            Assert.That(math.abs(axis.x) + math.abs(axis.y), Is.EqualTo(1f));
            float3 gate = g.LocalToDirection(g.ArenaCenterLocal + axis * g.ArenaRadiusM);
            Assert.That(math.distance(gate, g.ArenaGate) * g.Radius, Is.LessThan(0.05f));
            Assert.Throws<ContractException>(() => new WallRun(float2.zero, float2.zero));
        }

        private static float2 LocalOf(RoomDungeonGeometry g, float3 dir)
        {
            PlanetFrame f = PlanetFrame.FromUp(g.Anchor);
            float3 p = dir * (g.Radius / math.dot(dir, g.Anchor));
            return new float2(math.dot(p, f.East), math.dot(p, f.North));
        }

        private static float DistanceToSegment(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float t = math.saturate(math.dot(p - a, ab) / math.dot(ab, ab));
            return math.distance(p, a + ab * t);
        }

        [Test]
        public void Preconditions_are_checked()
        {
            RoomDungeon d = RoomDungeonPlanner.Generate(1, "cave", PlanetSize.Moon, Affixes);
            Assert.Throws<ContractException>(() => RoomDungeonGeometry.Build(null, 640f, Anchors[0]));
            Assert.Throws<ContractException>(() => RoomDungeonGeometry.Build(d, 640f, new float3(0f, 0f, 2f)));
            Assert.Throws<ContractException>(() => RoomDungeonGeometry.Build(d, 50f, Anchors[0]));
            var g = RoomDungeonGeometry.Build(d, 640f, Anchors[0]);
            Assert.Throws<ContractException>(() => g.RoomCenter(-1));
            Assert.Throws<ContractException>(() => g.SpawnPoint(0, -1));
            Assert.Throws<ContractException>(() => g.BuildWalls(new PlanetConfig(960f, float3.zero)));
        }

        // Cell centre of (x, y) whether or not a room is there: offset a room centre by whole cells on the tangent plane.
        private static float3 DirectionOfCell(RoomDungeonGeometry g, RoomDungeon d, int x, int y)
        {
            DungeonRoom r0 = d.Rooms[0];
            float3 c0 = g.RoomCenter(0);
            PlanetFrame f = PlanetFrame.FromUp(g.Anchor);
            float3 p0 = c0 * (g.Radius / math.dot(c0, g.Anchor));
            float3 p = p0 + f.East * ((x - r0.X) * RoomDungeonPlanner.CellSizeM) +
                       f.North * ((y - r0.Y) * RoomDungeonPlanner.CellSizeM);
            return math.normalize(p);
        }

        private static int Dx(int s) => s == RoomDungeon.East ? 1 : s == RoomDungeon.West ? -1 : 0;

        private static int Dy(int s) => s == RoomDungeon.North ? 1 : s == RoomDungeon.South ? -1 : 0;
    }
}
