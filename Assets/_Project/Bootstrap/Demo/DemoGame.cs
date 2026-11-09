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
        [SerializeField] private Shader _terrainShader;
        [SerializeField] private Shader _toonShader;
        [SerializeField] private Shader _starShader;
        [SerializeField] private TextAsset[] _geoMeta;
        [SerializeField] private TextAsset[] _geoHeights;
        [SerializeField] private TextAsset[] _geoWater;

        private DemoCopy _copy;
        private CombatBalance _combatBalance;
        private Progression _progression;
        private MaterialDrops _materialDrops;
        private LobbySave _save;
        private string _savePath;
        private Screen _screen = Screen.Lobby;
        private readonly List<PlanetOption> _options = new List<PlanetOption>();
        private readonly List<CreatureDef> _starters = new List<CreatureDef>();
        private readonly List<CreatureDef> _wildPool = new List<CreatureDef>();
        private RunSession _run;
        private RunOutcome _outcome;
        private readonly List<string> _xpLines = new List<string>();
        private readonly List<int> _runPartyIds = new List<int>();
        private Pcg32 _menuRng;
        private GUIStyle _title;
        private GUIStyle _text;
        private Camera _lobbyCamera;
        private Vector2 _partyScroll;
        private string _partyMsg = string.Empty;
        private InputActionMap _menuInput;
        private readonly MenuFocus _menuFocus = new MenuFocus();
        private int _menuButtonCount;
        private bool _confirmPending;
        private bool _scrollToFocus;
        private bool _insidePartyScroll;
        private readonly List<Planet.TemplateSource> _templates = new List<Planet.TemplateSource>();
        private readonly List<IReadOnlyList<string>> _templateTags = new List<IReadOnlyList<string>>();

        private void Awake()
        {
            LoadData();
            if (TryRunReproProbe())
            {
                return;
            }

            var menu = _actions.FindActionMap("DemoMenu", true);
            _menuInput = menu.Clone();
            if (isActiveAndEnabled)
            {
                _menuInput.Enable();
            }

            _lobbyCamera = new GameObject("Lobby Camera").AddComponent<Camera>();
            _lobbyCamera.clearFlags = CameraClearFlags.SolidColor;
            _lobbyCamera.backgroundColor = new Color(0.02f, 0.03f, 0.08f);
        }

        private void LoadData()
        {
            RebuildCopy();
            _combatBalance = CombatBalance.FromJson(_balance.text);
            _progression = Progression.FromJson(_balance.text);
            _materialDrops = MaterialDrops.FromJson(_balance.text);
            _savePath = Path.Combine(Application.persistentDataPath, "lobby.json");
            _save = LobbySave.Load(_savePath, _progression,
                id => _database.TryGet(id, out CreatureDef d) ? d.Stage : (CreatureStage?)null);
            GeoTemplateLoader.Load(_geoMeta, _geoHeights, _geoWater, _templates, _templateTags);
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
        }

        private void RebuildCopy()
        {
            _copy = new DemoCopy(_language, _strings.text, _names.text, _affixes.text, _themes.text, _quests.text,
                _epithets.text);
        }

        private void OnEnable()
        {
            if (_screen != Screen.Running && _menuInput != null && !_menuInput.enabled)
            {
                _menuInput.Enable();
            }
        }

        private void OnDisable()
        {
            _menuInput?.Disable();
            _confirmPending = false;
        }

        private void OnDestroy() => _menuInput?.Dispose();

        private void Update()
        {
            if (_menuInput == null || !_menuInput.enabled)
            {
                return;
            }

            _menuFocus.SetCount(MenuButtonCount());
            if (_menuInput.FindAction("Up").WasPressedThisFrame() || _menuInput.FindAction("Left").WasPressedThisFrame())
            {
                _menuFocus.Move(-1);
                _scrollToFocus = true;
            }
            else if (_menuInput.FindAction("Down").WasPressedThisFrame() || _menuInput.FindAction("Right").WasPressedThisFrame())
            {
                _menuFocus.Move(1);
                _scrollToFocus = true;
            }

            if (_menuInput.FindAction("Cancel").WasPressedThisFrame())
            {
                if (_screen == Screen.StarSelect || _screen == Screen.Results)
                {
                    _screen = Screen.Lobby;
                    _menuFocus.SetCount(0);
                }
                _confirmPending = false;
            }
            else if (_menuInput.FindAction("Submit").WasPressedThisFrame())
            {
                if (_screen == Screen.Lobby && _menuFocus.Index == 0)
                {
                    CycleLanguage();
                }
                else
                {
                    _confirmPending = true;
                }
            }
        }

        private int MenuButtonCount()
        {
            if (_screen == Screen.Results) return 1;
            if (_screen == Screen.StarSelect) return _options.Count + 1;
            if (_save.StarterLine == null) return 1 + _starters.Count;
            int count = 1 + _save.Party.Count + System.Math.Max(0, _save.Party.Count - 1) + 2;
            for (int i = 0; i < _save.Owned.Count; i++)
            {
                bool inParty = false;
                for (int j = 0; j < _save.Party.Count; j++)
                {
                    if (_save.Party[j] == _save.Owned[i].InstanceId)
                    {
                        inParty = true;
                        break;
                    }
                }
                if (!inParty)
                {
                    count++;
                }
            }
            return count;
        }

        private void CycleLanguage()
        {
            _language = _language == "zh-TW" ? "en" : _language == "en" ? "ja" : "zh-TW";
            RebuildCopy();
            _partyMsg = string.Empty;
        }

        private bool MenuButton(string label, params GUILayoutOption[] options)
        {
            bool selectable = GUI.enabled;
            int index = selectable ? _menuButtonCount++ : -1;
            bool focused = selectable && index == _menuFocus.Index;
            Color oldColor = GUI.backgroundColor;
            if (focused)
            {
                GUI.backgroundColor = new Color(0.55f, 0.85f, 1f);
            }
            bool clicked = GUILayout.Button((focused ? "> " : "") + label, options);
            GUI.backgroundColor = oldColor;
            if (focused && _insidePartyScroll && _scrollToFocus && Event.current.type == EventType.Repaint)
            {
                GUI.ScrollTo(GUILayoutUtility.GetLastRect());
            }
            bool confirmed = focused && _confirmPending && Event.current.type == EventType.Repaint;
            if (clicked || confirmed)
            {
                _confirmPending = false;
                return true;
            }
            return false;
        }

        // P3-10: `Cosmimera.exe -batchmode -reproProbe <file>` writes the probe report and quits, so the shipped player
        // binary (Mono or IL2CPP) is what gets measured, not the Editor.
        private bool TryRunReproProbe()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-reproProbe");
            if (i < 0)
            {
                return false;
            }

            if (i + 1 >= args.Length)
            {
                Debug.LogError("DemoGame: -reproProbe needs an output file path.");
                Application.Quit(2);
                return true;
            }

            // A batchmode player that throws out of Awake never quits; the caller would hang instead of seeing a failure.
            try
            {
                WriteReproReport(args[i + 1]);
                Application.Quit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Application.Quit(1);
            }

            return true;
        }

        // The sample dump sits next to the report as <file>.samples; tools/repro/compare.py reads it when present.
        private void WriteReproReport(string path)
        {
            var samples = new System.Text.StringBuilder();
            string report = ReproReport(samples);
            File.WriteAllText(path, report, new System.Text.UTF8Encoding(false));
            File.WriteAllText(path + ".samples", samples.ToString(), new System.Text.UTF8Encoding(false));
        }

        private string ReproReport(System.Text.StringBuilder samples)
        {
            var types = new List<PlanetTypeDef>();
            foreach (GameDef def in _database.All)
            {
                if (def is PlanetTypeDef t)
                {
                    types.Add(t);
                }
            }

#if ENABLE_IL2CPP
            const string backend = "il2cpp";
#else
            const string backend = "mono";
#endif
            string env = backend + " " + Application.platform + " editor=" + Application.isEditor + " cpu="
                + SystemInfo.processorType.Replace(' ', '_');
            var vocab = new WorldGen.RunPlanVocabulary(_copy.ThemeIds, _copy.GenericAffixIds, _copy.ChainIds);
            return ReproProbe.Report(types, _wildPool, vocab, _templates, _templateTags, RadiusFor, env, samples);
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
            _menuButtonCount = 0;
            _insidePartyScroll = false;
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)
            {
                _confirmPending = false;
            }
            Screen previousScreen = _screen;
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
            if (Event.current.type == EventType.Repaint)
            {
                _confirmPending = false;
                _scrollToFocus = false;
                if (_screen != previousScreen)
                {
                    _menuFocus.SetCount(0);
                }
                else
                {
                    _menuFocus.SetCount(_menuButtonCount);
                }
            }
        }

        private void DrawLobby()
        {
            GUILayout.Label(_copy.Ui("ui.lobby.title"), _title, GUILayout.Height(60));
            if (MenuButton(_copy.Ui("ui.lobby.language") + ": " + _language, GUILayout.Height(36)))
            {
                CycleLanguage();
                GUIUtility.ExitGUI();
            }
            if (_save.StarterLine == null)
            {
                GUILayout.Label(_copy.Ui("ui.lobby.choose_starter"), _text);
                foreach (CreatureDef s in _starters)
                {
                    if (MenuButton(_copy.Name(s.Id) + "  —  " + _copy.Ui("ui.lobby.starter_confirm"), GUILayout.Height(48)))
                    {
                        _save.ChooseStarter(s.LineId, s.Id, _progression.XpThreshold(_progression.StarterLevel));
                        _save.Write(_savePath);
                        GUIUtility.ExitGUI();
                    }
                }

                return;
            }

            DrawPartyEditor();
            DrawMaterials();
            GUILayout.Space(20);
            if (MenuButton(_copy.Ui("ui.lobby.start_run"), GUILayout.Height(56)))
            {
                RollOptions();
                _screen = Screen.StarSelect;
                _menuFocus.SetCount(0);
                GUIUtility.ExitGUI();
            }

            if (MenuButton(_copy.Ui("ui.lobby.quit"), GUILayout.Height(36)))
            {
                Application.Quit();
                GUIUtility.ExitGUI();
            }
        }

        // P4-07 minimal: order + add/remove only. Vessel/sub-hand pairing rules are not in D0 (vessel is shown, not enforced).
        private void DrawPartyEditor()
        {
            GUILayout.Label($"{_copy.Ui("ui.lobby.team")} ({_save.Party.Count}/{LobbySave.MaxParty})", _text);
            GUILayout.Label(_copy.Ui("ui.lobby.party_hint"), _text);
            _insidePartyScroll = true;
            _partyScroll = GUILayout.BeginScrollView(_partyScroll, GUILayout.Height(Mathf.Min(300, UnityEngine.Screen.height - 340)));
            // Snapshot: mutating the save while iterating Party would break the foreach.
            var party = new List<int>(_save.Party);
            for (int i = 0; i < party.Count; i++)
            {
                OwnedCreature o = _save.Get(party[i]);
                GUILayout.BeginHorizontal();
                GUILayout.Label((i == 0 ? "★ " : "· ") + PartyLabel(o), _text, GUILayout.Width(330));
                GUI.enabled = i > 0;
                if (MenuButton(_copy.Ui("ui.lobby.move_up"), GUILayout.Width(90)))
                {
                    (party[i - 1], party[i]) = (party[i], party[i - 1]);
                    CommitParty(party);
                    GUIUtility.ExitGUI();
                }

                GUI.enabled = true;
                EvolveButton(o);
                if (MenuButton(_copy.Ui("ui.lobby.remove_from_party"), GUILayout.Width(110)))
                {
                    if (party.Count <= 1)
                    {
                        _partyMsg = _copy.Ui("ui.lobby.party_min");
                    }
                    else
                    {
                        party.RemoveAt(i);
                        CommitParty(party);
                    }
                    GUIUtility.ExitGUI();
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Label(_copy.Ui("ui.lobby.reserve"), _text);
            foreach (OwnedCreature o in _save.Owned)
            {
                if (party.Contains(o.InstanceId))
                {
                    continue;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("· " + PartyLabel(o), _text, GUILayout.Width(330));
                EvolveButton(o);
                if (MenuButton(_copy.Ui("ui.lobby.add_to_party"), GUILayout.Width(110)))
                {
                    if (party.Count >= LobbySave.MaxParty)
                    {
                        _partyMsg = _copy.Ui("ui.lobby.party_full");
                    }
                    else
                    {
                        party.Add(o.InstanceId);
                        CommitParty(party);
                    }
                    GUIUtility.ExitGUI();
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            _insidePartyScroll = false;
            if (!string.IsNullOrEmpty(_partyMsg))
            {
                GUILayout.Label(_partyMsg, _text);
            }
        }

        // REQ: creatures.md#進化 — lobby only; level and every material shortfall are listed at once, nothing is consumed
        // on refusal (EvolutionRules). A final form, or a species missing from the database, gets no button.
        private void EvolveButton(OwnedCreature o)
        {
            if (!_database.TryGet(o.SpeciesId, out CreatureDef def))
            {
                return;
            }

            EvolutionRequirement req = EvolutionRequirement.FromDef(def);
            if (req.IsFinal)
            {
                return;
            }

            if (!MenuButton(_copy.Ui("ui.lobby.evolve"), GUILayout.Width(90)))
            {
                return;
            }

            EvolutionResult r = EvolutionRules.TryEvolve(o, req, _save.Materials, EvolutionVenue.Lobby, _progression);
            if (r.Succeeded)
            {
                _save.SetSpecies(o.InstanceId, r.Creature.SpeciesId);
                _save.Write(_savePath);
                _partyMsg = _copy.Ui("ui.lobby.evolve_success").Replace("{from}", _copy.Name(o.SpeciesId))
                    .Replace("{to}", _copy.Name(r.Creature.SpeciesId));
            }
            else
            {
                _partyMsg = DescribeRefusal(req, r.Check);
            }

            GUIUtility.ExitGUI();
        }

        private string DescribeRefusal(EvolutionRequirement req, EvolutionCheck check)
        {
            var needs = new List<string>();
            foreach (MaterialStack m in req.Materials)
            {
                needs.Add(_copy.Material(m.Key) + " ×" + m.Qty);
            }

            var lines = new List<string>
            {
                _copy.Ui("ui.lobby.evolve_requirement").Replace("{level}", req.RequiredLevel.ToString())
                    .Replace("{materials}", string.Join("  ", needs)),
            };
            if (check.LevelShortBy > 0)
            {
                lines.Add(_copy.Ui("ui.lobby.evolve_level_short").Replace("{level}", req.RequiredLevel.ToString())
                    .Replace("{missing}", check.LevelShortBy.ToString()));
            }

            foreach (MaterialShortfall s in check.Shortfalls)
            {
                lines.Add(_copy.Ui("ui.lobby.evolve_material_short").Replace("{material}", _copy.Material(s.Key))
                    .Replace("{have}", s.Have.ToString()).Replace("{need}", s.Need.ToString()));
            }

            return string.Join("\n", lines);
        }

        private void DrawMaterials()
        {
            GUILayout.Label(_copy.Ui("ui.lobby.materials"), _text);
            IReadOnlyList<MaterialStack> stock = _save.Materials.Snapshot();
            if (stock.Count == 0)
            {
                GUILayout.Label("· " + _copy.Ui("ui.lobby.materials_empty"), _text);
                return;
            }

            var parts = new List<string>(stock.Count);
            foreach (MaterialStack m in stock)
            {
                parts.Add(_copy.Material(m.Key) + " ×" + m.Qty);
            }

            GUILayout.Label("· " + string.Join("  ", parts), _text);
        }

        private string PartyLabel(OwnedCreature o) =>
            _copy.Name(o.SpeciesId) + " LV" + _progression.LevelOf(o.Exp) + "  (" + _copy.Ui("ui.vessel." + VesselKey(o.Vessel)) + ")";

        private void CommitParty(List<int> party)
        {
            _save.SetParty(party);
            _save.Write(_savePath);
            _partyMsg = string.Empty;
        }

        private void DrawStarSelect()
        {
            GUILayout.Label(_copy.Ui("ui.star_select.title"), _title, GUILayout.Height(60));
            foreach (PlanetOption o in _options)
            {
                string size = _copy.Ui("ui.star_select.size_" + o.Type.Size.ToString().ToLowerInvariant());
                var elementNames = new List<string>();
                foreach (Element e in o.Type.ElementBias)
                {
                    elementNames.Add(_copy.Element(e.ToString()));
                }

                string elements = string.Join(" / ", elementNames);
                GUILayout.Label($"{_copy.Ui("ui.star_select.planet_type")}：{_copy.Planet(o.Type.Id)}   {size} (R={o.Radius:0} m)\n" +
                                $"{_copy.Ui("ui.star_select.main_elements")}：{elements}   {_copy.Ui("ui.star_select.seed")}：{o.Seed}", _text);
                if (MenuButton(_copy.Ui("ui.star_select.launch"), GUILayout.Height(44)))
                {
                    Launch(o);
                    GUIUtility.ExitGUI();
                    return;
                }

                GUILayout.Space(12);
            }

            if (MenuButton(_copy.Ui("ui.star_select.reroll"), GUILayout.Height(36)))
            {
                RollOptions();
                GUIUtility.ExitGUI();
            }
        }

        private void DrawResults()
        {
            string title = _outcome.Victory ? _copy.Ui("ui.results.victory") : _copy.Ui("ui.results.defeat");
            GUILayout.Label(title, _title, GUILayout.Height(60));
            GUILayout.Label(_copy.Ui("ui.results.captured_list"), _text);
            foreach ((string speciesId, VesselKind vessel, int exp) in _outcome.Captured)
            {
                GUILayout.Label("· " + _copy.Name(speciesId) + " LV" + _progression.LevelOf(exp) + "  (" +
                    _copy.Ui("ui.vessel." + VesselKey(vessel)) + ")", _text);
            }

            GUILayout.Space(10);
            GUILayout.Label(_copy.Ui("ui.results.materials_list"), _text);
            if (_outcome.Materials.Count == 0)
            {
                GUILayout.Label("· " + _copy.Ui("ui.results.materials_none"), _text);
            }

            foreach (MaterialStack m in _outcome.Materials)
            {
                GUILayout.Label("· " + _copy.Material(m.Key) + " ×" + m.Qty, _text);
            }

            GUILayout.Space(10);
            GUILayout.Label(_copy.Ui("ui.lobby.team"), _text);
            foreach (string line in _xpLines)
            {
                GUILayout.Label("· " + line, _text);
            }

            GUILayout.Space(20);
            if (MenuButton(_copy.Ui("ui.results.return_lobby"), GUILayout.Height(52)))
            {
                _screen = Screen.Lobby;
                _menuFocus.SetCount(0);
                GUIUtility.ExitGUI();
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
        internal static float RadiusFor(PlanetSize size) => size == PlanetSize.Large ? 1280f : MoveGeometry.MinSupportedRadiusM;

        private void Launch(PlanetOption o)
        {
            var party = new List<CreatureDef>();
            var partyExp = new List<int>();
            _runPartyIds.Clear();
            foreach (int id in _save.Party)
            {
                if (_database.TryGet(_save.Get(id).SpeciesId, out CreatureDef def))
                {
                    party.Add(def);
                    partyExp.Add(_save.Get(id).Exp);
                    _runPartyIds.Add(id);
                }
            }

            if (party.Count == 0)
            {
                Debug.LogError("DemoGame: the party has no creature found in the GameDatabase.");
                return;
            }

            _menuInput.Disable();
            _confirmPending = false;
            _lobbyCamera.gameObject.SetActive(false);
            _run = new GameObject("Run").AddComponent<RunSession>();
            _run.Configure(_copy, _combatBalance, _actions, _terrainMaterial,
                new RunSetup
                {
                    PlanetType = o.Type, Seed = o.Seed, Radius = o.Radius, Party = party,
                    PartyIds = new List<int>(_runPartyIds), PartyExp = partyExp, Progression = _progression,
                    Materials = _materialDrops,
                    Templates = _templates, TemplateTags = _templateTags,
                }, _wildPool, OnRunEnded, _terrainShader, _toonShader, _starShader);
            _screen = Screen.Running;
        }

        private void OnRunEnded(RunOutcome outcome)
        {
            // Q-M4: the run itself is never saved; only what it brought back is written to the lobby save.
            // XP is applied before captures join, so a new capture never receives its own capture reward.
            _xpLines.Clear();
            for (int i = 0; i < _runPartyIds.Count; i++)
            {
                OwnedCreature o = _save.Get(_runPartyIds[i]);
                int after = outcome.PartyExp[i];
                _xpLines.Add(_copy.Name(o.SpeciesId) + " LV" + _progression.LevelOf(o.Exp) + " → LV" +
                    _progression.LevelOf(after) + "  (+" + (after - o.Exp) + " XP)");
                _save.SetExp(o.InstanceId, after);
            }

            foreach ((string speciesId, VesselKind vessel, int exp) in outcome.Captured)
            {
                _save.Add(speciesId, vessel, exp);
            }

            _save.AddMaterials(outcome.Materials);
            _save.Write(_savePath);
            _outcome = outcome;
            Destroy(_run.gameObject);
            _run = null;
            _lobbyCamera.gameObject.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _screen = Screen.Results;
            _menuFocus.SetCount(0);
            if (isActiveAndEnabled && !_menuInput.enabled)
            {
                _menuInput.Enable();
            }
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
            var meta = new List<TextAsset>();
            var heights = new List<TextAsset>();
            var water = new List<TextAsset>();
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:TextAsset", new[] { GeoTemplateLoader.Folder.TrimEnd('/') });
            var paths = new List<string>();
            foreach (string g in guids)
            {
                paths.Add(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
            }

            paths.Sort(System.StringComparer.Ordinal);
            foreach (string path in paths)
            {
                if (path.EndsWith(".meta.json"))
                {
                    string stem = path.Substring(0, path.Length - ".meta.json".Length);
                    meta.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(path));
                    heights.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(stem + ".height.bytes"));
                    water.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(stem + ".water.bytes"));
                }
            }

            game._terrainShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Art/Shaders/ToonTerrain.shader");
            game._toonShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Art/Shaders/ToonOutlined.shader");
            game._starShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Art/Shaders/Stars.shader");
            game._geoMeta = meta.ToArray();
            game._geoHeights = heights.ToArray();
            game._geoWater = water.ToArray();
            foreach (Object o in new Object[] { game._database, game._actions, game._terrainMaterial, game._balance,
                         game._strings, game._names, game._terrainShader, game._toonShader, game._starShader, game._affixes, game._themes, game._quests, game._epithets })
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

        /// <summary>Batchmode entry: <c>-executeMethod Cosmimera.Bootstrap.Demo.DemoGame.ReproProbeBatch -reproProbe &lt;file&gt;</c>.</summary>
        /// <remarks>
        /// Pre: batchmode; Demo.unity exists (CreateScene ran). Post: the Editor (Mono JIT) reference report and its
        ///      .samples dump are written.
        /// Side effects: opens Demo.unity; writes both files; exits the Editor with 0, or 1 on failure.
        /// </remarks>
        public static void ReproProbeBatch()
        {
            try
            {
                string[] args = System.Environment.GetCommandLineArgs();
                int i = System.Array.IndexOf(args, "-reproProbe");
                Core.Contracts.Contract.Requires(i >= 0 && i + 1 < args.Length, "-reproProbe <file> is required");
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/_Project/Bootstrap/Demo.unity");
                DemoGame game = Object.FindAnyObjectByType<DemoGame>(); // Editor tooling only; runtime never looks up.
                game.LoadData();
                game.WriteReproReport(args[i + 1]);
                UnityEditor.EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                UnityEditor.EditorApplication.Exit(1);
            }
        }

        /// <summary>Batchmode entry: <c>-executeMethod Cosmimera.Bootstrap.Demo.DemoGame.CreateSceneBatch</c>.</summary>
        /// <remarks>Pre: batchmode. Post: scene saved. Side effects: writes assets.</remarks>
        public static void CreateSceneBatch() => CreateScene();
#endif
    }
}
