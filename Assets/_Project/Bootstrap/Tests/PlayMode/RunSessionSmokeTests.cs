#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Combat;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
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
            Progression progression = Progression.FromJson(Load<TextAsset>(Copy + "balance.json").text);
            var actions = Load<InputActionAsset>("Assets/_Project/Settings/Input/InputSystem_Actions.inputactions");
            bool sharedRunEnabled = actions.FindActionMap("DemoRun").enabled;
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
            session.Configure(copy, balance, actions,
                Load<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat"),
                new RunSetup
                {
                    PlanetType = type, Seed = 2026, Radius = 640f, Party = party, Templates = templates, TemplateTags = tags,
                    PartyIds = Enumerable.Range(1, party.Count).ToList(),
                    PartyExp = Enumerable.Repeat(progression.XpThreshold(progression.StarterLevel), party.Count).ToList(),
                    Progression = progression, Materials = MaterialDrops.FromJson(Load<TextAsset>(Copy + "balance.json").text),
                }, wild, o => outcome = o, Load<Shader>("Assets/_Project/Art/Shaders/ToonTerrain.shader"),
                Load<Shader>("Assets/_Project/Art/Shaders/ToonOutlined.shader"), Load<Shader>("Assets/_Project/Art/Shaders/Stars.shader"));

            float deadline = Time.realtimeSinceStartup + 60f;
            while (!session.StartedForTests && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(session.StartedForTests, Is.True, "terrain colliders under the landing point within 60 s");

            Assert.That(session.TryQuestTarget(session.QuestStepForTests(0), out float3 questTarget), Is.True);
            Assert.That(math.distance(questTarget, session.RegionCenterForTests(session.QuestStepForTests(0).Region)),
                Is.LessThan(1e-5f), "the reach-region guide points at the quest region");

            Keyboard previousKeyboard = Keyboard.current;
            InputSettings.BackgroundBehavior previousBackground = InputSystem.settings.backgroundBehavior;
            InputSettings.EditorInputBehaviorInPlayMode previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            // Batchmode's hidden Game view can lose focus; synthetic input must still reach the real Update path.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Gamepad previousGamepad = Gamepad.current;
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            InputSystem.EnableDevice(keyboard);
            keyboard.MakeCurrent();
            try
            {
                bool view = session.FirstPersonForTests;
                var vessel = session.VesselForTests;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.B));
                yield return null;
                yield return null;
                Assert.That(keyboard.bKey.isPressed, Is.True, "the synthetic keyboard event reached InputSystem");
                Assert.That(session.VesselForTests, Is.Not.EqualTo(vessel), "B cycles the capture vessel");
                Assert.That(session.FirstPersonForTests, Is.EqualTo(view), "B leaves the view alone");

                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                vessel = session.VesselForTests;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V));
                yield return null;
                yield return null;
                Assert.That(session.FirstPersonForTests, Is.Not.EqualTo(view), "V toggles the view");
                Assert.That(session.VesselForTests, Is.EqualTo(vessel), "V leaves the vessel alone");
                session.ToggleViewForTests();

                vessel = session.VesselForTests;
                yield return Press(gamepad, new GamepadState().WithButton(GamepadButton.East));
                Assert.That(session.VesselForTests, Is.Not.EqualTo(vessel), "gamepad east cycles vessels");
                Assert.That(session.FirstPersonForTests, Is.EqualTo(view), "vessel input leaves the view alone");

                int main = session.MainForTests;
                yield return Press(gamepad, new GamepadState().WithButton(GamepadButton.DpadRight));
                Assert.That(session.MainForTests, Is.Not.EqualTo(main), "d-pad right selects the other active lead");
                yield return Press(gamepad, new GamepadState().WithButton(GamepadButton.DpadLeft));
                Assert.That(session.MainForTests, Is.EqualTo(main), "d-pad left selects the first active lead");
                yield return Press(gamepad, new GamepadState().WithButton(GamepadButton.RightShoulder));
                Assert.That(session.MainForTests, Is.Not.EqualTo(main), "right shoulder switches to a reserve creature");

                yield return Press(gamepad, new GamepadState().WithButton(GamepadButton.DpadUp));
                Assert.That(session.CommandForTests, Is.EqualTo(AiCommandKind.Follow));
                yield return Press(gamepad, new GamepadState { leftTrigger = 1f });
                Assert.That(session.CommandForTests, Is.EqualTo(AiCommandKind.Retreat));
                yield return Press(gamepad, new GamepadState().WithButton(GamepadButton.DpadDown));
                Assert.That(session.CommandForTests, Is.EqualTo(AiCommandKind.Free));

                session.enabled = false;
                vessel = session.VesselForTests;
                yield return Press(gamepad, new GamepadState().WithButton(GamepadButton.East));
                Assert.That(session.VesselForTests, Is.EqualTo(vessel), "disabled run actions do not fire");
                session.enabled = true;
                yield return Press(gamepad, new GamepadState().WithButton(GamepadButton.East));
                Assert.That(session.VesselForTests, Is.Not.EqualTo(vessel), "reenabled run actions work again");
                // Unity's project-wide action asset may already be enabled when PlayMode starts.
                Assert.That(actions.FindActionMap("DemoRun").enabled, Is.EqualTo(sharedRunEnabled),
                    "the session leaves the shared map's original state unchanged");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(gamepad);
                InputSystem.settings.backgroundBehavior = previousBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
                if (previousKeyboard != null && previousKeyboard.added)
                {
                    previousKeyboard.MakeCurrent();
                }

                if (previousGamepad != null && previousGamepad.added)
                {
                    previousGamepad.MakeCurrent();
                }
            }

            // Teleport next to the nearest normal pack so the 18 m aggro radius engages it. Nearest, because the
            // character's motor rejects an up vector that turns > 90° in one step (a far teleport is not gameplay).
            float3 target = float3.zero;
            float best = -2f;
            foreach (RunSpawn s in session.SpawnsForTests)
            {
                float d = math.dot(s.Direction, session.LandingDirectionForTests);
                if (s.Role == SpawnRole.Normal && d > best)
                {
                    best = d;
                    target = s.Direction;
                }
            }

            session.MovePlayerForTests(target);

            bool hit = false;
            deadline = Time.realtimeSinceStartup + 60f;
            while (!hit && outcome == null && Time.realtimeSinceStartup < deadline)
            {
                foreach (RunSession.UnitSnapshot u in session.UnitsForTests)
                {
                    hit |= u.Fielded && u.Hp < u.MaxHp;
                }

                yield return null;
            }

            Assert.That(hit, Is.True, "some fielded creature took damage within 60 s");

            // P3-09 acceptance: scatter exists around the player and nothing floats or sinks into the ground.
            var scatter = session.GetComponent<ScatterField>();
            Assert.That(scatter, Is.Not.Null, "scatter component is attached");
            Assert.That(scatter.LiveCount, Is.GreaterThan(0), "some scatter near the player");
            foreach (Transform root in scatter.transform)
            {
                float3 p = (float3)root.position;
                float3 dir = session.DirectionForTests(p);
                Assert.That(session.AltitudeForTests(p), Is.EqualTo(session.SurfaceHeightForTests(dir) - 0.1f).Within(0.15f),
                    "scatter root sits on the ground: " + root.name);
            }

            // P1-08 star shell: attached, centred on the camera, opacity in range.
            var stars = session.GetComponent<StarField>();
            Assert.That(stars, Is.Not.Null, "star field is attached");
            Assert.That(stars.Visibility, Is.InRange(0f, 1f));
            // Relative bound: -nographics never fires beginCameraRendering, so only the LateUpdate placement (one
            // Cinemachine frame behind) is observable here; 1e-3 of the shell radius is far below one pixel of parallax.
            Assert.That(Vector3.Distance(stars.Shell.position, session.CameraPositionForTests),
                Is.LessThan(stars.Shell.localScale.x * 1e-3f), "star shell follows the camera");

            if (System.Environment.GetEnvironmentVariable("COSMIMERA_CAPTURE") == "1")
            {
                // Visual proof for reviewers (needs a GPU: run without -nographics). Waits for the LOD to settle first.
                deadline = Time.realtimeSinceStartup + 20f;
                while (!session.TerrainSettledForTests && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                for (int f = 0; f < 30; f++)
                {
                    yield return null;
                }

                session.CaptureFightForTests(System.IO.Path.Combine(Application.dataPath, "..", "Logs", "demo-shot.png"));
            }

            Object.Destroy(session.gameObject);
            yield return null;
        }

        private static IEnumerator Press(Gamepad gamepad, GamepadState state)
        {
            InputSystem.QueueStateEvent(gamepad, state);
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            yield return null;
        }

    }
}
#endif
