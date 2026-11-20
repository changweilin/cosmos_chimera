using System.Collections.Generic;
using System.IO;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Bootstrap.Tests
{
    // REQ: docs/design/destructibles.md (碰撞, 狀態保留) — the bridge between generation and the combat rule layer.
    public sealed class WorldObjectsTests
    {
        private static readonly PlanetConfig Planet = new PlanetConfig(640f, float3.zero);
        private static readonly float3 VillageAt = new float3(0, 1, 0);
        private static readonly float3 RuinAt = math.normalize(new float3(1, 1, 0));

        private static EnvMaterials Env() => EnvMaterials.FromJson(File.ReadAllText(Path.Combine(Application.dataPath, "..",
            "Data", "_incoming", "p3-12-env-materials-draft-01", "env-materials", "env-materials.json")));

        private static RunLayout Layout()
        {
            var house = new RunBuilding(0, BuildingKind.House, VillageAt, new float3(1, 0, 0), 6f, 4f, 1);
            var ruin = new RunBuilding(1, BuildingKind.House, RuinAt, new float3(0, 0, 1), 5f, 5f, 2);
            var village = new RunSettlement(0, 0, SettlementKind.Village, VillageAt, 0, 1);
            var ruins = new RunSettlement(1, 1, SettlementKind.Ruins, RuinAt, 1, 1);
            return new RunLayout(new RunRoad[0], new[] { village, ruins }, new[] { house, ruin });
        }

        private sealed class Rig
        {
            public ScatterPlanner Scatter;
            public LayoutClearance Clearance;
            public DestructibleTable Table;
            public WorldObjects Objects;
        }

        private static Rig Build()
        {
            RunLayout layout = Layout();
            var clearance = new LayoutClearance(Planet, layout, 1.5f);
            var scatter = new ScatterPlanner(Planet, 99, "verdant_planet", _ => 0f, null, clearance.IsClear);
            EnvMaterials env = Env();
            var table = new DestructibleTable(env);
            return new Rig
            {
                Scatter = scatter, Clearance = clearance, Table = table,
                Objects = new WorldObjects(Planet, scatter, layout, clearance, _ => 0f, env, table),
            };
        }

        private static ScatterItem FirstTree(ScatterPlanner p)
        {
            for (long k = 0; ; k++)
            {
                if (p.TryGetItem(k, out ScatterItem it) && it.Shape == ScatterShape.Tree) return it;
            }
        }

        [Test]
        public void Field_culture_v3_replays_the_three_families_on_one_road()
        {
            var regions = new[] { new RunRegion(0, VillageAt, new[] { 1 }, "forest", null, 1, 0),
                new RunRegion(1, RuinAt, new[] { 0 }, "coast", null, 1, 1) };
            var road = new RunRoad(RoadClass.Main, 0, 1, 4f, new[] { VillageAt, RuinAt }, new bool[2]);
            var layout = new RunLayout(new[] { road }, new RunSettlement[0], new RunBuilding[0]);
            var plan = new RunPlan(7, "verdant_planet", null, regions, 0, 1, new[] { 0, 1 }, new RunSpawn[0],
                new RunQuestStep[0], VillageAt);
            var field = FieldSpecimen.Generate(Planet, layout, plan, plan.PlanetTypeId, 7, d => 1f, d => true, d => false);
            // Version 4 only added landform wildlife (terrainAt); without it the v3 replay holds.
            Assert.That(FieldSpecimen.GeneratorVersion, Is.EqualTo(4));
            var snapshot = File.ReadAllLines(Path.Combine(Application.dataPath, "_Project/Bootstrap/Tests/Snapshots/field-culture-v3.txt"));
            Assert.That(field.ConvertAll(s => $"{s.Style},{s.Duty},{s.Age},{s.Damage}"), Is.EqualTo(snapshot));
            Assert.That(FieldSpecimen.Generate(Planet, layout, plan, plan.PlanetTypeId, 7, d => 1f, d => false, d => false), Is.Empty);
        }

        [Test]
        public void Land_vehicles_shuttle_the_whole_clear_stretch_of_their_road()
        {
            var regions = new[] { new RunRegion(0, VillageAt, new[] { 1 }, "forest", null, 1, 0),
                new RunRegion(1, RuinAt, new[] { 0 }, "coast", null, 1, 1) };
            float roadM = math.distance(VillageAt, RuinAt) * Planet.Radius;
            int count = (int)math.ceil(roadM / 6f) + 1;
            var points = new float3[count];
            for (int i = 0; i < count; i++) points[i] = math.normalize(math.lerp(VillageAt, RuinAt, i / (float)(count - 1)));
            var road = new RunRoad(RoadClass.Main, 0, 1, 4f, points, new bool[count]);
            var layout = new RunLayout(new[] { road }, new RunSettlement[0], new RunBuilding[0]);
            var plan = new RunPlan(7, "verdant_planet", null, regions, 0, 1, new[] { 0, 1 }, new RunSpawn[0],
                new RunQuestStep[0], VillageAt);
            Assume.That(roadM, Is.GreaterThan(FieldSpecimen.MinLongRouteM + 20f));
            System.Func<float3, bool> beyondVillage = d => math.distance(d, VillageAt) * Planet.Radius > 10f;
            var field = FieldSpecimen.Generate(Planet, layout, plan, plan.PlanetTypeId, 7, d => 1f, beyondVillage, d => false);
            FieldSpecimen vehicle = field.Find(s => !s.Animal && s.Form > FieldForm.Hoist);
            Assert.That(vehicle, Is.Not.Null);
            float3 first = math.normalize(vehicle.RouteSurface(0)), last = math.normalize(vehicle.RouteSurface(vehicle.RouteCount - 1));
            Assert.That(math.distance(first, VillageAt) * Planet.Radius, Is.InRange(10f, 16f));
            Assert.That(math.distance(last, RuinAt) * Planet.Radius, Is.LessThan(0.5f));
        }

        private static RunRoad Straight(RoadClass roadClass, int from, int to, float3 a, float3 b)
        {
            int count = (int)math.ceil(math.distance(a, b) * Planet.Radius / 6f) + 1;
            var points = new float3[count];
            for (int i = 0; i < count; i++) points[i] = math.normalize(math.lerp(a, b, i / (float)(count - 1)));
            return new RunRoad(roadClass, from, to, roadClass == RoadClass.Main ? 4f : 2.5f, points, new bool[count]);
        }

        [Test]
        public void Some_land_vehicles_drive_one_way_round_a_closed_road_cycle()
        {
            float3 far = math.normalize(new float3(0, 1, 1));
            var regions = new[] { new RunRegion(0, VillageAt, new[] { 1, 2 }, "forest", null, 1, 0),
                new RunRegion(1, RuinAt, new[] { 0, 2 }, "coast", null, 1, 1),
                new RunRegion(2, far, new[] { 0, 1 }, "forest", null, 1, 1) };
            var layout = new RunLayout(new[] { Straight(RoadClass.Main, 0, 1, VillageAt, RuinAt),
                Straight(RoadClass.Trail, 0, 2, VillageAt, far), Straight(RoadClass.Loop, 1, 2, RuinAt, far) },
                new RunSettlement[0], new RunBuilding[0]);
            int loops = 0, shuttles = 0;
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var plan = new RunPlan(seed, "verdant_planet", null, regions, 0, 1, new[] { 0, 1 }, new RunSpawn[0],
                    new RunQuestStep[0], VillageAt);
                foreach (FieldSpecimen s in FieldSpecimen.Generate(Planet, layout, plan, plan.PlanetTypeId, seed, d => 1f, d => true, d => false))
                {
                    if (s.Animal || s.Form <= FieldForm.Hoist) continue;
                    if (!s.Loop) { shuttles++; continue; }
                    loops++;
                    Assert.That(math.distance(s.RouteUp(0), s.RouteUp(s.RouteCount - 1)), Is.LessThan(1e-4f));
                    foreach (int k in new[] { 0, s.RouteCount / 3, 2 * s.RouteCount / 3 })
                        Assert.That(math.distance(s.RouteUp(k), s.RouteUp(k + 1)) * Planet.Radius, Is.LessThan(4f));
                    for (long t = 0; t < 600_000; t += 37_000)
                    {
                        s.Step(t);
                        float3 at = s.Position, facing = s.Facing;
                        s.Step(t + 500);
                        Assert.That(math.dot(s.Position - at, facing), Is.GreaterThan(0f), $"seed {seed} t {t}: reversed");
                    }
                }
            }
            Assert.That(loops, Is.GreaterThan(0));
            Assert.That(shuttles, Is.GreaterThan(loops));
        }

        [Test]
        public void Gathering_queries_only_nearby_intact_scatter_and_preserves_the_source_kind()
        {
            Rig r = Build();
            ScatterItem tree = FirstTree(r.Scatter);
            var nodes = new List<GatheringNode>();
            r.Objects.QueryGathering(tree.Direction, 0.1f, nodes);
            Assert.That(nodes.Exists(n => n.Id == tree.CellKey && n.Source == r.Scatter.Kinds[tree.KindIndex].Id), Is.True);
            var found = new List<DestructibleCandidate>();
            r.Objects.Query(Planet.ToWorld(tree.Direction, 0f), 1f, found);
            DestructibleCandidate candidate = found.Find(c => c.Id == tree.CellKey);
            int slot = r.Table.Register(candidate.Id, candidate.Composition, candidate.VolumeL, candidate.Center);
            r.Table.ApplyHit(slot, Element.Earth, 100_000, 0, 0);
            r.Objects.QueryGathering(tree.Direction, 0.1f, nodes);
            Assert.That(nodes.Exists(n => n.Id == tree.CellKey), Is.False);
            r.Objects.QueryGathering(-tree.Direction, 0.1f, nodes);
            Assert.That(nodes.Exists(n => n.Id == tree.CellKey), Is.False);
        }

        [Test]
        public void Buildings_get_ids_above_scatter_and_their_settlement_material()
        {
            Rig r = Build();
            var found = new List<DestructibleCandidate>();
            r.Objects.Query(Planet.ToWorld(VillageAt, 0f), 5f, found);
            DestructibleCandidate house = found.Find(c => WorldObjects.IsBuilding(c.Id));
            Assert.That(house.Id, Is.EqualTo(WorldObjects.BuildingIdBase));
            Assert.That(house.Composition.Dominant.Id, Is.EqualTo("wood"));
            Assert.That(house.VolumeL, Is.EqualTo(6 * 4 * 3 * 1000));
            for (int i = 1; i < found.Count; i++) Assert.That(found[i].Id, Is.GreaterThan(found[i - 1].Id));

            found.Clear();
            r.Objects.Query(Planet.ToWorld(RuinAt, 0f), 5f, found);
            DestructibleCandidate ruin = found.Find(c => WorldObjects.IsBuilding(c.Id));
            Assert.That(ruin.Id, Is.EqualTo(WorldObjects.BuildingIdBase + 1));
            Assert.That(ruin.Composition.Dominant.Id, Is.EqualTo("stone"), "a Ruins settlement's buildings are ruin walls");
            Assert.That(r.Objects.MaxHitRadiusM, Is.GreaterThanOrEqualTo(3f));
        }

        [Test]
        public void A_tree_blocks_until_destroyed()
        {
            Rig r = Build();
            ScatterItem tree = FirstTree(r.Scatter);
            Assert.That(r.Objects.IsBlocked(tree.Direction, 0f), Is.True);
            float3 beside = math.normalize(tree.Direction + PlanetFrame.FromUp(tree.Direction).East * ((tree.BlockRadiusM + 0.3f) / Planet.Radius));
            Assert.That(r.Objects.IsBlocked(beside, 0f), Is.False, "the crown does not block, only the trunk");
            Assert.That(r.Objects.IsBlocked(beside, 0.5f), Is.True, "a body's radius counts");

            var found = new List<DestructibleCandidate>();
            r.Objects.Query(Planet.ToWorld(tree.Direction, 0f), 1f, found);
            DestructibleCandidate c = found.Find(x => x.Id == tree.CellKey);
            Assert.That(c.Composition.Dominant.Id, Is.EqualTo("wood"));
            int slot = r.Table.Register(c.Id, c.Composition, c.VolumeL, c.Center);
            r.Table.ApplyHit(slot, Element.Earth, 100_000, 0, 0);
            Assert.That(r.Objects.IsBlocked(tree.Direction, 0f), Is.False);
        }

        [Test]
        public void Tall_grass_can_be_hit_but_neither_blocks_nor_is_shoved()
        {
            Rig r = Build();
            ScatterItem grass = default, shrub = default;
            for (long k = 6L * r.Scatter.CellsPerFace * r.Scatter.CellsPerFace; k < r.Scatter.KeyCount; k++)
            {
                if (!r.Scatter.TryGetItem(k, out ScatterItem it)) continue;
                if (it.Shape == ScatterShape.TallGrass && grass.SizeM == 0f) grass = it;
                if (it.Shape == ScatterShape.Shrub && shrub.SizeM == 0f) shrub = it;
                if (grass.SizeM > 0f && shrub.SizeM > 0f) break;
            }

            Assert.That(grass.SizeM, Is.GreaterThan(0f));
            Assert.That(shrub.SizeM, Is.GreaterThan(0f));
            Assert.That(r.Objects.IsBlocked(grass.Direction, 0f), Is.False, "grass does not block");
            Assert.That(r.Objects.IsBlocked(shrub.Direction, 0f), Is.True, "a shrub blocks");
            Assert.That(r.Objects.CanBeShoved(grass.CellKey), Is.False);
            Assert.That(r.Objects.CanBeShoved(shrub.CellKey), Is.True);
            Assert.That(r.Objects.CanBeShoved(WorldObjects.BuildingIdBase), Is.True);
            Assert.That(r.Objects.TryGet(grass.CellKey, out DestructibleCandidate c), Is.True);
            Assert.That(c.Composition.Dominant.Id, Is.EqualTo("plant"));
            Assert.That(r.Objects.NameKey(c), Is.EqualTo("ui.object.tall_grass"));
            Assert.That(r.Objects.TryGet(shrub.CellKey, out DestructibleCandidate s), Is.True);
            Assert.That(r.Objects.NameKey(s), Is.EqualTo("ui.object.shrub"));
        }

        [Test]
        public void Grass_cover_uses_its_footprint_and_disappears_when_destroyed()
        {
            Rig r = Build();
            ScatterItem grass = default;
            for (long k = 6L * r.Scatter.CellsPerFace * r.Scatter.CellsPerFace; k < r.Scatter.KeyCount; k++)
            {
                if (r.Scatter.TryGetItem(k, out ScatterItem item) && item.Shape == ScatterShape.TallGrass)
                {
                    grass = item;
                    break;
                }
            }

            Assert.That(grass.SizeM, Is.GreaterThan(0f), "fixture must contain tall grass");
            // Keep only this clump so neighbouring grass cannot hide an incorrect footprint boundary.
            var scatter = new ScatterPlanner(Planet, 99, "verdant_planet", _ => 0f, null,
                d => math.distance(d, grass.Direction) * Planet.Radius < 0.01f);
            EnvMaterials env = Env();
            var table = new DestructibleTable(env);
            var objects = new WorldObjects(Planet, scatter, Layout(), r.Clearance, _ => 0f, env, table);
            Assert.That(objects.TryGet(grass.CellKey, out DestructibleCandidate candidate), Is.True);
            float3 east = PlanetFrame.FromUp(grass.Direction).East;
            float3 inside = math.normalize(grass.Direction + east * (candidate.HitRadiusM * 0.9f / Planet.Radius));
            float3 outside = math.normalize(grass.Direction + east * (candidate.HitRadiusM * 1.1f / Planet.Radius));
            Assert.That(objects.HasGrassCover(grass.Direction), Is.True);
            Assert.That(objects.HasGrassCover(inside), Is.True);
            Assert.That(objects.HasGrassCover(outside), Is.False);
            Assert.That(objects.HasGrassCover(-grass.Direction), Is.False);
            Assert.That(objects.HasGrassCover(FirstTree(r.Scatter).Direction), Is.False);

            int slot = table.Register(candidate.Id, candidate.Composition, candidate.VolumeL, candidate.Center);
            table.ApplyHit(slot, Element.Earth, 100_000, 0, 0);
            Assert.That(table.IsDestroyed(grass.CellKey), Is.True);
            Assert.That(objects.HasGrassCover(grass.Direction), Is.False);
            Assert.That(objects.HasGrassCover(inside), Is.False);
        }

        [Test]
        public void TryGet_answers_like_Query_and_rejects_unknown_ids()
        {
            Rig r = Build();
            ScatterItem tree = FirstTree(r.Scatter);
            var found = new List<DestructibleCandidate>();
            r.Objects.Query(Planet.ToWorld(VillageAt, 0f), 5f, found);
            r.Objects.Query(Planet.ToWorld(tree.Direction, 0f), 1f, found);
            foreach (DestructibleCandidate q in found)
            {
                Assert.That(r.Objects.TryGet(q.Id, out DestructibleCandidate g), Is.True, "id " + q.Id);
                Assert.That(g.Center, Is.EqualTo(q.Center));
                Assert.That(g.HitRadiusM, Is.EqualTo(q.HitRadiusM));
                Assert.That(g.VolumeL, Is.EqualTo(q.VolumeL));
                Assert.That(g.Composition, Is.SameAs(q.Composition));
            }

            Assert.That(found.Exists(c => c.Id == tree.CellKey), Is.True);
            Assert.That(r.Objects.TryGet(WorldObjects.BuildingIdBase + 2, out _), Is.False, "only two buildings");
            Assert.That(r.Objects.TryGet(-1, out _), Is.False);
            Assert.That(r.Objects.TryGet(r.Scatter.KeyCount, out _), Is.False);
        }

        [Test]
        public void A_destroyed_building_stops_blocking()
        {
            Rig r = Build();
            Assert.That(r.Objects.IsBlocked(VillageAt, 0.3f), Is.True);
            r.Objects.OnDestroyed(WorldObjects.BuildingIdBase);
            Assert.That(r.Objects.IsBlocked(VillageAt, 0.3f), Is.False);
            Assert.That(r.Objects.IsBlocked(RuinAt, 0.3f), Is.True, "only that building");
        }

        // REQ: planet-world.md#地城文化 (牆腳物件可破壞), ADR-0011 — indoor props exist only while the party is inside,
        // and then nothing of the surface does; surface-maze props are ordinary surface objects.
        [Test]
        public void Dungeon_props_are_hit_and_block_on_their_own_side_and_stop_when_destroyed()
        {
            Rig r = Build();
            float3 indoor = math.normalize(new float3(0.3f, 0.2f, 1f));
            float3 maze = math.normalize(new float3(0.3f, -0.2f, 1f));
            r.Objects.AttachDungeonProps(new[]
            {
                new WorldObjects.PropBody(indoor, 0f, 0.5f, 1f, "wood", 300, true, "ui.object.dungeon_crate"),
                new WorldObjects.PropBody(maze, 0f, 0.4f, 0.9f, "kerosene", 250, false, "ui.object.dungeon_oil_drum"),
            });
            long indoorId = WorldObjects.DungeonPropIdBase, mazeId = WorldObjects.DungeonPropIdBase + 1;
            var found = new List<DestructibleCandidate>();

            r.Objects.Query(Planet.ToWorld(maze, 0.5f), 1f, found);
            Assert.That(found.Exists(c => c.Id == mazeId), Is.True);
            Assert.That(found.Exists(c => c.Id == indoorId), Is.False);
            Assert.That(r.Objects.IsBlocked(maze, 0.3f), Is.True);
            Assert.That(r.Objects.IsBlockedByDungeonProps(indoor, 0.3f, true), Is.True);

            r.Objects.InsideDungeon = true;
            found.Clear();
            r.Objects.Query(Planet.ToWorld(indoor, 0.5f), 1f, found);
            Assert.That(found.Count, Is.EqualTo(1));
            Assert.That(found[0].Id, Is.EqualTo(indoorId));
            Assert.That(found[0].Composition.Dominant.Id, Is.EqualTo("wood"));
            Assert.That(r.Objects.NameKey(found[0]), Is.EqualTo("ui.object.dungeon_crate"));
            Assert.That(r.Objects.CanBeShoved(indoorId), Is.True);
            Assert.That(WorldObjects.IsDungeonProp(indoorId) && !WorldObjects.IsBuilding(indoorId), Is.True);
            // Nothing of the surface answers while inside, even where a village stands at the same world point.
            found.Clear();
            r.Objects.Query(Planet.ToWorld(VillageAt, 1f), 8f, found);
            Assert.That(found, Is.Empty);
            Assert.That(r.Objects.HasGrassCover(VillageAt), Is.False);

            Assert.That(r.Objects.TryGet(indoorId, out DestructibleCandidate c), Is.True);
            int slot = r.Table.Register(indoorId, c.Composition, c.VolumeL, c.Center);
            r.Table.ApplyHit(slot, Element.Earth, 100_000, 0, 0);
            Assert.That(r.Objects.IsPropDestroyed(0), Is.True);
            Assert.That(r.Objects.IsBlockedByDungeonProps(indoor, 0.3f, true), Is.False);
            found.Clear();
            r.Objects.Query(Planet.ToWorld(indoor, 0.5f), 1f, found);
            Assert.That(found, Is.Empty);
            Assert.That(r.Objects.TryGet(WorldObjects.DungeonPropIdBase + 2, out _), Is.False);
        }

        [Test]
        public void Contract_violations_throw()
        {
            Rig r = Build();
            RunLayout layout = Layout();
            EnvMaterials env = Env();
            Assert.Throws<ContractException>(() => new WorldObjects(Planet, null, layout, r.Clearance, _ => 0f, env, r.Table));
            Assert.Throws<ContractException>(() => new WorldObjects(Planet, r.Scatter, layout, r.Clearance, null, env, r.Table));
            Assert.Throws<ContractException>(() => r.Objects.Query(float3.zero, -1f, new List<DestructibleCandidate>()));
            Assert.Throws<ContractException>(() => r.Objects.Query(new float3(0, 700, 0), 1f, null));
            Assert.Throws<ContractException>(() => r.Objects.HasGrassCover(float3.zero));
            Assert.Throws<ContractException>(() => r.Objects.HasGrassCover(new float3(0f, 2f, 0f)));
            Assert.Throws<ContractException>(() => r.Objects.HasGrassCover(new float3(float.NaN, 1f, 0f)));
            Assert.Throws<ContractException>(() => r.Objects.HasGrassCover(new float3(float.PositiveInfinity, 0f, 0f)));

            // A scatter substance missing from env-materials must fail at construction, not at the first hit.
            Substance stone = env.Ground(Element.Earth);
            var buildings = new Composition[EnvMaterials.BuildingMaterialCount];
            for (int i = 0; i < buildings.Length; i++) buildings[i] = Composition.Of(stone);
            var bare = new EnvMaterials(100, 250, 150, 5000, 200, 150, 200, new VolumeChance(0, 0, 0), new VolumeChance(0, 0, 0),
                0, 0, buildings, new[] { env.Ground(Element.Metal), env.Ground(Element.Wood), env.Ground(Element.Water),
                    env.Ground(Element.Fire), stone, env.Ground(Element.Ice), env.Ground(Element.Wind),
                    env.Ground(Element.Thunder), null, null }, env.GemSubstance);
            Assert.Throws<ContractException>(() =>
                new WorldObjects(Planet, r.Scatter, layout, r.Clearance, _ => 0f, bare, new DestructibleTable(bare)));
        }

        [Test]
        public void Roadside_wildlife_follows_the_land()
        {
            // REQ: planet-world.md#自然生物 緯度×海拔.
            Assert.That(FieldSpecimen.Native("desert", 0.2f, 0f, 100), Is.EqualTo(FieldForm.Lizard));
            Assert.That(FieldSpecimen.Native("swamp", 0.2f, 0f, 100), Is.EqualTo(FieldForm.Frog));
            Assert.That(FieldSpecimen.Native("archipelago", 0.2f, 0f, 100), Is.EqualTo(FieldForm.Crab));
            Assert.That(FieldSpecimen.Native("grassland", 0.8f, 0f, 100), Is.EqualTo(FieldForm.Fox), "polar latitude");
            Assert.That(FieldSpecimen.Native("grassland", 0.3f, 0.02f, 100), Is.EqualTo(FieldForm.Goat), "highland");
            Assert.That(FieldSpecimen.Native("forest", 0.3f, 0f, 100), Is.EqualTo(FieldForm.Deer));
            for (int duty = 0; duty <= 1000; duty += 50)
            {
                foreach (string terrain in new[] { "grassland", "desert", "swamp", "tundra", "mountain", "fjord", null })
                {
                    FieldForm f = FieldSpecimen.Native(terrain, 0.4f, 0f, duty);
                    Assert.That(f >= FieldForm.Hare && f != FieldForm.Fish, Is.True, terrain + " " + duty);
                }
            }
        }
    }
}
