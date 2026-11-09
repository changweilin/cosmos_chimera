#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Save;
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

        // SOL r2 R2-03: Update sets the focus range from MenuButtonCount before input; if it is below the buttons the
        // lobby draws, a gamepad can never reach start-run. Evolvable creatures in party and reserve both add a button.
        [UnityTest]
        public IEnumerator Lobby_focus_budget_matches_drawn_buttons_with_evolvable_creatures()
        {
            SceneManager.LoadScene("Demo");
            yield return null;
            var game = Object.FindFirstObjectByType<DemoGame>();
            var starters = (List<CreatureDef>)Field(game, "_starters");
            Assert.That(starters.Count, Is.GreaterThanOrEqualTo(1));
            var save = new LobbySave();
            save.ChooseStarter(starters[0].LineId, starters[0].Id, 0);
            for (int i = 0; i < LobbySave.MaxParty + 1; i++)
            {
                save.Add(starters[i % starters.Count].Id, VesselKind.GlassBead, 0);
            }

            game.GetType().GetField("_save", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, save);
            yield return null;
            yield return null;
            int budget = (int)game.GetType().GetMethod("MenuButtonCount", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(game, null);
            // Batchmode -nographics never calls OnGUI, so the drawn count (_menuButtonCount) cannot be read here; the
            // expected value is the lobby layout: language + 6×(evolve, remove) + 5 enabled move-up (the first is
            // disabled, not selectable) + 2 reserves×(evolve, add) + start-run + quit. Before the fix this was 16.
            Assert.That(budget, Is.EqualTo(1 + 12 + 5 + 4 + 2));
            int drawn = (int)Field(game, "_menuButtonCount");
            if (drawn > 0)
            {
                Assert.That(budget, Is.EqualTo(drawn), "with a graphics device, budget equals what OnGUI drew");
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
