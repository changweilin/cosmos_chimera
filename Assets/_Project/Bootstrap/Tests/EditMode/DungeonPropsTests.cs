using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: planet-world.md#地城文化 (牆腳物件可破壞).
    public sealed class DungeonPropsTests
    {
        private static readonly string[] Affixes = { "elite_swift", "elite_shielded", "elite_frenzy", "elite_aura" };

        private static RoomDungeonGeometry GeometryFor(AdvancedTerrainSpec spec, ulong seed)
        {
            float3 anchor = math.normalize(new float3(0.2f, 1f, 0.1f));
            if (spec.Form == AdvancedDungeonForm.SurfaceMaze)
            {
                RoomDungeon maze = RoomDungeonPlanner.GenerateSurfaceMaze(seed, spec.Id, spec.MinSize, Affixes, 20f);
                return RoomDungeonGeometry.Build(maze, RadiusFor(maze), anchor, 20f);
            }

            RoomDungeon d = RoomDungeonPlanner.Generate(seed, spec.Id, spec.MinSize, Affixes);
            return RoomDungeonGeometry.Build(d, RadiusFor(d), anchor);
        }

        private static float RadiusFor(RoomDungeon d) => math.max(640f, d.CellSizeM * (math.max(d.Width, d.Height) + 3));

        private static DungeonCulture CultureFor(string terrainId, ulong seed) =>
            DungeonCulture.For(ArchitectureMeshes.PlanetStyleFor("storm_planet", seed), terrainId, seed);

        private static float SegmentDistance(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float t = math.saturate(math.dot(p - a, ab) / math.max(math.lengthsq(ab), 1e-6f));
            return math.distance(p, a + ab * t);
        }

        [Test]
        public void Same_dungeon_and_culture_give_the_same_props()
        {
            foreach (AdvancedTerrainSpec spec in AdvancedTerrains.All)
            {
                RoomDungeonGeometry g = GeometryFor(spec, 3);
                DungeonCulture k = CultureFor(spec.Id, 5);
                List<DungeonProp> a = DungeonDressing.PlanProps(g, spec.Id, k);
                List<DungeonProp> b = DungeonDressing.PlanProps(g, spec.Id, k);
                Assert.That(b.Count, Is.EqualTo(a.Count), spec.Id);
                for (int i = 0; i < a.Count; i++)
                {
                    Assert.That(b[i].Kind, Is.EqualTo(a[i].Kind));
                    Assert.That(b[i].Local, Is.EqualTo(a[i].Local));
                    Assert.That(b[i].Yaw, Is.EqualTo(a[i].Yaw));
                    Assert.That(b[i].Scale, Is.EqualTo(a[i].Scale));
                }
            }
        }

        [Test]
        public void Every_prop_stands_in_a_room_clear_of_every_wall()
        {
            int total = 0;
            foreach (AdvancedTerrainSpec spec in AdvancedTerrains.All)
            {
                for (ulong seed = 1; seed <= 6; seed++)
                {
                    RoomDungeonGeometry g = GeometryFor(spec, seed);
                    foreach (DungeonProp p in DungeonDressing.PlanProps(g, spec.Id, CultureFor(spec.Id, seed)))
                    {
                        total++;
                        float radius = DungeonProp.Body(p.Kind).radiusM * p.Scale;
                        Assert.That(g.RoomAt(g.LocalToDirection(p.Local)), Is.GreaterThanOrEqualTo(0), spec.Id);
                        foreach (WallRun run in g.WallRuns)
                        {
                            Assert.That(SegmentDistance(p.Local, run.From, run.To),
                                Is.GreaterThanOrEqualTo(DungeonDressing.PanelFootHalfM + radius), spec.Id);
                        }

                        Assert.That(math.abs(math.distance(p.Local, g.ArenaCenterLocal) - g.ArenaRadiusM),
                            Is.GreaterThanOrEqualTo(DungeonDressing.PanelFootHalfM + radius), spec.Id);
                        Assert.That(math.length(p.Facing), Is.EqualTo(1f).Within(1e-4f));
                        Assert.That(p.Scale, Is.InRange(0.85f, 1.15f));
                    }
                }
            }

            Assert.That(total, Is.GreaterThan(100), "the cultures above must stand props somewhere");
        }

        [Test]
        public void Every_kind_has_a_body_a_known_substance_and_a_name()
        {
            EnvMaterials env = EnvMaterials.FromJson(File.ReadAllText(Path.Combine(Application.dataPath, "..",
                "Data", "_incoming", "p3-12-env-materials-draft-01", "env-materials", "env-materials.json")));
            string strings = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Data", "Copy", "strings.json"));
            foreach (DungeonPropKind kind in Enum.GetValues(typeof(DungeonPropKind)))
            {
                (float radius, float height, int volume, string substance) = DungeonProp.Body(kind);
                Assert.That(radius, Is.GreaterThan(0f), kind.ToString());
                Assert.That(height, Is.GreaterThan(0f), kind.ToString());
                Assert.That(volume, Is.GreaterThan(0), kind.ToString());
                Assert.That(env.TryGetSubstance(substance, out _), Is.True, kind + " " + substance);
                Assert.That(strings, Does.Contain("\"" + DungeonProp.NameKey(kind) + "\""), kind.ToString());
            }
        }

        [Test]
        public void Null_arguments_are_rejected()
        {
            Assert.Throws<ContractException>(() => DungeonDressing.PlanProps(null, "temple", default));
            AdvancedTerrains.TryGet("temple", out AdvancedTerrainSpec spec);
            RoomDungeonGeometry g = GeometryFor(spec, 1);
            Assert.Throws<ContractException>(() => DungeonDressing.PlanProps(g, null, default));
        }
    }
}
