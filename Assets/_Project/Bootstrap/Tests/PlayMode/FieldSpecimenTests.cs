using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Planet;
using Cosmimera.Combat;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class FieldSpecimenTests
    {
        private static List<FieldSpecimen> Samples(ulong seed, string type = "verdant_planet", bool water = false)
        {
            var (plan, layout, _) = PopulaceViewTests.Make(seed);
            return FieldSpecimen.Generate(new PlanetConfig(640f, float3.zero), layout, plan, type,
                seed, d => water ? -2f : 4f, d => true, d => water);
        }

        [UnityTest]
        public IEnumerator Same_seed_rebuilds_the_same_bodies_and_motion()
        {
            var first = Samples(7);
            var replay = Samples(7);
            Assert.That(first.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(replay.Count, Is.EqualTo(first.Count));
            var root = new GameObject("field-replay");
            try
            {
                var view = root.AddComponent<FieldSpecimenView>();
                view.Configure(first, Shader.Find("Cosmimera/ToonScenery"));
                for (int i = 0; i < first.Count; i++)
                {
                    first[i].Step(9000);
                    replay[i].Step(2400);
                    replay[i].Step(9000);
                    Assert.That(first[i].Position, Is.EqualTo(replay[i].Position));
                    Assert.That(math.dot(first[i].Direction, first[i].Facing), Is.EqualTo(0).Within(0.001));
                    Assert.That(first[i].Era, Is.EqualTo(first[0].Era));
                    Assert.That(first[i].Style, Is.EqualTo(replay[i].Style));
                    Assert.That(view.Root(i).GetComponent<BoxCollider>() != null, Is.EqualTo(first[i].Solid));
                }
                view.Sync(9000);
                Assert.That(view.Root(0).transform.position, Is.EqualTo((Vector3)first[0].Position));
            }
            finally { Object.Destroy(root); }
            yield return null;
        }

        [Test]
        public void Moving_targets_keep_damage_at_their_current_location_and_stay_destroyed()
        {
            var (plan, layout, _) = PopulaceViewTests.Make(7);
            var planet = new PlanetConfig(640f, float3.zero);
            var clearance = new LayoutClearance(planet, layout, 1.5f);
            var scatter = new ScatterPlanner(planet, 7, "verdant_planet", d => 4f, null, clearance.IsClear);
            var env = EnvMaterials.FromJson(File.ReadAllText(Path.Combine(Application.dataPath, "..",
                "Data/_incoming/p3-12-env-materials-draft-01/env-materials/env-materials.json")));
            var table = new DestructibleTable(env);
            var objects = new WorldObjects(planet, scatter, layout, clearance, d => 4f, env, table);
            var field = FieldSpecimen.Generate(planet, layout, plan, "verdant_planet", 7, d => 4f, d => true, d => false);
            objects.AttachField(field);
            int index = field.FindIndex(s => s.Animal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            long id = WorldObjects.FieldIdBase + index;
            Assert.That(objects.TryGet(id, out var candidate), Is.True);
            int slot = table.Register(id, candidate.Composition, candidate.VolumeL, candidate.Center);
            objects.StepField(535);
            Assert.That(table.CenterOf(slot), Is.EqualTo(field[index].Center));
            Assert.That(table.CenterOf(slot), Is.Not.EqualTo(candidate.Center));
            var hits = new List<DestructibleCandidate>();
            objects.Query(field[index].Center, 0, hits);
            Assert.That(hits.Exists(h => h.Id == id), Is.True);
            table.ApplyHit(slot, Element.Fire, 100000, 0, 535);
            Assert.That(table.IsDestroyed(id), Is.True);
            objects.StepField(700);
            hits.Clear();
            objects.Query(field[index].Center, 0, hits);
            Assert.That(hits.Exists(h => h.Id == id), Is.False);
        }

        [UnityTest]
        public IEnumerator Generated_equipment_is_visible_on_the_demo_planet()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            var species = db.All.OfType<CreatureDef>().ToList();
            var type = db.All.OfType<PlanetTypeDef>().First(t => t.Id == "verdant_planet");
            var session = GeologyCaptureTests.OpenPlanet(type, 7, 320, CreatureStage.Juvenile,
                species.FindAll(c => c.LineId.StartsWith("starter_") && c.Stage == CreatureStage.Adult),
                species.FindAll(c => !c.LineId.StartsWith("starter_")));
            try
            {
                var field = session.GetComponentInChildren<FieldSpecimenView>();
                Assert.That(field, Is.Not.Null);
                Assert.That(field.transform.childCount, Is.GreaterThan(0));
                float deadline = Time.realtimeSinceStartup + 60f;
                while (!session.StartedForTests && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(session.StartedForTests, Is.True);
                if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") == "1")
                {
                    Transform target = field.Root(0).transform;
                    float3 dir = math.normalize((float3)(target.position + target.right * 7f + target.forward * 6f));
                    float3 from = session.DirectionForTests(session.PlayerPositionForTests);
                    int hops = 1 + (int)(math.acos(math.clamp(math.dot(from, dir), -1f, 1f)) / math.radians(40f));
                    for (int i = 1; i <= hops; i++)
                    {
                        session.PlaceCharacterForTests(math.normalize(math.lerp(from, dir, (float)i / hops)));
                        yield return new WaitForFixedUpdate();
                        yield return new WaitForFixedUpdate();
                    }
                    for (int i = 0; i < 40; i++) yield return null;
                    deadline = Time.realtimeSinceStartup + 30f;
                    while (!session.TerrainSettledForTests && Time.realtimeSinceStartup < deadline) yield return null;
                    session.SuspendCameraForTests();
                    session.CaptureSpecimenForTests(Path.Combine(Application.dataPath, "../Logs/field-planet.png"), target);
                }
            }
            finally { Object.Destroy(session.gameObject); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Capture_field_workshop()
        {
            if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") != "1")
                Assert.Ignore("Set COSMIMERA_CAPTURE=1 to render the field workshop.");
            var selected = new Dictionary<FieldForm, FieldSpecimen>();
            for (ulong seed = 1; selected.Count < 12 && seed <= 48; seed++)
            {
                bool water = seed % 7 == 0;
                string type = seed % 3 == 0 ? "ice_crystal_planet" : "verdant_planet";
                foreach (var item in Samples(seed, type, water))
                    selected.TryAdd(item.Form, item);
            }
            Assert.That(selected.Count, Is.EqualTo(12));
            var stage = new GameObject("field-workshop");
            stage.transform.position = new Vector3(9000, 0, 0);
            var meshes = new List<Mesh>();
            Shader shader = Shader.Find("Cosmimera/ToonScenery");
            var material = PlanetLook.SceneryMaterial(shader, Color.white, Color.white, 0, 0, 0, 0);
            var turf = PlanetLook.SceneryMaterial(shader, new Color(0.48f,0.60f,0.47f), Color.white, 0, 0, 0, 0);
            try
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.transform.SetParent(stage.transform, false);
                ground.transform.localPosition = new Vector3(0,-0.11f,0);
                ground.transform.localScale = new Vector3(50,0.2f,50);
                ground.GetComponent<Renderer>().sharedMaterial = turf;
                FieldForm[] order = { FieldForm.WindPump, FieldForm.Hoist, FieldForm.SteamCar, FieldForm.Balloon,
                    FieldForm.Sled, FieldForm.MineCart, FieldForm.Wagon, FieldForm.Skiff,
                    FieldForm.Hare, FieldForm.Heron, FieldForm.Beetle, FieldForm.Fish };
                for (int i = 0; i < order.Length; i++)
                {
                    var item = selected[order[i]];
                    var go = new GameObject(item.Form.ToString());
                    go.transform.SetParent(stage.transform, false);
                    go.transform.localPosition = new Vector3((i % 4 - 1.5f) * 3.9f,0,(1 - i / 4) * 5.4f);
                    go.transform.localRotation = Quaternion.Euler(0,125f,0);
                    Mesh mesh = new FieldMeshes(item).Build();
                    meshes.Add(mesh);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = material;
                }
                var light = new GameObject("sun").AddComponent<Light>();
                light.transform.SetParent(stage.transform, false);
                light.type = LightType.Directional;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(43,-32,0);
                var camera = new GameObject("camera").AddComponent<Camera>();
                camera.transform.SetParent(stage.transform, false);
                camera.transform.localPosition = new Vector3(0,9,-12);
                camera.transform.LookAt(stage.transform.position + new Vector3(0,1.15f,0));
                camera.orthographic = true;
                camera.orthographicSize = 5.8f;
                camera.farClipPlane = 35;
                yield return null;
                ScatterFieldTests.Shoot(camera, Path.Combine(Application.dataPath, "../Logs/field-workshop.png"));
            }
            finally
            {
                Object.Destroy(stage);
                foreach (Mesh mesh in meshes) Object.Destroy(mesh);
                Object.Destroy(material);
                Object.Destroy(turf);
            }
        }
    }
}
