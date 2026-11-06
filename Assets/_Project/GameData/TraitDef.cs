using System.Collections.Generic;
using UnityEngine;

namespace Cosmimera.GameData
{
    /// <summary>Imported passive trait: a trigger plus effects. REQ: docs/design/creatures.md#特性.</summary>
    /// <remarks>Pre: filled by the importer. Post: Trigger non-null; Effects has >= 1 entry. Side effects: none.</remarks>
    public sealed class TraitDef : GameDef
    {
        [SerializeField] private RuleDef trigger;
        [SerializeField] private List<RuleDef> effects = new List<RuleDef>();

        /// <summary>When the trait fires.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public RuleDef Trigger => trigger;

        /// <summary>What the trait does, in authoring order.</summary>
        /// <remarks>Pre: none. Post: Count >= 1. Side effects: none.</remarks>
        public IReadOnlyList<RuleDef> Effects => effects;

        internal void Set(RuleDef trigger, List<RuleDef> effects)
        {
            this.trigger = trigger;
            this.effects = effects;
        }
    }
}
