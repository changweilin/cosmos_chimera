#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Combat;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class RunSessionSmokeTests
    {
        private const string Copy = "Assets/_Project/Data/Copy/";

        private static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(a, Is.Not.Null, path);
            return a;
        }

        [UnityTest]
        public IEnumerator A_run_lands_engages_a_wild_pack_and_deals_damage()
        {
            var db = Load<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            var copy = new DemoCopy("zh-TW", Load<TextAsset>(Copy + "strings.json").text, Load<TextAsset>(Copy + "names.json").text,
                Load<TextAsset>(Copy + "affixes.json").text, Load<TextAsset>(Copy + "region-themes.json").text,
                Load<TextAsset>(Copy + "quest-templates.json").text, Load<TextAsset>(Copy + "boss-epithets.json").text);
            CombatBalance balance = CombatBalance.FromJson(Load<TextAsset>(Copy + "balance.json").text);
            var wild = new List<CreatureDef>();
            var party = new List<CreatureDef>();
            PlanetTypeDef type = null;
            foreach (GameDef d in db.All)
            {
                if (d is CreatureDef c)
                {
                    (c.LineId.StartsWith("starter_") ? party : wild).Add(c);
                }
                else if (d is PlanetTypeDef t && (type == null || t.Id == "verdant_planet"))
                {
                    type = t;
                }
            }

            party.RemoveAll(c => c.Stage != CreatureStage.Adult);
            var meta = new List<TextAsset>();
            var heights = new List<TextAsset>();
            var water = new List<TextAsset>();
            foreach (string id in new[] { "etna_volcano", "sognefjord", "olympic_forest", "kenya_rift" })
            {
                meta.Add(Load<TextAsset>(GeoTemplateLoader.Folder + id + ".meta.json"));
                heights.Add(Load<TextAsset>(GeoTemplateLoader.Folder + id + ".height.bytes"));
                water.Add(Load<TextAsset>(GeoTemplateLoader.Folder + id + ".water.bytes"));
            }

            var templates = new List<Planet.TemplateSource>();
            var tags = new List<IReadOnlyList<string>>();
            GeoTemplateLoader.Load(meta.ToArray(), heights.ToArray(), water.ToArray(), templates, tags);
            RunOutcome outcome = null;
            var session = new GameObject("Run").AddComponent<RunSession>();
            session.Configure(copy, balance, Load<InputActionAsset>("Assets/_Project/Settings/Input/InputSystem_Actions.inputactions"),
                Load<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat"),
                new RunSetup
                {
                    PlanetType = type, Seed = 2026, Radius = 640f, Party = party, Templates = templates, TemplateTags = tags,
                }, wild, o => outcome = o, Load<Shader>("Assets/_Project/Art/Shaders/ToonTerrain.shader"),
                Load<Shader>("Assets/_Project/Art/Shaders/ToonOutlined.shader"));

            float deadline = Time.realtimeSinceStartup + 60f;
            while (!session.StartedForTests && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(session.StartedForTests, Is.True, "terrain colliders under the landing point within 60 s");

            // Teleport next to the nearest normal pack so the 18 m aggro radius engages it. Nearest, because the
            // character's motor rejects an up vector that turns > 90° in one step (a far teleport is not gameplay).
            RunPlan plan = session.PlanForTests;
            float3 target = float3.zero;
            float best = -2f;
            foreach (RunSpawn s in plan.Spawns)
            {
                float d = math.dot(s.Direction, plan.LandingDirection);
                if (s.Role == SpawnRole.Normal && d > best)
                {
                    best = d;
                    target = s.Direction;
                }
            }

            PlanetRig rig = session.RigForTests;
            Encounter encounter = session.EncounterForTests;
            var rb = rig.Character.GetComponent<Rigidbody>();
            rb.position = rig.Config.ToWorld(target, rig.Height.Sample(target) + 1f);
            rig.Character.transform.position = rb.position;

            bool hit = false;
            deadline = Time.realtimeSinceStartup + 60f;
            while (!hit && outcome == null && Time.realtimeSinceStartup < deadline)
            {
                foreach (EncounterUnit u in encounter.Units)
                {
                    hit |= u.Fielded && u.Combatant.Hp < u.Combatant.MaxHp;
                }

                yield return null;
            }

            Assert.That(hit, Is.True, "some fielded creature took damage within 60 s");
            if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") == "1")
            {
                // Visual proof for reviewers (needs a GPU: run without -nographics). Waits for the LOD to settle first.
                deadline = Time.realtimeSinceStartup + 20f;
                while (!rig.Terrain.IsSettled && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                for (int f = 0; f < 30; f++)
                {
                    yield return null;
                }

                // Frame the fight: from above/behind the player toward the nearest fielded wild creature.
                float3 player = (float3)rig.Character.transform.position;
                float3 look = player;
                float bestD = float.MaxValue;
                foreach (EncounterUnit u in encounter.Units)
                {
                    float dd = math.distance(u.Position, player);
                    if (u.Side == EncounterSide.Wild && u.Fielded && dd < bestD)
                    {
                        bestD = dd;
                        look = u.Position;
                    }
                }

                float3 upv = rig.Config.ToDirection(player);
                Debug.Log($"CAPTURE type={type.Id} lat={math.abs(upv.y):0.000} h={rig.SurfaceHeight(upv):0.00} " +
                          $"relief={rig.Height.MaxAbsHeight:0.0} sea={rig.IsSea(upv)} mat={rig.Terrain.GetComponentInChildren<Renderer>()?.sharedMaterial?.shader?.name}");
                float3 toward = math.normalize(look - player - math.dot(look - player, upv) * upv + upv * 1e-4f);
                rig.Camera.transform.position = player - toward * 7f + upv * 4f;
                rig.Camera.transform.rotation = Quaternion.LookRotation(look - (float3)rig.Camera.transform.position, upv);
                var rt = new RenderTexture(1280, 720, 24);
                rig.Camera.targetTexture = rt;
                rig.Camera.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.dataPath, "..", "Logs", "demo-shot.png"),
                    tex.EncodeToPNG());
                rig.Camera.targetTexture = null;
                RenderTexture.active = null;
            }

            Object.Destroy(session.gameObject);
            yield return null;
        }
    }
}
#endif
