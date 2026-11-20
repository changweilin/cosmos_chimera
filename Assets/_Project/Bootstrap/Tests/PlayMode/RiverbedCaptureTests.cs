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
    // P1-18 visual proof (planet-world.md#河床): a real river channel on a real planet, painted with pebbles, sand bars,
    // wet banks and the shallow stream (Logs/riverbed-<planet>-{along,across,far}.png).
    public sealed class RiverbedCaptureTests
    {
        private const string Copy = "Assets/_Project/Data/Copy/";
        private const string Shaders = "Assets/_Project/Art/Shaders/";
        private const float RadiusM = 640f;
        private const CreatureStage Stage = CreatureStage.Adult;
        private static readonly string[] RiverKinds = { "river_valley", "grassland", "forest", "canyon" };
        private static readonly string[] PlanetTypes = { "verdant_planet", "mineral_planet" };

        private static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(a, Is.Not.Null, path);
            return a;
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator Capture_a_river_channel_when_requested()
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
                int count = TemplatePlanner.CountFor(BossDraw.SizeFor(Stage));
                ulong seed = 0;
                float3 river = default, along = default;
                for (ulong s = 1; s <= 60 && seed == 0; s++)
                {
                    if (!TemplatePlanner.Kinds(s, type.TerrainTags, count, out _).Intersect(RiverKinds).Any()) continue;
                    using PlanetTemplates t = TemplatePlanner.Build(config, s, type.TerrainTags, BossDraw.SizeFor(Stage));
                    if (FindRiver(t.View, out river, out along)) seed = s;
                }

                if (seed == 0)
                {
                    Debug.Log($"RIVERBED {typeId}: no river in seeds 1–60");
                    continue;
                }

                Debug.Log($"RIVERBED {typeId} seed={seed}");
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
                for (int h = 0; h < 8 && math.dot(cur, river) < 0.9999f; h++)
                {
                    float angle = math.acos(math.clamp(math.dot(cur, river), -1f, 1f));
                    if (angle <= math.radians(40f))
                    {
                        cur = river;
                    }
                    else
                    {
                        float3 axis = math.cross(cur, river);
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
                string name = "riverbed-" + typeId.Replace("_planet", "");
                float3 across = math.cross(river, along);
                session.CaptureGroundForTests(System.IO.Path.Combine(logs, name + "-along.png"), along, 9f, 3.5f);
                session.CaptureGroundForTests(System.IO.Path.Combine(logs, name + "-across.png"), across, 6f, 4f);
                session.CaptureGroundForTests(System.IO.Path.Combine(logs, name + "-far.png"), -along, 30f, 16f);
                shotsTaken++;
                Object.Destroy(session.gameObject);
                yield return null;
                yield return null;
            }

            Assert.That(shotsTaken, Is.GreaterThan(0), "no planet type had a river within the searched seeds");
        }

        // A point on a channel's centre line in a stamp's full-weight core, and the channel's heading there.
        private static bool FindRiver(TemplateView view, out float3 dir, out float3 along)
        {
            dir = along = default;
            for (int i = 0; i < view.Count; i++)
            {
                TemplateStamp s = view.Stamp(i);
                for (int gy = -40; gy <= 40; gy++)
                {
                    for (int gx = -40; gx <= 40; gx++)
                    {
                        float3 d = math.normalize(s.Center + (s.Right * gx + s.Up * gy) * (s.TanHalf * 0.55f / 40f));
                        if (view.River(d) < 0.97f || view.IsSea(d)) continue;
                        float best = -1f;
                        for (int b = 0; b < 16; b++)
                        {
                            float a = b * math.PI / 16f;
                            float3 t = s.Right * math.cos(a) + s.Up * math.sin(a);
                            float step = s.TanHalf * 0.03f;
                            float score = view.River(math.normalize(d + t * step)) + view.River(math.normalize(d - t * step));
                            if (score > best)
                            {
                                best = score;
                                along = t;
                            }
                        }

                        if (best < 1.6f) continue; // a bend or a single wet cell: look for a straighter reach
                        dir = d;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
#endif
