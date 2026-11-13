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
    /// <summary>
    /// The demo's smallest planet end to end on the real rig: landing, a full lap with ground and camera checks, and a
    /// fight whose capped ranged moves still connect.
    /// REQ: planet-world.md#星球尺度 (Q-D1, asteroid R=320); SOL Q-D1-R3 (tech-debt) asked for exactly this native run.
    /// </summary>
    public sealed class RunSessionAsteroidTests
    {
        private const string Copy = "Assets/_Project/Data/Copy/";
        private const float LapStepM = 4f;
        private const int Checkpoints = 8;
        // Collider triangles are chords of the curved surface (PlanetTerrainTests uses the same sag allowance).
        private const float GroundToleranceM = 0.5f;

        private static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(a, Is.Not.Null, path);
            return a;
        }

        [UnityTest]
        public IEnumerator A_juvenile_boss_run_lands_laps_and_fights_on_the_320_m_asteroid()
        {
            var db = Load<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            var copy = new DemoCopy("zh-TW", Load<TextAsset>(Copy + "strings.json").text, Load<TextAsset>(Copy + "names.json").text,
                Load<TextAsset>(Copy + "affixes.json").text, Load<TextAsset>(Copy + "region-themes.json").text,
                Load<TextAsset>(Copy + "quest-templates.json").text, Load<TextAsset>(Copy + "boss-epithets.json").text);
            string balanceJson = Load<TextAsset>(Copy + "balance.json").text;
            CombatBalance balance = CombatBalance.FromJson(balanceJson);
            Progression progression = Progression.FromJson(balanceJson);
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

            // Adult starters carry the longest ranged moves, so the planet cap is what limits their reach here.
            party.RemoveAll(c => c.Stage != CreatureStage.Adult);
            float radius = DemoGame.RadiusFor(BossDraw.SizeFor(CreatureStage.Juvenile));
            Assert.That(radius, Is.EqualTo(320f), "the juvenile boss tier is the asteroid");

            RunOutcome outcome = null;
            var session = new GameObject("Run").AddComponent<RunSession>();
            session.Configure(copy, balance, Load<InputActionAsset>("Assets/_Project/Settings/Input/InputSystem_Actions.inputactions"),
                Load<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat"),
                new RunSetup
                {
                    PlanetType = type, Seed = 2026, Radius = radius, BossStage = CreatureStage.Juvenile, Party = party,
                    PartyIds = Enumerable.Range(1, party.Count).ToList(),
                    PartyExp = Enumerable.Repeat(progression.XpThreshold(progression.StarterLevel), party.Count).ToList(),
                    Progression = progression, Materials = MaterialDrops.FromJson(balanceJson),
                    EnvMaterials = EnvMaterials.FromJson(Load<TextAsset>(Copy + "env-materials.json").text),
                }, wild, o => outcome = o, Load<Shader>("Assets/_Project/Art/Shaders/ToonTerrain.shader"),
                Load<Shader>("Assets/_Project/Art/Shaders/ToonOutlined.shader"), Load<Shader>("Assets/_Project/Art/Shaders/Stars.shader"));
            try
            {
                // --- Landing ---------------------------------------------------------------------------------------
                float deadline = Time.realtimeSinceStartup + 60f;
                while (!session.StartedForTests && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.That(session.StartedForTests, Is.True, "terrain colliders under the landing point within 60 s");
                Assert.That(session.RadiusForTests, Is.EqualTo(radius));
                Assert.That(RenderSettings.fogStartDistance,
                    Is.LessThan(new PlanetConfig(radius, float3.zero).HorizonDistance(balance.Horizon.EyeHeightMm / 1000f)),
                    "fog starts inside the asteroid's horizon");
                yield return Rest(session, 1f);
                AssertStandingWithLevelCamera(session, "landing");

                // --- Full lap --------------------------------------------------------------------------------------
                // Combat paused so a wandering pack cannot end the run mid-lap; terrain, motor and camera keep running.
                session.enabled = false;
                float3 start = session.LandingDirectionForTests;
                float3 axis = math.normalize(math.cross(start, math.abs(start.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f)));
                int steps = (int)math.ceil(2f * math.PI * radius / LapStepM);
                int every = steps / Checkpoints;
                for (int i = 1; i <= steps; i++)
                {
                    float3 dir = math.normalize(math.mul(quaternion.AxisAngle(axis, 2f * math.PI * i / steps), start));
                    session.PlaceCharacterForTests(dir);
                    yield return null;
                    if (i % every != 0 && i != steps)
                    {
                        continue;
                    }

                    deadline = Time.realtimeSinceStartup + 30f;
                    while (!session.TerrainSettledForTests && Time.realtimeSinceStartup < deadline)
                    {
                        yield return null;
                    }

                    Assert.That(session.TerrainSettledForTests, Is.True, $"LOD settles at lap step {i}/{steps}");
                    yield return new WaitForFixedUpdate();
                    float3 from = session.SurfacePointForTests(dir, 2f);
                    Assert.That(Physics.Raycast(from, -(Vector3)dir, out RaycastHit hit, 5f), Is.True,
                        $"a ground collider under lap step {i}/{steps}");
                    Assert.That(hit.distance, Is.EqualTo(2f).Within(GroundToleranceM), $"collider matches the surface at step {i}");
                    session.PlaceCharacterForTests(dir);
                    yield return Rest(session, 1f);
                    AssertStandingWithLevelCamera(session, $"lap step {i}/{steps}");
                }

                float3 end = session.DirectionForTests(session.PlayerPositionForTests);
                Assert.That(math.dot(end, start), Is.GreaterThan(math.cos(LapStepM / radius)), "the lap closes where it started");

                // --- Fight -----------------------------------------------------------------------------------------
                // Nearest normal pack, as in RunSessionSmokeTests: the motor rejects an up that turns > 90° in one step.
                session.enabled = true;
                float3 target = float3.zero;
                float best = -2f;
                foreach (RunSpawn s in session.SpawnsForTests)
                {
                    float d = math.dot(s.Direction, start);
                    if (s.Role == SpawnRole.Normal && d > best)
                    {
                        best = d;
                        target = s.Direction;
                    }
                }

                session.MovePlayerForTests(target);
                yield return null;
                session.FocusNearestForTests();

                // A cast the AI thought in reach but the capped volume could not touch raises no event (SOL Q-D1-IMPL-01:
                // the AI fired short forever), so contact from the party proves reach and cast agree at this radius.
                int casts = 0;
                int contacts = 0;
                long seenMs = session.NowMsForTests;
                deadline = Time.realtimeSinceStartup + 60f;
                while (contacts == 0 && outcome == null && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    // Events hold the last tick only, and batchmode frames outpace the 50 ms tick: count each tick once.
                    if (session.NowMsForTests == seenMs)
                    {
                        continue;
                    }

                    seenMs = session.NowMsForTests;
                    int units = session.UnitsForTests.Count;
                    foreach (EncounterEvent e in session.EventsForTests)
                    {
                        // The character (Encounter.PlayerCharacterId) is no creature unit.
                        if (e.SourceId < 0 || e.SourceId >= units || session.UnitForTests(e.SourceId).Side != EncounterSide.Player)
                        {
                            continue;
                        }

                        casts += e.Kind == EncounterEventKind.Cast ? 1 : 0;
                        contacts += e.Kind == EncounterEventKind.Hit || e.Kind == EncounterEventKind.Miss ? 1 : 0;
                    }
                }

                TestContext.WriteLine($"R={radius}: party casts {casts}, contacts {contacts}; fog start {RenderSettings.fogStartDistance:F2} m");
                Assert.That(contacts, Is.GreaterThan(0), "a party move touched a wild creature within 60 s");
            }
            finally
            {
                Object.Destroy(session.gameObject);
            }

            yield return null;
        }

        // Fixed steps, not frames: -nographics spins frames in microseconds while the body falls in physics time.
        private static IEnumerator Rest(RunSession session, float seconds)
        {
            int steps = (int)math.ceil(seconds / Time.fixedDeltaTime);
            for (int i = 0; i < steps; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Cinemachine damps toward the character after a staged move; wait until the camera stops moving.
            float deadline = Time.realtimeSinceStartup + 10f;
            Vector3 last;
            do
            {
                last = session.CameraPositionForTests;
                yield return null;
            }
            while (Vector3.Distance(last, session.CameraPositionForTests) > 1e-3f && Time.realtimeSinceStartup < deadline);
        }

        private static void AssertStandingWithLevelCamera(RunSession session, string where)
        {
            float3 player = session.PlayerPositionForTests;
            float3 up = session.DirectionForTests(player);
            Assert.That(session.GroundedForTests, Is.True, $"{where}: the character stands on ground");
            Assert.That(session.AltitudeForTests(player), Is.EqualTo(session.SurfaceHeightForTests(up)).Within(GroundToleranceM),
                $"{where}: the character is on the rendered surface");

            float3 camera = session.CameraPositionForTests;
            // Third-person follow distance is 4 m (PlanetRig); twice that catches a camera left behind or flung off.
            Assert.That(math.distance(camera, player), Is.LessThan(8f), $"{where}: the camera follows the character");
            // A camera that kept world +Y (or a stale up) as its up rolls the horizon on a sphere.
            Assert.That(math.abs(math.dot((float3)session.CameraRightForTests, up)), Is.LessThan(0.02f),
                $"{where}: camera roll follows local up");
        }
    }
}
#endif
