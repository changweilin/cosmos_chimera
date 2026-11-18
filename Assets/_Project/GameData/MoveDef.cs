using Cosmimera.Core.Contracts;
using UnityEngine;

namespace Cosmimera.GameData
{
    /// <summary>Imported move definition. REQ: docs/design/creatures.md#主動技, Data/schemas/move.schema.json.</summary>
    /// <remarks>
    /// Pre: filled by the importer. Post: <see cref="Effect"/> is null iff <see cref="Kind"/> is Damage.
    /// Side effects: none.
    /// </remarks>
    public sealed class MoveDef : GameDef
    {
        [SerializeField] private Element element;
        [SerializeField] private MoveKind kind;
        [SerializeField] private int power;
        [SerializeField] private bool hasEffect;
        [SerializeField] private EffectDef effect;
        [SerializeField] private int effectDurationMs;
        [SerializeField] private int fieldDurationMs;
        [SerializeField] private int cooldownMs;
        [SerializeField] private int windupMs;
        [SerializeField] private int accuracy;
        [SerializeField] private RangeClass rangeClass;
        [SerializeField] private MoveShape shape;
        [SerializeField] private int baseRangeMm;
        [SerializeField] private MoveMovement movement;
        [SerializeField] private BurstOrigin origin;
        // Initialized to ★ so fixtures that never call SetSkill stay valid; the importer always sets both.
        [SerializeField] private int star = 1;
        [SerializeField] private Polarity polarity;
        // ★ version (creatures.md#技能星級): -1 / false = keep the full version's value.
        [SerializeField] private bool hasDowngraded;
        [SerializeField] private int downPower = -1;
        [SerializeField] private int downCooldownMs = -1;
        [SerializeField] private int downAccuracy = -1;
        [SerializeField] private bool downHasEffect;
        [SerializeField] private EffectDef downEffect;
        [SerializeField] private int downEffectDurationMs = -1;

        /// <summary>Skill star / rarity: 1 一般, 2 稀有, 3 獨有. REQ: docs/design/creatures.md#技能星級.</summary>
        /// <remarks>Pre: none. Post: 1..3. Side effects: none.</remarks>
        public int Star => star;

        /// <summary>Damage polarity: picks the attacker's 陰攻/陽攻 and the target's 陰守/陽守 (combat.md#傷害公式).</summary>
        /// <remarks>Pre: none. Post: a defined Polarity; meaningless (default Yin) when Kind == Effect. Side effects: none.</remarks>
        public Polarity Polarity => polarity;

        /// <summary>Whether a ★ version is stored (iff Star >= 2 for imported data).</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool HasDowngraded => hasDowngraded;

        /// <summary>Power as used when <paramref name="downgraded"/> (partner-provided) or not.</summary>
        /// <remarks>Pre: none. Post: 0..100000. Side effects: none.</remarks>
        public int PowerFor(bool downgraded) => downgraded && hasDowngraded && downPower >= 0 ? downPower : power;

        /// <summary>Cooldown as used when <paramref name="downgraded"/> or not.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int CooldownMsFor(bool downgraded) =>
            downgraded && hasDowngraded && downCooldownMs >= 0 ? downCooldownMs : cooldownMs;

        /// <summary>Accuracy as used when <paramref name="downgraded"/> or not.</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int AccuracyFor(bool downgraded) => downgraded && hasDowngraded && downAccuracy >= 0 ? downAccuracy : accuracy;

        /// <summary>Effect as used when <paramref name="downgraded"/> or not.</summary>
        /// <remarks>Pre: none. Post: null iff Kind == Damage (a ★ version only replaces an existing effect). Side effects: none.</remarks>
        public EffectDef EffectFor(bool downgraded) => downgraded && hasDowngraded && downHasEffect ? downEffect : Effect;

        /// <summary>Effect window as used when <paramref name="downgraded"/> or not.</summary>
        /// <remarks>Pre: HasEffectDuration. Post: 1..600000. Side effects: none.</remarks>
        public int EffectDurationMsFor(bool downgraded) =>
            downgraded && hasDowngraded && downEffectDurationMs >= 0 ? downEffectDurationMs : EffectDurationMs;

        /// <summary>Move element.</summary>
        /// <remarks>Pre: none. Post: a defined Element. Side effects: none.</remarks>
        public Element Element => element;

        /// <summary>Damage / effect / both.</summary>
        /// <remarks>Pre: none. Post: a defined MoveKind. Side effects: none.</remarks>
        public MoveKind Kind => kind;

        /// <summary>Base power.</summary>
        /// <remarks>Pre: none. Post: 0..100000, matching DamageModel's representability bound. Side effects: none.</remarks>
        public int Power => power;

        /// <summary>The effect rule, or null for a pure damage move.</summary>
        /// <remarks>
        /// Unity serializes a [Serializable] class field as an empty instance rather than null, so null-ness
        /// is carried by a separate flag. Pre: none. Post: null iff Kind == Damage. Side effects: none.
        /// </remarks>
        public EffectDef Effect => hasEffect ? effect : null;

        /// <summary>Whether <see cref="Effect"/> lasts for a window rather than resolving instantly.</summary>
        /// <remarks>Pre: none. Post: true iff Effect is non-null and not HealPercentMaxHp. Side effects: none.</remarks>
        public bool HasEffectDuration => hasEffect && effect.Type != EffectType.HealPercentMaxHp;

        /// <summary>How long the effect lasts on each target it lands on, in milliseconds.</summary>
        /// <remarks>
        /// REQ: docs/design/combat.md#主動技效果. Pre: HasEffectDuration. Post: 1..600000. Side effects: none.
        /// </remarks>
        public int EffectDurationMs
        {
            get
            {
                Contract.Requires(HasEffectDuration, "only timed (non-heal) move effects have a duration");
                return effectDurationMs;
            }
        }

        /// <summary>Lifetime of the placed field in milliseconds.</summary>
        /// <remarks>
        /// REQ: docs/design/combat.md#場地 (Q-C17). Pre: Shape == Field. Post: 1..600000. Side effects: none.
        /// </remarks>
        public int FieldDurationMs
        {
            get
            {
                Contract.Requires(shape == MoveShape.Field, "only field moves have a field duration");
                return fieldDurationMs;
            }
        }

        /// <summary>Cooldown in milliseconds.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int CooldownMs => cooldownMs;

        /// <summary>Longest windup a move may declare, in milliseconds; must match move.schema.json.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxWindupMs = 10000;

        /// <summary>Delay from an accepted cast to hit resolution, before the boss scale.</summary>
        /// <remarks>REQ: docs/design/combat.md#前搖 (Q-C45). Pre: none. Post: 0..MaxWindupMs. Side effects: none.</remarks>
        public int WindupMs => windupMs;

        /// <summary>Accuracy percent.</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int Accuracy => accuracy;

        /// <summary>Range band.</summary>
        /// <remarks>Pre: none. Post: a defined RangeClass. Side effects: none.</remarks>
        public RangeClass RangeClass => rangeClass;

        /// <summary>Hit shape.</summary>
        /// <remarks>Pre: none. Post: a defined MoveShape. Side effects: none.</remarks>
        public MoveShape Shape => shape;

        /// <summary>Longest dash path a move may declare, in millimeters.</summary>
        /// <remarks>
        /// Pre: none. Post: constant. Side effects: none.
        /// REQ: docs/design/combat.md#判定體 (Q-C19, user decision 2026-11-07): same as the 36 m ranged cap; must match
        /// move.schema.json's dash branch.
        /// </remarks>
        public const int MaxDashRangeMm = 36000;

        /// <summary>Base geometric range in millimeters, before STR scaling; dash path length for a dash.</summary>
        /// <remarks>Pre: none. Post: nonnegative; 1..MaxDashRangeMm when Movement is Dash. Side effects: none.</remarks>
        public int BaseRangeMm => baseRangeMm;

        /// <summary>Movement accompanying this move.</summary>
        /// <remarks>Pre: none. Post: a defined MoveMovement; Dash implies Shape is Line. Side effects: none.</remarks>
        public MoveMovement Movement => movement;

        /// <summary>Where the burst is centered.</summary>
        /// <remarks>Pre: Shape is Burst. Post: a defined BurstOrigin. Side effects: none.</remarks>
        public BurstOrigin Origin
        {
            get
            {
                Contract.Requires(shape == MoveShape.Burst, "only burst moves have an origin");
                return origin;
            }
        }

        internal void Set(Element element, MoveKind kind, int power, EffectDef effect, int effectDurationMs, int fieldDurationMs, int cooldownMs, int accuracy,
            RangeClass rangeClass, MoveShape shape, int baseRangeMm, MoveMovement movement, BurstOrigin origin)
        {
            this.element = element;
            this.kind = kind;
            this.power = power;
            hasEffect = effect != null;
            this.effect = effect;
            this.effectDurationMs = effectDurationMs;
            this.fieldDurationMs = fieldDurationMs;
            this.cooldownMs = cooldownMs;
            this.accuracy = accuracy;
            this.rangeClass = rangeClass;
            this.shape = shape;
            this.baseRangeMm = baseRangeMm;
            this.movement = movement;
            this.origin = origin;
        }

        // Separate from Set so the many fixtures keep compiling; REQ: creatures.md#技能星級, #六大能力值.
        internal void SetSkill(int star, Polarity polarity)
        {
            Contract.Requires(star >= 1 && star <= 3, "star must be 1..3");
            this.star = star;
            this.polarity = polarity;
        }

        // Each argument is the ★ value or -1 / null to keep the full version's; at least one must be given.
        internal void SetDowngraded(int power, int cooldownMs, int accuracy, EffectDef effect, int effectDurationMs)
        {
            Contract.Requires(power >= 0 || cooldownMs >= 0 || accuracy >= 0 || effect != null || effectDurationMs >= 0,
                "a ★ version must change something");
            Contract.Requires(effect == null || hasEffect, "a ★ version only replaces an existing effect");
            hasDowngraded = true;
            downPower = power;
            downCooldownMs = cooldownMs;
            downAccuracy = accuracy;
            downHasEffect = effect != null;
            downEffect = effect;
            downEffectDurationMs = effectDurationMs;
        }

        // Separate from Set so the many test fixtures that build instant moves keep compiling with windup 0.
        internal void SetWindup(int windupMs)
        {
            Contract.Requires(windupMs >= 0 && windupMs <= MaxWindupMs, "windup_ms must be 0..MaxWindupMs");
            this.windupMs = windupMs;
        }
    }
}
