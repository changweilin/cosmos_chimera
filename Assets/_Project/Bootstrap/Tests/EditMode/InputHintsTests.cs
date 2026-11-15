using Cosmimera.Bootstrap.Demo;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class InputHintsTests : InputTestFixture
    {
        private InputActionAsset _actions;
        private InputAction _journal;
        private InputHints _hints;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private Gamepad _pad;

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _pad = InputSystem.AddDevice<Gamepad>();
            var shared = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/_Project/Settings/Input/InputSystem_Actions.inputactions");
            _actions = InputActionAsset.FromJson(shared.ToJson());
            _journal = _actions.FindAction("DemoGlobal/Journal");
            _hints = new InputHints(_actions.FindActionMap("DemoGlobal"), _actions.FindActionMap("Player"),
                _actions.FindActionMap("DemoRun"), _actions.FindActionMap("DemoMenu"));
            _actions.FindActionMap("Player").Enable();
            _actions.FindActionMap("DemoRun").Enable();
            _actions.FindActionMap("DemoGlobal").Enable();
            InputSystem.Update();
        }

        [TearDown]
        public override void TearDown()
        {
            try
            {
                _hints?.Dispose();
                if (_actions != null) Object.DestroyImmediate(_actions);
            }
            finally { base.TearDown(); }
        }

        [Test]
        public void Connected_pad_does_not_override_keyboard_until_an_owned_action_is_used()
        {
            Assert.That(_hints.UsesGamepad, Is.False);
            Assert.That(_hints.BindingLabel(_actions.FindAction("DemoRun/Capture")), Is.EqualTo("C"));
            using (var unrelated = new InputAction("Other", InputActionType.Button, "<Gamepad>/select"))
            {
                unrelated.Enable();
                InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.Select));
                InputSystem.Update();
                Assert.That(_hints.UsesGamepad, Is.False);
            }
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.North));
            InputSystem.Update();
            Assert.That(_hints.UsesGamepad, Is.True);
            Assert.That(_hints.BindingLabel(_actions.FindAction("DemoRun/Capture")),
                Is.EqualTo(_actions.FindAction("DemoRun/Capture").GetBindingDisplayString(group: "Gamepad")));
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.B));
            InputSystem.Update();
            Assert.That(_hints.UsesGamepad, Is.False, "keyboard commands win while a pad stays connected");
        }

        [Test]
        public void Movement_look_and_view_toggle_change_hints_without_stick_drift_or_releases_stealing_them()
        {
            InputSystem.QueueStateEvent(_pad, new GamepadState { leftStick = Vector2.right });
            InputSystem.Update();
            Assert.That(_hints.UsesGamepad, Is.True);
            InputSystem.QueueStateEvent(_mouse, new MouseState { delta = new Vector2(10, 0) });
            InputSystem.Update();
            Assert.That(_hints.UsesGamepad, Is.False, "mouse look changes prompts without a keyboard press");
            InputSystem.QueueStateEvent(_pad, new GamepadState { leftStick = new Vector2(0.99f, 0) });
            InputSystem.Update();
            Assert.That(_hints.UsesGamepad, Is.False, "jitter on an already deflected stick is not new intent");
            InputSystem.QueueStateEvent(_pad, new GamepadState { leftStick = new Vector2(0.01f, 0) });
            InputSystem.Update();
            Assert.That(_hints.UsesGamepad, Is.False, "stick drift is filtered before selecting a device");
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.RightStick));
            InputSystem.Update();
            Assert.That(_hints.UsesGamepad, Is.True);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.V));
            InputSystem.Update();
            Assert.That(_hints.UsesGamepad, Is.False, "view toggle uses the shared action map");
            InputSystem.QueueStateEvent(_pad, new GamepadState());
            InputSystem.Update();
            Assert.That(_hints.UsesGamepad, Is.False, "releasing the pad cannot steal keyboard prompts");
        }

        [Test]
        public void Journal_and_menu_keep_tracking_when_exploration_actions_are_disabled()
        {
            _actions.FindActionMap("Player").Disable();
            _actions.FindActionMap("DemoRun").Disable();
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.DpadUp));
            InputSystem.Update();
            Assert.That(_hints.UsesGamepad, Is.True);
            Assert.That(_hints.BindingLabel(_journal), Is.EqualTo(_journal.GetBindingDisplayString(group: "Gamepad")));
            _actions.FindActionMap("DemoMenu").Enable();
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.DownArrow));
            InputSystem.Update();
            Assert.That(_hints.UsesGamepad, Is.False);
            Assert.That(_hints.BindingLabel(_journal), Is.EqualTo("I"));
        }

        [Test]
        public void Reconnecting_a_pad_requires_new_input_and_disposal_removes_callbacks()
        {
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.North));
            InputSystem.Update();
            Assert.That(_hints.UsesGamepad, Is.True);
            InputSystem.RemoveDevice(_pad);
            Assert.That(_hints.UsesGamepad, Is.False);
            InputSystem.AddDevice(_pad);
            Assert.That(_hints.UsesGamepad, Is.False, "reconnection alone is not player activity");
            InputSystem.QueueStateEvent(_pad, new GamepadState());
            InputSystem.Update();
            _hints.Dispose();
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.North));
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.I));
            InputSystem.Update();
            Assert.That(_hints.UsesGamepad, Is.False, "disposed sessions cannot respond to either map or journal input");
        }

        [Test]
        public void Every_used_action_has_both_device_groups_and_jump_roll_view_overload_are_separate()
        {
            foreach (InputActionMap map in new[] { _actions.FindActionMap("DemoRun"), _actions.FindActionMap("DemoMenu"),
                         _actions.FindActionMap("DemoGlobal") })
                foreach (InputAction action in map.actions)
                {
                    Assert.That(action.GetBindingDisplayString(group: "Keyboard&Mouse"), Is.Not.Empty, action.ToString());
                    Assert.That(action.GetBindingDisplayString(group: "Gamepad"), Is.Not.Empty, action.ToString());
                }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Space));
            InputSystem.Update();
            Assert.That(_actions.FindAction("DemoRun/Roll").WasPressedThisFrame(), Is.True);
            Assert.That(_actions.FindAction("Player/Jump").WasPressedThisFrame(), Is.False);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.J));
            InputSystem.Update();
            Assert.That(_actions.FindAction("Player/Jump").WasPressedThisFrame(), Is.True);
            Assert.That(_actions.FindAction("DemoRun/Roll").WasPressedThisFrame(), Is.False);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.V));
            InputSystem.Update();
            Assert.That(_actions.FindAction("Player/ToggleView").WasPressedThisFrame(), Is.True);
            Assert.That(_actions.FindAction("DemoRun/Overload").WasPressedThisFrame(), Is.False);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.O));
            InputSystem.Update();
            Assert.That(_actions.FindAction("DemoRun/Overload").WasPressedThisFrame(), Is.True);
            Assert.That(_actions.FindAction("Player/ToggleView").WasPressedThisFrame(), Is.False);

            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.West));
            InputSystem.Update();
            Assert.That(_actions.FindAction("DemoRun/Roll").WasPressedThisFrame(), Is.True);
            Assert.That(_actions.FindAction("Player/Jump").WasPressedThisFrame(), Is.False);
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.North));
            InputSystem.Update();
            Assert.That(_actions.FindAction("Player/Jump").WasPressedThisFrame(), Is.True);
            Assert.That(_actions.FindAction("DemoRun/Roll").WasPressedThisFrame(), Is.False);
        }

        [Test]
        public void Screen_transitions_preserve_the_device_and_native_menu_clicks_switch_to_mouse()
        {
            InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(GamepadButton.North));
            InputSystem.Update();
            using (var next = new InputHints(_actions.FindActionMap("DemoMenu")))
            {
                next.Inherit(_hints);
                Assert.That(next.UsesGamepad, Is.True, "landing must retain the controller used to choose a planet");
                _actions.FindActionMap("DemoMenu").Enable();
                InputSystem.QueueStateEvent(_mouse, new MouseState().WithButton(MouseButton.Left));
                InputSystem.Update();
                Assert.That(next.UsesGamepad, Is.False, "IMGUI clicks do not have an InputAction callback");
            }
        }
    }
}
