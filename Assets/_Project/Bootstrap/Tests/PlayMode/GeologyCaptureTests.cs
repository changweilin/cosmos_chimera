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
    // P3-24(a) visual proof: one screenshot pair per bedrock on a real planet (Logs/geology-*.png).
    public sealed class GeologyCaptureTests
    {
        private const string Copy = "Assets/_Project/Data/Copy/";
        private const string Shaders = "Assets/_Project/Art/Shaders/";
        private const float RadiusM = 640f;

        private static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(a, Is.Not.Null, path);
            return a;
        }

        [UnityTest]
        public IEnumerator Capture_each_bedrock_when_requested()
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
                else if (d is PlanetTypeDef t && (type == null || t.Id == "verdant_planet"))
                {
                    type = t;
                }
            }

            party.RemoveAll(c => c.Stage != CreatureStage.Adult);
            var config = new PlanetConfig(RadiusM, float3.zero);
            const CreatureStage Stage = CreatureStage.Adult;

            // The seed whose stamps show the most different bedrocks: fewer sessions, more of the table on screen.
            ulong seed = 0;
            int most = -1;
            for (ulong s = 1; s <= 40; s++)
            {
                using PlanetTemplates probe = TemplatePlanner.Build(config, s, type.TerrainTags, BossDraw.SizeFor(Stage));
                int distinct = TemplatePlanner.Geologies(probe, s, type.Id, out _).Where(g => g != Geology.Native).Distinct().Count();
                if (distinct > most)
                {
                    most = distinct;
                    seed = s;
                }
            }

            var shots = new List<(Geology geology, string landform, float3 dir, float3 toward)>();
            using (PlanetTemplates t = TemplatePlanner.Build(config, seed, type.TerrainTags, BossDraw.SizeFor(Stage)))
            {
                Geology[] geologies = TemplatePlanner.Geologies(t, seed, type.Id, out Geology baseGeology);
                Debug.Log($"GEOLOGY seed={seed} base={baseGeology} stamps={string.Join(",", geologies)} ids={string.Join(",", t.StampIds)}");
                TemplateView view = t.View;
                var seen = new HashSet<Geology>();
                for (int i = 0; i < geologies.Length; i++)
                {
                    if (!seen.Add(geologies[i]))
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
                            shots.Add((geologies[i], LandformSynth.KindOf(t.StampIds[i]), dir, stamp.Right));
                            break;
                        }
                    }
                }
            }

            var session = OpenPlanet(type, seed, RadiusM, Stage, party, wild);

            float deadline = Time.realtimeSinceStartup + 60f;
            while (!session.StartedForTests && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(session.StartedForTests, Is.True);
            session.SuspendCameraForTests();
            foreach ((Geology geology, string landform, float3 dir, float3 toward) in shots)
            {
                // The motor's contract allows less than 90° of travel per physics step: cross the planet in short hops.
                float3 from = session.DirectionForTests(session.PlayerPositionForTests);
                int hops = 1 + (int)(math.acos(math.clamp(math.dot(from, dir), -1f, 1f)) / math.radians(40f));
                for (int h = 1; h <= hops; h++)
                {
                    float3 step = math.normalize(math.lerp(from, dir, h / (float)hops) + new float3(0f, 1e-3f, 0f) * (hops - h));
                    session.PlaceCharacterForTests(step);
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
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

                string logs = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
                session.CaptureGroundForTests(System.IO.Path.Combine(logs, $"geology-{geology}-{landform}-near.png"), toward, 14f, 7f);
                session.CaptureGroundForTests(System.IO.Path.Combine(logs, $"geology-{geology}-{landform}-far.png"), -toward, 45f, 30f);
            }

            Object.Destroy(session.gameObject);
            yield return null;
        }

        internal static RunSession OpenPlanet(PlanetTypeDef type, ulong seed, float radius, CreatureStage stage,
            List<CreatureDef> party, List<CreatureDef> wild)
        {
            string balanceJson = Load<TextAsset>(Copy + "balance.json").text;
            Progression progression = Progression.FromJson(balanceJson);
            var copy = new DemoCopy("zh-TW", Load<TextAsset>(Copy + "strings.json").text, Load<TextAsset>(Copy + "names.json").text,
                Load<TextAsset>(Copy + "affixes.json").text, Load<TextAsset>(Copy + "region-themes.json").text,
                Load<TextAsset>(Copy + "quest-templates.json").text, Load<TextAsset>(Copy + "boss-epithets.json").text);
            var session = new GameObject("Run").AddComponent<RunSession>();
            session.Configure(copy, CombatBalance.FromJson(balanceJson),
                Load<InputActionAsset>("Assets/_Project/Settings/Input/InputSystem_Actions.inputactions"),
                Load<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat"),
                new RunSetup
                {
                    PlanetType = type, Seed = seed, Radius = radius, BossStage = stage, Party = party,
                    PartyIds = Enumerable.Range(1, party.Count).ToList(),
                    PartyExp = Enumerable.Repeat(progression.XpThreshold(progression.StarterLevel), party.Count).ToList(),
                    Progression = progression, Materials = MaterialDrops.FromJson(balanceJson),
                    EnvMaterials = EnvMaterials.FromJson(Load<TextAsset>(Copy + "env-materials.json").text),
                }, wild, _ => { }, Load<Shader>(Shaders + "ToonTerrain.shader"), Load<Shader>(Shaders + "ToonOutlined.shader"),
                Load<Shader>(Shaders + "Stars.shader"),
                new SceneryShaders(Load<Shader>(Shaders + "ToonSky.shader"), Load<Shader>(Shaders + "ToonGrass.shader"),
                    Load<Shader>(Shaders + "ToonWater.shader"), Load<Shader>(Shaders + "ToonScenery.shader")));

            return session;
        }
    }
}
#endif
