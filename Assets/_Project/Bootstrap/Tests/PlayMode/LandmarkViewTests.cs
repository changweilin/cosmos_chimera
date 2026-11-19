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
    // REQ: planet-world.md#奇觀地標.
    public sealed class LandmarkViewTests
    {
        private const float Radius = 640f;
        private static readonly PlanetConfig Planet = new PlanetConfig(Radius, float3.zero);
        private readonly List<GameObject> _roots = new List<GameObject>();

        // ±3 m hills of ~10 m wavelength: rough enough that a base not sunk per footprint would float.
        private static float Hills(float3 d) => 3f * math.sin(d.x * 400f) * math.cos(d.z * 340f);

        private static LandmarkLayout Layout(ulong seed)
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
            RunPlan plan = RunPlanner.Generate(Planet, seed, type, CreatureStage.Adult, wild, vocab, null);
            return LandmarkPlanner.Generate(Planet, plan, SettlementPlanner.Generate(Planet, plan, null), null);
        }

        private LandmarkView View(LandmarkLayout layout, System.Func<float3, float> ground, bool scenery, string planetTypeId, ulong seed)
        {
            var root = new GameObject("landmark-test");
            _roots.Add(root);
            LandmarkView view = root.AddComponent<LandmarkView>();
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
        public IEnumerator Every_landmark_is_drawn_upright_facing_its_entrance_with_a_collider_only_when_solid()
        {
            LandmarkLayout layout = Layout(7);
            // Q-W41: a dwarf-planet fixture (R = 640) plans 2 ground wonders.
            Assert.That(layout.Landmarks.Count, Is.GreaterThanOrEqualTo(2));
            LandmarkView view = View(layout, Hills, true, "verdant_planet", 7);
            yield return null;
            Assert.That(view.Landmarks.Count, Is.EqualTo(layout.Landmarks.Count));
            float elevatorTop = float.NaN;
            for (int i = 0; i < layout.Landmarks.Count; i++)
            {
                RunLandmark l = layout.Landmarks[i];
                Transform t = view.Landmarks[i].transform;
                Assert.That(Vector3.Dot(t.up, (Vector3)l.Direction), Is.GreaterThan(0.9999f));
                Assert.That(Vector3.Dot(t.forward, (Vector3)l.Facing), Is.GreaterThan(0.999f));
                Assert.That(t.GetComponent<MeshRenderer>().sharedMaterials.Length, Is.EqualTo(ArchitectureMeshes.SubMeshCount));
                var capsule = t.GetComponent<CapsuleCollider>();
                Assert.That(capsule != null, Is.EqualTo(l.Solid));
                float altitude = t.position.magnitude - Radius;
                float ground = Hills(l.Direction);
                if (l.Solid)
                {
                    Assert.That(capsule.radius, Is.EqualTo(l.FootprintRadiusM));
                    Assert.That(altitude, Is.LessThanOrEqualTo(ground + 0.01f), "never above its own ground");
                    PlanetFrame frame = PlanetFrame.FromUp(l.Direction);
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k * math.PI / 4f;
                        float3 rim = math.normalize(l.Direction + (frame.East * math.cos(a) + frame.North * math.sin(a)) * (l.FootprintRadiusM / Radius));
                        Assert.That(altitude - LandmarkMeshes.SinkM, Is.LessThan(Hills(rim)), "sunk foot is below the ground at the rim");
                    }

                    if (l.Kind == LandmarkKind.SpaceElevator)
                    {
                        elevatorTop = altitude + l.HeightM;
                    }
                }
                else if (l.Kind == LandmarkKind.SpaceStation)
                {
                    Assert.That(altitude, Is.EqualTo(elevatorTop).Within(0.05f), "the station caps the tether");
                }
                else
                {
                    Assert.That(altitude, Is.EqualTo(ground + l.AltitudeM).Within(0.05f));
                }
            }
        }

        [UnityTest]
        public IEnumerator Capture_landmark_lineup_when_requested()
        {
            if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") != "1")
            {
                Assert.Ignore("visual proof only: set COSMIMERA_CAPTURE=1 and run without -nographics");
            }

            var stage = new GameObject("landmark-lineup");
            _roots.Add(stage);
            Shader scenery = Shader.Find("Cosmimera/ToonScenery");
            GameObject groundPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundPlane.transform.SetParent(stage.transform, false);
            groundPlane.transform.localPosition = new Vector3(0f, 0f, 2000f);
            groundPlane.transform.localScale = Vector3.one * 1000f;
            groundPlane.GetComponent<Renderer>().sharedMaterial =
                PlanetLook.SceneryMaterial(scenery, new Color(0.43f, 0.69f, 0.29f), Color.white, 0f, 0f, 0f, 0f);
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.transform.SetParent(stage.transform, false);
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            RenderSettings.ambientLight = new Color(0.55f, 0.78f, 0.94f) * 0.6f;

            // One row per shot: the same wonder across regions and eras, so a row shows how far one idea spreads.
            (string name, LandmarkKind kind, float footprint, float height, float lift, float spacing)[] rows =
            {
                ("tower-function", LandmarkKind.Tower, 6f, 40f, 0f, 22f),
                ("tower-era", LandmarkKind.Tower, 6f, 44f, 0f, 22f),
                ("tower-region", LandmarkKind.Tower, 6f, 36f, 0f, 22f),
                ("temple", LandmarkKind.Temple, 14f, 15f, 0f, 36f),
                ("sky-island", LandmarkKind.SkyIsland, 18f, 16f, 30f, 48f),
                ("space-elevator", LandmarkKind.SpaceElevator, 9f, 240f, 0f, 30f),
                ("space-station", LandmarkKind.SpaceStation, 0f, 24f, 40f, 70f),
                ("labyrinth", LandmarkKind.Labyrinth, 16f, 11f, 0f, 40f),
                ("mine", LandmarkKind.MineShaft, 7f, 10f, 0f, 20f),
                ("dungeon-gate", LandmarkKind.DungeonGate, 7f, 9f, 0f, 20f),
                ("compute-center", LandmarkKind.ComputeCenter, 11f, 18f, 0f, 30f),
                ("giant-tree", LandmarkKind.GiantTree, 9f, 36f, 0f, 30f),
                ("trench-mouth", LandmarkKind.TrenchMouth, 10f, 5f, 0f, 28f),
                ("crater", LandmarkKind.Crater, 16f, 13f, 0f, 38f),
                ("ice-castle", LandmarkKind.IceCastle, 13f, 23f, 0f, 32f),
            };
            ArchEra[] eras = { ArchEra.Agrarian, ArchEra.Mercantile, ArchEra.Steampunk, ArchEra.Future, ArchEra.Wasteland, ArchEra.Ancient };
            for (int r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                int count = row.kind == LandmarkKind.Tower ? 6 : 4;
                for (int i = 0; i < count; i++)
                {
                    int variant = r == 0 ? i : i % LandmarkPlanner.VariantCount(row.kind);
                    ArchEra era = r == 0 ? ArchEra.Mercantile : r == 2 ? ArchEra.Agrarian : eras[(i * 2 + r) % eras.Length];
                    if (r == 1) era = eras[i];
                    if (row.kind == LandmarkKind.SpaceElevator || row.kind == LandmarkKind.SpaceStation) era = i < 2 ? ArchEra.Future : ArchEra.Ancient;
                    ArchRegion region = r == 2 ? (ArchRegion)i : (ArchRegion)((i + r) % 6);
                    var spec = new LandmarkSpec(row.kind, variant, region, era, r == 1 ? 0.5f : 0.15f + 0.14f * i, row.footprint, row.height,
                        (ulong)(r * 31 + i));
                    var go = new GameObject(row.name + "_" + i);
                    go.transform.SetParent(stage.transform, false);
                    go.transform.localPosition = new Vector3((i - (count - 1) * 0.5f) * row.spacing, row.lift, r * 400f);
                    go.transform.localRotation = Quaternion.Euler(0f, 180f + (i - (count - 1) * 0.5f) * 8f, 0f);
                    go.AddComponent<MeshFilter>().sharedMesh = LandmarkMeshes.Build(spec);
                    ArchPalette p = LandmarkMeshes.PaletteFor(spec);
                    go.AddComponent<MeshRenderer>().sharedMaterials = new[]
                    {
                        PlanetLook.SceneryMaterial(scenery, p.Wall, p.Weathering, p.WeatheringStrength, 0f, 0f, 0f),
                        PlanetLook.SceneryMaterial(scenery, p.Roof, p.Weathering, p.WeatheringStrength, 0f, 0f, 0f),
                        PlanetLook.SceneryMaterial(scenery, p.Trim, p.Trim, 0f, 0f, 0f, 0f),
                        PlanetLook.SceneryMaterial(scenery, p.Glow, p.Glow, 0f, 0f, 0f, 1.4f),
                    };
                }
            }

            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.transform.SetParent(stage.transform, false);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.78f, 0.94f);
            camera.fieldOfView = 40f;
            camera.farClipPlane = 2000f;
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
            yield return null;
            for (int r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                int count = row.kind == LandmarkKind.Tower ? 6 : 4;
                float span = row.spacing * (count - 1) + row.footprint * 2f + 10f;
                // The tether is framed by its anchor: the whole line would shrink the terminal to a dot.
                float height = row.kind == LandmarkKind.SpaceElevator ? 60f : row.height + row.lift;
                float back = Mathf.Max(span * 0.85f, height * 1.9f) + 8f;
                camera.transform.position = new Vector3(back * 0.12f, row.lift * 0.6f + height * 0.45f + back * 0.12f, r * 400f - back);
                camera.transform.LookAt(new Vector3(0f, row.lift * 0.8f + height * 0.42f, r * 400f));
                ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "landmark-" + row.name + ".png"));
            }
        }
    }
}
