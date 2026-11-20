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
