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
    // P3-26 visual proof: the same forest on the same planet in each season (Logs/season-<season>-{near,far}.png).
    public sealed class SeasonCaptureTests
    {
        private const string Copy = "Assets/_Project/Data/Copy/";
        private const string Shaders = "Assets/_Project/Art/Shaders/";
        private const float RadiusM = 640f;
        private const CreatureStage Stage = CreatureStage.Adult;

        private static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(a, Is.Not.Null, path);
            return a;
        }

        [UnityTest]
        [Timeout(1800000)]
        public IEnumerator Capture_each_season_when_requested()
        {
            if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") != "1")
            {
                Assert.Ignore("visual proof only: set COSMIMERA_CAPTURE=1 and run without -nographics");
            }

            var db = Load<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            var wild = new List<CreatureDef>();
            var party = new List<CreatureDef>();
            PlanetTypeDef type = null;
            foreach (GameDef d in db.All)
            {
                if (d is CreatureDef c)
                {
                    (c.LineId.StartsWith("starter_") ? party : wild).Add(c);
                }
                else if (d is PlanetTypeDef t && t.Id == "verdant_planet")
                {
                    type = t;
                }
            }

            Assert.That(type, Is.Not.Null);
            party.RemoveAll(c => c.Stage != CreatureStage.Adult);
            string balanceJson = Load<TextAsset>(Copy + "balance.json").text;
            Progression progression = Progression.FromJson(balanceJson);
            var copy = new DemoCopy("zh-TW", Load<TextAsset>(Copy + "strings.json").text, Load<TextAsset>(Copy + "names.json").text,
                Load<TextAsset>(Copy + "affixes.json").text, Load<TextAsset>(Copy + "region-themes.json").text,
                Load<TextAsset>(Copy + "quest-templates.json").text, Load<TextAsset>(Copy + "boss-epithets.json").text);
            var config = new PlanetConfig(RadiusM, float3.zero);

            // A temperate forest stamp: broadleaf, conifer, shrubs and grass all in one frame.
            ulong seed = 0;
            float3 dir = default;
            float3 toward = default;
            for (ulong s = 1; s <= 80 && seed == 0; s++)
            {
                using PlanetTemplates t = TemplatePlanner.Build(config, s, type.TerrainTags, BossDraw.SizeFor(Stage));
                for (int i = 0; i < t.StampIds.Count; i++)
                {
                    TemplateStamp stamp = t.View.Stamp(i);
                    if (LandformSynth.KindOf(t.StampIds[i]) == "forest" && math.abs(stamp.Center.y) < 0.5f &&
                        !t.View.IsSea(stamp.Center))
                    {
                        seed = s;
                        dir = stamp.Center;
                        toward = stamp.Right;
                        break;
                    }
                }
            }

            Assert.That(seed, Is.Not.EqualTo(0UL), "no temperate forest stamp in 80 seeds");
            string logs = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
            foreach (Season season in System.Enum.GetValues(typeof(Season)))
            {
                var session = new GameObject("Run").AddComponent<RunSession>();
                session.Configure(copy, CombatBalance.FromJson(balanceJson),
                    Load<InputActionAsset>("Assets/_Project/Settings/Input/InputSystem_Actions.inputactions"),
                    Load<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat"),
                    new RunSetup
                    {
                        PlanetType = type, Seed = seed, Season = season, Radius = RadiusM, BossStage = Stage, Party = party,
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
                // 40° at a time about the great-circle axis: the motor's contract allows less than 90° per physics step.
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

                // Local weather caps visibility; these shots are about colour, so push the fog out for the renders.
                RenderSettings.fogStartDistance = 120f;
                RenderSettings.fogEndDistance = 700f;
                string name = "season-" + season.ToString().ToLowerInvariant();
                session.CaptureGroundForTests(System.IO.Path.Combine(logs, name + "-near.png"), toward, 14f, 7f);
                session.CaptureGroundForTests(System.IO.Path.Combine(logs, name + "-far.png"), -toward, 40f, 24f);
                Object.Destroy(session.gameObject);
                yield return null;
                yield return null;
            }
        }
    }
}
#endif
