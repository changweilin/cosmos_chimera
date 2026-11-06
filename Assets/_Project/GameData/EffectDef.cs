using System;
using Cosmimera.Core.Contracts;
using UnityEngine;

namespace Cosmimera.GameData
{
    /// <summary>Typed effect, shared by trait effects and move effects. REQ: common.schema.json#/$defs/effect.</summary>
    /// <remarks>
    /// Same accessor discipline as <see cref="TriggerDef"/>: reading a parameter the type does not carry is a
    /// contract violation. Units: Delta is percentage points for mastery/resist, and a relative percent of the
    /// derived value for cd/evasion/speed/range (user decision 2026-11-06); application and clamping live in
    /// Combat's EffectMath (P2-09).
    /// Pre: filled by the importer, which has checked params against the type.
    /// Post: exactly the parameters listed for <see cref="Type"/> are meaningful.
    /// Side effects: none.
    /// </remarks>
    [Serializable]
    public sealed class EffectDef
    {
        [SerializeField] private EffectType type;
        [SerializeField] private bool anyElement;
        [SerializeField] private Element element;
        [SerializeField] private int delta;
        [SerializeField] private int percent;
        [SerializeField] private int maxStacks;

        /// <summary>Largest accepted <see cref="MaxStacks"/>. Mirrors common.schema.json#/$defs/effect.max_stacks.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none.</remarks>
        public const int MaxStacksLimit = 10;

        internal EffectDef(EffectType type, bool anyElement, Element element, int delta, int percent, int maxStacks)
        {
            this.maxStacks = maxStacks;
            this.type = type;
            this.anyElement = anyElement;
            this.element = element;
            this.delta = delta;
            this.percent = percent;
        }

        /// <summary>The effect type.</summary>
        /// <remarks>Pre: none. Post: a defined EffectType. Side effects: none.</remarks>
        public EffectType Type => type;

        /// <summary>True when this type is scoped to an element (MasteryMod, ResistMod, CdModPercent).</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool HasElement => type == EffectType.MasteryMod || type == EffectType.ResistMod
            || type == EffectType.CdModPercent;

        /// <summary>True when the scope is every element ("any").</summary>
        /// <remarks>Pre: HasElement. Post: true only possible for CdModPercent. Side effects: none.</remarks>
        public bool AnyElement
        {
            get
            {
                Contract.Requires(HasElement, $"{type} has no element parameter");
                return anyElement;
            }
        }

        /// <summary>The element the effect applies to.</summary>
        /// <remarks>Pre: HasElement and !AnyElement. Post: a defined Element. Side effects: none.</remarks>
        public Element Element
        {
            get
            {
                Contract.Requires(HasElement && !anyElement, $"{type} has no concrete element parameter");
                return element;
            }
        }

        /// <summary>Signed modifier.</summary>
        /// <remarks>Pre: Type != HealPercentMaxHp. Post: -100..100. Side effects: none.</remarks>
        public int Delta
        {
            get
            {
                Contract.Requires(type != EffectType.HealPercentMaxHp, $"{type} has no delta parameter");
                return delta;
            }
        }

        /// <summary>How many applications from the same move/trait id can stack on one target; 1 = refresh only.</summary>
        /// <remarks>
        /// REQ: docs/design/combat.md#效果疊加. Pre: none. Post: 1..MaxStacksLimit; 1 for HealPercentMaxHp.
        /// Side effects: none.
        /// </remarks>
        public int MaxStacks => maxStacks;

        /// <summary>Heal amount as percent of max HP.</summary>
        /// <remarks>Pre: Type == HealPercentMaxHp. Post: 1..100. Side effects: none.</remarks>
        public int Percent
        {
            get
            {
                Contract.Requires(type == EffectType.HealPercentMaxHp, $"{type} has no percent parameter");
                return percent;
            }
        }
    }
}
