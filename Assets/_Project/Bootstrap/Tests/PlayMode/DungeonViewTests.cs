using System.Collections;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class DungeonViewTests
    {
        private const float Radius = 320f;
        private static readonly PlanetConfig Planet = new PlanetConfig(Radius, float3.zero);
        private readonly List<GameObject> _roots = new List<GameObject>();

        private static RunPlan Plan(ulong seed)
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            Assert.That(db, Is.Not.Null);
            var wild = new List<CreatureDef>();
            PlanetTypeDef type = null;
            foreach (GameDef d in db.All)
            {
                if (d is CreatureDef c && !c.LineId.StartsWith("starter_"))
                {
                    wild.Add(c);
                }
                else if (d is PlanetTypeDef t && (type == null || t.Id == "verdant_planet"))
                {
                    type = t;
                }
            }

            var vocab = new RunPlanVocabulary(new[] { "ruins", "forest" }, new[] { "affix_pack" }, new[] { "chain_a" });
            return RunPlanner.Generate(Planet, seed, type, CreatureStage.Adult, wild, vocab, null,
                DungeonPlanner.SpawnBorderClearanceRad(Planet));
        }

        // First seed whose dungeon has a lock, so the test does not depend on one seed's layout.
        private static void LockedDungeon(out RunPlan plan, out DungeonLayout dungeon)
        {
            for (ulong seed = 1; seed < 20; seed++)
            {
                plan = Plan(seed);
                dungeon = DungeonPlanner.Generate(Planet, plan, SettlementPlanner.Generate(Planet, plan, null), null);
                if (dungeon.HasLock)
                {
                    return;
                }
            }

            plan = null;
            dungeon = null;
            Assert.Fail("no locked dungeon in 19 seeds");
        }

        private DungeonView View(RunPlan plan, DungeonLayout dungeon)
        {
            var root = new GameObject("dungeon-test");
            _roots.Add(root);
            DungeonView view = root.AddComponent<DungeonView>();
            Shader shader = Shader.Find("Cosmimera/ToonOutlined");
            Assert.That(shader, Is.Not.Null);
            view.Configure(Planet, plan, dungeon, _ => 0f, shader);
            return view;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject root in _roots) Object.Destroy(root);
            _roots.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Seal_blocks_the_arena_gate_until_the_lock_opens()
        {
            LockedDungeon(out RunPlan plan, out DungeonLayout dungeon);
            DungeonView view = View(plan, dungeon);
            Assert.That(view.Seals.Count, Is.EqualTo(1));
            yield return new WaitForFixedUpdate();
            Vector3 gate = Planet.ToWorld(dungeon.ArenaGate, 1f);
            // Just inside the plug's rim: the collider must be as wide as the rule layer's plug.
            float3 side = PlanetFrame.FromUp(dungeon.ArenaGate).East;
            Vector3 rim = Planet.ToWorld(math.normalize(dungeon.ArenaGate + side * ((DungeonPlanner.ArenaGateOuterM - 0.5f) / Radius)), 1f);
            Assert.That(Physics.OverlapSphere(gate, 0.2f).Length, Is.GreaterThan(0), "gate centre");
            Assert.That(Physics.OverlapSphere(rim, 0.2f).Length, Is.GreaterThan(0), "plug rim");

            view.OpenLocks();
            yield return null;
            yield return new WaitForFixedUpdate();
            Assert.That(view.Seals.Count, Is.EqualTo(0));
            Assert.That(Physics.OverlapSphere(gate, 0.2f).Length, Is.EqualTo(0), "an open gate must not block");
            view.OpenLocks();
        }

        [Test]
        public void One_marker_per_treasure_room_without_colliders()
        {
            LockedDungeon(out RunPlan plan, out DungeonLayout dungeon);
            DungeonView view = View(plan, dungeon);
            int treasures = 0;
            foreach (RoomRole role in dungeon.Rooms)
            {
                treasures += role == RoomRole.Treasure ? 1 : 0;
            }

            Assert.That(treasures, Is.GreaterThan(0));
            Assert.That(view.Markers.Count, Is.EqualTo(treasures));
            foreach (GameObject marker in view.Markers)
            {
                Assert.That(marker.GetComponentInChildren<Collider>(), Is.Null, "no reward yet, so nothing to bump into");
                Assert.That(marker.GetComponent<MeshFilter>().sharedMesh, Is.Not.Null);
                // The chest's base sits on the sampled ground (0 here), not half a marker above it.
                Assert.That(Planet.Altitude(marker.transform.position), Is.EqualTo(0f).Within(1e-2f));
            }
        }

        [Test]
        public void Seal_is_drawn_as_spires_inside_its_collider()
        {
            LockedDungeon(out RunPlan plan, out DungeonLayout dungeon);
            DungeonView view = View(plan, dungeon);
            MeshFilter spires = view.Seals[0].transform.Find("Spires").GetComponent<MeshFilter>();
            Assert.That(spires.GetComponent<MeshRenderer>().sharedMaterial, Is.Not.Null);
            float3 axis = dungeon.ArenaGate;
            float reach = 0f, top = float.MinValue;
            foreach (Vector3 v in spires.sharedMesh.vertices)
            {
                float3 world = spires.transform.TransformPoint(v);
                float3 fromCentre = world - Planet.Center;
                reach = math.max(reach, math.length(fromCentre - axis * math.dot(fromCentre, axis)));
                top = math.max(top, Planet.Altitude(world));
            }

            Assert.That(reach, Is.InRange(DungeonPlanner.ArenaGateOuterM * 0.8f, DungeonPlanner.ArenaGateOuterM * 1.1f));
            Assert.That(top, Is.EqualTo(DungeonPlanner.WallHeightM).Within(0.5f), "as tall as the walls beside it");
        }

        [Test]
        public void Preconditions_are_checked()
        {
            LockedDungeon(out RunPlan plan, out DungeonLayout dungeon);
            var root = new GameObject("dungeon-test");
            _roots.Add(root);
            DungeonView view = root.AddComponent<DungeonView>();
            Shader shader = Shader.Find("Cosmimera/ToonOutlined");
            Assert.Throws<ContractException>(() => view.Configure(default, plan, dungeon, _ => 0f, shader));
            Assert.Throws<ContractException>(() => view.Configure(Planet, null, dungeon, _ => 0f, shader));
            Assert.Throws<ContractException>(() => view.Configure(Planet, plan, null, _ => 0f, shader));
            Assert.Throws<ContractException>(() => view.Configure(Planet, plan, dungeon, null, shader));
            Assert.Throws<ContractException>(() => view.Configure(Planet, plan, dungeon, _ => 0f, null));
            view.Configure(Planet, plan, dungeon, _ => 0f, shader);
            Assert.Throws<ContractException>(() => view.Configure(Planet, plan, dungeon, _ => 0f, shader));
        }
    }
}
