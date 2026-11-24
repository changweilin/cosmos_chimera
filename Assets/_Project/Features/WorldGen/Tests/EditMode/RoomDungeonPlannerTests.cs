using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;
using UnityEngine;

namespace Cosmimera.WorldGen.Tests
{
    // REQ: docs/design/planet-world.md#進階地形地城 (P3-07), docs/design/trainer-battles.md#地形試煉
    public sealed class RoomDungeonPlannerTests
    {
        private static readonly string[] Affixes = { "elite_swift", "elite_shielded", "elite_frenzy", "elite_aura" };
        private static readonly PlanetSize[] Sizes = { PlanetSize.Moon, PlanetSize.Planet, PlanetSize.Supergiant };
        private static readonly int[] Sides = { RoomDungeon.North, RoomDungeon.East, RoomDungeon.South, RoomDungeon.West };

        private static IEnumerable<(ulong seed, AdvancedTerrainSpec spec, PlanetSize size)> Cases(int seeds)
        {
            for (ulong seed = 0; seed < (ulong)seeds; seed++)
            {
                foreach (AdvancedTerrainSpec spec in AdvancedTerrains.All)
                {
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

        private static int Degree(int doors)
        {
            int n = 0;
            foreach (int s in Sides)
            {
                if ((doors & s) != 0) n++;
            }

            return n;
        }

        [Test]
        public void Every_room_is_reachable_and_every_door_matches_its_neighbour()
        {
            foreach (var (seed, spec, size) in Cases(60))
            {
                RoomDungeon d = RoomDungeonPlanner.Generate(seed, spec.Id, size, Affixes);
                int n = d.Rooms.Count;
                Assert.That(n, Is.InRange(RoomDungeonPlanner.BaseRoomCount(size), RoomDungeonPlanner.BaseRoomCount(size) + 2));
                Assert.That(d.EntranceRoom, Is.EqualTo(0));
                Assert.That(d.Rooms[0].Y, Is.EqualTo(0), "entrance on the south edge");

                for (int r = 0; r < n; r++)
                {
                    DungeonRoom room = d.Rooms[r];
                    Assert.That(d.RoomAt(room.X, room.Y), Is.EqualTo(r));
                    Assert.That(room.Doors, Is.Not.Zero);
                    foreach (int s in Sides)
                    {
                        int nb = d.Neighbour(r, s);
                        if ((room.Doors & s) == 0)
                        {
                            Assert.That(nb, Is.EqualTo(-1));
                            continue;
                        }

                        Assert.That(nb, Is.GreaterThanOrEqualTo(0), $"door into nothing seed {seed} {spec.Id} room {r}");
                        Assert.That(d.Neighbour(nb, RoomDungeon.Opposite(s)), Is.EqualTo(r), "doors match on both sides");
                    }
                }

                var seen = new bool[n];
                var queue = new Queue<int>();
                queue.Enqueue(0);
                seen[0] = true;
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

                Assert.That(reached, Is.EqualTo(n), $"sealed room seed {seed} {spec.Id} {size}");
            }
        }

        [Test]
        public void Boss_is_a_dead_end_behind_the_guard_and_dead_ends_hold_treasure()
        {
            foreach (var (seed, spec, size) in Cases(60))
            {
                RoomDungeon d = RoomDungeonPlanner.Generate(seed, spec.Id, size, Affixes);
                DungeonRoom boss = d.Rooms[d.BossRoom];
                Assert.That(boss.Role, Is.EqualTo(DungeonRoomRole.Boss));
                Assert.That(Degree(boss.Doors), Is.EqualTo(1));
                Assert.That(d.GuardRoom, Is.Not.EqualTo(d.EntranceRoom));
                Assert.That(d.Rooms[d.GuardRoom].Role, Is.EqualTo(DungeonRoomRole.Guard));
                foreach (int s in Sides)
                {
                    int nb = d.Neighbour(d.BossRoom, s);
                    Assert.That(nb == -1 || nb == d.GuardRoom, Is.True, "only the guard room leads to the boss");
                }

                int bosses = 0;
                int guards = 0;
                for (int r = 0; r < d.Rooms.Count; r++)
                {
                    DungeonRoom room = d.Rooms[r];
                    bosses += room.Role == DungeonRoomRole.Boss ? 1 : 0;
                    guards += room.Role == DungeonRoomRole.Guard ? 1 : 0;
                    bool deadEnd = Degree(room.Doors) == 1 && r != d.EntranceRoom && r != d.BossRoom;
                    Assert.That(room.Role == DungeonRoomRole.Treasure, Is.EqualTo(deadEnd), $"room {r} seed {seed}");
                }

                Assert.That(bosses, Is.EqualTo(1));
                Assert.That(guards, Is.EqualTo(1));
            }
        }

        [Test]
        public void Affixes_match_the_room_role_and_are_distinct()
        {
            foreach (var (seed, spec, size) in Cases(30))
            {
                RoomDungeon d = RoomDungeonPlanner.Generate(seed, spec.Id, size, Affixes);
                foreach (DungeonRoom room in d.Rooms)
                {
                    int count = room.Affixes.Count;
                    switch (room.Role)
                    {
                        case DungeonRoomRole.Guard: Assert.That(count, Is.EqualTo(2)); break;
                        case DungeonRoomRole.Treasure: Assert.That(count, Is.EqualTo(1)); break;
                        case DungeonRoomRole.Elite: Assert.That(count, Is.InRange(1, 2)); break;
                        default: Assert.That(count, Is.Zero); break;
                    }

                    CollectionAssert.AllItemsAreUnique(room.Affixes);
                    CollectionAssert.IsSubsetOf(room.Affixes, Affixes);
                }
            }
        }

        [Test]
        public void Affix_count_is_capped_by_a_small_pool()
        {
            RoomDungeon d = RoomDungeonPlanner.Generate(3, "cave", PlanetSize.Moon, new[] { "elite_swift" });
            Assert.That(d.Rooms[d.GuardRoom].Affixes.Count, Is.EqualTo(1));
        }

        [Test]
        public void Shape_and_rotation_reproduce_the_door_mask()
        {
            int[] canon = { RoomDungeon.North, RoomDungeon.North | RoomDungeon.South, RoomDungeon.North | RoomDungeon.East,
                RoomDungeon.North | RoomDungeon.East | RoomDungeon.South, 15 };
            foreach (var (seed, spec, size) in Cases(20))
            {
                foreach (DungeonRoom room in RoomDungeonPlanner.Generate(seed, spec.Id, size, Affixes).Rooms)
                {
                    int mask = canon[(int)room.Shape];
                    for (int i = 0; i < room.Rotation; i++)
                    {
                        mask = ((mask << 1) | (mask >> 3)) & 15;
                    }

                    Assert.That(mask, Is.EqualTo(room.Doors));
                    Assert.That(room.Rotation, Is.InRange(0, 3));
                    StringAssert.StartsWith(spec.Id + "_", room.ModuleId);
                }
            }
        }

        [Test]
        public void Alias_resolves_to_the_canonical_terrain()
        {
            RoomDungeon byAlias = RoomDungeonPlanner.Generate(9, "mine", PlanetSize.Moon, Affixes);
            RoomDungeon byId = RoomDungeonPlanner.Generate(9, "cave", PlanetSize.Moon, Affixes);
            Assert.That(byAlias.TerrainId, Is.EqualTo("cave"));
            Assert.That(RoomDungeonPlanner.Hash(byAlias), Is.EqualTo(RoomDungeonPlanner.Hash(byId)));
        }

        [Test]
        public void Same_inputs_same_dungeon_and_terrains_get_their_own_stream()
        {
            for (ulong seed = 0; seed < 20; seed++)
            {
                ulong a = RoomDungeonPlanner.Hash(RoomDungeonPlanner.Generate(seed, "tower", PlanetSize.Planet, Affixes));
                ulong b = RoomDungeonPlanner.Hash(RoomDungeonPlanner.Generate(seed, "tower", PlanetSize.Planet, Affixes));
                ulong c = RoomDungeonPlanner.Hash(RoomDungeonPlanner.Generate(seed, "space", PlanetSize.Planet, Affixes));
                Assert.That(a, Is.EqualTo(b));
                Assert.That(a, Is.Not.EqualTo(c));
            }
        }

        [Test]
        public void Snapshot_matches_generator_version_1()
        {
            Assert.That(RoomDungeonPlanner.GeneratorVersion, Is.EqualTo(1), "bump the snapshot file with the version");
            var actualLines = new List<string>();
            foreach (AdvancedTerrainSpec spec in AdvancedTerrains.All)
            {
                foreach (PlanetSize size in Sizes)
                {
                    if (!AdvancedTerrains.AppearsOn(spec, size)) continue;
                    RoomDungeon d = RoomDungeonPlanner.Generate(12345UL, spec.Id, size, Affixes);
                    actualLines.Add(spec.Id + " " + size + " 0x" + RoomDungeonPlanner.Hash(d).ToString("X16"));
                }
            }

            string snapPath = Path.Combine(Application.dataPath, "_Project/Features/WorldGen/Tests/Snapshots/room-dungeon-v1.txt");
            if (!File.Exists(snapPath))
            {
                string dump = Path.Combine(Application.dataPath, "../Logs/room-dungeon-v1.actual.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(dump));
                File.WriteAllLines(dump, actualLines);
                Assert.Fail($"snapshot missing; actual written to {dump}");
            }

            CollectionAssert.AreEqual(File.ReadAllLines(snapPath), actualLines);
        }

        [Test]
        public void Contract_violations_throw()
        {
            Assert.Throws<ContractException>(() => RoomDungeonPlanner.Generate(1, "forest", PlanetSize.Moon, Affixes));
            Assert.Throws<ContractException>(() => RoomDungeonPlanner.Generate(1, null, PlanetSize.Moon, Affixes));
            Assert.Throws<ContractException>(() => RoomDungeonPlanner.Generate(1, "temple", PlanetSize.Planet, Affixes),
                "temple appears on supergiants only");
            Assert.Throws<ContractException>(() => RoomDungeonPlanner.Generate(1, "cave", PlanetSize.Asteroid, Affixes));
            Assert.Throws<ContractException>(() => RoomDungeonPlanner.Generate(1, "cave", PlanetSize.Moon, null));
            Assert.Throws<ContractException>(() => RoomDungeonPlanner.Generate(1, "cave", PlanetSize.Moon, new string[0]));
            Assert.Throws<ContractException>(() => RoomDungeonPlanner.Generate(1, "cave", PlanetSize.Moon, new[] { "a", "a" }));
            Assert.Throws<ContractException>(() => RoomDungeonPlanner.Generate(1, "cave", PlanetSize.Moon, new[] { "" }));
            Assert.Throws<ContractException>(() => RoomDungeonPlanner.BaseRoomCount(PlanetSize.Asteroid));
            Assert.Throws<ContractException>(() => RoomDungeonPlanner.Hash(null));

            RoomDungeon d = RoomDungeonPlanner.Generate(1, "cave", PlanetSize.Moon, Affixes);
            Assert.Throws<ContractException>(() => d.Neighbour(-1, RoomDungeon.North));
            Assert.Throws<ContractException>(() => d.Neighbour(0, RoomDungeon.North | RoomDungeon.East));
            Assert.That(d.RoomAt(-1, 0), Is.EqualTo(-1));
            Assert.That(d.RoomAt(d.Width, 0), Is.EqualTo(-1));
        }
    }
}
