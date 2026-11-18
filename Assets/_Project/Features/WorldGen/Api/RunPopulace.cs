using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.WorldGen
{
    /// <summary>People a settlement can be home to (種族).</summary>
    /// <remarks>
    /// REQ: docs/design/characters.md#路人-npc (種族).
    /// Pre: none. Post: stable values (they are hashed and seed the appearance streams); append only. Side effects: none.
    /// </remarks>
    public enum NpcRace
    {
        Human = 0,
        Elf = 1,
        Dwarf = 2,
        Goblin = 3,
        Orc = 4,
        /// <summary>鳥人: feathered, winged, beaked.</summary>
        Avian = 5,
        /// <summary>魚人: finned and scaled, at home on coasts and riverbanks.</summary>
        Merfolk = 6,
        Lizardfolk = 7,
        /// <summary>龍人: horned, scaled, with a heavy tail and vestigial wings.</summary>
        Dragonkin = 8,
        Zombie = 9,
        Skeleton = 10,
        Werewolf = 11,
        Vampire = 12,
        Angel = 13,
        Demon = 14,
    }

    /// <summary>What a resident does for a living (職業); how it is dressed for it depends on the era, which the presentation owns.</summary>
    /// <remarks>Pre: none. Post: stable values; append only. Side effects: none.</remarks>
    public enum NpcRole
    {
        /// <summary>Children, and adults without a trade.</summary>
        None = 0,
        Farmer = 1,
        /// <summary>Hunters and gatherers.</summary>
        Forager = 2,
        Fisher = 3,
        Miner = 4,
        Artisan = 5,
        Merchant = 6,
        Guard = 7,
        Scholar = 8,
        Cleric = 9,
        Healer = 10,
        Laborer = 11,
        Performer = 12,
        /// <summary>Chiefs, mayors, nobles.</summary>
        Leader = 13,
    }

    /// <summary>Settlement duty assigned to an NPC from its <see cref="NpcRole"/>; governs in-town wandering.</summary>
    /// <remarks>REQ: docs/design/characters.md#職業與聚落職務. Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum NpcDuty
    {
        IdlePlay = 0,
        FieldTending = 1,
        CatchSorting = 2,
        NetMending = 3,
        OreSorting = 4,
        WorkshopCraft = 5,
        MarketHawking = 6,
        TownPatrol = 7,
        ArchiveStudy = 8,
        ShrineBlessing = 9,
        HerbTriage = 10,
        CargoHauling = 11,
        StreetBusking = 12,
        CivicInspect = 13,
    }

    /// <summary>Bounded in-settlement wander parameters for one <see cref="NpcDuty"/>.</summary>
    /// <remarks>
    /// REQ: docs/design/characters.md#職業與聚落職務.
    /// Pre: built through constructor. Post: Mode is bounded (never UnboundedRandom); RadiusMm &gt; 0. Side effects: none.
    /// </remarks>
    public readonly struct NpcDutySpec
    {
        /// <summary>Creates an NPC duty specification.</summary>
        /// <remarks>
        /// Pre: duty defined; mode != UnboundedRandom; radiusMm &gt; 0.
        /// Post: properties return arguments. Side effects: none.
        /// </remarks>
        public NpcDutySpec(NpcDuty duty, WanderMode mode, int radiusMm, WanderDistribution speedPermille,
            WanderDistribution moveMs, WanderDistribution pauseMs)
        {
            Contract.Requires(Enum.IsDefined(typeof(NpcDuty), duty), "duty must be defined");
            Contract.Requires(mode != WanderMode.UnboundedRandom, "NPC duty must use a bounded wander mode");
            Contract.Requires(radiusMm > 0, "radiusMm must be > 0");
            Duty = duty;
            Mode = mode;
            RadiusMm = radiusMm;
            SpeedPermille = speedPermille;
            MoveMs = moveMs;
            PauseMs = pauseMs;
        }

        /// <summary>Duty enum.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public NpcDuty Duty { get; }

        /// <summary>Bounded wander pattern.</summary>
        /// <remarks>Pre: none. Post: Shuttle, Loop or BoundedRandom. Side effects: none.</remarks>
        public WanderMode Mode { get; }

        /// <summary>Activity radius around the home spot (mm).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public int RadiusMm { get; }

        /// <summary>Speed distribution in permille of base NPC walk speed.</summary>
        /// <remarks>Pre: none. Post: valid distribution. Side effects: none.</remarks>
        public WanderDistribution SpeedPermille { get; }

        /// <summary>Move budget per leg (ms).</summary>
        /// <remarks>Pre: none. Post: valid distribution. Side effects: none.</remarks>
        public WanderDistribution MoveMs { get; }

        /// <summary>Pause duration between legs (ms).</summary>
        /// <remarks>Pre: none. Post: valid distribution. Side effects: none.</remarks>
        public WanderDistribution PauseMs { get; }
    }

    /// <summary>Where a resident is in its people's lifespan.</summary>
    /// <remarks>Pre: none. Post: stable, ascending with age. Side effects: none.</remarks>
    public enum NpcLifeStage
    {
        Child = 0,
        Youth = 1,
        Adult = 2,
        MiddleAged = 3,
        Elder = 4,
    }

    /// <summary>Body frame a resident is drawn on.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum NpcSex
    {
        Male = 0,
        Female = 1,
    }

    /// <summary>
    /// How the planet's era shifts the age and health its settlements average. The era is a presentation axis
    /// (ArchitectureMeshes.PlanetStyleFor), so the caller translates it into these two numbers.
    /// </summary>
    /// <remarks>Pre: built by the constructor. Post: immutable. Side effects: none.</remarks>
    public readonly struct PopulaceTemper
    {
        /// <summary>Describes an era's pull on age and health.</summary>
        /// <remarks>Pre: both in [−200, 200] (permille). Post: properties return the arguments. Side effects: none.</remarks>
        public PopulaceTemper(int ageShiftPermille, int healthShiftPermille)
        {
            Contract.Requires(ageShiftPermille >= -200 && ageShiftPermille <= 200 && healthShiftPermille >= -200 &&
                              healthShiftPermille <= 200, "shifts must be in [-200, 200]");
            AgeShiftPermille = ageShiftPermille;
            HealthShiftPermille = healthShiftPermille;
        }

        /// <summary>Added to every settlement's mean age (permille of lifespan).</summary>
        /// <remarks>Pre: none. Post: in [−200, 200]. Side effects: none.</remarks>
        public int AgeShiftPermille { get; }

        /// <summary>Added to every settlement's mean health (permille).</summary>
        /// <remarks>Pre: none. Post: in [−200, 200]. Side effects: none.</remarks>
        public int HealthShiftPermille { get; }
    }

    /// <summary>One resident: who it is and where it stands.</summary>
    /// <remarks>
    /// Pre: produced by PopulacePlanner. Post: immutable.
    /// The *ZMilli fields are bounded standard-normal draws × 1000 (|z| ≤ 3464): the presentation scales them into
    /// height, girth and how far the outfit strays toward the settlement's secondary style, so most residents sit near
    /// their settlement's mean and a few stand out.
    /// Side effects: none.
    /// </remarks>
    public readonly struct RunResident
    {
        internal RunResident(int settlement, int building, NpcRace race, NpcRole role, NpcSex sex, int agePermille,
            int ageYears, NpcLifeStage stage, int healthPermille, int statureZMilli, int bulkZMilli, int styleZMilli, int textilePermille,
            ulong seed, float3 direction, float3 facing, NpcDuty duty, WanderMode wanderMode, int wanderRadiusMm,
            int speedPermilleMean, int moveMsMean, int pauseMsMean)
        {
            Settlement = settlement;
            Building = building;
            Race = race;
            Role = role;
            Sex = sex;
            AgePermille = agePermille;
            AgeYears = ageYears;
            LifeStage = stage;
            HealthPermille = healthPermille;
            StatureZMilli = statureZMilli;
            BulkZMilli = bulkZMilli;
            TextilePermille = textilePermille;
            StyleZMilli = styleZMilli;
            Seed = seed;
            Direction = direction;
            Facing = facing;
            Duty = duty;
            WanderMode = wanderMode;
            WanderRadiusMm = wanderRadiusMm;
            SpeedPermilleMean = speedPermilleMean;
            MoveMsMean = moveMsMean;
            PauseMsMean = pauseMsMean;
        }

        /// <summary>Index of the settlement in RunLayout.Settlements.</summary>
        /// <remarks>Pre: none. Post: valid index of an inhabited settlement. Side effects: none.</remarks>
        public int Settlement { get; }

        /// <summary>Index in RunLayout.Buildings of the building the resident stands in front of.</summary>
        /// <remarks>Pre: none. Post: valid index; that building belongs to Settlement. Side effects: none.</remarks>
        public int Building { get; }

        /// <summary>Race.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public NpcRace Race { get; }

        /// <summary>Trade.</summary>
        /// <remarks>Pre: none. Post: defined; None for every Child. Side effects: none.</remarks>
        public NpcRole Role { get; }

        /// <summary>Body frame.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public NpcSex Sex { get; }

        /// <summary>Age as a share of the race's lifespan.</summary>
        /// <remarks>Pre: none. Post: in [40, 980]. Side effects: none.</remarks>
        public int AgePermille { get; }

        /// <summary>Age in years (the undead count the years they lived).</summary>
        /// <remarks>Pre: none. Post: ≥ 1; AgePermille × PopulacePlanner.LifespanYears(Race) / 1000, rounded down, at least 1. Side effects: none.</remarks>
        public int AgeYears { get; }

        /// <summary>Life stage AgePermille falls in.</summary>
        /// <remarks>Pre: none. Post: == PopulacePlanner.StageFor(AgePermille). Side effects: none.</remarks>
        public NpcLifeStage LifeStage { get; }

        /// <summary>Health: 1000 = hale, low = stooped, bandaged, leaning on a cane. Appearance only (characters.md#路人-npc).</summary>
        /// <remarks>Pre: none. Post: in [50, 1000]. Side effects: none.</remarks>
        public int HealthPermille { get; }

        /// <summary>Height relative to the race's usual, standard deviations × 1000.</summary>
        /// <remarks>Pre: none. Post: in [−3464, 3464]. Side effects: none.</remarks>
        public int StatureZMilli { get; }

        /// <summary>Girth relative to the race's usual, standard deviations × 1000.</summary>
        /// <remarks>Pre: none. Post: in [−3464, 3464]. Side effects: none.</remarks>
        public int BulkZMilli { get; }

        /// <summary>How far the outfit strays from the settlement's mean style mix, standard deviations × 1000.</summary>
        /// <remarks>Pre: none. Post: in [−3464, 3464]. Side effects: none.</remarks>
        public int StyleZMilli { get; }

        /// <summary>Local textile tradition, from narrow workaday hems to broad contrasting borders.</summary>
        /// <remarks>Pre: none. Post: in [0, 1000]. Side effects: none.</remarks>
        public int TextilePermille { get; }

        /// <summary>Seed of everything else the presentation varies (hair, colours, pose, which garments are borrowed).</summary>
        /// <remarks>Pre: none. Post: as generated; differs between residents of one run with overwhelming probability. Side effects: none.</remarks>
        public ulong Seed { get; }

        /// <summary>Where the resident stands (unit direction).</summary>
        /// <remarks>Pre: none. Post: unit length; outside every building footprint. Side effects: none.</remarks>
        public float3 Direction { get; }

        /// <summary>Unit tangent the resident faces.</summary>
        /// <remarks>Pre: none. Post: unit length; |dot(Facing, Direction)| &lt; 1e-3. Side effects: none.</remarks>
        public float3 Facing { get; }

        /// <summary>Settlement duty derived from <see cref="Role"/>.</summary>
        /// <remarks>Pre: none. Post: defined; matches PopulacePlanner.DutyFor(Role). Side effects: none.</remarks>
        public NpcDuty Duty { get; }

        /// <summary>Bounded wander mode inside the settlement.</summary>
        /// <remarks>Pre: none. Post: Shuttle, Loop or BoundedRandom. Side effects: none.</remarks>
        public WanderMode WanderMode { get; }

        /// <summary>Activity radius around <see cref="Direction"/> (mm).</summary>
        /// <remarks>Pre: none. Post: &gt; 0. Side effects: none.</remarks>
        public int WanderRadiusMm { get; }

        /// <summary>Individual mean walk speed (permille of base walk speed).</summary>
        /// <remarks>Pre: none. Post: within the duty distribution [Min, Max]. Side effects: none.</remarks>
        public int SpeedPermilleMean { get; }

        /// <summary>Individual mean move budget per leg (ms).</summary>
        /// <remarks>Pre: none. Post: within the duty distribution [Min, Max]. Side effects: none.</remarks>
        public int MoveMsMean { get; }

        /// <summary>Individual mean pause duration between legs (ms).</summary>
        /// <remarks>Pre: none. Post: within the duty distribution [Min, Max]. Side effects: none.</remarks>
        public int PauseMsMean { get; }
    }

    /// <summary>The make-up of one settlement's population: residents are drawn around these means.</summary>
    /// <remarks>Pre: produced by PopulacePlanner. Post: immutable. Side effects: none.</remarks>
    public readonly struct SettlementPopulace
    {
        internal SettlementPopulace(int settlement, NpcRace primary, NpcRace secondary, int secondaryPermille,
            int minorityPermille, int meanAgePermille, int meanHealthPermille, int firstResident, int residentCount)
        {
            Settlement = settlement;
            PrimaryRace = primary;
            SecondaryRace = secondary;
            SecondarySharePermille = secondaryPermille;
            MinoritySharePermille = minorityPermille;
            MeanAgePermille = meanAgePermille;
            MeanHealthPermille = meanHealthPermille;
            FirstResident = firstResident;
            ResidentCount = residentCount;
        }

        /// <summary>Index in RunLayout.Settlements.</summary>
        /// <remarks>Pre: none. Post: valid index; never Ruins or Lair. Side effects: none.</remarks>
        public int Settlement { get; }

        /// <summary>Race most residents belong to.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public NpcRace PrimaryRace { get; }

        /// <summary>Second most common race.</summary>
        /// <remarks>Pre: none. Post: defined, != PrimaryRace. Side effects: none.</remarks>
        public NpcRace SecondaryRace { get; }

        /// <summary>Expected share of SecondaryRace.</summary>
        /// <remarks>Pre: none. Post: in [0, 480]. Side effects: none.</remarks>
        public int SecondarySharePermille { get; }

        /// <summary>Expected share of residents of any other race (travellers, settlers).</summary>
        /// <remarks>Pre: none. Post: in [0, 250]; SecondarySharePermille + MinoritySharePermille ≤ 730. Side effects: none.</remarks>
        public int MinoritySharePermille { get; }

        /// <summary>Mean age the residents were drawn around, as a share of lifespan.</summary>
        /// <remarks>Pre: none. Post: in [200, 650]. Side effects: none.</remarks>
        public int MeanAgePermille { get; }

        /// <summary>Mean health the residents were drawn around.</summary>
        /// <remarks>Pre: none. Post: in [300, 950]. Side effects: none.</remarks>
        public int MeanHealthPermille { get; }

        /// <summary>First index of this settlement's residents in RunPopulace.Residents.</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public int FirstResident { get; }

        /// <summary>Number of residents (a settlement with no building on land has none).</summary>
        /// <remarks>Pre: none. Post: ≥ 0. Side effects: none.</remarks>
        public int ResidentCount { get; }
    }

    /// <summary>Everyone who lives on the planet of one run.</summary>
    /// <remarks>
    /// Pre: produced by <see cref="PopulacePlanner.Generate"/>. Post: identity fields are immutable; same inputs → same
    ///      populace and same deterministic wander steps via <see cref="Step"/> inside each resident's settlement.
    /// Side effects: Step updates live resident directions/facings.
    /// </remarks>
    public sealed class RunPopulace
    {
        private const float BaseWalkSpeedMs = 3.2f;
        private const float ArriveM = 0.25f;
        private const int WaypointRetries = 8;
        private const long Unscheduled = long.MinValue;

        private static readonly float2[] SlideTurns =
        {
            new float2(0.70710678f, 0.70710678f), new float2(0f, 1f), new float2(-0.70710678f, 0.70710678f),
        };

        private struct ResidentWander
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

        private readonly float _radius;
        private readonly RunResident[] _residents;
        private readonly LayoutClearance _clearance;
        private readonly Func<float3, bool> _isStandable;
        private readonly float3[] _currentDirections;
        private readonly float3[] _currentFacings;
        private readonly ResidentWander[] _wander;
        private long _lastStepMs = long.MinValue;

        internal RunPopulace(NpcRace primary, NpcRace secondary, SettlementPopulace[] settlements, RunResident[] residents,
            float radius, LayoutClearance clearance, Func<float3, bool> isStandable)
        {
            PrimaryRace = primary;
            SecondaryRace = secondary;
            Settlements = settlements;
            Residents = residents;
            _residents = residents;
            _radius = radius;
            _clearance = clearance;
            _isStandable = isStandable;
            _currentDirections = new float3[residents.Length];
            _currentFacings = new float3[residents.Length];
            _wander = new ResidentWander[residents.Length];
            for (int i = 0; i < residents.Length; i++)
            {
                _currentDirections[i] = residents[i].Direction;
                _currentFacings[i] = residents[i].Facing;
                _wander[i] = new ResidentWander
                {
                    RouteStep = 1,
                    ResumeMs = Unscheduled,
                    SlideSide = 1,
                    Rng = new Pcg32(residents[i].Seed, 'W'),
                };
            }
        }

        /// <summary>Race most of the planet's settlements are built by.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public NpcRace PrimaryRace { get; }

        /// <summary>The planet's usual second race.</summary>
        /// <remarks>Pre: none. Post: defined, != PrimaryRace. Side effects: none.</remarks>
        public NpcRace SecondaryRace { get; }

        /// <summary>One entry per inhabited settlement (everything but Ruins and Lair), in RunLayout order.</summary>
        /// <remarks>Pre: none. Post: ascending Settlement. Side effects: none.</remarks>
        public IReadOnlyList<SettlementPopulace> Settlements { get; }

        /// <summary>All residents, grouped by settlement in Settlements order.</summary>
        /// <remarks>Pre: none. Post: Residents[FirstResident .. +ResidentCount) belong to that settlement. Side effects: none.</remarks>
        public IReadOnlyList<RunResident> Residents { get; }

        /// <summary>Monotonic count of <see cref="Step"/> calls completed; views check this to avoid redundant updates.</summary>
        /// <remarks>Pre: none. Post: &gt;= 0. Side effects: none.</remarks>
        public int StepVersion { get; private set; }

        /// <summary>Live unit direction of resident <paramref name="residentIndex"/> (equals <see cref="RunResident.Direction"/> before stepping).</summary>
        /// <remarks>Pre: 0 &lt;= residentIndex &lt; Residents.Count. Post: unit length. Side effects: none.</remarks>
        public float3 CurrentDirection(int residentIndex)
        {
            Contract.Requires(residentIndex >= 0 && residentIndex < _residents.Length, "residentIndex out of range");
            return _currentDirections[residentIndex];
        }

        /// <summary>Live unit tangent facing of resident <paramref name="residentIndex"/>.</summary>
        /// <remarks>Pre: 0 &lt;= residentIndex &lt; Residents.Count. Post: unit length, tangent to CurrentDirection. Side effects: none.</remarks>
        public float3 CurrentFacing(int residentIndex)
        {
            Contract.Requires(residentIndex >= 0 && residentIndex < _residents.Length, "residentIndex out of range");
            return _currentFacings[residentIndex];
        }

        /// <summary>
        /// Advances every resident along its <see cref="NpcDuty"/> within its settlement boundary.
        /// REQ: docs/design/characters.md#職業與聚落職務 — NPCs never leave their inhabited settlement.
        /// </summary>
        /// <remarks>
        /// Pre: nowMs &gt;= 0 and strictly greater than previous Step's nowMs; 0 &lt; dtMs &lt;= 1000.
        /// Post: CurrentDirection(i) stays on standable ground, outside building footprints, within WanderRadiusMm of
        ///       home, and strictly contained in resident i's settlement; StepVersion increments by 1.
        /// Side effects: updates live resident directions, facings and per-resident wander RNG streams.
        /// </remarks>
        public void Step(long nowMs, int dtMs)
        {
            Contract.Requires(nowMs >= 0 && nowMs > _lastStepMs, "nowMs must be >= 0 and strictly increasing");
            Contract.Requires(dtMs > 0 && dtMs <= 1000, "dtMs must be 1..1000");
            _lastStepMs = nowMs;

            for (int i = 0; i < _residents.Length; i++)
            {
                StepResident(i, nowMs, dtMs);
            }

            StepVersion++;
        }

        /// <summary>True when a body of radius <paramref name="clearanceM"/> centred at <paramref name="direction"/> would overlap a resident.</summary>
        /// <remarks>
        /// REQ: characters.md#路人-npc (規則層實體，有碰撞).
        /// Pre: direction finite and non-zero (normalized internally); clearanceM finite and ≥ 0.
        /// Post: true iff some resident stands within clearanceM + PopulacePlanner.BodyRadiusM (chord distance at the
        ///       planet's radius). Side effects: none.
        /// </remarks>
        public bool IsBlocked(float3 direction, float clearanceM)
        {
            Contract.Requires(math.all(math.isfinite(direction)) && math.lengthsq(direction) > 0f,
                "direction must be finite and non-zero");
            Contract.Requires(math.isfinite(clearanceM) && clearanceM >= 0f, "clearanceM must be finite and >= 0");
            float3 d = math.normalize(direction);
            float reach = (clearanceM + PopulacePlanner.BodyRadiusM) / _radius;
            float reachSq = reach * reach;
            for (int i = 0; i < _residents.Length; i++)
            {
                if (math.distancesq(_currentDirections[i], d) < reachSq)
                {
                    return true;
                }
            }

            return false;
        }

        private void StepResident(int index, long nowMs, int dtMs)
        {
            RunResident r = _residents[index];
            if (_clearance == null || _clearance.SettlementRadiusM(r.Settlement) <= 0f)
            {
                return;
            }

            NpcDutySpec spec = PopulacePlanner.DutySpecFor(r.Duty);
            float capM = r.WanderRadiusMm * 0.001f;
            ref ResidentWander w = ref _wander[index];
            if ((spec.Mode == WanderMode.Shuttle || spec.Mode == WanderMode.Loop) && w.Route == null)
            {
                BuildRoute(index, in r, capM);
            }

            if (!w.HasTarget)
            {
                if (w.ResumeMs == Unscheduled)
                {
                    w.ResumeMs = nowMs + DrawPause(ref w.Rng, in r, in spec);
                    return;
                }

                if (nowMs < w.ResumeMs)
                {
                    return;
                }

                if (!TryPickTarget(index, in r, in spec, capM))
                {
                    w.ResumeMs = nowMs + DrawPause(ref w.Rng, in r, in spec);
                    return;
                }

                w.LegSpeedPermille = BoundedNormal.Sample(ref w.Rng, r.SpeedPermilleMean, spec.SpeedPermille.ActionSd,
                    spec.SpeedPermille.Min, spec.SpeedPermille.Max);
                w.LegRemainingMs = BoundedNormal.Sample(ref w.Rng, r.MoveMsMean, spec.MoveMs.ActionSd, spec.MoveMs.Min,
                    spec.MoveMs.Max);
                w.HasTarget = true;
            }

            float3 beforeDir = _currentDirections[index];
            float3 beforeFacing = _currentFacings[index];
            MoveResult res = Steer(index, r.Settlement, w.Target, w.LegSpeedPermille, dtMs);
            if (res != MoveResult.Blocked && ArcM(r.Direction, _currentDirections[index], _radius) > capM + ArriveM)
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
                w.ResumeMs = nowMs + DrawPause(ref w.Rng, in r, in spec);
            }
            else if (res == MoveResult.Blocked || w.LegRemainingMs <= 0)
            {
                w.HasTarget = false;
                w.ResumeMs = nowMs + DrawPause(ref w.Rng, in r, in spec);
            }
        }

        private void BuildRoute(int index, in RunResident r, float capM)
        {
            ref ResidentWander w = ref _wander[index];
            var points = new List<float3>(3) { r.Direction };
            for (int k = 0; k < 2; k++)
            {
                if (TryPointInSettlement(ref w.Rng, r.Settlement, r.Direction, capM, out float3 p))
                {
                    points.Add(p);
                }
            }

            w.Route = points.ToArray();
            w.RouteIndex = (int)w.Rng.NextUInt((uint)w.Route.Length);
            w.RouteStep = w.Rng.NextUInt(2) == 0 ? 1 : -1;
        }

        private static void AdvanceRoute(ref ResidentWander w, WanderMode mode)
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

        private bool TryPickTarget(int index, in RunResident r, in NpcDutySpec spec, float capM)
        {
            ref ResidentWander w = ref _wander[index];
            if (spec.Mode == WanderMode.Shuttle || spec.Mode == WanderMode.Loop)
            {
                if (w.Route == null || w.Route.Length < 2)
                {
                    return false;
                }

                w.Target = w.Route[w.RouteIndex];
                return true;
            }

            if (TryPointInSettlement(ref w.Rng, r.Settlement, r.Direction, capM, out float3 target))
            {
                w.Target = target;
                return true;
            }

            return false;
        }

        private bool TryPointInSettlement(ref Pcg32 rng, int settlement, float3 center, float radiusM, out float3 target)
        {
            for (int i = 0; i < WaypointRetries; i++)
            {
                target = WanderPoint(ref rng, center, radiusM);
                if ((_isStandable == null || _isStandable(target)) &&
                    !_clearance.IsInsideBuilding(target, PopulacePlanner.BodyRadiusM) &&
                    _clearance.IsContainedInSettlement(settlement, target, PopulacePlanner.BodyRadiusM))
                {
                    return true;
                }
            }

            target = default;
            return false;
        }

        private MoveResult Steer(int index, int settlement, float3 dest, int speedPermille, int dtMs)
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
            if (!CanStepTo(settlement, up, next))
            {
                if (!TrySlide(index, settlement, up, step, ref heading, ref next))
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

        private bool TrySlide(int index, int settlement, float3 up, float step, ref float3 heading, ref float3 next)
        {
            float3 left = math.cross(up, heading);
            ref ResidentWander w = ref _wander[index];
            for (int k = 0; k < 2; k++)
            {
                int sideSign = k == 0 ? w.SlideSide : -w.SlideSide;
                foreach (float2 turn in SlideTurns)
                {
                    float3 h = heading * turn.x + left * (turn.y * sideSign);
                    float3 n = math.normalize(up * math.cos(step) + h * math.sin(step));
                    if (CanStepTo(settlement, up, n))
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

        private bool CanStepTo(int settlement, float3 from, float3 to)
        {
            if (_isStandable != null && !_isStandable(to))
            {
                return false;
            }

            if (_clearance.IsInsideBuilding(to, PopulacePlanner.BodyRadiusM) &&
                !_clearance.IsInsideBuilding(from, PopulacePlanner.BodyRadiusM))
            {
                return false;
            }

            if (!_clearance.IsContainedInSettlement(settlement, to, PopulacePlanner.BodyRadiusM) &&
                _clearance.IsContainedInSettlement(settlement, from, PopulacePlanner.BodyRadiusM))
            {
                return false;
            }

            return true;
        }

        private static int DrawPause(ref Pcg32 rng, in RunResident r, in NpcDutySpec spec) =>
            BoundedNormal.Sample(ref rng, r.PauseMsMean, spec.PauseMs.ActionSd, spec.PauseMs.Min, spec.PauseMs.Max);

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
