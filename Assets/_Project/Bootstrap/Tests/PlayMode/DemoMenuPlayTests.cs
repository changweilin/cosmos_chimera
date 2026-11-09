#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using Cosmimera.Bootstrap.Demo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class DemoMenuPlayTests
    {
        private static object Field(object obj, string name) => obj.GetType().GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj);

        [UnityTest]
        public IEnumerator Gamepad_moves_lobby_focus_and_confirms_language()
        {
            SceneManager.LoadScene("Demo");
            yield return null;
            var game = Object.FindFirstObjectByType<DemoGame>();
            Assert.That(game, Is.Not.Null);
            var focus = (MenuFocus)Field(game, "_menuFocus");
            var pad = InputSystem.AddDevice<Gamepad>();
            try
            {
                yield return null;
                Assert.That(focus.Index, Is.EqualTo(0));
                yield return Press(pad, GamepadButton.DpadDown);
                Assert.That(focus.Index, Is.EqualTo(1));
                yield return Press(pad, GamepadButton.DpadUp);
                Assert.That(focus.Index, Is.EqualTo(0));
                string before = (string)Field(game, "_language");
                yield return Press(pad, GamepadButton.South);
                Assert.That((string)Field(game, "_language"), Is.Not.EqualTo(before));
                var menu = (InputActionMap)Field(game, "_menuInput");
                Assert.That(menu.enabled, Is.True);
                game.enabled = false;
                Assert.That(menu.enabled, Is.False);
                game.enabled = true;
                Assert.That(menu.enabled, Is.True);
            }
            finally
            {
                InputSystem.RemoveDevice(pad);
            }
        }

        private static IEnumerator Press(Gamepad pad, GamepadButton button)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
        }
    }
}
#endif
