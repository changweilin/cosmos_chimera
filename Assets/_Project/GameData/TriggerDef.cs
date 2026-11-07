using System;
using Cosmimera.Core.Contracts;
using UnityEngine;

namespace Cosmimera.GameData
{
    /// <summary>Typed trait trigger. REQ: common.schema.json#/$defs/trigger, docs/design/creatures.md#特性.</summary>
    /// <remarks>
    /// Which parameters exist depends on <see cref="Type"/>; reading one that the type does not carry is a
    /// contract violation rather than a silent 0, so a handler written against the wrong type fails in tests.
    /// What the parameters mean at runtime (edge vs level triggering, stacking) is owned by P2-09.
    /// Pre: filled by the importer, which has checked params against the type.
    /// Post: exactly the parameters listed for <see cref="Type"/> are meaningful.
    /// Side effects: none.
    /// </remarks>
    [Serializable]
    public sealed class TriggerDef
    {
        [SerializeField] private TriggerType type;
        [SerializeField] private bool anyElement;
        [SerializeField] private Element element;
        [SerializeField] private int percent;
        [SerializeField] private int durationMs;

        internal TriggerDef(TriggerType type, bool anyElement, Element element, int percent, int durationMs)
        {
            this.type = type;
            this.anyElement = anyElement;
            this.element = element;
            this.percent = percent;
            this.durationMs = durationMs;
        }

        /// <summary>The trigger type.</summary>
        /// <remarks>Pre: none. Post: a defined TriggerType. Side effects: none.</remarks>
        public TriggerType Type => type;

        /// <summary>True when this type carries an element filter (OnHitTaken, OnMoveUsed).</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool HasElement => type == TriggerType.OnHitTaken || type == TriggerType.OnMoveUsed;

        /// <summary>True when the element filter matches every element ("any").</summary>
        /// <remarks>Pre: HasElement. Post: false for OnMoveUsed (schema allows only a concrete element). Side effects: none.</remarks>
        public bool AnyElement
        {
            get
            {
                Contract.Requires(HasElement, $"{type} has no element parameter");
                return anyElement;
            }
        }

        /// <summary>The element filter.</summary>
        /// <remarks>Pre: HasElement and !AnyElement. Post: a defined Element. Side effects: none.</remarks>
        public Element Element
        {
            get
            {
                Contract.Requires(HasElement && !anyElement, $"{type} has no concrete element parameter");
                return element;
            }
        }

        /// <summary>HP threshold percent of max HP.</summary>
        /// <remarks>Pre: Type is OnHpBelow, OnHpAbove or OnHpCrossBelow. Post: 1..100. Side effects: none.</remarks>
        public int Percent
        {
            get
            {
                Contract.Requires(type == TriggerType.OnHpBelow || type == TriggerType.OnHpAbove || type == TriggerType.OnHpCrossBelow,
                    $"{type} has no percent parameter");
                return percent;
            }
        }

        /// <summary>True when this type opens a timed window (OnSummon, OnHitTaken, OnMoveUsed, OnAllySwap, OnHpCrossBelow).</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool HasDuration => type == TriggerType.OnSummon || type == TriggerType.OnHitTaken
            || type == TriggerType.OnMoveUsed || type == TriggerType.OnAllySwap || type == TriggerType.OnHpCrossBelow;

        /// <summary>How long the effects stay active after the trigger, in ms.</summary>
        /// <remarks>Pre: HasDuration. Post: 1..600000. Side effects: none.</remarks>
        public int DurationMs
        {
            get
            {
                Contract.Requires(HasDuration, $"{type} has no duration_ms parameter");
                return durationMs;
            }
        }
    }
}
