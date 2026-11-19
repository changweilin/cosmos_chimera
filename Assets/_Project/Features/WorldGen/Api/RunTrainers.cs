using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>Trainer tactical posture (analogous to creature <see cref="Habit"/>): sets active challenge radius.</summary>
    /// <remarks>Pre: none. Post: 1:1 with creature Habit reaction distances (0/4/8/14/20/28 m). Side effects: none.</remarks>
    public enum TrainerFunction
    {
        Observant = 0,
        Escort = 1,
        Sparring = 2,
        Warden = 3,
        Pursuit = 4,
        Champion = 5,
    }

    /// <summary>Trainer outdoor/road activity duty (analogous to creature <see cref="NicheDef"/>).</summary>
    /// <remarks>Pre: none. Post: stable enum values for the 14 trainer duties. Side effects: none.</remarks>
    public enum TrainerDutyId
    {
        RoadCourier = 0,
        CircuitPatrol = 1,
        WatersideAngling = 2,
        ProspectMining = 3,
        BotanicalForaging = 4,
        RuinExcavation = 5,
        MartialTraining = 6,
        ShrinePilgrimage = 7,
        RovingMinstrel = 8,
        NightWatch = 9,
        BountyHunting = 10,
        FrontierTrailblazing = 11,
        LeylineTuning = 12,
        TrialWardenship = 13,
    }

    /// <summary>Classification axis where a trainer profession originates.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum TrainerOriginKind
    {
        InGame = 0,
        PlanetEra = 1,
        CultureTimeline = 2,
    }

    /// <summary>Weighted trainer function candidate belonging to a <see cref="TrainerDutySpec"/>.</summary>
    /// <remarks>Pre: Weight &gt; 0. Post: immutable. Side effects: none.</remarks>
    public readonly struct TrainerFunctionWeight
    {
        /// <summary>Creates a weighted function entry.</summary>
        /// <remarks>Pre: function defined; weight &gt; 0. Post: fields equal arguments. Side effects: none.</remarks>
        public TrainerFunctionWeight(TrainerFunction function, int weight)
        {
            Contract.Requires(Enum.IsDefined(typeof(TrainerFunction), function), "function must be defined");
            Contract.Requires(weight > 0, "weight must be > 0");
            Function = function;
            Weight = weight;
        }

        /// <summary>Function option.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public TrainerFunction Function { get; }

        /// <summary>Selection weight.</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public int Weight { get; }
    }

    /// <summary>
    /// Rule-layer specification of one trainer outdoor duty (wander mode, radii, two-layer wander distributions, allowed
    /// habitats and function weights). REQ: docs/design/trainer-battles.md#職務與職能.
    /// </summary>
    /// <remarks>Pre: built by TrainerPlanner. Post: immutable. Side effects: none.</remarks>
    public readonly struct TrainerDutySpec
    {
        internal TrainerDutySpec(TrainerDutyId duty, string id, string nameZh, WanderMode mode, int radiusMm,
            int exclusionRadiusMm, WanderDistribution speedPermille, WanderDistribution moveMs,
            WanderDistribution pauseMs, IReadOnlyList<string> habitatKinds, IReadOnlyList<TrainerFunctionWeight> functions)
        {
            Duty = duty;
            Id = id;
            NameZh = nameZh;
            Mode = mode;
            RadiusMm = radiusMm;
            ExclusionRadiusMm = exclusionRadiusMm;
            SpeedPermille = speedPermille;
            MoveMs = moveMs;
            PauseMs = pauseMs;
            HabitatKinds = habitatKinds;
            Functions = functions;
        }

        /// <summary>Duty identifier.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public TrainerDutyId Duty { get; }

        /// <summary>Stable string key matching catalog.js ("duty_*").</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Display name in Traditional Chinese.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string NameZh { get; }

        /// <summary>Wander pattern.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public WanderMode Mode { get; }

        /// <summary>Home cap radius in millimetres (0 for UnboundedRandom).</summary>
        /// <remarks>Pre: none. Post: &gt; 0 unless UnboundedRandom (0). Side effects: none.</remarks>
        public int RadiusMm { get; }

        /// <summary>Competitive exclusion radius in millimetres.</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public int ExclusionRadiusMm { get; }

        /// <summary>Radius reserved for non-overlap and unbounded step size (mm).</summary>
        /// <remarks>Pre: none. Post: RadiusMm for bounded duties; ExclusionRadiusMm for unbounded. Side effects: none.</remarks>
        public int ActivityRadiusMm => RadiusMm > 0 ? RadiusMm : ExclusionRadiusMm;

        /// <summary>Walk speed distribution (permille of 3.2 m/s base).</summary>
        /// <remarks>Pre: none. Post: valid distribution. Side effects: none.</remarks>
        public WanderDistribution SpeedPermille { get; }

        /// <summary>Move budget per leg (ms).</summary>
        /// <remarks>Pre: none. Post: valid distribution. Side effects: none.</remarks>
        public WanderDistribution MoveMs { get; }

        /// <summary>Pause duration between legs (ms).</summary>
        /// <remarks>Pre: none. Post: valid distribution. Side effects: none.</remarks>
        public WanderDistribution PauseMs { get; }

        /// <summary>Landform kind ids where this duty occurs.</summary>
        /// <remarks>Pre: none. Post: non-empty list. Side effects: none.</remarks>
        public IReadOnlyList<string> HabitatKinds { get; }

        /// <summary>Weighted tactical functions drawn when taking this duty.</summary>
        /// <remarks>Pre: none. Post: non-empty list. Side effects: none.</remarks>
        public IReadOnlyList<TrainerFunctionWeight> Functions { get; }

        /// <summary>True when <paramref name="kind"/> is in <see cref="HabitatKinds"/>.</summary>
        /// <remarks>Pre: kind non-null. Post: ordinal match. Side effects: none.</remarks>
        public bool AllowsHabitat(string kind)
        {
            Contract.Requires(kind != null, "kind must not be null");
            for (int i = 0; i < HabitatKinds.Count; i++)
            {
                if (string.Equals(HabitatKinds[i], kind, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Candidate duty weighted by profession fit.</summary>
    /// <remarks>Pre: duty defined; weight &gt; 0. Post: immutable. Side effects: none.</remarks>
    public readonly struct TrainerProfessionDuty
    {
        /// <summary>Creates a weighted duty option.</summary>
        /// <remarks>Pre: duty defined; weight &gt; 0. Post: fields equal arguments. Side effects: none.</remarks>
        public TrainerProfessionDuty(TrainerDutyId duty, int weight)
        {
            Contract.Requires(Enum.IsDefined(typeof(TrainerDutyId), duty), "duty must be defined");
            Contract.Requires(weight > 0, "weight must be > 0");
            Duty = duty;
            Weight = weight;
        }

        /// <summary>Duty option.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public TrainerDutyId Duty { get; }

        /// <summary>Selection weight.</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public int Weight { get; }
    }

    /// <summary>Profession definition spanning in-game, planet-era and cultural-timeline roles.</summary>
    /// <remarks>Pre: built by TrainerPlanner. Post: immutable. Side effects: none.</remarks>
    public readonly struct TrainerProfessionSpec
    {
        internal TrainerProfessionSpec(string id, string nameZh, TrainerOriginKind origin, string cultureRegion,
            string timelineTag, IReadOnlyList<Element> elementBias, int strengthPermille, TrainerFunction defaultFunction,
            NpcRole propRole, IReadOnlyList<TrainerProfessionDuty> candidateDuties, int weight)
        {
            Id = id;
            NameZh = nameZh;
            Origin = origin;
            CultureRegion = cultureRegion;
            TimelineTag = timelineTag;
            ElementBias = elementBias;
            StrengthPermille = strengthPermille;
            DefaultFunction = defaultFunction;
            PropRole = propRole;
            CandidateDuties = candidateDuties;
            Weight = weight;
        }

        /// <summary>Profession id (trv_*, era_* or cul_*).</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Display name.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string NameZh { get; }

        /// <summary>Classification origin.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public TrainerOriginKind Origin { get; }

        /// <summary>Cultural region tag (null for non-cultural professions).</summary>
        /// <remarks>Pre: none. Post: optional string. Side effects: none.</remarks>
        public string CultureRegion { get; }

        /// <summary>Era / timeline tag (null when unrestricted).</summary>
        /// <remarks>Pre: none. Post: optional string. Side effects: none.</remarks>
        public string TimelineTag { get; }

        /// <summary>Preferred team elements (empty = unrestricted).</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public IReadOnlyList<Element> ElementBias { get; }

        /// <summary>Interpolation factor t in permille between wild max level and boss level.</summary>
        /// <remarks>Pre: none. Post: in [100, 1000]. Side effects: none.</remarks>
        public int StrengthPermille { get; }

        /// <summary>Characteristic tactical posture.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public TrainerFunction DefaultFunction { get; }

        /// <summary>Nearest <see cref="NpcRole"/> borrowed for placeholder prop silhouettes.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public NpcRole PropRole { get; }

        /// <summary>Candidate duties with positive weights.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public IReadOnlyList<TrainerProfessionDuty> CandidateDuties { get; }

        /// <summary>Base planet draw weight.</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public int Weight { get; }
    }

    /// <summary>One creature slot in a wandering trainer's team.</summary>
    /// <remarks>Pre: creatureId non-empty; level &gt;= 1. Post: immutable. Side effects: none.</remarks>
    public readonly struct TrainerTeamMember
    {
        /// <summary>Creates a trainer party member descriptor.</summary>
        /// <remarks>Pre: creatureId non-empty; level &gt;= 1. Post: fields equal arguments. Side effects: none.</remarks>
        public TrainerTeamMember(string creatureId, int level)
        {
            Contract.Requires(!string.IsNullOrEmpty(creatureId), "creatureId must be non-empty");
            Contract.Requires(level >= 1, "level must be >= 1");
            CreatureId = creatureId;
            Level = level;
        }

        /// <summary>Creature form id.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string CreatureId { get; }

        /// <summary>Individual level.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int Level { get; }
    }

    /// <summary>One wandering battle trainer generated for a planet run.</summary>
    /// <remarks>Pre: built by TrainerPlanner. Post: immutable; HomeDirection sits outside inhabited settlements. Side effects: none.</remarks>
    public sealed class RunTrainer
    {
        internal RunTrainer(int index, int region, string professionId, TrainerOriginKind origin, TrainerDutyId duty,
            TrainerFunction function, WanderMode wanderMode, int wanderRadiusMm, int exclusionRadiusMm,
            int speedPermilleMean, int moveMsMean, int pauseMsMean, float3 homeDirection, float3 homeFacing,
            ulong seed, NpcRole propRole, TrainerTeamMember[] team)
        {
            Index = index;
            Region = region;
            ProfessionId = professionId;
            Origin = origin;
            Duty = duty;
            Function = function;
            WanderMode = wanderMode;
            WanderRadiusMm = wanderRadiusMm;
            ExclusionRadiusMm = exclusionRadiusMm;
            SpeedPermilleMean = speedPermilleMean;
            MoveMsMean = moveMsMean;
            PauseMsMean = pauseMsMean;
            HomeDirection = homeDirection;
            HomeFacing = homeFacing;
            Seed = seed;
            PropRole = propRole;
            Team = Array.AsReadOnly(team);
        }

        /// <summary>Index in <see cref="RunTrainers.Trainers"/>.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int Index { get; }

        /// <summary>Region index owning the home point.</summary>
        /// <remarks>Pre: none. Post: valid region index. Side effects: none.</remarks>
        public int Region { get; }

        /// <summary>Profession id (trv_*, era_*, cul_*).</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string ProfessionId { get; }

        /// <summary>Origin axis of <see cref="ProfessionId"/>.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public TrainerOriginKind Origin { get; }

        /// <summary>Assigned outdoor duty (analogous to creature Niche).</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public TrainerDutyId Duty { get; }

        /// <summary>Assigned tactical function (analogous to creature Habit).</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public TrainerFunction Function { get; }

        /// <summary>Wander pattern from <see cref="Duty"/>.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public WanderMode WanderMode { get; }

        /// <summary>Individual activity cap radius (mm; 0 for UnboundedRandom).</summary>
        /// <remarks>Pre: none. Post: &gt; 0 unless UnboundedRandom (0). Side effects: none.</remarks>
        public int WanderRadiusMm { get; }

        /// <summary>Competitive exclusion radius (mm).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public int ExclusionRadiusMm { get; }

        /// <summary>Effective non-overlap / unbounded step radius (mm).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public int ActivityRadiusMm => WanderRadiusMm > 0 ? WanderRadiusMm : ExclusionRadiusMm;

        /// <summary>Individual walk speed mean (permille of base speed).</summary>
        /// <remarks>Pre: none. Post: within duty distribution bounds. Side effects: none.</remarks>
        public int SpeedPermilleMean { get; }

        /// <summary>Individual move leg duration mean (ms).</summary>
        /// <remarks>Pre: none. Post: within duty distribution bounds. Side effects: none.</remarks>
        public int MoveMsMean { get; }

        /// <summary>Individual pause duration mean (ms).</summary>
        /// <remarks>Pre: none. Post: within duty distribution bounds. Side effects: none.</remarks>
        public int PauseMsMean { get; }

        /// <summary>Active challenge distance in mm from <see cref="Function"/>.</summary>
        /// <remarks>Pre: none. Post: 0 / 4000 / 8000 / 14000 / 20000 / 28000. Side effects: none.</remarks>
        public int ChallengeRadiusMm => TrainerPlanner.FunctionRadiusMm(Function);

        /// <summary>Initial spawn direction (unit vector, outside inhabited settlements).</summary>
        /// <remarks>Pre: none. Post: unit length. Side effects: none.</remarks>
        public float3 HomeDirection { get; }

        /// <summary>Initial facing tangent.</summary>
        /// <remarks>Pre: none. Post: unit tangent perpendicular to HomeDirection. Side effects: none.</remarks>
        public float3 HomeFacing { get; }

        /// <summary>Individual deterministic seed.</summary>
        /// <remarks>Pre: none. Post: deterministic per (plan seed, trainer index). Side effects: none.</remarks>
        public ulong Seed { get; }

        /// <summary>Borrowed prop role for procedural placeholder visuals.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public NpcRole PropRole { get; }

        /// <summary>Trainer party creatures.</summary>
        /// <remarks>Pre: none. Post: 1..4 entries. Side effects: none.</remarks>
        public IReadOnlyList<TrainerTeamMember> Team { get; }
    }

    /// <summary>Outcome of stepping wandering trainer sight detection and pursuit.</summary>
    /// <remarks>Pre: none. Post: enum value. Side effects: none.</remarks>
    public enum TrainerPursuitResult
    {
        None = 0,
        Spotted = 1,
        Chasing = 2,
        Engaged = 3,
        Evaded = 4,
    }

    /// <summary>
    /// All wandering battle trainers on a planet, including live out-of-settlement movement stepping and active challenge
    /// queries. REQ: docs/design/trainer-battles.md#職務與職能.
    /// </summary>
    /// <remarks>
    /// Pre: built by TrainerPlanner.
    /// Post: immutable configuration with deterministic Step(nowMs, dtMs) state that keeps every trainer strictly outside
    ///       inhabited settlements and building footprints.
    /// Side effects: Step advances live positions and defeated flags.
    /// </remarks>
    public sealed class RunTrainers
    {
        /// <summary>Arc distance (m) beyond which a pursued player evades a wandering trainer.</summary>
        /// <remarks>Pre: none. Post: constant 25 m. Side effects: none.</remarks>
        public const float EvadeRadiusM = 25f;

        /// <summary>Arc distance (m) within which a pursuing trainer catches and engages the player.</summary>
        /// <remarks>Pre: none. Post: constant 3 m. Side effects: none.</remarks>
        public const float EngageReachM = 3f;

        /// <summary>Arc distance (m) within which the player can talk to / challenge a trainer via interact input.</summary>
        /// <remarks>Pre: none. Post: constant 3 m. Side effects: none.</remarks>
        public const float InteractReachM = 3f;

        /// <summary>Cosine of half-angle of trainer forward sight cone (60 degrees half-angle, 120 degrees FOV).</summary>
        /// <remarks>Pre: none. Post: constant 0.5f. Side effects: none.</remarks>
        public const float SightHalfAngleCos = 0.5f;

        private const float BaseWalkSpeedMs = 3.2f;
        private const int PursuitSpeedPermille = 1400;
        private const float ArriveM = 0.25f;
        private const int WaypointRetries = 24;
        private const long Unscheduled = long.MinValue;

        private static readonly float2[] SlideTurns =
        {
            new float2(0.70710678f, 0.70710678f), new float2(0f, 1f), new float2(-0.70710678f, 0.70710678f),
        };

        private struct TrainerWander
        {
            public float3[] Route;
            public int RouteIndex;
            public int RouteStep;
            public float3 Target;
            public bool HasTarget;
            public long ResumeMs;
            public long LegRemainingMs;
            public int LegSpeedPermille;
            public int SlideSide;
            public Pcg32 Rng;
        }

        private enum MoveResult
        {
            Moving,
            Arrived,
            Blocked,
        }

        private readonly RunTrainer[] _trainers;
        private readonly float _radius;
        private readonly LayoutClearance _clearance;
        private readonly Func<float3, bool> _isLand;
        private readonly float3[] _currentDirections;
        private readonly float3[] _currentFacings;
        private readonly bool[] _defeated;
        private readonly TrainerWander[] _wander;
        private int _pursuingTrainer = -1;
        private long _lastStepMs = long.MinValue;

        internal RunTrainers(RunTrainer[] trainers, float radius, LayoutClearance clearance, Func<float3, bool> isLand)
        {
            _trainers = trainers;
            Trainers = Array.AsReadOnly(trainers);
            _radius = radius;
            _clearance = clearance;
            _isLand = isLand;
            _currentDirections = new float3[trainers.Length];
            _currentFacings = new float3[trainers.Length];
            _defeated = new bool[trainers.Length];
            _wander = new TrainerWander[trainers.Length];
            for (int i = 0; i < trainers.Length; i++)
            {
                _currentDirections[i] = trainers[i].HomeDirection;
                _currentFacings[i] = trainers[i].HomeFacing;
                _wander[i] = new TrainerWander
                {
                    RouteStep = 1,
                    ResumeMs = Unscheduled,
                    SlideSide = 1,
                    Rng = new Pcg32(trainers[i].Seed, 'W'),
                };
            }
        }

        /// <summary>Generated trainers in deterministic order.</summary>
        /// <remarks>Pre: none. Post: read-only, non-null. Side effects: none.</remarks>
        public IReadOnlyList<RunTrainer> Trainers { get; }

        /// <summary>Monotonic step counter incremented by every <see cref="Step"/> call or pursuit move.</summary>
        /// <remarks>Pre: none. Post: 0 before first Step. Side effects: none.</remarks>
        public int StepVersion { get; private set; }

        /// <summary>Index of the trainer currently pursuing the player, or -1 when none is chasing.</summary>
        /// <remarks>Pre: none. Post: -1 or valid undefeated trainer index. Side effects: none.</remarks>
        public int PursuingTrainer => _pursuingTrainer;

        /// <summary>Live unit direction of trainer <paramref name="index"/> (initially <see cref="RunTrainer.HomeDirection"/>).</summary>
        /// <remarks>Pre: 0 &lt;= index &lt; Trainers.Count. Post: unit vector outside inhabited settlements. Side effects: none.</remarks>
        public float3 CurrentDirection(int index)
        {
            Contract.Requires(index >= 0 && index < _trainers.Length, "trainer index out of range");
            return _currentDirections[index];
        }

        /// <summary>Live unit facing tangent of trainer <paramref name="index"/>.</summary>
        /// <remarks>Pre: 0 &lt;= index &lt; Trainers.Count. Post: unit tangent perpendicular to CurrentDirection. Side effects: none.</remarks>
        public float3 CurrentFacing(int index)
        {
            Contract.Requires(index >= 0 && index < _trainers.Length, "trainer index out of range");
            return _currentFacings[index];
        }

        /// <summary>True once trainer <paramref name="index"/> has been defeated in this run.</summary>
        /// <remarks>Pre: 0 &lt;= index &lt; Trainers.Count. Post: boolean state. Side effects: none.</remarks>
        public bool IsDefeated(int index)
        {
            Contract.Requires(index >= 0 && index < _trainers.Length, "trainer index out of range");
            return _defeated[index];
        }

        /// <summary>Marks trainer <paramref name="index"/> defeated so it no longer initiates battles in this run.</summary>
        /// <remarks>Pre: 0 &lt;= index &lt; Trainers.Count. Post: IsDefeated(index) is true; clears active pursuit if it matches. Side effects: updates defeated flag.</remarks>
        public void MarkDefeated(int index)
        {
            Contract.Requires(index >= 0 && index < _trainers.Length, "trainer index out of range");
            _defeated[index] = true;
            if (_pursuingTrainer == index)
            {
                CancelPursuit();
            }
        }

        /// <summary>Cancels any active trainer pursuit so the trainer resumes normal outdoor wandering.</summary>
        /// <remarks>Pre: none. Post: PursuingTrainer == -1. Side effects: resets active pursuit and pending wander leg.</remarks>
        public void CancelPursuit()
        {
            if (_pursuingTrainer >= 0 && _pursuingTrainer < _wander.Length)
            {
                _wander[_pursuingTrainer].HasTarget = false;
                _wander[_pursuingTrainer].ResumeMs = Unscheduled;
            }

            _pursuingTrainer = -1;
        }

        /// <summary>
        /// True when undefeated trainer <paramref name="index"/> has an active challenge radius (&gt; 0) and sees
        /// <paramref name="playerDirection"/> within its sight range and forward cone (<see cref="SightHalfAngleCos"/>).
        /// </summary>
        /// <remarks>
        /// Pre: 0 &lt;= index &lt; Trainers.Count; playerDirection finite and non-zero; planetRadius &gt; 0.
        /// Post: false for defeated or Observant (0 m) trainers; true iff within range and forward cone.
        /// Side effects: none.
        /// </remarks>
        public bool IsInSight(int index, float3 playerDirection, float planetRadius)
        {
            Contract.Requires(index >= 0 && index < _trainers.Length, "trainer index out of range");
            Contract.Requires(math.all(math.isfinite(playerDirection)) && math.lengthsq(playerDirection) > 0f,
                "playerDirection must be finite and non-zero");
            Contract.Requires(math.isfinite(planetRadius) && planetRadius > 0f, "planetRadius must be > 0");
            if (_defeated[index])
            {
                return false;
            }

            int rangeMm = _trainers[index].ChallengeRadiusMm;
            if (rangeMm <= 0)
            {
                return false;
            }

            float3 p = math.normalize(playerDirection);
            float3 pos = _currentDirections[index];
            float dM = ArcM(pos, p, planetRadius);
            if (dM > rangeMm * 0.001f)
            {
                return false;
            }

            if (dM <= 0.1f)
            {
                return true;
            }

            float3 delta = p - pos;
            float3 tangent = delta - math.dot(delta, pos) * pos;
            float lenSq = math.lengthsq(tangent);
            if (lenSq <= 1e-10f)
            {
                return true;
            }

            float3 toPlayer = tangent / math.sqrt(lenSq);
            return math.dot(_currentFacings[index], toPlayer) >= SightHalfAngleCos;
        }

        /// <summary>
        /// Returns the closest undefeated trainer within <see cref="InteractReachM"/> (typically used to challenge
        /// <see cref="TrainerFunction.Observant"/> trainers via interact input), or -1 when none is in reach.
        /// </summary>
        /// <remarks>
        /// Pre: playerDirection finite and non-zero; planetRadius &gt; 0.
        /// Post: -1 or index of an undefeated trainer within InteractReachM.
        /// Side effects: none.
        /// </remarks>
        public int NearestInteractable(float3 playerDirection, float planetRadius)
        {
            Contract.Requires(math.all(math.isfinite(playerDirection)) && math.lengthsq(playerDirection) > 0f,
                "playerDirection must be finite and non-zero");
            Contract.Requires(math.isfinite(planetRadius) && planetRadius > 0f, "planetRadius must be > 0");
            float3 p = math.normalize(playerDirection);
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _trainers.Length; i++)
            {
                if (_defeated[i])
                {
                    continue;
                }

                float dM = ArcM(_currentDirections[i], p, planetRadius);
                if (dM <= InteractReachM && dM < bestDist)
                {
                    bestDist = dM;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// Advances sight detection and active pursuit of the player over <paramref name="dtMs"/>.
        /// Spots undefeated non-Observant trainers via <see cref="IsInSight"/>, pursues until within
        /// <see cref="EngageReachM"/> (<see cref="TrainerPursuitResult.Engaged"/>), or drops pursuit once the player
        /// escapes beyond <see cref="EvadeRadiusM"/> (<see cref="TrainerPursuitResult.Evaded"/>).
        /// </summary>
        /// <remarks>
        /// REQ: docs/design/trainer-battles.md#生成與遭遇.
        /// Pre: playerDirection finite and non-zero; planetRadius &gt; 0; 0 &lt; dtMs &lt;= 1000.
        /// Post: trainerIndex is -1 on None, otherwise the trainer involved in Spotted/Chasing/Engaged/Evaded;
        ///       pursued trainer stays outside inhabited settlements and building footprints.
        /// Side effects: updates PursuingTrainer and the chasing trainer's direction, facing and StepVersion.
        /// </remarks>
        public TrainerPursuitResult StepPursuit(float3 playerDirection, float planetRadius, int dtMs, out int trainerIndex)
        {
            Contract.Requires(math.all(math.isfinite(playerDirection)) && math.lengthsq(playerDirection) > 0f,
                "playerDirection must be finite and non-zero");
            Contract.Requires(math.isfinite(planetRadius) && planetRadius > 0f, "planetRadius must be > 0");
            Contract.Requires(dtMs > 0 && dtMs <= 1000, "dtMs must be 1..1000");

            float3 p = math.normalize(playerDirection);
            if (_pursuingTrainer >= 0)
            {
                int chasing = _pursuingTrainer;
                if (_defeated[chasing])
                {
                    CancelPursuit();
                    trainerIndex = -1;
                    return TrainerPursuitResult.None;
                }

                float dM = ArcM(_currentDirections[chasing], p, planetRadius);
                float sightM = _trainers[chasing].ChallengeRadiusMm * 0.001f;
                float evadeM = sightM >= EvadeRadiusM ? sightM + 1f : EvadeRadiusM;
                if (dM >= evadeM)
                {
                    CancelPursuit();
                    trainerIndex = chasing;
                    return TrainerPursuitResult.Evaded;
                }

                if (dM <= EngageReachM)
                {
                    CancelPursuit();
                    trainerIndex = chasing;
                    return TrainerPursuitResult.Engaged;
                }

                int speedPermille = math.max(_trainers[chasing].SpeedPermilleMean, PursuitSpeedPermille);
                MoveResult move = Steer(chasing, p, speedPermille, dtMs);
                StepVersion++;
                float afterM = ArcM(_currentDirections[chasing], p, planetRadius);
                if (move == MoveResult.Arrived || afterM <= EngageReachM)
                {
                    CancelPursuit();
                    trainerIndex = chasing;
                    return TrainerPursuitResult.Engaged;
                }

                trainerIndex = chasing;
                return TrainerPursuitResult.Chasing;
            }

            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _trainers.Length; i++)
            {
                if (!IsInSight(i, p, planetRadius))
                {
                    continue;
                }

                float dM = ArcM(_currentDirections[i], p, planetRadius);
                if (dM < bestDist)
                {
                    bestDist = dM;
                    best = i;
                }
            }

            if (best < 0)
            {
                trainerIndex = -1;
                return TrainerPursuitResult.None;
            }

            _pursuingTrainer = best;
            _currentFacings[best] = Tangent(_currentDirections[best], p - _currentDirections[best], _currentFacings[best]);
            StepVersion++;
            trainerIndex = best;
            return TrainerPursuitResult.Spotted;
        }

        /// <summary>
        /// Returns the closest undefeated trainer whose active challenge range (<see cref="RunTrainer.ChallengeRadiusMm"/> &gt; 0)
        /// reaches <paramref name="playerDirection"/>, or -1 when none triggers.
        /// </summary>
        /// <remarks>
        /// REQ: docs/design/trainer-battles.md#職務與職能 (observant 0m never initiates; other functions trigger within radius).
        /// Pre: playerDirection finite and non-zero; planetRadius &gt; 0.
        /// Post: -1 or index of an undefeated trainer with ChallengeRadiusMm &gt; 0 whose arc distance &lt;= ChallengeRadiusMm * 0.001f.
        /// Side effects: none.
        /// </remarks>
        public int FindChallenger(float3 playerDirection, float planetRadius)
        {
            Contract.Requires(math.all(math.isfinite(playerDirection)) && math.lengthsq(playerDirection) > 0f,
                "playerDirection must be finite and non-zero");
            Contract.Requires(math.isfinite(planetRadius) && planetRadius > 0f, "planetRadius must be > 0");
            float3 p = math.normalize(playerDirection);
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _trainers.Length; i++)
            {
                if (_defeated[i])
                {
                    continue;
                }

                int rangeMm = _trainers[i].ChallengeRadiusMm;
                if (rangeMm <= 0)
                {
                    continue;
                }

                float dM = ArcM(_currentDirections[i], p, planetRadius);
                if (dM <= rangeMm * 0.001f && dM < bestDist)
                {
                    bestDist = dM;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// Advances every wandering trainer by <paramref name="dtMs"/> along its duty trajectory while keeping every
        /// trainer outside inhabited settlements and building footprints.
        /// </summary>
        /// <remarks>
        /// Pre: nowMs &gt;= 0 and strictly greater than previous Step's nowMs; 0 &lt; dtMs &lt;= 1000.
        /// Post: CurrentDirection(i) stays on land, outside building footprints and outside inhabited settlements; StepVersion++ .
        /// Side effects: updates live directions, facings and per-trainer RNG streams.
        /// </remarks>
        public void Step(long nowMs, int dtMs)
        {
            Contract.Requires(nowMs >= 0 && nowMs > _lastStepMs, "nowMs must be >= 0 and strictly increasing");
            Contract.Requires(dtMs > 0 && dtMs <= 1000, "dtMs must be 1..1000");
            _lastStepMs = nowMs;

            for (int i = 0; i < _trainers.Length; i++)
            {
                if (i == _pursuingTrainer)
                {
                    continue;
                }

                StepTrainer(i, nowMs, dtMs);
            }

            StepVersion++;
        }

        private void StepTrainer(int index, long nowMs, int dtMs)
        {
            RunTrainer t = _trainers[index];
            TrainerDutySpec spec = TrainerPlanner.DutySpecFor(t.Duty);
            float capM = t.ActivityRadiusMm * 0.001f;
            bool bounded = spec.Mode != WanderMode.UnboundedRandom;
            ref TrainerWander w = ref _wander[index];

            if ((spec.Mode == WanderMode.Shuttle || spec.Mode == WanderMode.Loop) && w.Route == null)
            {
                BuildRoute(index, t, capM);
            }

            if (!w.HasTarget)
            {
                if (w.ResumeMs == Unscheduled)
                {
                    w.ResumeMs = nowMs + DrawPause(ref w.Rng, t, in spec);
                    return;
                }

                if (nowMs < w.ResumeMs)
                {
                    return;
                }

                if (!TryPickTarget(index, t, in spec, capM))
                {
                    w.ResumeMs = nowMs + DrawPause(ref w.Rng, t, in spec);
                    return;
                }

                w.LegSpeedPermille = BoundedNormal.Sample(ref w.Rng, t.SpeedPermilleMean, spec.SpeedPermille.ActionSd,
                    spec.SpeedPermille.Min, spec.SpeedPermille.Max);
                w.LegRemainingMs = BoundedNormal.Sample(ref w.Rng, t.MoveMsMean, spec.MoveMs.ActionSd, spec.MoveMs.Min,
                    spec.MoveMs.Max);
                w.HasTarget = true;
            }

            float3 beforeDir = _currentDirections[index];
            float3 beforeFacing = _currentFacings[index];
            MoveResult res = Steer(index, w.Target, w.LegSpeedPermille, dtMs);
            if (bounded && res != MoveResult.Blocked &&
                ArcM(t.HomeDirection, _currentDirections[index], _radius) > capM + ArriveM &&
                ArcM(t.HomeDirection, _currentDirections[index], _radius) > ArcM(t.HomeDirection, beforeDir, _radius))
            {
                _currentDirections[index] = beforeDir;
                _currentFacings[index] = beforeFacing;
                res = MoveResult.Blocked;
            }

            w.LegRemainingMs -= dtMs;
            if (res == MoveResult.Arrived)
            {
                if (spec.Mode == WanderMode.Shuttle || spec.Mode == WanderMode.Loop)
                {
                    AdvanceRoute(ref w, spec.Mode);
                }

                w.HasTarget = false;
                w.ResumeMs = nowMs + DrawPause(ref w.Rng, t, in spec);
            }
            else if (res == MoveResult.Blocked || w.LegRemainingMs <= 0)
            {
                w.HasTarget = false;
                w.ResumeMs = nowMs + DrawPause(ref w.Rng, t, in spec);
            }
        }

        private void BuildRoute(int index, RunTrainer t, float capM)
        {
            ref TrainerWander w = ref _wander[index];
            var points = new List<float3>(3) { t.HomeDirection };
            for (int k = 0; k < 2; k++)
            {
                if (TryPointOutsideSettlement(ref w.Rng, t.HomeDirection, capM, out float3 p))
                {
                    points.Add(p);
                }
            }

            w.Route = points.ToArray();
            w.RouteIndex = (int)w.Rng.NextUInt((uint)w.Route.Length);
            w.RouteStep = w.Rng.NextUInt(2) == 0 ? 1 : -1;
        }

        private static void AdvanceRoute(ref TrainerWander w, WanderMode mode)
        {
            int n = w.Route.Length;
            if (n < 2)
            {
                return;
            }

            if (mode == WanderMode.Loop)
            {
                w.RouteIndex = (w.RouteIndex + w.RouteStep + n) % n;
                return;
            }

            int next = w.RouteIndex + w.RouteStep;
            if (next < 0 || next >= n)
            {
                w.RouteStep = -w.RouteStep;
                next = w.RouteIndex + w.RouteStep;
            }

            w.RouteIndex = next;
        }

        private bool TryPickTarget(int index, RunTrainer t, in TrainerDutySpec spec, float capM)
        {
            ref TrainerWander w = ref _wander[index];
            if (spec.Mode == WanderMode.Shuttle || spec.Mode == WanderMode.Loop)
            {
                if (w.Route == null || w.Route.Length < 2)
                {
                    return false;
                }

                w.Target = w.Route[w.RouteIndex];
                return true;
            }

            float3 center = spec.Mode == WanderMode.UnboundedRandom ? _currentDirections[index] : t.HomeDirection;
            if (TryPointOutsideSettlement(ref w.Rng, center, capM, out float3 target))
            {
                w.Target = target;
                return true;
            }

            return false;
        }

        private bool TryPointOutsideSettlement(ref Pcg32 rng, float3 center, float radiusM, out float3 target)
        {
            for (int i = 0; i < WaypointRetries; i++)
            {
                target = WanderPoint(ref rng, center, radiusM);
                if (IsOutdoorValid(target))
                {
                    return true;
                }
            }

            target = default;
            return false;
        }

        private bool IsOutdoorValid(float3 dir)
        {
            if (_isLand != null && !_isLand(dir))
            {
                return false;
            }

            if (_clearance == null)
            {
                return true;
            }

            return !_clearance.IsInsideBuilding(dir, PopulacePlanner.BodyRadiusM) &&
                !_clearance.IsInsideSettlement(dir, PopulacePlanner.BodyRadiusM);
        }

        private MoveResult Steer(int index, float3 dest, int speedPermille, int dtMs)
        {
            float3 up = _currentDirections[index];
            float angle = math.acos(math.clamp(math.dot(up, dest), -1f, 1f));
            if (angle * _radius <= ArriveM)
            {
                return MoveResult.Arrived;
            }

            float speedMs = BaseWalkSpeedMs * (speedPermille / 1000f);
            float step = math.min(angle, speedMs * dtMs * 0.001f / _radius);
            float3 heading = Tangent(up, dest - up, _currentFacings[index]);
            float3 next = math.normalize(up * math.cos(step) + heading * math.sin(step));
            if (!CanStepTo(up, next))
            {
                if (!TrySlide(index, up, step, ref heading, ref next))
                {
                    return MoveResult.Blocked;
                }

                _currentDirections[index] = next;
                _currentFacings[index] = Tangent(next, heading, _currentFacings[index]);
                float left = math.acos(math.clamp(math.dot(next, dest), -1f, 1f));
                return left * _radius <= ArriveM ? MoveResult.Arrived : MoveResult.Moving;
            }

            _currentDirections[index] = next;
            _currentFacings[index] = Tangent(next, heading, _currentFacings[index]);
            return (angle - step) * _radius <= ArriveM ? MoveResult.Arrived : MoveResult.Moving;
        }

        private bool TrySlide(int index, float3 up, float step, ref float3 heading, ref float3 next)
        {
            float3 left = math.cross(up, heading);
            ref TrainerWander w = ref _wander[index];
            for (int k = 0; k < 2; k++)
            {
                int sideSign = k == 0 ? w.SlideSide : -w.SlideSide;
                foreach (float2 turn in SlideTurns)
                {
                    float3 h = heading * turn.x + left * (turn.y * sideSign);
                    float3 n = math.normalize(up * math.cos(step) + h * math.sin(step));
                    if (CanStepTo(up, n))
                    {
                        heading = h;
                        next = n;
                        w.SlideSide = sideSign;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool CanStepTo(float3 from, float3 to)
        {
            if (_isLand != null && !_isLand(to))
            {
                return false;
            }

            if (_clearance == null)
            {
                return true;
            }

            if (_clearance.IsInsideBuilding(to, PopulacePlanner.BodyRadiusM) &&
                !_clearance.IsInsideBuilding(from, PopulacePlanner.BodyRadiusM))
            {
                return false;
            }

            // REQ: docs/design/trainer-battles.md#職務與職能 — trainers never enter inhabited settlements (城內).
            if (_clearance.IsInsideSettlement(to, PopulacePlanner.BodyRadiusM) &&
                !_clearance.IsInsideSettlement(from, PopulacePlanner.BodyRadiusM))
            {
                return false;
            }

            return true;
        }

        private static int DrawPause(ref Pcg32 rng, RunTrainer t, in TrainerDutySpec spec) =>
            BoundedNormal.Sample(ref rng, t.PauseMsMean, spec.PauseMs.ActionSd, spec.PauseMs.Min, spec.PauseMs.Max);

        private float3 WanderPoint(ref Pcg32 rng, float3 center, float radiusM)
        {
            float theta = rng.NextFloat() * 2f * math.PI;
            float arc = radiusM * math.sqrt(rng.NextFloat()) / _radius;
            PlanetFrame f = PlanetFrame.FromUp(center);
            float3 heading = f.East * math.cos(theta) + f.North * math.sin(theta);
            return math.normalize(center * math.cos(arc) + heading * math.sin(arc));
        }

        private static float ArcM(float3 a, float3 b, float radius) =>
            math.atan2(math.length(math.cross(a, b)), math.dot(a, b)) * radius;

        private static float3 Tangent(float3 up, float3 v, float3 fallback)
        {
            float3 t = v - math.dot(v, up) * up;
            float len = math.length(t);
            if (len > 1e-6f)
            {
                return t / len;
            }

            t = fallback - math.dot(fallback, up) * up;
            len = math.length(t);
            return len > 1e-6f ? t / len : PlanetFrame.FromUp(up).North;
        }
    }
}
