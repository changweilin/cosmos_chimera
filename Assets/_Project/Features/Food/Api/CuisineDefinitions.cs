using System;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Food
{
    /// <summary>How an ingredient participates in pot cooking. REQ: docs/design/cuisine.md#2-食材屬性每份原料加工品皆有</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum IngredientRole
    {
        /// <summary>Main food without an effect.</summary>
        Base = 0,
        /// <summary>Main food that also carries an effect family.</summary>
        Effect = 1,
        /// <summary>Not a main food; only extends duration or completes recipes.</summary>
        Seasoning = 2,
        /// <summary>Medicinal critter/fungus additive; carries an effect but is not a main food.</summary>
        Critter = 3,
    }

    /// <summary>Culinary tags used by cooking-method conditions and dish naming.</summary>
    /// <remarks>Pre: none. Post: bit flags. Side effects: none.</remarks>
    [Flags]
    public enum IngredientTags
    {
        None = 0,
        RedMeat = 1 << 0,
        Poultry = 1 << 1,
        Fish = 1 << 2,
        Shellfish = 1 << 3,
        Egg = 1 << 4,
        Dairy = 1 << 5,
        Grain = 1 << 6,
        Flour = 1 << 7,
        Vegetable = 1 << 8,
        Fruit = 1 << 9,
        Herb = 1 << 10,
        Mushroom = 1 << 11,
        Nut = 1 << 12,
        Oil = 1 << 13,
        Salt = 1 << 14,
        Sugar = 1 << 15,
        Spice = 1 << 16,
        Critter = 1 << 17,
        Raw = 1 << 18,
        Chilled = 1 << 19,
        Smoked = 1 << 20,
        Fermented = 1 << 21,
        Powder = 1 << 22,
    }

    /// <summary>Effect family an ingredient contributes to a free-form dish. REQ: docs/design/cuisine.md#效果家族與等級</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum FoodEffectFamily
    {
        None = 0,
        Mastery = 1,
        Resist = 2,
        Swift = 3,
        Nimble = 4,
        Focus = 5,
        Keen = 6,
        Fierce = 7,
        Guard = 8,
        Bounty = 9,
        Tame = 10,
        Revive = 11,
    }

    /// <summary>One-step transformation turning a raw ingredient into a processed good. REQ: docs/design/cuisine.md#3-加工加工技與加工品</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum ProcessKind
    {
        Freeze = 0,
        Grind = 1,
        Press = 2,
        Churn = 3,
        Ferment = 4,
        Smoke = 5,
    }

    /// <summary>Dish naming theme derived from the main ingredients of a free-form dish.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum DishTheme
    {
        Meat = 0,
        Fowl = 1,
        Fish = 2,
        Seafood = 3,
        Mushroom = 4,
        Egg = 5,
        Vegetable = 6,
        Fruit = 7,
        Grain = 8,
        Dairy = 9,
        Nut = 10,
        Herb = 11,
        Mixed = 12,
        Tonic = 13,
    }

    /// <summary>Pot-cooking properties of an ingredient or processed good.</summary>
    /// <remarks>Pre: validated in constructor. Post: immutable. Side effects: none.</remarks>
    public readonly struct CuisineTraits
    {
        /// <summary>Default per-unit duration extension for non-base ingredients (暫定, cuisine.md#2).</summary>
        public const int DefaultDurationAddMs = 30_000;

        /// <summary>Duration extension for fermented goods (暫定, cuisine.md#3).</summary>
        public const int FermentedDurationAddMs = 60_000;

        /// <summary>Upper bound for per-ingredient effect points.</summary>
        public const int MaxEffectPoints = 9;

        /// <summary>Creates validated cuisine traits.</summary>
        /// <remarks>
        /// Pre: role, effectFamily, effectElement defined; 0 &lt;= cookHealPercent &lt;= 100;
        ///      effectPoints == 0 iff effectFamily == None, else 1..MaxEffectPoints;
        ///      role is Base or Seasoning iff effectFamily == None (Effect/Critter must carry an effect);
        ///      0 &lt;= durationAddMs &lt;= 600 000.
        /// Post: properties equal arguments.
        /// Side effects: none.
        /// </remarks>
        public CuisineTraits(
            IngredientRole role,
            IngredientTags tags,
            int cookHealPercent,
            FoodEffectFamily effectFamily = FoodEffectFamily.None,
            Element effectElement = Element.Metal,
            int effectPoints = 0,
            int durationAddMs = DefaultDurationAddMs)
        {
            Contract.Requires(Enum.IsDefined(typeof(IngredientRole), role), "role must be defined");
            Contract.Requires(Enum.IsDefined(typeof(FoodEffectFamily), effectFamily), "effectFamily must be defined");
            Contract.Requires(Enum.IsDefined(typeof(Element), effectElement), "effectElement must be defined");
            Contract.Requires(cookHealPercent >= 0 && cookHealPercent <= 100, "cookHealPercent must be 0..100");
            Contract.Requires(effectFamily == FoodEffectFamily.None ? effectPoints == 0 : effectPoints >= 1 && effectPoints <= MaxEffectPoints,
                "effectPoints must be 0 without an effect, else 1..MaxEffectPoints");
            bool carriesEffect = role == IngredientRole.Effect || role == IngredientRole.Critter;
            Contract.Requires(carriesEffect == (effectFamily != FoodEffectFamily.None), "Effect/Critter roles carry an effect; Base/Seasoning do not");
            Contract.Requires(durationAddMs >= 0 && durationAddMs <= 600_000, "durationAddMs must be 0..600000");

            Role = role;
            Tags = tags;
            CookHealPercent = cookHealPercent;
            EffectFamily = effectFamily;
            EffectElement = effectElement;
            EffectPoints = effectPoints;
            DurationAddMs = durationAddMs;
        }

        /// <summary>Participation role.</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public IngredientRole Role { get; }

        /// <summary>Culinary tags.</summary>
        /// <remarks>Pre: none. Post: flags. Side effects: none.</remarks>
        public IngredientTags Tags { get; }

        /// <summary>HP percent this ingredient contributes once cooked.</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int CookHealPercent { get; }

        /// <summary>Effect family contributed to free-form dishes (None for Base/Seasoning).</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public FoodEffectFamily EffectFamily { get; }

        /// <summary>Element parameter; meaningful only for Mastery and Resist.</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public Element EffectElement { get; }

        /// <summary>Effect points (0 without an effect).</summary>
        /// <remarks>Pre: none. Post: 0..MaxEffectPoints. Side effects: none.</remarks>
        public int EffectPoints { get; }

        /// <summary>Duration extension contributed when role is not Base.</summary>
        /// <remarks>Pre: none. Post: 0..600000. Side effects: none.</remarks>
        public int DurationAddMs { get; }

        /// <summary>True for main foods (Base or Effect roles).</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool IsMain => Role == IngredientRole.Base || Role == IngredientRole.Effect;

        /// <summary>True when the effect family is parameterized by element.</summary>
        /// <remarks>Pre: none. Post: true iff family is Mastery or Resist. Side effects: none.</remarks>
        public bool IsElementalEffect => EffectFamily == FoodEffectFamily.Mastery || EffectFamily == FoodEffectFamily.Resist;

        /// <summary>Copy with tags replaced.</summary>
        /// <remarks>Pre: none. Post: other fields unchanged. Side effects: none.</remarks>
        public CuisineTraits WithTags(IngredientTags tags) =>
            new CuisineTraits(Role, tags, CookHealPercent, EffectFamily, EffectElement, EffectPoints, DurationAddMs);

        /// <summary>Copy with effect points and duration extension replaced.</summary>
        /// <remarks>Pre: same as constructor for the new values. Post: other fields unchanged. Side effects: none.</remarks>
        public CuisineTraits WithPotency(int effectPoints, int durationAddMs) =>
            new CuisineTraits(Role, Tags, CookHealPercent, EffectFamily, EffectElement, effectPoints, durationAddMs);

        /// <summary>Copy with cook heal replaced.</summary>
        /// <remarks>Pre: 0..100. Post: other fields unchanged. Side effects: none.</remarks>
        public CuisineTraits WithCookHeal(int cookHealPercent) =>
            new CuisineTraits(Role, Tags, cookHealPercent, EffectFamily, EffectElement, EffectPoints, DurationAddMs);

        /// <summary>Copy with role replaced (keeps effect; Base/Effect and Seasoning/Critter must stay consistent).</summary>
        /// <remarks>Pre: role consistent with EffectFamily. Post: other fields unchanged. Side effects: none.</remarks>
        public CuisineTraits WithRole(IngredientRole role) =>
            new CuisineTraits(role, Tags, CookHealPercent, EffectFamily, EffectElement, EffectPoints, DurationAddMs);

        /// <summary>Neutral traits derived from a category for ingredients registered without explicit cuisine data.</summary>
        /// <remarks>
        /// Pre: category defined; 0 &lt;= rawHealPercent &lt;= 100.
        /// Post: effect-free traits; Spice/Oil is Seasoning, everything else Base (Critter cannot be neutral, so it is Seasoning).
        /// Side effects: none.
        /// </remarks>
        public static CuisineTraits FromCategory(IngredientCategory category, int rawHealPercent)
        {
            Contract.Requires(Enum.IsDefined(typeof(IngredientCategory), category), "category must be defined");
            Contract.Requires(rawHealPercent >= 0 && rawHealPercent <= 100, "rawHealPercent must be 0..100");
            IngredientTags tags;
            IngredientRole role = IngredientRole.Base;
            switch (category)
            {
                case IngredientCategory.MeatGround: tags = IngredientTags.RedMeat | IngredientTags.Raw; break;
                case IngredientCategory.MeatSky: tags = IngredientTags.Poultry | IngredientTags.Raw; break;
                case IngredientCategory.Seafood: tags = IngredientTags.Fish | IngredientTags.Raw; break;
                case IngredientCategory.BerryHerb: tags = IngredientTags.Fruit; break;
                case IngredientCategory.GrainCrop: tags = IngredientTags.Grain; break;
                case IngredientCategory.Dairy: tags = IngredientTags.Dairy; break;
                case IngredientCategory.Egg: tags = IngredientTags.Egg; break;
                case IngredientCategory.Fungi: tags = IngredientTags.Mushroom; break;
                case IngredientCategory.Critter: tags = IngredientTags.Critter; role = IngredientRole.Seasoning; break;
                default: tags = IngredientTags.Spice; role = IngredientRole.Seasoning; break;
            }

            int heal = rawHealPercent > 10 ? rawHealPercent : 10;
            return new CuisineTraits(role, tags, role == IngredientRole.Base ? heal : 0);
        }
    }

    /// <summary>Processing recipe: one unit of <see cref="InputIngredientId"/> becomes one unit of <see cref="OutputIngredientId"/>.</summary>
    /// <remarks>Pre: validated in constructor. Post: immutable. Side effects: none.</remarks>
    public sealed class ProcessSpec
    {
        /// <summary>Creates a processing recipe.</summary>
        /// <remarks>
        /// Pre: id, inputIngredientId, outputIngredientId non-empty and input != output; kind defined.
        /// Post: properties equal arguments.
        /// Side effects: none.
        /// </remarks>
        public ProcessSpec(string id, ProcessKind kind, string inputIngredientId, string outputIngredientId)
        {
            Contract.Requires(!string.IsNullOrEmpty(id), "id must be non-empty");
            Contract.Requires(Enum.IsDefined(typeof(ProcessKind), kind), "kind must be defined");
            Contract.Requires(!string.IsNullOrEmpty(inputIngredientId), "inputIngredientId must be non-empty");
            Contract.Requires(!string.IsNullOrEmpty(outputIngredientId), "outputIngredientId must be non-empty");
            Contract.Requires(inputIngredientId != outputIngredientId, "input and output must differ");
            Id = id;
            Kind = kind;
            InputIngredientId = inputIngredientId;
            OutputIngredientId = outputIngredientId;
        }

        /// <summary>Unique process id.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Processing kind (decides the required life skill).</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public ProcessKind Kind { get; }

        /// <summary>Consumed ingredient id.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string InputIngredientId { get; }

        /// <summary>Produced (processed) ingredient id.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string OutputIngredientId { get; }
    }

    /// <summary>Naming and effect summary attached to a generated free-form dish.</summary>
    /// <remarks>Pre: constructed by <see cref="PotCookingRules"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct FreeDishDescriptor
    {
        internal FreeDishDescriptor(CookingMethod method, DishTheme theme, FoodEffectFamily family, Element familyElement, int level)
        {
            Method = method;
            Theme = theme;
            Family = family;
            FamilyElement = familyElement;
            Level = level;
        }

        /// <summary>Cooking method used.</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public CookingMethod Method { get; }

        /// <summary>Main-ingredient theme for naming.</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public DishTheme Theme { get; }

        /// <summary>Effect family (None when effects cancelled or absent).</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public FoodEffectFamily Family { get; }

        /// <summary>Element of a Mastery/Resist family; ignore otherwise.</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public Element FamilyElement { get; }

        /// <summary>Effect level 1..3 (0 when Family is None).</summary>
        /// <remarks>Pre: none. Post: 0..3. Side effects: none.</remarks>
        public int Level { get; }
    }
}
