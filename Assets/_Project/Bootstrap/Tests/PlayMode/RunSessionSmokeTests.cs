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
            RunOutcome outcome = null;
            var session = new GameObject("Run").AddComponent<RunSession>();
            session.Configure(copy, balance, actions,
                Load<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat"),
                new RunSetup
                {
                    // Adolescent boss = medium planet (R=640, 4 landform stamps), the combination this smoke run always played.
                    PlanetType = type, Seed = 2026, Radius = 640f, BossStage = CreatureStage.Adolescent, Party = party,
                    PartyIds = Enumerable.Range(1, party.Count).ToList(),
                    PartyExp = Enumerable.Repeat(progression.XpThreshold(progression.StarterLevel), party.Count).ToList(),
                    Progression = progression, Materials = MaterialDrops.FromJson(Load<TextAsset>(Copy + "balance.json").text),
                    // The demo path: trees, rocks and buildings block creatures and can be hit (P3-12).
                    EnvMaterials = EnvMaterials.FromJson(Load<TextAsset>(Copy + "env-materials.json").text),
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

            // Teleport onto the nearest normal pack and press Focus, which provokes it whatever its temperament (D0-E2:
            // tier-0 juveniles are timid). Nearest, because the character's motor rejects an up vector that turns > 90°
            // in one step (a far teleport is not gameplay).
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
            yield return null;
            session.FocusNearestForTests();

            bool hit = false;
            deadline = Time.realtimeSinceStartup + 60f;
            while (!hit && outcome == null && Time.realtimeSinceStartup < deadline)
            {
                foreach (RunSession.UnitSnapshot u in session.UnitsForTests)
                {
                    hit |= u.Fielded && u.Hp < u.MaxHp;
                }

                // Q-C33: the character is a target too; damage to it proves the pack engaged just as well.
                hit |= session.PlayerHpForTests < session.PlayerMaxHpForTests;

                yield return null;
            }

            Assert.That(hit, Is.True, "some fielded creature or the character took damage within 60 s");

            // P3-09 acceptance: scatter exists around the player and nothing floats or sinks into the ground.
            var scatter = session.GetComponent<ScatterField>();
            Assert.That(scatter, Is.Not.Null, "scatter component is attached");
            Assert.That(scatter.LiveCount, Is.GreaterThan(0), "some scatter near the player");
            foreach (Transform root in scatter.transform)
            {
                if (!root.name.StartsWith("Scatter_"))
                {
                    continue; // the Settlements child (P3-11) shares this transform
                }

                float3 p = (float3)root.position;
                float3 dir = session.DirectionForTests(p);
                Assert.That(session.AltitudeForTests(p), Is.EqualTo(session.SurfaceHeightForTests(dir) - 0.1f).Within(0.15f),
                    "scatter root sits on the ground: " + root.name);
            }

            // P3-11: roads and buildings are drawn, and kept apart from scatter roots.
            var settlementView = session.GetComponentInChildren<SettlementView>();
            Assert.That(settlementView, Is.Not.Null, "settlement view is attached");
            Assert.That(settlementView.Roads.Count, Is.GreaterThan(0), "roads are drawn");
            Assert.That(settlementView.transform, Is.Not.EqualTo(scatter.transform), "settlements are not scatter roots");

            // P1-08 star shell: attached, centred on the camera, opacity in range.
            var stars = session.GetComponent<StarField>();
            Assert.That(stars, Is.Not.Null, "star field is attached");
            Assert.That(stars.Visibility, Is.InRange(0f, 1f));
            // The pack can hit within frames of the teleport above, while Cinemachine is still damping toward the
            // character tens of metres away; the shell trails by one frame, so let the camera settle before measuring.
            Vector3 lastCamera = session.CameraPositionForTests;
            deadline = Time.realtimeSinceStartup + 10f;
            do
            {
                lastCamera = session.CameraPositionForTests;
                yield return null;
            }
            while (Vector3.Distance(lastCamera, session.CameraPositionForTests) > stars.Shell.localScale.x * 2e-4f
                   && Time.realtimeSinceStartup < deadline);
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
