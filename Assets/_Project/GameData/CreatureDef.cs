using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using UnityEngine;

namespace Cosmimera.GameData
{
    /// <summary>Six core stats of a creature form (LV100 basis). REQ: docs/design/creatures.md#六大能力值.</summary>
    /// <remarks>Pre: none. Post: all values >= 0 for imported data. Side effects: none.</remarks>
    [Serializable]
    public struct CreatureStats
    {
        [SerializeField] private int hp;
        [SerializeField] private int speed;
        [SerializeField] private int yinAtk;
        [SerializeField] private int yangAtk;
        [SerializeField] private int yinDef;
        [SerializeField] private int yangDef;

        /// <summary>Builds a stat set (importer, level scaling, rule-layer callers and tests).</summary>
        /// <remarks>Pre: none (rule-layer consumers check nonnegativity). Post: properties equal the arguments. Side effects: none.</remarks>
        public CreatureStats(int hp, int speed, int yinAtk, int yangAtk, int yinDef, int yangDef)
        {
            this.hp = hp;
            this.speed = speed;
            this.yinAtk = yinAtk;
            this.yangAtk = yangAtk;
            this.yinDef = yinDef;
            this.yangDef = yangDef;
        }

        /// <summary>HP points; max HP = Hp × balance.json#hp.per_point.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Hp => hp;

        /// <summary>Speed: drives move speed and move cooldowns.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int Speed => speed;

        /// <summary>陰攻: attack of yin (inside-out) moves.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int YinAtk => yinAtk;

        /// <summary>陽攻: attack of yang (outside-in) moves.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int YangAtk => yangAtk;

        /// <summary>陰守: defence against yin moves.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int YinDef => yinDef;

        /// <summary>陽守: defence against yang moves.</summary>
        /// <remarks>Pre: none. Post: >= 0. Side effects: none.</remarks>
        public int YangDef => yangDef;

        /// <summary>Attack used by a move of <paramref name="polarity"/>.</summary>
        /// <remarks>Pre: polarity defined. Post: YinAtk or YangAtk. Side effects: none.</remarks>
        public int Attack(Polarity polarity)
        {
            Contract.Requires(polarity == Polarity.Yin || polarity == Polarity.Yang, "undefined Polarity");
            return polarity == Polarity.Yin ? yinAtk : yangAtk;
        }

        /// <summary>Defence against a move of <paramref name="polarity"/>.</summary>
        /// <remarks>Pre: polarity defined. Post: YinDef or YangDef. Side effects: none.</remarks>
        public int Defence(Polarity polarity)
        {
            Contract.Requires(polarity == Polarity.Yin || polarity == Polarity.Yang, "undefined Polarity");
            return polarity == Polarity.Yin ? yinDef : yangDef;
        }

        /// <summary>Sum of the six stats (creatures.md#綜合值預算).</summary>
        /// <remarks>Pre: none. Post: Int64 sum, no overflow. Side effects: none.</remarks>
        public long Total => (long)hp + speed + yinAtk + yangAtk + yinDef + yangDef;

        /// <summary>
        /// These stats (the LV100 basis) scaled to <paramref name="level"/>: each of the six becomes
        /// round_half_up(B × (L + 10) / 110). REQ: docs/design/level-progression.md#能力值縮放 (Q-C28, Q-C67).
        /// </summary>
        /// <remarks>
        /// Pre: 1 &lt;= level &lt;= 100; stats &gt;= 0.
        /// Post: each value in 0..base, monotone in level, equal to base at 100. Side effects: none.
        /// </remarks>
        public CreatureStats AtLevel(int level)
        {
            Contract.Requires(level >= 1 && level <= 100, "level must be 1..100");
            Contract.Requires(hp >= 0 && speed >= 0 && yinAtk >= 0 && yangAtk >= 0 && yinDef >= 0 && yangDef >= 0,
                "stats must be >= 0");
            return new CreatureStats(Scale(hp, level), Scale(speed, level), Scale(yinAtk, level), Scale(yangAtk, level),
                Scale(yinDef, level), Scale(yangDef, level));
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
        [SerializeField] private List<NicheCandidate> niches = new List<NicheCandidate>();
        [SerializeField] private CreatureDef evolvesTo;
        [SerializeField] private CreatureDef overloadOf;
        [SerializeField] private CreatureDef overloadForm;
        [SerializeField] private int evolutionLevel;
        [SerializeField] private List<EvolutionMaterial> evolutionMaterials = new List<EvolutionMaterial>();
        [SerializeField] private int[] resist = new int[ElementCount];
        [SerializeField] private List<ElementValue> mastery = new List<ElementValue>();
        [SerializeField] private CreatureStats stats;
        [SerializeField] private MoveDef signatureMove;
        [SerializeField] private TraitDef trait;
        [SerializeField] private FieldUtility fieldUtility;
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

        /// <summary>
        /// Candidate niches of this form with positive weights; a wild individual draws one at spawn, then its habit.
        /// REQ: creature-ecology.md#生成順序.
        /// </summary>
        /// <remarks>
        /// Pre: none. Post: never null; empty only for forms that never spawn wild (overload) or hand-built test defs.
        /// Side effects: none.
        /// </remarks>
        public IReadOnlyList<NicheCandidate> Niches => niches;

        /// <summary>The next form, or null for an adult or overload form.</summary>
        /// <remarks>Pre: none. Post: null iff Stage == Adult || Stage == Overload; otherwise same LineId, next stage. Side effects: none.</remarks>
        public CreatureDef EvolvesTo => evolvesTo;

        /// <summary>Source adult form when this is an overload form; null otherwise (creatures.md#超載, Q-C12).</summary>
        /// <remarks>Pre: none. Post: non-null iff Stage == Overload (same LineId, Adult). Side effects: none.</remarks>
        public CreatureDef OverloadOf => overloadOf;

        /// <summary>Overload form of this adult creature, or null if none is defined.</summary>
        /// <remarks>Pre: none. Post: null unless Stage == Adult and an Overload form points to this form. Side effects: none.</remarks>
        public CreatureDef OverloadForm => overloadForm;

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

        /// <summary>Single life/field utility move or trait for this form (None if unassigned).</summary>
        /// <remarks>Pre: none. Post: a defined FieldUtility. Side effects: none.</remarks>
        public FieldUtility FieldUtility => fieldUtility;

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
            List<NicheCandidate> niches, CreatureDef evolvesTo,
            int evolutionLevel,
            List<EvolutionMaterial> evolutionMaterials, int[] resist, List<ElementValue> mastery, CreatureStats stats,
            MoveDef signatureMove, TraitDef trait, string conceptJson, bool needsSensitivityReview)
            => Set(lineId, stage, sizeClass, aiStyle, niches, evolvesTo, evolutionLevel, evolutionMaterials,
                resist, mastery, stats, signatureMove, trait, FieldUtility.None, conceptJson, needsSensitivityReview);

        internal void Set(string lineId, CreatureStage stage, CreatureSizeClass sizeClass, AiStyle aiStyle,
            List<NicheCandidate> niches, CreatureDef evolvesTo,
            int evolutionLevel,
            List<EvolutionMaterial> evolutionMaterials, int[] resist, List<ElementValue> mastery, CreatureStats stats,
            MoveDef signatureMove, TraitDef trait, FieldUtility fieldUtility, string conceptJson, bool needsSensitivityReview)
        {
            Contract.Requires(resist != null && resist.Length == ElementCount, "resist must have one entry per element");
            this.lineId = lineId;
            this.stage = stage;
            this.sizeClass = sizeClass;
            this.aiStyle = aiStyle;
            this.niches = niches ?? new List<NicheCandidate>();
            this.evolvesTo = evolvesTo;
            this.evolutionLevel = evolutionLevel;
            this.evolutionMaterials = evolutionMaterials;
            this.resist = resist;
            this.mastery = mastery;
            this.stats = stats;
            this.signatureMove = signatureMove;
            this.trait = trait;
            this.fieldUtility = fieldUtility;
            this.conceptJson = conceptJson;
            this.needsSensitivityReview = needsSensitivityReview;
        }

        internal void SetOverloadLinks(CreatureDef overloadOf, CreatureDef overloadForm)
        {
            this.overloadOf = overloadOf;
            this.overloadForm = overloadForm;
        }
    }
}
