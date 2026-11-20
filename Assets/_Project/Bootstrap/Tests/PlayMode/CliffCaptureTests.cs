#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Combat;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    // P1-18 visual proof (planet-world.md#懸崖): the steepest face a landform stamp makes on a real planet, seen from its
    // foot (Logs/cliff-<planet>-{near,far}.png).
    public sealed class CliffCaptureTests
    {
        private const string Copy = "Assets/_Project/Data/Copy/";
        private const string Shaders = "Assets/_Project/Art/Shaders/";
        private const float RadiusM = 640f;
        private const CreatureStage Stage = CreatureStage.Adult;
        private static readonly string[] PlanetTypes = { "twilight_planet", "mineral_planet", "storm_planet", "verdant_planet" };

        private static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(a, Is.Not.Null, path);
            return a;
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator Capture_cliff_faces_when_requested()
        {
            if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") != "1")
            {
                Assert.Ignore("visual proof only: set COSMIMERA_CAPTURE=1 and run without -nographics");
            }

            var db = Load<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            var wild = new List<CreatureDef>();
            var party = new List<CreatureDef>();
            var types = new Dictionary<string, PlanetTypeDef>();
            foreach (GameDef d in db.All)
            {
                if (d is CreatureDef c)
                {
                    (c.LineId.StartsWith("starter_") ? party : wild).Add(c);
                }
                else if (d is PlanetTypeDef t)
                {
                    types[t.Id] = t;
                }
            }

            party.RemoveAll(c => c.Stage != CreatureStage.Adult);
            string balanceJson = Load<TextAsset>(Copy + "balance.json").text;
            Progression progression = Progression.FromJson(balanceJson);
            var copy = new DemoCopy("zh-TW", Load<TextAsset>(Copy + "strings.json").text, Load<TextAsset>(Copy + "names.json").text,
                Load<TextAsset>(Copy + "affixes.json").text, Load<TextAsset>(Copy + "region-themes.json").text,
                Load<TextAsset>(Copy + "quest-templates.json").text, Load<TextAsset>(Copy + "boss-epithets.json").text);
            var config = new PlanetConfig(RadiusM, float3.zero);
            string logs = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
            int shotsTaken = 0;

            foreach (string typeId in PlanetTypes)
            {
                PlanetTypeDef type = types[typeId];
                                ulong seed = 0;
                float3 foot = default, uphill = default;
                for (ulong s = 1; s <= 60 && seed == 0; s++)
                {
                    using PlanetTemplates t = TemplatePlanner.Build(config, s, type.TerrainTags, BossDraw.SizeFor(Stage));
                    if (FindCliff(t.View, out foot, out uphill)) seed = s;
                }

                if (seed == 0)
                {
                    Debug.Log($"CLIFF {typeId}: no cliff in seeds 1–60");
                    continue;
                }

                Debug.Log($"CLIFF {typeId} seed={seed}");
                var session = new GameObject("Run").AddComponent<RunSession>();
                session.Configure(copy, CombatBalance.FromJson(balanceJson),
                    Load<InputActionAsset>("Assets/_Project/Settings/Input/InputSystem_Actions.inputactions"),
                    Load<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat"),
                    new RunSetup
                    {
                        PlanetType = type, Seed = seed, Radius = RadiusM, BossStage = Stage, Party = party,
                        PartyIds = Enumerable.Range(1, party.Count).ToList(),
                        PartyExp = Enumerable.Repeat(progression.XpThreshold(progression.StarterLevel), party.Count).ToList(),
                        Progression = progression, Materials = MaterialDrops.FromJson(balanceJson),
                        EnvMaterials = EnvMaterials.FromJson(Load<TextAsset>(Copy + "env-materials.json").text),
                    }, wild, _ => { }, Load<Shader>(Shaders + "ToonTerrain.shader"), Load<Shader>(Shaders + "ToonOutlined.shader"),
                    Load<Shader>(Shaders + "Stars.shader"),
                    new SceneryShaders(Load<Shader>(Shaders + "ToonSky.shader"), Load<Shader>(Shaders + "ToonGrass.shader"),
                        Load<Shader>(Shaders + "ToonWater.shader"), Load<Shader>(Shaders + "ToonScenery.shader")));

                float deadline = Time.realtimeSinceStartup + 60f;
                while (!session.StartedForTests && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(session.StartedForTests, Is.True);
                session.SuspendCameraForTests();

                // Short hops: the motor allows less than 90° of travel per physics step.
                float3 cur = session.DirectionForTests(session.PlayerPositionForTests);
                for (int h = 0; h < 8 && math.dot(cur, foot) < 0.9999f; h++)
                {
                    float angle = math.acos(math.clamp(math.dot(cur, foot), -1f, 1f));
                    if (angle <= math.radians(40f))
                    {
                        cur = foot;
                    }
                    else
                    {
                        float3 axis = math.cross(cur, foot);
                        if (math.lengthsq(axis) < 1e-6f) axis = math.cross(cur, math.abs(cur.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f));
                        cur = math.normalize(math.mul(quaternion.AxisAngle(math.normalize(axis), math.radians(40f)), cur));
                    }

                    session.PlaceCharacterForTests(cur);
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                }

                deadline = Time.realtimeSinceStartup + 30f;
                for (int f = 0; f < 20; f++) yield return null;
                while (!session.TerrainSettledForTests && Time.realtimeSinceStartup < deadline) yield return null;
                for (int f = 0; f < 60; f++) yield return null;

                RenderSettings.fogStartDistance = 120f;
                RenderSettings.fogEndDistance = 700f;
                string name = "cliff-" + typeId.Replace("_planet", "");
                session.CaptureGroundForTests(System.IO.Path.Combine(logs, name + "-near.png"), uphill, 7f, 2.5f);
                session.CaptureGroundForTests(System.IO.Path.Combine(logs, name + "-far.png"), uphill, 30f, 10f);
                shotsTaken++;
                Object.Destroy(session.gameObject);
                yield return null;
                yield return null;
            }

            Assert.That(shotsTaken, Is.GreaterThan(0), "no planet type had a cliff within the searched seeds");
        }

        // A point ~10 m out from the foot of the steepest face (rise/run >= 1.2) in a stamp's full-weight core, and the
        // uphill heading toward that face. Templates only: the procedural noise adds little at this scale.
        private static bool FindCliff(TemplateView view, out float3 foot, out float3 uphill)
        {
            foot = uphill = default;
            float best = 1.2f;
            for (int i = 0; i < view.Count; i++)
            {
                TemplateStamp s = view.Stamp(i);
                float step = s.TanHalf * 0.55f / 40f;
                float runM = step * RadiusM * 2f;
                for (int gy = -39; gy <= 39; gy++)
                {
                    for (int gx = -39; gx <= 39; gx++)
                    {
                        float3 d = math.normalize(s.Center + (s.Right * gx + s.Up * gy) * step);
                        if (view.IsSea(d)) continue;
                        float3 e = math.normalize(d + s.Right * step), w = math.normalize(d - s.Right * step);
                        float3 n = math.normalize(d + s.Up * step), so = math.normalize(d - s.Up * step);
                        float dx = (view.Apply(e, 0f) - view.Apply(w, 0f)) / runM;
                        float dy = (view.Apply(n, 0f) - view.Apply(so, 0f)) / runM;
                        float rise = math.sqrt(dx * dx + dy * dy);
                        if (rise <= best) continue;
                        float3 up = math.normalize(s.Right * dx + s.Up * dy);
                        float3 at = math.normalize(d - up * (10f / RadiusM));
                        if (view.IsSea(at)) continue;
                        best = rise;
                        foot = at;
                        uphill = up;
                    }
                }
            }

            return best > 1.2f;
        }
    }
}
#endif
