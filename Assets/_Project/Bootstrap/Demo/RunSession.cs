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

        /// <summary>Baked geo-templates available to this run (P3-02 output), with their terrain tags; may be empty.</summary>
        /// <remarks>Pre: Templates.Count == TemplateTags.Count. Post: none. Side effects: none.</remarks>
        public List<TemplateSource> Templates = new List<TemplateSource>();

        /// <summary>Terrain tags per template.</summary>
        /// <remarks>Pre: see Templates. Post: none. Side effects: none.</remarks>
        public List<IReadOnlyList<string>> TemplateTags = new List<IReadOnlyList<string>>();

        /// <summary>Party forms in party order (≤ 6; the first 4 are the deploy pool, P2-06).</summary>
        /// <remarks>Pre: 1..6 entries. Post: none. Side effects: none.</remarks>
        public List<CreatureDef> Party;

        /// <summary>Persistent instance id of each party slot (XP rounding tie-break, level-progression.md).</summary>
        /// <remarks>Pre: same count as Party; unique, &gt;= 1. Post: none. Side effects: none.</remarks>
        public List<int> PartyIds;

        /// <summary>Cumulative XP of each party slot; its level scales the slot's stats for the whole run.</summary>
        /// <remarks>Pre: same count as Party; each &gt;= 0. Post: none. Side effects: none.</remarks>
        public List<int> PartyExp;

        /// <summary>Approved level curve and XP rewards (balance.json#progression).</summary>
        /// <remarks>Pre: non-null. Post: none. Side effects: none.</remarks>
        public Progression Progression;

        /// <summary>Approved evolution material drop rules (balance.json#materials).</summary>
        /// <remarks>Pre: non-null. Post: none. Side effects: none.</remarks>
        public MaterialDrops Materials;
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
    public sealed class RunSession : MonoBehaviour
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

            public HeightSurface(PlanetHeight height, PlanetTemplates templates)
            {
                _height = height;
                _templates = templates;
            }

            // Same function the terrain mesh evaluates, so creatures stand on the rendered ground.
            public float GroundAltitude(float3 direction) =>
                _templates != null ? _templates.Sample(_height, direction) : _height.Sample(direction);
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
        private QuestTracker _quest;
        private Encounter _encounter;
        private TeamState _team;
        private readonly List<int> _partyUnits = new List<int>();
        private readonly HashSet<int> _appeared = new HashSet<int>();
        private readonly List<int> _injectedFaints = new List<int>();
        private Progression _progression;
        private int[] _partyExp;
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
        private AiCommand _command = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
        private VesselKind _vessel = VesselKind.TalismanCard;
        private int _region = -1;
        private bool _started;
        private bool _ended;
        private long _bossDownAtMs = -1;
        private long _abandonArmedUntilMs = -1;
        private string _toast = string.Empty;
        private long _toastUntilMs;
        private GUIStyle _label;
        private Material _terrainMaterial;
        private Material _playerMaterial;

        internal Encounter EncounterForTests => _encounter;

        internal RunPlan PlanForTests => _plan;

        internal PlanetRig RigForTests => _rig;

        internal bool StartedForTests => _started;

        internal VesselKind VesselForTests => _vessel;

        internal int MainForTests => _mainId;

        internal AiCommandKind CommandForTests => _command.Kind;

        // Seams for RunSessionProgressionTests (P2-12 regression). They expose state or call existing private flow; none
        // changes behaviour while _injectedFaints stays empty.

        /// <summary>Team rule state, so a test can read Active/InCombat without re-deriving them.</summary>
        /// <remarks>Pre: Configure ran. Post: the live TeamState (callers must not drive it). Side effects: none.</remarks>
        internal TeamState TeamForTests => _team;

        /// <summary>The quest tracker of this run.</summary>
        /// <remarks>Pre: Configure ran. Post: the live QuestTracker (read only for callers). Side effects: none.</remarks>
        internal QuestTracker QuestForTests => _quest;

        /// <summary>Simulation time of the last <see cref="StepCombatForTests"/> (ms).</summary>
        /// <remarks>Pre: none. Post: &gt;= 1000, equals the nowMs of the last Encounter.Tick. Side effects: none.</remarks>
        internal long NowMsForTests => _nowMs;

        /// <summary>Encounter unit id of each party slot, in party order.</summary>
        /// <remarks>Pre: Configure ran. Post: one id per RunSetup.Party slot. Side effects: none.</remarks>
        internal IReadOnlyList<int> PartyUnitsForTests => _partyUnits;

        /// <summary>Cumulative XP of one party slot right now.</summary>
        /// <remarks>Pre: 0 &lt;= slot &lt; party size. Post: &gt;= the slot's starting XP. Side effects: none.</remarks>
        internal int PartyExpForTests(int slot) => _partyExp[slot];

        /// <summary>Runs exactly one fixed combat step, the same code Update runs per 50 ms tick.</summary>
        /// <remarks>
        /// Pre: Configure ran; the component is disabled so Update does not tick as well.
        /// Post: _nowMs advanced by one tick.
        /// Side effects: everything StepCombat does (encounter tick, XP, level-ups, quest, end check).
        /// </remarks>
        internal void StepCombatForTests() => StepCombat();

        /// <summary>Replaces the placeholder view factory (model-replacement seam, art-asset-pipeline.md#佔位模型).</summary>
        /// <remarks>Pre: factory != null; called before Configure. Post: Configure builds every unit view with it. Side effects: replaces the selected factory.</remarks>
        internal void UseViewFactory(ICreatureViewFactory factory)
        {
            Core.Contracts.Contract.Requires(factory != null, "factory required");
            Core.Contracts.Contract.Requires(_plan == null, "view factory must be chosen before Configure");
            _viewFactory = factory;
        }

        /// <summary>Materials dropped so far this run (the list RunOutcome hands to the lobby).</summary>
        /// <remarks>Pre: none. Post: non-null, drop order. Side effects: none.</remarks>
        internal IReadOnlyList<MaterialStack> MaterialsForTests => _outcome.Materials;

        /// <summary>Swaps the main for the first eligible reserve, exactly as the Switch input does.</summary>
        /// <remarks>Pre: Configure ran. Post: as SwitchMain. Side effects: as SwitchMain (team, encounter, toast).</remarks>
        internal void SwitchMainForTests() => SwitchMain();

        /// <summary>Queues the knock-out of a wild unit that a test already took to 0 HP, to be settled in the next step.</summary>
        /// <remarks>
        /// Pre: wildUnitId is a wild unit with 0 HP whose knock-out has not been settled yet.
        /// Post: the next StepCombat settles it like an Encounter Faint event (after that tick's real events).
        /// Side effects: appends to the queue; nothing else until the next step.
        /// </remarks>
        internal void InjectFaintForTests(int wildUnitId)
        {
            Core.Contracts.Contract.Requires(_encounter != null && wildUnitId >= 0 && wildUnitId < _encounter.Units.Count,
                "unknown unit");
            Core.Contracts.Contract.Requires(_encounter.Units[wildUnitId].Side == EncounterSide.Wild &&
                _encounter.Units[wildUnitId].Combatant.IsFainted && _wild[wildUnitId].KnockedOutAtMs < 0,
                "only an unsettled fainted wild unit can be injected");
            _injectedFaints.Add(wildUnitId);
        }

        /// <summary>Starts the run.</summary>
        /// <remarks>
        /// Pre: all arguments non-null; actions contains the DemoRun map and its ten actions;
        ///      setup.Radius >= MoveGeometry.MinSupportedRadiusM; party 1..6 non-null forms with
        ///      matching PartyIds/PartyExp; setup.Progression non-null.
        /// Post: planet, plan and wild spawns exist; party is summoned once the ground is ready.
        /// Side effects: creates GameObjects and a private input map, enabled while the component is enabled.
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
            Core.Contracts.Contract.Requires(setup.Materials != null, "material drop rules are required");
            _copy = copy;
            _progression = setup.Progression;
            _drops = setup.Materials;
            _partyExp = setup.PartyExp.ToArray();
            _balance = balance;
            _actions = actions;
            _runInput = runMap.Clone();
            _selectFirstAction = _runInput.FindAction("SelectFirst", true);
            _selectSecondAction = _runInput.FindAction("SelectSecond", true);
            _switchAction = _runInput.FindAction("Switch", true);
            _focusAction = _runInput.FindAction("Focus", true);
            _freeAction = _runInput.FindAction("Free", true);
            _followAction = _runInput.FindAction("Follow", true);
            _retreatAction = _runInput.FindAction("Retreat", true);
            _cycleVesselAction = _runInput.FindAction("CycleVessel", true);
            _captureAction = _runInput.FindAction("Capture", true);
            _abandonAction = _runInput.FindAction("Abandon", true);
            if (isActiveAndEnabled)
            {
                _runInput.Enable();
            }
            _setup = setup;
            _onEnd = onEnd;

            var config = new PlanetConfig(setup.Radius, float3.zero);
            PlanetHeight height = PlanetHeight.ForPlanet(config, setup.Seed);
            PlanetTemplates templates = null;
            if (setup.Templates.Count > 0)
            {
                List<TemplatePlacement> placements = TemplatePlanner.Choose(setup.Seed, setup.PlanetType.TerrainTags,
                    setup.TemplateTags, TemplatePlanner.CountFor(setup.PlanetType.Size));
                templates = PlanetTemplates.Create(config, setup.Templates, placements);
            }

            var vocab = new RunPlanVocabulary(copy.ThemeIds, copy.GenericAffixIds, copy.ChainIds);
            System.Func<float3, bool> isLand = null;
            if (templates != null)
            {
                TemplateView view = templates.View;
                isLand = d => !view.IsSea(d);
            }

            _plan = RunPlanner.Generate(config, setup.Seed, setup.PlanetType, wildPool, vocab, isLand);
            _quest = new QuestTracker(_plan);
            _questPaid = new bool[_plan.Quest.Count];
            _viewFactory ??= new PlaceholderCreatureViewFactory(toonShader);
            if (terrainShader != null)
            {
                float relief = height.MaxAbsHeight + config.Radius * PlanetTemplates.AmplitudeFractionOfRadius * 0.5f;
                _terrainMaterial = PlanetLook.TerrainMaterial(terrainShader, setup.PlanetType.Id, config, relief);
                terrainMaterial = _terrainMaterial;
            }

            _rig = PlanetRig.Build(config, height, _plan.LandingDirection, terrainMaterial, templates);
            // The planet's spin axis is a property of this planet (fixed in world space), not "up" anywhere.
            gameObject.AddComponent<AtmosphereDriver>().Configure(_rig.Camera, _rig.Sun, config, new float3(0f, 1f, 0f),
                PlanetLook.Sky(setup.PlanetType.Id), 600f);
            if (starShader != null)
            {
                gameObject.AddComponent<StarField>().Configure(_rig.Camera, _rig.Sun, config, setup.Seed, starShader);
            }

            if (toonShader != null)
            {
                // Runtime primitives keep the built-in material, which renders magenta under URP.
                _playerMaterial = PlanetLook.CreatureMaterial(toonShader, Color.white, 0.03f);
                _rig.Character.GetComponentInChildren<Renderer>().sharedMaterial = _playerMaterial;
                var surface = new HeightSurface(height, templates);
                System.Func<float3, bool> isSea = templates != null ? (d => templates.View.IsSea(d)) : null;
                gameObject.AddComponent<ScatterField>().Configure(config, setup.Seed, setup.PlanetType.Id,
                    surface.GroundAltitude, isSea, PlayerPosition, toonShader);
            }

            _encounter = new Encounter(config, balance, new HeightSurface(height, templates), setup.Seed);
            _captureRng = new Pcg32(setup.Seed, 'K');
            // Own stream: drops must not shift capture or combat rolls, so the P2-12 XP regressions stay valid.
            _materialRng = new Pcg32(setup.Seed, 'D');

            for (int s = 0; s < _plan.Spawns.Count; s++)
            {
                RunSpawn spawn = _plan.Spawns[s];
                CreatureDef def = FindForm(wildPool, spawn.CreatureId);
                int level = _progression.WildLevel(D0Difficulty, _plan.Regions[spawn.Region].Tier, spawn.Role);
                EncounterUnit u = _encounter.AddCreature(EncounterSide.Wild, def, null, spawn.Direction, level);
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
            _encounter.Tick(_nowMs, TickMs, player, _command, _mainId);
            bool engaged = AnyEngaged();
            if (engaged && !_team.InCombat)
            {
                BeginCombat();
            }

            RecordAppearances();

            foreach (EncounterEvent ev in _encounter.Events)
            {
                if (ev.Kind == EncounterEventKind.Faint)
                {
                    OnFaint(ev.TargetId);
                }
            }

            // Test seam: empty in play. A knock-out a test produced out of band settles exactly like a Tick event.
            for (int i = 0; i < _injectedFaints.Count; i++)
            {
                OnFaint(_injectedFaints[i]);
            }

            _injectedFaints.Clear();

            if (!engaged && _team.InCombat)
            {
                _team.EndCombat();
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
            if (spawn.Role == SpawnRole.Boss)
            {
                _bossDownAtMs = _nowMs;
                Toast(_copy.Ui("ui.hud.knocked_out"));
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
                int target = NearestEngaged(PlayerPosition());
                _command = target >= 0 ? new AiCommand(AiCommandKind.Focus, target) : _command;
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
        }

        internal static InputActionMap RequireRunMap(InputActionAsset actions)
        {
            Core.Contracts.Contract.Requires(actions != null, "actions are required");
            InputActionMap map = actions.FindActionMap("DemoRun");
            Core.Contracts.Contract.Requires(map != null, "actions must contain the DemoRun map");
            foreach (string name in new[] { "SelectFirst", "SelectSecond", "Switch", "Focus", "Free", "Follow",
                         "Retreat", "CycleVessel", "Capture", "Abandon" })
            {
                Core.Contracts.Contract.Requires(map.FindAction(name) != null, "DemoRun is missing " + name);
            }

            return map;
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
            }
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

        private int NearestEngaged(float3 player)
        {
            int best = -1;
            float bestDist = float.MaxValue;
            foreach (KeyValuePair<int, WildInfo> kv in _wild)
            {
                EncounterUnit u = _encounter.Units[kv.Key];
                if (!u.Engaged || !u.IsActive)
                {
                    continue;
                }

                float d = math.distance(u.Position, player);
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

        private void End(bool victory, bool abandoned)
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            _outcome.PartyExp.AddRange(_partyExp);
            _outcome.Victory = victory;
            _outcome.Abandoned = abandoned;
            _onEnd(_outcome);
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
            DrawQuestPanel();
            DrawTeamPanel();
            DrawControlsPanel();
            if (_nowMs < _toastUntilMs)
            {
                GUI.Label(new Rect(Screen.width / 2f - 300, Screen.height * 0.3f, 600, 40),
                    "<b>" + _toast + "</b>", new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontSize = 24 });
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
            foreach (int id in _team.Pool)
            {
                EncounterUnit u = _encounter.Units[id];
                bool active = In(_team.Active, id);
                string mark = id == _mainId ? "★" : active ? "●" : "○";
                string cd = u.Combatant.Cooldowns.IsReady(0, _nowMs) ? _copy.Ui("ui.hud.cd_ready")
                    : $"{(u.Combatant.Cooldowns.ReadyAtMs(0) - _nowMs) / 1000f:0.0}s";
                string state = u.Combatant.IsFainted ? _copy.Ui("ui.hud.knocked_out") : $"{_copy.Ui("ui.hud.hp")} {u.Combatant.Hp}/{u.Combatant.MaxHp}";
                GUILayout.Label($"{mark} {_copy.Name(u.Def.Id)} LV{u.Level}\n{state}\n{(active ? cd : string.Empty)}",
                    new GUIStyle(_label) { fontSize = 14 }, GUILayout.Width(145));
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

        private void OnDisable() => _runInput?.Disable();

        private void OnDestroy()
        {
            _runInput?.Dispose();
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
