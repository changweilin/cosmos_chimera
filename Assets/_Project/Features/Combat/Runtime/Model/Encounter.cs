using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Unity.Mathematics;

namespace Cosmimera.Combat
{
    /// <summary>Which side of an encounter a unit fights for.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum EncounterSide
    {
        /// <summary>The player's fielded creatures.</summary>
        Player = 0,
        /// <summary>Wild creatures, elites and the boss.</summary>
        Wild = 1,
    }

    /// <summary>Ground under a direction, so the rule layer stays independent of how terrain is generated.</summary>
    /// <remarks>Pre: implementations are pure and deterministic. Post: see member. Side effects: none.</remarks>
    public interface IEncounterSurface
    {
        /// <summary>Terrain altitude (m above Radius) under unit <paramref name="direction"/>.</summary>
        /// <remarks>Pre: direction unit length. Post: finite. Side effects: none.</remarks>
        float GroundAltitude(float3 direction);

        /// <summary>Whether unit <paramref name="direction"/> is sea, where wild units may not stand.</summary>
        /// <remarks>
        /// REQ: combat.md#只在陸地. Pre: direction unit length. Post: deterministic for a given direction on one host.
        /// Side effects: none.
        /// </remarks>
        bool IsSea(float3 direction);

        /// <summary>Whether a body of radius <paramref name="clearanceM"/> centred at <paramref name="direction"/> overlaps a building.</summary>
        /// <remarks>
        /// REQ: planet-world.md#道路與建築 (buildings collide with the character and creatures).
        /// Pre: direction unit length; clearanceM finite and ≥ 0. Post: deterministic for a given input on one host.
        /// Side effects: none.
        /// </remarks>
        bool IsBlocked(float3 direction, float clearanceM);
    }

    /// <summary>What happened during one tick, for presentation (VFX, numbers, HUD) only.</summary>
    /// <remarks>Pre: produced by <see cref="Encounter.Tick"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct EncounterEvent
    {
        internal EncounterEvent(EncounterEventKind kind, int sourceId, int targetId, int slot, int amount)
        {
            Kind = kind;
            SourceId = sourceId;
            TargetId = targetId;
            Slot = slot;
            Amount = amount;
        }

        /// <summary>Event kind.</summary>
        /// <remarks>Pre: none. Post: defined value. Side effects: none.</remarks>
        public EncounterEventKind Kind { get; }

        /// <summary>Acting unit id.</summary>
        /// <remarks>Pre: none. Post: a unit id. Side effects: none.</remarks>
        public int SourceId { get; }

        /// <summary>Receiving unit id, <see cref="Encounter.PlayerCharacterId"/>, or <see cref="CombatAi.NoTarget"/> for Cast.</summary>
        /// <remarks>
        /// Pre: none. Post: a unit id, PlayerCharacterId (Cast/Hit/Miss/PlayerDowned aimed at the player character) or
        /// NoTarget. Side effects: none.
        /// </remarks>
        public int TargetId { get; }

        /// <summary>Move slot of the cast.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Slot { get; }

        /// <summary>Damage (Hit), heal (Support) or 0.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Amount { get; }
    }

    /// <summary>Encounter event kinds.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum EncounterEventKind
    {
        /// <summary>A cast was accepted.</summary>
        Cast = 0,
        /// <summary>A hostile move hit.</summary>
        Hit = 1,
        /// <summary>A hostile move touched but the roll missed (MISS feedback, combat.md#命中與閃避).</summary>
        Miss = 2,
        /// <summary>A self/ally move landed.</summary>
        Support = 3,
        /// <summary>The target reached 0 HP.</summary>
        Faint = 4,
        /// <summary>The player character reached 0 HP (TargetId == Encounter.PlayerCharacterId); the run is lost (Q-C33).</summary>
        PlayerDowned = 5,
    }

    /// <summary>One creature taking part in an encounter.</summary>
    /// <remarks>
    /// Pre: build through <see cref="Encounter.AddCreature"/>. Post: Combatant and moves fixed for the unit's lifetime.
    /// Side effects: Position/Forward/Fielded/Engaged are written only by the owning Encounter.
    /// </remarks>
    public sealed class EncounterUnit
    {
        internal EncounterUnit(int id, EncounterSide side, CreatureDef def, Combatant combatant, CastMove[] moves,
            HitVolume[] volumes, RangeClass[] rangeClasses, float bodyRadiusM, int agi, float3 home)
        {
            Id = id;
            Side = side;
            Def = def;
            Combatant = combatant;
            Moves = moves;
            Volumes = volumes;
            RangeClasses = rangeClasses;
            BodyRadiusM = bodyRadiusM;
            Agi = agi;
            Home = home;
        }

        /// <summary>Unique id inside the encounter; also the AI's target id.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Id { get; }

        /// <summary>Side.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public EncounterSide Side { get; }

        /// <summary>Species form.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public CreatureDef Def { get; }

        /// <summary>Level its stats are currently scaled to (raised only by <see cref="Encounter.SetLevel"/>).</summary>
        /// <remarks>Pre: none. Post: 1..100; never decreases. Side effects: none.</remarks>
        public int Level { get; internal set; }

        /// <summary>Whether this unit is a planet boss (HP ×2, body and melee volumes ×1.5 — D0-R2).</summary>
        /// <remarks>Pre: none. Post: fixed at creation; true only for wild units built by Encounter.AddBoss. Side effects: none.</remarks>
        public bool IsBoss { get; internal set; }

        internal MoveDef[] MoveDefs { get; set; }

        /// <summary>HP, cooldowns and effects.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public Combatant Combatant { get; }

        /// <summary>Moves by slot (slot 0 = signature move; slot 1 = talisman-card partner move when present).</summary>
        /// <remarks>Pre: none. Post: length 1..2. Side effects: none.</remarks>
        public IReadOnlyList<CastMove> Moves { get; }

        internal HitVolume[] Volumes { get; }

        internal RangeClass[] RangeClasses { get; }

        /// <summary>Hit-test sphere radius (m), from the size class.</summary>
        /// <remarks>Pre: none. Post: > 0. Side effects: none.</remarks>
        public float BodyRadiusM { get; }

        /// <summary>
        /// Separation radius (m) on the sphere: no other active body's circle may overlap it (combat.md#實體碰撞). Distinct
        /// from <see cref="BodyRadiusM"/>, which sizes the hit-test sphere.
        /// </summary>
        /// <remarks>Pre: none. Post: > 0; boss = BossSizePermille/1000 × balance body_mm. Side effects: none.</remarks>
        public float CollisionRadiusM { get; internal set; }

        internal int Agi { get; set; }

        /// <summary>Leash anchor for wild units (their spawn point); unused for the player's side.</summary>
        /// <remarks>Pre: none. Post: as constructed. Side effects: none.</remarks>
        public float3 Home { get; }

        /// <summary>Body-center world position.</summary>
        /// <remarks>Pre: none. Post: inside the combat shell. Side effects: none.</remarks>
        public float3 Position { get; internal set; }

        /// <summary>Unit tangent facing.</summary>
        /// <remarks>Pre: none. Post: unit, tangent at Position. Side effects: none.</remarks>
        public float3 Forward { get; internal set; }

        /// <summary>Whether the unit currently takes part (player: summoned; wild: always true until removed).</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool Fielded { get; internal set; }

        /// <summary>Wild only: aggroed by the player's side and therefore a valid target for it.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool Engaged { get; internal set; }

        internal bool Provoked { get; set; }

        // Idle wander state (combat.md#遊蕩, D0-W1). WanderResumeMs == Encounter.WanderUnscheduled means "decide on the next
        // idle tick" — the unit was just spawned or just disengaged.
        internal bool HasWanderTarget { get; set; }
        internal float3 WanderTarget { get; set; }
        internal long WanderResumeMs { get; set; } = Encounter.WanderUnscheduled;

        // Side (+1 left of heading, −1 right) of the last wall slide that moved. Sticky so a unit keeps rounding a building
        // the same way; re-choosing every tick made it zigzag along the near wall (heading re-aims at the target each tick).
        internal int SlideSide { get; set; } = 1;

        /// <summary>Last AI target id, or NoTarget.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public int TargetId { get; internal set; } = CombatAi.NoTarget;

        /// <summary>Whether the unit is fielded and alive.</summary>
        /// <remarks>Pre: none. Post: Fielded &amp;&amp; !Combatant.IsFainted. Side effects: none.</remarks>
        public bool IsActive => Fielded && !Combatant.IsFainted;
    }

    /// <summary>
    /// Deterministic real-time fight loop: AI decision → steering → cast → geometry → resolution, for every unit in id
    /// order. REQ: docs/design/combat.md (rules), docs/roadmap/demo.md (D0 glue), Q-T1 steering.
    /// </summary>
    /// <remarks>
    /// Field moves persist for field_duration_ms and resolve on <see cref="FieldTimeline"/> ticks (combat.md#場地).
    /// Wild units engage by territory (combat.md#野生領域, D0-E2): an intruder — the player character or an active
    /// player-side creature — inside the species' temperament radius starts the fight, and the nearest enemy, the player
    /// character included once attached through <see cref="AttachPlayer"/>, is the target.
    /// D0 simplifications (recorded in docs/tech-debt.md): steering is direct great-circle movement without obstacle
    /// avoidance; movement speed ignores move_speed effects. All of these are presentation-visible but never break a rule-layer contract.
    /// Pre: one owner, main thread. Side effects: owns its units and RNG stream.
    /// </remarks>
    public sealed class Encounter
    {
        /// <summary>
        /// How far (m) the player may reach to pick a not-yet-engaged wild unit as a Focus target and so start the fight
        /// with it (<see cref="Provoke"/>). Kept at the pre-territory aggro radius so timid creatures stay catchable.
        /// </summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const float ProvokeRadiusM = 18f;

        /// <summary>Engaged wild units disengage when no intruder is within this distance (m).</summary>
        /// <remarks>Pre: none. Post: constant > every territory radius (TerritoryTuning Pre). Side effects: none.</remarks>
        public const float DisengageRadiusM = 40f;

        /// <summary>Target id of the player character in AI decisions and events (never a Units index).</summary>
        /// <remarks>Pre: none. Post: constant; above every unit id, so distance ties prefer creatures. Side effects: none.</remarks>
        public const int PlayerCharacterId = int.MaxValue;

        /// <summary>Target id the main creature carries while it attacks the focused object (<see cref="FocusObject"/>); never a Units index.</summary>
        /// <remarks>
        /// REQ: destructibles.md#誰能打. Pre: none. Post: constant, distinct from PlayerCharacterId and every unit id.
        /// Side effects: none. A Focus command with this target attacks the focused object.
        /// </remarks>
        public const int ObjectTargetId = int.MaxValue - 1;

        /// <summary>Hit-test sphere radius (m) of the player character: ~0.35 × 1.8 m, the BodyRadiusFor convention.</summary>
        /// <remarks>Pre: none. Post: constant > 0. Side effects: none.</remarks>
        public const float PlayerBodyRadiusM = 0.6f;

        /// <summary>Boss MaxHp multiplier. REQ: docs/design/planet-world.md (D0-R2, 2026-11-10).</summary>
        /// <remarks>Pre: none. Post: constant >= 1. Side effects: none.</remarks>
        public const int BossHpMultiplier = 2;

        /// <summary>Boss body radius and melee hit-volume scale, in permille. REQ: planet-world.md (D0-R2).</summary>
        /// <remarks>Pre: none. Post: constant >= 1000. Side effects: none.</remarks>
        public const int BossSizePermille = 1500;

        private const float ArriveM = 0.4f;
        private const int MaxSlots = 2;
        internal const long WanderUnscheduled = long.MinValue;

        private readonly PlanetConfig _planet;
        private readonly CombatBalance _balance;
        private readonly IEncounterSurface _surface;
        private readonly List<EncounterUnit> _units = new List<EncounterUnit>(32);
        private readonly List<EncounterEvent> _events = new List<EncounterEvent>(64);
        private readonly List<AiEnemy> _enemyBuffer = new List<AiEnemy>(32);
        private readonly List<AiMoveOption> _optionBuffer = new List<AiMoveOption>(MaxSlots);
        private readonly List<PlacedField> _fields = new List<PlacedField>(16);
        private Pcg32 _rng;
        // Separate stream so wandering never shifts the combat roll order (hit/crit/capture replays stay identical).
        private Pcg32 _wanderRng;
        private readonly int _rangeCapMm;
        private long _lastTickMs = long.MinValue;
        private PlayerVitals _player;
        private float3 _playerCenter;
        private float3 _playerPush;
        private DestructibleTable _objects;
        private IDestructibleIndex _objectIndex;
        private Func<float3, bool> _isNight;
        // Separate stream: object drops must not shift combat, wander, capture or creature-material rolls.
        private Pcg32 _objectRng;
        private readonly List<ObjectEvent> _objectEvents = new List<ObjectEvent>(16);
        private readonly List<DestructibleCandidate> _candidateBuffer = new List<DestructibleCandidate>(32);
        private readonly List<int> _burntBuffer = new List<int>(8);
        private bool _hasObjectFocus;
        private DestructibleCandidate _objectFocus;
        // Edge point of the focused object's hit sphere nearest the main creature, refreshed each time it decides.
        private float3 _objectAim;

        // A field is ground-anchored and keeps the caster modifiers frozen at cast (combat.md#特性觸發: "施放者的修正值在此刻
        // 凍結…每次場地結算都用同一份"), so it outlives its caster fainting or being recalled.
        private readonly struct PlacedField
        {
            public PlacedField(int casterId, EncounterSide side, int slot, in CastContext ctx, in HitVolume volume,
                float3 anchor, float3 forward, in FieldTimeline timeline)
            {
                CasterId = casterId;
                Side = side;
                Slot = slot;
                Ctx = ctx;
                Volume = volume;
                Anchor = anchor;
                Forward = forward;
                Timeline = timeline;
            }

            public int CasterId { get; }
            public EncounterSide Side { get; }
            public int Slot { get; }
            public CastContext Ctx { get; }
            public HitVolume Volume { get; }
            public float3 Anchor { get; }
            public float3 Forward { get; }
            public FieldTimeline Timeline { get; }
        }

        /// <summary>Number of fields still alive after the last tick (presentation and tests).</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int ActiveFieldCount => _fields.Count;

        /// <summary>Creates an empty encounter.</summary>
        /// <remarks>
        /// Pre: planet.Radius >= MoveGeometry.MinSupportedRadiusM; balance and surface non-null.
        /// Post: no units; combat RNG seeded from (seed, 'C') and wander RNG from (seed, 'W') so all peers replay the same rolls.
        /// Side effects: allocates buffers.
        /// </remarks>
        public Encounter(in PlanetConfig planet, CombatBalance balance, IEncounterSurface surface, ulong seed)
        {
            Contract.Requires(planet.IsValid && planet.Radius >= MoveGeometry.MinSupportedRadiusM,
                "combat needs R >= MoveGeometry.MinSupportedRadiusM");
            Contract.Requires(balance != null && surface != null, "balance and surface must not be null");
            _planet = planet;
            _balance = balance;
            _surface = surface;
            _rng = new Pcg32(seed, 'C');
            _wanderRng = new Pcg32(seed, 'W');
            _objectRng = new Pcg32(seed, 'O');
            _rangeCapMm = balance.Horizon.CapMm(planet.Radius);
        }

        /// <summary><see cref="ObjectEvent.SourceId"/> of an object that burnt out on its own.</summary>
        /// <remarks>Pre: none. Post: constant; never a unit id. Side effects: none.</remarks>
        public const int NoSource = -1;

        /// <summary>Lets moves hit generated scene objects. REQ: docs/design/destructibles.md, ADR-0009.</summary>
        /// <remarks>
        /// Pre: table, index and isNight non-null; called at most once, before the first Tick; isNight(direction) answers
        ///      "sun below the horizon there" at the host's current time.
        /// Post: every damaging hostile volume (any side, fields included) applies to each object it overlaps; burning
        ///       objects tick each Tick; destroyed objects roll drops on the object stream (seed, 'O').
        /// Side effects: stores the references.
        /// </remarks>
        public void AttachDestructibles(DestructibleTable table, IDestructibleIndex index, Func<float3, bool> isNight)
        {
            Contract.Requires(table != null && index != null && isNight != null, "table, index and isNight are required");
            Contract.Requires(_objects == null, "destructibles already attached");
            Contract.Requires(_lastTickMs == long.MinValue, "attach before the first Tick");
            _objects = table;
            _objectIndex = index;
            _isNight = isNight;
        }

        /// <summary>Sets the object a <c>Focus(ObjectTargetId)</c> command attacks. REQ: destructibles.md#誰能打.</summary>
        /// <remarks>
        /// Pre: destructibles attached; target from the attached index (objects never move, so its centre stays valid).
        /// Post: FocusedObjectId == target.Id. While the command is Focus(ObjectTargetId) and the object is not
        ///       destroyed, the main creature decides by CombatAi against the object alone, as an immobile enemy at the
        ///       edge of its hit sphere nearest the creature (so reach and stand-off count from the object's surface);
        ///       its TargetId is ObjectTargetId and it aims there. Once destroyed, the command acts as Free. The support
        ///       never takes the object (it is absent from its enemy list), and nothing is provoked.
        /// Side effects: replaces any previous object focus.
        /// </remarks>
        public void FocusObject(in DestructibleCandidate target)
        {
            Contract.Requires(_objects != null, "attach destructibles before focusing an object");
            _objectFocus = target;
            _hasObjectFocus = true;
        }

        /// <summary>Forgets the focused object.</summary>
        /// <remarks>Pre: none. Post: FocusedObjectId == -1; Focus(ObjectTargetId) acts as Free. Side effects: none beyond that.</remarks>
        public void ClearObjectFocus() => _hasObjectFocus = false;

        /// <summary>Id of the object <see cref="FocusObject"/> set, or -1.</summary>
        /// <remarks>Pre: none. Post: -1 when none is set (a destroyed object stays set until cleared). Side effects: none.</remarks>
        public long FocusedObjectId => _hasObjectFocus ? _objectFocus.Id : -1;

        /// <summary>Object events of the last tick, in resolution order.</summary>
        /// <remarks>Pre: none. Post: cleared at the start of each Tick; empty when nothing is attached. Side effects: none.</remarks>
        public IReadOnlyList<ObjectEvent> ObjectEvents => _objectEvents;

        /// <summary>Ranged-distance cap on this planet (mm), from balance.Horizon and planet.Radius.</summary>
        /// <remarks>Pre: none. Post: 1..ranged_distance.cap_mm; fixed for the encounter's lifetime. Side effects: none.</remarks>
        public int RangeCapMm => _rangeCapMm;

        /// <summary>All units in id order.</summary>
        /// <remarks>Pre: none. Post: Units[i].Id == i. Side effects: none.</remarks>
        public IReadOnlyList<EncounterUnit> Units => _units;

        /// <summary>Events of the last tick.</summary>
        /// <remarks>Pre: none. Post: cleared at the start of each Tick. Side effects: none.</remarks>
        public IReadOnlyList<EncounterEvent> Events => _events;

        /// <summary>
        /// World-space displacement the caller must apply to the player character's feet after the last Tick so it no
        /// longer overlaps an active unit (combat.md#實體碰撞). The character is driven by Unity physics outside this
        /// model, hence the push is reported rather than applied.
        /// </summary>
        /// <remarks>
        /// Pre: none.
        /// Post: zero before the first Tick and when the character overlapped nothing; otherwise a move along the sphere
        ///       that keeps the feet's distance from the planet centre.
        /// Side effects: none.
        /// </remarks>
        public float3 PlayerPush => _playerPush;

        /// <summary>Hit-test radius for a size class (m): ~0.35 × the class's representative height (creatures.md#體型級距).</summary>
        /// <remarks>Pre: sizeClass defined. Post: > 0. Side effects: none.</remarks>
        public static float BodyRadiusFor(CreatureSizeClass sizeClass)
        {
            switch (sizeClass)
            {
                case CreatureSizeClass.XS: return 0.1f;
                case CreatureSizeClass.S: return 0.16f;
                case CreatureSizeClass.M: return 0.35f;
                case CreatureSizeClass.L: return 0.7f;
                case CreatureSizeClass.XL: return 1.1f;
                default:
                    Contract.Requires(false, "undefined CreatureSizeClass");
                    return 0f;
            }
        }

        /// <summary>
        /// Adds a creature at <paramref name="level"/> (CON/STR/SPI/AGI scaled from the form's LV100 basis,
        /// level-progression.md#四維縮放) at full HP, standing on the ground under <paramref name="direction"/>.
        /// </summary>
        /// <remarks>
        /// Pre: def non-null with a signature move; partnerMove null or a MoveDef; direction unit length; 1 &lt;= level &lt;= 100.
        /// Post: returns the unit with Id == previous Units.Count and Level == level; Fielded == (side == Wild); Forward tangent.
        /// Side effects: allocates the unit; does not fire on_summon (call <see cref="Field"/> for the player's side).
        /// </remarks>
        public EncounterUnit AddCreature(EncounterSide side, CreatureDef def, MoveDef partnerMove, float3 direction, int level)
        {
            return AddUnit(side, def, partnerMove, direction, level, false);
        }

        /// <summary>
        /// Adds the planet boss: a wild unit like <see cref="AddCreature"/>, with MaxHp × <see cref="BossHpMultiplier"/>
        /// and body radius and melee move volumes × <see cref="BossSizePermille"/>/1000. REQ: planet-world.md (D0-R2).
        /// </summary>
        /// <remarks>
        /// Pre: as AddCreature.
        /// Post: as AddCreature with side Wild, plus IsBoss; MaxHp == BossHpMultiplier × the AddCreature MaxHp at that
        ///       level; BodyRadiusM == BossSizePermille/1000 × BodyRadiusFor(def.SizeClass). Mid/long and dash volumes are
        ///       unchanged (the decision names melee reach only).
        /// Side effects: allocates the unit.
        /// </remarks>
        public EncounterUnit AddBoss(CreatureDef def, MoveDef partnerMove, float3 direction, int level)
        {
            return AddUnit(EncounterSide.Wild, def, partnerMove, direction, level, true);
        }

        private EncounterUnit AddUnit(EncounterSide side, CreatureDef def, MoveDef partnerMove, float3 direction, int level,
            bool boss)
        {
            Contract.Requires(def != null && def.SignatureMove != null, "creature needs a signature move");
            Contract.Requires(level >= 1 && level <= 100, "level must be 1..100");
            Contract.Requires(math.abs(math.lengthsq(direction) - 1f) <= 2e-3f, "direction must be unit length");
            int slots = partnerMove != null ? 2 : 1;
            var moves = new CastMove[slots];
            var volumes = new HitVolume[slots];
            var ranges = new RangeClass[slots];
            var defs = new MoveDef[slots];
            CreatureStats s = def.Stats.AtLevel(level);
            for (int i = 0; i < slots; i++)
            {
                MoveDef m = i == 0 ? def.SignatureMove : partnerMove;
                defs[i] = m;
                moves[i] = CastMove.FromDef(m);
                volumes[i] = BuildVolume(m, s.Str, boss);
                ranges[i] = m.RangeClass;
            }

            var mastery = new int[10];
            var resist = new int[10];
            foreach (ElementValue ev in def.Mastery)
            {
                mastery[(int)ev.Element] = ev.Value;
            }

            for (int e = 0; e < 10; e++)
            {
                resist[e] = def.GetResist((Element)e);
            }

            EffectBook book = BookFor(def.Trait);
            var combatant = new Combatant(_balance.Stats, _balance.Effects, s.Con, s.Spi, s.Agi, mastery, resist, book, slots);
            float radius = BodyRadiusFor(def.SizeClass);
            int collisionMm = _balance.Collision.BodyMm(def.SizeClass);
            if (boss)
            {
                combatant.MultiplyMaxHp(BossHpMultiplier);
                radius = radius * BossSizePermille / 1000f;
                // REQ: combat.md#實體碰撞 — a boss's body grows with its hit volume.
                collisionMm = ScaleBoss(collisionMm);
            }

            float3 position = GroundPoint(direction, radius);
            var unit = new EncounterUnit(_units.Count, side, def, combatant, moves, volumes, ranges, radius, s.Agi, position)
            {
                Level = level,
                IsBoss = boss,
                CollisionRadiusM = collisionMm * 0.001f,
                MoveDefs = defs,
                Position = position,
                Forward = PlanetFrame.FromUp(direction).North,
                Fielded = side == EncounterSide.Wild,
            };
            _units.Add(unit);
            return unit;
        }

        private bool AnyActiveEngaged()
        {
            for (int i = 0; i < _units.Count; i++)
            {
                if (_units[i].Engaged && _units[i].IsActive)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Raises a unit to <paramref name="level"/> between battles: CON/SPI/AGI and STR-scaled hit volumes are re-derived
        /// from the form's LV100 basis. REQ: Q-C31 (2026-11-09) — level-up effects settle when the battle ends.
        /// </summary>
        /// <remarks>
        /// Pre: unitId indexes Units; unit not fainted; unit.Level &lt;= level &lt;= 100; called between Ticks while no
        ///      active unit is Engaged (stats must not change mid-fight, Q-C31).
        /// Post: Level == level; volumes and stats equal those AddCreature (or AddBoss when IsBoss, keeping its HP and melee
        ///       multipliers) would build at that level; Hp grows by the MaxHp
        ///       increase; position, cooldowns and effects unchanged. level == Level is a no-op.
        /// Side effects: mutates the unit.
        /// </remarks>
        public void SetLevel(int unitId, int level)
        {
            Contract.Requires(unitId >= 0 && unitId < _units.Count, "unknown unit");
            EncounterUnit u = _units[unitId];
            Contract.Requires(level >= u.Level && level <= 100, "level must be in Level..100");
            Contract.Requires(!u.Combatant.IsFainted, "a fainted unit cannot level up");
            // The Pre "between battles" is checkable here: a battle exists exactly while an active wild is engaged
            // (RunSession.AnyEngaged), so a mid-fight call is a caller bug rather than a rule we can only document.
            Contract.Requires(!AnyActiveEngaged(), "stats must not change mid-fight (Q-C31)");
            if (level == u.Level)
            {
                return;
            }

            CreatureStats s = u.Def.Stats.AtLevel(level);
            for (int i = 0; i < u.Volumes.Length; i++)
            {
                u.Volumes[i] = BuildVolume(u.MoveDefs[i], s.Str, u.IsBoss);
            }

            u.Combatant.Rescale(s.Con, s.Spi, s.Agi);
            u.Agi = s.Agi;
            u.Level = level;
        }

        private HitVolume BuildVolume(MoveDef m, int str, bool boss)
        {
            // MoveDef.Origin is only defined for bursts (its getter enforces that); Build ignores it otherwise.
            BurstOrigin origin = m.Shape == MoveShape.Burst ? m.Origin : BurstOrigin.Caster;
            HitVolume v = MoveGeometry.Build(m.Shape, m.Movement, m.RangeClass, origin, m.BaseRangeMm, str, _balance.Stats,
                _balance.Hit);
            // Melee volumes are already "the body's reach" scaled by STR (combat.md#射程), so a larger boss body scales
            // them the same way. Ranged caps (36 m) and data-authored dash lengths stay untouched.
            if (!boss || m.RangeClass != RangeClass.Melee || v.DashDistanceMm > 0)
            {
                return v;
            }

            return new HitVolume(v.Kind, v.Anchor, ScaleBoss(v.ReachMm), ScaleBoss(v.WidthMm), v.HalfAngleDeciDeg,
                ScaleBoss(v.HalfHeightMm), ScaleBoss(v.CastDistanceMm), v.DashDistanceMm);
        }

        private static int ScaleBoss(int mm) => (int)((long)mm * BossSizePermille / 1000);

        /// <summary>Puts a player-side unit on the field at <paramref name="direction"/> and fires on_summon.</summary>
        /// <remarks>
        /// Pre: unitId is a Player-side, unfielded, non-fainted unit; nowMs >= last tick time; direction unit length.
        /// Post: unit.Fielded; positioned on the ground there.
        /// Side effects: mutates the unit's Combatant (trait heals) via EnterField.
        /// </remarks>
        public void Field(int unitId, float3 direction, bool bySwap, long nowMs)
        {
            EncounterUnit u = Get(unitId);
            Contract.Requires(u.Side == EncounterSide.Player && !u.Fielded && !u.Combatant.IsFainted,
                "only an unfielded, conscious player-side unit can be fielded");
            u.Position = GroundPoint(math.normalize(direction), u.BodyRadiusM);
            u.Forward = PlanetFrame.FromUp(math.normalize(direction)).North;
            u.Fielded = true;
            u.Combatant.EnterField(bySwap, nowMs);
        }

        /// <summary>Takes a player-side unit off the field (recall or switch out).</summary>
        /// <remarks>Pre: unitId is a fielded Player-side unit. Post: !Fielded. Side effects: none beyond that.</remarks>
        public void Unfield(int unitId)
        {
            EncounterUnit u = Get(unitId);
            Contract.Requires(u.Side == EncounterSide.Player && u.Fielded, "only a fielded player-side unit can be recalled");
            u.Fielded = false;
            u.TargetId = CombatAi.NoTarget;
        }

        /// <summary>Removes a wild unit from play (captured, or left behind when its region unloads).</summary>
        /// <remarks>Pre: unitId is a Wild unit. Post: !Fielded, !Engaged. Side effects: none beyond that.</remarks>
        public void Remove(int unitId)
        {
            EncounterUnit u = Get(unitId);
            Contract.Requires(u.Side == EncounterSide.Wild, "only wild units are removed");
            u.Fielded = false;
            u.Engaged = false;
            u.Provoked = false;
        }

        /// <summary>
        /// Makes the player character a target of wild units from now on (Q-C33, 2026-11-10). Hits on it go through
        /// <paramref name="vitals"/> (dodge-roll i-frames apply); reaching 0 HP raises one PlayerDowned event.
        /// </summary>
        /// <remarks>
        /// Pre: vitals non-null; no player attached yet.
        /// Post: wild units may target <see cref="PlayerCharacterId"/> while !vitals.IsDowned.
        /// Side effects: keeps a reference to vitals; Tick mutates it through ApplyDamage.
        /// </remarks>
        public void AttachPlayer(PlayerVitals vitals)
        {
            Contract.Requires(vitals != null, "vitals must not be null");
            Contract.Requires(_player == null, "a player is already attached");
            _player = vitals;
        }

        /// <summary>The player starts a fight with a wild unit (Focus on it), whatever its temperament.</summary>
        /// <remarks>
        /// Pre: unitId is an active Wild unit.
        /// Post: Engaged from the next Tick (if still active); then it stays engaged until no intruder is within
        ///       DisengageRadiusM. Engaged itself is unchanged until then.
        /// Side effects: mutates the unit.
        /// </remarks>
        public void Provoke(int unitId)
        {
            EncounterUnit u = Get(unitId);
            Contract.Requires(u.Side == EncounterSide.Wild && u.IsActive, "only an active wild unit can be provoked");
            // Deferred to the Tick: input is read before the host's between-battles step, and an Engaged flag set here
            // would make that step's level-ups look mid-fight (SetLevel's Q-C31 guard).
            u.Provoked = true;
        }

        /// <summary>Territory radius (m) of a unit, from its species temperament (combat.md#野生領域).</summary>
        /// <remarks>Pre: unitId indexes Units. Post: >= 0; 0 means it never starts a fight by proximity. Side effects: none.</remarks>
        public float TerritoryRadiusM(int unitId) => _balance.Territory.RadiusMm(Get(unitId).Def.Temperament) * 0.001f;

        /// <summary>Wander radius (m) of a unit around its spawn point, from its species temperament (combat.md#遊蕩).</summary>
        /// <remarks>Pre: unitId indexes Units. Post: >= 0; 0 means it holds its spawn point. Side effects: none.</remarks>
        public float WanderRadiusM(int unitId) => _balance.Wander.RadiusMm(Get(unitId).Def.Temperament) * 0.001f;

        /// <summary>Advances the fight to <paramref name="nowMs"/>.</summary>
        /// <remarks>
        /// Pre: nowMs > previous tick's nowMs (strictly increasing CombatTime); 0 &lt; dtMs &lt;= 1000; mainId is a fielded
        ///      Player unit or NoTarget; player position inside the combat shell.
        /// Post: Events lists, in order: resolutions of fields placed in earlier ticks (fields in placement order, targets
        ///       in id order), then each acting unit's cast and its resolutions in unit-id order (a new field resolves
        ///       at once); positions stay on the ground. Idle (not engaged) wild units wander (combat.md#遊蕩): pause
        ///       PauseMinMs..PauseMaxMs, then walk at SpeedPermille of their move speed to a seeded point within
        ///       WanderRadiusM of Home; one outside that radius (just disengaged) first walks straight back to Home.
        ///       Last, overlapping bodies are pushed apart (combat.md#實體碰撞): active units (CollisionRadiusM) and the
        ///       character at playerPosition (balance body_mm.character), each pair split by inverse footprint area, in
        ///       <see cref="SeparationPasses"/> sweeps in unit-id order; the character's share is in PlayerPush. A pair
        ///       can still overlap afterwards only when a third body pushed it back (crowds settle over later ticks).
        ///       No step, dash or push moves a unit from outside a building (IEncounterSurface.IsBlocked at its
        ///       CollisionRadiusM) to inside one; a step into a wall slides along it (heading turned 45°, 90°, then 135°,
        ///       toward the unit's last slide side, then the other side) or, if every turn is walled, does not move.
        ///       A unit already inside (spawned or pushed there) may move freely.
        /// Side effects: mutates units, both RNG streams, Events and PlayerPush. No allocation after warm-up.
        /// </remarks>
        public void Tick(long nowMs, int dtMs, float3 playerPosition, in AiCommand command, int mainId)
        {
            Contract.Requires(nowMs > _lastTickMs, "ticks must strictly advance CombatTime");
            Contract.Requires(dtMs > 0 && dtMs <= 1000, "dtMs must be 1..1000");
            long previousMs = _lastTickMs;
            _lastTickMs = nowMs;
            _events.Clear();
            _objectEvents.Clear();
            // Units stand with their body centre one radius above the ground (GroundPoint); the character's position is
            // its feet, so lift it the same way to keep MoveGeometry's height test consistent.
            _playerCenter = playerPosition + Up(playerPosition) * PlayerBodyRadiusM;
            UpdateEngagement(playerPosition);

            // Fields placed in earlier ticks resolve before any unit acts, in placement order, so every peer sees the same
            // RNG draw order. Their tick-0 resolution already happened inside the placing TryCast.
            ResolveFields(previousMs, nowMs);

            for (int i = 0; i < _units.Count; i++)
            {
                EncounterUnit u = _units[i];
                if (!u.IsActive)
                {
                    continue;
                }

                bool player = u.Side == EncounterSide.Player;
                if (!player && !u.Engaged)
                {
                    Wander(u, nowMs, dtMs);
                    continue;
                }

                EncounterUnit partner = PartnerOf(u);
                AiRole role = !player || u.Id == mainId || mainId == CombatAi.NoTarget ? AiRole.Main : AiRole.Support;
                AiCommand cmd = player && role == AiRole.Main ? command : new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
                bool onObject = cmd.Kind == AiCommandKind.Focus && cmd.TargetId == ObjectTargetId && ObjectFocusIntact();
                if (cmd.Kind == AiCommandKind.Focus && !onObject && !IsValidEnemy(u, cmd.TargetId))
                {
                    cmd = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
                }

                int mainTarget = role == AiRole.Support && mainId >= 0 ? _units[mainId].TargetId : CombatAi.NoTarget;
                float3 leashAnchor = player ? playerPosition : u.Home;
                if (onObject)
                {
                    FillObjectEnemy(u);
                }
                else
                {
                    FillEnemies(u);
                }

                FillOptions(u, nowMs);
                var input = new AiInput(u.Position, u.Def.AiStyle, role, cmd, mainTarget, leashAnchor,
                    partner != null, partner != null ? partner.Position : float3.zero);
                AiDecision d = CombatAi.Decide(_planet, _balance.Ai, input, _enemyBuffer, _optionBuffer);
                u.TargetId = d.TargetId;

                Steer(u, d.DestinationDirection, nowMs, dtMs, 1000);
                if (d.TargetId != CombatAi.NoTarget)
                {
                    u.Forward = TangentToward(u.Position, TargetPosition(d.TargetId), u.Forward);
                }

                if (d.CastSlot != CombatAi.NoCast)
                {
                    TryCast(u, partner, d.CastSlot, d.TargetId, nowMs);
                }
            }

            BurnObjects(nowMs);
            Separate(playerPosition);
        }

        private void BurnObjects(long nowMs)
        {
            if (_objects == null)
            {
                return;
            }

            _burntBuffer.Clear();
            _objects.Tick(nowMs, _burntBuffer);
            foreach (int index in _burntBuffer)
            {
                EmitDestroyed(index, NoSource, 0);
            }
        }

        // REQ: destructibles.md#誰能打 — every damaging volume resolves against every object it touches; objects never roll
        // and never draw from the combat stream (destructibles.md#傷害), so attaching them leaves combat replays unchanged.
        private void ResolveObjects(int casterId, in CastContext ctx, in HitVolume volume, float3 anchor, float3 forward,
            long nowMs)
        {
            if (_objects == null || !ctx.Move.DealsDamage)
            {
                return;
            }

            float reachM = ((long)volume.ReachMm + volume.WidthMm + volume.HalfHeightMm) * 0.001f + _objectIndex.MaxHitRadiusM;
            _candidateBuffer.Clear();
            _objectIndex.Query(anchor, reachM, _candidateBuffer);
            foreach (DestructibleCandidate c in _candidateBuffer)
            {
                if (_objects.IsDestroyed(c.Id) ||
                    !MoveGeometry.Overlaps(volume, _planet, anchor, forward, c.Center, c.HitRadiusM))
                {
                    continue;
                }

                int index = _objects.IndexOf(c.Id);
                if (index < 0)
                {
                    index = _objects.Register(c.Id, c.Composition, c.VolumeL, c.Center);
                }

                ObjectHitResult r = _objects.ApplyHit(index, ctx.Move.Element, ctx.Move.Power, ctx.MasteryPercent, nowMs);
                _objectEvents.Add(new ObjectEvent(ObjectEventKind.Hit, c.Id, casterId, r.Damage, r.Reactions, r.Cause,
                    default, ctx.Move.Power));
                if (r.Destroyed)
                {
                    EmitDestroyed(index, casterId, ctx.Move.Power);
                }
            }
        }

        private void EmitDestroyed(int index, int sourceId, int power)
        {
            // REQ: destructibles.md#影素材 — night is judged where the object stood.
            ObjectDrop drop = _objects.RollDrop(index, _isNight(_planet.ToDirection(_objects.CenterOf(index))), ref _objectRng);
            _objectEvents.Add(new ObjectEvent(ObjectEventKind.Destroyed, _objects.IdOf(index), sourceId, 0,
                ObjectReaction.None, _objects.CauseOf(index), drop, power));
        }

        /// <summary>
        /// Reach (m) for close interaction with an idle wild (observe / provoke / pet), measured between body edges.
        /// REQ: combat.md#近身互動 (D0-W3); 3 m is the value decided in Q-D2 (2026-11-10).
        /// </summary>
        public const float InteractRadiusM = 3f;

        /// <summary>The idle wild unit the character at <paramref name="playerPosition"/> can interact with, if any.</summary>
        /// <remarks>
        /// Pre: none.
        /// Post: returns the id of the active, not Engaged and not Provoked Wild unit whose body edge is within
        ///       InteractRadiusM of the character's body edge (arc on the sphere minus both collision radii), the
        ///       smallest such gap winning and ties going to the lower id; -1 when there is none.
        /// Side effects: none.
        /// </remarks>
        public int NearestInteractable(float3 playerPosition)
        {
            float3 here = Up(playerPosition);
            float character = _balance.Collision.CharacterMm * 0.001f;
            int best = -1;
            float bestGap = float.MaxValue;
            for (int i = 0; i < _units.Count; i++)
            {
                EncounterUnit u = _units[i];
                if (u.Side != EncounterSide.Wild || !u.IsActive || u.Engaged || u.Provoked)
                {
                    continue;
                }

                float gap = ArcM(here, Up(u.Position), _planet.Radius) - u.CollisionRadiusM - character;
                if (gap <= InteractRadiusM && gap < bestGap)
                {
                    best = i;
                    bestGap = gap;
                }
            }

            return best;
        }

        /// <summary>Sweeps of the pairwise push-out per Tick.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public const int SeparationPasses = 2;

        // REQ: combat.md#實體碰撞 (D0-W2). Runs after every move of the tick, so this tick's casts used un-separated
        // positions; the shift is at most a body width. Two sweeps settle a pair and most 3-body pile-ups; residual overlap
        // in a crowd is left to later ticks rather than iterating to convergence, keeping the cost O(passes × n²), n ≤ 32.
        private void Separate(float3 playerPosition)
        {
            float r = _planet.Radius;
            float3 playerStart = Up(playerPosition);
            float3 playerDir = playerStart;
            float playerRadius = _balance.Collision.CharacterMm * 0.001f;
            for (int pass = 0; pass < SeparationPasses; pass++)
            {
                for (int i = 0; i < _units.Count; i++)
                {
                    EncounterUnit a = _units[i];
                    if (!a.IsActive)
                    {
                        continue;
                    }

                    for (int j = i + 1; j < _units.Count; j++)
                    {
                        EncounterUnit b = _units[j];
                        if (!b.IsActive)
                        {
                            continue;
                        }

                        float3 da = Up(a.Position);
                        float3 db = Up(b.Position);
                        if (PushApart(ref da, a.CollisionRadiusM, ref db, b.CollisionRadiusM, r))
                        {
                            PlaceIfAllowed(a, da);
                            PlaceIfAllowed(b, db);
                        }
                    }

                    float3 d = Up(a.Position);
                    if (PushApart(ref d, a.CollisionRadiusM, ref playerDir, playerRadius, r))
                    {
                        PlaceIfAllowed(a, d);
                    }
                }
            }

            // Rotating the feet about the centre keeps their altitude; the character's ground probe re-seats it.
            _playerPush = math.all(playerDir == playerStart)
                ? float3.zero
                : _planet.Center + playerDir * math.length(playerPosition - _planet.Center) - playerPosition;
        }

        // atan2(|a×b|, a·b) instead of acos(a·b): body-scale arcs at R ≥ 320 are ~1e-3 rad, where float acos near 1 is off
        // by ~2 cm (dθ ≈ ε/θ); the cross-product form keeps sub-millimetre accuracy.
        private static float ArcM(float3 a, float3 b, float planetRadius)
        {
            return math.atan2(math.length(math.cross(a, b)), math.dot(a, b)) * planetRadius;
        }

        // REQ: combat.md#只在陸地. A push never moves a wild onto the sea, nor any unit into a building; its share of the
        // overlap stays unresolved and later ticks (or the other body's own share) settle it.
        private void PlaceIfAllowed(EncounterUnit u, float3 direction)
        {
            if (u.Side == EncounterSide.Wild && _surface.IsSea(direction))
            {
                return;
            }

            if (EntersBuilding(u, Up(u.Position), direction))
            {
                return;
            }

            Place(u, direction);
        }

        // REQ: planet-world.md#道路與建築. Only entering is refused: a unit that starts inside a footprint (a spawn on a lot,
        // a push from a third body) must be able to walk out, or it would be frozen for the rest of the run.
        private bool EntersBuilding(EncounterUnit u, float3 from, float3 to)
        {
            return _surface.IsBlocked(to, u.CollisionRadiusM) && !_surface.IsBlocked(from, u.CollisionRadiusM);
        }

        private void Place(EncounterUnit u, float3 direction)
        {
            u.Position = GroundPoint(direction, u.BodyRadiusM);
            u.Forward = Tangent(direction, u.Forward);
        }

        // Moves two unit directions apart along their great circle until the arc between them is ra + rb; the larger
        // footprint (radius²) yields less. Coincident directions fall back to a's East (b) and its opposite (a), so the pair
        // still separates deterministically. Returns whether they overlapped.
        private static bool PushApart(ref float3 da, float ra, ref float3 db, float rb, float planetRadius)
        {
            float overlap = ra + rb - ArcM(da, db, planetRadius);
            if (overlap <= 0f)
            {
                return false;
            }

            // Tangent at a pointing to b is n×a, tangent at b pointing to a is −(n×b), with n = a×b; each body moves against
            // its own. Gate on |n| = sin θ rather than on the projection residual b − (a·b)a: for identical non-axial float
            // directions |a| ≠ 1 by ~1e-7, the residual is radial (not tangent) and passed a 1e-7 gate, so both headings
            // pointed the same way and the pair never separated (SOL W-B2-SOL-01). 1e-6 rad ≤ 1.3 mm at R = 1280.
            float3 n = math.cross(da, db);
            float3 headingB;
            float3 headingA;
            if (math.length(n) > 1e-6f)
            {
                headingA = -math.normalize(math.cross(n, da));
                headingB = math.normalize(math.cross(n, db));
            }
            else
            {
                headingB = PlanetFrame.FromUp(da).East;
                headingA = -headingB;
            }

            float wa = ra * ra;
            float wb = rb * rb;
            float turnA = overlap * wb / (wa + wb) / planetRadius;
            float turnB = overlap * wa / (wa + wb) / planetRadius;
            da = math.normalize(da * math.cos(turnA) + headingA * math.sin(turnA));
            db = math.normalize(db * math.cos(turnB) + headingB * math.sin(turnB));
            return true;
        }

        private void TryCast(EncounterUnit u, EncounterUnit partner, int slot, int targetId, long nowMs)
        {
            CastMove move = u.Moves[slot];
            if (u.Combatant.BeginCast(slot, move, nowMs, out CastContext ctx) != CastResult.Accepted)
            {
                return;
            }

            _events.Add(new EncounterEvent(EncounterEventKind.Cast, u.Id, targetId, slot, 0));
            // u.Volumes stays unbounded so range modifiers scale the raw value; the planet cap comes last (Q-D1).
            HitVolume volume = MoveGeometry.ApplyPlanetCap(EffectMath.ApplyRange(u.Volumes[slot], u.RangeClasses[slot],
                ctx.RangePercentSum, _balance.Stats, _balance.Effects), u.RangeClasses[slot], _rangeCapMm);

            if (volume.Kind == HitVolumeKind.Caster)
            {
                Support(u, u, ctx, slot, nowMs);
                return;
            }

            if (volume.Kind == HitVolumeKind.Ally)
            {
                if (partner != null && MoveGeometry.Overlaps(volume, _planet, u.Position, u.Forward, partner.Position,
                        partner.BodyRadiusM))
                {
                    Support(u, partner, ctx, slot, nowMs);
                }

                return;
            }

            float3 anchor = u.Position;
            if (volume.Anchor == HitVolumeAnchor.AimPoint)
            {
                float3 aim = targetId >= 0 ? TargetPosition(targetId) : u.Position + u.Forward * (volume.CastDistanceMm * 0.001f);
                anchor = MoveGeometry.ResolveAnchor(volume, _planet, u.Position, aim);
            }

            if (u.MoveDefs[slot].Shape == MoveShape.Field)
            {
                var timeline = new FieldTimeline(nowMs, u.MoveDefs[slot].FieldDurationMs, _balance.Effects.FieldTickMs);
                var field = new PlacedField(u.Id, u.Side, slot, ctx, volume, anchor, u.Forward, timeline);
                _fields.Add(field);
                // nowMs - 1 makes the placement instant itself the first resolution (FieldTimeline.TicksDue contract).
                int due = timeline.TicksDue(nowMs - 1, nowMs);
                for (int k = 0; k < due; k++)
                {
                    ResolveVolume(u.Id, u.Side, slot, ctx, volume, anchor, u.Forward, nowMs);
                }

                return;
            }

            ResolveVolume(u.Id, u.Side, slot, ctx, volume, anchor, u.Forward, nowMs);

            if (volume.DashDistanceMm > 0)
            {
                // Hits are tested from the start position (the rect covers the whole path), then the caster moves.
                float3 end = MoveGeometry.ResolveDashEnd(volume, _planet, u.Position, u.Forward);
                float3 endDirection = _planet.ToDirection(end);
                // REQ: combat.md#只在陸地. A wild dash ending on the sea keeps the hits (already resolved) but not the
                // displacement, matching the "a step onto the sea does not move" steering rule (SOL W-B2-SOL-02).
                if (u.Side == EncounterSide.Wild && _surface.IsSea(endDirection))
                {
                    return;
                }

                // Same rule for a wall: a dash may not end inside a building it started outside of.
                if (EntersBuilding(u, Up(u.Position), endDirection))
                {
                    return;
                }

                u.Position = GroundPoint(endDirection, u.BodyRadiusM);
                u.Forward = Tangent(Up(u.Position), u.Forward);
            }
        }

        private void ResolveVolume(int casterId, EncounterSide side, int slot, in CastContext ctx, in HitVolume volume,
            float3 anchor, float3 forward, long nowMs)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                EncounterUnit t = _units[i];
                // Any hostile volume hits every active enemy it touches (combat.md#場地 for fields; "被擊中即開戰" for the
                // rest), including a wild unit that has not engaged; the Engaged gate only governs AI target choice
                // (SOL D0-E1-01 for fields, D0-E2 E2-SOL-01 for caster/front volumes).
                bool eligible = t.Side != side && t.IsActive;
                if (!eligible ||
                    !MoveGeometry.Overlaps(volume, _planet, anchor, forward, t.Position, t.BodyRadiusM))
                {
                    continue;
                }

                HitReport r = t.Combatant.ReceiveHostile(ctx, true, ref _rng, nowMs);
                bool hit = r.Outcome == HitOutcome.Hit;
                _events.Add(new EncounterEvent(hit ? EncounterEventKind.Hit : EncounterEventKind.Miss, casterId, t.Id, slot,
                    r.Damage));
                if (r.Fainted)
                {
                    _events.Add(new EncounterEvent(EncounterEventKind.Faint, casterId, t.Id, slot, 0));
                }
                else if (hit && t.Side == EncounterSide.Wild)
                {
                    // Being hit starts the fight even outside its territory (timid only fights back, Q-C35); a MISS does
                    // not, or a timid unit would join from a roll it never felt (E2-SOL-02).
                    t.Engaged = true;
                }
            }

            // The player character resolves last: its id is above every unit id, and targets resolve in id order.
            if (side == EncounterSide.Wild && PlayerTargetable() &&
                MoveGeometry.Overlaps(volume, _planet, anchor, forward, _playerCenter, PlayerBodyRadiusM))
            {
                HitPlayer(casterId, slot, ctx, nowMs);
            }

            ResolveObjects(casterId, ctx, volume, anchor, forward, nowMs);
        }

        // Provisional player defence (Q-C35): evasion 0 and resist 0, so only accuracy and dodge-roll i-frames stand
        // between a move and the fixed player HP; timed effects are not applied (the character has no EffectBook).
        private void HitPlayer(int casterId, int slot, in CastContext ctx, long nowMs)
        {
            CastMove move = ctx.Move;
            int chance = _balance.Stats.HitChancePermille(move.AccuracyPercent, 0);
            HitOutcome outcome = HitResolver.Resolve(true, _player.IsInvulnerable(nowMs), chance, ref _rng);
            int dealt = 0;
            if (outcome == HitOutcome.Hit && move.DealsDamage)
            {
                dealt = _player.ApplyDamage(DamageModel.Compute(move.Power, ctx.MasteryPercent, 0), nowMs);
            }

            // An i-frame dodge (Invulnerable) shows the same MISS feedback as a missed roll.
            _events.Add(new EncounterEvent(outcome == HitOutcome.Hit ? EncounterEventKind.Hit : EncounterEventKind.Miss,
                casterId, PlayerCharacterId, slot, dealt));
            if (_player.IsDowned)
            {
                _events.Add(new EncounterEvent(EncounterEventKind.PlayerDowned, casterId, PlayerCharacterId, slot, 0));
            }
        }

        /// <summary>Copy of the combat stream, for draw-count oracles (SOL E2-SOL-05).</summary>
        /// <remarks>Pre: none. Post: detached copy; drawing from it does not move the encounter's stream. Side effects: none.</remarks>
        internal Pcg32 RngForTests => _rng;

        /// <summary>Copy of the wander stream, for golden-vector oracles (SOL W-B2-SOL-05).</summary>
        /// <remarks>Pre: none. Post: detached copy; drawing from it does not move the encounter's stream. Side effects: none.</remarks>
        internal Pcg32 WanderRngForTests => _wanderRng;

        private bool PlayerTargetable() => _player != null && !_player.IsDowned;

        private float3 TargetPosition(int id) => id == PlayerCharacterId ? _playerCenter
            : id == ObjectTargetId ? _objectAim : _units[id].Position;

        private bool ObjectFocusIntact() => _hasObjectFocus && !_objects.IsDestroyed(_objectFocus.Id);

        // CombatAi measures reach to a zero-radius centre; a building's hit sphere can be wider than every move's reach,
        // so the creature would stand at the wall forever without casting. Presenting the nearest surface point instead
        // makes reach, stand-off and aim count from the object's edge. Facing points into the object, so a flank-style
        // creature circles to the side it is already on rather than into the object.
        private void FillObjectEnemy(EncounterUnit self)
        {
            float3 c = _objectFocus.Center;
            float3 upC = Up(c);
            float3 rel = self.Position - c;
            float3 tangent = rel - math.dot(rel, upC) * upC;
            float h = math.length(tangent);
            _objectAim = h > 1e-4f ? c + tangent * (math.min(_objectFocus.HitRadiusM, h) / h) : c;
            float3 upA = Up(_objectAim);
            float3 inward = -tangent - math.dot(-tangent, upA) * upA;
            float3 forward = math.lengthsq(inward) > 1e-12f ? math.normalize(inward) : PlanetFrame.FromUp(upA).North;
            _enemyBuffer.Clear();
            _enemyBuffer.Add(new AiEnemy(ObjectTargetId, _objectAim, forward, true));
        }

        private void ResolveFields(long previousMs, long nowMs)
        {
            int write = 0;
            for (int i = 0; i < _fields.Count; i++)
            {
                PlacedField f = _fields[i];
                FieldTimeline tl = f.Timeline;
                // previousMs >= PlacedMs here (placement happened in an earlier tick), so (previousMs, nowMs] never
                // re-counts tick 0.
                int due = tl.TicksDue(previousMs, nowMs);
                for (int k = 0; k < due; k++)
                {
                    ResolveVolume(f.CasterId, f.Side, f.Slot, f.Ctx, f.Volume, f.Anchor, f.Forward, nowMs);
                }

                // Kept for its whole life [placed, placed + life), not just until its last resolution (SOL D0-E1-02).
                if (tl.IsAlive(nowMs))
                {
                    _fields[write++] = f;
                }
            }

            _fields.RemoveRange(write, _fields.Count - write);
        }

        private void Support(EncounterUnit caster, EncounterUnit receiver, in CastContext ctx, int slot, long nowMs)
        {
            HitReport r = receiver.Combatant.ReceiveSupport(ctx, nowMs);
            _events.Add(new EncounterEvent(EncounterEventKind.Support, caster.Id, receiver.Id, slot, r.Healed));
            if (r.Fainted)
            {
                _events.Add(new EncounterEvent(EncounterEventKind.Faint, caster.Id, receiver.Id, slot, 0));
            }
        }

        private void UpdateEngagement(float3 playerPosition)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                EncounterUnit w = _units[i];
                if (w.Side != EncounterSide.Wild || !w.IsActive)
                {
                    continue;
                }

                float nearest = math.distance(w.Position, playerPosition);
                for (int j = 0; j < _units.Count; j++)
                {
                    EncounterUnit p = _units[j];
                    if (p.Side == EncounterSide.Player && p.IsActive)
                    {
                        nearest = math.min(nearest, math.distance(w.Position, p.Position));
                    }
                }

                // Chord distance is fine at these ranges: chord/arc ≈ 1 − θ²/24, so ≤ 40 m on R ≥ 320 is short by < 0.07 % (26 mm;
                // SOL Q-D1 B). A threshold crossing that close is not a gameplay distinction.
                // REQ: combat.md#野生領域 (D0-E2) — a 0 m territory (timid) never starts a fight by proximity.
                int territoryMm = _balance.Territory.RadiusMm(w.Def.Temperament);
                w.Engaged = w.Engaged
                    ? nearest <= DisengageRadiusM
                    : w.Provoked || (territoryMm > 0 && nearest <= territoryMm * 0.001f);
                w.Provoked = false;
                if (w.Engaged)
                {
                    // Re-decide from scratch after the fight: the old leg may now be far behind (combat.md#遊蕩).
                    w.HasWanderTarget = false;
                    w.WanderResumeMs = WanderUnscheduled;
                }
            }
        }

        // REQ: combat.md#遊蕩 (D0-W1). Engagement for this tick was already decided from the pre-move positions, so a
        // wander step can only bring a unit into someone's territory on the next tick — same as any other movement.
        private void Wander(EncounterUnit u, long nowMs, int dtMs)
        {
            WanderTuning t = _balance.Wander;
            float radiusM = t.RadiusMm(u.Def.Temperament) * 0.001f;
            if (!u.HasWanderTarget)
            {
                if (u.WanderResumeMs == WanderUnscheduled)
                {
                    float fromHome = math.distance(u.Position, u.Home);
                    if (fromHome <= radiusM + ArriveM)
                    {
                        u.WanderResumeMs = nowMs + NextWanderPause(t);
                        return;
                    }

                    u.WanderTarget = Up(u.Home);
                    u.HasWanderTarget = true;
                }
                else if (nowMs < u.WanderResumeMs)
                {
                    return;
                }
                else
                {
                    if (!TryLandWanderPoint(u.Home, radiusM, u.CollisionRadiusM, out float3 target))
                    {
                        u.WanderResumeMs = nowMs + NextWanderPause(t);
                        return;
                    }

                    u.WanderTarget = target;
                    u.HasWanderTarget = true;
                }
            }

            // A blocked step (sea ahead) ends the leg like an arrival, so the unit pauses and draws a new destination.
            if (Steer(u, u.WanderTarget, nowMs, dtMs, t.SpeedPermille) != SteerResult.Moving)
            {
                u.HasWanderTarget = false;
                u.WanderResumeMs = nowMs + NextWanderPause(t);
            }
        }

        private int NextWanderPause(WanderTuning t)
        {
            return t.PauseMinMs + (int)_wanderRng.NextUInt((uint)(t.PauseMaxMs - t.PauseMinMs + 1));
        }

        /// <summary>Destination draws per wander leg before giving up on a disk that is (almost) all sea.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public const int WanderSeaRetries = 8;

        // REQ: combat.md#只在陸地. Redraws come from the same (seed,'W') stream, so the count of draws is still a pure
        // function of seed and positions; a coast-hugging spawn just pauses again instead of walking into the water.
        // A destination inside a building is redrawn the same way (planet-world.md#道路與建築).
        private bool TryLandWanderPoint(float3 home, float radiusM, float clearanceM, out float3 target)
        {
            for (int i = 0; i < WanderSeaRetries; i++)
            {
                target = WanderPoint(home, radiusM);
                if (!_surface.IsSea(target) && !_surface.IsBlocked(target, clearanceM))
                {
                    return true;
                }
            }

            target = default;
            return false;
        }

        // Uniform over the disk (sqrt of the radial draw); the disk is laid on the sphere by arc length, which differs from
        // the flat disk's area by ≈ θ²/12: < 0.012 % for the ≤ 12 m wander radii at R ≥ 320 (SOL Q-D1 B).
        private float3 WanderPoint(float3 home, float radiusM)
        {
            float3 up = Up(home);
            float theta = _wanderRng.NextFloat() * 2f * math.PI;
            float arc = radiusM * math.sqrt(_wanderRng.NextFloat()) / _planet.Radius;
            PlanetFrame f = PlanetFrame.FromUp(up);
            float3 heading = f.East * math.cos(theta) + f.North * math.sin(theta);
            return math.normalize(up * math.cos(arc) + heading * math.sin(arc));
        }

        private bool IsValidEnemy(EncounterUnit self, int id) => IsValidEnemy(self.Side, id);

        private bool IsValidEnemy(EncounterSide side, int id)
        {
            if (id == PlayerCharacterId)
            {
                return side == EncounterSide.Wild && PlayerTargetable();
            }

            if (id < 0 || id >= _units.Count)
            {
                return false;
            }

            EncounterUnit t = _units[id];
            if (t.Side == side || !t.IsActive)
            {
                return false;
            }

            return t.Side == EncounterSide.Player || t.Engaged;
        }

        private void FillEnemies(EncounterUnit self)
        {
            _enemyBuffer.Clear();
            for (int i = 0; i < _units.Count; i++)
            {
                if (IsValidEnemy(self, i))
                {
                    EncounterUnit t = _units[i];
                    _enemyBuffer.Add(new AiEnemy(t.Id, t.Position, t.Forward, true));
                }
            }

            if (IsValidEnemy(self, PlayerCharacterId))
            {
                // The rule layer has no character facing; North is a deterministic stand-in that only affects where a
                // flank-style creature circles to.
                _enemyBuffer.Add(new AiEnemy(PlayerCharacterId, _playerCenter, PlanetFrame.FromUp(Up(_playerCenter)).North,
                    true));
            }
        }

        private void FillOptions(EncounterUnit u, long nowMs)
        {
            _optionBuffer.Clear();
            long rangePercent = u.Combatant.RangePercent(nowMs);
            for (int s = 0; s < u.Moves.Count; s++)
            {
                // The AI must judge reach on what the cast will actually use — same range modifiers, then the planet cap,
                // in TryCast's order — or it fires from beyond reach (SOL Q-D1 B, Q-D1-IMPL-01).
                HitVolume v = MoveGeometry.ApplyPlanetCap(EffectMath.ApplyRange(u.Volumes[s], u.RangeClasses[s],
                    rangePercent, _balance.Stats, _balance.Effects), u.RangeClasses[s], _rangeCapMm);
                AiMoveTargeting targeting = v.Kind == HitVolumeKind.Caster ? AiMoveTargeting.Self
                    : v.Kind == HitVolumeKind.Ally ? AiMoveTargeting.Ally : AiMoveTargeting.Hostile;
                int reach = targeting == AiMoveTargeting.Self ? 0 : CombatAi.ReachMmOf(v);
                _optionBuffer.Add(new AiMoveOption(s, targeting, reach, u.Moves[s].Power,
                    u.Combatant.Cooldowns.IsReady(s, nowMs)));
            }
        }

        private EncounterUnit PartnerOf(EncounterUnit u)
        {
            if (u.Side != EncounterSide.Player)
            {
                return null;
            }

            for (int i = 0; i < _units.Count; i++)
            {
                EncounterUnit p = _units[i];
                if (p != u && p.Side == EncounterSide.Player && p.IsActive)
                {
                    return p;
                }
            }

            return null;
        }

        private enum SteerResult
        {
            Moving,
            Arrived,
            Blocked,
        }

        // Arrived when the unit is (already or now) within ArriveM of the destination; Blocked when a wild's next step
        // would land on the sea (combat.md#只在陸地), or every slide around a wall is walled too, in which case it does not
        // move this tick.
        private SteerResult Steer(EncounterUnit u, float3 destinationDirection, long nowMs, int dtMs, int speedPermille)
        {
            float3 up = Up(u.Position);
            float3 dest = math.normalize(destinationDirection);
            float angle = math.acos(math.clamp(math.dot(up, dest), -1f, 1f));
            float r = _planet.Radius;
            if (angle * r <= ArriveM)
            {
                return SteerResult.Arrived;
            }

            // REQ: combat.md#效果疊加 (tech-debt D0-E3): move_speed_mod_percent scales both combat and wander steps.
            float speedMs = u.Combatant.MoveSpeedMmS(nowMs) * 0.001f * speedPermille / 1000f;
            float step = math.min(angle, speedMs * dtMs * 0.001f / r);
            float3 heading = Tangent(up, dest - up);
            float3 next = math.normalize(up * math.cos(step) + heading * math.sin(step));
            if (u.Side == EncounterSide.Wild && _surface.IsSea(next))
            {
                return SteerResult.Blocked;
            }

            if (EntersBuilding(u, up, next))
            {
                if (!TrySlide(u, up, step, ref heading, ref next))
                {
                    return SteerResult.Blocked;
                }

                u.Position = GroundPoint(next, u.BodyRadiusM);
                u.Forward = Tangent(next, heading);
                // A slide does not head at the destination, so measure what is actually left.
                float left = math.acos(math.clamp(math.dot(next, dest), -1f, 1f));
                return left * r <= ArriveM ? SteerResult.Arrived : SteerResult.Moving;
            }

            u.Position = GroundPoint(next, u.BodyRadiusM);
            u.Forward = Tangent(next, heading);
            return (angle - step) * r <= ArriveM ? SteerResult.Arrived : SteerResult.Moving;
        }

        // cos/sin of the slide turns on one side: 45° keeps most of the progress along a wall met at an angle; 90° follows
        // a wall met head-on; 135° is needed once the target is past the wall's middle, where the heading tilts back toward
        // the wall and a 90° turn on the sticky side still points into it (without it the unit flipped sides each tick).
        private static readonly float2[] SlideTurns =
        {
            new float2(0.70710678f, 0.70710678f), new float2(0f, 1f), new float2(-0.70710678f, 0.70710678f),
        };

        // REQ: planet-world.md#道路與建築. Sliding (rather than stopping) keeps a chasing creature from pinning itself on
        // the near wall of a house the character walked around. The unit's last slide side is tried first, then the other;
        // sea rules still apply to every turned step.
        private bool TrySlide(EncounterUnit u, float3 up, float step, ref float3 heading, ref float3 next)
        {
            float3 left = math.cross(up, heading);
            for (int k = 0; k < 2; k++)
            {
                int sideSign = k == 0 ? u.SlideSide : -u.SlideSide;
                foreach (float2 turn in SlideTurns)
                {
                    float3 h = heading * turn.x + left * (turn.y * sideSign);
                    float3 n = math.normalize(up * math.cos(step) + h * math.sin(step));
                    if (u.Side == EncounterSide.Wild && _surface.IsSea(n))
                    {
                        continue;
                    }

                    if (!EntersBuilding(u, up, n))
                    {
                        heading = h;
                        next = n;
                        u.SlideSide = sideSign;
                        return true;
                    }
                }
            }

            return false;
        }

        private float3 GroundPoint(float3 direction, float bodyRadius)
        {
            return _planet.ToWorld(direction, _surface.GroundAltitude(direction) + bodyRadius);
        }

        private float3 Up(float3 position) => _planet.ToDirection(position);

        // REQ: tech-debt D0-E4 · docs/reviews/D0-E4-proposal-sol.md. In float, a target almost straight above/below left a
        // ~4e-6 residue after removing the radial part; normalising it gave a forward 0.0135 off the tangent plane and
        // tripped MoveGeometry's unit-tangent Pre. Promoting before the subtraction keeps the projection exact enough, and
        // a projected offset below the float position uncertainty (tau) is treated as "no bearing" (keep the heading).
        internal float3 TangentToward(float3 from, float3 to, float3 fallback)
        {
            double3 offset = (double3)from - (double3)_planet.Center;
            double radius = math.length(offset);
            double3 up = offset / radius;
            double3 delta = (double3)to - (double3)from;
            double3 tangent = math.cross(math.cross(up, delta), up);
            double length = math.length(tangent);

            const double FloatUnitRoundoff = 5.9604644775390625e-8; // 2^-24
            double scale = math.max(1.0, math.max(radius, math.max(math.cmax(math.abs((double3)from)),
                math.max(math.cmax(math.abs((double3)to)), math.cmax(math.abs((double3)_planet.Center))))));
            // 16·u·S bounds per-axis coordinate rounding with margin; (1 + |d|/|r|) adds the error of the up estimate
            // itself, which the projection scales by |d| (derivation in the proposal).
            double tau = 16.0 * FloatUnitRoundoff * scale * (1.0 + math.length(delta) / radius);
            float3 candidate = length > tau ? (float3)(tangent / length) : fallback;
            // ProjectOnTangent re-orthogonalises against the float up and maps a zero/radial fallback to North.
            return PlanetFrame.FromUp((float3)up).ProjectOnTangent(candidate);
        }

        private static float3 Tangent(float3 up, float3 v) => Tangent(up, v, v);

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

        private EncounterUnit Get(int unitId)
        {
            Contract.Requires(unitId >= 0 && unitId < _units.Count, "unknown unit id");
            return _units[unitId];
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
