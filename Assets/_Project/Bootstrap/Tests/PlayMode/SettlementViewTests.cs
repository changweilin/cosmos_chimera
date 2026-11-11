using System.Collections;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
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
    public sealed class SettlementViewTests
    {
        private const float Radius = 320f;
        private const float RoadLiftM = 0.12f;
        private const float BridgeDeckM = 0.6f;
        private static readonly PlanetConfig Planet = new PlanetConfig(Radius, float3.zero);
        private readonly List<GameObject> _roots = new List<GameObject>();

        // Rolling hills of ±3 m with ~10 m wavelength: rough enough that a ribbon not draped per sample would bury itself.
        private static float Hills(float3 d) => 3f * math.sin(d.x * 200f) * math.cos(d.z * 170f);

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
            return RunPlanner.Generate(Planet, seed, type, CreatureStage.Adult, wild, vocab, null);
        }

        private SettlementView View(RunLayout layout, System.Func<float3, float> ground)
        {
            var root = new GameObject("settlement-test");
            _roots.Add(root);
            SettlementView view = root.AddComponent<SettlementView>();
            Shader shader = Shader.Find("Cosmimera/ToonOutlined");
            Assert.That(shader, Is.Not.Null);
            view.Configure(Planet, layout, ground, shader);
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
        public IEnumerator A_removed_building_no_longer_collides()
        {
            RunLayout layout = SettlementPlanner.Generate(Planet, Plan(7), null);
            SettlementView view = View(layout, _ => 0f);
            Assert.That(view.Buildings.Count, Is.GreaterThan(0));
            GameObject first = view.Buildings[0];
            Assert.That(first.GetComponentsInChildren<Collider>().Length, Is.GreaterThan(0));
            yield return new WaitForFixedUpdate();
            Vector3 centre = first.GetComponentInChildren<Collider>().bounds.center;
            Assert.That(Physics.OverlapSphere(centre, 0.2f).Length, Is.GreaterThan(0));
            view.RemoveBuilding(0);
            yield return new WaitForFixedUpdate();
            Assert.That(first.activeSelf, Is.False);
            foreach (Collider c in Physics.OverlapSphere(centre, 0.2f))
            {
                Assert.That(c.transform.IsChildOf(first.transform), Is.False, "a destroyed building must not block");
            }

            Assert.That(view.Buildings.Count, Is.EqualTo(layout.Buildings.Count), "indices stay aligned with the layout");
            Assert.Throws<Core.Contracts.ContractException>(() => view.RemoveBuilding(view.Buildings.Count));
        }

        [Test]
        public void Roads_drape_over_the_ground_and_face_the_sky()
        {
            RunLayout layout = SettlementPlanner.Generate(Planet, Plan(7), null);
            SettlementView view = View(layout, Hills);
            Assert.That(view.Roads.Count, Is.EqualTo(layout.Roads.Count));
            foreach (GameObject road in view.Roads)
            {
                Mesh mesh = road.GetComponent<MeshFilter>().sharedMesh;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Assert.That(vertices.Length, Is.GreaterThanOrEqualTo(10));
                for (int i = 0; i < vertices.Length; i++)
                {
                    float3 world = (float3)road.transform.TransformPoint(vertices[i]);
                    float3 dir = Planet.ToDirection(world);
                    Assert.That(Planet.Altitude(world) - Hills(dir), Is.EqualTo(RoadLiftM).Within(2e-3f), road.name + " vertex " + i);
                    Assert.That(math.dot((float3)normals[i], dir), Is.GreaterThan(0f), road.name + " normal " + i);
                }
            }
        }

        [Test]
        public void Bridge_decks_stay_above_the_sea()
        {
            RunPlan plan = Plan(11);
            System.Func<float3, bool> isLand = d => d.x > 0f;
            RunLayout layout = SettlementPlanner.Generate(Planet, plan, isLand);
            bool anyBridge = false;
            foreach (RunRoad r in layout.Roads) foreach (bool b in r.Bridge) anyBridge |= b;
            Assume.That(anyBridge, Is.True, "fixture needs a road over the sea half");

            // Sea floor 4 m below sea level on the sea half.
            SettlementView view = View(layout, d => d.x > 0f ? 1f : -4f);
            foreach (GameObject road in view.Roads)
            {
                foreach (Vector3 v in road.GetComponent<MeshFilter>().sharedMesh.vertices)
                {
                    float alt = Planet.Altitude((float3)road.transform.TransformPoint(v));
                    Assert.That(alt, Is.GreaterThanOrEqualTo(-4f + RoadLiftM - 2e-3f));
                    float3 dir = Planet.ToDirection((float3)road.transform.TransformPoint(v));
                    // Columns more than a road width into the sea are well inside a bridge row.
                    if (dir.x < -6f / Radius)
                    {
                        Assert.That(alt, Is.GreaterThanOrEqualTo(BridgeDeckM - 2e-3f), road.name);
                    }
                }
            }
        }

        [Test]
        public void Buildings_root_below_their_lowest_corner()
        {
            RunLayout layout = SettlementPlanner.Generate(Planet, Plan(3), null);
            SettlementView view = View(layout, Hills);
            Assert.That(view.Buildings.Count, Is.EqualTo(layout.Buildings.Count));
            Assert.That(view.Buildings.Count, Is.GreaterThan(0));
            for (int i = 0; i < layout.Buildings.Count; i++)
            {
                RunBuilding b = layout.Buildings[i];
                Transform t = view.Buildings[i].transform;
                float3 across = math.normalize(math.cross(b.Direction, b.Facing));
                float lowest = Hills(b.Direction);
                for (int k = 0; k < 4; k++)
                {
                    float3 corner = b.Facing * ((k & 1) == 0 ? 0.5f : -0.5f) * b.DepthM +
                                    across * ((k & 2) == 0 ? 0.5f : -0.5f) * b.WidthM;
                    lowest = math.min(lowest, Hills(math.normalize(b.Direction + corner / Radius)));
                }

                Assert.That(Planet.Altitude((float3)t.position), Is.LessThan(lowest), $"building {i}");
                Assert.That(math.dot((float3)t.up, b.Direction), Is.GreaterThan(0.999f), $"building {i} upright");
                Assert.That(math.dot((float3)t.forward, b.Facing), Is.GreaterThan(0.999f), $"building {i} faces its road");
                Assert.That(t.GetComponentsInChildren<Collider>(), Is.Not.Empty, $"building {i} blocks the character");
            }
        }
    }
}
