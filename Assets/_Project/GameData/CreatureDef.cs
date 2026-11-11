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

        /// <summary>
        /// These stats (the LV100 basis) scaled to <paramref name="level"/>: each of CON/STR/SPI/AGI becomes
        /// round_half_up(B × (L + 10) / 110). REQ: docs/design/level-progression.md#四維縮放 (Q-C28).
        /// </summary>
        /// <remarks>
        /// Pre: 1 &lt;= level &lt;= 100; stats &gt;= 0.
        /// Post: each value in 0..base, monotone in level, equal to base at 100. Side effects: none.
        /// </remarks>
        public CreatureStats AtLevel(int level)
        {
            Contract.Requires(level >= 1 && level <= 100, "level must be 1..100");
            Contract.Requires(con >= 0 && str >= 0 && spi >= 0 && agi >= 0, "stats must be >= 0");
            return new CreatureStats(Scale(con, level), Scale(str, level), Scale(spi, level), Scale(agi, level));
        }

        // Int64 before multiplying: B up to Int32.MaxValue × 110 overflows Int32. One rounding at the end only, so low
        // levels do not get the extra staircase a pre-rounded percentage would add.
        private static int Scale(int b, int level) => (int)(((long)b * (level + 10) + 55) / 110);
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

        /// <summary>Lowest legal evolution level gate per non-adult stage (juvenile, adolescent).</summary>
        /// <remarks>
        /// Pre: none. Post: constant. Side effects: none.
        /// REQ: level-progression.md#已確認規則 — juvenile→adolescent LV20–30, adolescent→adult LV35–50, inclusive. Mirrored
        /// by creature.schema.json and the importer; tools/tests checks the schema against these numbers.
        /// </remarks>
        public static readonly IReadOnlyList<int> EvolutionLevelMin = new[] { 20, 35 };

        /// <summary>Highest legal evolution level gate per non-adult stage (juvenile, adolescent).</summary>
        /// <remarks>Pre: none. Post: constant; &gt;= EvolutionLevelMin. Side effects: none.</remarks>
        public static readonly IReadOnlyList<int> EvolutionLevelMax = new[] { 30, 50 };

        [SerializeField] private string lineId;
        [SerializeField] private CreatureStage stage;
        [SerializeField] private CreatureSizeClass sizeClass;
        [SerializeField] private AiStyle aiStyle;
        [SerializeField] private Temperament temperament;
        [SerializeField] private CreatureDef evolvesTo;
        [SerializeField] private int evolutionLevel;
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

        /// <summary>Combat AI positioning style of this form (P2-10).</summary>
        /// <remarks>Pre: none. Post: a defined AiStyle. Side effects: none.</remarks>
        public AiStyle AiStyle => aiStyle;

        /// <summary>Temperament of this form: its wild territory radius (D0-E2, combat.md#野生領域).</summary>
        /// <remarks>Pre: none. Post: a defined Temperament. Side effects: none.</remarks>
        public Temperament Temperament => temperament;

        /// <summary>The next form, or null for an adult.</summary>
        /// <remarks>Pre: none. Post: null iff Stage == Adult; otherwise same LineId, next stage. Side effects: none.</remarks>
        public CreatureDef EvolvesTo => evolvesTo;

        /// <summary>Level gate to evolve (fixed per species; juvenile 20–30, adolescent 35–50 — level-progression.md).</summary>
        /// <remarks>Pre: none. Post: 0 iff EvolvesTo == null, otherwise inside the stage's band. Side effects: none.</remarks>
        public int EvolutionLevel => evolutionLevel;

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

        internal void Set(string lineId, CreatureStage stage, CreatureSizeClass sizeClass, AiStyle aiStyle,
            Temperament temperament, CreatureDef evolvesTo,
            int evolutionLevel,
            List<EvolutionMaterial> evolutionMaterials, int[] resist, List<ElementValue> mastery, CreatureStats stats,
            MoveDef signatureMove, TraitDef trait, string conceptJson, bool needsSensitivityReview)
        {
            Contract.Requires(resist != null && resist.Length == ElementCount, "resist must have one entry per element");
            this.lineId = lineId;
            this.stage = stage;
            this.sizeClass = sizeClass;
            this.aiStyle = aiStyle;
            this.temperament = temperament;
            this.evolvesTo = evolvesTo;
            this.evolutionLevel = evolutionLevel;
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
