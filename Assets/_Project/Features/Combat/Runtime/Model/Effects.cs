using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Combat
{
    /// <summary>A typed effect as the combat rules see it; mirrors <see cref="EffectDef"/> but is constructible in tests.</summary>
    /// <remarks>
    /// REQ: docs/design/combat.md#效果疊加, common.schema.json#/$defs/effect.
    /// Pre: build through the constructor or <see cref="FromDef"/>. Post: immutable; fields obey the constructor's Pre.
    /// Side effects: none.
    /// </remarks>
    public readonly struct EffectSpec
    {
        /// <summary>Creates a validated effect.</summary>
        /// <remarks>
        /// Pre: type is a defined EffectType; element is a defined Element even where it is meaningless (pass default);
        ///      for HealPercentMaxHp: 1 &lt;= percent &lt;= 100, delta == 0, maxStacks == 1;
        ///      otherwise: -100 &lt;= delta &lt;= 100, percent == 0, 1 &lt;= maxStacks &lt;= EffectDef.MaxStacksLimit;
        ///      anyElement only for CdModPercent.
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public EffectSpec(EffectType type, bool anyElement, Element element, int delta, int percent, int maxStacks)
        {
            Contract.Requires(type >= EffectType.MasteryMod && type <= EffectType.HealPercentMaxHp, "undefined EffectType");
            Contract.Requires(element >= Element.Metal && element <= Element.Shadow, "undefined Element");
            Contract.Requires(!anyElement || type == EffectType.CdModPercent, "only cd_mod_percent accepts 'any'");
            if (type == EffectType.HealPercentMaxHp)
            {
                Contract.Requires(percent >= 1 && percent <= 100, "heal percent must be 1..100");
                Contract.Requires(delta == 0 && maxStacks == 1, "a heal has no delta and never stacks");
            }
            else
            {
                Contract.Requires(delta >= -100 && delta <= 100, "delta must be -100..100");
                Contract.Requires(percent == 0, "only a heal has a percent");
                Contract.Requires(maxStacks >= 1 && maxStacks <= EffectDef.MaxStacksLimit, "maxStacks must be 1..MaxStacksLimit");
            }

            Type = type;
            AnyElement = anyElement;
            Element = element;
            Delta = delta;
            Percent = percent;
            MaxStacks = maxStacks;
        }

        /// <summary>Effect type.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public EffectType Type { get; }

        /// <summary>True for a cd_mod_percent scoped to every element.</summary>
        /// <remarks>Pre: none. Post: implies Type == CdModPercent. Side effects: none.</remarks>
        public bool AnyElement { get; }

        /// <summary>Element scope for MasteryMod / ResistMod / CdModPercent; meaningless otherwise.</summary>
        /// <remarks>Pre: none. Post: defined Element. Side effects: none.</remarks>
        public Element Element { get; }

        /// <summary>Signed modifier: percentage points (mastery, resist) or relative percent (cd, evasion, speed, range).</summary>
        /// <remarks>Pre: none. Post: -100..100; 0 for a heal. Side effects: none.</remarks>
        public int Delta { get; }

        /// <summary>Heal amount as percent of max HP.</summary>
        /// <remarks>Pre: none. Post: 1..100 for a heal, else 0. Side effects: none.</remarks>
        public int Percent { get; }

        /// <summary>Stack limit per (source id, effect index) on one target; 1 = refresh only.</summary>
        /// <remarks>Pre: none. Post: 1..EffectDef.MaxStacksLimit. Side effects: none.</remarks>
        public int MaxStacks { get; }

        /// <summary>True when the effect resolves instantly instead of lasting a window.</summary>
        /// <remarks>Pre: none. Post: true iff Type == HealPercentMaxHp. Side effects: none.</remarks>
        public bool IsInstant => Type == EffectType.HealPercentMaxHp;

        /// <summary>Converts an imported definition.</summary>
        /// <remarks>Pre: def != null and importer-validated. Post: same values as def. Side effects: none.</remarks>
        public static EffectSpec FromDef(EffectDef def)
        {
            Contract.Requires(def != null, "def must not be null");
            if (def.Type == EffectType.HealPercentMaxHp)
            {
                return new EffectSpec(def.Type, false, default, 0, def.Percent, 1);
            }

            bool any = def.HasElement && def.AnyElement;
            Element element = def.HasElement && !any ? def.Element : default;
            return new EffectSpec(def.Type, any, element, def.Delta, 0, def.MaxStacks);
        }
    }

    /// <summary>A trait trigger as the combat rules see it; mirrors <see cref="TriggerDef"/>.</summary>
    /// <remarks>
    /// REQ: docs/design/combat.md#特性觸發. Pre: build through the constructor or <see cref="FromDef"/>.
    /// Post: immutable. Side effects: none.
    /// </remarks>
    public readonly struct TriggerSpec
    {
        /// <summary>Creates a validated trigger.</summary>
        /// <remarks>
        /// Pre: type defined; element is a defined Element even where it is meaningless (pass default);
        ///      OnHpBelow/OnHpAbove: 1 &lt;= percent &lt;= 100 and durationMs == 0; OnSummon/OnHitTaken/
        ///      OnMoveUsed/OnAllySwap: 1 &lt;= durationMs &lt;= 600000 and percent == 0; Always: both 0;
        ///      anyElement only for OnHitTaken.
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public TriggerSpec(TriggerType type, bool anyElement, Element element, int percent, int durationMs)
        {
            Contract.Requires(type >= TriggerType.Always && type <= TriggerType.OnAllySwap, "undefined TriggerType");
            Contract.Requires(element >= Element.Metal && element <= Element.Shadow, "undefined Element");
            Contract.Requires(!anyElement || type == TriggerType.OnHitTaken, "only on_hit_taken accepts 'any'");
            bool threshold = type == TriggerType.OnHpBelow || type == TriggerType.OnHpAbove;
            bool timed = !threshold && type != TriggerType.Always;
            Contract.Requires(threshold ? percent >= 1 && percent <= 100 : percent == 0, "percent must match the trigger type");
            Contract.Requires(timed ? durationMs >= 1 && durationMs <= EffectBook.MaxDurationMs : durationMs == 0,
                "durationMs must match the trigger type");

            Type = type;
            AnyElement = anyElement;
            Element = element;
            Percent = percent;
            DurationMs = durationMs;
        }

        /// <summary>Trigger type.</summary>
        /// <remarks>Pre: none. Post: defined. Side effects: none.</remarks>
        public TriggerType Type { get; }

        /// <summary>True for on_hit_taken{any}.</summary>
        /// <remarks>Pre: none. Post: implies Type == OnHitTaken. Side effects: none.</remarks>
        public bool AnyElement { get; }

        /// <summary>Element scope for OnHitTaken / OnMoveUsed; meaningless otherwise.</summary>
        /// <remarks>Pre: none. Post: defined Element. Side effects: none.</remarks>
        public Element Element { get; }

        /// <summary>HP threshold percent for OnHpBelow / OnHpAbove, else 0.</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int Percent { get; }

        /// <summary>Window length for event triggers, else 0.</summary>
        /// <remarks>Pre: none. Post: 0..600000. Side effects: none.</remarks>
        public int DurationMs { get; }

        /// <summary>True when the trigger is a state test evaluated at query time (always / HP thresholds).</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool IsContinuous => DurationMs == 0;

        /// <summary>Converts an imported definition.</summary>
        /// <remarks>Pre: def != null and importer-validated. Post: same values as def. Side effects: none.</remarks>
        public static TriggerSpec FromDef(TriggerDef def)
        {
            Contract.Requires(def != null, "def must not be null");
            switch (def.Type)
            {
                case TriggerType.OnHpBelow:
                case TriggerType.OnHpAbove:
                    return new TriggerSpec(def.Type, false, default, def.Percent, 0);
                case TriggerType.OnHitTaken:
                    return new TriggerSpec(def.Type, def.AnyElement, def.AnyElement ? default : def.Element, 0, def.DurationMs);
                case TriggerType.OnMoveUsed:
                    return new TriggerSpec(def.Type, false, def.Element, 0, def.DurationMs);
                case TriggerType.OnSummon:
                case TriggerType.OnAllySwap:
                    return new TriggerSpec(def.Type, false, default, 0, def.DurationMs);
                default:
                    return new TriggerSpec(TriggerType.Always, false, default, 0, 0);
            }
        }
    }

    /// <summary>Global effect-rule constants from Data/balance.json (#effects, #field).</summary>
    /// <remarks>
    /// REQ: docs/design/derived-stats/balance-keys.md. Pre: see constructor. Post: immutable. Side effects: none.
    /// </remarks>
    public sealed class EffectRulesConfig
    {
        /// <summary>Validates and stores the constants.</summary>
        /// <remarks>
        /// Pre: 0 &lt;= multiplierMinPercent &lt;= 100 &lt;= multiplierMaxPercent &lt;= 1000; 1 &lt;= fieldTickMs &lt;= 600000.
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public EffectRulesConfig(int multiplierMinPercent, int multiplierMaxPercent, int fieldTickMs)
        {
            Contract.Requires(multiplierMinPercent >= 0 && multiplierMinPercent <= 100, "multiplierMinPercent must be 0..100");
            Contract.Requires(multiplierMaxPercent >= 100 && multiplierMaxPercent <= 1000, "multiplierMaxPercent must be 100..1000");
            Contract.Requires(fieldTickMs >= 1 && fieldTickMs <= EffectBook.MaxDurationMs, "fieldTickMs must be 1..600000");
            MultiplierMinPercent = multiplierMinPercent;
            MultiplierMaxPercent = multiplierMaxPercent;
            FieldTickMs = fieldTickMs;
        }

        /// <summary>Lowest multiplier (100 + Σ delta) a relative-percent modifier can reach.</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int MultiplierMinPercent { get; }

        /// <summary>Highest multiplier (100 + Σ delta) a relative-percent modifier can reach.</summary>
        /// <remarks>Pre: none. Post: 100..1000. Side effects: none.</remarks>
        public int MultiplierMaxPercent { get; }

        /// <summary>Interval between field resolutions (Q-C17).</summary>
        /// <remarks>Pre: none. Post: 1..600000. Side effects: none.</remarks>
        public int FieldTickMs { get; }
    }

    /// <summary>Summed modifiers active on one combatant at one instant. Reused buffer; fill with <see cref="EffectBook.Collect"/>.</summary>
    /// <remarks>
    /// Sums are Int64: at most MaxStacksLimit × 100 per slot, so overflow would need ~10^15 slots.
    /// Pre: none. Post: all sums 0 after <see cref="Clear"/>. Side effects: none beyond its own fields.
    /// </remarks>
    public sealed class ModifierTotals
    {
        private const int ElementCount = 10;
        private readonly long[] _mastery = new long[ElementCount];
        private readonly long[] _resist = new long[ElementCount];
        private readonly long[] _cd = new long[ElementCount];
        private long _cdAny;

        /// <summary>Σ evasion_mod_percent deltas.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public long EvasionPercent { get; private set; }

        /// <summary>Σ move_speed_mod_percent deltas.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public long MoveSpeedPercent { get; private set; }

        /// <summary>Σ range_mod_percent deltas.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public long RangePercent { get; private set; }

        /// <summary>Σ mastery_mod deltas (percentage points) for <paramref name="element"/>.</summary>
        /// <remarks>Pre: element defined. Post: none. Side effects: none.</remarks>
        public long MasteryDelta(Element element) => _mastery[Index(element)];

        /// <summary>Σ resist_mod deltas (percentage points) for <paramref name="element"/>.</summary>
        /// <remarks>Pre: element defined. Post: none. Side effects: none.</remarks>
        public long ResistDelta(Element element) => _resist[Index(element)];

        /// <summary>Σ cd_mod_percent deltas that apply to a move of <paramref name="moveElement"/> (its element plus 'any').</summary>
        /// <remarks>Pre: moveElement defined. Post: none. Side effects: none.</remarks>
        public long CdPercent(Element moveElement) => _cdAny + _cd[Index(moveElement)];

        /// <summary>Resets every sum to 0.</summary>
        /// <remarks>Pre: none. Post: all sums 0. Side effects: mutates this buffer.</remarks>
        public void Clear()
        {
            for (int i = 0; i < ElementCount; i++)
            {
                _mastery[i] = 0;
                _resist[i] = 0;
                _cd[i] = 0;
            }

            _cdAny = 0;
            EvasionPercent = 0;
            MoveSpeedPercent = 0;
            RangePercent = 0;
        }

        internal void Add(in EffectSpec spec, int stacks)
        {
            long amount = (long)spec.Delta * stacks;
            switch (spec.Type)
            {
                case EffectType.MasteryMod:
                    _mastery[Index(spec.Element)] += amount;
                    break;
                case EffectType.ResistMod:
                    _resist[Index(spec.Element)] += amount;
                    break;
                case EffectType.CdModPercent:
                    if (spec.AnyElement)
                    {
                        _cdAny += amount;
                    }
                    else
                    {
                        _cd[Index(spec.Element)] += amount;
                    }

                    break;
                case EffectType.EvasionModPercent:
                    EvasionPercent += amount;
                    break;
                case EffectType.MoveSpeedModPercent:
                    MoveSpeedPercent += amount;
                    break;
                case EffectType.RangeModPercent:
                    RangePercent += amount;
                    break;
            }
        }

        private static int Index(Element element)
        {
            Contract.Requires(element >= Element.Metal && element <= Element.Shadow, "undefined Element");
            return (int)element;
        }
    }

    /// <summary>Outcome of an <see cref="EffectBook.Apply"/>.</summary>
    /// <remarks>Pre: none. Post: values are stable (part of the replay/net contract). Side effects: none.</remarks>
    public enum ApplyOutcome
    {
        /// <summary>No live slot existed (or it had expired); the effect starts at 1 stack.</summary>
        /// <remarks>Pre: none. Post: constant 0. Side effects: none.</remarks>
        Started = 0,
        /// <summary>A live slot gained a stack and its window was refreshed.</summary>
        /// <remarks>Pre: none. Post: constant 1. Side effects: none.</remarks>
        Stacked = 1,
        /// <summary>A live slot was already at MaxStacks; only its window was refreshed.</summary>
        /// <remarks>Pre: none. Post: constant 2. Side effects: none.</remarks>
        Refreshed = 2,
    }

    /// <summary>
    /// Timed effects on one combatant, plus its trait. Stacking key = (source id, effect index): the same move or trait
    /// refreshes / stacks no matter who cast it; different sources always add up.
    /// REQ: docs/design/combat.md#效果疊加, #特性觸發 (user decisions 2026-11-06).
    /// </summary>
    /// <remarks>
    /// Windows are half-open [applied, applied + duration). A re-application inside the window adds one stack up to
    /// MaxStacks and restarts the window for the whole stack (so a stack never decays piecemeal). Continuous trait
    /// triggers (always, on_hp_below/above) are not stored: <see cref="Collect"/> tests them against the HP it is given.
    /// Time watermark as in <see cref="TeamState"/>: commands (Apply / Notify* / Clear) record nowMs; every call taking
    /// nowMs requires nowMs >= the watermark; Collect never moves it.
    /// Allocation: slots live in a pre-sized list; Apply only allocates when more distinct sources are live than ever before.
    /// </remarks>
    public sealed class EffectBook
    {
        /// <summary>Longest effect or trigger window. Mirrors common.schema.json#/$defs/durationMs.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxDurationMs = 600_000;

        private struct Slot
        {
            public string SourceId;
            public int Index;
            public EffectSpec Spec;
            public int Stacks;
            public long ExpiresMs;
        }

        private readonly List<Slot> _slots = new List<Slot>(16);
        private readonly string _traitId;
        private readonly TriggerSpec _trigger;
        private readonly EffectSpec[] _traitEffects;
        private long _watermarkMs;

        /// <summary>Creates a book for a combatant without a trait.</summary>
        /// <remarks>Pre: none. Post: HasTrait false; no active effects. Side effects: none.</remarks>
        public EffectBook()
        {
            _traitEffects = new EffectSpec[0];
        }

        /// <summary>Creates a book for a combatant whose trait is <paramref name="traitId"/>.</summary>
        /// <remarks>
        /// Pre: traitId non-empty; traitEffects non-null with 1..TraitDef.MaxEffects entries, none instant unless the trigger is an event
        ///      trigger (a heal on a continuous trigger would have to fire every query).
        /// Post: HasTrait true; no active effects. Side effects: copies traitEffects.
        /// </remarks>
        public EffectBook(string traitId, in TriggerSpec trigger, IReadOnlyList<EffectSpec> traitEffects)
        {
            Contract.Requires(!string.IsNullOrEmpty(traitId), "traitId must be non-empty");
            Contract.Requires(traitEffects != null && traitEffects.Count >= 1 && traitEffects.Count <= TraitDef.MaxEffects,
                "a trait has 1..TraitDef.MaxEffects effects");
            _traitId = traitId;
            _trigger = trigger;
            _traitEffects = new EffectSpec[traitEffects.Count];
            for (int i = 0; i < traitEffects.Count; i++)
            {
                Contract.Requires(!(traitEffects[i].IsInstant && trigger.IsContinuous),
                    "a heal needs an event trigger");
                _traitEffects[i] = traitEffects[i];
            }
        }

        /// <summary>Whether this combatant has a trait.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool HasTrait => _traitId != null;

        /// <summary>
        /// Applies a timed effect from <paramref name="sourceId"/> (move or trait id) at <paramref name="nowMs"/>.
        /// </summary>
        /// <remarks>
        /// Pre: sourceId non-empty; effectIndex >= 0; !spec.IsInstant (heals resolve via <see cref="EffectMath.HealAmount"/>);
        ///      1 &lt;= durationMs &lt;= MaxDurationMs; nowMs in CombatTime and >= watermark.
        /// Post: the slot (sourceId, effectIndex) is live until nowMs + durationMs with
        ///       stacks = min(previous live stacks + 1, spec.MaxStacks), or 1 when no live slot existed.
        /// Side effects: records the watermark; may drop expired slots; may grow the slot list.
        /// </remarks>
        public ApplyOutcome Apply(string sourceId, int effectIndex, in EffectSpec spec, int durationMs, long nowMs)
        {
            Contract.Requires(!string.IsNullOrEmpty(sourceId), "sourceId must be non-empty");
            Contract.Requires(effectIndex >= 0, "effectIndex must be nonnegative");
            Contract.Requires(!spec.IsInstant, "instant effects are not stored");
            Contract.Requires(spec.MaxStacks >= 1, "spec must come from the EffectSpec constructor (default has MaxStacks 0)");
            Contract.Requires(durationMs >= 1 && durationMs <= MaxDurationMs, "durationMs must be 1..MaxDurationMs");
            Advance(nowMs);

            long expires = nowMs + durationMs;
            for (int i = _slots.Count - 1; i >= 0; i--)
            {
                Slot s = _slots[i];
                if (s.ExpiresMs <= nowMs)
                {
                    _slots.RemoveAt(i);
                    continue;
                }

                if (s.Index != effectIndex || s.SourceId != sourceId)
                {
                    continue;
                }

                ApplyOutcome outcome = s.Stacks < spec.MaxStacks ? ApplyOutcome.Stacked : ApplyOutcome.Refreshed;
                // A data edit between applications can lower MaxStacks; clamp instead of keeping an illegal stack.
                s.Stacks = s.Stacks < spec.MaxStacks ? s.Stacks + 1 : spec.MaxStacks;
                s.Spec = spec;
                s.ExpiresMs = expires;
                _slots[i] = s;
                return outcome;
            }

            _slots.Add(new Slot { SourceId = sourceId, Index = effectIndex, Spec = spec, Stacks = 1, ExpiresMs = expires });
            return ApplyOutcome.Started;
        }

        /// <summary>Current stacks of (sourceId, effectIndex) at <paramref name="nowMs"/>; 0 when absent or expired.</summary>
        /// <remarks>Pre: sourceId non-null; nowMs in CombatTime and >= watermark. Post: 0..MaxStacksLimit. Side effects: none.</remarks>
        public int StacksOf(string sourceId, int effectIndex, long nowMs)
        {
            Contract.Requires(sourceId != null, "sourceId must not be null");
            CombatTime.Require(nowMs, _watermarkMs);
            foreach (Slot s in _slots)
            {
                if (s.ExpiresMs > nowMs && s.Index == effectIndex && s.SourceId == sourceId)
                {
                    return s.Stacks;
                }
            }

            return 0;
        }

        /// <summary>The creature entered the field (initial summon or switch-in): fires on_summon.</summary>
        /// <remarks>
        /// REQ: combat.md#特性觸發 — every entry counts as a summon. Pre: nowMs in CombatTime and >= watermark.
        /// Post: if the trait is on_summon, its effects are applied with the trigger's window.
        /// Side effects: records the watermark. Instant trait effects are returned through <paramref name="healPercent"/>.
        /// </remarks>
        public void NotifySummoned(long nowMs, out int healPercent)
        {
            Fire(TriggerType.OnSummon, true, nowMs, out healPercent);
        }

        /// <summary>The creature entered the field by a switch (not the opening summon): fires on_ally_swap.</summary>
        /// <remarks>
        /// REQ: combat.md#特性觸發 (user decision 2026-11-06: the creature swapped in). Call in addition to
        /// <see cref="NotifySummoned"/>. Pre/Post/Side effects: as NotifySummoned for on_ally_swap.
        /// </remarks>
        public void NotifySwappedIn(long nowMs, out int healPercent)
        {
            Fire(TriggerType.OnAllySwap, true, nowMs, out healPercent);
        }

        /// <summary>The creature was hit (contact + roll succeeded) by a move of <paramref name="moveElement"/>: fires on_hit_taken.</summary>
        /// <remarks>
        /// Pre: moveElement defined; nowMs in CombatTime and >= watermark. Post: fires iff trigger is any or matches.
        /// Side effects: records the watermark.
        /// </remarks>
        public void NotifyHitTaken(Element moveElement, long nowMs, out int healPercent)
        {
            // Checked before matching: an 'any' trait would otherwise fire for an undefined element (SOL P2-09 E3).
            Contract.Requires(moveElement >= Element.Metal && moveElement <= Element.Shadow, "undefined Element");
            bool match = _traitId != null && _trigger.Type == TriggerType.OnHitTaken
                && (_trigger.AnyElement || _trigger.Element == moveElement);
            Fire(TriggerType.OnHitTaken, match, nowMs, out healPercent);
        }

        /// <summary>The creature used a move of <paramref name="moveElement"/>: fires on_move_used for that element.</summary>
        /// <remarks>Pre/Post/Side effects: as NotifyHitTaken (no 'any' scope).</remarks>
        public void NotifyMoveUsed(Element moveElement, long nowMs, out int healPercent)
        {
            Contract.Requires(moveElement >= Element.Metal && moveElement <= Element.Shadow, "undefined Element");
            bool match = _traitId != null && _trigger.Type == TriggerType.OnMoveUsed && _trigger.Element == moveElement;
            Fire(TriggerType.OnMoveUsed, match, nowMs, out healPercent);
        }

        /// <summary>Removes every timed effect (end of combat).</summary>
        /// <remarks>Pre: nowMs in CombatTime and >= watermark. Post: no live slots. Side effects: records the watermark.</remarks>
        public void Clear(long nowMs)
        {
            Advance(nowMs);
            _slots.Clear();
        }

        /// <summary>Sums every modifier active at <paramref name="nowMs"/> into <paramref name="into"/>.</summary>
        /// <remarks>
        /// on_hp_below{p} holds iff hp·100 &lt; p·maxHp, on_hp_above{p} iff hp·100 > p·maxHp (strict, exact integers).
        /// Pre: into != null; 1 &lt;= maxHp; 0 &lt;= hp &lt;= maxHp; nowMs in CombatTime and >= watermark.
        /// Post: into holds Σ delta × stacks over live slots plus continuous trait effects; previous contents discarded.
        /// Side effects: overwrites into only.
        /// </remarks>
        public void Collect(long nowMs, int hp, int maxHp, ModifierTotals into)
        {
            Contract.Requires(into != null, "into must not be null");
            Contract.Requires(maxHp >= 1 && hp >= 0 && hp <= maxHp, "need 0 <= hp <= maxHp, maxHp >= 1");
            CombatTime.Require(nowMs, _watermarkMs);
            into.Clear();
            foreach (Slot s in _slots)
            {
                if (s.ExpiresMs > nowMs)
                {
                    into.Add(s.Spec, s.Stacks);
                }
            }

            if (_traitId == null || !ContinuousHolds(hp, maxHp))
            {
                return;
            }

            foreach (EffectSpec e in _traitEffects)
            {
                into.Add(e, 1);
            }
        }

        private bool ContinuousHolds(int hp, int maxHp)
        {
            long scaledHp = (long)hp * 100;
            switch (_trigger.Type)
            {
                case TriggerType.Always:
                    return true;
                case TriggerType.OnHpBelow:
                    return scaledHp < (long)_trigger.Percent * maxHp;
                case TriggerType.OnHpAbove:
                    return scaledHp > (long)_trigger.Percent * maxHp;
                default:
                    return false;
            }
        }

        private void Fire(TriggerType type, bool match, long nowMs, out int healPercent)
        {
            Advance(nowMs);
            healPercent = 0;
            if (_traitId == null || _trigger.Type != type || !match)
            {
                return;
            }

            for (int i = 0; i < _traitEffects.Length; i++)
            {
                if (_traitEffects[i].IsInstant)
                {
                    healPercent += _traitEffects[i].Percent;
                }
                else
                {
                    Apply(_traitId, i, _traitEffects[i], _trigger.DurationMs, nowMs);
                }
            }
        }

        private void Advance(long nowMs)
        {
            CombatTime.Require(nowMs, _watermarkMs);
            _watermarkMs = nowMs;
        }
    }

    /// <summary>Applies summed modifiers to base and derived values. Pure.</summary>
    /// <remarks>
    /// REQ: docs/design/combat.md#效果疊加. Mastery/resist deltas are percentage points; cd/evasion/speed/range deltas are
    /// relative: all of one kind are summed, the multiplier 100 + Σ is clamped to the config band, applied once, and
    /// the derived value's own cap/floor is re-applied (user decision 2026-11-06). Summing first keeps the result
    /// independent of the order effects were applied in, which peers cannot agree on.
    /// </remarks>
    public static class EffectMath
    {
        /// <summary>Effective mastery percent for DamageModel.</summary>
        /// <remarks>
        /// Pre: baseMastery >= 0. Post: 0 &lt;= result &lt;= DamageModel.MaxMasteryPercent (combat.md: negative sums floor at 0).
        /// Side effects: none.
        /// </remarks>
        public static int EffectiveMastery(int baseMastery, long delta)
        {
            Contract.Requires(baseMastery >= 0, "baseMastery must be nonnegative");
            return SaturatingAdd(baseMastery, delta, 0, DamageModel.MaxMasteryPercent);
        }

        /// <summary>Effective resist percent for DamageModel.</summary>
        /// <remarks>Pre: none. Post: -ResistClamp &lt;= result &lt;= ResistClamp (DamageModel saturates there). Side effects: none.</remarks>
        public static int EffectiveResist(int baseResist, long delta)
        {
            return SaturatingAdd(baseResist, delta, -DamageModel.ResistClamp, DamageModel.ResistClamp);
        }

        /// <summary>The clamped multiplier percent for a relative-percent sum.</summary>
        /// <remarks>Pre: config != null. Post: config.MultiplierMinPercent..MultiplierMaxPercent. Side effects: none.</remarks>
        public static int Multiplier(long percentSum, EffectRulesConfig config)
        {
            Contract.Requires(config != null, "config must not be null");
            long m = percentSum > 1_000_000 ? 1_000_000 : percentSum < -1_000_000 ? -1_000_000 : percentSum;
            return (int)Clamp(100 + m, config.MultiplierMinPercent, config.MultiplierMaxPercent);
        }

        /// <summary>value × multiplier / 100, rounded half up.</summary>
        /// <remarks>
        /// Pre: value >= 0; config != null. Post: result >= 0; == value when percentSum == 0; non-decreasing in percentSum.
        /// Side effects: none.
        /// </remarks>
        public static int ApplyPercent(int value, long percentSum, EffectRulesConfig config)
        {
            Contract.Requires(value >= 0, "value must be nonnegative");
            long scaled = ((long)value * Multiplier(percentSum, config) + 50) / 100;
            return scaled > int.MaxValue ? int.MaxValue : (int)scaled;
        }

        /// <summary>Cooldown after SPI and cd_mod_percent.</summary>
        /// <remarks>
        /// Ceil like DerivedStats.EffectiveCooldownMs: a cooldown must never come ready earlier than the multiplier allows.
        /// Pre: stats, config non-null; baseCooldownMs >= 0; spi >= 0.
        /// Post: result >= CdFloorMs; == stats.EffectiveCooldownMs(base, spi) when percentSum == 0; non-decreasing in percentSum.
        /// Side effects: none.
        /// </remarks>
        public static int EffectiveCooldownMs(DerivedStats stats, int baseCooldownMs, int spi, long percentSum,
            EffectRulesConfig config)
        {
            Contract.Requires(stats != null, "stats must not be null");
            int derived = stats.EffectiveCooldownMs(baseCooldownMs, spi);
            long cd = ((long)derived * Multiplier(percentSum, config) + 99) / 100;
            int floor = stats.Coefficients.CdFloorMs;
            return cd < floor ? floor : cd > int.MaxValue ? int.MaxValue : (int)cd;
        }

        /// <summary>Evasion after AGI and evasion_mod_percent.</summary>
        /// <remarks>
        /// Pre: stats, config non-null; agi >= 0. Post: 0 &lt;= result &lt;= EvasionCapPermille; non-decreasing in percentSum.
        /// Side effects: none.
        /// </remarks>
        public static int EvasionPermille(DerivedStats stats, int agi, long percentSum, EffectRulesConfig config)
        {
            Contract.Requires(stats != null, "stats must not be null");
            int e = ApplyPercent(stats.EvasionPermille(agi), percentSum, config);
            int cap = stats.Coefficients.EvasionCapPermille;
            return e > cap ? cap : e;
        }

        /// <summary>Move speed after AGI and move_speed_mod_percent.</summary>
        /// <remarks>Pre: stats, config non-null; agi >= 0. Post: result >= 0; non-decreasing in percentSum. Side effects: none.</remarks>
        public static int MoveSpeedMmS(DerivedStats stats, int agi, long percentSum, EffectRulesConfig config)
        {
            Contract.Requires(stats != null, "stats must not be null");
            return ApplyPercent(stats.MoveSpeedMmS(agi), percentSum, config);
        }

        /// <summary>A hit volume with range_mod_percent applied to its reach and cast distance.</summary>
        /// <remarks>
        /// "Range" = how far the move reaches: Rect/Sector length and the cast distance of aim-point / ally moves. AoE
        /// radii, widths and angles keep their values, and a dash never scales (derived-stats.md). The result gets the
        /// same bound <see cref="MoveGeometry.Build"/> gave the unmodified value (combat.md: re-apply the original
        /// limits): mid/long ranges are capped at RangedCapMm (horizon bound), melee ranges are not (SOL P2-09 E2).
        /// Pre: stats, config non-null; rangeClass defined; volume came from MoveGeometry.Build with that rangeClass,
        ///      so for mid/long its range dimensions are &lt;= RangedCapMm — except a dash, whose path length is never
        ///      capped or scaled (SOL P2-09 r2 E8). Checked before any early return.
        /// Post: equals volume when percentSum == 0 or volume is a dash; Kind/Anchor/Width/Angle/Height/Dash unchanged;
        ///       range dimensions non-decreasing in percentSum and, for a non-dash mid/long volume, &lt;= RangedCapMm.
        /// Side effects: none.
        /// </remarks>
        public static HitVolume ApplyRange(in HitVolume volume, RangeClass rangeClass, long percentSum, DerivedStats stats,
            EffectRulesConfig config)
        {
            Contract.Requires(stats != null && config != null, "stats and config must not be null");
            Contract.Requires(rangeClass >= RangeClass.Melee && rangeClass <= RangeClass.Long, "undefined RangeClass");
            bool reachIsRange = volume.Kind == HitVolumeKind.Rect || volume.Kind == HitVolumeKind.Sector;
            bool dash = volume.DashDistanceMm > 0;
            int cap = rangeClass == RangeClass.Melee ? int.MaxValue : stats.Coefficients.RangedCapMm;
            Contract.Requires(volume.CastDistanceMm <= cap && (!reachIsRange || dash || volume.ReachMm <= cap),
                "a mid/long volume's range exceeds RangedCapMm; build it with MoveGeometry.Build");
            if (percentSum == 0 || dash)
            {
                return volume;
            }

            int reach = reachIsRange ? Capped(volume.ReachMm, percentSum, cap, config) : volume.ReachMm;
            int cast = Capped(volume.CastDistanceMm, percentSum, cap, config);
            return new HitVolume(volume.Kind, volume.Anchor, reach, volume.WidthMm, volume.HalfAngleDeciDeg,
                volume.HalfHeightMm, cast, volume.DashDistanceMm);
        }

        /// <summary>HP restored by a heal of <paramref name="percent"/> % of max HP, rounded half up.</summary>
        /// <remarks>
        /// Pre: maxHp >= 1; 0 &lt;= percent. Post: 0 &lt;= result; result >= 1 when percent >= 1. Caller clamps HP at maxHp.
        /// Side effects: none.
        /// </remarks>
        public static int HealAmount(int maxHp, int percent)
        {
            Contract.Requires(maxHp >= 1 && percent >= 0, "need maxHp >= 1 and percent >= 0");
            long heal = ((long)maxHp * percent + 50) / 100;
            if (percent > 0 && heal < 1)
            {
                heal = 1;
            }

            return heal > int.MaxValue ? int.MaxValue : (int)heal;
        }

        private static int Capped(int value, long percentSum, int cap, EffectRulesConfig config)
        {
            int scaled = ApplyPercent(value, percentSum, config);
            return scaled > cap ? cap : scaled;
        }

        // Compare against the headroom instead of adding first: base + long.MaxValue wraps negative and saturated the
        // wrong way (SOL P2-09 E1). value is an int, so lo - value and hi - value cannot overflow a long.
        private static int SaturatingAdd(int value, long delta, int lo, int hi)
        {
            if (delta >= (long)hi - value)
            {
                return hi;
            }

            if (delta <= (long)lo - value)
            {
                return lo;
            }

            return (int)(value + delta);
        }

        private static long Clamp(long v, long lo, long hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }

    /// <summary>When a placed field resolves. REQ: docs/design/combat.md#場地 (Q-C17, 2026-11-06).</summary>
    /// <remarks>
    /// The first resolution happens at placement, then every FieldTickMs while t &lt; placed + lifetime, so a field
    /// resolves ceil(lifetime / tick) times and each target at most once per resolution.
    /// Pre: constructor. Post: immutable. Side effects: none.
    /// </remarks>
    public readonly struct FieldTimeline
    {
        /// <summary>Creates a timeline.</summary>
        /// <remarks>
        /// Pre: placedMs in 0..CombatTime.MaxMs − lifetimeMs; 1 &lt;= lifetimeMs &lt;= EffectBook.MaxDurationMs;
        ///      1 &lt;= tickMs &lt;= EffectBook.MaxDurationMs.
        /// Post: properties equal the arguments. Side effects: none.
        /// </remarks>
        public FieldTimeline(long placedMs, int lifetimeMs, int tickMs)
        {
            Contract.Requires(lifetimeMs >= 1 && lifetimeMs <= EffectBook.MaxDurationMs, "lifetimeMs must be 1..600000");
            Contract.Requires(tickMs >= 1 && tickMs <= EffectBook.MaxDurationMs, "tickMs must be 1..600000");
            Contract.Requires(placedMs >= 0 && placedMs <= CombatTime.MaxMs - lifetimeMs, "placedMs out of CombatTime");
            PlacedMs = placedMs;
            LifetimeMs = lifetimeMs;
            TickMs = tickMs;
        }

        /// <summary>Placement time.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public long PlacedMs { get; }

        /// <summary>Lifetime (move field_duration_ms).</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public int LifetimeMs { get; }

        /// <summary>Interval between resolutions.</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public int TickMs { get; }

        /// <summary>Total resolutions over the field's life.</summary>
        /// <remarks>Pre: none. Post: ceil(LifetimeMs / TickMs) >= 1. Side effects: none.</remarks>
        public int TotalTicks => (LifetimeMs + TickMs - 1) / TickMs;

        /// <summary>Whether the field still exists at <paramref name="nowMs"/>.</summary>
        /// <remarks>Pre: none. Post: true iff PlacedMs &lt;= nowMs &lt; PlacedMs + LifetimeMs. Side effects: none.</remarks>
        public bool IsAlive(long nowMs) => nowMs >= PlacedMs && nowMs - PlacedMs < LifetimeMs;

        /// <summary>Number of resolutions due in the half-open interval (fromMs, toMs].</summary>
        /// <remarks>
        /// Simulation steps call this with (previous step, this step]; summing over consecutive steps never double-counts
        /// or skips, whatever the step size. Pass fromMs = PlacedMs − 1 on the placement step to include tick 0.
        /// Pre: -1 &lt;= fromMs &lt;= toMs &lt;= CombatTime.MaxMs. Post: 0..TotalTicks. Side effects: none.
        /// </remarks>
        public int TicksDue(long fromMs, long toMs)
        {
            Contract.Requires(fromMs >= -1 && fromMs <= toMs && toMs <= CombatTime.MaxMs, "need -1 <= fromMs <= toMs <= MaxMs");
            return CountUpTo(toMs) - CountUpTo(fromMs);
        }

        // Ticks k with PlacedMs + k·TickMs <= t, k in 0..TotalTicks-1.
        private int CountUpTo(long t)
        {
            if (t < PlacedMs)
            {
                return 0;
            }

            long k = (t - PlacedMs) / TickMs + 1;
            int total = TotalTicks;
            return k > total ? total : (int)k;
        }
    }
}
