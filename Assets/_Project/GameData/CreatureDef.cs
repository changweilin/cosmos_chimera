using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using UnityEngine;

namespace Cosmimera.GameData
{
    /// <summary>Base stats of a creature form. REQ: docs/design/creatures.md#能力值.</summary>
    /// <remarks>Pre: none. Post: all values >= 0 for imported data. Side effects: none.</remarks>
    [Serializable]
    public struct CreatureStats
    {
        [SerializeField] private int con;
        [SerializeField] private int str;
        [SerializeField] private int spi;
        [SerializeField] private int agi;

        internal CreatureStats(int con, int str, int spi, int agi)
        {
            this.con = con;
            this.str = str;
            this.spi = spi;
            this.agi = agi;
        }

        /// <summary>Constitution.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Con => con;

        /// <summary>Strength.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Str => str;

        /// <summary>Spirit.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Spi => spi;

        /// <summary>Agility.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Agi => agi;
    }

    /// <summary>An (element, integer) pair, used for mastery.</summary>
    /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
    [Serializable]
    public struct ElementValue
    {
        [SerializeField] private Element element;
        [SerializeField] private int value;

        internal ElementValue(Element element, int value)
        {
            this.element = element;
            this.value = value;
        }

        /// <summary>The element.</summary>
        /// <remarks>Pre: none. Post: a defined Element. Side effects: none.</remarks>
        public Element Element => element;

        /// <summary>The value for that element.</summary>
        /// <remarks>Pre: none. Post: >= 0 for mastery. Side effects: none.</remarks>
        public int Value => value;
    }

    /// <summary>One evolution material requirement.</summary>
    /// <remarks>Pre: none. Post: Tier >= 1 and Qty >= 1 for imported data. Side effects: none.</remarks>
    [Serializable]
    public struct EvolutionMaterial
    {
        [SerializeField] private Element element;
        [SerializeField] private int tier;
        [SerializeField] private int qty;

        internal EvolutionMaterial(Element element, int tier, int qty)
        {
            this.element = element;
            this.tier = tier;
            this.qty = qty;
        }

        /// <summary>Material element.</summary>
        /// <remarks>Pre: none. Post: a defined Element. Side effects: none.</remarks>
        public Element Element => element;

        /// <summary>Material tier.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int Tier => tier;

        /// <summary>Required quantity.</summary>
        /// <remarks>Pre: none. Post: >= 1. Side effects: none.</remarks>
        public int Qty => qty;
    }

    /// <summary>Imported creature form. REQ: docs/design/creatures.md#資料模型, Data/schemas/creature.schema.json.</summary>
    /// <remarks>
    /// Pre: filled by the importer.
    /// Post: EvolvesTo == null iff Stage == Adult; SignatureMove and Trait are non-null; resist covers all ten elements.
    /// Side effects: none.
    /// </remarks>
    public sealed class CreatureDef : GameDef
    {
        private const int ElementCount = 10;

        [SerializeField] private string lineId;
        [SerializeField] private CreatureStage stage;
        [SerializeField] private CreatureSizeClass sizeClass;
        [SerializeField] private CreatureDef evolvesTo;
        [SerializeField] private int evolutionExp;
        [SerializeField] private List<EvolutionMaterial> evolutionMaterials = new List<EvolutionMaterial>();
        [SerializeField] private int[] resist = new int[ElementCount];
        [SerializeField] private List<ElementValue> mastery = new List<ElementValue>();
        [SerializeField] private CreatureStats stats;
        [SerializeField] private MoveDef signatureMove;
        [SerializeField] private TraitDef trait;
        [SerializeField] private string conceptJson;
        [SerializeField] private bool needsSensitivityReview;

        /// <summary>Evolution line id shared by all stages of the line.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string LineId => lineId;

        /// <summary>Stage of this form.</summary>
        /// <remarks>Pre: none. Post: a defined CreatureStage. Side effects: none.</remarks>
        public CreatureStage Stage => stage;

        /// <summary>Size class for animation pairing and art budgets.</summary>
        /// <remarks>Pre: none. Post: a defined CreatureSizeClass. Side effects: none.</remarks>
        public CreatureSizeClass SizeClass => sizeClass;

        /// <summary>The next form, or null for an adult.</summary>
        /// <remarks>Pre: none. Post: null iff Stage == Adult; otherwise same LineId, next stage. Side effects: none.</remarks>
        public CreatureDef EvolvesTo => evolvesTo;

        /// <summary>Experience required to evolve.</summary>
        /// <remarks>Pre: none. Post: 0 iff EvolvesTo == null, otherwise >= 1. Side effects: none.</remarks>
        public int EvolutionExp => evolutionExp;

        /// <summary>Materials required to evolve.</summary>
        /// <remarks>Pre: none. Post: empty iff EvolvesTo == null. Side effects: none.</remarks>
        public IReadOnlyList<EvolutionMaterial> EvolutionMaterials => evolutionMaterials;

        /// <summary>Mastery entries (1..3 elements).</summary>
        /// <remarks>Pre: none. Post: 1..3 entries, unique elements. Side effects: none.</remarks>
        public IReadOnlyList<ElementValue> Mastery => mastery;

        /// <summary>Base stats.</summary>
        /// <remarks>Pre: none. Post: all >= 0. Side effects: none.</remarks>
        public CreatureStats Stats => stats;

        /// <summary>Signature move.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public MoveDef SignatureMove => signatureMove;

        /// <summary>Passive trait.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public TraitDef Trait => trait;

        /// <summary>Concept block as compact JSON (authoring metadata, not read by game rules).</summary>
        /// <remarks>Pre: none. Post: a JSON object. Side effects: none.</remarks>
        public string ConceptJson => conceptJson;

        /// <summary>True when concept.sensitivity == "review" (creature-concept-pipeline.md).</summary>
        /// <remarks>Pre: none. Post: none. Side effects: none.</remarks>
        public bool NeedsSensitivityReview => needsSensitivityReview;

        /// <summary>Resistance percent against <paramref name="element"/>; negative is a weakness.</summary>
        /// <remarks>
        /// Pre: element is a defined Element.
        /// Post: -100..100.
        /// Side effects: none.
        /// </remarks>
        public int GetResist(Element element)
        {
            Contract.Requires(Enum.IsDefined(typeof(Element), element), "element must be a defined Element");
            return resist[(int)element];
        }

        internal void Set(string lineId, CreatureStage stage, CreatureSizeClass sizeClass, CreatureDef evolvesTo, int evolutionExp,
            List<EvolutionMaterial> evolutionMaterials, int[] resist, List<ElementValue> mastery, CreatureStats stats,
            MoveDef signatureMove, TraitDef trait, string conceptJson, bool needsSensitivityReview)
        {
            Contract.Requires(resist != null && resist.Length == ElementCount, "resist must have one entry per element");
            this.lineId = lineId;
            this.stage = stage;
            this.sizeClass = sizeClass;
            this.evolvesTo = evolvesTo;
            this.evolutionExp = evolutionExp;
            this.evolutionMaterials = evolutionMaterials;
            this.resist = resist;
            this.mastery = mastery;
            this.stats = stats;
            this.signatureMove = signatureMove;
            this.trait = trait;
            this.conceptJson = conceptJson;
            this.needsSensitivityReview = needsSensitivityReview;
        }
    }
}
