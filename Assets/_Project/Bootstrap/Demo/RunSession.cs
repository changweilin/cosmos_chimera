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
    /// <summary>What a finished run hands back to the lobby (Q-M4: only captures persist).</summary>
    /// <remarks>Pre: none. Post: Captured non-null. Side effects: none.</remarks>
    public sealed class RunOutcome
    {
        /// <summary>Boss knocked out.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool Victory;

        /// <summary>Player left mid-run.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool Abandoned;

        /// <summary>Captured (species id, vessel) in capture order.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public readonly List<(string speciesId, VesselKind vessel)> Captured = new List<(string, VesselKind)>();
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
    }

    /// <summary>
    /// One planet run of the D0 demo: terrain + character (PlanetRig), wild spawns from RunPlanner, real-time combat
    /// through Encounter, capture, quest tracking and an IMGUI HUD. REQ: docs/roadmap/demo.md.
    /// </summary>
    /// <remarks>
    /// Pre: Configure once right after AddComponent. Post: calls the end callback exactly once.
    /// Side effects: owns every object it creates and destroys them in OnDestroy.
    /// Input is read from Keyboard.current for the combat verbs; gamepad mapping is P7-07 (tech-debt D0-U1).
    /// </remarks>
    public sealed class RunSession : MonoBehaviour
    {
        private const int TickMs = 50;
        private const long KnockedOutWindowMs = 20000;
        private const float CaptureReachM = 8f;
        private const long AffixRefreshMs = 1000;

        private static readonly Color[] ElementColors =
        {
            new Color(0.75f, 0.75f, 0.8f), new Color(0.3f, 0.7f, 0.25f), new Color(0.2f, 0.5f, 0.95f),
            new Color(0.95f, 0.35f, 0.15f), new Color(0.6f, 0.45f, 0.25f), new Color(0.6f, 0.9f, 1f),
            new Color(0.6f, 0.95f, 0.7f), new Color(0.95f, 0.85f, 0.2f), new Color(1f, 0.97f, 0.75f),
            new Color(0.35f, 0.25f, 0.5f),
        };

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
            public long KnockedOutAtMs = -1;
            public bool Gone;
        }

        private DemoCopy _copy;
        private CombatBalance _balance;
        private InputActionAsset _actions;
        private RunSetup _setup;
        private Action<RunOutcome> _onEnd;

        private PlanetRig _rig;
        private RunPlan _plan;
        private QuestTracker _quest;
        private Encounter _encounter;
        private TeamState _team;
        private readonly List<int> _partyUnits = new List<int>();
        private readonly Dictionary<int, WildInfo> _wild = new Dictionary<int, WildInfo>();
        private readonly Dictionary<int, Transform> _views = new Dictionary<int, Transform>();
        private readonly Dictionary<int, Renderer> _renderers = new Dictionary<int, Renderer>();
        private readonly RunOutcome _outcome = new RunOutcome();
        private Pcg32 _captureRng;
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
        private Shader _toonShader;
        private Material _terrainMaterial;

        internal Encounter EncounterForTests => _encounter;

        internal RunPlan PlanForTests => _plan;

        internal PlanetRig RigForTests => _rig;

        internal bool StartedForTests => _started;

        /// <summary>Starts the run.</summary>
        /// <remarks>
        /// Pre: all arguments non-null; setup.Radius >= MoveGeometry.MinSupportedRadiusM; party 1..6 non-null forms.
        /// Post: planet, plan and wild spawns exist; party is summoned once the ground is ready.
        /// Side effects: creates GameObjects.
        /// </remarks>
        public void Configure(DemoCopy copy, CombatBalance balance, InputActionAsset actions, Material terrainMaterial,
            RunSetup setup, IReadOnlyList<CreatureDef> wildPool, Action<RunOutcome> onEnd, Shader terrainShader = null,
            Shader toonShader = null)
        {
            Core.Contracts.Contract.Requires(copy != null && balance != null && setup != null && onEnd != null,
                "copy, balance, setup and onEnd are required");
            Core.Contracts.Contract.Requires(setup.Party != null && setup.Party.Count >= 1 && setup.Party.Count <= 6,
                "party must hold 1..6 forms");
            _copy = copy;
            _balance = balance;
            _actions = actions;
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
            _toonShader = toonShader;
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
            _encounter = new Encounter(config, balance, new HeightSurface(height, templates), setup.Seed);
            _captureRng = new Pcg32(setup.Seed, 'K');

            for (int s = 0; s < _plan.Spawns.Count; s++)
            {
                RunSpawn spawn = _plan.Spawns[s];
                CreatureDef def = FindForm(wildPool, spawn.CreatureId);
                EncounterUnit u = _encounter.AddCreature(EncounterSide.Wild, def, null, spawn.Direction);
                _wild[u.Id] = new WildInfo { Spawn = s };
                CreateView(u, spawn.Role);
            }

            var roster = new List<int>();
            foreach (CreatureDef def in setup.Party)
            {
                EncounterUnit u = _encounter.AddCreature(EncounterSide.Player, def, null, _plan.LandingDirection);
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
            UpdateRegion(player);
            if (_nowMs >= _nextAffixMs)
            {
                _nextAffixMs = _nowMs + AffixRefreshMs;
                ApplyAffixes(player);
            }

            _encounter.Tick(_nowMs, TickMs, player, _command, _mainId);
            bool engaged = AnyEngaged();
            if (engaged && !_team.InCombat)
            {
                _team.BeginCombat();
            }

            foreach (EncounterEvent ev in _encounter.Events)
            {
                if (ev.Kind == EncounterEventKind.Faint)
                {
                    OnFaint(ev.TargetId);
                }
            }

            if (!engaged && _team.InCombat)
            {
                _team.EndCombat();
            }

            RefillField(player);
            ExpireKnockedOut();
            CheckEnd();
        }

        private void OnFaint(int unitId)
        {
            EncounterUnit u = _encounter.Units[unitId];
            if (u.Side == EncounterSide.Player)
            {
                if (!_team.InCombat)
                {
                    _team.BeginCombat();
                }

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
            _quest.OnKnockedOut(w.Spawn, RunPlanner.MainElement(u.Def));
            if (spawn.Role == SpawnRole.Boss)
            {
                _bossDownAtMs = _nowMs;
                Toast(_copy.Ui("ui.hud.knocked_out"));
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
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            if (kb.digit1Key.wasPressedThisFrame && _team.Active.Count > 0)
            {
                _mainId = _team.Active[0];
            }

            if (kb.digit2Key.wasPressedThisFrame && _team.Active.Count > 1)
            {
                _mainId = _team.Active[1];
            }

            if (kb.tabKey.wasPressedThisFrame)
            {
                SwitchMain();
            }

            if (kb.fKey.wasPressedThisFrame)
            {
                int target = NearestEngaged(PlayerPosition());
                _command = target >= 0 ? new AiCommand(AiCommandKind.Focus, target) : _command;
            }

            if (kb.rKey.wasPressedThisFrame)
            {
                _command = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
            }

            if (kb.tKey.wasPressedThisFrame)
            {
                _command = new AiCommand(AiCommandKind.Follow, CombatAi.NoTarget);
            }

            if (kb.gKey.wasPressedThisFrame)
            {
                _command = new AiCommand(AiCommandKind.Retreat, CombatAi.NoTarget);
            }

            if (kb.vKey.wasPressedThisFrame)
            {
                _vessel = (VesselKind)(((int)_vessel + 1) % 3);
            }

            if (kb.cKey.wasPressedThisFrame)
            {
                TryCapture();
            }

            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (_nowMs <= _abandonArmedUntilMs)
                {
                    End(false, true);
                }
                else
                {
                    _abandonArmedUntilMs = _nowMs + 3000;
                    Toast(_copy.Ui("ui.hud.abandon_confirm"));
                }
            }
        }

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
            float3 player = PlayerPosition();
            int best = -1;
            float bestDist = CaptureReachM;
            foreach (KeyValuePair<int, WildInfo> kv in _wild)
            {
                if (kv.Value.Gone || kv.Value.KnockedOutAtMs < 0)
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

            if (best < 0)
            {
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
                _outcome.Captured.Add((spawn.CreatureId, _vessel));
                _quest.OnCaptured();
                Toast(_copy.Ui("ui.hud.capture_success") + " " + _copy.Name(spawn.CreatureId));
            }
            else
            {
                Toast(_copy.Ui("ui.hud.capture_fail"));
            }
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
            }
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
            // REQ: art-asset-pipeline.md#佔位模型 — placeholder geometry by size class and main element until models land.
            GameObject go = GameObject.CreatePrimitive(u.Side == EncounterSide.Player ? PrimitiveType.Capsule : PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = (u.Side == EncounterSide.Player ? "Ally " : "Wild ") + u.Def.Id;
            float scale = u.BodyRadiusM * 2f * (role == SpawnRole.Boss ? 2.5f : role == SpawnRole.Elite ? 1.4f : 1f);
            go.transform.localScale = new Vector3(scale, scale, scale);
            var r = go.GetComponent<Renderer>();
            Color color = ElementColors[(int)RunPlanner.MainElement(u.Def)];
            if (_toonShader != null)
            {
                // Outline width is in world metres (the hull extrudes along the normalized world normal).
                r.material = PlanetLook.CreatureMaterial(_toonShader, color, 0.03f);
            }
            else
            {
                r.material.color = color;
            }
            _views[u.Id] = go.transform;
            _renderers[u.Id] = r;
            go.SetActive(u.Fielded);
        }

        private void SyncViews()
        {
            foreach (EncounterUnit u in _encounter.Units)
            {
                Transform t = _views[u.Id];
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
                    if (_toonShader != null)
                    {
                        _renderers[u.Id].material.SetColor("_BaseColor", Color.gray);
                    }
                    else
                    {
                        _renderers[u.Id].material.color = Color.gray;
                    }
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
                if (!_views[u.Id].gameObject.activeSelf)
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
            var box = new Rect(16, 16, 460, 150);
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
                GUILayout.Label(_copy.Ui("ui.hud.quest_tracker") + "：" + text, _label);
                float3 here = _rig.Config.ToDirection(PlayerPosition());
                float meters = math.acos(math.clamp(math.dot(here, _plan.Regions[s.Region].Center), -1f, 1f)) * _rig.Config.Radius;
                GUILayout.Label($"→ {meters:0} m", _label);
            }

            if (_region >= 0)
            {
                RunRegion r = _plan.Regions[_region];
                GUILayout.Label(_copy.Theme(r.ThemeId) + (r.AffixId != null ? " · " + _copy.AffixName(r.AffixId) + "：" + _copy.AffixDesc(r.AffixId) : string.Empty),
                    new GUIStyle(_label) { fontSize = 14, wordWrap = true });
            }

            GUILayout.EndArea();
        }

        private void DrawTeamPanel()
        {
            float y = Screen.height - 150;
            var box = new Rect(16, y, 620, 140);
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
                GUILayout.Label($"{mark} {_copy.Name(u.Def.Id)}\n{state}\n{(active ? cd : string.Empty)}",
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
            GUILayout.Label($"[1/2] ★ {_copy.Ui("ui.hud.select_active")}  [Tab] {_copy.Ui("ui.hud.switch")}{(cdLeft > 0 ? $" ({cdLeft / 1000f:0.0}s)" : string.Empty)}\n" +
                $"{_copy.Ui("ui.hud.command_keys")}（{cmd}）\n" +
                $"[V] {_copy.Ui("ui.hud.cycle_vessel")}：{_copy.Ui("ui.vessel." + VesselKey(_vessel))}  " +
                _copy.Ui("ui.hud.capture_prompt").Replace("{key}", "C").Replace("{vessel}", _copy.Ui("ui.vessel." + VesselKey(_vessel))),
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

        private void OnDestroy()
        {
            _rig?.Dispose();
            if (_terrainMaterial != null)
            {
                Destroy(_terrainMaterial);
            }

            foreach (Transform t in _views.Values)
            {
                if (t != null)
                {
                    Destroy(t.gameObject);
                }
            }
        }
    }
}
