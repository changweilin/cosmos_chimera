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
        public void A_destroyed_building_stops_blocking()
        {
            Rig r = Build();
            Assert.That(r.Objects.IsBlocked(VillageAt, 0.3f), Is.True);
            r.Objects.OnDestroyed(WorldObjects.BuildingIdBase);
            Assert.That(r.Objects.IsBlocked(VillageAt, 0.3f), Is.False);
            Assert.That(r.Objects.IsBlocked(RuinAt, 0.3f), Is.True, "only that building");
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
    }
}
