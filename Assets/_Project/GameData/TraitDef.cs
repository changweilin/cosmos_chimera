using System.Collections.Generic;
using UnityEngine;

namespace Cosmimera.GameData
{
    /// <summary>Imported passive trait: a trigger plus effects. REQ: docs/design/creatures.md#特性.</summary>
    /// <remarks>Pre: filled by the importer. Post: Trigger non-null; Effects has >= 1 entry. Side effects: none.</remarks>
    public sealed class TraitDef : GameDef
    {
        [SerializeField] private TriggerDef trigger;
        [SerializeField] private List<EffectDef> effects = new List<EffectDef>();

        /// <summary>Most effects one trait may carry. Mirrors trait.schema.json effects.maxItems.</summary>
        /// <remarks>Pre: none. Post: constant. Side effects: none. Keeps a trait's per-query cost bounded in combat.</remarks>
        public const int MaxEffects = 8;

        /// <summary>When the trait fires.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public TriggerDef Trigger => trigger;

        /// <summary>What the trait does, in authoring order.</summary>
        /// <remarks>
        /// Pre: none. Post: 1 &lt;= Count &lt;= MaxEffects; no heal_percent_max_hp when Trigger is continuous
        ///       (always, on_hp_below, on_hp_above), since a heal is instant and needs an event to fire on.
        /// Side effects: none.
        /// </remarks>
        public IReadOnlyList<EffectDef> Effects => effects;

        internal void Set(TriggerDef trigger, List<EffectDef> effects)
        {
            this.trigger = trigger;
            this.effects = effects;
        }
    }
}
