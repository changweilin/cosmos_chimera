using System.Collections.Generic;
using System.IO;
using Cosmimera.Combat;
using Cosmimera.Core.Random;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Save;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// D0 composition root: Earth lobby → star selection → planet run → results, looping.
    /// REQ: docs/roadmap/demo.md, docs/design/lobby-meta.md#循環.
    /// </summary>
    /// <remarks>
    /// Pre: serialized references assigned by <c>Cosmimera/Create Demo Scene</c>. Post: one RunSession at a time.
    /// Side effects: reads/writes the lobby save under Application.persistentDataPath.
    /// UI is IMGUI on purpose for D0: it is throwaway until the P4 UI pass, and IMGUI needs no prefab/asset plumbing.
    /// </remarks>
    public sealed class DemoGame : MonoBehaviour
    {
        private enum Screen { Lobby, StarSelect, Running, Results }

        private sealed class PlanetOption
        {
            public PlanetTypeDef Type;
            public ulong Seed;
            public float Radius;
        }

        [SerializeField] private GameDatabase _database;
        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private Material _terrainMaterial;
        [SerializeField] private TextAsset _balance;
        [SerializeField] private TextAsset _strings;
        [SerializeField] private TextAsset _names;
        [SerializeField] private TextAsset _affixes;
        [SerializeField] private TextAsset _themes;
        [SerializeField] private TextAsset _quests;
        [SerializeField] private TextAsset _epithets;
        [SerializeField] private string _language = "zh-TW";

        private DemoCopy _copy;
        private CombatBalance _combatBalance;
        private LobbySave _save;
        private string _savePath;
        private Screen _screen = Screen.Lobby;
        private readonly List<PlanetOption> _options = new List<PlanetOption>();
        private readonly List<CreatureDef> _starters = new List<CreatureDef>();
        private readonly List<CreatureDef> _wildPool = new List<CreatureDef>();
        private RunSession _run;
        private RunOutcome _outcome;
        private Pcg32 _menuRng;
        private GUIStyle _title;
        private GUIStyle _text;
        private Camera _lobbyCamera;

        private void Awake()
        {
            _copy = new DemoCopy(_language, _strings.text, _names.text, _affixes.text, _themes.text, _quests.text,
                _epithets.text);
            _combatBalance = CombatBalance.FromJson(_balance.text);
            _savePath = Path.Combine(Application.persistentDataPath, "lobby.json");
            _save = LobbySave.Load(_savePath);
            _menuRng = new Pcg32((ulong)System.DateTime.UtcNow.Ticks, 'M'); // menu rolls only, never generation

            foreach (GameDef def in _database.All)
            {
                if (def is CreatureDef c)
                {
                    if (c.LineId.StartsWith("starter_"))
                    {
                        if (c.Stage == CreatureStage.Juvenile)
                        {
                            _starters.Add(c);
                        }
                    }
                    else
                    {
                        _wildPool.Add(c);
                    }
                }
            }

            _lobbyCamera = new GameObject("Lobby Camera").AddComponent<Camera>();
            _lobbyCamera.clearFlags = CameraClearFlags.SolidColor;
            _lobbyCamera.backgroundColor = new Color(0.02f, 0.03f, 0.08f);
        }

        private void OnGUI()
        {
            if (_screen == Screen.Running)
            {
                return;
            }

            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _text ??= new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            var area = new Rect(UnityEngine.Screen.width / 2f - 320, 60, 640, UnityEngine.Screen.height - 120);
            GUILayout.BeginArea(area);
            switch (_screen)
            {
                case Screen.Lobby:
                    DrawLobby();
                    break;
                case Screen.StarSelect:
                    DrawStarSelect();
                    break;
                case Screen.Results:
                    DrawResults();
                    break;
            }

            GUILayout.EndArea();
        }

        private void DrawLobby()
        {
            GUILayout.Label(_copy.Ui("ui.lobby.title"), _title, GUILayout.Height(60));
            if (_save.StarterLine == null)
            {
                GUILayout.Label(_copy.Ui("ui.lobby.choose_starter"), _text);
                foreach (CreatureDef s in _starters)
                {
                    if (GUILayout.Button(_copy.Name(s.Id) + "  —  " + _copy.Ui("ui.lobby.starter_confirm"), GUILayout.Height(48)))
                    {
                        _save.ChooseStarter(s.LineId, s.Id);
                        _save.Write(_savePath);
                    }
                }

                return;
            }

            GUILayout.Label(_copy.Ui("ui.lobby.team"), _text);
            foreach (int id in _save.Party)
            {
                OwnedCreature o = _save.Get(id);
                GUILayout.Label("· " + _copy.Name(o.SpeciesId) + "  (" + _copy.Ui("ui.vessel." + VesselKey(o.Vessel)) + ")", _text);
            }

            GUILayout.Space(20);
            if (GUILayout.Button(_copy.Ui("ui.lobby.start_run"), GUILayout.Height(56)))
            {
                RollOptions();
                _screen = Screen.StarSelect;
            }

            if (GUILayout.Button(_copy.Ui("ui.lobby.quit"), GUILayout.Height(36)))
            {
                Application.Quit();
            }
        }

        private void DrawStarSelect()
        {
            GUILayout.Label(_copy.Ui("ui.star_select.title"), _title, GUILayout.Height(60));
            foreach (PlanetOption o in _options)
            {
                string size = _copy.Ui("ui.star_select.size_" + o.Type.Size.ToString().ToLowerInvariant());
                string elements = string.Join(" / ", o.Type.ElementBias);
                GUILayout.Label($"{_copy.Ui("ui.star_select.planet_type")}：{o.Type.Id}   {size} (R={o.Radius:0} m)\n" +
                                $"{_copy.Ui("ui.star_select.main_elements")}：{elements}   {_copy.Ui("ui.star_select.seed")}：{o.Seed}", _text);
                if (GUILayout.Button(_copy.Ui("ui.star_select.launch"), GUILayout.Height(44)))
                {
                    Launch(o);
                    return;
                }

                GUILayout.Space(12);
            }

            if (GUILayout.Button(_copy.Ui("ui.star_select.reroll"), GUILayout.Height(36)))
            {
                RollOptions();
            }
        }

        private void DrawResults()
        {
            string title = _outcome.Victory ? _copy.Ui("ui.results.victory") : _copy.Ui("ui.results.defeat");
            GUILayout.Label(title, _title, GUILayout.Height(60));
            GUILayout.Label(_copy.Ui("ui.results.captured_list"), _text);
            foreach ((string speciesId, VesselKind vessel) in _outcome.Captured)
            {
                GUILayout.Label("· " + _copy.Name(speciesId) + "  (" + _copy.Ui("ui.vessel." + VesselKey(vessel)) + ")", _text);
            }

            GUILayout.Space(20);
            if (GUILayout.Button(_copy.Ui("ui.results.return_lobby"), GUILayout.Height(52)))
            {
                _screen = Screen.Lobby;
            }
        }

        private void RollOptions()
        {
            _options.Clear();
            var types = new List<PlanetTypeDef>();
            foreach (GameDef def in _database.All)
            {
                if (def is PlanetTypeDef t)
                {
                    types.Add(t);
                }
            }

            for (int i = 0; i < 3 && types.Count > 0; i++)
            {
                PlanetTypeDef t = types[(int)_menuRng.NextUInt((uint)types.Count)];
                _options.Add(new PlanetOption { Type = t, Seed = _menuRng.NextUInt(), Radius = RadiusFor(t.Size) });
            }
        }

        // REQ: planet-world.md#星球尺度 says small = 320 m, but combat geometry is approved only for R >= 640
        // (MoveGeometry.MinSupportedRadiusM). D0 plays small planets at 640 m; tracked as open question Q-D1.
        private static float RadiusFor(PlanetSize size) => size == PlanetSize.Large ? 1280f : MoveGeometry.MinSupportedRadiusM;

        private void Launch(PlanetOption o)
        {
            var party = new List<CreatureDef>();
            foreach (int id in _save.Party)
            {
                if (_database.TryGet(_save.Get(id).SpeciesId, out CreatureDef def))
                {
                    party.Add(def);
                }
            }

            if (party.Count == 0)
            {
                Debug.LogError("DemoGame: the party has no creature found in the GameDatabase.");
                return;
            }

            _lobbyCamera.gameObject.SetActive(false);
            _run = new GameObject("Run").AddComponent<RunSession>();
            _run.Configure(_copy, _combatBalance, _actions, _terrainMaterial,
                new RunSetup { PlanetType = o.Type, Seed = o.Seed, Radius = o.Radius, Party = party }, _wildPool, OnRunEnded);
            _screen = Screen.Running;
        }

        private void OnRunEnded(RunOutcome outcome)
        {
            // Q-M4: the run itself is never saved; only what it brought back is written to the lobby save.
            foreach ((string speciesId, VesselKind vessel) in outcome.Captured)
            {
                _save.Add(speciesId, vessel);
            }

            _save.Write(_savePath);
            _outcome = outcome;
            Destroy(_run.gameObject);
            _run = null;
            _lobbyCamera.gameObject.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _screen = Screen.Results;
        }

        private static string VesselKey(VesselKind v) => v == VesselKind.TalismanCard ? "talisman_card"
            : v == VesselKind.GlassBead ? "glass_bead" : "magic_cube";

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Cosmimera/Create Demo Scene")]
        public static void CreateScene()
        {
            const string scenePath = "Assets/_Project/Bootstrap/Demo.unity";
            const string copy = "Assets/_Project/Data/Copy/";
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var game = new GameObject("DemoGame").AddComponent<DemoGame>();
            game._database = UnityEditor.AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Project/Data/GameDatabase.asset");
            game._actions = UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/_Project/Settings/Input/InputSystem_Actions.inputactions");
            game._terrainMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Bootstrap/SandboxTerrain.mat");
            game._balance = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(copy + "balance.json");
            game._strings = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(copy + "strings.json");
            game._names = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(copy + "names.json");
            game._affixes = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(copy + "affixes.json");
            game._themes = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(copy + "region-themes.json");
            game._quests = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(copy + "quest-templates.json");
            game._epithets = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(copy + "boss-epithets.json");
            foreach (Object o in new Object[] { game._database, game._actions, game._terrainMaterial, game._balance,
                         game._strings, game._names, game._affixes, game._themes, game._quests, game._epithets })
            {
                if (o == null)
                {
                    throw new System.InvalidOperationException("Create Demo Scene: a required asset is missing; run tools/demo-data/merge.py and the importer first.");
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
            var scenes = new List<UnityEditor.EditorBuildSettingsScene>(UnityEditor.EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == scenePath))
            {
                scenes.Insert(0, new UnityEditor.EditorBuildSettingsScene(scenePath, true));
                UnityEditor.EditorBuildSettings.scenes = scenes.ToArray();
            }

            UnityEditor.AssetDatabase.SaveAssets();
        }

        /// <summary>Batchmode entry: <c>-executeMethod Cosmimera.Bootstrap.Demo.DemoGame.CreateSceneBatch</c>.</summary>
        /// <remarks>Pre: batchmode. Post: scene saved. Side effects: writes assets.</remarks>
        public static void CreateSceneBatch() => CreateScene();
#endif
    }
}
