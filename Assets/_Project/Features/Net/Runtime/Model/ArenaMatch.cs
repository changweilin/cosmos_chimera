using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Net.Api;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.Net.Runtime.Model
{
    /// <summary>One creature belonging to an <see cref="ArenaParticipant"/>.</summary>
    /// <remarks>
    /// Pre: created by <see cref="ArenaMatch"/>. Post: immutable identity and move set; runtime state mutated by ArenaMatch.
    /// Side effects: none.
    /// </remarks>
    public sealed class ArenaUnit
    {
        internal ArenaUnit(int unitId, int participantId, int teamIndex, int rosterIndex, CreatureDef def,
            Combatant combatant, CastMove[] moves, MoveDef[] moveDefs, HitVolume[] volumes, RangeClass[] rangeClasses,
            float bodyRadiusM, float collisionRadiusM, int agi, int level, bool overloadUnlocked, float3 position,
            float3 forward)
        {
            UnitId = unitId;
            ParticipantId = participantId;
            TeamIndex = teamIndex;
            RosterIndex = rosterIndex;
            Def = def;
            Combatant = combatant;
            Moves = moves;
            MoveDefs = moveDefs;
            Volumes = volumes;
            RangeClasses = rangeClasses;
            BodyRadiusM = bodyRadiusM;
            CollisionRadiusM = collisionRadiusM;
            Agi = agi;
            Level = level;
            OverloadUnlocked = overloadUnlocked;
            Position = position;
            Forward = forward;
        }

        /// <summary>Unique unit id within the match.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int UnitId { get; }

        /// <summary>Owning participant id.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int ParticipantId { get; }

        /// <summary>Owning team index.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int TeamIndex { get; }

        /// <summary>Index in the owning participant's roster (0..5).</summary>
        /// <remarks>Pre: none. Post: 0..5. Side effects: none.</remarks>
        public int RosterIndex { get; }

        /// <summary>Creature definition.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public CreatureDef Def { get; }

        /// <summary>Underlying combat state (HP, cooldowns, effects).</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public Combatant Combatant { get; }

        /// <summary>Castable moves (1 or 2 slots).</summary>
        /// <remarks>Pre: none. Post: length 1..2. Side effects: none.</remarks>
        public IReadOnlyList<CastMove> Moves { get; }

        internal MoveDef[] MoveDefs { get; }
        internal HitVolume[] Volumes { get; }
        internal RangeClass[] RangeClasses { get; }

        /// <summary>Hit-test sphere radius in meters.</summary>
        /// <remarks>Pre: none. Post: > 0. Side effects: none.</remarks>
        public float BodyRadiusM { get; }

        /// <summary>Physical separation radius in meters.</summary>
        /// <remarks>Pre: none. Post: > 0. Side effects: none.</remarks>
        public float CollisionRadiusM { get; }

        /// <summary>Agility at the unit's level.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Agi { get; }

        /// <summary>Level (1..100).</summary>
        /// <remarks>Pre: none. Post: 1..100. Side effects: none.</remarks>
        public int Level { get; }

        /// <summary>Whether adult overload is unlocked.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool OverloadUnlocked { get; }

        /// <summary>Whether once-per-combat overload has been activated.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool OverloadUsed { get; internal set; }

        /// <summary>World position on the planet surface.</summary>
        /// <remarks>Pre: none. Post: finite. Side effects: none.</remarks>
        public float3 Position { get; internal set; }

        /// <summary>Unit-length tangent facing direction.</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 Forward { get; internal set; }

        /// <summary>Whether the unit is currently fielded.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool Fielded { get; internal set; }

        /// <summary>True iff fielded and not fainted.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool IsActive => Fielded && !Combatant.IsFainted;
    }

    /// <summary>One participant (human or AI) in an <see cref="ArenaMatch"/>.</summary>
    /// <remarks>Pre: created by <see cref="ArenaMatch"/>. Post: owns TeamState and PlayerVitals. Side effects: none.</remarks>
    public sealed class ArenaParticipant
    {
        private readonly int[] _unitIdsByRosterIndex;

        internal ArenaParticipant(int participantId, int teamIndex, TeamState team, PlayerVitals vitals,
            float3 direction, float3 position, bool autoRefill, int[] unitIdsByRosterIndex)
        {
            ParticipantId = participantId;
            TeamIndex = teamIndex;
            Team = team;
            Vitals = vitals;
            Direction = direction;
            Position = position;
            AutoRefill = autoRefill;
            _unitIdsByRosterIndex = unitIdsByRosterIndex;
        }

        /// <summary>Unique participant id.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int ParticipantId { get; }

        /// <summary>Assigned team index.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int TeamIndex { get; }

        /// <summary>Carried team, deploy pool, active slots, and switch cooldown.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public TeamState Team { get; }

        /// <summary>Player character HP and dodge-roll i-frames.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public PlayerVitals Vitals { get; }

        /// <summary>Unit-length spherical direction of the player character.</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 Direction { get; internal set; }

        /// <summary>World position of the player character on the surface.</summary>
        /// <remarks>Pre: none. Post: finite. Side effects: none.</remarks>
        public float3 Position { get; internal set; }

        /// <summary>Current command issued to the participant's main creature.</summary>
        /// <remarks>Pre: none. Post: valid AiCommand. Side effects: none.</remarks>
        public AiCommand Command { get; internal set; } = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);

        /// <summary>Whether fainted active slots auto-summon from the pool.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool AutoRefill { get; }

        /// <summary>True once downed or all pool creatures are retired.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool IsEliminated { get; internal set; }

        /// <summary>Maps roster index (0..Roster.Count-1) to <see cref="ArenaUnit.UnitId"/>.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= rosterIndex &lt; Team.Roster.Count.
        /// Post: valid unit id in the owning ArenaMatch.
        /// Side effects: none.
        /// </remarks>
        public int UnitIdForRosterIndex(int rosterIndex)
        {
            Contract.Requires(rosterIndex >= 0 && rosterIndex < _unitIdsByRosterIndex.Length, "rosterIndex out of range");
            return _unitIdsByRosterIndex[rosterIndex];
        }
    }

    /// <summary>
    /// Host-authoritative pure C# simulation for 2–4 player PvP arena matches (FFA and 2v2).
    /// REQ: docs/design/multiplayer.md (Q-N2), docs/roadmap/phase-6.md (P6-05).
    /// </summary>
    /// <remarks>
    /// Reuses <see cref="Cosmimera.Combat"/> primitives (<see cref="TeamState"/>, <see cref="PlayerVitals"/>,
    /// <see cref="Combatant"/>, <see cref="MoveGeometry"/>, <see cref="HitResolver"/>, <see cref="CombatAi"/>) so
    /// arena combat obeys the exact same damage, cooldown, evasion, and spherical-geometry rules as planet encounters.
    /// </remarks>
    public sealed class ArenaMatch
    {
        private const int PlayerTargetBaseId = 1_000_000;

        private sealed class PlacedField
        {
            public PlacedField(int casterUnitId, int teamIndex, int slot, in CastContext ctx, in HitVolume volume,
                float3 anchor, float3 forward, FieldTimeline timeline)
            {
                CasterUnitId = casterUnitId;
                TeamIndex = teamIndex;
                Slot = slot;
                Context = ctx;
                Volume = volume;
                Anchor = anchor;
                Forward = forward;
                Timeline = timeline;
            }

            public int CasterUnitId { get; }
            public int TeamIndex { get; }
            public int Slot { get; }
            public CastContext Context { get; }
            public HitVolume Volume { get; }
            public float3 Anchor { get; }
            public float3 Forward { get; }
            public FieldTimeline Timeline { get; }
        }

        private readonly PlanetConfig _planet;
        private readonly CombatBalance _balance;
        private readonly IEncounterSurface _surface;
        private readonly ArenaConfig _config;
        private readonly int _rangeCapMm;
        private readonly List<ArenaParticipant> _participants = new List<ArenaParticipant>(4);
        private readonly Dictionary<int, ArenaParticipant> _participantById = new Dictionary<int, ArenaParticipant>(4);
        private readonly List<ArenaUnit> _units = new List<ArenaUnit>(24);
        private readonly List<ArenaEvent> _events = new List<ArenaEvent>(32);
        private readonly List<PlacedField> _fields = new List<PlacedField>(8);
        private readonly List<AiEnemy> _enemiesBuffer = new List<AiEnemy>(24);
        private readonly List<AiMoveOption> _movesBuffer = new List<AiMoveOption>(2);
        private readonly ReadOnlyCollection<ArenaParticipant> _participantsView;
        private readonly ReadOnlyCollection<ArenaUnit> _unitsView;
        private readonly ReadOnlyCollection<ArenaEvent> _eventsView;
        private Pcg32 _rng;
        private long _lastNowMs;

        /// <summary>Creates an arena match in <see cref="ArenaPhase.Lobby"/>.</summary>
        /// <remarks>
        /// Pre: balance != null; surface != null; planet.Radius > 0; participants != null;
        ///      FreeForAll: 2..4 participants with unique ParticipantId and unique TeamIndex;
        ///      Teams2v2: 4 participants with unique ParticipantId, TeamIndex in 0..1, and 2 participants per team.
        /// Post: Phase == ArenaPhase.Lobby; WinningTeamIndex == -1; all participants and units initialized.
        /// Side effects: allocates match state and combatants.
        /// </remarks>
        public ArenaMatch(in PlanetConfig planet, CombatBalance balance, IEncounterSurface surface,
            in ArenaConfig config, IReadOnlyList<ArenaParticipantConfig> participants)
        {
            Contract.Requires(planet.Radius > 0f, "planet.Radius must be > 0");
            Contract.Requires(balance != null, "balance must not be null");
            Contract.Requires(surface != null, "surface must not be null");
            Contract.Requires(participants != null, "participants must not be null");
            ValidateModeParticipants(config.Mode, participants);

            _planet = planet;
            _balance = balance;
            _surface = surface;
            _config = config;
            _rangeCapMm = _balance.Horizon.RangedCapMm(_planet.Radius, _balance.Stats.RangedCapMm);
            _rng = new Pcg32(config.Seed, 0x4152454EUL);

            _participantsView = _participants.AsReadOnly();
            _unitsView = _units.AsReadOnly();
            _eventsView = _events.AsReadOnly();

            BuildParticipants(participants);
        }

        /// <summary>Current match lifecycle phase.</summary>
        /// <remarks>Pre: none. Post: defined ArenaPhase. Side effects: none.</remarks>
        public ArenaPhase Phase { get; private set; } = ArenaPhase.Lobby;

        /// <summary>Winning team index when <see cref="Phase"/> is <see cref="ArenaPhase.Finished"/>, or -1 on draw/unfinished.</summary>
        /// <remarks>Pre: none. Post: >= -1. Side effects: none.</remarks>
        public int WinningTeamIndex { get; private set; } = -1;

        /// <summary>Match configuration.</summary>
        /// <remarks>Pre: none. Post: immutable ArenaConfig. Side effects: none.</remarks>
        public ArenaConfig Config => _config;

        /// <summary>All participants in the match.</summary>
        /// <remarks>Pre: none. Post: 2..4 entries. Side effects: none.</remarks>
        public IReadOnlyList<ArenaParticipant> Participants => _participantsView;

        /// <summary>All creature units across all participants.</summary>
        /// <remarks>Pre: none. Post: read-only view. Side effects: none.</remarks>
        public IReadOnlyList<ArenaUnit> Units => _unitsView;

        /// <summary>Events emitted during the most recent <see cref="Start"/>, command, or <see cref="Tick"/>.</summary>
        /// <remarks>Pre: none. Post: read-only view. Side effects: none.</remarks>
        public IReadOnlyList<ArenaEvent> Events => _eventsView;

        /// <summary>Starts the match: locks pools, begins combat, and summons up to 2 active creatures per participant.</summary>
        /// <remarks>
        /// Pre: Phase == ArenaPhase.Lobby; 0 &lt;= nowMs &lt;= CombatTime.MaxMs; nowMs >= watermark.
        /// Post: Phase == ArenaPhase.Active; every participant's TeamState.InCombat is true; opening summons emitted.
        /// Side effects: mutates Phase, TeamState, units, and Events; advances watermark to nowMs.
        /// </remarks>
        public void Start(long nowMs)
        {
            Contract.Requires(Phase == ArenaPhase.Lobby, "match already started");
            CombatTime.Require(nowMs, _lastNowMs);
            _lastNowMs = nowMs;
            _events.Clear();

            Phase = ArenaPhase.Active;
            for (int i = 0; i < _participants.Count; i++)
            {
                ArenaParticipant p = _participants[i];
                p.Team.BeginCombat();

                int toSummon = math.min(TeamState.MaxActive, p.Team.Pool.Count);
                for (int s = 0; s < toSummon; s++)
                {
                    int rosterIdx = p.Team.Pool[s];
                    TeamResult res = p.Team.Summon(rosterIdx, nowMs);
                    if (res == TeamResult.Ok)
                    {
                        DeployUnit(p, rosterIdx, bySwap: false, nowMs);
                    }
                }
            }
        }

        /// <summary>Updates a participant's spherical position inside the arena boundary.</summary>
        /// <remarks>
        /// Pre: Phase == ArenaPhase.Active; participantId exists; direction unit length.
        /// Post: participant Direction clamped within ArenaRadiusM of CenterDirection; Position updated on surface.
        /// Side effects: mutates participant Direction and Position.
        /// </remarks>
        public void SetPlayerDirection(int participantId, float3 direction)
        {
            Contract.Requires(Phase == ArenaPhase.Active, "match is not active");
            Contract.Requires(math.abs(math.lengthsq(direction) - 1f) <= 2e-3f, "direction must be unit length");
            ArenaParticipant p = RequireParticipant(participantId);
            if (p.IsEliminated)
            {
                return;
            }

            float3 clamped = ClampToArena(math.normalize(direction));
            p.Direction = clamped;
            p.Position = GroundPoint(clamped, Encounter.PlayerBodyRadiusM);
        }

        /// <summary>Updates the AI command for a participant's main creature.</summary>
        /// <remarks>
        /// Pre: Phase == ArenaPhase.Active; participantId exists.
        /// Post: participant Command equals command.
        /// Side effects: mutates participant Command.
        /// </remarks>
        public void SetAiCommand(int participantId, in AiCommand command)
        {
            Contract.Requires(Phase == ArenaPhase.Active, "match is not active");
            ArenaParticipant p = RequireParticipant(participantId);
            p.Command = command;
        }

        /// <summary>Attempts a dodge roll for a participant.</summary>
        /// <remarks>
        /// Pre: Phase == ArenaPhase.Active; participantId exists; 0 &lt;= nowMs &lt;= CombatTime.MaxMs; nowMs >= watermark.
        /// Post: returns RollResult from PlayerVitals.TryRoll.
        /// Side effects: mutates PlayerVitals on Ok; advances watermark to nowMs.
        /// </remarks>
        public RollResult TryRoll(int participantId, long nowMs)
        {
            Contract.Requires(Phase == ArenaPhase.Active, "match is not active");
            CombatTime.Require(nowMs, _lastNowMs);
            _lastNowMs = nowMs;
            ArenaParticipant p = RequireParticipant(participantId);
            return p.Vitals.TryRoll(nowMs);
        }

        /// <summary>Attempts to summon a pool creature for a participant.</summary>
        /// <remarks>
        /// Pre: Phase == ArenaPhase.Active; participantId exists; 0 &lt;= nowMs &lt;= CombatTime.MaxMs; nowMs >= watermark.
        /// Post: returns TeamResult; on Ok, unit becomes fielded and UnitSummoned event is emitted.
        /// Side effects: mutates TeamState, unit, Events; advances watermark to nowMs.
        /// </remarks>
        public TeamResult Summon(int participantId, int rosterIndex, long nowMs)
        {
            Contract.Requires(Phase == ArenaPhase.Active, "match is not active");
            CombatTime.Require(nowMs, _lastNowMs);
            _lastNowMs = nowMs;
            ArenaParticipant p = RequireParticipant(participantId);
            if (p.IsEliminated)
            {
                return TeamResult.Retired;
            }

            TeamResult result = p.Team.Summon(rosterIndex, nowMs);
            if (result == TeamResult.Ok)
            {
                DeployUnit(p, rosterIndex, bySwap: false, nowMs);
            }

            return result;
        }

        /// <summary>Attempts to recall a fielded creature for a participant.</summary>
        /// <remarks>
        /// Pre: Phase == ArenaPhase.Active; participantId exists; 0 &lt;= nowMs &lt;= CombatTime.MaxMs; nowMs >= watermark.
        /// Post: returns TeamResult; on Ok, unit leaves field and UnitRecalled event is emitted.
        /// Side effects: mutates TeamState, unit, Events; advances watermark to nowMs.
        /// </remarks>
        public TeamResult Recall(int participantId, int rosterIndex, long nowMs)
        {
            Contract.Requires(Phase == ArenaPhase.Active, "match is not active");
            CombatTime.Require(nowMs, _lastNowMs);
            _lastNowMs = nowMs;
            ArenaParticipant p = RequireParticipant(participantId);
            TeamResult result = p.Team.Recall(rosterIndex, nowMs);
            if (result == TeamResult.Ok)
            {
                ArenaUnit u = _units[p.UnitIdForRosterIndex(rosterIndex)];
                u.Fielded = false;
                _events.Add(new ArenaEvent(ArenaEventKind.UnitRecalled, p.ParticipantId, u.UnitId, -1, rosterIndex, 0));
            }

            return result;
        }

        /// <summary>Attempts to swap an active creature for a pooled creature.</summary>
        /// <remarks>
        /// Pre: Phase == ArenaPhase.Active; participantId exists; 0 &lt;= nowMs &lt;= CombatTime.MaxMs; nowMs >= watermark.
        /// Post: returns TeamResult; on Ok, outUnit leaves field, inUnit enters field, and UnitSwitched is emitted.
        /// Side effects: mutates TeamState, units, Events; advances watermark to nowMs.
        /// </remarks>
        public TeamResult Switch(int participantId, int outRosterIndex, int inRosterIndex, long nowMs)
        {
            Contract.Requires(Phase == ArenaPhase.Active, "match is not active");
            CombatTime.Require(nowMs, _lastNowMs);
            _lastNowMs = nowMs;
            ArenaParticipant p = RequireParticipant(participantId);
            if (p.IsEliminated)
            {
                return TeamResult.Retired;
            }

            TeamResult result = p.Team.Switch(outRosterIndex, inRosterIndex, nowMs);
            if (result == TeamResult.Ok)
            {
                ArenaUnit outUnit = _units[p.UnitIdForRosterIndex(outRosterIndex)];
                outUnit.Fielded = false;
                DeployUnit(p, inRosterIndex, bySwap: true, nowMs);
                int inUnitId = p.UnitIdForRosterIndex(inRosterIndex);
                _events.Add(new ArenaEvent(ArenaEventKind.UnitSwitched, p.ParticipantId, inUnitId, outUnit.UnitId,
                    inRosterIndex, 0));
            }

            return result;
        }

        /// <summary>Attempts to activate adult overload on an active creature.</summary>
        /// <remarks>
        /// Pre: Phase == ArenaPhase.Active; participantId exists; 0 &lt;= unitId &lt; Units.Count.
        /// Post: returns OverloadResult; on Ok, unit enters overload until combat ends.
        /// Side effects: mutates unit Combatant on Ok.
        /// </remarks>
        public OverloadResult TryOverload(int participantId, int unitId, long nowMs)
        {
            Contract.Requires(Phase == ArenaPhase.Active, "match is not active");
            CombatTime.Require(nowMs, _lastNowMs);
            _lastNowMs = nowMs;
            ArenaParticipant p = RequireParticipant(participantId);
            Contract.Requires(unitId >= 0 && unitId < _units.Count, "unitId out of range");
            ArenaUnit u = _units[unitId];
            if (u.ParticipantId != p.ParticipantId)
            {
                return OverloadResult.NotPlayerSide;
            }

            if (!u.Fielded)
            {
                return OverloadResult.NotFielded;
            }

            if (u.Combatant.IsFainted)
            {
                return OverloadResult.Fainted;
            }

            if (u.Def.Stage != CreatureStage.Adult)
            {
                return OverloadResult.NotAdult;
            }

            if (!u.OverloadUnlocked)
            {
                return OverloadResult.NotUnlocked;
            }

            if (u.Combatant.IsOverloaded)
            {
                return OverloadResult.AlreadyOverloaded;
            }

            if (u.OverloadUsed)
            {
                return OverloadResult.AlreadyUsedThisCombat;
            }

            u.OverloadUsed = true;
            u.Combatant.ActivateOverload();
            return OverloadResult.Ok;
        }

        /// <summary>Advances the arena simulation by <paramref name="dtMs"/> milliseconds.</summary>
        /// <remarks>
        /// Pre: Phase != ArenaPhase.Lobby; dtMs > 0; 0 &lt;= nowMs &lt;= CombatTime.MaxMs; nowMs >= watermark.
        /// Post: if Phase == ArenaPhase.Finished, Events cleared and no state changed; otherwise AI, casts, fields,
        ///       collisions, faints, eliminations, and win checks run deterministically.
        /// Side effects: mutates units, participants, RNG, and Events; advances watermark to nowMs.
        /// </remarks>
        public void Tick(long nowMs, int dtMs)
        {
            Contract.Requires(Phase != ArenaPhase.Lobby, "call Start before Tick");
            Contract.Requires(dtMs > 0, "dtMs must be > 0");
            CombatTime.Require(nowMs, _lastNowMs);
            long prevMs = _lastNowMs;
            _lastNowMs = nowMs;
            _events.Clear();

            if (Phase == ArenaPhase.Finished)
            {
                return;
            }

            TickEffectsAndFields(prevMs, nowMs);
            TickUnits(nowMs, dtMs);
            SeparateActiveUnits();
            ProcessEliminationsAndWin(nowMs);
        }

        private void ValidateModeParticipants(ArenaMode mode, IReadOnlyList<ArenaParticipantConfig> participants)
        {
            var seenIds = new HashSet<int>();
            var teamCounts = new Dictionary<int, int>();
            for (int i = 0; i < participants.Count; i++)
            {
                ArenaParticipantConfig cfg = participants[i];
                Contract.Requires(seenIds.Add(cfg.ParticipantId), "duplicate ParticipantId");
                teamCounts.TryGetValue(cfg.TeamIndex, out int count);
                teamCounts[cfg.TeamIndex] = count + 1;
            }

            if (mode == ArenaMode.FreeForAll)
            {
                Contract.Requires(participants.Count >= 2 && participants.Count <= 4,
                    "FreeForAll requires 2..4 participants");
                Contract.Requires(teamCounts.Count == participants.Count,
                    "FreeForAll requires each participant on a distinct TeamIndex");
            }
            else
            {
                Contract.Requires(participants.Count == 4, "Teams2v2 requires 4 participants");
                Contract.Requires(teamCounts.Count == 2 && teamCounts.TryGetValue(0, out int t0) && t0 == 2 &&
                    teamCounts.TryGetValue(1, out int t1) && t1 == 2,
                    "Teams2v2 requires 2 participants on Team 0 and 2 on Team 1");
            }
        }

        private void BuildParticipants(IReadOnlyList<ArenaParticipantConfig> configs)
        {
            float spawnDistM = math.min(_config.ArenaRadiusM * 0.6f, 15f);
            PlanetFrame centerFrame = PlanetFrame.FromUp(_config.CenterDirection);

            for (int i = 0; i < configs.Count; i++)
            {
                ArenaParticipantConfig cfg = configs[i];
                float angle = (2f * math.PI * i) / configs.Count;
                float3 tangentDir = math.normalize(centerFrame.East * math.cos(angle) + centerFrame.North * math.sin(angle));
                float3 spawnDir = OffsetDirection(_config.CenterDirection, tangentDir, spawnDistM);
                float3 spawnPos = GroundPoint(spawnDir, Encounter.PlayerBodyRadiusM);

                var rosterIds = new int[cfg.Roster.Count];
                var unitIdsByRoster = new int[cfg.Roster.Count];
                for (int r = 0; r < cfg.Roster.Count; r++)
                {
                    rosterIds[r] = r;
                    ArenaUnit unit = CreateUnit(cfg.ParticipantId, cfg.TeamIndex, r, cfg.Roster[r], spawnDir);
                    unitIdsByRoster[r] = unit.UnitId;
                    _units.Add(unit);
                }

                var team = new TeamState(rosterIds, _balance.SwitchCooldownMs);
                TeamResult poolRes = team.SetPool(cfg.InitialPoolIndices);
                Contract.Requires(poolRes == TeamResult.Ok, "initialPoolIndices failed TeamState.SetPool");

                var vitals = new PlayerVitals(_balance.Player);
                var participant = new ArenaParticipant(cfg.ParticipantId, cfg.TeamIndex, team, vitals, spawnDir,
                    spawnPos, cfg.AutoRefill, unitIdsByRoster);
                _participants.Add(participant);
                _participantById.Add(cfg.ParticipantId, participant);
            }
        }

        private ArenaUnit CreateUnit(int participantId, int teamIndex, int rosterIndex, in ArenaCreatureSpec spec,
            float3 direction)
        {
            int slots = spec.PartnerMove != null ? 2 : 1;
            var moves = new CastMove[slots];
            var moveDefs = new MoveDef[slots];
            var volumes = new HitVolume[slots];
            var ranges = new RangeClass[slots];
            CreatureStats s = spec.Def.Stats.AtLevel(spec.Level);
            for (int i = 0; i < slots; i++)
            {
                MoveDef m = i == 0 ? spec.Def.SignatureMove : spec.PartnerMove;
                moveDefs[i] = m;
                moves[i] = CastMove.FromDef(m);
                volumes[i] = MoveGeometry.Build(m.Shape, m.Movement, m.RangeClass, m.BurstOrigin, m.BaseRangeMm,
                    s.Str, _balance.Stats, _balance.Hit);
                ranges[i] = m.RangeClass;
            }

            var mastery = new int[10];
            var resist = new int[10];
            foreach (ElementValue ev in spec.Def.Mastery)
            {
                mastery[(int)ev.Element] = ev.Value;
            }

            for (int e = 0; e < 10; e++)
            {
                resist[e] = spec.Def.GetResist((Element)e);
            }

            EffectBook book = BookFor(spec.Def.Trait);
            var combatant = new Combatant(_balance.Stats, _balance.Effects, s.Con, s.Spi, s.Agi, mastery, resist, book, slots);
            float bodyRadiusM = Encounter.BodyRadiusFor(spec.Def.SizeClass);
            float collisionRadiusM = _balance.Collision.BodyMm(spec.Def.SizeClass) * 0.001f;
            float3 pos = GroundPoint(direction, bodyRadiusM);
            float3 fwd = PlanetFrame.FromUp(direction).North;

            return new ArenaUnit(_units.Count, participantId, teamIndex, rosterIndex, spec.Def, combatant, moves,
                moveDefs, volumes, ranges, bodyRadiusM, collisionRadiusM, s.Agi, spec.Level, spec.OverloadUnlocked,
                pos, fwd);
        }

        private void DeployUnit(ArenaParticipant p, int rosterIndex, bool bySwap, long nowMs)
        {
            int unitId = p.UnitIdForRosterIndex(rosterIndex);
            ArenaUnit u = _units[unitId];
            float3 dir = ClampToArena(p.Direction);
            u.Position = GroundPoint(dir, u.BodyRadiusM);
            u.Forward = PlanetFrame.FromUp(dir).North;
            u.Fielded = true;
            u.Combatant.EnterField(bySwap, nowMs);
            _events.Add(new ArenaEvent(ArenaEventKind.UnitSummoned, p.ParticipantId, unitId, -1, rosterIndex, 0));
        }

        private void TickEffectsAndFields(long prevMs, long nowMs)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                ArenaUnit u = _units[i];
                if (u.IsActive)
                {
                    u.Combatant.Book.Tick(nowMs);
                }
            }

            for (int i = _fields.Count - 1; i >= 0; i--)
            {
                PlacedField f = _fields[i];
                int due = f.Timeline.TicksDue(prevMs, nowMs);
                for (int t = 0; t < due; t++)
                {
                    ResolveVolume(f.CasterUnitId, f.TeamIndex, f.Slot, f.Context, f.Volume, f.Anchor, f.Forward, nowMs);
                }

                if (f.Timeline.IsExpired(nowMs))
                {
                    _fields.RemoveAt(i);
                }
            }
        }

        private void TickUnits(long nowMs, int dtMs)
        {
            for (int pIdx = 0; pIdx < _participants.Count; pIdx++)
            {
                ArenaParticipant p = _participants[pIdx];
                if (p.IsEliminated)
                {
                    continue;
                }

                BuildEnemyBuffer(p.TeamIndex);
                if (_enemiesBuffer.Count == 0)
                {
                    continue;
                }

                IReadOnlyList<int> active = p.Team.Active;
                ArenaUnit main = active.Count >= 1 ? _units[p.UnitIdForRosterIndex(active[0])] : null;
                ArenaUnit support = active.Count >= 2 ? _units[p.UnitIdForRosterIndex(active[1])] : null;

                int mainTargetId = CombatAi.NoTarget;
                if (main != null && main.IsActive)
                {
                    AiDecision mainDec = StepUnitAi(main, support, AiRole.Main, p.Command, CombatAi.NoTarget,
                        p.Position, nowMs, dtMs);
                    mainTargetId = mainDec.TargetId;
                }

                if (support != null && support.IsActive)
                {
                    StepUnitAi(support, main, AiRole.Support, p.Command, mainTargetId, p.Position, nowMs, dtMs);
                }
            }
        }

        private AiDecision StepUnitAi(ArenaUnit u, ArenaUnit partner, AiRole role, in AiCommand command, int mainTargetId,
            float3 playerPos, long nowMs, int dtMs)
        {
            bool hasPartner = partner != null && partner.IsActive;
            float3 partnerPos = hasPartner ? partner.Position : float3.zero;
            int partnerHp = hasPartner ? partner.Combatant.Hp : 0;
            int partnerMaxHp = hasPartner ? partner.Combatant.MaxHp : 1;

            _movesBuffer.Clear();
            for (int s = 0; s < u.Moves.Count; s++)
            {
                HitVolume scaled = MoveGeometry.ApplyPlanetCap(EffectMath.ApplyRange(u.Volumes[s], u.RangeClasses[s],
                    0, _balance.Stats, _balance.Effects), u.RangeClasses[s], _rangeCapMm);
                AiMoveTargeting targeting = TargetingOf(u.MoveDefs[s].Shape);
                AiMoveCategory category = CategoryOf(u.MoveDefs[s]);
                bool ready = u.Combatant.Cooldowns.IsReady(s, nowMs);
                int healPct = u.Moves[s].HasEffect && u.Moves[s].Effect.Type == EffectType.HealPercentMaxHp
                    ? u.Moves[s].Effect.Percent
                    : 0;
                _movesBuffer.Add(new AiMoveOption(s, targeting, CombatAi.ReachMmOf(scaled), u.Moves[s].Power, ready,
                    category, healPct));
            }

            var input = new AiInput(u.Position, u.Def.AiStyle, role, command, mainTargetId, playerPos, hasPartner,
                partnerPos, u.Combatant.Hp, u.Combatant.MaxHp, partnerHp, partnerMaxHp);

            AiDecision decision = CombatAi.Decide(_planet, _balance.Ai, input, _movesBuffer, _enemiesBuffer);
            MoveUnitToward(u, decision.DestinationDirection, nowMs, dtMs);
            if (decision.TargetId != CombatAi.NoTarget)
            {
                float3 targetPos = TargetPosition(decision.TargetId);
                FaceToward(u, targetPos);
            }

            if (decision.CastSlot != CombatAi.NoCast)
            {
                TryCast(u, partner, decision.CastSlot, decision.TargetId, nowMs);
            }

            return decision;
        }

        private void BuildEnemyBuffer(int myTeamIndex)
        {
            _enemiesBuffer.Clear();
            for (int i = 0; i < _units.Count; i++)
            {
                ArenaUnit other = _units[i];
                if (other.TeamIndex != myTeamIndex && other.IsActive)
                {
                    _enemiesBuffer.Add(new AiEnemy(other.UnitId, other.Position, other.Forward, true));
                }
            }

            for (int i = 0; i < _participants.Count; i++)
            {
                ArenaParticipant other = _participants[i];
                if (other.TeamIndex != myTeamIndex && !other.IsEliminated && !other.Vitals.IsDowned)
                {
                    float3 fwd = PlanetFrame.FromUp(other.Direction).North;
                    _enemiesBuffer.Add(new AiEnemy(PlayerTargetBaseId + i, other.Position, fwd, true));
                }
            }
        }

        private void TryCast(ArenaUnit u, ArenaUnit partner, int slot, int targetId, long nowMs)
        {
            CastMove move = u.Moves[slot];
            if (u.Combatant.BeginCast(slot, move, nowMs, WeatherProfiles.Clear, out CastContext ctx) != CastResult.Accepted)
            {
                return;
            }

            _events.Add(new ArenaEvent(ArenaEventKind.Cast, u.ParticipantId, u.UnitId, targetId, slot, 0));
            HitVolume volume = MoveGeometry.ApplyPlanetCap(EffectMath.ApplyRange(u.Volumes[slot], u.RangeClasses[slot],
                ctx.RangePercentSum, _balance.Stats, _balance.Effects), u.RangeClasses[slot], _rangeCapMm);

            if (volume.Kind == HitVolumeKind.Caster)
            {
                ApplySupport(u, u, ctx, slot, nowMs);
                return;
            }

            if (volume.Kind == HitVolumeKind.Ally)
            {
                if (partner != null && partner.IsActive && MoveGeometry.Overlaps(volume, _planet, u.Position, u.Forward,
                        partner.Position, partner.BodyRadiusM))
                {
                    ApplySupport(u, partner, ctx, slot, nowMs);
                }

                return;
            }

            float3 anchor = u.Position;
            if (volume.Anchor == HitVolumeAnchor.AimPoint)
            {
                float3 aim = targetId != CombatAi.NoTarget
                    ? TargetPosition(targetId)
                    : u.Position + u.Forward * (volume.CastDistanceMm * 0.001f);
                anchor = MoveGeometry.ResolveAnchor(volume, _planet, u.Position, aim);
            }

            if (u.MoveDefs[slot].Shape == MoveShape.Field)
            {
                var timeline = new FieldTimeline(nowMs, u.MoveDefs[slot].FieldDurationMs, _balance.Effects.FieldTickMs);
                var field = new PlacedField(u.UnitId, u.TeamIndex, slot, ctx, volume, anchor, u.Forward, timeline);
                _fields.Add(field);
                int due = timeline.TicksDue(nowMs - 1, nowMs);
                for (int k = 0; k < due; k++)
                {
                    ResolveVolume(u.UnitId, u.TeamIndex, slot, ctx, volume, anchor, u.Forward, nowMs);
                }

                return;
            }

            ResolveVolume(u.UnitId, u.TeamIndex, slot, ctx, volume, anchor, u.Forward, nowMs);

            if (volume.DashDistanceMm > 0)
            {
                float3 endDir = ClampToArena(math.normalize(
                    MoveGeometry.ResolveDashEnd(volume, _planet, u.Position, u.Forward) - _planet.Center));
                u.Position = GroundPoint(endDir, u.BodyRadiusM);
                u.Forward = PlanetFrame.FromUp(endDir).ProjectOnTangent(u.Forward);
                if (math.lengthsq(u.Forward) < 1e-6f)
                {
                    u.Forward = PlanetFrame.FromUp(endDir).North;
                }
                else
                {
                    u.Forward = math.normalize(u.Forward);
                }
            }
        }

        private void ResolveVolume(int casterUnitId, int casterTeamIndex, int slot, in CastContext ctx,
            in HitVolume volume, float3 anchor, float3 forward, long nowMs)
        {
            ArenaUnit caster = _units[casterUnitId];
            for (int i = 0; i < _units.Count; i++)
            {
                ArenaUnit t = _units[i];
                if (t.TeamIndex == casterTeamIndex || !t.IsActive)
                {
                    continue;
                }

                bool hit = MoveGeometry.Overlaps(volume, _planet, anchor, forward, t.Position, t.BodyRadiusM);
                if (!hit)
                {
                    continue;
                }

                HitReport report = t.Combatant.ReceiveHostile(ctx, true, ref _rng, nowMs);
                if (report.Outcome == HitOutcome.Miss)
                {
                    _events.Add(new ArenaEvent(ArenaEventKind.Miss, caster.ParticipantId, casterUnitId, t.UnitId, slot, 0));
                }
                else if (report.Outcome == HitOutcome.Hit)
                {
                    _events.Add(new ArenaEvent(ArenaEventKind.Hit, caster.ParticipantId, casterUnitId, t.UnitId, slot,
                        report.Damage));
                    if (report.Fainted)
                    {
                        HandleUnitFainted(t, nowMs);
                    }
                }
            }

            for (int i = 0; i < _participants.Count; i++)
            {
                ArenaParticipant p = _participants[i];
                if (p.TeamIndex == casterTeamIndex || p.IsEliminated || p.Vitals.IsDowned)
                {
                    continue;
                }

                if (!MoveGeometry.Overlaps(volume, _planet, anchor, forward, p.Position, Encounter.PlayerBodyRadiusM))
                {
                    continue;
                }

                int chance = _balance.Stats.HitChancePermille(ctx.Move.AccuracyPercent, 0);
                HitOutcome outcome = HitResolver.Resolve(true, p.Vitals.IsInvulnerable(nowMs), chance, ref _rng);
                if (outcome == HitOutcome.Miss)
                {
                    _events.Add(new ArenaEvent(ArenaEventKind.Miss, caster.ParticipantId, casterUnitId,
                        PlayerTargetBaseId + i, slot, 0));
                }
                else if (outcome == HitOutcome.Hit && ctx.Move.DealsDamage)
                {
                    int dmg = DamageModel.Compute(ctx.Move.Power, ctx.MasteryPercent, 0, ctx.PowerDeltaPercent);
                    int dealt = p.Vitals.ApplyDamage(dmg, nowMs);
                    _events.Add(new ArenaEvent(ArenaEventKind.Hit, caster.ParticipantId, casterUnitId,
                        PlayerTargetBaseId + i, slot, dealt));
                    if (p.Vitals.IsDowned)
                    {
                        _events.Add(new ArenaEvent(ArenaEventKind.PlayerDowned, p.ParticipantId, casterUnitId,
                            PlayerTargetBaseId + i, slot, 0));
                    }
                }
            }
        }

        private void ApplySupport(ArenaUnit caster, ArenaUnit target, in CastContext ctx, int slot, long nowMs)
        {
            HitReport report = target.Combatant.ReceiveSupport(ctx, nowMs);
            _events.Add(new ArenaEvent(ArenaEventKind.Support, caster.ParticipantId, caster.UnitId, target.UnitId, slot,
                report.Healed));
        }

        private void HandleUnitFainted(ArenaUnit unit, long nowMs)
        {
            ArenaParticipant owner = _participantById[unit.ParticipantId];
            if (unit.Fielded)
            {
                unit.Fielded = false;
                owner.Team.Faint(unit.RosterIndex);
            }

            _events.Add(new ArenaEvent(ArenaEventKind.UnitFainted, owner.ParticipantId, unit.UnitId, -1,
                unit.RosterIndex, 0));

            if (owner.AutoRefill && !owner.IsEliminated && !owner.Team.IsWipedOut &&
                owner.Team.SwitchCooldownRemainingMs(nowMs) == 0)
            {
                IReadOnlyList<int> pool = owner.Team.Pool;
                for (int i = 0; i < pool.Count; i++)
                {
                    int candidate = pool[i];
                    if (owner.Team.Summon(candidate, nowMs) == TeamResult.Ok)
                    {
                        DeployUnit(owner, candidate, bySwap: false, nowMs);
                        break;
                    }
                }
            }
        }

        private void ProcessEliminationsAndWin(long nowMs)
        {
            int survivingTeams = 0;
            int lastSurvivingTeam = -1;
            var seenTeams = new HashSet<int>();

            for (int i = 0; i < _participants.Count; i++)
            {
                ArenaParticipant p = _participants[i];
                if (!p.IsEliminated && (p.Vitals.IsDowned || p.Team.IsWipedOut))
                {
                    p.IsEliminated = true;
                    for (int r = 0; r < p.Team.Roster.Count; r++)
                    {
                        ArenaUnit u = _units[p.UnitIdForRosterIndex(r)];
                        u.Fielded = false;
                    }

                    _events.Add(new ArenaEvent(ArenaEventKind.ParticipantEliminated, p.ParticipantId, -1, -1, -1, 0));
                }

                if (!p.IsEliminated && seenTeams.Add(p.TeamIndex))
                {
                    survivingTeams++;
                    lastSurvivingTeam = p.TeamIndex;
                }
            }

            if (survivingTeams <= 1)
            {
                Phase = ArenaPhase.Finished;
                WinningTeamIndex = survivingTeams == 1 ? lastSurvivingTeam : -1;
                for (int i = 0; i < _participants.Count; i++)
                {
                    if (_participants[i].Team.InCombat)
                    {
                        _participants[i].Team.EndCombat();
                    }
                }

                _events.Add(new ArenaEvent(ArenaEventKind.MatchFinished, -1, -1, -1, -1, WinningTeamIndex));
            }
        }

        private void MoveUnitToward(ArenaUnit u, float3 destDirection, long nowMs, int dtMs)
        {
            float3 currentDir = math.normalize(u.Position - _planet.Center);
            float dot = math.clamp(math.dot(currentDir, destDirection), -1f, 1f);
            float angle = math.acos(dot);
            float distM = angle * _planet.Radius;
            if (distM < 1e-3f)
            {
                return;
            }

            float speedM = u.Combatant.MoveSpeedMmS(nowMs) * 0.001f;
            float maxStepM = speedM * (dtMs * 0.001f);
            float stepM = math.min(distM, maxStepM);
            float3 cross = math.cross(currentDir, destDirection);
            if (math.lengthsq(cross) < 1e-10f)
            {
                return;
            }

            float3 tangent = math.normalize(math.cross(cross, currentDir));
            float stepAngle = stepM / _planet.Radius;
            float3 nextDir = ClampToArena(math.normalize(currentDir * math.cos(stepAngle) + tangent * math.sin(stepAngle)));
            u.Position = GroundPoint(nextDir, u.BodyRadiusM);
            u.Forward = tangent;
        }

        private void FaceToward(ArenaUnit u, float3 targetPos)
        {
            float3 up = math.normalize(u.Position - _planet.Center);
            float3 delta = targetPos - u.Position;
            float3 tangent = delta - math.dot(delta, up) * up;
            if (math.lengthsq(tangent) > 1e-6f)
            {
                u.Forward = math.normalize(tangent);
            }
        }

        private void SeparateActiveUnits()
        {
            for (int i = 0; i < _units.Count; i++)
            {
                ArenaUnit a = _units[i];
                if (!a.IsActive)
                {
                    continue;
                }

                for (int j = i + 1; j < _units.Count; j++)
                {
                    ArenaUnit b = _units[j];
                    if (!b.IsActive)
                    {
                        continue;
                    }

                    float3 da = math.normalize(a.Position - _planet.Center);
                    float3 db = math.normalize(b.Position - _planet.Center);
                    float arc = math.acos(math.clamp(math.dot(da, db), -1f, 1f)) * _planet.Radius;
                    float minArc = a.CollisionRadiusM + b.CollisionRadiusM;
                    if (arc >= minArc)
                    {
                        continue;
                    }

                    float overlap = minArc - arc;
                    float3 n = math.cross(da, db);
                    float3 headingB = math.length(n) > 1e-6f
                        ? math.normalize(math.cross(n, db))
                        : PlanetFrame.FromUp(da).East;
                    float3 headingA = math.length(n) > 1e-6f
                        ? -math.normalize(math.cross(n, da))
                        : -headingB;

                    float turn = (overlap * 0.5f) / _planet.Radius;
                    da = ClampToArena(math.normalize(da * math.cos(turn) + headingA * math.sin(turn)));
                    db = ClampToArena(math.normalize(db * math.cos(turn) + headingB * math.sin(turn)));
                    a.Position = GroundPoint(da, a.BodyRadiusM);
                    b.Position = GroundPoint(db, b.BodyRadiusM);
                }
            }
        }

        private float3 ClampToArena(float3 dir)
        {
            float dot = math.clamp(math.dot(_config.CenterDirection, dir), -1f, 1f);
            float angle = math.acos(dot);
            float maxAngle = _config.ArenaRadiusM / _planet.Radius;
            if (angle <= maxAngle)
            {
                return dir;
            }

            float3 tangent = dir - dot * _config.CenterDirection;
            if (math.lengthsq(tangent) < 1e-10f)
            {
                tangent = PlanetFrame.FromUp(_config.CenterDirection).North;
            }
            else
            {
                tangent = math.normalize(tangent);
            }

            return math.normalize(_config.CenterDirection * math.cos(maxAngle) + tangent * math.sin(maxAngle));
        }

        private float3 OffsetDirection(float3 fromDir, float3 tangentDir, float distanceM)
        {
            float angle = distanceM / _planet.Radius;
            return math.normalize(fromDir * math.cos(angle) + tangentDir * math.sin(angle));
        }

        private float3 GroundPoint(float3 direction, float bodyRadiusM)
        {
            float alt = _surface.GroundAltitude(direction);
            return _planet.Center + direction * (_planet.Radius + alt + bodyRadiusM);
        }

        private float3 TargetPosition(int targetId)
        {
            if (targetId >= PlayerTargetBaseId)
            {
                int idx = targetId - PlayerTargetBaseId;
                return _participants[idx].Position;
            }

            return _units[targetId].Position;
        }

        private ArenaParticipant RequireParticipant(int participantId)
        {
            Contract.Requires(_participantById.TryGetValue(participantId, out ArenaParticipant p),
                "unknown participantId");
            return p;
        }

        private static AiMoveTargeting TargetingOf(MoveShape shape)
        {
            if (shape == MoveShape.Self)
            {
                return AiMoveTargeting.Self;
            }

            return shape == MoveShape.Ally ? AiMoveTargeting.Ally : AiMoveTargeting.Hostile;
        }

        private static AiMoveCategory CategoryOf(MoveDef def)
        {
            if (def.Effect != null && def.Effect.Type == EffectType.HealPercentMaxHp)
            {
                return AiMoveCategory.Heal;
            }

            return def.Shape == MoveShape.Self || def.Shape == MoveShape.Ally
                ? AiMoveCategory.Buff
                : AiMoveCategory.Standard;
        }

        private static EffectBook BookFor(TraitDef trait)
        {
            if (trait == null)
            {
                return new EffectBook();
            }

            var effects = new EffectSpec[trait.Effects.Count];
            for (int i = 0; i < effects.Length; i++)
            {
                effects[i] = EffectSpec.FromDef(trait.Effects[i]);
            }

            return new EffectBook(trait.Id, TriggerSpec.FromDef(trait.Trigger), effects);
        }
    }
}
