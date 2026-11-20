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
    // P1-12 visual proof (vision.md#美術投資 第 1 項): spring flower meadows and the dense near grass ring on real planets
    // (Logs/meadow-<planet>-{near,low,far}.png); logs how many clumps the field draws.
    public sealed class MeadowCaptureTests
    {
        private const string Copy = "Assets/_Project/Data/Copy/";
        private const string Shaders = "Assets/_Project/Art/Shaders/";
        private const float RadiusM = 640f;
        private const CreatureStage Stage = CreatureStage.Adult;
        private static readonly string[] PlanetTypes = { "verdant_planet", "twilight_planet" };

        private static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(a, Is.Not.Null, path);
            return a;
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator Capture_meadows_when_requested()
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
                float3 spot = default, along = default;
                for (ulong s = 1; s <= 60 && seed == 0; s++)
                {
                    using PlanetTemplates t = TemplatePlanner.Build(config, s, type.TerrainTags, BossDraw.SizeFor(Stage));
                    if (FindMeadow(config, t.View, out spot, out along)) seed = s;
                }

                if (seed == 0)
                {
                    Debug.Log($"MEADOW {typeId}: no meadow in seeds 1–60");
                    continue;
                }

                Debug.Log($"MEADOW {typeId} seed={seed}");
                var session = new GameObject("Run").AddComponent<RunSession>();
                session.Configure(copy, CombatBalance.FromJson(balanceJson),
                    Load<InputActionAsset>("Assets/_Project/Settings/Input/InputSystem_Actions.inputactions"),
                    Load<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat"),
                    new RunSetup
                    {
                        PlanetType = type, Seed = seed, Season = Cosmimera.WorldGen.Season.Spring, Radius = RadiusM, BossStage = Stage, Party = party,
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
                for (int h = 0; h < 8 && math.dot(cur, spot) < 0.9999f; h++)
                {
                    float angle = math.acos(math.clamp(math.dot(cur, spot), -1f, 1f));
                    if (angle <= math.radians(40f))
                    {
                        cur = spot;
                    }
                    else
                    {
                        float3 axis = math.cross(cur, spot);
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
                string name = "meadow-" + typeId.Replace("_planet", "");
                GrassField grass = session.GetComponent<GrassField>();
                Debug.Log($"MEADOW {typeId} clumps={grass.InstanceCount} near={grass.NearCount} flowers={grass.FlowerCount}");
                session.CaptureGroundForTests(System.IO.Path.Combine(logs, name + "-near.png"), along, 5f, 1.6f);
                session.CaptureGroundForTests(System.IO.Path.Combine(logs, name + "-low.png"), -along, 3f, 0.7f);
                session.CaptureGroundForTests(System.IO.Path.Combine(logs, name + "-far.png"), along, 26f, 12f);
                shotsTaken++;
                Object.Destroy(session.gameObject);
                yield return null;
                yield return null;
            }

            Assert.That(shotsTaken, Is.GreaterThan(0), "no planet type had a meadow within the searched seeds");
        }

        // Land deep inside a meadow patch (mask > 0.9 out to 15 m) in a stamp's full-weight core, and a heading.
        private static bool FindMeadow(PlanetConfig config, TemplateView view, out float3 dir, out float3 along)
        {
            dir = along = default;
            for (int i = 0; i < view.Count; i++)
            {
                TemplateStamp s = view.Stamp(i);
                for (int gy = -20; gy <= 20; gy++)
                {
                    for (int gx = -20; gx <= 20; gx++)
                    {
                        float3 d = math.normalize(s.Center + (s.Right * gx + s.Up * gy) * (s.TanHalf * 0.55f / 20f));
                        if (view.IsSea(d) || view.River(d) > 0f || view.Apply(d, 0f) < 3f) continue;
                        bool full = true;
                        for (int b = 0; b < 8 && full; b++)
                        {
                            float a = b * math.PI / 4f;
                            float3 t = (s.Right * math.cos(a) + s.Up * math.sin(a)) * (15f / RadiusM);
                            full = GrassField.MeadowAt(config, d + t, 1f) > 0.9f && !view.IsSea(math.normalize(d + t));
                        }

                        if (!full || GrassField.MeadowAt(config, d, 1f) < 0.9f) continue;
                        dir = d;
                        along = s.Right;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
#endif
