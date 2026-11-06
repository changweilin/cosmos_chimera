using System;
using Cosmimera.Core.Contracts;
using UnityEngine;

namespace Cosmimera.GameData
{
    /// <summary>Typed effect, shared by trait effects and move effects. REQ: common.schema.json#/$defs/effect.</summary>
    /// <remarks>
    /// Same accessor discipline as <see cref="TriggerDef"/>: reading a parameter the type does not carry is a
    /// contract violation. Units: Delta is percent (mastery/resist/cd/speed/range) or percentage points
    /// (evasion); how each is applied and clamped is owned by P2-09 / P2-02.
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

        internal EffectDef(EffectType type, bool anyElement, Element element, int delta, int percent)
        {
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
