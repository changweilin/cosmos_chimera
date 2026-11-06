using Cosmimera.Core.Contracts;
using UnityEngine;

namespace Cosmimera.GameData
{
    /// <summary>Imported move definition. REQ: docs/design/creatures.md#招式, Data/schemas/move.schema.json.</summary>
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
        [SerializeField] private int cooldownMs;
        [SerializeField] private int accuracy;
        [SerializeField] private RangeClass rangeClass;
        [SerializeField] private MoveShape shape;
        [SerializeField] private int baseRangeMm;
        [SerializeField] private MoveMovement movement;
        [SerializeField] private BurstOrigin origin;

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
        /// REQ: docs/design/combat.md#招式效果. Pre: HasEffectDuration. Post: 1..600000. Side effects: none.
        /// </remarks>
        public int EffectDurationMs
        {
            get
            {
                Contract.Requires(HasEffectDuration, "only timed (non-heal) move effects have a duration");
                return effectDurationMs;
            }
        }

        /// <summary>Cooldown in milliseconds.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int CooldownMs => cooldownMs;

        /// <summary>Accuracy percent.</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int Accuracy => accuracy;

        /// <summary>Range band.</summary>
        /// <remarks>Pre: none. Post: a defined RangeClass. Side effects: none.</remarks>
        public RangeClass RangeClass => rangeClass;

        /// <summary>Hit shape.</summary>
        /// <remarks>Pre: none. Post: a defined MoveShape. Side effects: none.</remarks>
        public MoveShape Shape => shape;

        /// <summary>Base geometric range in millimeters, before STR scaling; dash path length for a dash.</summary>
        /// <remarks>Pre: none. Post: nonnegative; positive when Movement is Dash. Side effects: none.</remarks>
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

        internal void Set(Element element, MoveKind kind, int power, EffectDef effect, int effectDurationMs, int cooldownMs, int accuracy,
            RangeClass rangeClass, MoveShape shape, int baseRangeMm, MoveMovement movement, BurstOrigin origin)
        {
            this.element = element;
            this.kind = kind;
            this.power = power;
            hasEffect = effect != null;
            this.effect = effect;
            this.effectDurationMs = effectDurationMs;
            this.cooldownMs = cooldownMs;
            this.accuracy = accuracy;
            this.rangeClass = rangeClass;
            this.shape = shape;
            this.baseRangeMm = baseRangeMm;
            this.movement = movement;
            this.origin = origin;
        }
    }
}
