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

        private SettlementView View(RunLayout layout, System.Func<float3, float> ground, bool scenery = false,
            string planetTypeId = null, ulong seed = 0)
        {
            var root = new GameObject("settlement-test");
            _roots.Add(root);
            SettlementView view = root.AddComponent<SettlementView>();
            Shader shader = Shader.Find("Cosmimera/ToonOutlined");
            Assert.That(shader, Is.Not.Null);
            Shader props = scenery ? Shader.Find("Cosmimera/ToonScenery") : null;
            Assert.That(!scenery || props != null, Is.True);
            view.Configure(Planet, layout, ground, shader, props, planetTypeId, seed);
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
        public void Bridges_get_masonry_only_where_the_road_crosses_the_sea()
        {
            RunPlan plan = Plan(11);
            RunLayout wet = SettlementPlanner.Generate(Planet, plan, d => d.x > 0f);
            bool anyBridge = false;
            foreach (RunRoad r in wet.Roads) foreach (bool b in r.Bridge) anyBridge |= b;
            Assume.That(anyBridge, Is.True, "fixture needs a road over the sea half");

            SettlementView view = View(wet, d => d.x > 0f ? 1f : -4f, scenery: true);
            int bridges = 0;
            for (int i = 0; i < wet.Roads.Count; i++)
            {
                Transform bridge = view.Roads[i].transform.Find("Bridge");
                int run = 0, longest = 0;
                foreach (bool b in wet.Roads[i].Bridge)
                {
                    run = b ? run + 1 : 0;
                    longest = math.max(longest, run);
                }

                if (longest == 0)
                {
                    Assert.That(bridge, Is.Null, "a road on land has no bridge");
                    continue;
                }

                if (bridge == null) continue; // a single wet vertex is too short to span
                bridges++;
                Assert.That(bridge.GetComponentInChildren<Collider>(), Is.Null, "the terrain stays the only walking surface");
                Mesh mesh = bridge.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(bridge.GetComponent<MeshRenderer>().sharedMaterials.Length, Is.EqualTo(mesh.subMeshCount));
                float highest = float.MinValue, lowest = float.MaxValue;
                foreach (Vector3 v in mesh.vertices)
                {
                    float alt = Planet.Altitude((float3)bridge.TransformPoint(v));
                    highest = math.max(highest, alt);
                    lowest = math.min(lowest, alt);
                }

                Assert.That(highest, Is.GreaterThan(BridgeDeckM + 0.5f), "parapets stand above the deck");
                Assert.That(lowest, Is.LessThan(0f), "arches and piers reach into the water");
                Assert.That(lowest, Is.GreaterThan(-4f - 1.5f), "piers stop just under the sea floor");
            }

            Assert.That(bridges, Is.GreaterThan(0));
        }

        [Test]
        public void Buildings_are_one_styled_mesh_with_a_footprint_collider()
        {
            RunLayout layout = SettlementPlanner.Generate(Planet, Plan(3), null);
            SettlementView view = View(layout, Hills, scenery: true, planetTypeId: "twilight_planet", seed: 3);
            Shader props = Shader.Find("Cosmimera/ToonScenery");
            for (int i = 0; i < layout.Buildings.Count; i++)
            {
                RunBuilding b = layout.Buildings[i];
                GameObject go = view.Buildings[i];
                Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
                Material[] materials = go.GetComponent<MeshRenderer>().sharedMaterials;
                Assert.That(mesh.subMeshCount, Is.EqualTo(ArchitectureMeshes.SubMeshCount), $"building {i}");
                Assert.That(materials.Length, Is.EqualTo(ArchitectureMeshes.SubMeshCount), $"building {i}");
                foreach (Material m in materials) Assert.That(m.shader, Is.SameAs(props));
                Assert.That(materials[ArchitectureMeshes.GlowSubMesh].GetFloat("_Glow"), Is.GreaterThan(0f));
                Collider[] colliders = go.GetComponentsInChildren<Collider>();
                Assert.That(colliders.Length, Is.EqualTo(1), $"building {i}: one collider");
                var box = (BoxCollider)colliders[0];
                Assert.That(box.size.x, Is.LessThanOrEqualTo(b.WidthM + 1e-4f));
                Assert.That(box.size.z, Is.LessThanOrEqualTo(b.DepthM + 1e-4f));
                Assert.That(box.center.y, Is.EqualTo(box.size.y * 0.5f).Within(1e-4f), "the box stands on the sunk base");
            }

            // Same inputs, same village; another seed or planet type, another one.
            SettlementView again = View(layout, Hills, scenery: true, planetTypeId: "twilight_planet", seed: 3);
            SettlementView reseeded = View(layout, Hills, scenery: true, planetTypeId: "twilight_planet", seed: 4);
            SettlementView elsewhere = View(layout, Hills, scenery: true, planetTypeId: "ice_crystal_planet", seed: 3);
            bool seedDiffers = false, regionDiffers = false;
            for (int i = 0; i < layout.Buildings.Count; i++)
            {
                Vector3[] v = view.Buildings[i].GetComponent<MeshFilter>().sharedMesh.vertices;
                Assert.That(again.Buildings[i].GetComponent<MeshFilter>().sharedMesh.vertices, Is.EqualTo(v), $"building {i}");
                seedDiffers |= !SameVertices(reseeded.Buildings[i].GetComponent<MeshFilter>().sharedMesh.vertices, v);
                regionDiffers |= !SameVertices(elsewhere.Buildings[i].GetComponent<MeshFilter>().sharedMesh.vertices, v);
            }

            Assert.That(seedDiffers, Is.True);
            Assert.That(regionDiffers, Is.True);
        }

        private static bool SameVertices(Vector3[] a, Vector3[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }

            return true;
        }

        [UnityTest]
        public IEnumerator Capture_architecture_lineup_when_requested()
        {
            if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") != "1")
            {
                Assert.Ignore("visual proof only: set COSMIMERA_CAPTURE=1 and run without -nographics");
            }

            var stage = new GameObject("architecture-lineup");
            _roots.Add(stage);
            Shader scenery = Shader.Find("Cosmimera/ToonScenery");
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(stage.transform, false);
            ground.transform.localPosition = new Vector3(60f, 0.55f, 250f);
            ground.transform.localScale = new Vector3(50f, 1f, 90f);
            ground.GetComponent<Renderer>().sharedMaterial =
                PlanetLook.SceneryMaterial(scenery, new Color(0.43f, 0.69f, 0.29f), Color.white, 0f, 0f, 0f, 0f);
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.transform.SetParent(stage.transform, false);
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            RenderSettings.ambientLight = new Color(0.55f, 0.78f, 0.94f) * 0.6f;

            (BuildingKind kind, ArchEra era, float w, float d, int storeys)[] columns =
            {
                (BuildingKind.Tent, ArchEra.Primal, 3f, 3f, 1), (BuildingKind.House, ArchEra.Primal, 5f, 4f, 1),
                (BuildingKind.House, ArchEra.Agrarian, 5f, 4f, 1), (BuildingKind.Hall, ArchEra.Agrarian, 7f, 6f, 2),
                (BuildingKind.House, ArchEra.Mercantile, 5f, 5f, 2), (BuildingKind.Tower, ArchEra.Mercantile, 3f, 3f, 4),
                (BuildingKind.House, ArchEra.Industrial, 6f, 5f, 2), (BuildingKind.Tower, ArchEra.Industrial, 3f, 3f, 5),
                (BuildingKind.House, ArchEra.Ancient, 5f, 4f, 1), (BuildingKind.Pillar, ArchEra.Ancient, 1.2f, 1.2f, 3),
                (BuildingKind.House, ArchEra.Steampunk, 6f, 5f, 2), (BuildingKind.Tower, ArchEra.Steampunk, 3f, 3f, 5),
                (BuildingKind.House, ArchEra.Future, 6f, 5f, 2), (BuildingKind.Tower, ArchEra.Future, 3f, 3f, 5),
                (BuildingKind.House, ArchEra.Wasteland, 6f, 5f, 2), (BuildingKind.Tower, ArchEra.Wasteland, 3f, 3f, 4),
            };

            // One street per row: the same (tradition, era, mix) ten times over, so what is on show is only the
            // per-building draw (shape, age, damage) and which secondary traits each house borrowed.
            (string name, ArchRegion region, ArchEra era, ArchMix mix)[] streets =
            {
                ("verdant-mercantile", ArchRegion.Verdant, ArchEra.Mercantile, new ArchMix(ArchRegion.Twilight, ArchEra.Agrarian, 0.35f, 0.4f, 0.1f)),
                ("ice-steampunk", ArchRegion.Ice, ArchEra.Steampunk, new ArchMix(ArchRegion.Storm, ArchEra.Industrial, 0.35f, 0.4f, 0.1f)),
                ("crystal-future", ArchRegion.Crystal, ArchEra.Future, new ArchMix(ArchRegion.Lava, ArchEra.Steampunk, 0.3f, 0.1f, 0.03f)),
                ("lava-wasteland", ArchRegion.Lava, ArchEra.Wasteland, new ArchMix(ArchRegion.Verdant, ArchEra.Industrial, 0.35f, 0.8f, 0.5f)),
                ("twilight-agrarian-old", ArchRegion.Twilight, ArchEra.Agrarian, new ArchMix(ArchRegion.Verdant, ArchEra.Mercantile, 0.4f, 0.8f, 0.45f)),
            };
            var regions = (ArchRegion[])System.Enum.GetValues(typeof(ArchRegion));
            const float rowGap = 50f, colGap = 9.5f;
            for (int r = 0; r < regions.Length; r++)
            {
                for (int c = 0; c < columns.Length; c++)
                {
                    var col = columns[c];
                    var spec = new ArchSpec(col.kind, regions[r], col.era, col.w, col.d, col.storeys, 0.55f, (ulong)(r * 31 + c));
                    ArchPalette p = ArchitectureMeshes.PaletteFor(regions[r], col.era, col.kind);
                    var go = new GameObject(regions[r] + "_" + col.kind + "_" + col.era);
                    go.transform.SetParent(stage.transform, false);
                    go.transform.localPosition = new Vector3(c * colGap, 0f, r * rowGap);
                    go.transform.localRotation = Quaternion.Euler(0f, 180f + 28f, 0f);
                    go.AddComponent<MeshFilter>().sharedMesh = ArchitectureMeshes.Build(spec);
                    go.AddComponent<MeshRenderer>().sharedMaterials = new[]
                    {
                        PlanetLook.SceneryMaterial(scenery, p.Wall, p.Weathering, p.WeatheringStrength, 0f, 0f, 0f),
                        PlanetLook.SceneryMaterial(scenery, p.Roof, p.Weathering, p.WeatheringStrength, 0f, 0f, 0f),
                        PlanetLook.SceneryMaterial(scenery, p.Trim, p.Trim, 0f, 0f, 0f, 0f),
                        PlanetLook.SceneryMaterial(scenery, p.Glow, p.Glow, 0f, 0f, 0f, 0.9f),
                    };
                }
            }

            for (int r = 0; r < streets.Length; r++)
            {
                var street = streets[r];
                ArchPalette p = ArchitectureMeshes.PaletteFor(street.region, street.era, BuildingKind.House, street.mix);
                var materials = new[]
                {
                    PlanetLook.SceneryMaterial(scenery, p.Wall, p.Weathering, p.WeatheringStrength, 0f, 0f, 0f),
                    PlanetLook.SceneryMaterial(scenery, p.Roof, p.Weathering, p.WeatheringStrength, 0f, 0f, 0f),
                    PlanetLook.SceneryMaterial(scenery, p.Trim, p.Trim, 0f, 0f, 0f, 0f),
                    PlanetLook.SceneryMaterial(scenery, p.Glow, p.Glow, 0f, 0f, 0f, 0.9f),
                };
                for (int c = 0; c < 10; c++)
                {
                    BuildingKind kind = c == 4 ? BuildingKind.Hall : c == 8 ? BuildingKind.Tower : BuildingKind.House;
                    var spec = kind == BuildingKind.Hall
                        ? new ArchSpec(kind, street.region, street.era, 7f, 6f, 2, 0.55f, (ulong)(r * 101 + c), street.mix)
                        : kind == BuildingKind.Tower
                            ? new ArchSpec(kind, street.region, street.era, 3f, 3f, 4, 0.55f, (ulong)(r * 101 + c), street.mix)
                            : new ArchSpec(kind, street.region, street.era, 5f + c % 2, 5f, 1 + c % 3 / 2 + c % 2, 0.55f, (ulong)(r * 101 + c), street.mix);
                    var go = new GameObject("street_" + street.name + "_" + c);
                    go.transform.SetParent(stage.transform, false);
                    go.transform.localPosition = new Vector3(c * colGap, 0f, (regions.Length + r) * rowGap);
                    go.transform.localRotation = Quaternion.Euler(0f, 180f + 28f, 0f);
                    go.AddComponent<MeshFilter>().sharedMesh = ArchitectureMeshes.Build(spec);
                    go.AddComponent<MeshRenderer>().sharedMaterials = materials;
                }
            }

            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.transform.SetParent(stage.transform, false);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.78f, 0.94f);
            camera.fieldOfView = 36f;
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
            yield return null;
            for (int r = 0; r < regions.Length; r++)
            {
                for (int half = 0; half < 3; half++)
                {
                    float centre = (half == 0 ? 2f : half == 1 ? 7f : 12.5f) * colGap;
                    camera.transform.position = new Vector3(centre + 3f, 9f, r * rowGap - 40f);
                    camera.transform.LookAt(new Vector3(centre, 4.2f, r * rowGap));
                    ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "arch-" + regions[r] + "-" + "abc"[half] + ".png"));
                }
            }

            for (int r = 0; r < streets.Length; r++)
            {
                for (int half = 0; half < 2; half++)
                {
                    float centre = (half == 0 ? 2f : 7f) * colGap, z = (regions.Length + r) * rowGap;
                    camera.transform.position = new Vector3(centre + 3f, 9f, z - 40f);
                    camera.transform.LookAt(new Vector3(centre, 4.2f, z));
                    ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "arch-street-" + streets[r].name + "-" + "ab"[half] + ".png"));
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
