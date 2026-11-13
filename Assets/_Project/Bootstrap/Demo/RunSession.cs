using System;
using System.Collections.Generic;
using Cosmimera.Capture;
using Cosmimera.Combat;
using Cosmimera.Core.Random;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cosmimera.Bootstrap.Demo
{
    /// <summary>What a finished run hands back to the lobby (Q-M4: only captures and earned XP persist).</summary>
    /// <remarks>Pre: none. Post: Captured and PartyExp non-null. Side effects: none.</remarks>
    public sealed class RunOutcome
    {
        /// <summary>Boss knocked out.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool Victory;

        /// <summary>Player left mid-run.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool Abandoned;

        /// <summary>Captured (species id, vessel, cumulative XP = T(encounter level)) in capture order.</summary>
        /// <remarks>Pre: none. Post: non-null; exp &gt;= 0. Side effects: none.</remarks>
        public readonly List<(string speciesId, VesselKind vessel, int exp)> Captured = new List<(string, VesselKind, int)>();

        /// <summary>Cumulative XP of each party slot when the run ended (same order as RunSetup.Party).</summary>
        /// <remarks>Pre: none. Post: one entry per party slot, each &gt;= its starting value and &lt;= MaxExp. Side effects: none.</remarks>
        public readonly List<int> PartyExp = new List<int>();

        /// <summary>Cumulative player XP when the run ended (level-progression.md#玩家等級).</summary>
        /// <remarks>Pre: none. Post: &gt;= RunSetup.PlayerExp and &lt;= Progression.PlayerMaxExp. Side effects: none.</remarks>
        public int PlayerExp;

        /// <summary>Evolution materials dropped by wild knock-outs, in drop order (Q-C32 option B).</summary>
        /// <remarks>
        /// Pre: none. Post: non-null; at most one entry per spawn. Kept on victory, defeat and abandon alike, like
        ///      captures and XP (Q-M4: whatever the run earned comes back). Side effects: none.
        /// </remarks>
        public readonly List<MaterialStack> Materials = new List<MaterialStack>();
    }

    /// <summary>Everything a run needs from the lobby.</summary>
    /// <remarks>Pre: all fields set. Post: none. Side effects: none.</remarks>
    public sealed class RunSetup
    {
        /// <summary>Planet type.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public PlanetTypeDef PlanetType;

        /// <summary>Planet seed.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public ulong Seed;

        /// <summary>Radius (m).</summary>
        /// <remarks>Pre: >= MoveGeometry.MinSupportedRadiusM. Post: none. Side effects: none.</remarks>
        public float Radius;

        /// <summary>Whether procedural landform stamps (TemplatePlanner.Build) shape the terrain; false = noise only.</summary>
        /// <remarks>
        /// Pre: none. Post: none. Side effects: none.
        /// Tests that pin a plan built with isLand == null turn this off so the session builds exactly that plan.
        /// </remarks>
        public bool Landforms = true;

        /// <summary>
        /// Whether the surface is an enclosed dungeon (DungeonPlanner): walls on region borders, gates, a locked boss room.
        /// Default false: general planet surfaces are open procedural terrain; only the 10 elemental advanced terrains are dungeons.
        /// </summary>
        /// <remarks>
        /// Pre: none. Post: none. Side effects: none.
        /// </remarks>
        public bool Dungeon = false;

        /// <summary>Party forms in party order (≤ 6; the first 4 are the deploy pool, P2-06).</summary>
        /// <remarks>Pre: 1..6 entries. Post: none. Side effects: none.</remarks>
        public List<CreatureDef> Party;

        /// <summary>Persistent instance id of each party slot (XP rounding tie-break, level-progression.md).</summary>
        /// <remarks>Pre: same count as Party; unique, &gt;= 1. Post: none. Side effects: none.</remarks>
        public List<int> PartyIds;

        /// <summary>Cumulative XP of each party slot; its level scales the slot's stats for the whole run.</summary>
        /// <remarks>Pre: same count as Party; each &gt;= 0. Post: none. Side effects: none.</remarks>
        public List<int> PartyExp;

        /// <summary>Cumulative player XP at the start of the run.</summary>
        /// <remarks>Pre: 0..Progression.PlayerMaxExp. Post: none. Side effects: none.</remarks>
        public int PlayerExp;

        /// <summary>Boss stage drawn on the star card (BossDraw.Stage); decides planet size together with Radius.</summary>
        /// <remarks>Pre: Juvenile..Adult; the wild pool has a form of it. Post: none. Side effects: none.</remarks>
        public CreatureStage BossStage;

        /// <summary>Approved level curve and XP rewards (balance.json#progression).</summary>
        /// <remarks>Pre: non-null. Post: none. Side effects: none.</remarks>
        public Progression Progression;

        /// <summary>Approved evolution material drop rules (balance.json#materials).</summary>
        /// <remarks>Pre: non-null. Post: none. Side effects: none.</remarks>
        public MaterialDrops Materials;

        /// <summary>Destructible substances (Data/env-materials, P3-12); null = scene objects cannot be hit or block creatures.</summary>
        /// <remarks>
        /// Pre: when non-null, every scatter substance (ScatterPlanner.KindsFor) exists in it. Post: none. Side effects: none.
        /// Null exists for tests that pin creature movement recorded before trees and rocks blocked creatures.
        /// </remarks>
        public EnvMaterials EnvMaterials;

        /// <summary>Optional player character model prefab; null falls back to the placeholder capsule.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public GameObject CharacterModelPrefab;

        /// <summary>Optional 2D concept portrait table for HUD team slots; null omits portraits.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public ConceptPortraitTable Portraits;

        /// <summary>Optional URP VolumeProfile for post-processing during the run; null skips Volume setup.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public VolumeProfile VolumeProfile;
    }

    /// <summary>
    /// One planet run of the D0 demo: terrain + character (PlanetRig), wild spawns from RunPlanner, real-time combat
    /// through Encounter, capture, quest tracking and an IMGUI HUD. REQ: docs/roadmap/demo.md.
    /// </summary>
    /// <remarks>
    /// Pre: Configure once right after AddComponent. Post: calls the end callback exactly once.
    /// Side effects: owns every object it creates and destroys them in OnDestroy.
    /// Run actions are cloned per session so enabling or disposing a run never changes the shared input asset.
    /// </remarks>
    public sealed partial class RunSession : MonoBehaviour
    {
        private const int TickMs = 50;
        private const long KnockedOutWindowMs = 20000;
        private const float CaptureReachM = 8f;
        private const long AffixRefreshMs = 1000;

        // REQ: level-progression.md#獎勵量 — planet difficulty d is a proposed field absent from the data; every D0
        // planet type is calibrated at d = 1 (SOL rewards.json d0_evidence).
        private const int D0Difficulty = 1;

        private sealed class HeightSurface : IEncounterSurface
        {
            private readonly PlanetHeight _height;
            private readonly PlanetTemplates _templates;
            private readonly LayoutClearance _layout;
            private readonly PlanetWalls _walls;
            private readonly float _radius;

            public HeightSurface(PlanetHeight height, PlanetTemplates templates, LayoutClearance layout, PlanetWalls walls,
                float radius)
            {
                _height = height;
                _templates = templates;
                _layout = layout;
                _walls = walls;
                _radius = radius;
            }

            // REQ: planet-world.md#地表地牢 — true until the key elite is knocked out; locked gates then block like walls.
            public bool LocksClosed { get; set; } = true;

            // Set once the scatter planner exists (it needs GroundAltitude first); null = buildings only.
            public WorldObjects Objects { get; set; }

            // Same function the terrain mesh evaluates, so creatures stand on the rendered ground.
            public float GroundAltitude(float3 direction) =>
                (_templates != null ? _templates.Sample(_height, direction) : _height.Sample(direction)) +
                (_walls != null ? _walls.View.Height(math.normalize(direction)) : 0f);

            // Same predicate the planner uses to put spawns on land, so a wild never leaves the land it spawned on.
            public bool IsSea(float3 direction) => _templates != null && _templates.View.IsSea(math.normalize(direction));

            // Same footprints SettlementView gives colliders, so creatures stop at the walls the character bumps into.
            // Room walls (and locked gates) block the same way: the terrain stops the character, this stops creatures.
            public bool IsBlocked(float3 direction, float clearanceM) =>
                (Objects != null ? Objects.IsBlocked(direction, clearanceM) : _layout.IsInsideBuilding(direction, clearanceM)) ||
                (_walls != null && _walls.View.IsBlocked(math.normalize(direction), clearanceM / _radius, LocksClosed));

            // Scatter keeps off the whole wall band, gates included, so no tree or rock stands in a doorway.
            public bool IsOffWalls(float3 direction) =>
                _walls == null ||
                _walls.View.BorderDistance(math.normalize(direction)) > DungeonPlanner.WallHalfWidthM / _radius;
        }

        private sealed class WildInfo
        {
            public int Spawn;
            public int Level;
            public HashSet<int> AppearedAtKnockout;
            public long KnockedOutAtMs = -1;
            public bool Gone;
            public bool Dropped;
        }

        private DemoCopy _copy;
        private CombatBalance _balance;
        private InputActionAsset _actions;
        private InputActionMap _runInput;
        private InputAction _selectFirstAction;
        private InputAction _selectSecondAction;
        private InputAction _switchAction;
        private InputAction _focusAction;
        private InputAction _cycleTargetAction;
        private InputAction _freeAction;
        private InputAction _followAction;
        private InputAction _retreatAction;
        private InputAction _cycleVesselAction;
        private InputAction _captureAction;
        private InputAction _abandonAction;
        private RunSetup _setup;
        private Action<RunOutcome> _onEnd;

        private PlanetRig _rig;
        private RunPlan _plan;
        private RunLayout _layout;
        private DungeonLayout _dungeon;
        private HeightSurface _surface;
        private DungeonView _dungeonView;
        private QuestTracker _quest;
        private Encounter _encounter;
        private WorldObjects _worldObjects;
        private DestructibleTable _objectTable;
        private SettlementView _settlementView;
        private ScatterField _scatterField;
        private ObjectPhysics _objectPhysics;
        private EnvMaterials _envMaterials;
        private PlayerVitals _vitals;
        private TeamState _team;
        private readonly List<int> _partyUnits = new List<int>();
        private readonly HashSet<int> _appeared = new HashSet<int>();
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        private readonly List<int> _injectedFaints = new List<int>();
        /// <summary>Observes the boundary immediately before the real Encounter.Tick.</summary>
        /// <remarks>Pre: observer must not mutate the run. Post: called before each tick. Side effects: invokes the observer.</remarks>
        internal Action BeforeTickForTests;
        /// <summary>Synchronizes views for isolated view tests; per-frame wiring tests must use Unity Update.</summary>
        /// <remarks>Pre: Configure ran. Post: views reflect current units. Side effects: view transforms and materials.</remarks>
        internal void SyncViewsForTests() => SyncViews();
        internal void AbandonForTests() => RequestAbandon();
        internal bool EndedForTests => _ended;
#endif
        private Progression _progression;
        private int[] _partyExp;
        private int _playerExp;
        private bool[] _questPaid;
        private bool _chainPaid;
        private readonly Dictionary<int, WildInfo> _wild = new Dictionary<int, WildInfo>();
        private readonly Dictionary<int, ICreatureView> _views = new Dictionary<int, ICreatureView>();
        private ICreatureViewFactory _viewFactory;
        private readonly RunOutcome _outcome = new RunOutcome();
        private Pcg32 _captureRng;
        private Pcg32 _materialRng;
        private MaterialDrops _drops;
        private long _nowMs = 1000;
        private float _accumulator;
        private long _nextAffixMs;
        private int _mainId = CombatAi.NoTarget;
        // Wild unit chosen with CycleTarget (combat.md#指定目標, D0-W4); -1 when nothing is locked.
        private int _lockedTarget = -1;
        // Scene object chosen with CycleTarget (destructibles.md#誰能打); -1 when none. Never set together with
        // _lockedTarget. Objects do not move, so the candidate captured at lock time stays valid until destroyed.
        private long _lockedObjectId = -1;
        private DestructibleCandidate _lockedObject;
        private string _lockedObjectNameKey;
        // Close interaction (combat.md#近身互動, D0-W3): the idle wild offered by the HUD prompt, and the one whose menu is
        // open (-1 = none). While the menu is open the character stands still and only the menu reads input.
        private int _interactable = -1;
        private int _interactTarget = -1;
        private InteractChoice _interactChoice;
        private string _observeText;
        private InputAction _interactAction;
        private InputActionMap _menuInput;
        private InputAction _menuUp;
        private InputAction _menuDown;
        private InputAction _menuSubmit;
        private InputAction _menuCancel;

        private enum InteractChoice
        {
            Observe = 0, Provoke = 1, Pet = 2, Close = 3,
        }
        private readonly List<LockOption> _lockOptions = new List<LockOption>(32);
        private readonly List<DestructibleCandidate> _objectCandidates = new List<DestructibleCandidate>(32);
        private AiCommand _command = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
        private VesselKind _vessel = VesselKind.TalismanCard;
        private int _region = -1;
        private bool _started;
        private bool _ended;
        private long _bossDownAtMs = -1;
        private long _abandonArmedUntilMs = -1;
        private string _toast = string.Empty;
        private long _toastUntilMs;
        private ConceptPortraitTable _portraits;
        private GUIStyle _label;
        private GUIStyle _markerStyle;
        private Material _terrainMaterial;
        private Material _playerMaterial;

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal bool StartedForTests => _started;

        internal VesselKind VesselForTests => _vessel;

        internal int MainForTests => _mainId;

        internal AiCommandKind CommandForTests => _command.Kind;

        /// <summary>Simulation time of the last <see cref="StepCombatForTests"/> (ms).</summary>
        /// <remarks>Pre: none. Post: &gt;= 1000, equals the nowMs of the last Encounter.Tick. Side effects: none.</remarks>
        internal long NowMsForTests => _nowMs;

        /// <summary>Encounter unit id of each party slot, in party order.</summary>
        /// <remarks>Pre: Configure ran. Post: one id per RunSetup.Party slot. Side effects: none.</remarks>
        internal IReadOnlyList<int> PartyUnitsForTests => _partyUnits.ToArray();

        /// <summary>Cumulative XP of one party slot right now.</summary>
        /// <remarks>Pre: 0 &lt;= slot &lt; party size. Post: &gt;= the slot's starting XP. Side effects: none.</remarks>
        internal int PartyExpForTests(int slot) => _partyExp[slot];

        internal int PlayerExpForTests => _playerExp;

        /// <summary>Runs exactly one fixed combat step, the same code Update runs per 50 ms tick.</summary>
        /// <remarks>
        /// Pre: Configure ran; the component is disabled so Update does not tick as well.
        /// Post: _nowMs advanced by one tick.
        /// Side effects: everything StepCombat does (encounter tick, XP, level-ups, quest, end check).
        /// </remarks>
        internal void StepCombatForTests() => StepCombat();
#endif

        /// <summary>Replaces the placeholder view factory (model-replacement seam, art-asset-pipeline.md#佔位模型).</summary>
        /// <remarks>Pre: factory != null; called before Configure. Post: Configure builds every unit view with it. Side effects: replaces the selected factory.</remarks>
        internal void UseViewFactory(ICreatureViewFactory factory)
        {
            Core.Contracts.Contract.Requires(factory != null, "factory required");
            Core.Contracts.Contract.Requires(_plan == null, "view factory must be chosen before Configure");
            _viewFactory = factory;
        }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        /// <summary>Materials dropped so far this run (the list RunOutcome hands to the lobby).</summary>
        /// <remarks>Pre: none. Post: non-null, drop order. Side effects: none.</remarks>
        internal IReadOnlyList<MaterialStack> MaterialsForTests => _outcome.Materials.ToArray();

        /// <summary>Swaps the main for the first eligible reserve, exactly as the Switch input does.</summary>
        /// <remarks>Pre: Configure ran. Post: as SwitchMain. Side effects: as SwitchMain (team, encounter, toast).</remarks>
        internal void SwitchMainForTests() => SwitchMain();

        /// <summary>Queues the knock-out of a wild unit that a test already took to 0 HP, to be settled in the next step.</summary>
        /// <remarks>
        /// Pre: wildUnitId is a wild unit with 0 HP whose knock-out has not been settled yet.
        /// Post: the next StepCombat settles it like an Encounter Faint event (after that tick's real events).
        /// Side effects: appends to the queue unless already queued; nothing else until the next step.
        /// </remarks>
        internal void InjectFaintForTests(int wildUnitId)
        {
            Core.Contracts.Contract.Requires(_encounter != null && wildUnitId >= 0 && wildUnitId < _encounter.Units.Count,
                "unknown unit");
            Core.Contracts.Contract.Requires(_encounter.Units[wildUnitId].Side == EncounterSide.Wild &&
                _encounter.Units[wildUnitId].Combatant.IsFainted && _wild[wildUnitId].KnockedOutAtMs < 0,
                "only an unsettled fainted wild unit can be injected");
            if (!_injectedFaints.Contains(wildUnitId))
            {
                _injectedFaints.Add(wildUnitId);
            }
        }

#endif

        /// <summary>Starts the run.</summary>
        /// <remarks>
        /// Pre: all arguments non-null; actions contains the DemoRun map and its twelve actions and the DemoMenu map
        ///      with Up/Down/Submit/Cancel;
        ///      setup.Radius >= MoveGeometry.MinSupportedRadiusM; party 1..6 non-null forms with
        ///      matching PartyIds/PartyExp; setup.Progression non-null.
        /// Post: planet, plan and wild spawns exist; party is summoned once the ground is ready.
        /// Side effects: creates GameObjects and private input maps (DemoRun enabled while the component is enabled;
        ///      DemoMenu only while an interaction menu is open).
        /// </remarks>
        public void Configure(DemoCopy copy, CombatBalance balance, InputActionAsset actions, Material terrainMaterial,
            RunSetup setup, IReadOnlyList<CreatureDef> wildPool, Action<RunOutcome> onEnd, Shader terrainShader = null,
            Shader toonShader = null, Shader starShader = null)
        {
            Core.Contracts.Contract.Requires(copy != null && balance != null && setup != null && onEnd != null,
                "copy, balance, setup and onEnd are required");
            InputActionMap runMap = RequireRunMap(actions);
            Core.Contracts.Contract.Requires(setup.Party != null && setup.Party.Count >= 1 && setup.Party.Count <= 6,
                "party must hold 1..6 forms");
            Core.Contracts.Contract.Requires(setup.Progression != null && setup.PartyIds != null && setup.PartyExp != null &&
                setup.PartyIds.Count == setup.Party.Count && setup.PartyExp.Count == setup.Party.Count,
                "progression, party ids and party exp are required, one per party slot");
            Core.Contracts.Contract.Requires(setup.PlayerExp >= 0 && setup.PlayerExp <= setup.Progression.PlayerMaxExp,
                "player exp must be 0..PlayerMaxExp");
            Core.Contracts.Contract.Requires(setup.Materials != null, "material drop rules are required");
            _copy = copy;
            _progression = setup.Progression;
            _drops = setup.Materials;
            _partyExp = setup.PartyExp.ToArray();
            _playerExp = setup.PlayerExp;
            _balance = balance;
            _actions = actions;
            _runInput = runMap.Clone();
            _selectFirstAction = _runInput.FindAction("SelectFirst", true);
            _selectSecondAction = _runInput.FindAction("SelectSecond", true);
            _switchAction = _runInput.FindAction("Switch", true);
            _focusAction = _runInput.FindAction("Focus", true);
            _cycleTargetAction = _runInput.FindAction("CycleTarget", true);
            _freeAction = _runInput.FindAction("Free", true);
            _followAction = _runInput.FindAction("Follow", true);
            _retreatAction = _runInput.FindAction("Retreat", true);
            _cycleVesselAction = _runInput.FindAction("CycleVessel", true);
            _captureAction = _runInput.FindAction("Capture", true);
            _abandonAction = _runInput.FindAction("Abandon", true);
            _interactAction = _runInput.FindAction("Interact", true);
            _menuInput = actions.FindActionMap("DemoMenu", true).Clone();
            _menuUp = _menuInput.FindAction("Up", true);
            _menuDown = _menuInput.FindAction("Down", true);
            _menuSubmit = _menuInput.FindAction("Submit", true);
            _menuCancel = _menuInput.FindAction("Cancel", true);
            if (isActiveAndEnabled)
            {
                _runInput.Enable();
            }
            _setup = setup;
            _onEnd = onEnd;

            var config = new PlanetConfig(setup.Radius, float3.zero);
            PlanetHeight height = PlanetHeight.ForPlanet(config, setup.Seed);
            PlanetTemplates templates = null;
            if (setup.Landforms)
            {
                templates = TemplatePlanner.Build(config, setup.Seed, setup.PlanetType.TerrainTags,
                    BossDraw.SizeFor(setup.BossStage));
            }

            var vocab = new RunPlanVocabulary(copy.ThemeIds, copy.GenericAffixIds, copy.ChainIds);
            System.Func<float3, bool> isLand = null;
            if (templates != null)
            {
                TemplateView view = templates.View;
                isLand = d => !view.IsSea(d);
            }

            _plan = RunPlanner.Generate(config, setup.Seed, setup.PlanetType, setup.BossStage, wildPool, vocab, isLand,
                setup.Dungeon ? DungeonPlanner.SpawnBorderClearanceRad(config) : 0f);
            _layout = SettlementPlanner.Generate(config, _plan, isLand);
            PlanetWalls walls = null;
            if (setup.Dungeon)
            {
                _dungeon = DungeonPlanner.Generate(config, _plan, _layout, isLand);
                walls = DungeonPlanner.BuildWalls(config, _plan, _dungeon);
            }

            // 1.5 m keeps tree crowns and rocks off the road edge and out of doorways (scatter only; creature collision
            // passes its own body radius to IsInsideBuilding).
            var clearance = new LayoutClearance(config, _layout, 1.5f);
            var surface = new HeightSurface(height, templates, clearance, walls, config.Radius);
            _surface = surface;
            System.Func<float3, bool> isSea = templates != null ? (d => templates.View.IsSea(d)) : null;
            TerrainProfile terrainProfile = TerrainProfiles.ForPlanetType(setup.PlanetType.Id);
            // Rule layer (destructibles.md#實作分層): creatures are blocked by and moves hit the same items ScatterField draws,
            // whether or not anything is drawn.
            var scatter = new ScatterPlanner(config, setup.Seed, setup.PlanetType.Id, surface.GroundAltitude, isSea,
                walls != null ? (System.Func<float3, bool>)(d => clearance.IsClear(d) && surface.IsOffWalls(d)) : clearance.IsClear,
                terrainProfile.ScatterDensity);
            DestructibleTable objects = null;
            if (setup.EnvMaterials != null)
            {
                objects = new DestructibleTable(setup.EnvMaterials);
                _envMaterials = setup.EnvMaterials;
                _objectTable = objects;
                _worldObjects = new WorldObjects(config, scatter, _layout, clearance, surface.GroundAltitude,
                    setup.EnvMaterials, objects);
                surface.Objects = _worldObjects;
            }
            _quest = new QuestTracker(_plan);
            _questPaid = new bool[_plan.Quest.Count];
            _viewFactory ??= new ProceduralCreatureViewFactory(toonShader);
            if (terrainShader != null)
            {
                float relief = height.MaxAbsHeight + config.Radius * PlanetTemplates.AmplitudeFractionOfRadius * 0.5f;
                _terrainMaterial = PlanetLook.TerrainMaterial(terrainShader, setup.PlanetType.Id, config, relief);
                terrainMaterial = _terrainMaterial;
            }

            _rig = PlanetRig.Build(config, height, _plan.LandingDirection, terrainMaterial, templates,
                setup.CharacterModelPrefab, walls);
            _portraits = setup.Portraits;
            if (setup.VolumeProfile != null)
            {
                UniversalAdditionalCameraData camData = _rig.Camera.GetUniversalAdditionalCameraData();
                if (camData != null)
                {
                    camData.renderPostProcessing = true;
                }

                var volumeGo = new GameObject("PostProcessVolume");
                volumeGo.transform.SetParent(transform, false);
                Volume volume = volumeGo.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = setup.VolumeProfile;
            }
            // The planet's spin axis is a property of this planet (fixed in world space), not "up" anywhere.
            gameObject.AddComponent<AtmosphereDriver>().Configure(_rig.Camera, _rig.Sun, config, new float3(0f, 1f, 0f),
                PlanetLook.Sky(setup.PlanetType.Id), 600f, balance.Horizon.EyeHeightMm / 1000f);
            if (starShader != null)
            {
                gameObject.AddComponent<StarField>().Configure(_rig.Camera, _rig.Sun, config, setup.Seed, starShader);
            }

            if (toonShader != null)
            {
                if (setup.CharacterModelPrefab == null)
                {
                    // Runtime primitives keep the built-in material, which renders magenta under URP.
                    _playerMaterial = PlanetLook.CreatureMaterial(toonShader, Color.white, 0.03f);
                    _rig.Character.GetComponentInChildren<Renderer>().sharedMaterial = _playerMaterial;
                }
                // Own child, so building and road roots never mix with the scatter roots ScatterField parents to this transform.
                var settlements = new GameObject("Settlements");
                settlements.transform.SetParent(transform, false);
                _settlementView = settlements.AddComponent<SettlementView>();
                _settlementView.Configure(config, _layout, surface.GroundAltitude, toonShader);
                if (_dungeon != null)
                {
                    var dungeonRoot = new GameObject("Dungeon");
                    dungeonRoot.transform.SetParent(transform, false);
                    _dungeonView = dungeonRoot.AddComponent<DungeonView>();
                    _dungeonView.Configure(config, _plan, _dungeon, surface.GroundAltitude, toonShader);
                }

                _scatterField = gameObject.AddComponent<ScatterField>();
                _scatterField.Configure(config, scatter, setup.PlanetType.Id, PlayerPosition, toonShader,
                    objects != null ? objects.IsDestroyed : (System.Func<long, bool>)null);
                if (objects != null)
                {
                    // REQ: destructibles.md#物理 (ADR-0009) — only where something is drawn: headless runs have no bodies.
                    var gravity = gameObject.AddComponent<PlanetGravity>();
                    gravity.Configure(config, _rig.Character.Gravity);
                    var loose = new GameObject("LooseObjects");
                    loose.transform.SetParent(transform, false);
                    _objectPhysics = loose.AddComponent<ObjectPhysics>();
                    _objectPhysics.Configure(config, gravity, surface.GroundAltitude,
                        _rig.Character.GetComponentsInChildren<Collider>(), ObjectPhysicsTuning.Default, setup.EnvMaterials,
                        toonShader);
                }
            }

            if (_rig != null && _rig.Character != null)
            {
                _rig.Character.SetTerrainModifiers(terrainProfile.MoveSpeedMultiplier,
                    terrainProfile.SprintSpeedMultiplier, terrainProfile.GroundAccelerationMultiplier);
            }

            _encounter = new Encounter(config, balance, surface, setup.Seed);
            _encounter.SetTerrainMoveSpeedMultiplier(_ => terrainProfile.MoveSpeedMultiplier);
            if (objects != null)
            {
                // REQ: destructibles.md#影素材 — night where the object stands: the sun is below its local horizon.
                _encounter.AttachDestructibles(objects, _worldObjects, d => IsNight(d));
            }
            // REQ: combat.md#野生領域 (Q-C33) — wild creatures may target the character; 0 HP loses the run.
            _vitals = new PlayerVitals(balance.Player);
            _encounter.AttachPlayer(_vitals);
            _captureRng = new Pcg32(setup.Seed, 'K');
            // Own stream: drops must not shift capture or combat rolls, so the P2-12 XP regressions stay valid.
            _materialRng = new Pcg32(setup.Seed, 'D');

            for (int s = 0; s < _plan.Spawns.Count; s++)
            {
                RunSpawn spawn = _plan.Spawns[s];
                CreatureDef def = FindForm(wildPool, spawn.CreatureId);
                int level = _progression.WildLevel(D0Difficulty, _plan.Regions[spawn.Region].Tier, spawn.Role);
                EncounterUnit u = spawn.Role == SpawnRole.Boss
                    ? _encounter.AddBoss(def, null, spawn.Direction, level)
                    : _encounter.AddCreature(EncounterSide.Wild, def, null, spawn.Direction, level);
                _wild[u.Id] = new WildInfo { Spawn = s, Level = level };
                CreateView(u, spawn.Role);
            }

            var roster = new List<int>();
            for (int i = 0; i < setup.Party.Count; i++)
            {
                CreatureDef def = setup.Party[i];
                EncounterUnit u = _encounter.AddCreature(EncounterSide.Player, def, null, _plan.LandingDirection,
                    _progression.LevelOf(_partyExp[i]));
                roster.Add(u.Id);
                _partyUnits.Add(u.Id);
                CreateView(u, SpawnRole.Normal);
            }

            _team = new TeamState(roster, balance.SwitchCooldownMs);
            _team.SetPool(roster.GetRange(0, math.min(roster.Count, 4)));
        }

        private void Update()
        {
            if (_ended || _rig == null)
            {
                return;
            }

            if (!_started)
            {
                if (_rig.TrySpawn(_actions))
                {
                    _started = true;
                    SummonOpening();
                    UpdateBgm();
                }

                return;
            }

            ReadInput();
            _accumulator += Time.deltaTime * 1000f;
            int steps = 0;
            while (_accumulator >= TickMs && steps++ < 5)
            {
                _accumulator -= TickMs;
                StepCombat();
                if (_ended)
                {
                    return;
                }
            }

            if (steps > 5)
            {
                _accumulator = 0f; // a long hitch drops time rather than spiralling (fixed-step catch-up cap)
            }

            SyncViews();
        }

        private void SummonOpening()
        {
            float3 player = PlayerPosition();
            for (int i = 0; i < _team.Pool.Count && _team.Active.Count < 2; i++)
            {
                int id = _team.Pool[i];
                if (_team.Summon(id, _nowMs) == TeamResult.Ok)
                {
                    _encounter.Field(id, BesidePlayer(player, i), false, _nowMs);
                }
            }

            _mainId = _team.Active.Count > 0 ? _team.Active[0] : CombatAi.NoTarget;
        }

        private void StepCombat()
        {
            _nowMs += TickMs;
            float3 player = PlayerPosition();
            // SOL P2-12 R3-02: a switch made by input since the last tick must count before this tick's region payout.
            RecordAppearances();
            UpdateRegion(player);
            if (_nowMs >= _nextAffixMs)
            {
                _nextAffixMs = _nowMs + AffixRefreshMs;
                ApplyAffixes(player);
            }

            // SOL P2-12 R3-01: XP earned out of combat (capture input, region step) must level up before a new battle
            // can start this tick; otherwise the growth would wait for that battle to end.
            ApplyLevelUps();
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
            BeforeTickForTests?.Invoke();
#endif
            _encounter.Tick(_nowMs, TickMs, player, _command, _mainId);
            ApplyObjectEvents();
            ApplyPlayerPush();
            ValidateLock(player);
            UpdateInteraction(PlayerPosition());
            bool engaged = AnyEngaged();
            if (engaged && !_team.InCombat)
            {
                BeginCombat();
            }

            RecordAppearances();

            bool playerDowned = false;
            foreach (EncounterEvent ev in _encounter.Events)
            {
                if (ev.Kind == EncounterEventKind.Faint)
                {
                    OnFaint(ev.TargetId);
                }
                else if (ev.Kind == EncounterEventKind.PlayerDowned)
                {
                    playerDowned = true;
                }
            }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
            // Test seam: empty in play. A knock-out a test produced out of band settles exactly like a Tick event.
            for (int i = 0; i < _injectedFaints.Count; i++)
            {
                OnFaint(_injectedFaints[i]);
            }

            _injectedFaints.Clear();
#endif

            if (!engaged && _team.InCombat)
            {
                _team.EndCombat();
            }

            if (playerDowned)
            {
                // Q-C33 (2026-11-10): Downed settles the run as a defeat; captures, XP and materials earned so far
                // (including knock-outs in this same tick, paid above) still return to the lobby (RunOutcome).
                End(false, false);
                return;
            }

            ApplyLevelUps();

            RefillField(player);
            ExpireKnockedOut();
            CheckEnd();
        }

        private void OnFaint(int unitId)
        {
            EncounterUnit u = _encounter.Units[unitId];
            // SOL P2-12 R1: a battle whose only enemy falls on its first tick is no longer "engaged" after Tick, so the
            // combat start above never ran; open it here or the knockout would be paid with the previous battle's record.
            if (!_team.InCombat)
            {
                BeginCombat();
                RecordAppearances();
            }

            if (u.Side == EncounterSide.Player)
            {

                _team.Faint(unitId);
                _encounter.Unfield(unitId);
                if (_mainId == unitId)
                {
                    _mainId = _team.Active.Count > 0 ? _team.Active[0] : CombatAi.NoTarget;
                }

                return;
            }

            WildInfo w = _wild[unitId];
            w.KnockedOutAtMs = _nowMs;
            RunSpawn spawn = _plan.Spawns[w.Spawn];
            // The capture of this creature settles later but belongs to this battle's participants (per-event record).
            w.AppearedAtKnockout = new HashSet<int>(_appeared);
            // REQ: option A — a boss pays only its boss pool, never a second wild-defeat pool.
            Reward(spawn.Role == SpawnRole.Boss ? XpSource.Boss : XpSource.Wild, w.Level, spawn.Role == SpawnRole.Elite,
                w.AppearedAtKnockout);
            _quest.OnKnockedOut(w.Spawn, RunPlanner.MainElement(u.Def));
            PayQuest(w.AppearedAtKnockout);
            // REQ: planet-world.md#地表地牢 — the key elite's knock-out opens the boss room for the rest of the run.
            if (_dungeon != null && w.Spawn == _dungeon.KeySpawn && _surface.LocksClosed)
            {
                _surface.LocksClosed = false;
                _dungeonView?.OpenLocks();
                Toast(_copy.Ui("ui.hud.gate_open"));
            }

            if (spawn.Role == SpawnRole.Boss)
            {
                _bossDownAtMs = _nowMs;
                Toast(_copy.Ui("ui.hud.knocked_out"));
                UpdateBgm();
            }

            // REQ: creatures.md#進化 (Q-C32 B) — one roll per spawn; the flag also stops a re-sent faint from paying twice.
            if (!w.Dropped)
            {
                w.Dropped = true;
                if (_drops.TryRoll(spawn.Role, _setup.PlanetType.ElementBias, ref _materialRng, out MaterialStack drop))
                {
                    _outcome.Materials.Add(drop);
                    string msg = _copy.Ui("ui.hud.material_drop").Replace("{material}", _copy.Material(drop.Key));
                    // One toast slot: keep the boss knock-out line visible instead of overwriting it.
                    Toast(spawn.Role == SpawnRole.Boss ? _copy.Ui("ui.hud.knocked_out") + "  " + msg : msg);
                }
            }
        }

        private void RefillField(float3 player)
        {
            if (_team.Active.Count >= 2)
            {
                return;
            }

            foreach (int id in _team.Pool)
            {
                if (_team.Active.Count >= 2)
                {
                    break;
                }

                if (_encounter.Units[id].Combatant.IsFainted || In(_team.Active, id))
                {
                    continue;
                }

                if (_team.Summon(id, _nowMs) == TeamResult.Ok)
                {
                    _encounter.Field(id, BesidePlayer(player, _team.Active.Count), false, _nowMs);
                }
            }

            if (_mainId == CombatAi.NoTarget && _team.Active.Count > 0)
            {
                _mainId = _team.Active[0];
            }
        }

        private void ExpireKnockedOut()
        {
            foreach (KeyValuePair<int, WildInfo> kv in _wild)
            {
                WildInfo w = kv.Value;
                if (!w.Gone && w.KnockedOutAtMs >= 0 && _nowMs - w.KnockedOutAtMs > KnockedOutWindowMs)
                {
                    w.Gone = true;
                    _encounter.Remove(kv.Key);
                }
            }
        }

        private void CheckEnd()
        {
            bool anyLeft = false;
            foreach (int id in _team.Pool)
            {
                anyLeft |= !_encounter.Units[id].Combatant.IsFainted;
            }

            if (!anyLeft)
            {
                End(false, false);
                return;
            }

            // The boss stays capturable for the knocked-out window; the run ends when it is captured or that window closes.
            if (_bossDownAtMs >= 0)
            {
                int bossUnit = BossUnit();
                if (_wild[bossUnit].Gone)
                {
                    End(true, false);
                }
            }
        }

        private void ReadInput()
        {
            if (_runInput == null || !_runInput.enabled)
            {
                return;
            }

            // The menu owns input while open: its Cancel (Esc / B) must not also abandon the run or cycle the vessel.
            if (_interactTarget >= 0)
            {
                ReadInteractMenu();
                return;
            }

            if (_interactAction.WasPressedThisFrame())
            {
                OpenInteract();
            }

            if (_selectFirstAction.WasPressedThisFrame() && _team.Active.Count > 0)
            {
                _mainId = _team.Active[0];
            }

            if (_selectSecondAction.WasPressedThisFrame() && _team.Active.Count > 1)
            {
                _mainId = _team.Active[1];
            }

            if (_switchAction.WasPressedThisFrame())
            {
                SwitchMain();
            }

            if (_focusAction.WasPressedThisFrame())
            {
                FocusNearest();
            }

            if (_cycleTargetAction.WasPressedThisFrame())
            {
                CycleTarget();
            }

            if (_freeAction.WasPressedThisFrame())
            {
                _command = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
            }

            if (_followAction.WasPressedThisFrame())
            {
                _command = new AiCommand(AiCommandKind.Follow, CombatAi.NoTarget);
            }

            if (_retreatAction.WasPressedThisFrame())
            {
                _command = new AiCommand(AiCommandKind.Retreat, CombatAi.NoTarget);
            }

            if (_cycleVesselAction.WasPressedThisFrame())
            {
                _vessel = (VesselKind)(((int)_vessel + 1) % 3);
            }

            if (_captureAction.WasPressedThisFrame())
            {
                TryCapture();
            }

            if (_abandonAction.WasPressedThisFrame())
            {
                RequestAbandon();
            }
        }

        internal static InputActionMap RequireRunMap(InputActionAsset actions)
        {
            Core.Contracts.Contract.Requires(actions != null, "actions are required");
            InputActionMap map = actions.FindActionMap("DemoRun");
            Core.Contracts.Contract.Requires(map != null, "actions must contain the DemoRun map");
            foreach (string name in new[] { "SelectFirst", "SelectSecond", "Switch", "Focus", "CycleTarget", "Free", "Follow",
                         "Retreat", "CycleVessel", "Capture", "Abandon", "Interact" })
            {
                Core.Contracts.Contract.Requires(map.FindAction(name) != null, "DemoRun is missing " + name);
            }

            InputActionMap menu = actions.FindActionMap("DemoMenu");
            Core.Contracts.Contract.Requires(menu != null, "actions must contain the DemoMenu map");
            foreach (string name in new[] { "Up", "Down", "Submit", "Cancel" })
            {
                Core.Contracts.Contract.Requires(menu.FindAction(name) != null, "DemoMenu is missing " + name);
            }

            return map;
        }

        // REQ: combat.md#近身互動 (D0-W3). The prompt follows the nearest idle wild; an open menu closes once its target
        // starts fighting, is gone, or wanders past twice the reach (hysteresis so a step of wandering does not flicker it).
        private void UpdateInteraction(float3 player)
        {
            if (_interactTarget >= 0)
            {
                EncounterUnit u = _encounter.Units[_interactTarget];
                float limit = 2f * Encounter.InteractRadiusM + u.CollisionRadiusM;
                if (!u.IsActive || u.Engaged || math.distance(u.Position, player) > limit)
                {
                    CloseInteract();
                }
            }

            _interactable = _interactTarget < 0 ? _encounter.NearestInteractable(player) : -1;
        }

        private void OpenInteract()
        {
            int id = _encounter.NearestInteractable(PlayerPosition());
            if (id < 0)
            {
                return;
            }

            _interactTarget = id;
            _interactable = -1;
            _interactChoice = InteractChoice.Observe;
            _observeText = null;
            _menuInput.Enable();
            // Disabling the driver stops reading the stick, but the motor keeps its last input; zero it so the character
            // does not keep walking while the menu is up.
            _rig.Input.enabled = false;
            _rig.Character.SetInput(Vector2.zero, _rig.Character.Facing, false, false);
        }

        private void CloseInteract()
        {
            _interactTarget = -1;
            _observeText = null;
            _menuInput.Disable();
            _rig.Input.enabled = true;
        }

        private void ReadInteractMenu()
        {
            if (_menuCancel.WasPressedThisFrame() || _interactAction.WasPressedThisFrame())
            {
                CloseInteract();
                return;
            }

            if (_menuUp.WasPressedThisFrame())
            {
                _interactChoice = (InteractChoice)(((int)_interactChoice + 3) % 4);
            }

            if (_menuDown.WasPressedThisFrame())
            {
                _interactChoice = (InteractChoice)(((int)_interactChoice + 1) % 4);
            }

            if (_menuSubmit.WasPressedThisFrame())
            {
                ChooseInteract(_interactChoice);
            }
        }

        private void ChooseInteract(InteractChoice choice)
        {
            int id = _interactTarget;
            EncounterUnit u = _encounter.Units[id];
            switch (choice)
            {
                case InteractChoice.Observe:
                    _observeText = ObserveText(u);
                    return;
                case InteractChoice.Provoke:
                    // REQ: equals Provoke (works on timid too); locking it lets Focus and the marker follow the fight.
                    CloseInteract();
                    _encounter.Provoke(id);
                    _lockedTarget = id;
                    _lockedObjectId = -1;
                    return;
                case InteractChoice.Pet:
                    // Q-D2 (decided 2026-11-10): petting has no rule effect.
                    CloseInteract();
                    Toast(_copy.Ui("ui.interact.pet_reaction").Replace("{target}", _copy.Name(u.Def.Id)));
                    return;
                default:
                    CloseInteract();
                    return;
            }
        }

        private string ObserveText(EncounterUnit u)
        {
            var elements = new List<string>(u.Def.Mastery.Count);
            foreach (ElementValue ev in u.Def.Mastery)
            {
                elements.Add(_copy.Element(ev.Element.ToString()));
            }

            return _copy.Ui("ui.interact.observe_detail")
                .Replace("{species}", _copy.Name(u.Def.Id))
                .Replace("{stage}", _copy.Ui("ui.stage." + u.Def.Stage.ToString().ToLowerInvariant()))
                .Replace("{element}", string.Join("/", elements))
                .Replace("{temperament}", _copy.Ui("ui.temperament." + u.Def.Temperament.ToString().ToLowerInvariant()))
                .Replace("{level}", u.Level.ToString());
        }

        private static string BindingLabel(InputAction action) =>
            action.GetBindingDisplayString(group: Gamepad.current != null ? "Gamepad" : "Keyboard&Mouse");

        private void SwitchMain()
        {
            if (_mainId == CombatAi.NoTarget)
            {
                return;
            }

            foreach (int id in _team.Pool)
            {
                if (In(_team.Active, id) || _encounter.Units[id].Combatant.IsFainted || _team.IsRetired(id))
                {
                    continue;
                }

                TeamResult r = _team.Switch(_mainId, id, _nowMs);
                if (r == TeamResult.Ok)
                {
                    float3 at = _encounter.Units[_mainId].Position;
                    _encounter.Unfield(_mainId);
                    _encounter.Field(id, _rig.Config.ToDirection(at), true, _nowMs);
                    _mainId = id;
                }
                else if (r == TeamResult.SwitchOnCooldown)
                {
                    Toast(_copy.Ui("ui.hud.switch_cooldown"));
                }

                return;
            }
        }

        private void TryCapture()
        {
            int best = FindCaptureTarget(CaptureReachM);
            if (best < 0)
            {
                Toast(_copy.Ui("ui.hud.capture_no_target").Replace("{capture}", BindingLabel(_captureAction)));
                return;
            }

            WildInfo w = _wild[best];
            RunSpawn spawn = _plan.Spawns[w.Spawn];
            bool bounty = _plan.Regions[spawn.Region].AffixId == "affix_bounty";
            int chance = CaptureRules.ChancePercent(spawn.Role == SpawnRole.Boss, bounty, _balance.CaptureBasePercent,
                _balance.CaptureBossPercent);
            w.Gone = true; // one throw per knocked-out creature: a miss lets it escape (no re-roll farming)
            _encounter.Remove(best);
            if (CaptureRules.Roll(chance, ref _captureRng))
            {
                // A capture keeps its form and level (option A); it joins the reward roster only in the lobby.
                _outcome.Captured.Add((spawn.CreatureId, _vessel, _progression.XpThreshold(w.Level)));
                HashSet<int> battle = w.AppearedAtKnockout ?? new HashSet<int>();
                Reward(XpSource.Capture, w.Level, false, battle);
                _quest.OnCaptured();
                PayQuest(battle);
                Toast(_copy.Ui("ui.hud.capture_success") + " " + _copy.Name(spawn.CreatureId));
            }
            else
            {
                Toast(_copy.Ui("ui.hud.capture_fail"));
            }
        }

        private int FindCaptureTarget(float reach)
        {
            float3 player = PlayerPosition();
            int best = -1;
            float bestDist = reach;
            foreach (KeyValuePair<int, WildInfo> kv in _wild)
            {
                if (kv.Value.Gone || kv.Value.KnockedOutAtMs < 0 ||
                    _nowMs - kv.Value.KnockedOutAtMs > KnockedOutWindowMs)
                {
                    continue;
                }

                float d = math.distance(_encounter.Units[kv.Key].Position, player);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = kv.Key;
                }
            }

            return best;
        }

        private void ApplyAffixes(float3 player)
        {
            foreach (EncounterUnit u in _encounter.Units)
            {
                if (!u.IsActive)
                {
                    continue;
                }

                int region = u.Side == EncounterSide.Player ? RunPlanner.RegionAt(_plan, _rig.Config.ToDirection(player))
                    : _plan.Spawns[_wild[u.Id].Spawn].Region;
                string affix = _plan.Regions[region].AffixId;
                if (affix == null || !affix.EndsWith("_surge"))
                {
                    continue;
                }

                // REQ: Terra affixes.json — "<element> moves in this region: CD −15%". A short refreshed buff models
                // "while inside the region" without tracking exits.
                Element e = ElementOfSurge(affix);
                u.Combatant.Book.Apply("region_affix", 0, new EffectSpec(EffectType.CdModPercent, false, e, -15, 0, 1),
                    (int)(AffixRefreshMs * 2), _nowMs);
            }
        }

        private void UpdateRegion(float3 player)
        {
            int region = RunPlanner.RegionAt(_plan, _rig.Config.ToDirection(player));
            if (region != _region)
            {
                _region = region;
                _quest.OnRegionEntered(region);
                // No battle produced this step: use the battle in progress, if any (Q-C31: same record as a battle).
                PayQuest(_team.InCombat ? new HashSet<int>(_appeared) : new HashSet<int>());
                if (_plan != null && region >= 0 && region < _plan.Regions.Count)
                {
                    RunRegion nextRegion = _plan.Regions[region];
                    if (!BgmResolver.CanPartyEnterAdvancedTerrain(nextRegion.ThemeId, _setup?.Party, out Element req))
                    {
                        Toast($"需攜帶擁有【{req}】屬性招式的宙獸");
                    }
                }
                UpdateBgm();
            }
        }

        private void UpdateBgm()
        {
            if (BgmPlayer.Instance == null || _plan == null)
            {
                return;
            }

            int region = _region >= 0 ? _region : _plan.LandingRegion;
            if (region < 0 || region >= _plan.Regions.Count)
            {
                return;
            }

            if (region == _plan.BossRegion && _bossDownAtMs < 0)
            {
                PlanetSize size = BossDraw.SizeFor(BossDraw.Stage(_plan.Seed, CreatureStage.Adult));
                string bossTrack = BgmResolver.ResolveBossBgm(size);
                BgmPlayer.Instance.Play(bossTrack);
                return;
            }

            RunRegion r = _plan.Regions[region];
            IReadOnlyList<string> tags = (_setup != null && _setup.PlanetType != null) ? _setup.PlanetType.TerrainTags : null;
            string track = BgmResolver.ResolveRegionBgm(r.ThemeId, tags);
            BgmPlayer.Instance.Play(track);
        }

        // REQ: level-progression.md#經驗分配 — each battle keeps its own appearance record, reset when the next starts.
        private void BeginCombat()
        {
            _appeared.Clear();
            _team.BeginCombat();
        }

        private void RecordAppearances()
        {
            if (!_team.InCombat)
            {
                return;
            }

            foreach (int id in _team.Active)
            {
                _appeared.Add(id);
            }
        }

        private void Reward(XpSource source, int level, bool elite, HashSet<int> appeared)
        {
            int pool = _progression.SourceXp(source, level, elite);
            var recipients = new List<XpRecipient>(_partyUnits.Count);
            for (int i = 0; i < _partyUnits.Count; i++)
            {
                int unit = _partyUnits[i];
                recipients.Add(new XpRecipient(_setup.PartyIds[i], _partyExp[i], _encounter.Units[unit].Combatant.IsFainted,
                    appeared.Contains(unit), -1));
            }

            // D0 has no sidekick loadout yet (P2-07 lobby UI), so only mains are paid.
            XpSettlement settled = _progression.Settle(pool, recipients);
            for (int i = 0; i < _partyUnits.Count; i++)
            {
                int before = _progression.LevelOf(_partyExp[i]);
                _partyExp[i] += settled.Paid[i];
                // REQ: level-progression.md#玩家等級 — the player earns what the mains actually gained (after the LV100
                // cap), sidekicks excluded. Long sum: six mains near the cap can pass Int32 before the clamp.
                _playerExp = (int)Math.Min((long)_playerExp + settled.Paid[i], _progression.PlayerMaxExp);
                int after = _progression.LevelOf(_partyExp[i]);
                if (after > before)
                {
                    // Q-C31: the stats change when this battle ends (ApplyLevelUps), not mid-fight.
                    Toast(_copy.Ui("ui.hud.level_up").Replace("{name}", _copy.Name(_encounter.Units[_partyUnits[i]].Def.Id))
                        .Replace("{level}", after.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }
            }
        }

        // REQ: Q-C31 (2026-11-09) — growth applies once the battle is over; a creature that fainted stays at its level
        // for this run (it is not revived, D0-R1), but its XP still reaches the save.
        private void ApplyLevelUps()
        {
            if (_team.InCombat)
            {
                return;
            }

            for (int i = 0; i < _partyUnits.Count; i++)
            {
                EncounterUnit u = _encounter.Units[_partyUnits[i]];
                int level = _progression.LevelOf(_partyExp[i]);
                if (level > u.Level && !u.Combatant.IsFainted)
                {
                    _encounter.SetLevel(u.Id, level);
                }
            }
        }

        // REQ: level-progression.md#獎勵量 — each genuinely completed step pays once at its region's normal wild level;
        // the chain bonus needs every step earned, so a boss shortcut that skips steps pays neither for them.
        // Q-C31 (2026-11-09): quest XP is split by the same appearance record as the battle event that completed the step.
        private void PayQuest(HashSet<int> appeared)
        {
            bool all = true;
            for (int i = 0; i < _plan.Quest.Count; i++)
            {
                if (!_quest.IsEarned(i))
                {
                    all = false;
                    continue;
                }

                if (!_questPaid[i])
                {
                    _questPaid[i] = true;
                    int tier = _plan.Regions[_plan.Quest[i].Region].Tier;
                    Reward(XpSource.QuestStep, _progression.WildLevel(D0Difficulty, tier, SpawnRole.Normal), false,
                        RecordFor(_plan.Quest[i], appeared));
                }
            }

            if (all && !_chainPaid)
            {
                _chainPaid = true;
                Reward(XpSource.QuestChain, _wild[BossUnit()].Level, false, appeared);
            }
        }

        // SOL P2-12 R3-03: an elite step credited late (Q-E1 pre-credit) belongs to the battle that knocked the elite out,
        // not to whichever event advanced the tracker into it.
        private HashSet<int> RecordFor(RunQuestStep step, HashSet<int> eventRecord)
        {
            if (step.Kind != QuestStepKind.DefeatElite && step.Kind != QuestStepKind.DefeatBoss)
            {
                return eventRecord;
            }

            foreach (WildInfo w in _wild.Values)
            {
                if (w.Spawn == step.TargetSpawn && w.AppearedAtKnockout != null)
                {
                    return w.AppearedAtKnockout;
                }
            }

            return eventRecord;
        }

        private bool AnyEngaged()
        {
            foreach (KeyValuePair<int, WildInfo> kv in _wild)
            {
                if (_encounter.Units[kv.Key].Engaged && _encounter.Units[kv.Key].IsActive)
                {
                    return true;
                }
            }

            return false;
        }

        // REQ: combat.md#指定目標 (D0-W4) — Focus attacks the locked target when there is one, else the nearest candidate
        // (which then becomes the lock). The nearest fallback stays wild-only: destructibles.md#誰能打 lets the player
        // pick an object, never the AI.
        private void FocusNearest()
        {
            float3 player = PlayerPosition();
            if (_lockedObjectId >= 0 && IsObjectCandidate(_lockedObject, player))
            {
                FocusOnObject();
                return;
            }

            int target = IsFocusCandidate(_lockedTarget, player) ? _lockedTarget : NearestFocusTarget(player);
            if (target < 0)
            {
                return;
            }

            _lockedTarget = target;
            _lockedObjectId = -1;
            FocusOn(target);
        }

        private void FocusOn(int target)
        {
            if (!_encounter.Units[target].Engaged)
            {
                // Focusing a wild that has not noticed you starts the fight: the only way to battle a timid one.
                _encounter.Provoke(target);
            }

            _encounter.ClearObjectFocus();
            _command = new AiCommand(AiCommandKind.Focus, target);
        }

        // REQ: destructibles.md#誰能打 — hitting an object provokes nobody.
        private void FocusOnObject()
        {
            _encounter.FocusObject(_lockedObject);
            _command = new AiCommand(AiCommandKind.Focus, Encounter.ObjectTargetId);
        }

        // A CycleTarget candidate: a wild unit (Unit >= 0) or _objectCandidates[ObjectIndex].
        private readonly struct LockOption
        {
            internal LockOption(int unit, int objectIndex, float distance, long id)
            {
                Unit = unit;
                ObjectIndex = objectIndex;
                Distance = distance;
                Id = id;
            }

            internal readonly int Unit;
            internal readonly int ObjectIndex;
            internal readonly float Distance;
            internal readonly long Id;

            // (distance, wild before object, id): unit and object ids come from different spaces, so the kind breaks
            // ties before the id does.
            internal bool Before(in LockOption other)
            {
                if (Distance != other.Distance)
                {
                    return Distance < other.Distance;
                }

                bool wild = Unit >= 0;
                bool otherWild = other.Unit >= 0;
                return wild != otherWild ? wild : Id < other.Id;
            }
        }

        // Locks the next candidate by (distance, id) after the current lock, wrapping. Candidates are focusable wilds and
        // intact scene objects whose surface lies within ProvokeRadiusM (destructibles.md#誰能打); objects count from
        // their hit-sphere surface so a large building next to the player is not out of reach by its centre. While the
        // main is already under a Focus command the new lock is attacked at once, so cycling mid-fight retargets without
        // a second key press; otherwise it only marks the target and nothing is provoked.
        private void CycleTarget()
        {
            float3 player = PlayerPosition();
            _lockOptions.Clear();
            foreach (KeyValuePair<int, WildInfo> kv in _wild)
            {
                if (IsFocusCandidate(kv.Key, player))
                {
                    _lockOptions.Add(new LockOption(kv.Key, -1,
                        math.distance(_encounter.Units[kv.Key].Position, player), kv.Key));
                }
            }

            _objectCandidates.Clear();
            if (_worldObjects != null)
            {
                _worldObjects.Query(player, Encounter.ProvokeRadiusM + _worldObjects.MaxHitRadiusM, _objectCandidates);
                for (int i = 0; i < _objectCandidates.Count; i++)
                {
                    DestructibleCandidate c = _objectCandidates[i];
                    if (IsObjectCandidate(c, player))
                    {
                        _lockOptions.Add(new LockOption(-1, i, ObjectDistance(c, player), c.Id));
                    }
                }
            }

            if (_lockOptions.Count == 0)
            {
                _lockedTarget = -1;
                _lockedObjectId = -1;
                return;
            }

            // Insertion sort: a few dozen candidates at most, and no allocation (List.Sort with a lambda would allocate).
            for (int i = 1; i < _lockOptions.Count; i++)
            {
                LockOption o = _lockOptions[i];
                int j = i - 1;
                while (j >= 0 && o.Before(_lockOptions[j]))
                {
                    _lockOptions[j + 1] = _lockOptions[j];
                    j--;
                }

                _lockOptions[j + 1] = o;
            }

            int at = -1;
            for (int i = 0; i < _lockOptions.Count; i++)
            {
                LockOption o = _lockOptions[i];
                if (o.Unit >= 0 ? o.Unit == _lockedTarget : o.Id == _lockedObjectId)
                {
                    at = i;
                    break;
                }
            }

            LockOption next = _lockOptions[(at + 1) % _lockOptions.Count];
            if (next.Unit >= 0)
            {
                _lockedTarget = next.Unit;
                _lockedObjectId = -1;
            }
            else
            {
                _lockedTarget = -1;
                _lockedObject = _objectCandidates[next.ObjectIndex];
                _lockedObjectId = _lockedObject.Id;
                _lockedObjectNameKey = _worldObjects.NameKey(_lockedObject);
            }

            if (_command.Kind == AiCommandKind.Focus)
            {
                if (_lockedTarget >= 0)
                {
                    FocusOn(_lockedTarget);
                }
                else
                {
                    FocusOnObject();
                }
            }
        }

        private static float ObjectDistance(in DestructibleCandidate c, float3 player) =>
            math.max(0f, math.distance(c.Center, player) - c.HitRadiusM);

        private bool IsObjectCandidate(in DestructibleCandidate c, float3 player) =>
            !_objectTable.IsDestroyed(c.Id) && ObjectDistance(c, player) <= Encounter.ProvokeRadiusM;

        // Active wild that is already fighting, or close enough (Encounter.ProvokeRadiusM) to start a fight with.
        private bool IsFocusCandidate(int id, float3 player)
        {
            if (id < 0 || !_wild.TryGetValue(id, out WildInfo info) || info.Gone)
            {
                return false;
            }

            EncounterUnit u = _encounter.Units[id];
            return u.IsActive && (u.Engaged || math.distance(u.Position, player) <= Encounter.ProvokeRadiusM);
        }

        // A lock that stops being a candidate (knocked out, captured, destroyed, or out of reach) is dropped, so the
        // marker never points at something Focus would refuse. An object has no fight to keep the main on it once the
        // player walks off, so its Focus ends with the lock (a wild's Focus outlives the lock while the wild is engaged).
        private void ValidateLock(float3 player)
        {
            if (_lockedTarget >= 0 && !IsFocusCandidate(_lockedTarget, player))
            {
                _lockedTarget = -1;
            }

            if (_lockedObjectId >= 0 && !IsObjectCandidate(_lockedObject, player))
            {
                _lockedObjectId = -1;
                _encounter.ClearObjectFocus();
            }
        }

        private int NearestFocusTarget(float3 player)
        {
            int best = -1;
            float bestDist = float.MaxValue;
            foreach (KeyValuePair<int, WildInfo> kv in _wild)
            {
                if (!IsFocusCandidate(kv.Key, player))
                {
                    continue;
                }

                float d = math.distance(_encounter.Units[kv.Key].Position, player);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = kv.Key;
                }
            }

            return best;
        }

        private int BossUnit()
        {
            foreach (KeyValuePair<int, WildInfo> kv in _wild)
            {
                if (_plan.Spawns[kv.Value.Spawn].Role == SpawnRole.Boss)
                {
                    return kv.Key;
                }
            }

            return -1;
        }

        // Two presses within 3 s: the first only arms and explains, so a stray press cannot throw away a run.
        private void RequestAbandon()
        {
            if (_nowMs <= _abandonArmedUntilMs)
            {
                End(false, true);
            }
            else
            {
                _abandonArmedUntilMs = _nowMs + 3000;
                Toast(_copy.Ui("ui.hud.abandon_binding_confirm").Replace("{key}", BindingLabel(_abandonAction)));
            }
        }

        private void End(bool victory, bool abandoned)
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            _outcome.PartyExp.AddRange(_partyExp);
            _outcome.PlayerExp = _playerExp;
            _outcome.Victory = victory;
            _outcome.Abandoned = abandoned;
            _onEnd(_outcome);
        }

        // REQ: combat.md#實體碰撞 (D0-W2). Writes the transform as well as the body: PlayerPosition reads the transform, and
        // a second catch-up tick in the same frame must see the pushed position or it would push again.
        // Generation-side and presentation-side effects of a destruction; drops lie on the ground until the pickup rule
        // (Q-M6) exists.
        private void ApplyObjectEvents()
        {
            foreach (ObjectEvent ev in _encounter.ObjectEvents)
            {
                if (ev.Kind != ObjectEventKind.Destroyed)
                {
                    ShoveObject(ev);
                    continue;
                }

                _worldObjects.OnDestroyed(ev.ObjectId);
                ShatterObject(ev);
                if (WorldObjects.IsBuilding(ev.ObjectId))
                {
                    _settlementView?.RemoveBuilding((int)(ev.ObjectId - WorldObjects.BuildingIdBase));
                }
                else
                {
                    _scatterField?.Remove(ev.ObjectId);
                }
            }
        }

        // REQ: destructibles.md#物理 — a hit shoves the object (first hit wakes it into a rigidbody); presentation only.
        private void ShoveObject(in ObjectEvent ev)
        {
            if (_objectPhysics == null || !_worldObjects.TryGet(ev.ObjectId, out DestructibleCandidate c) ||
                !TryCasterPosition(ev.SourceId, out float3 source))
            {
                return;
            }

            float massKg = ObjectImpulse.MassKg(c.Composition, c.VolumeL);
            float3 impulse = ImpulseFor(ev, c, source, massKg);
            if (math.lengthsq(impulse) == 0f)
            {
                return;
            }

            GameObject root = ObjectRoot(ev.ObjectId);
            if (root != null)
            {
                _objectPhysics.Wake(ev.ObjectId, root, math.max(massKg, 1f), impulse, c.Center);
            }
        }

        // REQ: destructibles.md#破壞與掉落 — the object is replaced by debris where it stands now (a woken one may have
        // rolled away from its generated spot). The drawn scatter root is ours after ObjectRoot detaches it.
        private void ShatterObject(in ObjectEvent ev)
        {
            if (_objectPhysics == null || !_worldObjects.TryGet(ev.ObjectId, out DestructibleCandidate c))
            {
                return;
            }

            float massKg = ObjectImpulse.MassKg(c.Composition, c.VolumeL);
            float3 impulse = TryCasterPosition(ev.SourceId, out float3 source) ? ImpulseFor(ev, c, source, massKg) : float3.zero;
            GameObject woken = _objectPhysics.WokenRoot(ev.ObjectId);
            GameObject root = ObjectRoot(ev.ObjectId);
            float3 center = woken != null ? (float3)woken.GetComponent<Rigidbody>().worldCenterOfMass : c.Center;
            _objectPhysics.Shatter(ev.ObjectId, root, center, c.HitRadiusM, massKg, impulse, ev.Drop);
            if (root != null && !WorldObjects.IsBuilding(ev.ObjectId))
            {
                Destroy(root);
            }
        }

        private float3 ImpulseFor(in ObjectEvent ev, in DestructibleCandidate c, float3 source, float massKg)
        {
            int gust = (ev.Reactions & ObjectReaction.Gust) != 0 ? _envMaterials.GustImpulsePercent : 100;
            return ObjectImpulse.Compute(_rig.Config, source, c.Center, ev.Power, gust, massKg, _objectPhysics.Tuning);
        }

        // The drawn root of an object: its woken body, its building, or its scatter item (detached from streaming, since
        // the caller is about to turn it into a rigidbody or debris). Null when nothing is drawn there.
        private GameObject ObjectRoot(long id)
        {
            GameObject woken = _objectPhysics.WokenRoot(id);
            if (woken != null)
            {
                return woken;
            }

            if (WorldObjects.IsBuilding(id))
            {
                GameObject b = _settlementView != null ? _settlementView.Buildings[(int)(id - WorldObjects.BuildingIdBase)] : null;
                return b != null && b.activeSelf ? b : null;
            }

            return _scatterField != null ? _scatterField.Detach(id) : null;
        }

        private bool TryCasterPosition(int sourceId, out float3 position)
        {
            if (sourceId < 0 || sourceId >= _encounter.Units.Count)
            {
                position = default;
                return false;
            }

            position = _encounter.Units[sourceId].Position;
            return true;
        }

        private bool IsNight(float3 direction)
        {
            if (_rig == null || _rig.Sun == null)
            {
                return false;
            }

            float3 toSun = -(float3)_rig.Sun.transform.forward;
            return math.dot(math.normalize(direction), toSun) < 0f;
        }

        private void ApplyPlayerPush()
        {
            float3 push = _encounter.PlayerPush;
            if (math.all(push == float3.zero))
            {
                return;
            }

            Transform t = _rig.Character.transform;
            Vector3 moved = t.position + new Vector3(push.x, push.y, push.z);
            t.position = moved;
            _rig.Character.GetComponent<Rigidbody>().position = moved;
        }

        private float3 PlayerPosition()
        {
            Vector3 p = _rig.Character.transform.position;
            return new float3(p.x, p.y, p.z);
        }

        private float3 BesidePlayer(float3 player, int slot)
        {
            float3 up = _rig.Config.ToDirection(player);
            PlanetFrame f = PlanetFrame.FromUp(up);
            float side = slot == 0 ? 1.5f : -1.5f;
            return math.normalize(player + f.East * side - f.North * 1.5f);
        }

        private static Element ElementOfSurge(string affixId)
        {
            string name = affixId.Substring("affix_".Length, affixId.Length - "affix_".Length - "_surge".Length);
            return (Element)Enum.Parse(typeof(Element), name, true);
        }

        private static CreatureDef FindForm(IReadOnlyList<CreatureDef> pool, string id)
        {
            foreach (CreatureDef c in pool)
            {
                if (c.Id == id)
                {
                    return c;
                }
            }

            throw new InvalidOperationException("plan references a form outside the wild pool: " + id);
        }

        private void CreateView(EncounterUnit u, SpawnRole role)
        {
            // REQ: art-asset-pipeline.md#佔位模型 — the factory is the model-replacement seam; D0 uses placeholders.
            ICreatureView view = _viewFactory.Create(
                new CreatureViewRequest(u.Def, u.Side == EncounterSide.Player, role, u.BodyRadiusM));
            _views[u.Id] = view;
            view.Root.gameObject.SetActive(u.Fielded);
        }

        private void SyncViews()
        {
            foreach (EncounterUnit u in _encounter.Units)
            {
                ICreatureView view = _views[u.Id];
                Transform t = view.Root;
                bool visible = u.Fielded || (_wild.TryGetValue(u.Id, out WildInfo w) && !w.Gone);
                if (_wild.TryGetValue(u.Id, out WildInfo wi) && wi.Gone)
                {
                    visible = false;
                }

                if (t.gameObject.activeSelf != visible)
                {
                    t.gameObject.SetActive(visible);
                }

                _objectPhysics?.SyncPusher(u.Id, visible && !u.Combatant.IsFainted, u.Position, u.CollisionRadiusM);
                if (!visible)
                {
                    continue;
                }

                float3 up = _rig.Config.ToDirection(u.Position);
                t.SetPositionAndRotation(u.Position, quaternion.LookRotationSafe(u.Forward, up));
                if (u.Combatant.IsFainted)
                {
                    view.ShowFainted();
                }
            }
        }

        private void Toast(string text)
        {
            _toast = text;
            _toastUntilMs = _nowMs + 2500;
        }

        private void OnGUI()
        {
            if (_rig == null || _ended)
            {
                return;
            }

            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true };
            if (!_started)
            {
                GUI.Label(new Rect(20, 20, 600, 30), _copy.Ui("ui.star_select.loading"), _label);
                return;
            }

            DrawWorldBars();
            DrawLockMarker();
            DrawQuestPanel();
            DrawTeamPanel();
            DrawControlsPanel();
            DrawInteraction();
            if (_nowMs < _toastUntilMs)
            {
                GUI.Label(new Rect(Screen.width / 2f - 300, Screen.height * 0.3f, 600, 40),
                    "<b>" + _toast + "</b>", new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontSize = 24 });
            }
        }

        // REQ: combat.md#指定目標 (D0-W4). Drawn for idle targets too (DrawWorldBars skips those), above where the HP bar
        // and name sit, so the lock is visible before the fight starts.
        private void DrawLockMarker()
        {
            Vector3 head;
            if (_lockedObjectId >= 0)
            {
                // Over the top of the hit sphere: the drawn object roughly fills it.
                head = (Vector3)_lockedObject.Center +
                    (Vector3)_rig.Config.ToDirection(_lockedObject.Center) * (_lockedObject.HitRadiusM + 0.3f);
            }
            else if (_lockedTarget >= 0 && _views[_lockedTarget].Root.gameObject.activeSelf)
            {
                EncounterUnit u = _encounter.Units[_lockedTarget];
                head = (Vector3)u.Position + (Vector3)_rig.Config.ToDirection(u.Position) * (u.BodyRadiusM * 2.6f + 0.3f);
            }
            else
            {
                return;
            }

            Vector3 sp = _rig.Camera.WorldToScreenPoint(head);
            if (sp.z <= 0f)
            {
                return;
            }

            _markerStyle ??= new GUIStyle(_label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 28,
                normal = { textColor = new Color(1f, 0.85f, 0.2f) },
            };
            GUI.Label(new Rect(sp.x - 20, Screen.height - sp.y - 58, 40, 32), "▼", _markerStyle);
        }

        private string LockedTargetName() =>
            _lockedObjectId >= 0 ? _copy.Ui(_lockedObjectNameKey)
            : _lockedTarget >= 0 ? _copy.Name(_encounter.Units[_lockedTarget].Def.Id)
            : _copy.Ui("ui.hud.target_none");

        private void DrawInteraction()
        {
            var style = new GUIStyle(_label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            if (_interactTarget < 0)
            {
                if (_interactable < 0)
                {
                    return;
                }

                string prompt = _copy.Ui("ui.hud.interact_prompt").Replace("{key}", BindingLabel(_interactAction))
                    .Replace("{target}", _copy.Name(_encounter.Units[_interactable].Def.Id));
                GUI.Box(new Rect(Screen.width / 2f - 180, Screen.height - 330, 360, 40), GUIContent.none);
                GUI.Label(new Rect(Screen.width / 2f - 175, Screen.height - 328, 350, 36), prompt, style);
                return;
            }

            var box = new Rect(Screen.width / 2f - 230, Screen.height * 0.35f, 460, 230);
            GUI.Box(box, GUIContent.none);
            GUI.Label(new Rect(box.x + 10, box.y + 8, box.width - 20, 30),
                "<b>" + _copy.Name(_encounter.Units[_interactTarget].Def.Id) + "</b>", style);
            string[] keys = { "ui.interact.observe", "ui.interact.provoke", "ui.interact.pet", "ui.interact.close" };
            for (int i = 0; i < keys.Length; i++)
            {
                string text = _copy.Ui(keys[i]);
                GUI.Label(new Rect(box.x + 10, box.y + 42 + i * 30, box.width - 20, 28),
                    (int)_interactChoice == i ? "▶ <b>" + text + "</b> ◀" : text, style);
            }

            if (_observeText != null)
            {
                GUI.Label(new Rect(box.x + 10, box.y + 164, box.width - 20, 60), _observeText,
                    new GUIStyle(style) { fontSize = 14 });
            }
        }

        private void DrawWorldBars()
        {
            Camera cam = _rig.Camera;
            foreach (EncounterUnit u in _encounter.Units)
            {
                if (!_views[u.Id].Root.gameObject.activeSelf)
                {
                    continue;
                }

                bool wild = u.Side == EncounterSide.Wild;
                if (wild && !u.Engaged && !u.Combatant.IsFainted)
                {
                    continue;
                }

                Vector3 head = (Vector3)u.Position + (Vector3)_rig.Config.ToDirection(u.Position) * (u.BodyRadiusM * 2.6f + 0.3f);
                Vector3 sp = cam.WorldToScreenPoint(head);
                if (sp.z <= 0f || sp.z > 60f)
                {
                    continue;
                }

                var rect = new Rect(sp.x - 40, Screen.height - sp.y, 80, 8);
                GUI.DrawTexture(rect, Texture2D.blackTexture);
                float frac = u.Combatant.Hp / (float)u.Combatant.MaxHp;
                Color old = GUI.color;
                GUI.color = wild ? new Color(0.9f, 0.25f, 0.2f) : new Color(0.3f, 0.85f, 0.35f);
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * frac, rect.height), Texture2D.whiteTexture);
                GUI.color = old;
                string name = _copy.Name(u.Def.Id);
                if (wild && _plan.Spawns[_wild[u.Id].Spawn].Role == SpawnRole.Boss)
                {
                    name = _copy.Epithet(_plan.PlanetTypeId, (int)(_plan.Seed % 3)) + name;
                }

                if (u.Combatant.IsFainted)
                {
                    name += " · " + _copy.Ui("ui.hud.knocked_out");
                }

                GUI.Label(new Rect(sp.x - 120, Screen.height - sp.y - 24, 240, 22), name,
                    new GUIStyle(_label) { fontSize = 14, alignment = TextAnchor.MiddleCenter });
            }
        }

        private void DrawQuestPanel()
        {
            var box = new Rect(16, 16, 460, 210);
            GUI.Box(box, GUIContent.none);
            GUILayout.BeginArea(new Rect(box.x + 10, box.y + 6, box.width - 20, box.height - 12));
            GUILayout.Label("<b>" + _copy.ChainTitle(_plan.ChainId) + "</b>", _label);
            if (!_quest.IsComplete)
            {
                RunQuestStep s = _quest.Current;
                string text = _copy.Step(StepType(s.Kind), (int)(_plan.Seed % 3))
                    .Replace("{count}", (s.Count - _quest.Progress).ToString())
                    .Replace("{element}", _copy.Element(s.Element.ToString()))
                    .Replace("{poi}", _copy.Theme(_plan.Regions[s.Region].ThemeId))
                    .Replace("{boss}", _copy.Name(_plan.Spawns[_plan.Quest[_plan.Quest.Count - 1].TargetSpawn].CreatureId));
                GUILayout.Label(_copy.Ui("ui.hud.quest_tracker") + "：" + text,
                    new GUIStyle(_label) { wordWrap = true });
                if (TryQuestTarget(s, out float3 target))
                {
                    float3 here = _rig.Config.ToDirection(PlayerPosition());
                    float meters = math.acos(math.clamp(math.dot(here, target), -1f, 1f)) * _rig.Config.Radius;
                    // The bearing follows the view on the local tangent plane, including beyond the horizon.
                    bool hasBearing = DemoNavigation.TryBearing(here, target, _rig.CameraRig.Heading, out float angle);
                    string bearing = !hasBearing ? _copy.Ui("ui.hud.bearing_unavailable")
                        : math.abs(angle) < 10f ? _copy.Ui("ui.hud.bearing_ahead")
                        : _copy.Ui(angle < 0f ? "ui.hud.bearing_left" : "ui.hud.bearing_right")
                            .Replace("{angle}", math.abs(angle).ToString("0"));
                    GUILayout.Label($"{bearing} · {meters:0} m", _label);
                }
                else
                {
                    GUILayout.Label(_copy.Ui("ui.hud.no_quest_target"),
                        new GUIStyle(_label) { fontSize = 14, wordWrap = true });
                }
            }
            else
            {
                GUILayout.Label(_copy.Ui("ui.hud.quest_complete").Replace("{capture}", BindingLabel(_captureAction)),
                    new GUIStyle(_label) { wordWrap = true });
            }

            if (_region >= 0)
            {
                RunRegion r = _plan.Regions[_region];
                GUILayout.Label(_copy.Theme(r.ThemeId) + (r.AffixId != null ? " · " + _copy.AffixName(r.AffixId) + "：" + _copy.AffixDesc(r.AffixId) : string.Empty),
                    new GUIStyle(_label) { fontSize = 14, wordWrap = true });
            }

            GUILayout.EndArea();
        }

        internal bool TryQuestTarget(RunQuestStep step, out float3 target)
        {
            target = float3.zero;
            if (step.Kind == QuestStepKind.ReachPoi)
            {
                target = _plan.Regions[step.Region].Center;
                return true;
            }

            int capture = step.Kind == QuestStepKind.CaptureOne ? FindCaptureTarget(float.MaxValue) : -1;
            if (capture >= 0)
            {
                target = _rig.Config.ToDirection(_encounter.Units[capture].Position);
                return true;
            }

            float3 here = _rig.Config.ToDirection(PlayerPosition());
            float bestDot = -2f;
            foreach (KeyValuePair<int, WildInfo> kv in _wild)
            {
                EncounterUnit unit = _encounter.Units[kv.Key];
                if (kv.Value.Gone || unit.Combatant.IsFainted)
                {
                    continue;
                }

                if ((step.Kind == QuestStepKind.DefeatBoss || step.Kind == QuestStepKind.DefeatElite) &&
                    kv.Value.Spawn != step.TargetSpawn)
                {
                    continue;
                }

                if (step.Kind == QuestStepKind.DefeatWild && RunPlanner.MainElement(unit.Def) != step.Element)
                {
                    continue;
                }

                float3 direction = _rig.Config.ToDirection(unit.Position);
                float dot = math.dot(here, direction);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    target = direction;
                }
            }

            return bestDot > -2f;
        }

        private void DrawControlsPanel()
        {
            var box = new Rect(Screen.width - 346, 16, 330, 260);
            GUI.Box(box, GUIContent.none);
            GUI.Label(new Rect(box.x + 10, box.y + 6, box.width - 20, box.height - 12),
                _copy.Ui(Gamepad.current != null ? "ui.hud.gamepad_help" : "ui.hud.exploration_help"),
                new GUIStyle(_label) { fontSize = 14, wordWrap = true });

            int capture = FindCaptureTarget(CaptureReachM);
            if (capture < 0)
            {
                return;
            }

            float seconds = (KnockedOutWindowMs - (_nowMs - _wild[capture].KnockedOutAtMs)) / 1000f;
            string prompt = _copy.Ui("ui.hud.capture_available")
                .Replace("{capture}", BindingLabel(_captureAction)).Replace("{cycle}", BindingLabel(_cycleVesselAction))
                .Replace("{name}", _copy.Name(_encounter.Units[capture].Def.Id))
                .Replace("{seconds}", seconds.ToString("0.0"));
            GUI.Box(new Rect(Screen.width / 2f - 220, Screen.height - 280, 440, 50), GUIContent.none);
            GUI.Label(new Rect(Screen.width / 2f - 210, Screen.height - 277, 420, 44), prompt,
                new GUIStyle(_label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = true });
        }

        private void DrawTeamPanel()
        {
            float y = Screen.height - 220;
            var box = new Rect(16, y, math.min(900, Screen.width - 32), 210);
            GUI.Box(box, GUIContent.none);
            GUILayout.BeginArea(new Rect(box.x + 10, box.y + 6, box.width - 20, box.height - 12));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"◆ {_copy.Ui("ui.hud.hp")} {_vitals.Hp}/{_vitals.MaxHp}", new GUIStyle(_label) { fontSize = 14 },
                GUILayout.Width(120));
            foreach (int id in _team.Pool)
            {
                EncounterUnit u = _encounter.Units[id];
                bool active = In(_team.Active, id);
                string mark = id == _mainId ? "★" : active ? "●" : "○";
                string cd = u.Combatant.Cooldowns.IsReady(0, _nowMs) ? _copy.Ui("ui.hud.cd_ready")
                    : $"{(u.Combatant.Cooldowns.ReadyAtMs(0) - _nowMs) / 1000f:0.0}s";
                string state = u.Combatant.IsFainted ? _copy.Ui("ui.hud.knocked_out") : $"{_copy.Ui("ui.hud.hp")} {u.Combatant.Hp}/{u.Combatant.MaxHp}";
                Texture2D portrait = _portraits != null ? _portraits.FindCreature(u.Def.LineId, u.Def.Stage) : null;
                if (portrait != null)
                {
                    GUILayout.BeginHorizontal(GUILayout.Width(175));
                    Rect iconRect = GUILayoutUtility.GetRect(36f, 36f, GUILayout.ExpandWidth(false));
                    GUI.DrawTexture(iconRect, portrait, ScaleMode.ScaleToFit, true);
                    GUILayout.Label($"{mark} {_copy.Name(u.Def.Id)} LV{u.Level}\n{state}\n{(active ? cd : string.Empty)}",
                        new GUIStyle(_label) { fontSize = 14 }, GUILayout.Width(135));
                    GUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.Label($"{mark} {_copy.Name(u.Def.Id)} LV{u.Level}\n{state}\n{(active ? cd : string.Empty)}",
                        new GUIStyle(_label) { fontSize = 14 }, GUILayout.Width(145));
                }
            }

            GUILayout.EndHorizontal();
            long cdLeft = _team.SwitchCooldownRemainingMs(_nowMs);
            string cmd = _command.Kind switch
            {
                AiCommandKind.Focus => _copy.Ui("ui.hud.command_focus"),
                AiCommandKind.Follow => _copy.Ui("ui.hud.command_follow"),
                AiCommandKind.Retreat => _copy.Ui("ui.hud.command_retreat"),
                _ => _copy.Ui("ui.hud.command_free"),
            };
            string commands = _copy.Ui("ui.hud.command_bindings")
                .Replace("{focus}", BindingLabel(_focusAction)).Replace("{free}", BindingLabel(_freeAction))
                .Replace("{follow}", BindingLabel(_followAction)).Replace("{retreat}", BindingLabel(_retreatAction));
            GUILayout.Label($"[{BindingLabel(_selectFirstAction)} / {BindingLabel(_selectSecondAction)}] ★ {_copy.Ui("ui.hud.select_active")}  " +
                $"[{BindingLabel(_switchAction)}] {_copy.Ui("ui.hud.switch")}{(cdLeft > 0 ? $" ({cdLeft / 1000f:0.0}s)" : string.Empty)}\n" +
                $"{commands}（{cmd}）\n" +
                _copy.Ui("ui.hud.target_cycle").Replace("{key}", BindingLabel(_cycleTargetAction))
                    .Replace("{target}", LockedTargetName()) + "\n" +
                $"[{BindingLabel(_cycleVesselAction)}] {_copy.Ui("ui.hud.cycle_vessel")}：{_copy.Ui("ui.vessel." + VesselKey(_vessel))}  " +
                _copy.Ui("ui.hud.capture_prompt").Replace("{key}", BindingLabel(_captureAction)).Replace("{vessel}", _copy.Ui("ui.vessel." + VesselKey(_vessel))),
                new GUIStyle(_label) { fontSize = 14, wordWrap = true });
            GUILayout.EndArea();
        }

        private static bool In(IReadOnlyList<int> list, int id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == id)
                {
                    return true;
                }
            }

            return false;
        }

        private static string VesselKey(VesselKind v) => v == VesselKind.TalismanCard ? "talisman_card"
            : v == VesselKind.GlassBead ? "glass_bead" : "magic_cube";

        private static string StepType(QuestStepKind k) => k switch
        {
            QuestStepKind.ReachPoi => "reach_poi",
            QuestStepKind.DefeatWild => "defeat_wild",
            QuestStepKind.DefeatElite => "defeat_elite",
            QuestStepKind.CaptureOne => "capture_one",
            _ => "defeat_boss",
        };

        private void OnEnable() => _runInput?.Enable();

        private void OnDisable()
        {
            _runInput?.Disable();
            _menuInput?.Disable();
        }

        private void OnDestroy()
        {
            _runInput?.Dispose();
            _menuInput?.Dispose();
            _rig?.Dispose();
            if (_terrainMaterial != null)
            {
                Destroy(_terrainMaterial);
            }

            if (_playerMaterial != null)
            {
                Destroy(_playerMaterial);
            }

            foreach (ICreatureView view in _views.Values)
            {
                view.Dispose();
            }
        }
    }
}
