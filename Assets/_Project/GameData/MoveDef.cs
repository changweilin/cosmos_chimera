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
        [SerializeField] private int cooldownMs;
        [SerializeField] private int accuracy;
        [SerializeField] private RangeClass rangeClass;
        [SerializeField] private MoveShape shape;

        /// <summary>Move element.</summary>
        /// <remarks>Pre: none. Post: a defined Element. Side effects: none.</remarks>
        public Element Element => element;

        /// <summary>Damage / effect / both.</summary>
        /// <remarks>Pre: none. Post: a defined MoveKind. Side effects: none.</remarks>
        public MoveKind Kind => kind;

        /// <summary>Base power.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Power => power;

        /// <summary>The effect rule, or null for a pure damage move.</summary>
        /// <remarks>
        /// Unity serializes a [Serializable] class field as an empty instance rather than null, so null-ness
        /// is carried by a separate flag. Pre: none. Post: null iff Kind == Damage. Side effects: none.
        /// </remarks>
        public EffectDef Effect => hasEffect ? effect : null;

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

        internal void Set(Element element, MoveKind kind, int power, EffectDef effect, int cooldownMs, int accuracy,
            RangeClass rangeClass, MoveShape shape)
        {
            this.element = element;
            this.kind = kind;
            this.power = power;
            hasEffect = effect != null;
            this.effect = effect;
            this.cooldownMs = cooldownMs;
            this.accuracy = accuracy;
            this.rangeClass = rangeClass;
            this.shape = shape;
        }
    }
}
