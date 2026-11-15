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
    // P3-28 / P3-24(b) visual proof: screenshots of each LandformSynth v2 landform on a real planet
    // (Logs/landform-<kind>-<planet>-{near,far}.png).
    public sealed class LandformCaptureTests
    {
        private const string Copy = "Assets/_Project/Data/Copy/";
        private const string Shaders = "Assets/_Project/Art/Shaders/";
        private const float RadiusM = 640f;
        private const CreatureStage Stage = CreatureStage.Adult;

        // Planet type → the v2 landforms its affinity tags prefer, so a few seeds are enough to find them.
        private static readonly (string type, string[] kinds)[] Wanted =
        {
            ("ice_crystal_planet", new[] { "tundra", "glacier", "cirque", "moraine" }),
            ("verdant_planet", new[] { "grassland", "swamp" }),
            ("mineral_planet", new[] { "desert", "karst" }),
            ("storm_planet", new[] { "mountain", "granite_domes" }),
            ("lava_planet", new[] { "basalt_terrace" }),
            ("twilight_planet", new[] { "slate_cliffs" }),
        };

        private static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(a, Is.Not.Null, path);
            return a;
        }

        [UnityTest]
        [Timeout(1800000)]
        public IEnumerator Capture_each_new_landform_when_requested()
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

            foreach ((string typeId, string[] kinds) in Wanted)
            {
                Assert.That(types.ContainsKey(typeId), Is.True, typeId);
                PlanetTypeDef type = types[typeId];
                int count = TemplatePlanner.CountFor(BossDraw.SizeFor(Stage));
                ulong seed = 0;
                int most = -1;
                for (ulong s = 1; s <= 80; s++)
                {
                    int found = TemplatePlanner.Kinds(s, type.TerrainTags, count, out _).Intersect(kinds).Count();
                    if (found > most)
                    {
                        most = found;
                        seed = s;
                    }
                }

                var shots = new List<(string landform, float3 dir, float3 toward)>();
                using (PlanetTemplates t = TemplatePlanner.Build(config, seed, type.TerrainTags, BossDraw.SizeFor(Stage)))
                {
                    Debug.Log($"LANDFORM {typeId} seed={seed} ids={string.Join(",", t.StampIds)}");
                    TemplateView view = t.View;
                    var seen = new HashSet<string>();
                    for (int i = 0; i < t.StampIds.Count; i++)
                    {
                        string kind = LandformSynth.KindOf(t.StampIds[i]);
                        if (System.Array.IndexOf(kinds, kind) < 0 || !seen.Add(kind))
                        {
                            continue;
                        }

                        TemplateStamp stamp = view.Stamp(i);
                        // Walk out from the centre until the ground is land; stay inside the stamp's full-weight core.
                        for (int k = 0; k < 24; k++)
                        {
                            float a = k * 2.4f;
                            float r = stamp.TanHalf * 0.5f * k / 24f;
                            float3 dir = math.normalize(stamp.Center + (stamp.Right * math.cos(a) + stamp.Up * math.sin(a)) * r);
                            if (!view.IsSea(dir))
                            {
                                shots.Add((kind, dir, stamp.Right));
                                break;
                            }
                        }
                    }
                }

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
                while (!session.StartedForTests && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.That(session.StartedForTests, Is.True);
                session.SuspendCameraForTests();
                foreach ((string landform, float3 dir, float3 toward) in shots)
                {
                    // The motor's contract allows less than 90° of travel per physics step: cross the planet in short hops.
                    // Rotate 40° at a time about the great-circle axis: a straight lerp between near-antipodal points
                    // passes close to the centre and would jump most of the way in one hop.
                    float3 cur = session.DirectionForTests(session.PlayerPositionForTests);
                    for (int h = 0; h < 8; h++)
                    {
                        float angle = math.acos(math.clamp(math.dot(cur, dir), -1f, 1f));
                        if (angle <= math.radians(40f))
                        {
                            cur = dir;
                        }
                        else
                        {
                            float3 axis = math.cross(cur, dir);
                            if (math.lengthsq(axis) < 1e-6f)
                            {
                                axis = math.cross(cur, math.abs(cur.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f));
                            }

                            cur = math.normalize(math.mul(quaternion.AxisAngle(math.normalize(axis), math.radians(40f)), cur));
                        }

                        session.PlaceCharacterForTests(cur);
                        yield return new WaitForFixedUpdate();
                        yield return new WaitForFixedUpdate();
                        if (math.dot(cur, dir) > 0.9999f)
                        {
                            break;
                        }
                    }

                    deadline = Time.realtimeSinceStartup + 30f;
                    for (int f = 0; f < 20; f++)
                    {
                        yield return null;
                    }

                    while (!session.TerrainSettledForTests && Time.realtimeSinceStartup < deadline)
                    {
                        yield return null;
                    }

                    for (int f = 0; f < 60; f++)
                    {
                        yield return null;
                    }

                    // Local weather (blizzard, sandstorm, fog) caps visibility to a few tens of metres; the shots are
                    // about the ground's shape, so push the fog out for the synchronous renders below.
                    RenderSettings.fogStartDistance = 120f;
                    RenderSettings.fogEndDistance = 700f;
                    string name = $"landform-{landform}-{typeId.Replace("_planet", "")}";
                    session.CaptureGroundForTests(System.IO.Path.Combine(logs, name + "-near.png"), toward, 14f, 7f);
                    session.CaptureGroundForTests(System.IO.Path.Combine(logs, name + "-far.png"), -toward, 70f, 55f);
                }

                Object.Destroy(session.gameObject);
                yield return null;
                yield return null;
            }
        }
    }
}
#endif
