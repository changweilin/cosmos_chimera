#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using Cosmimera.Bootstrap.Demo;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class AdventurePanelInputTests
    {
        [UnityTest]
        public IEnumerator Search_typing_keeps_focus_while_gamepad_navigation_exits_text_entry()
        {
            var copy = new DemoCopy("en", "{}", "{\"entries\":[]}", "[]", "[]", "{\"steps\":[],\"chains\":[]}", "[]");
            var panel = new AdventurePanel(copy);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var focus = (MenuFocus)typeof(AdventurePanel).GetField("_focus", flags).GetValue(panel);
            focus.SetCount(8);
            typeof(AdventurePanel).GetField("_editingText", flags).SetValue(panel, true);
            var previousBackground = InputSystem.settings.backgroundBehavior;
            var previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var pad = InputSystem.AddDevice<Gamepad>();
            var map = new InputActionMap("Journal text test");
            map.AddAction("Up", InputActionType.Button, "<Keyboard>/w");
            map.AddAction("Down", InputActionType.Button, "<Gamepad>/dpad/down");
            map.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
            map.Enable();
            try
            {
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                InputSystem.Update();
                Assert.That(map.FindAction("Up").WasPressedThisFrame(), Is.True);
                panel.ReadInput(map);
                Assert.That(focus.Index, Is.Zero, "typing W into search must not navigate");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
                InputSystem.Update();
                panel.ReadInput(map);
                Assert.That(typeof(AdventurePanel).GetField("_submit", flags).GetValue(panel), Is.False,
                    "Enter finishes editing without clicking the focused menu item");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadDown));
                InputSystem.Update();
                Assert.That(map.FindAction("Down").WasPressedThisFrame(), Is.True);
                Assert.DoesNotThrow(() => panel.ReadInput(map), "Update must not call GUI.FocusControl");
                Assert.That(focus.Index, Is.EqualTo(1));
            }
            finally
            {
                map.Dispose();
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(pad);
                InputSystem.settings.backgroundBehavior = previousBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
            }
        }
    }
}
#endif
