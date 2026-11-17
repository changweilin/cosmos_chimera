using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.WorldGen;

namespace Cosmimera.Combat
{
    /// <summary>A move as the cast pipeline sees it; mirrors <see cref="MoveDef"/> but is constructible in tests.</summary>
    /// <remarks>
    /// Geometry (shape, range, dash) is deliberately absent: contact is decided by <see cref="MoveGeometry"/> and
    /// passed in as a bool, so this layer stays free of planet math.
    /// Pre: build through the constructor or <see cref="FromDef"/>. Post: immutable. Side effects: none.
    /// </remarks>
    public readonly struct CastMove
    {
        /// <summary>Creates a validated move.</summary>
        /// <remarks>
        /// Pre: id non-empty; element and kind defined; 0 &lt;= power &lt;= DamageModel.MaxPower (ignored when kind == Effect);
        ///      0 &lt;= accuracyPercent &lt;= 100; baseCooldownMs >= 0; hasEffect iff kind != Damage, and then effect was built
        ///      by the EffectSpec constructor (MaxStacks >= 1);
        ///      when hasEffect and !effect.IsInstant: 1 &lt;= effectDurationMs &lt;= EffectBook.MaxDurationMs, else effectDurationMs == 0.
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public CastMove(string id, Element element, MoveKind kind, int power, int accuracyPercent, int baseCooldownMs,
            bool hasEffect, in EffectSpec effect, int effectDurationMs, Polarity polarity = Polarity.Yin)
        {
            Contract.Requires(polarity == Polarity.Yin || polarity == Polarity.Yang, "undefined Polarity");
            Polarity = polarity;
            Contract.Requires(!string.IsNullOrEmpty(id), "id must be non-empty");
            Contract.Requires(element >= Element.Metal && element <= Element.Shadow, "undefined Element");
            Contract.Requires(kind >= MoveKind.Damage && kind <= MoveKind.Both, "undefined MoveKind");
            Contract.Requires(power >= 0 && power <= DamageModel.MaxPower, "power must be 0..MaxPower");
            Contract.Requires(accuracyPercent >= 0 && accuracyPercent <= 100, "accuracyPercent must be 0..100");
            Contract.Requires(baseCooldownMs >= 0, "baseCooldownMs must be nonnegative");
            Contract.Requires(hasEffect == (kind != MoveKind.Damage), "hasEffect iff kind != Damage");
            if (hasEffect)
            {
                Contract.Requires(effect.MaxStacks >= 1, "effect must come from the EffectSpec constructor");
            }

            if (hasEffect && !effect.IsInstant)
            {
                Contract.Requires(effectDurationMs >= 1 && effectDurationMs <= EffectBook.MaxDurationMs,
                    "a timed effect needs 1..MaxDurationMs");
            }
            else
            {
                Contract.Requires(effectDurationMs == 0, "only a timed effect has a duration");
            }

            Id = id;
            Element = element;
            Kind = kind;
            Power = power;
            AccuracyPercent = accuracyPercent;
            BaseCooldownMs = baseCooldownMs;
            HasEffect = hasEffect;
            Effect = effect;
            EffectDurationMs = effectDurationMs;
        }

        /// <summary>Move id; also the stacking source id of its effect (combat.md#效果疊加).</summary>
        /// <remarks>Pre: none. Post: non-empty, except null on default(CastMove). Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Move element.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public Element Element { get; }

        /// <summary>Damage polarity: selects 陰攻/陽攻 and 陰守/陽守 (combat.md#傷害公式).</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public Polarity Polarity { get; }

        /// <summary>Damage, effect or both.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public MoveKind Kind { get; }

        /// <summary>Damage power; ignored when Kind == Effect (kept as given, never applied).</summary>
        /// <remarks>Pre: none. Post: 0..DamageModel.MaxPower for every kind. Side effects: none.</remarks>
        public int Power { get; }

        /// <summary>Move accuracy percent used by the roll.</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int AccuracyPercent { get; }

        /// <summary>Cooldown before SPI and cd_mod_percent.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int BaseCooldownMs { get; }

        /// <summary>Whether the move carries an effect.</summary>
        /// <remarks>Pre: none. Post: true iff Kind != Damage. Side effects: none.</remarks>
        public bool HasEffect { get; }

        /// <summary>The effect; meaningful only when <see cref="HasEffect"/>.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public EffectSpec Effect { get; }

        /// <summary>Window of a timed effect on each target; 0 for a heal or no effect.</summary>
        /// <remarks>Pre: none. Post: 0..EffectBook.MaxDurationMs. Side effects: none.</remarks>
        public int EffectDurationMs { get; }

        /// <summary>Whether the move deals damage.</summary>
        /// <remarks>Pre: none. Post: true iff Kind != Effect. Side effects: none.</remarks>
        public bool DealsDamage => Kind != MoveKind.Effect;

        /// <summary>Converts an imported definition.</summary>
        /// <remarks>Pre: def != null and importer-validated. Post: same values as def. Side effects: none.</remarks>
        public static CastMove FromDef(MoveDef def)
        {
            return FromDef(def, false);
        }

        /// <summary>Converts an imported definition, using its ★ version when <paramref name="downgraded"/>.</summary>
        /// <remarks>
        /// REQ: creatures.md#技能星級 — a skill provided by a partner always works at ★.
        /// Pre: def != null and importer-validated. Post: def's values, or its stored ★ values where present when
        ///      downgraded (identical to FromDef(def) for a ★ move). Side effects: none.
        /// </remarks>
        public static CastMove FromDef(MoveDef def, bool downgraded)
        {
            Contract.Requires(def != null, "def must not be null");
            EffectDef effectDef = def.EffectFor(downgraded);
            bool hasEffect = effectDef != null;
            EffectSpec effect = hasEffect ? EffectSpec.FromDef(effectDef) : default;
            int duration = def.HasEffectDuration ? def.EffectDurationMsFor(downgraded) : 0;
            return new CastMove(def.Id, def.Element, def.Kind, def.PowerFor(downgraded), def.AccuracyFor(downgraded),
                def.CooldownMsFor(downgraded), hasEffect, effect, duration, def.Polarity);
        }
    }

    /// <summary>Per-slot move cooldowns of one combatant.</summary>
    /// <remarks>
    /// REQ: docs/design/combat.md#CD; user decision 2026-11-07: a cooldown starts at the instant the cast is accepted.
    /// Time watermark: every call taking nowMs requires nowMs >= the latest nowMs passed to <see cref="Start"/> or <see cref="Reset"/>.
    /// Invariant: ReadyAtMs(slot) &lt;= CombatTime.MaxMs + int.MaxValue for every slot (no Int64 wrap).
    /// </remarks>
    public sealed class MoveCooldowns
    {
        /// <summary>Most move slots a combatant may have (signature + partner moves, with headroom).</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxSlots = 8;

        private readonly long[] _readyAt;
        private long _watermarkMs;

        /// <summary>Creates <paramref name="slotCount"/> slots, all ready.</summary>
        /// <remarks>
        /// Pre: 1 &lt;= slotCount &lt;= MaxSlots. Post: SlotCount == slotCount; every slot ready at 0.
        /// Side effects: allocates the slot array (construction only, never per frame).
        /// </remarks>
        public MoveCooldowns(int slotCount)
        {
            Contract.Requires(slotCount >= 1 && slotCount <= MaxSlots, "slotCount must be 1..MaxSlots");
            _readyAt = new long[slotCount];
        }

        /// <summary>Number of slots.</summary>
        /// <remarks>Pre: none. Post: 1..MaxSlots. Side effects: none.</remarks>
        public int SlotCount => _readyAt.Length;

        /// <summary>Earliest time the slot can be cast again.</summary>
        /// <remarks>Pre: 0 &lt;= slot &lt; SlotCount. Post: >= 0. Side effects: none.</remarks>
        public long ReadyAtMs(int slot)
        {
            RequireSlot(slot);
            return _readyAt[slot];
        }

        /// <summary>Whether the slot can be cast at <paramref name="nowMs"/>.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= slot &lt; SlotCount; nowMs in CombatTime and >= watermark. Post: true iff nowMs >= ReadyAtMs(slot)
        /// (half-open cooldown [start, start + cd)). Side effects: none.
        /// </remarks>
        public bool IsReady(int slot, long nowMs)
        {
            RequireSlot(slot);
            CombatTime.Require(nowMs, _watermarkMs);
            return nowMs >= _readyAt[slot];
        }

        /// <summary>Starts the slot's cooldown at <paramref name="nowMs"/>.</summary>
        /// <remarks>
        /// Pre: IsReady(slot, nowMs); cooldownMs >= 0. Post: ReadyAtMs(slot) == nowMs + cooldownMs.
        /// Side effects: records the watermark.
        /// </remarks>
        public void Start(int slot, long nowMs, int cooldownMs)
        {
            Contract.Requires(IsReady(slot, nowMs), "slot is still cooling down");
            Contract.Requires(cooldownMs >= 0, "cooldownMs must be nonnegative");
            _watermarkMs = nowMs;
            _readyAt[slot] = nowMs + cooldownMs;
        }

        /// <summary>Gives back a cooldown started by a cast that never resolved (combat.md#前搖: interrupted windup).</summary>
        /// <remarks>
        /// Pre: 0 &lt;= slot &lt; SlotCount; nowMs in CombatTime and >= watermark.
        /// Post: IsReady(slot, nowMs); a slot already ready is unchanged. Side effects: records the watermark.
        /// </remarks>
        public void Refund(int slot, long nowMs)
        {
            RequireSlot(slot);
            CombatTime.Require(nowMs, _watermarkMs);
            _watermarkMs = nowMs;
            if (_readyAt[slot] > nowMs)
            {
                _readyAt[slot] = nowMs;
            }
        }

        /// <summary>Makes every slot ready (start of a new combat).</summary>
        /// <remarks>
        /// Pre: nowMs in CombatTime and >= watermark. Post: every ReadyAtMs == 0. Side effects: records the watermark.
        /// </remarks>
        public void Reset(long nowMs)
        {
            CombatTime.Require(nowMs, _watermarkMs);
            _watermarkMs = nowMs;
            for (int i = 0; i < _readyAt.Length; i++)
            {
                _readyAt[i] = 0;
            }
        }

        private void RequireSlot(int slot)
        {
            Contract.Requires(slot >= 0 && slot < _readyAt.Length, "slot out of range");
        }
    }

    /// <summary>Why <see cref="Combatant.BeginCast"/> refused, or that it accepted.</summary>
    /// <remarks>Pre: none. Post: values are stable (part of the replay/net contract). Side effects: none.</remarks>
    public enum CastResult
    {
        /// <summary>The cast went out; its cooldown started.</summary>
        Accepted = 0,
        /// <summary>The slot is still cooling down; nothing changed.</summary>
        OnCooldown = 1,
        /// <summary>The caster has 0 HP; nothing changed.</summary>
        Fainted = 2,
    }

    /// <summary>A cast frozen at the moment it went out: what every target resolves against.</summary>
    /// <remarks>
    /// The caster's modifiers are sampled once, after on_move_used fired (user decision 2026-11-07: the triggering
    /// move benefits), so every target and every field tick of this cast sees the same mastery even if the caster's
    /// buffs expire mid-flight.
    /// Pre: obtained from <see cref="Combatant.BeginCast"/>. Post: immutable. Side effects: none.
    /// </remarks>
    public readonly struct CastContext
    {
        internal CastContext(in CastMove move, int masteryPercent, long rangePercentSum, int cooldownMs, long castAtMs,
            int powerDeltaPercent = 0, int weaponPowerPermille = 1000, int accuracyDelta = 0, int critRatePermille = 0,
            int critDamagePercent = 100, int attack = 0)
        {
            Attack = attack;
            AccuracyDelta = accuracyDelta;
            CritRatePermille = critRatePermille;
            CritDamageOffsetPercent = critDamagePercent - 100;
            // Stored as an offset so default(CastContext) stays a neutral ×1.
            WeaponPowerOffsetPermille = weaponPowerPermille - 1000;
            Move = move;
            MasteryPercent = masteryPercent;
            RangePercentSum = rangePercentSum;
            CooldownMs = cooldownMs;
            CastAtMs = castAtMs;
            PowerDeltaPercent = powerDeltaPercent;
        }

        /// <summary>The move cast.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public CastMove Move { get; }

        /// <summary>Caster's effective mastery in the move element.</summary>
        /// <remarks>Pre: none. Post: 0..DamageModel.MaxMasteryPercent. Side effects: none.</remarks>
        public int MasteryPercent { get; }

        /// <summary>Caster's range_mod_percent sum; feed to <see cref="EffectMath.ApplyRange"/> with the built volume.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public long RangePercentSum { get; }

        /// <summary>Effective cooldown that was started.</summary>
        /// <remarks>Pre: none. Post: >= CdFloorMs for an Accepted cast; 0 on default(CastContext). Side effects: none.</remarks>
        public int CooldownMs { get; }

        /// <summary>Time the cast went out.</summary>
        /// <remarks>Pre: none. Post: in CombatTime. Side effects: none.</remarks>
        public long CastAtMs { get; }

        /// <summary>Weather percentage modifier on move power frozen at cast time.</summary>
        /// <remarks>Pre: none. Post: in [-90, 100]. Side effects: none.</remarks>
        public int PowerDeltaPercent { get; }

        private int WeaponPowerOffsetPermille { get; }

        /// <summary>Caster's attack for the move's polarity (陰攻 or 陽攻) frozen at cast time (combat.md#傷害公式).</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Attack { get; }

        /// <summary>Caster's accuracy_mod sum frozen at cast time, percentage points.</summary>
        /// <remarks>Pre: none. Post: none (0 on default). Side effects: none.</remarks>
        public int AccuracyDelta { get; }

        /// <summary>Move accuracy plus <see cref="AccuracyDelta"/>, clamped for the hit roll.</summary>
        /// <remarks>Pre: none. Post: 0..DerivedStats.MaxAccuracyPercent. Side effects: none.</remarks>
        public int EffectiveAccuracyPercent
        {
            get
            {
                long a = (long)Move.AccuracyPercent + AccuracyDelta;
                return a < 0 ? 0 : a > DerivedStats.MaxAccuracyPercent ? DerivedStats.MaxAccuracyPercent : (int)a;
            }
        }

        /// <summary>Caster's crit chance frozen at cast time (combat.md#暴擊), permille.</summary>
        /// <remarks>Pre: none. Post: 0..1000 (0 on default: no crit). Side effects: none.</remarks>
        public int CritRatePermille { get; }

        // Offset so default(CastContext) is a neutral ×1.
        private int CritDamageOffsetPercent { get; }

        /// <summary>Caster's crit multiplier frozen at cast time, percent.</summary>
        /// <remarks>Pre: none. Post: 100..DamageModel.MaxCritDamagePercent (100 on default). Side effects: none.</remarks>
        public int CritDamagePercent => 100 + CritDamageOffsetPercent;

        /// <summary>Weapon multiplier on move power frozen at cast time, permille (1000 when unarmed).</summary>
        /// <remarks>Pre: none. Post: 1..DamageModel.MaxWeaponPermille for an Accepted cast. Side effects: none.</remarks>
        public int WeaponPowerPermille => 1000 + WeaponPowerOffsetPermille;

        /// <summary>Move power after the frozen weather delta and weapon multiplier.</summary>
        /// <remarks>Pre: obtained from an Accepted BeginCast. Post: as DamageModel.ScalePower(power, delta, weapon). Side effects: none.</remarks>
        public int EffectivePower => DamageModel.ScalePower(Move.Power, PowerDeltaPercent, WeaponPowerPermille);
    }

    /// <summary>What one move did to one combatant.</summary>
    /// <remarks>Pre: obtained from Combatant.ReceiveHostile / ReceiveSupport (default is an empty report). Post: immutable. Side effects: none.</remarks>
    public readonly struct HitReport
    {
        internal HitReport(HitOutcome outcome, int damage, int healed, int hpBefore, int hpAfter, bool effectApplied)
            : this(outcome, damage, healed, hpBefore, hpAfter, effectApplied, damage)
        {
        }

        internal HitReport(HitOutcome outcome, int damage, int healed, int hpBefore, int hpAfter, bool effectApplied,
            int preBlockDamage, bool critical = false)
        {
            Critical = critical;
            PreBlockDamage = preBlockDamage;
            Outcome = outcome;
            Damage = damage;
            Healed = healed;
            HpBefore = hpBefore;
            HpAfter = hpAfter;
            EffectApplied = effectApplied;
        }

        /// <summary>Contact / roll result; Miss must get explicit feedback (combat.md#命中與閃避).</summary>
        /// <remarks>
        /// Pre: none. Post: defined; Invulnerable only when the caller passed invulnerable (a rolling creature).
        /// Side effects: none.
        /// </remarks>
        public HitOutcome Outcome { get; }

        /// <summary>Formula damage (with any taken bonus) before a block reduced it; feeds the stance gauge.</summary>
        /// <remarks>
        /// Pre: none. Post: 0 unless Outcome == Hit; >= Damage whenever the hit did not faint the target. Not clamped to HP.
        /// Side effects: none.
        /// </remarks>
        public int PreBlockDamage { get; }

        /// <summary>HP removed by the move's damage.</summary>
        /// <remarks>Pre: none. Post: 0 unless Outcome == Hit; &lt;= HpBefore. Side effects: none.</remarks>
        public int Damage { get; }

        /// <summary>HP restored afterwards (move heal or trait heals).</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Healed { get; }

        /// <summary>HP before the move landed.</summary>
        /// <remarks>Pre: none. Post: >= 1 (fainted combatants are never targets), except 0 on default(HitReport). Side effects: none.</remarks>
        public int HpBefore { get; }

        /// <summary>HP after damage, trait triggers and heals.</summary>
        /// <remarks>Pre: none. Post: == HpBefore − Damage + Healed. Side effects: none.</remarks>
        public int HpAfter { get; }

        /// <summary>Whether the move's timed effect was applied to this combatant.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool EffectApplied { get; }

        /// <summary>Whether the damage was a critical hit (combat.md#暴擊).</summary>
        /// <remarks>Pre: none. Post: false unless Outcome == Hit and the move deals damage. Side effects: none.</remarks>
        public bool Critical { get; }

        /// <summary>Whether this move took the combatant to 0 HP.</summary>
        /// <remarks>Pre: none. Post: true iff HpBefore >= 1 and HpAfter == 0. Side effects: none.</remarks>
        public bool Fainted => HpBefore >= 1 && HpAfter == 0;
    }

    /// <summary>
    /// One creature in combat: HP, move cooldowns, trait/effect book. Resolves casts it makes and moves that land on
    /// it, in a fixed order every peer replays identically.
    /// REQ: docs/design/combat.md#傷害公式, #命中與閃避, #CD, #效果疊加, #特性觸發.
    /// </summary>
    /// <remarks>
    /// Order (user decisions 2026-11-07):
    ///   cast  — refuse if fainted or cooling down → on_move_used fires (its buffs apply to this cast) → sample
    ///           modifiers → start cooldown at the cast instant.
    ///   hit   — sample target modifiers → roll (only on contact) → damage → on_hp_cross_below / on_hit_taken fire
    ///           (their buffs apply from the next hit, not this one) → heals.
    ///   0 HP  — a fainted combatant fires no trait events and takes no effects, so no trait or move revives it.
    /// Heals from trait events go through the same HP path and are reported back via NotifyHpChanged too, keeping
    /// the "notify after every HP change" contract of <see cref="EffectBook.NotifyHpChanged"/>.
    /// Invariant: 0 &lt;= Hp &lt;= MaxHp.
    /// </remarks>
    public sealed class Combatant
    {
        private const int ElementCount = 10;

        private readonly DerivedStats _stats;
        private readonly EffectRulesConfig _rules;
        private CreatureStats _core;
        private readonly int[] _mastery = new int[ElementCount];
        private readonly int[] _resist = new int[ElementCount];
        private readonly ModifierTotals _totals = new ModifierTotals();
        private int _hpMultiplier = 1;
        private int _preOverloadMaxHp;
        private CreatureStats _preOverloadCore;
        private int[] _preOverloadMastery;
        private int[] _preOverloadResist;
        private bool _hasOverloadFormStats;
        private WeaponLoadout _weapons;
        private long _acceptedCasts;

        /// <summary>Creates a combatant at full HP with all moves ready.</summary>
        /// <remarks>
        /// Pre: stats, rules, book non-null; every core stat >= 0 (core is already level-scaled); masteryByElement and
        ///      resistByElement have exactly 10 entries indexed by <see cref="Element"/>; mastery
        ///      0..DamageModel.MaxMasteryPercent; resist −ResistClamp..ResistClamp; 1 &lt;= moveSlots &lt;= MoveCooldowns.MaxSlots.
        /// Post: Hp == MaxHp == stats.MaxHpFromPoints(core.Hp); Core == core; Book == book; every slot ready.
        /// Side effects: copies the element tables; allocates per-combatant buffers (construction only).
        /// </remarks>
        public Combatant(DerivedStats stats, EffectRulesConfig rules, in CreatureStats core,
            IReadOnlyList<int> masteryByElement, IReadOnlyList<int> resistByElement, EffectBook book, int moveSlots)
        {
            Contract.Requires(stats != null && rules != null && book != null, "stats, rules and book must not be null");
            RequireCore(core);
            Contract.Requires(masteryByElement != null && masteryByElement.Count == ElementCount,
                "masteryByElement needs one entry per Element");
            Contract.Requires(resistByElement != null && resistByElement.Count == ElementCount,
                "resistByElement needs one entry per Element");
            for (int i = 0; i < ElementCount; i++)
            {
                Contract.Requires(masteryByElement[i] >= 0 && masteryByElement[i] <= DamageModel.MaxMasteryPercent,
                    "mastery must be 0..MaxMasteryPercent");
                Contract.Requires(resistByElement[i] >= -DamageModel.ResistClamp && resistByElement[i] <= DamageModel.ResistClamp,
                    "resist must be -100..100");
                _mastery[i] = masteryByElement[i];
                _resist[i] = resistByElement[i];
            }

            _stats = stats;
            _rules = rules;
            _core = core;
            Book = book;
            MaxHp = stats.MaxHpFromPoints(core.Hp);
            Hp = MaxHp;
            Cooldowns = new MoveCooldowns(moveSlots);
        }

        /// <summary>Max HP from the HP stat (× the boss multiplier).</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int MaxHp { get; private set; }

        /// <summary>Current (level-scaled; overload form while overloaded) six core stats.</summary>
        /// <remarks>Pre: none. Post: every value >= 0. Side effects: none.</remarks>
        public CreatureStats Core => _core;

        private static void RequireCore(in CreatureStats core)
        {
            Contract.Requires(core.Hp >= 0 && core.Speed >= 0 && core.YinAtk >= 0 && core.YangAtk >= 0 && core.YinDef >= 0
                && core.YangDef >= 0, "stats must be nonnegative");
        }

        /// <summary>
        /// Re-derives the stat-driven values after a level-up between battles (Q-C31, 2026-11-09: growth applies when
        /// the battle ends). The damage already taken is kept: Hp rises by exactly the MaxHp increase.
        /// </summary>
        /// <remarks>
        /// Pre: !IsFainted (a fainted creature earns no XP); every core stat >= 0; MaxHpFromPoints(core.Hp) × HP multiplier
        ///      >= MaxHp (levels never go down, and level scaling is monotone).
        /// Post: MaxHp == stats.MaxHpFromPoints(core.Hp) × the multiplier set by <see cref="MultiplyMaxHp"/> (1 if never set);
        ///       Hp == old Hp + (new MaxHp − old MaxHp); Core == core from the next query; cooldowns, effects and
        ///       mastery/resist unchanged.
        /// Side effects: mutates this combatant.
        /// </remarks>
        internal void Rescale(in CreatureStats core)
        {
            Contract.Requires(!IsFainted, "a fainted combatant cannot level up");
            RequireCore(core);
            int maxHp = (int)System.Math.Min(int.MaxValue, (long)_stats.MaxHpFromPoints(core.Hp) * _hpMultiplier);
            Contract.Requires(maxHp >= MaxHp, "a level-up never lowers MaxHp");
            Hp += maxHp - MaxHp;
            MaxHp = maxHp;
            _core = core;
        }

        /// <summary>Multiplies MaxHp for this combatant's whole life (boss fights: HP ×3, Q-C48) and refills HP.</summary>
        /// <remarks>
        /// Pre: called once, right after construction (Hp == MaxHp, never multiplied before); 1 &lt;= multiplier;
        ///      MaxHp × multiplier fits Int32.
        /// Post: MaxHp == old MaxHp × multiplier; Hp == MaxHp; later <see cref="Rescale"/> calls keep the multiplier.
        /// Side effects: mutates this combatant.
        /// </remarks>
        internal void MultiplyMaxHp(int multiplier)
        {
            Contract.Requires(multiplier >= 1, "multiplier must be >= 1");
            Contract.Requires(_hpMultiplier == 1 && Hp == MaxHp, "multiply MaxHp once, before any damage");
            Contract.Requires((long)MaxHp * multiplier <= int.MaxValue, "MaxHp would exceed Int32");
            _hpMultiplier = multiplier;
            MaxHp *= multiplier;
            Hp = MaxHp;
        }

        /// <summary>Current HP.</summary>
        /// <remarks>Pre: none. Post: 0..MaxHp. Side effects: none.</remarks>
        public int Hp { get; private set; }

        /// <summary>Whether HP is 0.</summary>
        /// <remarks>Pre: none. Post: true iff Hp == 0. Side effects: none.</remarks>
        public bool IsFainted => Hp == 0;

        /// <summary>Effective resist (base or overload form plus active effects) to <paramref name="element"/> at <paramref name="nowMs"/>.</summary>
        /// <remarks>
        /// Pre: element is one of the 10 elements; nowMs &gt;= 0.
        /// Post: −ResistClamp..ResistClamp; equals the resist a hostile hit of that element would use at nowMs.
        /// Side effects: refreshes the internal modifier scratch only; HP, cooldowns, effects and RNG unchanged.
        /// </remarks>
        public int EffectiveResistPercent(Element element, long nowMs)
        {
            Contract.Requires((int)element >= 0 && (int)element < ElementCount, "element out of range");
            Contract.Requires(nowMs >= 0, "nowMs must be >= 0");
            Book.Collect(nowMs, Hp, MaxHp, _totals);
            return EffectMath.EffectiveResist(_resist[(int)element], _totals.ResistDelta(element));
        }

        /// <summary>Whether this combatant is currently in temporary combat overload (Q-C12).</summary>
        /// <remarks>Pre: none. Post: true between BeginOverload and EndOverload. Side effects: none.</remarks>
        public bool IsOverloaded { get; private set; }

        /// <summary>Enters temporary combat overload (+25% MaxHp, minimum +1; Q-C12 fallback when no 4th form exists).</summary>
        /// <remarks>
        /// Pre: !IsFainted &amp;&amp; !IsOverloaded.
        /// Post: IsOverloaded == true; MaxHp increased; Hp increased by the same delta.
        /// Side effects: mutates MaxHp, Hp, IsOverloaded.
        /// </remarks>
        public void BeginOverload()
        {
            Contract.Requires(!IsFainted && !IsOverloaded, "must be alive and not already overloaded");
            _preOverloadMaxHp = MaxHp;
            _hasOverloadFormStats = false;
            int bonus = MaxHp / 4;
            if (bonus < 1)
            {
                bonus = 1;
            }

            MaxHp += bonus;
            Hp += bonus;
            IsOverloaded = true;
        }

        /// <summary>Enters temporary combat overload by switching to the overload form's stats, mastery and resist (Q-C12).</summary>
        /// <remarks>
        /// Pre: !IsFainted &amp;&amp; !IsOverloaded; every core stat >= 0; masteryByElement and resistByElement have 10 valid entries.
        /// Post: IsOverloaded == true; MaxHp == MaxHpFromPoints(core.Hp) * _hpMultiplier; Hp grows by max(0, newMaxHp - oldMaxHp)
        ///       and is clamped to 1..MaxHp; Core/mastery/resist match the overload form.
        /// Side effects: mutates MaxHp, Hp, Core, mastery, resist and IsOverloaded; snapshots pre-overload values.
        /// </remarks>
        internal void BeginOverload(in CreatureStats core, IReadOnlyList<int> masteryByElement, IReadOnlyList<int> resistByElement)
        {
            Contract.Requires(!IsFainted && !IsOverloaded, "must be alive and not already overloaded");
            RequireCore(core);
            Contract.Requires(masteryByElement != null && masteryByElement.Count == ElementCount,
                "masteryByElement needs one entry per Element");
            Contract.Requires(resistByElement != null && resistByElement.Count == ElementCount,
                "resistByElement needs one entry per Element");

            _preOverloadMaxHp = MaxHp;
            _preOverloadCore = _core;
            _preOverloadMastery ??= new int[ElementCount];
            _preOverloadResist ??= new int[ElementCount];
            System.Array.Copy(_mastery, _preOverloadMastery, ElementCount);
            System.Array.Copy(_resist, _preOverloadResist, ElementCount);
            _hasOverloadFormStats = true;

            for (int i = 0; i < ElementCount; i++)
            {
                Contract.Requires(masteryByElement[i] >= 0 && masteryByElement[i] <= DamageModel.MaxMasteryPercent,
                    "mastery must be 0..MaxMasteryPercent");
                Contract.Requires(resistByElement[i] >= -DamageModel.ResistClamp && resistByElement[i] <= DamageModel.ResistClamp,
                    "resist must be -100..100");
                _mastery[i] = masteryByElement[i];
                _resist[i] = resistByElement[i];
            }

            int newMaxHp = (int)System.Math.Min(int.MaxValue, (long)_stats.MaxHpFromPoints(core.Hp) * _hpMultiplier);
            int delta = newMaxHp - MaxHp;
            MaxHp = newMaxHp;
            if (delta > 0)
            {
                Hp += delta;
            }
            else if (Hp > MaxHp)
            {
                Hp = MaxHp;
            }

            _core = core;
            IsOverloaded = true;
        }

        /// <summary>Reverts temporary combat overload when combat ends (Q-C12).</summary>
        /// <remarks>
        /// Pre: none. Post: IsOverloaded == false; MaxHp and any form-switched stats/mastery/resist restored and Hp clamped to MaxHp.
        /// Side effects: mutates MaxHp, Hp, stats, mastery, resist, IsOverloaded when overloaded.
        /// </remarks>
        public void EndOverload()
        {
            if (!IsOverloaded)
            {
                return;
            }

            MaxHp = _preOverloadMaxHp;
            if (Hp > MaxHp)
            {
                Hp = MaxHp;
            }

            if (_hasOverloadFormStats)
            {
                _core = _preOverloadCore;
                System.Array.Copy(_preOverloadMastery, _mastery, ElementCount);
                System.Array.Copy(_preOverloadResist, _resist, ElementCount);
                _hasOverloadFormStats = false;
            }

            IsOverloaded = false;
        }

        /// <summary>Revives a fainted combatant with <paramref name="hp"/> (Q-C36).</summary>
        /// <remarks>
        /// Pre: IsFainted; 1 &lt;= hp &lt;= MaxHp; nowMs in CombatTime and >= Book watermark.
        /// Post: !IsFainted; Hp == hp.
        /// Side effects: mutates Hp and Book.
        /// </remarks>
        internal int Revive(int hp, long nowMs)
        {
            Contract.Requires(IsFainted, "must be fainted to revive");
            Contract.Requires(hp >= 1 && hp <= MaxHp, "hp must be in 1..MaxHp");
            Book.RequireTime(nowMs);
            Hp = hp;
            Book.NotifyHpChanged(0, Hp, MaxHp, nowMs, out _);
            return Hp;
        }

        /// <summary>This combatant's trait and timed effects.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public EffectBook Book { get; }

        /// <summary>This combatant's move cooldowns.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public MoveCooldowns Cooldowns { get; }

        /// <summary>Tries to cast the move in <paramref name="slot"/>.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= slot &lt; Cooldowns.SlotCount; move from the CastMove constructor; nowMs in CombatTime and >= every
        ///      watermark of Book and Cooldowns.
        /// Post: Fainted iff IsFainted on entry; else OnCooldown iff !Cooldowns.IsReady(slot, nowMs) on entry; on those two nothing
        ///       changes and context is default. Accepted otherwise: on_move_used{move.Element} has fired (heals applied),
        ///       context.MasteryPercent / RangePercentSum reflect modifiers after that, and
        ///       Cooldowns.ReadyAtMs(slot) == nowMs + context.CooldownMs.
        /// Side effects: on Accepted, mutates Book, Hp (trait heal) and Cooldowns.
        /// </remarks>
        public CastResult BeginCast(int slot, in CastMove move, long nowMs, out CastContext context)
        {
            return BeginCast(slot, move, nowMs, WeatherProfiles.Clear, out context);
        }

        /// <summary>Starts casting <paramref name="move"/> in <paramref name="slot"/> under <paramref name="weather"/>.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= slot &lt; Cooldowns.SlotCount; move from the CastMove constructor; nowMs in CombatTime and >= every
        ///      watermark of Book and Cooldowns.
        /// Post: Fainted iff IsFainted on entry; else OnCooldown iff !Cooldowns.IsReady(slot, nowMs) on entry; on those two nothing
        ///       changes; else Accepted, weather elemental CD and power deltas apply, on_move_used fires, context freezes the
        ///       effective mastery, range, power delta and cooldown, and Cooldowns.ReadyAtMs(slot) == nowMs + context.CooldownMs.
        /// Side effects: on Accepted, mutates Book, Hp (trait heal) and Cooldowns.
        /// </remarks>
        public CastResult BeginCast(int slot, in CastMove move, long nowMs, in WeatherProfile weather, out CastContext context)
        {
            Contract.Requires(move.Id != null, "move must come from the CastMove constructor");
            // Both checks run before any early return so a refused cast still enforces Pre (ARGON cast review K1).
            bool ready = Cooldowns.IsReady(slot, nowMs);
            Book.RequireTime(nowMs);
            context = default;
            if (IsFainted)
            {
                return CastResult.Fainted;
            }

            if (!ready)
            {
                return CastResult.OnCooldown;
            }

            Book.NotifyMoveUsed(move.Element, nowMs, out int healPercent);
            HealPercent(healPercent, nowMs);

            Book.Collect(nowMs, Hp, MaxHp, _totals);
            long masteryDelta = _totals.MasteryDelta(move.Element);
            int weaponPower = 1000;
            int speed = 1000;
            if (_weapons.HandCount > 0)
            {
                // REQ: crafting.md#武器數值模型 — dual wield alternates hands per accepted cast; a hand's element
                // mastery counts only when it matches the move.
                WeaponProfile hand = _weapons.Hand(_acceptedCasts);
                if (hand.Element == move.Element)
                {
                    masteryDelta += hand.ElementMastery;
                }

                weaponPower = _weapons.PowerPermille(hand);
                speed = _weapons.SpeedPermille(hand);
                _acceptedCasts++;
            }

            int mastery = EffectMath.EffectiveMastery(_mastery[(int)move.Element], masteryDelta);
            int cd = EffectMath.EffectiveCooldownMs(_stats, move.BaseCooldownMs, _core.Speed,
                _totals.CdPercent(move.Element) + weather.CdDeltaPercent(move.Element), _rules);
            if (speed != 1000)
            {
                long sped = ((long)cd * 1000 + speed - 1) / speed;
                int floor = _stats.Coefficients.CdFloorMs;
                cd = sped < floor ? floor : sped > int.MaxValue ? int.MaxValue : (int)sped;
            }

            Cooldowns.Start(slot, nowMs, cd);
            context = new CastContext(move, mastery, _totals.RangePercent, cd, nowMs,
                weather.PowerDeltaPercent(move.Element), weaponPower, ClampInt(_totals.AccuracyDelta, -1000, 1000),
                CritRatePermille(), CritDamagePercent(), _core.Attack(move.Polarity));
            return CastResult.Accepted;
        }

        /// <summary>Replaces the departure weapon loadout; default(WeaponLoadout) is unarmed.</summary>
        /// <remarks>
        /// REQ: docs/design/crafting.md#穿戴與數值.
        /// Pre: Book's simulation watermark is zero (departure only, like equipment effects).
        /// Post: later accepted casts use the loadout's multiplier, speed and element mastery, starting from the first hand;
        ///       HasShield equals loadout.HasShield.
        /// Side effects: replaces weapon state and resets hand alternation.
        /// </remarks>
        public void SetWeapons(in WeaponLoadout loadout)
        {
            Contract.Requires(Book.WatermarkMs == 0, "weapons must be assigned before simulation time advances");
            _weapons = loadout;
            _acceptedCasts = 0;
        }

        // REQ: combat.md#暴擊 — base from balance.json#crit, raised by crit_rate_mod / crit_damage_mod percentage points.
        private int CritRatePermille()
        {
            return (int)ClampLong(_rules.CritBaseRatePermille + _totals.CritRateDelta * 10, 0, DerivedStats.Permille);
        }

        private int CritDamagePercent()
        {
            return (int)ClampLong(_rules.CritBaseDamagePercent + _totals.CritDamageDelta, 100,
                DamageModel.MaxCritDamagePercent);
        }

        private static long ClampLong(long v, long min, long max) => v < min ? min : v > max ? max : v;

        private static int ClampInt(long v, int min, int max) => (int)ClampLong(v, min, max);

        /// <summary>Σ block_reduction_mod (percentage points) active at <paramref name="nowMs"/>.</summary>
        /// <remarks>
        /// REQ: combat.md#格擋 — armor's 格擋減免率 adds to the base / shield block value.
        /// Pre: nowMs in CombatTime and >= Book's watermark. Post: none. Side effects: overwrites the modifier buffer only.
        /// </remarks>
        public long BlockReductionDelta(long nowMs)
        {
            Book.Collect(nowMs, Hp, MaxHp, _totals);
            return _totals.BlockReductionDelta;
        }

        /// <summary>Σ stagger_capacity_mod_percent active at <paramref name="nowMs"/>.</summary>
        /// <remarks>
        /// REQ: combat.md#失衡與擊退 — armor's 失衡值 scales the stagger gauge capacity.
        /// Pre: nowMs in CombatTime and >= Book's watermark. Post: none. Side effects: overwrites the modifier buffer only.
        /// </remarks>
        public long StaggerCapacityPercent(long nowMs)
        {
            Book.Collect(nowMs, Hp, MaxHp, _totals);
            return _totals.StaggerCapacityPercent;
        }

        /// <summary>Whether a shield is held (block uses block.shield_permille).</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool HasShield => _weapons.HasShield;

        /// <summary>Move speed (mm/s) at <paramref name="nowMs"/> after AGI and the active move_speed_mod_percent.</summary>
        /// <remarks>
        /// REQ: combat.md#效果疊加 — modifiers are summed first, then applied once with the [10%, 300%] clamp.
        /// Pre: nowMs in CombatTime and >= Book's watermark.
        /// Post: result >= 0; equals EffectMath.MoveSpeedMmS(stats, agi, Σ move_speed deltas active at nowMs, rules), so it
        ///       equals stats.MoveSpeedMmS(agi) when no modifier is active.
        /// Side effects: overwrites the internal modifier buffer only; no allocation.
        /// </remarks>
        public int MoveSpeedMmS(long nowMs)
        {
            Book.Collect(nowMs, Hp, MaxHp, _totals);
            return EffectMath.MoveSpeedMmS(_stats, _core.Speed, _totals.MoveSpeedPercent, _rules);
        }

        /// <summary>Σ range_mod_percent active at <paramref name="nowMs"/>, as a cast starting now would see it before its own on_move_used.</summary>
        /// <remarks>
        /// REQ: SOL Q-D1-IMPL-01 — AI reach must use the same range modifiers as BeginCast, or a debuffed caster stands
        /// still and fires short forever. Unlike BeginCast this does not fire on_move_used (a preview must not spend
        /// triggers), so a range buff the cast itself would grant is missing; every on_move_used range trait in Data is
        /// positive (2026-11-11), so the preview errs short and the AI steps closer instead of firing out of reach.
        /// Pre: nowMs in CombatTime and >= Book's watermark.
        /// Post: equals the RangePercentSum BeginCast(…, nowMs) would report when the move's on_move_used grants no range.
        /// Side effects: overwrites the internal modifier buffer only; no allocation.
        /// </remarks>
        public long RangePercent(long nowMs)
        {
            Book.Collect(nowMs, Hp, MaxHp, _totals);
            return _totals.RangePercent;
        }

        /// <summary>Resolves an enemy-targeted move (any shape except self/ally) landing on this combatant.</summary>
        /// <remarks>
        /// Call once per (cast or field tick, target), in the host's deterministic target order.
        /// Pre: !IsFainted (callers drop fainted targets before geometry); cast from BeginCast; rng is the host's combat
        ///      stream; nowMs in CombatTime and >= every watermark of Book.
        /// Post: Outcome follows <see cref="HitResolver.Resolve"/> with this combatant's evasion after modifiers sampled
        ///       before the hit. On Hit: Damage = min(HpBefore, DamageModel.Compute(power, cast mastery, effective resist)) for
        ///       damaging moves; then, if still alive, on_hp_cross_below / on_hit_taken fire, the move's timed effect is
        ///       applied (or its heal added), and all heals resolve once. Trait buffs gained here never change this
        ///       hit's damage. On NoContact / Miss: nothing but the roll changes.
        /// Side effects: advances rng by one draw iff geometricContact; mutates Hp and Book on Hit.
        /// </remarks>
        public HitReport ReceiveHostile(in CastContext cast, bool geometricContact, ref Pcg32 rng, long nowMs)
        {
            return ReceiveHostile(cast, geometricContact, ref rng, nowMs, 0);
        }

        /// <summary>As <see cref="ReceiveHostile(in CastContext, bool, ref Pcg32, long)"/>, taking extra damage (stagger).</summary>
        /// <remarks>
        /// REQ: combat.md#失衡與擊退 — a staggered unit takes more damage.
        /// Pre: as the four-argument overload; 0 &lt;= damageTakenBonusPercent &lt;= 1000.
        /// Post: as the four-argument overload, except the formula damage is first scaled by (100 + bonus)/100 rounded
        ///       half up (a nonzero raw stays >= 1; immunity stays 0). Side effects: as the four-argument overload.
        /// </remarks>
        public HitReport ReceiveHostile(in CastContext cast, bool geometricContact, ref Pcg32 rng, long nowMs,
            int damageTakenBonusPercent)
        {
            return ReceiveHostile(cast, geometricContact, ref rng, nowMs, damageTakenBonusPercent, 0, false);
        }

        /// <summary>As the five-argument overload, with a block reduction and roll i-frames.</summary>
        /// <remarks>
        /// REQ: combat.md#格擋, #完美閃避.
        /// Pre: as the five-argument overload; 0 &lt;= blockPermille &lt;= 1000.
        /// Post: invulnerable with contact → Outcome Invulnerable, no roll drawn, nothing changes (like a dodge-rolling
        ///       character). Otherwise as the five-argument overload, except the bonus-scaled damage (PreBlockDamage) is then
        ///       scaled by (1000 − blockPermille)/1000 rounded half up; a nonzero value stays >= 1 unless blockPermille == 1000.
        /// Side effects: as the five-argument overload; no rng draw when invulnerable.
        /// </remarks>
        public HitReport ReceiveHostile(in CastContext cast, bool geometricContact, ref Pcg32 rng, long nowMs,
            int damageTakenBonusPercent, int blockPermille, bool invulnerable)
        {
            Contract.Requires(!IsFainted, "a fainted combatant is not a target");
            Contract.Requires(blockPermille >= 0 && blockPermille <= 1000, "blockPermille must be 0..1000");
            Contract.Requires(damageTakenBonusPercent >= 0 && damageTakenBonusPercent <= 1000,
                "damageTakenBonusPercent must be 0..1000");
            CastMove move = cast.Move;
            Contract.Requires(move.Id != null, "cast must come from BeginCast");
            int hpBefore = Hp;

            Book.Collect(nowMs, Hp, MaxHp, _totals);
            int evasion = EffectMath.EvasionPermille(_stats, _stats.Coefficients.SharedBaseStat, _totals.EvasionPercent,
                _rules);
            int chance = _stats.HitChancePermille(cast.EffectiveAccuracyPercent, evasion);
            HitOutcome outcome = HitResolver.Resolve(geometricContact, invulnerable, chance, ref rng);
            if (outcome != HitOutcome.Hit)
            {
                return new HitReport(outcome, 0, 0, hpBefore, hpBefore, false);
            }

            int damage = 0;
            int preBlock = 0;
            bool critical = false;
            if (move.DealsDamage)
            {
                int resist = EffectMath.EffectiveResist(_resist[(int)move.Element], _totals.ResistDelta(move.Element));
                int raw = DamageModel.Compute(cast.EffectivePower, cast.MasteryPercent, resist,
                    _stats.AttackRatioPermille(cast.Attack, _core.Defence(move.Polarity)));
                critical = HitResolver.RollCrit(cast.CritRatePermille, ref rng);
                if (critical)
                {
                    raw = DamageModel.ApplyCrit(raw, cast.CritDamagePercent);
                }
                if (damageTakenBonusPercent > 0)
                {
                    raw = (int)System.Math.Min(int.MaxValue, ((long)raw * (100 + damageTakenBonusPercent) + 50) / 100);
                }

                preBlock = raw;
                if (blockPermille > 0)
                {
                    long kept = ((long)raw * (1000 - blockPermille) + 500) / 1000;
                    raw = (int)(raw > 0 && kept == 0 && blockPermille < 1000 ? 1 : kept);
                }

                damage = raw > Hp ? Hp : raw;
                Hp -= damage;
                if (Hp == 0)
                {
                    // Q-C18 spirit (user decision 2026-11-07): nothing may fire on a fainted unit, so no heal revives it.
                    return new HitReport(outcome, damage, 0, hpBefore, 0, false, preBlock, critical);
                }
            }

            int hpAfterDamage = Hp;
            Book.NotifyHpChanged(hpBefore, hpAfterDamage, MaxHp, nowMs, out int crossHeal);
            Book.NotifyHitTaken(move.Element, nowMs, out int hitHeal);

            bool applied = false;
            int moveHeal = 0;
            if (move.HasEffect && move.Effect.IsInstant)
            {
                // combat.md#招式效果: the target is whoever the shape hits, so a heal on an enemy shape heals that enemy.
                moveHeal = move.Effect.Percent;
            }
            else if (move.HasEffect)
            {
                Book.Apply(move.Id, 0, move.Effect, move.EffectDurationMs, nowMs);
                applied = true;
            }

            HealPercent(crossHeal + hitHeal + moveHeal, nowMs);
            return new HitReport(outcome, damage, Hp - hpAfterDamage, hpBefore, Hp, applied, preBlock, critical);
        }

        /// <summary>Resolves a self/ally move landing on this combatant: no roll (user decision 2026-11-07).</summary>
        /// <remarks>
        /// Damage/both on self/ally is a cost (user decision 2026-11-07, ARGON cast review K3): it hurts the receiver with
        /// the caster's frozen mastery and the receiver's resist, may faint it, and is not a hostile hit.
        /// Pre: !IsFainted; cast from BeginCast; nowMs in CombatTime and >= every watermark of Book.
        /// Post: Outcome == Hit. Damage = min(HpBefore, DamageModel.Compute(power, cast mastery, effective resist)) when the
        ///       move deals damage, else 0; if that faints the receiver nothing else happens. Otherwise on_hp_cross_below
        ///       may fire (it is an HP change), the move's timed effect is applied or its heal added, and heals resolve
        ///       once. on_hit_taken never fires — that trigger means a hostile hit.
        /// Side effects: mutates Hp and Book. Does not touch any rng.
        /// </remarks>
        public HitReport ReceiveSupport(in CastContext cast, long nowMs)
        {
            Contract.Requires(!IsFainted, "a fainted combatant cannot be supported (no revive)");
            CastMove move = cast.Move;
            Contract.Requires(move.Id != null, "cast must come from BeginCast");
            Book.RequireTime(nowMs);
            int hpBefore = Hp;
            int damage = 0;
            int crossHeal = 0;
            if (move.DealsDamage)
            {
                Book.Collect(nowMs, Hp, MaxHp, _totals);
                int resist = EffectMath.EffectiveResist(_resist[(int)move.Element], _totals.ResistDelta(move.Element));
                int raw = DamageModel.Compute(move.Power, cast.MasteryPercent, resist,
                    _stats.AttackRatioPermille(cast.Attack, _core.Defence(move.Polarity)));
                damage = raw > Hp ? Hp : raw;
                Hp -= damage;
                if (Hp == 0)
                {
                    return new HitReport(HitOutcome.Hit, damage, 0, hpBefore, 0, false);
                }

                Book.NotifyHpChanged(hpBefore, Hp, MaxHp, nowMs, out crossHeal);
            }

            int hpAfterDamage = Hp;
            bool applied = false;
            int moveHeal = 0;
            if (move.HasEffect && move.Effect.IsInstant)
            {
                moveHeal = move.Effect.Percent;
            }
            else if (move.HasEffect)
            {
                Book.Apply(move.Id, 0, move.Effect, move.EffectDurationMs, nowMs);
                applied = true;
            }

            HealPercent(crossHeal + moveHeal, nowMs);
            return new HitReport(HitOutcome.Hit, damage, Hp - hpAfterDamage, hpBefore, Hp, applied);
        }

        /// <summary>Takes a perfect block's counter-damage from the move this combatant cast (combat.md#格擋, Q-C59).</summary>
        /// <remarks>
        /// Not a hostile hit: no roll, no on_hit_taken, no timed effect (like a cost, ReceiveSupport).
        /// Pre: !IsFainted; cast is one this combatant began (BeginCast); 0 &lt;= percent &lt;= 1000; nowMs in CombatTime and
        ///      >= every watermark of Book.
        /// Post: returns damage dealt = min(HpBefore, round_half_up(DamageModel.Compute(power, cast mastery, this
        ///       combatant's effective resist) × percent / 100)), at least 1 when that formula damage and percent are
        ///       nonzero; 0 for non-damaging moves. If still alive, on_hp_cross_below may fire and its heal resolves.
        /// Side effects: mutates Hp and Book. Does not touch any rng.
        /// </remarks>
        public int ReceiveReflect(in CastContext cast, int percent, long nowMs)
        {
            Contract.Requires(!IsFainted, "a fainted combatant takes no counter-damage");
            Contract.Requires(percent >= 0 && percent <= 1000, "percent must be 0..1000");
            CastMove move = cast.Move;
            Contract.Requires(move.Id != null, "cast must come from BeginCast");
            Book.RequireTime(nowMs);
            if (!move.DealsDamage || percent == 0)
            {
                return 0;
            }

            Book.Collect(nowMs, Hp, MaxHp, _totals);
            int resist = EffectMath.EffectiveResist(_resist[(int)move.Element], _totals.ResistDelta(move.Element));
            // combat.md#傷害公式: the reflected move turns back on its caster, so its own defence applies (like its resist).
            int raw = DamageModel.Compute(cast.EffectivePower, cast.MasteryPercent, resist,
                _stats.AttackRatioPermille(cast.Attack, _core.Defence(move.Polarity)));
            long scaled = ((long)raw * percent + 50) / 100;
            int amount = (int)System.Math.Min(int.MaxValue, raw > 0 && scaled == 0 ? 1 : scaled);
            int hpBefore = Hp;
            int damage = amount > Hp ? Hp : amount;
            Hp -= damage;
            if (Hp > 0)
            {
                Book.NotifyHpChanged(hpBefore, Hp, MaxHp, nowMs, out int crossHeal);
                HealPercent(crossHeal, nowMs);
            }

            return damage;
        }

        /// <summary>The creature entered the field: fires on_summon, and on_ally_swap too when it came in by a switch.</summary>
        /// <remarks>
        /// REQ: combat.md#特性觸發 (every entry is a summon; on_ally_swap = the creature swapped in). Routing the trait heal
        /// through here is the only way it reaches Hp (ARGON cast review K2).
        /// Pre: !IsFainted (a fainted creature cannot enter); nowMs in CombatTime and >= every watermark of Book.
        /// Post: returns HP restored (>= 0); Hp == previous Hp + result &lt;= MaxHp.
        /// Side effects: mutates Book (trait windows) and Hp (trait heals, resolved once for both triggers).
        /// </remarks>
        public int EnterField(bool bySwap, long nowMs)
        {
            Contract.Requires(!IsFainted, "a fainted combatant cannot enter the field");
            int hpBefore = Hp;
            Book.NotifySummoned(nowMs, out int summonHeal);
            int swapHeal = 0;
            if (bySwap)
            {
                Book.NotifySwappedIn(nowMs, out swapHeal);
            }

            HealPercent(summonHeal + swapHeal, nowMs);
            return Hp - hpBefore;
        }

        /// <summary>Heals a living combatant by <paramref name="percent"/>% of MaxHp (e.g. from food or consumables).</summary>
        /// <remarks>
        /// Pre: !IsFainted; 0 &lt;= percent &lt;= 100; nowMs in CombatTime and >= every watermark of Book.
        /// Post: returns HP restored (&gt;= 0); Hp == previous Hp + result &lt;= MaxHp.
        /// Side effects: mutates Hp and notifies Book of the HP change.
        /// </remarks>
        public int HealByPercent(int percent, long nowMs)
        {
            Contract.Requires(!IsFainted, "cannot heal a fainted combatant; use ReviveByPercent");
            Contract.Requires(percent >= 0 && percent <= 100, "percent must be 0..100");
            Book.RequireTime(nowMs);
            int hpBefore = Hp;
            HealPercent(percent, nowMs);
            return Hp - hpBefore;
        }

        /// <summary>Revives a fainted combatant with <paramref name="percent"/>% of MaxHp (minimum 1 HP).</summary>
        /// <remarks>
        /// Pre: IsFainted; 1 &lt;= percent &lt;= 100; nowMs in CombatTime and >= every watermark of Book.
        /// Post: !IsFainted; 1 &lt;= Hp &lt;= MaxHp; returns restored HP.
        /// Side effects: mutates Hp and advances Book watermark.
        /// </remarks>
        public int ReviveByPercent(int percent, long nowMs)
        {
            Contract.Requires(IsFainted, "combatant is not fainted");
            Contract.Requires(percent >= 1 && percent <= 100, "percent must be 1..100");
            int amount = EffectMath.HealAmount(MaxHp, percent);
            if (amount < 1) amount = 1;
            if (amount > MaxHp) amount = MaxHp;
            Revive(amount, nowMs);
            return Hp;
        }

        /// <summary>Cleanses all harmful timed effects from this combatant's <see cref="Book"/>.</summary>
        /// <remarks>
        /// Pre: !IsFainted; nowMs in CombatTime and >= every watermark of Book.
        /// Post: returns number of negative timed effects removed (&gt;= 0).
        /// Side effects: mutates Book.
        /// </remarks>
        public int CleanseNegativeEffects(long nowMs)
        {
            Contract.Requires(!IsFainted, "a fainted combatant cannot be cleansed");
            return Book.RemoveNegativeTimedEffects(nowMs);
        }

        private void HealPercent(int percent, long nowMs)
        {
            if (percent <= 0 || Hp == MaxHp)
            {
                return;
            }

            int amount = EffectMath.HealAmount(MaxHp, percent);
            int previous = Hp;
            Hp = amount >= MaxHp - Hp ? MaxHp : Hp + amount;
            // A heal can never cross below, but the book's contract is "notify after every HP change".
            Book.NotifyHpChanged(previous, Hp, MaxHp, nowMs, out int _);
        }
    }
}
