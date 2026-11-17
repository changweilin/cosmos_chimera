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
        [SerializeField] private int star = 1;
        [SerializeField] private List<EffectDef> downgradedEffects = new List<EffectDef>();

        /// <summary>Skill star / rarity: 1 一般, 2 稀有, 3 獨有. REQ: docs/design/creatures.md#技能星級.</summary>
        /// <remarks>Pre: none. Post: 1..3. Side effects: none.</remarks>
        public int Star => star;

        /// <summary>Effects as used when <paramref name="downgraded"/> (partner-provided ★ version) or not.</summary>
        /// <remarks>Pre: none. Post: as <see cref="Effects"/>; the ★ list when downgraded and one is stored. Side effects: none.</remarks>
        public IReadOnlyList<EffectDef> EffectsFor(bool downgraded) =>
            downgraded && downgradedEffects.Count > 0 ? downgradedEffects : effects;

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

        // Empty downgradedEffects = no ★ version (Star 1).
        internal void SetSkill(int star, List<EffectDef> downgradedEffects)
        {
            this.star = star;
            this.downgradedEffects = downgradedEffects ?? new List<EffectDef>();
        }
    }
}
