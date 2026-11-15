#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Cosmimera.Bootstrap.Demo;
using Cosmimera.Combat;
using Cosmimera.Creatures;
using Cosmimera.Food;
using Cosmimera.GameData;
using Cosmimera.Save;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Cosmimera.Bootstrap.Tests
{
    public sealed class JournalPlayTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static object Field(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
        private static string Copy(string name) => Load<TextAsset>("Assets/_Project/Data/Copy/" + name + ".json").text;
        private static CreatureDef Creature(GameDatabase db, string id)
        {
            Assert.That(db.TryGet(id, out CreatureDef creature), Is.True, id);
            return creature;
        }

        private static void Page(AdventurePanel panel, AdventurePanel.Page page) =>
            typeof(AdventurePanel).GetProperty("Current", Private).SetValue(panel, page);

        private static IEnumerator Capture(string name)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
            // Batchmode has no automatic Game view repaint or end-of-frame screenshot callback.
            var capture = Type.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands,Unity.Pipeline.Editor", true);
            var view = EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor", true));
            view.position = new Rect(0, 0, 1280, 760);
            capture.GetMethod("PrepareGameView", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            for (int i = 0; i < 3; i++) yield return null;
            capture.GetMethod("GameView", BindingFlags.Static | BindingFlags.Public).Invoke(null,
                new object[] { 1280, 720, null, Path.GetFullPath("Logs/journal-" + name + ".png"), false, 0, "screen", null });
        }

        [UnityTest]
        public IEnumerator Lobby_journal_draws_all_pages_and_preserves_pairing_validation()
        {
            SceneManager.LoadScene("Demo");
            yield return null;
            var game = Object.FindFirstObjectByType<DemoGame>();
            var save = new LobbySave();
            save.ChooseStarter("starter_sun", "sun_heliostat_heron", 0);
            save.Add("sun_mirror_chick", VesselKind.TalismanCard, 0);
            save.SetParty(new[] { 1 });
            save.SetPartners(new[] { new PartnerPair(1, 2) });
            Set(game, "_save", save);
            Invoke(game, "OpenJournal");
            var panel = (AdventurePanel)Field(game, "_journal");
            yield return Capture("partners");
            foreach (AdventurePanel.Page page in Enum.GetValues(typeof(AdventurePanel.Page)))
            {
                Page(panel, page);
                yield return null;
                yield return null;
            }
            yield return Capture("codex");
            Set(panel, "_scroll", new Vector2(0, 10000));
            yield return Capture("codex-detail");
            var args = new object[] { null };
            Assert.That(Invoke(game, "TryBuildPartners", args), Is.True);
            save.SetSpecies(1, "sun_mirror_chick");
            Assert.That(Invoke(game, "TryBuildPartners", args), Is.False, "evolution/form changes must not silently repair invalid pairs");
            Assert.That(save.Partners.Count, Is.EqualTo(1));
            Invoke(game, "CloseJournal");
            Assert.That(Field(game, "_journal"), Is.Null);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Field_journal_pauses_input_consumes_food_and_restores_on_disable()
        {
            SceneManager.LoadScene("Demo");
            yield return null;
            var game = Object.FindFirstObjectByType<DemoGame>();
            game.enabled = false;
            ((Camera)Field(game, "_lobbyCamera")).gameObject.SetActive(false);
            var db = (GameDatabase)Field(game, "_database");
            var copy = new DemoCopy("zh-TW", Copy("strings"), Copy("names"), Copy("affixes"), Copy("region-themes"), Copy("quest-templates"), Copy("boss-epithets"));
            var progression = Progression.FromJson(Copy("balance"));
            var wild = new List<CreatureDef>();
            PlanetTypeDef planet = null;
            foreach (GameDef def in db.All)
            {
                if (def is CreatureDef c && !c.LineId.StartsWith("starter_")) wild.Add(c);
                if (def is PlanetTypeDef p && p.Id == "verdant_planet") planet = p;
            }
            var owned = new List<OwnedCreature>
            {
                new OwnedCreature(1, "sun_heliostat_heron", VesselKind.TalismanCard, progression.XpThreshold(30)),
                new OwnedCreature(2, "wood_fern_chick", VesselKind.GlassBead, progression.XpThreshold(10)),
                new OwnedCreature(3, "fire_ember_cub", VesselKind.TalismanCard, progression.XpThreshold(10)),
                new OwnedCreature(4, "water_wave_elver", VesselKind.TalismanCard, progression.XpThreshold(10)),
                new OwnedCreature(5, "sun_mirror_chick", VesselKind.TalismanCard, 0),
            };
            var loadout = new PartnerLoadout(new[] { 1, 2, 3, 4 });
            Assert.That(loadout.TryAssign(owned[0], owned[4], new GameDatabaseRelationGraph(db), new GameDatabaseSpeciesLookup(db)), Is.EqualTo(PartnerResult.Ok));
            var session = new GameObject("Journal test run").AddComponent<RunSession>();
            var party = new List<CreatureDef>();
            var xp = new List<int>();
            for (int i = 0; i < 4; i++) { party.Add(Creature(db, owned[i].SpeciesId)); xp.Add(owned[i].Exp); }
            var previousBackground = InputSystem.settings.backgroundBehavior;
            var previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                session.Configure(copy, CombatBalance.FromJson(Copy("balance")),
                    Load<InputActionAsset>("Assets/_Project/Settings/Input/InputSystem_Actions.inputactions"),
                    Load<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat"),
                    new RunSetup
                    {
                        PlanetType = planet, Seed = 2026, Radius = 320, BossStage = CreatureStage.Juvenile,
                        Landforms = false, Party = party, PartyIds = new List<int> { 1, 2, 3, 4 }, PartyExp = xp,
                        Progression = progression, Materials = MaterialDrops.FromJson(Copy("balance")),
                        Database = db, Owned = owned, Partners = loadout,
                    }, wild, _ => { });
                float deadline = Time.realtimeSinceStartup + 60;
                while (!session.StartedForTests && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(session.StartedForTests, Is.True);
                var utilities = (HashSet<FieldUtility>)Field(session, "_utilities");
                Assert.That(utilities, Does.Contain(FieldUtility.CookFire));
                Assert.That(utilities, Does.Contain(FieldUtility.Fishing));
                Assert.That(utilities, Does.Contain(FieldUtility.ForageBerries));
                var encounter = (Encounter)Field(session, "_encounter");
                Assert.That(encounter.Units[session.PartyUnitsForTests[0]].Moves.Count, Is.EqualTo(2));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.I));
                yield return null;
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                Assert.That(Field(session, "_journal"), Is.Not.Null);
                Assert.That(Time.timeScale, Is.Zero);
                Assert.That(((InputActionMap)Field(session, "_runInput")).enabled, Is.False);
                long now = session.NowMsForTests;
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(session.NowMsForTests, Is.EqualTo(now), "menu browsing cannot advance combat");
                var inventory = (FoodInventory)Field(session, "_foodInventory");
                var catalog = (FoodCatalog)Field(session, "_foodCatalog");
                var vitals = (PlayerVitals)Field(session, "_vitals");
                vitals.ApplyDamage(vitals.MaxHp / 2, now);
                int hp = vitals.Hp;
                inventory.AddIngredient("ing_wild_berry", 2);
                Assert.That(catalog.TryGetIngredient("ing_wild_berry", out IngredientSpec berry), Is.True);
                Invoke(session, "EatIngredient", berry);
                Assert.That(vitals.Hp, Is.GreaterThan(hp));
                Assert.That(inventory.CountIngredient(berry.Id), Is.EqualTo(1));
                var panel = (AdventurePanel)Field(session, "_journal");
                Page(panel, AdventurePanel.Page.Items);
                yield return Capture("items");
                Page(panel, AdventurePanel.Page.Skills);
                yield return Capture("skills");
                session.enabled = false;
                Assert.That(Time.timeScale, Is.EqualTo(1));
                Assert.That(Field(session, "_journal"), Is.Null);
                Assert.That(((InputActionMap)Field(session, "_runInput")).enabled, Is.False);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(session.gameObject);
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.backgroundBehavior = previousBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
                Time.timeScale = 1;
                SceneManager.LoadScene("Demo");
            }
            yield return null;
        }
    }
}
#endif
