using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;

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
            bool hasEffect, in EffectSpec effect, int effectDurationMs)
        {
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
            Contract.Requires(def != null, "def must not be null");
            bool hasEffect = def.Effect != null;
            EffectSpec effect = hasEffect ? EffectSpec.FromDef(def.Effect) : default;
            int duration = def.HasEffectDuration ? def.EffectDurationMs : 0;
            return new CastMove(def.Id, def.Element, def.Kind, def.Power, def.Accuracy, def.CooldownMs, hasEffect, effect,
                duration);
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
        internal CastContext(in CastMove move, int masteryPercent, long rangePercentSum, int cooldownMs, long castAtMs)
        {
            Move = move;
            MasteryPercent = masteryPercent;
            RangePercentSum = rangePercentSum;
            CooldownMs = cooldownMs;
            CastAtMs = castAtMs;
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
    }

    /// <summary>What one move did to one combatant.</summary>
    /// <remarks>Pre: obtained from Combatant.ReceiveHostile / ReceiveSupport (default is an empty report). Post: immutable. Side effects: none.</remarks>
    public readonly struct HitReport
    {
        internal HitReport(HitOutcome outcome, int damage, int healed, int hpBefore, int hpAfter, bool effectApplied)
        {
            Outcome = outcome;
            Damage = damage;
            Healed = healed;
            HpBefore = hpBefore;
            HpAfter = hpAfter;
            EffectApplied = effectApplied;
        }

        /// <summary>Contact / roll result; Miss must get explicit feedback (combat.md#命中與閃避).</summary>
        /// <remarks>Pre: none. Post: defined; never Invulnerable (creatures have no i-frames). Side effects: none.</remarks>
        public HitOutcome Outcome { get; }

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
        private int _spi;
        private int _agi;
        private readonly int[] _mastery = new int[ElementCount];
        private readonly int[] _resist = new int[ElementCount];
        private readonly ModifierTotals _totals = new ModifierTotals();
        private int _hpMultiplier = 1;

        /// <summary>Creates a combatant at full HP with all moves ready.</summary>
        /// <remarks>
        /// Pre: stats, rules, book non-null; con, spi, agi >= 0; masteryByElement and resistByElement have exactly 10
        ///      entries indexed by <see cref="Element"/>; mastery 0..DamageModel.MaxMasteryPercent; resist
        ///      −ResistClamp..ResistClamp; 1 &lt;= moveSlots &lt;= MoveCooldowns.MaxSlots.
        /// Post: Hp == MaxHp == stats.MaxHp(con); Book == book; every slot ready.
        /// Side effects: copies the element tables; allocates per-combatant buffers (construction only).
        /// </remarks>
        public Combatant(DerivedStats stats, EffectRulesConfig rules, int con, int spi, int agi,
            IReadOnlyList<int> masteryByElement, IReadOnlyList<int> resistByElement, EffectBook book, int moveSlots)
        {
            Contract.Requires(stats != null && rules != null && book != null, "stats, rules and book must not be null");
            Contract.Requires(con >= 0 && spi >= 0 && agi >= 0, "stats must be nonnegative");
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
            _spi = spi;
            _agi = agi;
            Book = book;
            MaxHp = stats.MaxHp(con);
            Hp = MaxHp;
            Cooldowns = new MoveCooldowns(moveSlots);
        }

        /// <summary>Max HP from CON.</summary>
        /// <remarks>Pre: none. Post: >= HpBase >= 1. Side effects: none.</remarks>
        public int MaxHp { get; private set; }

        /// <summary>
        /// Re-derives the stat-driven values after a level-up between battles (Q-C31, 2026-11-09: growth applies when
        /// the battle ends). The damage already taken is kept: Hp rises by exactly the MaxHp increase.
        /// </summary>
        /// <remarks>
        /// Pre: !IsFainted (a fainted creature earns no XP); con, spi, agi >= 0; stats.MaxHp(con) × HP multiplier >= MaxHp
        ///      (levels never go down, and the stat curves are monotone).
        /// Post: MaxHp == stats.MaxHp(con) × the multiplier set by <see cref="MultiplyMaxHp"/> (1 if never set);
        ///       Hp == old Hp + (new MaxHp − old MaxHp); SPI/AGI-derived values use the new
        ///       stats from the next query; cooldowns, effects and mastery/resist unchanged.
        /// Side effects: mutates this combatant.
        /// </remarks>
        internal void Rescale(int con, int spi, int agi)
        {
            Contract.Requires(!IsFainted, "a fainted combatant cannot level up");
            Contract.Requires(con >= 0 && spi >= 0 && agi >= 0, "stats must be nonnegative");
            int maxHp = _stats.MaxHp(con) * _hpMultiplier;
            Contract.Requires(maxHp >= MaxHp, "a level-up never lowers MaxHp");
            Hp += maxHp - MaxHp;
            MaxHp = maxHp;
            _spi = spi;
            _agi = agi;
        }

        /// <summary>Multiplies MaxHp for this combatant's whole life (boss fights: HP ×2, D0-R2) and refills HP.</summary>
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
            int mastery = EffectMath.EffectiveMastery(_mastery[(int)move.Element], _totals.MasteryDelta(move.Element));
            int cd = EffectMath.EffectiveCooldownMs(_stats, move.BaseCooldownMs, _spi, _totals.CdPercent(move.Element),
                _rules);
            Cooldowns.Start(slot, nowMs, cd);
            context = new CastContext(move, mastery, _totals.RangePercent, cd, nowMs);
            return CastResult.Accepted;
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
            Contract.Requires(!IsFainted, "a fainted combatant is not a target");
            CastMove move = cast.Move;
            Contract.Requires(move.Id != null, "cast must come from BeginCast");
            int hpBefore = Hp;

            Book.Collect(nowMs, Hp, MaxHp, _totals);
            int evasion = EffectMath.EvasionPermille(_stats, _agi, _totals.EvasionPercent, _rules);
            int chance = _stats.HitChancePermille(move.AccuracyPercent, evasion);
            HitOutcome outcome = HitResolver.Resolve(geometricContact, false, chance, ref rng);
            if (outcome != HitOutcome.Hit)
            {
                return new HitReport(outcome, 0, 0, hpBefore, hpBefore, false);
            }

            int damage = 0;
            if (move.DealsDamage)
            {
                int resist = EffectMath.EffectiveResist(_resist[(int)move.Element], _totals.ResistDelta(move.Element));
                int raw = DamageModel.Compute(move.Power, cast.MasteryPercent, resist);
                damage = raw > Hp ? Hp : raw;
                Hp -= damage;
                if (Hp == 0)
                {
                    // Q-C18 spirit (user decision 2026-11-07): nothing may fire on a fainted unit, so no heal revives it.
                    return new HitReport(outcome, damage, 0, hpBefore, 0, false);
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
            return new HitReport(outcome, damage, Hp - hpAfterDamage, hpBefore, Hp, applied);
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
                int raw = DamageModel.Compute(move.Power, cast.MasteryPercent, resist);
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
