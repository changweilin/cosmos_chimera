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
    // REQ: characters.md#路人-npc.
    public sealed class PopulaceViewTests
    {
        private const float Radius = 640f;
        private static readonly PlanetConfig Planet = new PlanetConfig(Radius, float3.zero);
        private readonly List<GameObject> _roots = new List<GameObject>();

        private static float Hills(float3 d) => 3f * math.sin(d.x * 400f) * math.cos(d.z * 340f);

        private static (RunPlan plan, RunLayout layout, RunPopulace people) Make(ulong seed)
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

            var vocab = new RunPlanVocabulary(new[] { "ruins", "forest", "coast" }, new[] { "affix_pack" }, new[] { "chain_a" });
            RunPlan plan = RunPlanner.Generate(Planet, seed, type, CreatureStage.Adult, wild, vocab, null);
            RunLayout layout = SettlementPlanner.Generate(Planet, plan, null);
            return (plan, layout, PopulacePlanner.Generate(Planet, plan, layout, default, null));
        }

        private PopulaceView View(RunPopulace people, RunLayout layout, RunPlan plan, bool scenery, System.Func<float3> focus = null)
        {
            var root = new GameObject("populace-test");
            _roots.Add(root);
            PopulaceView view = root.AddComponent<PopulaceView>();
            Shader shader = Shader.Find("Cosmimera/ToonOutlined");
            Assert.That(shader, Is.Not.Null);
            Shader props = scenery ? Shader.Find("Cosmimera/ToonScenery") : null;
            Assert.That(!scenery || props != null, Is.True);
            view.Configure(Planet, people, layout, Hills, shader, props, "verdant_planet", 7, plan, focus);
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
        public IEnumerator Every_resident_stands_upright_on_the_ground_facing_its_way_with_a_body_collider()
        {
            var (plan, layout, people) = Make(7);
            Assert.That(people.Residents.Count, Is.GreaterThan(0));
            PopulaceView view = View(people, layout, plan, true);
            yield return null;
            Assert.That(view.Residents.Count, Is.EqualTo(people.Residents.Count));
            for (int i = 0; i < people.Residents.Count; i++)
            {
                RunResident r = people.Residents[i];
                Transform t = view.Residents[i].transform;
                Assert.That(Vector3.Dot(t.up, (Vector3)r.Direction), Is.GreaterThan(0.999f));
                Assert.That(Vector3.Dot(t.forward, (Vector3)r.Facing), Is.GreaterThan(0.999f));
                float altitude = t.position.magnitude - Radius;
                Assert.That(altitude, Is.EqualTo(Hills(r.Direction)).Within(0.1f));
                Assert.That(t.GetComponent<SkinnedMeshRenderer>().sharedMaterials.Length, Is.EqualTo(NpcMeshes.SubMeshCount));
                var capsule = t.GetComponent<CapsuleCollider>();
                Assert.That(capsule.radius, Is.EqualTo(PopulacePlanner.BodyRadiusM));
                Assert.That(capsule.height, Is.InRange(0.8f, 2.7f));
            }
        }

        [UnityTest]
        public IEnumerator Settlement_motion_reaches_the_visible_rig()
        {
            var (plan, layout, people) = Make(7);
            PopulaceView view = View(people, layout, plan, true);
            for (int tick = 1; tick <= 160; tick++)
            {
                people.Step(tick * 50, 50);
                if (tick % 4 != 0) continue;
                yield return null;
                for (int i = 0; i < view.Residents.Count; i++)
                {
                    if (math.distance(people.CurrentDirection(i), people.Residents[i].Direction) * Radius < 0.1f) continue;
                    Transform root = view.Residents[i].transform;
                    Transform[] bones = root.GetComponent<SkinnedMeshRenderer>().bones;
                    if (Quaternion.Angle(bones[1].localRotation, Quaternion.identity) < 0.2f) continue;
                    Assert.That(Vector3.Dot(root.up, (Vector3)people.CurrentDirection(i)), Is.GreaterThan(0.99999f));
                    Assert.That(root.GetComponent<CapsuleCollider>().enabled, Is.True);
                    yield break;
                }
            }

            Assert.Fail("Expected a moving resident to articulate its visible leg within eight simulation seconds.");
        }

        [UnityTest]
        public IEnumerator Figures_far_from_the_focus_are_not_drawn_but_keep_their_colliders()
        {
            var (plan, layout, people) = Make(7);
            float3 near = Planet.ToWorld(people.Residents[0].Direction, 0f);
            PopulaceView view = View(people, layout, plan, false, () => near);
            yield return null;
            yield return null;
            int drawn = 0, hidden = 0;
            for (int i = 0; i < view.Residents.Count; i++)
            {
                GameObject go = view.Residents[i];
                bool close = math.distance((float3)go.transform.position, near) < PopulaceView.DrawDistanceM;
                Assert.That(go.GetComponent<SkinnedMeshRenderer>().enabled, Is.EqualTo(close));
                Assert.That(go.GetComponent<CapsuleCollider>().enabled, Is.True);
                drawn += close ? 1 : 0;
                hidden += close ? 0 : 1;
            }

            Assert.That(drawn, Is.GreaterThan(0));
            Assert.That(hidden, Is.GreaterThan(0), "other settlements are farther than the draw distance");
        }

        [UnityTest]
        public IEnumerator Capture_resident_lineups_when_requested()
        {
            if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") != "1")
            {
                Assert.Ignore("visual proof only: set COSMIMERA_CAPTURE=1 and run without -nographics");
            }

            var stage = new GameObject("npc-lineup");
            _roots.Add(stage);
            Shader scenery = Shader.Find("Cosmimera/ToonScenery");
            GameObject groundPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundPlane.transform.SetParent(stage.transform, false);
            groundPlane.transform.localPosition = new Vector3(0f, 0f, 200f);
            groundPlane.transform.localScale = Vector3.one * 100f;
            groundPlane.GetComponent<Renderer>().sharedMaterial =
                PlanetLook.SceneryMaterial(scenery, new Color(0.43f, 0.69f, 0.29f), Color.white, 0f, 0f, 0f, 0f);
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.transform.SetParent(stage.transform, false);
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.transform.rotation = Quaternion.Euler(38f, 28f, 0f);
            RenderSettings.ambientLight = new Color(0.55f, 0.78f, 0.94f) * 0.6f;
            Material[] materials =
            {
                PlanetLook.SceneryMaterial(scenery, Color.white, Color.white, 0f, 0f, 0f, 0f),
                PlanetLook.SceneryMaterial(scenery, Color.white, Color.white, 0f, 0f, 0f, 1.2f),
            };

            ArchEra[] eras =
            {
                ArchEra.Primal, ArchEra.Agrarian, ArchEra.Mercantile, ArchEra.Industrial, ArchEra.Steampunk, ArchEra.Future,
                ArchEra.Wasteland,
            };
            // One row per shot; each shows one axis spreading while the others hold still.
            var rows = new List<(string name, List<NpcSpec> specs)>();
            for (int half = 0; half < 2; half++)
            {
                var races = new List<NpcSpec>();
                for (int i = half * 8; i < math.min(15, half * 8 + 8); i++)
                {
                    races.Add(new NpcSpec((NpcRace)i, NpcRole.None, (NpcSex)(i & 1), 0.4f, 0.85f, 0f, 0f, ArchRegion.Verdant,
                        ArchEra.Agrarian, ArchRegion.Verdant, ArchEra.Agrarian, 0f, (ulong)(100 + i)));
                }

                rows.Add(("races-" + (half + 1), races));
            }

            var textiles = new List<NpcSpec>();
            for (int i = 0; i < 6; i++)
            {
                textiles.Add(new NpcSpec(NpcRace.Elf, NpcRole.None, NpcSex.Male, 0.42f, 0.8f, 0f, 0f,
                    ArchRegion.Crystal, ArchEra.Agrarian, ArchRegion.Crystal, ArchEra.Agrarian, 0f, 47, i / 5f));
            }

            rows.Add(("textiles", textiles));
            var byEra = new List<NpcSpec>();
            foreach (ArchEra era in eras)
            {
                byEra.Add(new NpcSpec(NpcRace.Human, NpcRole.None, (NpcSex)((int)era & 1), 0.4f, 0.85f, 0f, 0f, ArchRegion.Verdant,
                    era, ArchRegion.Verdant, era, 0f, (ulong)(200 + (int)era)));
            }

            rows.Add(("eras", byEra));
            var byRegion = new List<NpcSpec>();
            for (int i = 0; i < 6; i++)
            {
                byRegion.Add(new NpcSpec(NpcRace.Human, NpcRole.None, (NpcSex)(i & 1), 0.4f, 0.85f, 0f, 0f, (ArchRegion)i,
                    ArchEra.Mercantile, (ArchRegion)i, ArchEra.Mercantile, 0f, (ulong)(300 + i)));
            }

            rows.Add(("regions", byRegion));
            for (int half = 0; half < 2; half++)
            {
                var roles = new List<NpcSpec>();
                for (int i = 1 + half * 7; i < math.min(14, 1 + half * 7 + 7); i++)
                {
                    roles.Add(new NpcSpec(half == 0 ? NpcRace.Human : NpcRace.Elf, (NpcRole)i, (NpcSex)(i & 1), 0.4f, 0.85f, 0f, 0f,
                        ArchRegion.Storm, ArchEra.Mercantile, ArchRegion.Storm, ArchEra.Mercantile, 0f, (ulong)(400 + i)));
                }

                rows.Add(("roles-" + (half + 1), roles));
            }

            var ages = new List<NpcSpec>();
            float[] ageSteps = { 0.06f, 0.14f, 0.22f, 0.4f, 0.6f, 0.8f, 0.95f, 0.95f };
            float[] healthSteps = { 0.9f, 0.9f, 0.9f, 0.9f, 0.7f, 0.5f, 0.3f, 0.1f };
            for (int i = 0; i < ageSteps.Length; i++)
            {
                ages.Add(new NpcSpec(NpcRace.Human, NpcRole.None, (NpcSex)(i & 1), ageSteps[i], healthSteps[i], 0f, 0f, ArchRegion.Ice,
                    ArchEra.Agrarian, ArchRegion.Ice, ArchEra.Agrarian, 0f, (ulong)(500 + i)));
            }

            rows.Add(("age-health", ages));

            // One village as the planner draws it: same race mix, age and health spread, and style borrowing.
            var (_, layout, people) = Make(7);
            ArchPlanetStyle style = ArchitectureMeshes.PlanetStyleFor("verdant_planet", 7);
            int lineupCount = rows.Count;
            foreach (SettlementPopulace home in people.Settlements)
            {
                if (home.ResidentCount < 8 || rows.Count >= lineupCount + 3)
                {
                    continue;
                }

                RunSettlement s = layout.Settlements[home.Settlement];
                ArchMix mix = ArchitectureMeshes.MixFor(style, s.Kind, "forest", 7, s.Index);
                var crowd = new List<NpcSpec>();
                for (int i = home.FirstResident; i < home.FirstResident + math.min(10, home.ResidentCount); i++)
                {
                    crowd.Add(NpcMeshes.SpecFor(people.Residents[i], style.Region, ArchitectureMeshes.SettlementEra(style, s.Kind), mix));
                }

                rows.Add(("crowd-" + s.Kind, crowd));
            }

            var walk = new List<NpcSpec>();
            for (int i = 0; i < 5; i++) walk.Add(new NpcSpec(NpcRace.Human, NpcRole.Guard, NpcSex.Male, 0.4f,
                0.92f, 0f, 0f, ArchRegion.Storm, ArchEra.Mercantile, ArchRegion.Storm, ArchEra.Mercantile, 0f, 43));
            rows.Add(("stride", walk));

            const float RowGap = 30f;
            const float Spacing = 1.2f;
            for (int r = 0; r < rows.Count; r++)
            {
                List<NpcSpec> specs = rows[r].specs;
                for (int i = 0; i < specs.Count; i++)
                {
                    var go = new GameObject(rows[r].name + "_" + i);
                    go.transform.SetParent(stage.transform, false);
                    go.transform.localPosition = new Vector3((i - (specs.Count - 1) * 0.5f) * Spacing, 0f, r * RowGap);
                    go.transform.localRotation = Quaternion.Euler(0f, 180f + (i - (specs.Count - 1) * 0.5f) * 5f - 20f, 0f);
                    if (rows[r].name == "stride")
                    {
                        go.transform.localRotation = Quaternion.Euler(0f, 117f, 0f);
                        Mesh mesh = NpcMeshes.Build(specs[i], go.transform, out ResidentStride gait);
                        var skinned = go.AddComponent<SkinnedMeshRenderer>();
                        skinned.sharedMesh = mesh;
                        skinned.bones = gait.Bones;
                        skinned.rootBone = go.transform;
                        skinned.sharedMaterials = materials;
                        gait.Pose((i - 1) * math.PI * 0.5f, 1f);
                        continue;
                    }
                    go.AddComponent<MeshFilter>().sharedMesh = NpcMeshes.Build(specs[i]);
                    go.AddComponent<MeshRenderer>().sharedMaterials = materials;
                }
            }

            var camera = new GameObject("camera").AddComponent<Camera>();
            camera.transform.SetParent(stage.transform, false);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.78f, 0.94f);
            camera.fieldOfView = 30f;
            camera.farClipPlane = 25f;
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
            yield return null;
            for (int r = 0; r < rows.Count; r++)
            {
                float span = rows[r].specs.Count * Spacing + 1.2f;
                float back = span * 1.06f;
                camera.transform.position = new Vector3(0f, 1.7f, r * RowGap - back);
                camera.transform.LookAt(new Vector3(0f, 1.05f, r * RowGap));
                ScatterFieldTests.Shoot(camera, System.IO.Path.Combine(dir, "npc-" + rows[r].name + ".png"));
            }
        }
    }
}
