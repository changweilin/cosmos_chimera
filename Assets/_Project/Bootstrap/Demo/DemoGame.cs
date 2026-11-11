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
            public CreatureStage BossStage;
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
        // Non-null while the ended run's rewards are not yet in the lobby save (W-02): Results offers a retry and stays
        // open, because leaving would drop rewards the player was shown.
        private string _settleError;
        private readonly List<string> _xpLines = new List<string>();
        private string _playerXpLine = string.Empty;
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
            // Before the JsonUtility loaders below, which truncate/wrap non-integer numbers into int fields unseen.
            BalanceIntegerGuard.RequireIntegers(_balance.text, "materials", "progression");
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
                if (_screen == Screen.StarSelect || (_screen == Screen.Results && _settleError == null))
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
            if (_screen == Screen.Results) return 1;  // return-to-lobby, or retry while unsettled
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

                // Same predicate EvolveButton draws with: a budget short of the drawn buttons strands focus before
                // start-run on a gamepad (SOL r2 R2-03).
                if (HasEvolveButton(_save.Owned[i]))
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
                        LobbySave candidate = _save.Clone();
                        candidate.ChooseStarter(s.LineId, s.Id, _progression.XpThreshold(_progression.StarterLevel));
                        CommitLobby(candidate);
                        GUIUtility.ExitGUI();
                    }
                }

                // The party editor that normally shows _partyMsg is not drawn before a starter exists (SOL r2 R2-02).
                if (!string.IsNullOrEmpty(_partyMsg))
                {
                    GUILayout.Label(_partyMsg, _text);
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
            GUILayout.Label(PlayerLevelLabel(), _text);
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

        // W-01: lobby changes go to a clone first; the live save is replaced only after the file holds the clone.
        private bool CommitLobby(LobbySave candidate)
        {
            if (!LobbyTransactions.TryWrite(candidate, _savePath, out System.Exception error))
            {
                Debug.LogException(error);
                _partyMsg = _copy.Ui("ui.lobby.save_failed");
                return false;
            }

            _save = candidate;
            _partyMsg = string.Empty;
            return true;
        }

        // REQ: creatures.md#進化 — lobby only; level and every material shortfall are listed at once, nothing is consumed
        // on refusal (EvolutionRules). A final form, or a species missing from the database, gets no button.
        // Allocation-free (Update calls it through MenuButtonCount every frame, SOL r3 R3-01); FromDef is final exactly
        // when EvolvesTo is null, so this is the same predicate EvolveButton draws with.
        private bool HasEvolveButton(OwnedCreature o) =>
            _database.TryGet(o.SpeciesId, out CreatureDef def) && def.EvolvesTo != null;

        private void EvolveButton(OwnedCreature o)
        {
            if (!HasEvolveButton(o))
            {
                return;
            }

            _database.TryGet(o.SpeciesId, out CreatureDef def);
            EvolutionRequirement req = EvolutionRequirement.FromDef(def);

            if (!MenuButton(_copy.Ui("ui.lobby.evolve"), GUILayout.Width(90)))
            {
                return;
            }

            CommitStatus status = LobbyTransactions.Evolve(_save, o.InstanceId, req, _progression, _savePath,
                out LobbySave next, out EvolutionResult r, out System.Exception error);
            if (status == CommitStatus.Committed)
            {
                _save = next;
                _partyMsg = _copy.Ui("ui.lobby.evolve_success").Replace("{from}", _copy.Name(o.SpeciesId))
                    .Replace("{to}", _copy.Name(r.Creature.SpeciesId));
            }
            else if (status == CommitStatus.Refused)
            {
                _partyMsg = DescribeRefusal(req, r.Check);
            }
            else
            {
                Debug.LogException(error);
                _partyMsg = _copy.Ui("ui.lobby.save_failed");
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
            LobbySave candidate = _save.Clone();
            candidate.SetParty(party);
            CommitLobby(candidate);
        }

        private string PlayerLevelLabel()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            int level = _progression.PlayerLevelOf(_save.PlayerExp);
            string next = level < _progression.MaxLevel ? _progression.PlayerXpThreshold(level + 1).ToString(inv) : "-";
            return _copy.Ui("ui.lobby.player_level").Replace("{level}", level.ToString(inv))
                .Replace("{exp}", _save.PlayerExp.ToString(inv)).Replace("{next}", next);
        }

        private void DrawStarSelect()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            GUILayout.Label(_copy.Ui("ui.star_select.title"), _title, GUILayout.Height(60));
            GUILayout.Label(PlayerLevelLabel() + "\n" + _copy.Ui("ui.star_select.boss_unlock_hint")
                .Replace("{adolescent}", _progression.BossStageUnlockLevel(CreatureStage.Adolescent).ToString(inv))
                .Replace("{adult}", _progression.BossStageUnlockLevel(CreatureStage.Adult).ToString(inv)), _text);
            foreach (PlanetOption o in _options)
            {
                string size = _copy.Ui("ui.star_select.size_" + WorldGen.BossDraw.SizeFor(o.BossStage).ToString().ToLowerInvariant());
                string boss = _copy.Ui("ui.star_select.boss_stage")
                    .Replace("{stage}", _copy.Ui("ui.stage." + o.BossStage.ToString().ToLowerInvariant()));
                var elementNames = new List<string>();
                foreach (Element e in o.Type.ElementBias)
                {
                    elementNames.Add(_copy.Element(e.ToString()));
                }

                string elements = string.Join(" / ", elementNames);
                GUILayout.Label($"{_copy.Ui("ui.star_select.planet_type")}：{_copy.Planet(o.Type.Id)}   {boss}   {size} (R={o.Radius:0} m)\n" +
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
            GUILayout.Label(_playerXpLine, _text);
            GUILayout.Label(_copy.Ui("ui.lobby.team"), _text);
            foreach (string line in _xpLines)
            {
                GUILayout.Label("· " + line, _text);
            }

            GUILayout.Space(20);
            if (_settleError != null)
            {
                GUILayout.Label(_settleError, _text);
                if (MenuButton(_copy.Ui("ui.results.retry"), GUILayout.Height(52)))
                {
                    TrySettle();
                    GUIUtility.ExitGUI();
                }

                return;
            }

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

            // REQ: planet-world.md#boss-尺寸 — the boss stage is drawn first (capped by player level) and fixes the size.
            CreatureStage maxStage = _progression.MaxBossStage(_progression.PlayerLevelOf(_save.PlayerExp));
            for (int i = 0; i < 3 && types.Count > 0; i++)
            {
                PlanetTypeDef t = types[(int)_menuRng.NextUInt((uint)types.Count)];
                ulong seed = _menuRng.NextUInt();
                CreatureStage stage = WorldGen.BossDraw.Stage(seed, maxStage);
                _options.Add(new PlanetOption
                {
                    Type = t, Seed = seed, BossStage = stage, Radius = RadiusFor(WorldGen.BossDraw.SizeFor(stage)),
                });
            }
        }

        // REQ: planet-world.md#星球尺度 says asteroid = 320 m, but combat geometry is approved only for R >= 640
        // (MoveGeometry.MinSupportedRadiusM). D0 plays asteroids at 640 m; tracked as open question Q-D1.
        internal static float RadiusFor(PlanetSize size)
        {
            switch (size)
            {
                case PlanetSize.Planet: return 960f;
                case PlanetSize.Supergiant: return 1280f;
                default: return MoveGeometry.MinSupportedRadiusM; // Asteroid (Q-D1 clamp) and Moon
            }
        }

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
                    PlanetType = o.Type, Seed = o.Seed, Radius = o.Radius, BossStage = o.BossStage, Party = party,
                    PlayerExp = _save.PlayerExp,
                    PartyIds = new List<int>(_runPartyIds), PartyExp = partyExp, Progression = _progression,
                    Materials = _materialDrops,
                    Templates = _templates, TemplateTags = _templateTags,
                }, _wildPool, OnRunEnded, _terrainShader, _toonShader, _starShader);
            _screen = Screen.Running;
        }

        private void OnRunEnded(RunOutcome outcome)
        {
            // Q-M4: the run itself is never saved; only what it brought back is written to the lobby save.
            _xpLines.Clear();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            _playerXpLine = _copy.Ui("ui.results.player_exp")
                .Replace("{exp}", (outcome.PlayerExp - _save.PlayerExp).ToString(inv))
                .Replace("{level}", _progression.PlayerLevelOf(outcome.PlayerExp).ToString(inv));
            for (int i = 0; i < _runPartyIds.Count; i++)
            {
                OwnedCreature o = _save.Get(_runPartyIds[i]);
                int after = outcome.PartyExp[i];
                _xpLines.Add(_copy.Name(o.SpeciesId) + " LV" + _progression.LevelOf(o.Exp) + " → LV" +
                    _progression.LevelOf(after) + "  (+" + (after - o.Exp) + " XP)");
            }

            // The run is over whether or not banking succeeds (End is single-shot), so leave it first; a failed bank
            // keeps Results open with a retry instead of stranding the player in Running (W-02).
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

            TrySettle();
        }

        private void TrySettle()
        {
            CommitStatus status = LobbyTransactions.Settle(_save, _runPartyIds, _outcome, _savePath,
                out LobbySave next, out MaterialKey overflow, out System.Exception error);
            if (status == CommitStatus.Committed)
            {
                _save = next;
                _settleError = null;
                return;
            }

            if (status == CommitStatus.StockFull)
            {
                _settleError = _copy.Ui("ui.results.stock_full").Replace("{material}", _copy.Material(overflow));
                return;
            }

            Debug.LogException(error);
            _settleError = _copy.Ui("ui.results.save_failed");
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
