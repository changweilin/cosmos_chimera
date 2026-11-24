using System.Collections.Generic;
using System.IO;
using Cosmimera.Bootstrap.Demo.UI;
using Cosmimera.Combat;
using Cosmimera.Core.Random;
using Cosmimera.Crafting;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Lobby;
using Cosmimera.Save;
using Cosmimera.WorldGen;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>
    /// D0 composition root: Character Select → Cradle Moon village / Main World → Star Rift runs → Results, looping.
    /// REQ: docs/design/lobby-meta.md, docs/design/beginner-quest.md.
    /// </summary>
    /// <remarks>
    /// Pre: serialized references assigned by <c>Cosmimera/Create Demo Scene</c>. Post: one RunSession at a time.
    /// Side effects: reads/writes the persistent save under Application.persistentDataPath.
    /// Standalone lobby screen removed: persistent Meta services run inside the Main World and Adventure Handbook.
    /// </remarks>
    public sealed partial class DemoGame : MonoBehaviour
    {
        private enum Screen { CharacterSelect, Running, Results }

        private sealed class PlanetOption
        {
            public PlanetTypeDef Type;
            public ulong Seed;
            public float Radius;
            public CreatureStage BossStage;
            public WorldGen.PlanetElements Elements;
            public bool IsBeginnerQuest;
            public bool IsLobbyPlanet;
        }

        internal const ulong BeginnerQuestSeed = 20261117UL;

        [SerializeField] private GameDatabase _database;
        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private Material _terrainMaterial;
        [SerializeField] private TextAsset _balance;
        [SerializeField] private TextAsset _envMaterials;
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
        [SerializeField] private Shader _skyShader;
        [SerializeField] private Shader _grassShader;
        [SerializeField] private Shader _waterShader;
        [SerializeField] private Shader _sceneryShader;
        [SerializeField] private BgmTable _bgmTable;
        [SerializeField] private CharacterModelTable _characterModels;
        [SerializeField] private ConceptPortraitTable _portraits;
        [SerializeField] private VolumeProfile _volumeProfile;

        private int _selectedCharacterIndex;

        private DemoCopy _copy;
        private CombatBalance _combatBalance;
        private Progression _progression;
        private MaterialDrops _materialDrops;
        private int _weatherControlCooldownMs;
        private EnvMaterials _env;
        private LobbySave _save;
        private PlanetTypeDef _cradlePlanetCache;
        private string _savePath;
        private Screen _screen = Screen.CharacterSelect;
        private readonly List<CreatureDef> _starters = new List<CreatureDef>();
        private readonly List<CreatureDef> _wildPool = new List<CreatureDef>();
        private RunSession _run;
        private RunOutcome _outcome;
        private string _settleError;
        private readonly List<string> _xpLines = new List<string>();
        private string _playerXpLine = string.Empty;
        private readonly List<int> _runPartyIds = new List<int>();
        private Pcg32 _menuRng;
        private Camera _lobbyCamera;
        private BgmPlayer _bgm;
        private string _partyMsg = string.Empty;
        private InputActionMap _menuInput;
        private InputHints _menuHints;
        private bool _confirmPending;
        private GameOverlayView _overlay;
        private RunCheckpointSave _pendingResumeCheckpoint;
        private MainWorldServices _services;
        private int _riftRefreshEpoch;
        private readonly HashSet<int> _clearedRiftIndices = new HashSet<int>();
        private int _activeRiftIndex = -1;

        internal LobbySave LiveSave => _save;
        internal GameDatabase LiveDatabase => _database;
        internal Progression LiveProgression => _progression;
        internal CombatBalance LiveBalance => _combatBalance;
        internal CraftCatalog LiveCraftCatalog => CraftCatalog;
        internal DemoCopy LiveCopy => _copy;
        internal string LiveLanguage => _language;
        internal MainWorldServices Services => _services ??= new MainWorldServices(this);

        private void Awake()
        {
            LoadData();
            if (TryRunReproProbe())
            {
                return;
            }

            var menu = _actions.FindActionMap("DemoMenu", true);
            _menuInput = menu.Clone();
            _menuHints = new InputHints(_menuInput);
            if (isActiveAndEnabled)
            {
                _menuInput.Enable();
            }

            _lobbyCamera = new GameObject("Lobby Camera").AddComponent<Camera>();
            _lobbyCamera.clearFlags = CameraClearFlags.SolidColor;
            _lobbyCamera.backgroundColor = new Color(0.02f, 0.03f, 0.08f);
            if (_lobbyCamera.GetComponent<AudioListener>() == null)
            {
                _lobbyCamera.gameObject.AddComponent<AudioListener>();
            }

            var bgmGo = new GameObject("BgmPlayer");
            bgmGo.transform.SetParent(transform, false);
            _bgm = bgmGo.AddComponent<BgmPlayer>();
            _bgm.Initialize(_bgmTable);
            _bgm.Play(BgmResolver.Town);

            StartGameDirectly();
        }

        private void StartGameDirectly()
        {
            if (_save.ActiveCheckpoint != null)
            {
                LaunchFromCheckpoint(_save.ActiveCheckpoint);
                return;
            }

            if (!_save.TutorialDone)
            {
                // REQ: docs/design/beginner-quest.md#流程 — new save skips lobby and opens Character Select directly.
                EnterCharacterSelect();
                return;
            }

            if (_save.StarterLine == null && _starters.Count > 0)
            {
                CreatureDef s = _starters[0];
                LobbySave candidate = _save.Clone();
                int exp = _progression.XpThreshold(_progression.StarterLevel);
                if (s.Niches.Count > 0)
                {
                    WildIndividual ind = WildIndividual.Draw(s, null, WildIndividual.KeyFor(0x53545254UL, 1UL));
                    candidate.ChooseStarter(s.LineId, s.Id, exp, ind.Niche.Id, ind.Habit);
                }
                else
                {
                    candidate.ChooseStarter(s.LineId, s.Id, exp);
                }

                CommitLobby(candidate);
            }

            LaunchLobbyPlanet();
        }

        private void LoadData()
        {
            RebuildCopy();
            _combatBalance = CombatBalance.FromJson(_balance.text);
            BalanceIntegerGuard.RequireIntegers(_balance.text, "materials", "progression");
            _progression = Progression.FromJson(_balance.text);
            _materialDrops = MaterialDrops.FromJson(_balance.text);
            _weatherControlCooldownMs = BalanceIntegerGuard.ReadPositiveInteger(_balance.text,
                "field_utilities", "weather_control_cooldown_ms");
            _env = EnvMaterials.FromJson(_envMaterials.text);
            _savePath = Path.Combine(Application.persistentDataPath, "lobby.json");
            _save = LobbySave.Load(_savePath, _progression,
                id => _database.TryGet(id, out CreatureDef d) ? d.Stage : (CreatureStage?)null);
            if (LobbyTransactions.MigrateEcology(_save, id => _database.TryGet(id, out CreatureDef d) ? d : null,
                    _savePath, out LobbySave migrated, out System.Exception migrateError) == CommitStatus.Committed)
            {
                _save = migrated;
            }
            else if (migrateError != null)
            {
                Debug.LogException(migrateError);
            }

            if (!string.IsNullOrEmpty(_save.CharacterId))
            {
                SyncSelectedCharacterModel(_save.CharacterId);
            }

            _menuRng = new Pcg32((ulong)System.DateTime.UtcNow.Ticks, 'M');

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
            _overlay?.Hide();
            _menuInput?.Disable();
            _confirmPending = false;
        }

        private void OnDestroy()
        {
            _overlay?.Dispose();
            _overlay = null;
            _paperdollPreview?.Dispose();
            _paperdollPreview = null;
            _menuHints?.Dispose();
            _menuInput?.Dispose();
            if (_cradlePlanetCache != null)
            {
                Destroy(_cradlePlanetCache);
                _cradlePlanetCache = null;
            }
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
            {
                OpenDevTestSavesOverlay();
                return;
            }

            if (_menuInput == null || !_menuInput.enabled)
            {
                return;
            }

            if (_journal != null)
            {
                if (_menuInput.FindAction("Cancel").WasPressedThisFrame()) CloseJournal();
                else
                {
                    _journal.ReadInput(_menuInput);
                    _journal?.Draw(DrawJournal, CloseJournal);
                }
            }
        }

        private void CycleLanguage()
        {
            _language = _language == "zh-TW" ? "en" : _language == "en" ? "ja" : "zh-TW";
            RebuildCopy();
            _partyMsg = string.Empty;
        }

        internal DemoCopy CycleLanguageFromServices()
        {
            CycleLanguage();
            return _copy;
        }

        internal bool CommitFromServices(LobbySave candidate, out string error)
        {
            bool ok = CommitLobby(candidate);
            error = ok ? string.Empty : _partyMsg;
            if (ok)
            {
                SyncRunPartyIdsFromSave();
            }
            return ok;
        }

        internal void SyncRunPartyIdsFromSave()
        {
            if (_save == null) return;
            _runPartyIds.Clear();
            foreach (int id in _save.Party)
            {
                _runPartyIds.Add(id);
            }
        }

        internal List<PlanetTypeDef> BuildSelectablePlanetTypes()
        {
            var list = new List<PlanetTypeDef>();
            if (_database == null) return list;
            foreach (GameDef def in _database.All)
            {
                if (def is PlanetTypeDef t && t.Id != "cradle_planet")
                {
                    list.Add(t);
                }
            }
            return list;
        }

        internal PlanetTypeDef FindPlanetTypeById(string planetTypeId)
        {
            if (_database == null || string.IsNullOrEmpty(planetTypeId)) return null;
            foreach (GameDef def in _database.All)
            {
                if (def is PlanetTypeDef t && t.Id == planetTypeId)
                {
                    return t;
                }
            }
            return null;
        }

        internal bool TryPayStarSelectCostFromServices(PlanetSize size, Element mainElement, out string error)
        {
            MaterialStack cost = StarSelectRules.LaunchCost(size, mainElement);
            int held = _save.Materials.Count(cost.Key);
            if (!TryPayStarSelectCost(size, mainElement))
            {
                error = $"元素素材不足：需要 {_copy.Material(cost.Key)} ×{cost.Qty}（目前持有 {held}）。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        // P3-10: `Cosmimera.exe -batchmode -reproProbe <file>` writes the probe report and quits.
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
            return ReproProbe.Report(types, _wildPool, vocab, RadiusFor, env, samples);
        }

        // W-01: changes go to a clone first; the live save is replaced only after the file holds the clone.
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

        private bool HasEvolveButton(OwnedCreature o) =>
            _database.TryGet(o.SpeciesId, out CreatureDef def) && def.EvolvesTo != null;

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

        private string PartyLabel(OwnedCreature o) =>
            _copy.Name(o.SpeciesId) + " LV" + _progression.LevelOf(o.Exp) + "  (" + _copy.Ui("ui.vessel." + VesselKey(o.Vessel)) + ")";

        private bool CommitParty(List<int> party)
        {
            LobbySave candidate = _save.Clone();
            candidate.SetParty(party);
            return CommitLobby(candidate);
        }

        private string PlayerLevelLabel()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            int level = _progression.PlayerLevelOf(_save.PlayerExp);
            string next = level < _progression.MaxLevel ? _progression.PlayerXpThreshold(level + 1).ToString(inv) : "-";
            return _copy.Ui("ui.lobby.player_level").Replace("{level}", level.ToString(inv))
                .Replace("{exp}", _save.PlayerExp.ToString(inv)).Replace("{next}", next);
        }

        private bool TryPayStarSelectCost(PlanetSize size, Element mainElement)
        {
            if (!StarSelectRules.CanLaunch(_save.Materials, size, mainElement))
            {
                return false;
            }

            LobbySave candidate = _save.Clone();
            if (!StarSelectRules.TryPayLaunchCost(candidate.Materials, size, mainElement))
            {
                return false;
            }

            return CommitLobby(candidate);
        }

        private PlanetTypeDef ResolveCradlePlanetType()
        {
            if (_database != null)
            {
                foreach (GameDef def in _database.All)
                {
                    if (def is PlanetTypeDef t && t.Id == "cradle_planet")
                    {
                        return t;
                    }
                }
            }

            if (_cradlePlanetCache == null)
            {
                _cradlePlanetCache = ScriptableObject.CreateInstance<PlanetTypeDef>();
                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(GameDef).GetField("id", flags)?.SetValue(_cradlePlanetCache, "cradle_planet");
                typeof(PlanetTypeDef).GetField("terrainTags", flags)?.SetValue(
                    _cradlePlanetCache, new List<string> { "forest", "river_valley", "plateau" });
                typeof(PlanetTypeDef).GetField("elementBias", flags)?.SetValue(
                    _cradlePlanetCache, new List<Element> { Element.Wood, Element.Water, Element.Wind });
            }

            return _cradlePlanetCache;
        }

        private void LaunchCradleMoon()
        {
            PlanetTypeDef cradleType = ResolveCradlePlanetType();
            if (cradleType == null)
            {
                Debug.LogError("DemoGame: no PlanetTypeDef found for the cradle moon.");
                return;
            }

            Launch(new PlanetOption
            {
                Type = cradleType,
                Seed = BeginnerQuestSeed,
                Radius = RadiusFor(PlanetSize.Asteroid),
                BossStage = CreatureStage.Juvenile,
                Elements = WorldGen.PlanetElementDraw.Draw(BeginnerQuestSeed, PlanetSize.Asteroid, cradleType.ElementBias),
            });
        }

        private CreatureDef FindCreatureById(string id)
        {
            if (string.IsNullOrEmpty(id) || _database == null)
            {
                return null;
            }

            return _database.TryGet(id, out CreatureDef def) ? def : null;
        }

        private void LaunchLobbyPlanet()
        {
            PlanetTypeDef lobbyType = null;
            foreach (GameDef def in _database.All)
            {
                if (def is PlanetTypeDef t)
                {
                    if (t.Id == LobbyPlanet.PlanetTypeId)
                    {
                        lobbyType = t;
                        break;
                    }

                    lobbyType ??= t;
                }
            }

            if (lobbyType == null)
            {
                Debug.LogError("DemoGame: no PlanetTypeDef found for the lobby planet.");
                return;
            }

            _activeRiftIndex = -1;
            Launch(new PlanetOption
            {
                Type = lobbyType,
                Seed = LobbyPlanet.Seed,
                Radius = LobbyPlanet.RadiusM,
                BossStage = CreatureStage.Adult,
                Elements = WorldGen.PlanetElementDraw.Draw(LobbyPlanet.Seed, PlanetSize.Supergiant, lobbyType.ElementBias),
                IsLobbyPlanet = true,
            });
        }

        /// <summary>Builds the deterministic beginner-quest planet option (cradle asteroid, juvenile boss).</summary>
        /// <remarks>
        /// Pre: types is non-null and has at least one entry.
        /// Post: returns a juvenile-boss 320 m planet option preferring "cradle_planet", then "verdant_planet".
        /// Side effects: none.
        /// </remarks>
        internal static (PlanetTypeDef type, ulong seed, CreatureStage bossStage, float radius, WorldGen.PlanetElements elements)
            BuildBeginnerOption(IReadOnlyList<PlanetTypeDef> types)
        {
            Cosmimera.Core.Contracts.Contract.Requires(types != null && types.Count > 0, "types must be non-empty");
            PlanetTypeDef chosen = types[0];
            for (int i = 0; i < types.Count; i++)
            {
                if (types[i].Id == "cradle_planet")
                {
                    chosen = types[i];
                    break;
                }

                if (types[i].Id == "verdant_planet")
                {
                    chosen = types[i];
                }
            }

            const CreatureStage stage = CreatureStage.Juvenile;
            PlanetSize size = WorldGen.BossDraw.SizeFor(stage);
            return (chosen, BeginnerQuestSeed, stage, RadiusFor(size),
                WorldGen.PlanetElementDraw.Draw(BeginnerQuestSeed, size, chosen.ElementBias));
        }

        // REQ: planet-world.md#星球尺度
        internal static float RadiusFor(PlanetSize size)
        {
            switch (size)
            {
                case PlanetSize.Asteroid: return 320f;
                case PlanetSize.Moon: return 640f;
                case PlanetSize.Planet: return 960f;
                case PlanetSize.Supergiant: return 1280f;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(size), size, "undefined PlanetSize");
            }
        }

        private void Launch(PlanetOption o)
        {
            if (!TryBuildPartners(out PartnerLoadout loadout))
            {
                Debug.LogWarning("DemoGame: invalid sub-partner loadout detected on launch; opening journal to repair.");
                OpenJournalAt(AdventurePanel.Page.Partners);
                return;
            }

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

            _activeRunIsBeginnerQuest = o.IsBeginnerQuest;
            _menuInput.Disable();
            _confirmPending = false;
            _overlay?.Hide();
            _save.SetLastPlanet(new GearOrigin(o.Type.Id, o.Seed));
            RunCheckpointSave resumeCheckpoint = _pendingResumeCheckpoint;
            _pendingResumeCheckpoint = null;
            StartRunSession(new RunSetup
            {
                PlanetType = o.Type,
                Seed = o.Seed,
                Radius = o.Radius,
                BossStage = o.BossStage,
                Party = party,
                PlayerExp = _save.PlayerExp,
                Codex = _save.Codex,
                DiscoveredRecipes = _save.DiscoveredRecipesSnapshot(),
                PartyIds = new List<int>(_runPartyIds),
                PartyExp = partyExp,
                Progression = _progression,
                Materials = _materialDrops,
                EnvMaterials = _env,
                WeatherControlCooldownMs = _weatherControlCooldownMs,
                CharacterModelPrefab = SelectedCharacterPrefab(),
                Portraits = _portraits,
                VolumeProfile = _volumeProfile,
                Database = _database,
                Owned = new List<OwnedCreature>(_save.Owned),
                Partners = loadout,
                CraftStock = _save.CraftStock.Snapshot(),
                CraftLooks = _save.CraftStock.LookSnapshot(),
                Equipment = _save.Equipment.Snapshot(),
                TrialBadges = _save.CompletedTrialsSnapshot(),
                IsLobbyPlanet = o.IsLobbyPlanet,
                TutorialDone = _save.TutorialDone,
                FriendId = _save.FriendId,
                FriendStarter = FindCreatureById(_save.FriendStarterId),
                RivalId = _save.RivalId,
                RivalStarter = FindCreatureById(_save.RivalStarterId),
                ResumeCheckpoint = resumeCheckpoint,
                Services = Services,
                RiftRefreshEpoch = _riftRefreshEpoch,
                ClearedRiftIndices = new HashSet<int>(_clearedRiftIndices),
            });
        }

        private void StartRunSession(RunSetup setup)
        {
            if (_run != null)
            {
                _run.CheckpointTriggered -= OnCheckpointTriggered;
                Destroy(_run.gameObject);
                _run = null;
            }

            _lobbyCamera.gameObject.SetActive(false);
            _run = new GameObject("Run").AddComponent<RunSession>();
            _run.Configure(_copy, _combatBalance, _actions, _terrainMaterial, setup, _wildPool, OnRunEnded,
                _terrainShader, _toonShader, _starShader,
                new SceneryShaders(_skyShader, _grassShader, _waterShader, _sceneryShader));
            _run.CheckpointTriggered += OnCheckpointTriggered;
            _run.Hints.Inherit(_menuHints);
            _screen = Screen.Running;
        }

        private void OnRunEnded(RunOutcome outcome)
        {
            _xpLines.Clear();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            _playerXpLine = _copy.Ui("ui.results.player_exp")
                .Replace("{exp}", (outcome.PlayerExp - _save.PlayerExp).ToString(inv))
                .Replace("{level}", _progression.PlayerLevelOf(outcome.PlayerExp).ToString(inv));
            int count = System.Math.Min(_runPartyIds.Count, outcome.PartyExp.Count);
            for (int i = 0; i < count; i++)
            {
                OwnedCreature o = _save.Get(_runPartyIds[i]);
                int after = outcome.PartyExp[i];
                _xpLines.Add(_copy.Name(o.SpeciesId) + " LV" + _progression.LevelOf(o.Exp) + " → LV" +
                    _progression.LevelOf(after) + "  (+" + (after - o.Exp) + " XP)");
            }

            if (outcome.NewRiftRefreshEpoch != _riftRefreshEpoch)
            {
                _riftRefreshEpoch = outcome.NewRiftRefreshEpoch;
                _clearedRiftIndices.Clear();
            }

            if (_activeRiftIndex >= 0 && outcome.Victory && !outcome.Abandoned)
            {
                _clearedRiftIndices.Add(_activeRiftIndex);
            }
            _activeRiftIndex = -1;

            _outcome = outcome;
            _menuHints.Inherit(_run.Hints);
            Destroy(_run.gameObject);
            _run = null;
            _lobbyCamera.gameObject.SetActive(true);
            if (_bgm != null)
            {
                _bgm.Play(BgmResolver.Town);
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _screen = Screen.Results;
            if (isActiveAndEnabled && !_menuInput.enabled)
            {
                _menuInput.Enable();
            }

            if (_activeRunIsBeginnerQuest)
            {
                _activeRunIsBeginnerQuest = false;
                if (outcome.Victory)
                {
                    _save.CompleteTutorial(_bqCharacterId, _bqFriendId, _bqRivalId, _bqRivalStarterId, _bqFriendStarterId);
                    _save.AddGold(ShopAndRepairRules.QuestCompletionGold);
                    _save.Codex.Record(Codex.CodexEvent.Capture, _bqCharacterId);
                    TrySettle();
                    ShowResultsOverlay();
                    return;
                }

                // REQ: beginner-quest.md#失敗與中斷 — failed/abandoned tutorial resets cleanly without saving.
                _save = LobbySave.Load(_savePath, _progression,
                    id => _database.TryGet(id, out CreatureDef d) ? d.Stage : (CreatureStage?)null);
                EnterCharacterSelect();
                return;
            }

            TrySettle();
            if (_settleError == null && outcome.PendingStarRiftLaunch.HasValue && outcome.PendingStarRiftPlanetType != null)
            {
                StarRiftEntry rift = outcome.PendingStarRiftLaunch.Value;
                _activeRiftIndex = rift.RiftIndex;
                Launch(new PlanetOption
                {
                    Type = outcome.PendingStarRiftPlanetType,
                    Seed = rift.Seed,
                    Radius = RadiusFor(rift.Size),
                    BossStage = rift.BossStage,
                    Elements = rift.Elements,
                });
                return;
            }

            if (_settleError == null && outcome.TravelToCradleMoon)
            {
                LaunchCradleMoon();
                return;
            }

            if (_settleError == null && outcome.TravelToLobbyPlanet)
            {
                if (_save.ActiveCheckpoint != null && _save.ActiveCheckpoint.WorldKind == SaveWorldKind.RandomPlanet)
                {
                    LobbySave cleared = _save.Clone();
                    cleared.ClearCheckpoint();
                    CommitLobby(cleared);
                }
                LaunchLobbyPlanet();
                return;
            }

            ShowResultsOverlay();
        }

        private void OnCheckpointTriggered(RunCheckpointSave checkpoint)
        {
            if (checkpoint == null || _save == null || (_activeRunIsBeginnerQuest && _save.Party.Count == 0))
            {
                return;
            }

            LobbySave candidate = _save.Clone();
            candidate.SetCheckpoint(checkpoint);
            CommitLobby(candidate);
        }

        private void LaunchFromCheckpoint(RunCheckpointSave cp)
        {
            if (cp == null)
            {
                LaunchLobbyPlanet();
                return;
            }

            _pendingResumeCheckpoint = cp;
            if (cp.WorldKind == SaveWorldKind.CradlePlanet)
            {
                if (_save.TutorialDone)
                {
                    LaunchCradleMoon();
                }
                else
                {
                    if (_save.Party.Count == 0)
                    {
                        _pendingResumeCheckpoint = null;
                        EnterCharacterSelect();
                        return;
                    }

                    PlanetTypeDef cradleType = ResolveCradlePlanetType();
                    _activeRunIsBeginnerQuest = true;
                    _bqCharacterId = _save.CharacterId;
                    _bqFriendId = _save.FriendId;
                    _bqRivalId = _save.RivalId;
                    Launch(new PlanetOption
                    {
                        Type = cradleType,
                        Seed = cp.PlanetSeed,
                        Radius = RadiusFor(PlanetSize.Asteroid),
                        BossStage = CreatureStage.Juvenile,
                        Elements = WorldGen.PlanetElementDraw.Draw(cp.PlanetSeed, PlanetSize.Asteroid, cradleType.ElementBias),
                        IsBeginnerQuest = true,
                    });
                }
                return;
            }

            if (cp.WorldKind == SaveWorldKind.LobbyPlanet)
            {
                LaunchLobbyPlanet();
                return;
            }

            PlanetTypeDef randomType = null;
            foreach (GameDef def in _database.All)
            {
                if (def is PlanetTypeDef t && t.Id == cp.PlanetTypeId)
                {
                    randomType = t;
                    break;
                }
            }

            if (randomType == null)
            {
                _pendingResumeCheckpoint = null;
                LaunchLobbyPlanet();
                return;
            }

            Launch(new PlanetOption
            {
                Type = randomType,
                Seed = cp.PlanetSeed,
                Radius = RadiusFor(cp.PlanetSize),
                BossStage = cp.BossStage,
                Elements = WorldGen.PlanetElementDraw.Draw(cp.PlanetSeed, cp.PlanetSize, randomType.ElementBias),
            });
        }

        internal void OpenDevTestSavesOverlay()
        {
            _overlay ??= new GameOverlayView(transform);
            _overlay.Show("開發者遊戲測試存檔 (Dev)", "選擇關鍵劇情節點一鍵套用並啟動");
            foreach (DevTestSavePreset preset in DevTestSaves.All)
            {
                DevTestSavePreset captured = preset;
                _overlay.AddCard(captured.Label, captured.FileName, "載入此測試存檔", () =>
                {
                    ApplyDevTestSave(captured);
                }, primary: false);
            }

            _overlay.AddFooterButton("關閉", () => _overlay.Hide());
        }

        private void ApplyDevTestSave(DevTestSavePreset preset)
        {
            LobbySave built = preset.Build();
            if (!LobbyTransactions.TryWrite(built, _savePath, out System.Exception err))
            {
                Debug.LogException(err);
                return;
            }

            if (_journal != null)
            {
                CloseJournal();
            }

            _save = built;
            _riftRefreshEpoch = 0;
            _clearedRiftIndices.Clear();
            _activeRiftIndex = -1;
            if (!string.IsNullOrEmpty(_save.CharacterId))
            {
                SyncSelectedCharacterModel(_save.CharacterId);
            }

            _overlay?.Hide();
            if (_run != null)
            {
                Destroy(_run.gameObject);
                _run = null;
            }

            if (!_save.TutorialDone && _save.ActiveCheckpoint == null)
            {
                EnterCharacterSelect();
                return;
            }

            LaunchFromCheckpoint(_save.ActiveCheckpoint);
        }

        private void ShowResultsOverlay()
        {
            _overlay ??= new GameOverlayView(transform);
            string title = _outcome.Victory ? _copy.Ui("ui.results.victory") : _copy.Ui("ui.results.defeat");
            _overlay.Show(title, _playerXpLine);

            if (_outcome.Captured.Count > 0)
            {
                string capBody = string.Empty;
                foreach ((string speciesId, VesselKind vessel, int exp, _, _) in _outcome.Captured)
                {
                    capBody += $"· {_copy.Name(speciesId)} LV{_progression.LevelOf(exp)} ({_copy.Ui("ui.vessel." + VesselKey(vessel))})\n";
                }
                _overlay.AddCard(_copy.Ui("ui.results.captured_list"), capBody.TrimEnd());
            }

            string matBody = _outcome.Materials.Count == 0 ? _copy.Ui("ui.results.materials_none") : string.Empty;
            foreach (MaterialStack m in _outcome.Materials)
            {
                matBody += $"· {_copy.Material(m.Key)} ×{m.Qty}\n";
            }
            _overlay.AddCard(_copy.Ui("ui.results.materials_list"), matBody.TrimEnd());

            if (_xpLines.Count > 0)
            {
                _overlay.AddCard(_copy.Ui("ui.lobby.team"), string.Join("\n", _xpLines));
            }

            if (_settleError != null)
            {
                _overlay.AddCard(_copy.Ui("ui.results.retry"), _settleError, _copy.Ui("ui.results.retry"), () =>
                {
                    TrySettle();
                    ShowResultsOverlay();
                }, primary: false);
                return;
            }

            _overlay.AddFooterButton(_copy.Ui("ui.journal.open"), () =>
            {
                _overlay.Hide();
                OpenJournal();
            });
            if (_save.ActiveCheckpoint != null && _save.ActiveCheckpoint.WorldKind == SaveWorldKind.RandomPlanet)
            {
                _overlay.AddFooterButton("從最後存檔點重試", () =>
                {
                    _overlay.Hide();
                    LaunchFromCheckpoint(_save.ActiveCheckpoint);
                }, primary: false);
            }
            _overlay.AddFooterButton("返回主世界", () =>
            {
                if (_save.ActiveCheckpoint != null && _save.ActiveCheckpoint.WorldKind == SaveWorldKind.RandomPlanet)
                {
                    LobbySave cleared = _save.Clone();
                    cleared.ClearCheckpoint();
                    CommitLobby(cleared);
                }
                _overlay.Hide();
                LaunchLobbyPlanet();
            }, primary: true);
        }

        private void TrySettle()
        {
            CommitStatus status = LobbyTransactions.Settle(_save, _runPartyIds, _outcome, _progression, _savePath,
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

        private GameObject SelectedCharacterPrefab()
        {
            if (_characterModels == null || _selectedCharacterIndex < 0 ||
                _selectedCharacterIndex >= _characterModels.Entries.Count)
            {
                return null;
            }

            return _characterModels.Entries[_selectedCharacterIndex].prefab;
        }

        private static string VesselKey(VesselKind v) => v == VesselKind.TalismanCard ? "talisman_card"
            : v == VesselKind.GlassBead ? "glass_bead" : "magic_cube";

#if UNITY_EDITOR
        /// <summary>Creates and saves the Boot Demo scene with all serialized assets wired.</summary>
        /// <remarks>Pre: Editor mode with imported data assets present. Post: Demo.unity saved and added to build scenes. Side effects: writes scene and asset files.</remarks>
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
            game._envMaterials = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(copy + "env-materials.json");
            game._terrainShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Art/Shaders/ToonTerrain.shader");
            game._toonShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Art/Shaders/ToonOutlined.shader");
            game._starShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Art/Shaders/Stars.shader");
            game._skyShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Art/Shaders/ToonSky.shader");
            game._grassShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Art/Shaders/ToonGrass.shader");
            game._waterShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Art/Shaders/ToonWater.shader");
            game._sceneryShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Art/Shaders/ToonScenery.shader");
            game._bgmTable = UnityEditor.AssetDatabase.LoadAssetAtPath<BgmTable>("Assets/_Project/Audio/BgmTable.asset");
            game._characterModels = UnityEditor.AssetDatabase.LoadAssetAtPath<CharacterModelTable>(
                CharacterModelTableBuilder.TableAssetPath);
            game._portraits = ConceptPortraitTableBuilder.Build();
            game._volumeProfile = UnityEditor.AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                "Assets/_Project/Settings/Rendering/BootstrapVolumeProfile.asset")
                ?? UnityEditor.AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                    "Assets/_Project/Settings/Rendering/DefaultVolumeProfile.asset");
            foreach (Object o in new Object[] { game._database, game._actions, game._terrainMaterial, game._balance,
                         game._strings, game._names, game._terrainShader, game._toonShader, game._starShader, game._skyShader, game._grassShader, game._waterShader, game._sceneryShader, game._affixes, game._themes, game._quests, game._epithets, game._envMaterials })
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
        /// <remarks>Pre: batchmode; Demo.unity exists. Post: report and .samples written. Side effects: opens scene, writes files, exits Editor.</remarks>
        public static void ReproProbeBatch()
        {
            try
            {
                string[] args = System.Environment.GetCommandLineArgs();
                int i = System.Array.IndexOf(args, "-reproProbe");
                Core.Contracts.Contract.Requires(i >= 0 && i + 1 < args.Length, "-reproProbe <file> is required");
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/_Project/Bootstrap/Demo.unity");
                DemoGame game = Object.FindAnyObjectByType<DemoGame>();
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
