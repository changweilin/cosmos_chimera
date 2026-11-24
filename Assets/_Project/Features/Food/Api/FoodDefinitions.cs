using System;
using System.Collections.Generic;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Food
{
    /// <summary>Definition of a raw culinary ingredient acquired through life skills.</summary>
    /// <remarks>Pre: constructed with valid ids and ranges. Post: immutable value. Side effects: none.</remarks>
    public sealed class IngredientSpec
    {
        /// <summary>Creates a validated ingredient definition.</summary>
        /// <remarks>
        /// Pre: id non-empty; category, source, element are defined enums; 1 &lt;= tier &lt;= 3; 0 &lt;= rawHealPercent &lt;= 100.
        /// Post: properties equal arguments.
        /// Side effects: none.
        /// </remarks>
        public IngredientSpec(
            string id,
            IngredientCategory category,
            FoodAcquisitionMethod source,
            Element element,
            int tier,
            int rawHealPercent)
            : this(id, category, source, element, tier, rawHealPercent, CuisineTraits.FromCategory(category, rawHealPercent), false)
        {
        }

        /// <summary>Creates a validated ingredient definition with explicit pot-cooking traits.</summary>
        /// <remarks>
        /// Pre: same as the short constructor; traits constructed via CuisineTraits (validated there).
        ///      For a processed good, source is the raw input's source (it is excluded from gathering pools).
        /// Post: properties equal arguments.
        /// Side effects: none.
        /// </remarks>
        public IngredientSpec(
            string id,
            IngredientCategory category,
            FoodAcquisitionMethod source,
            Element element,
            int tier,
            int rawHealPercent,
            in CuisineTraits traits,
            bool isProcessed)
        {
            Contract.Requires(!string.IsNullOrEmpty(id), "id must be non-empty");
            Contract.Requires(Enum.IsDefined(typeof(IngredientCategory), category), "category must be defined");
            Contract.Requires(Enum.IsDefined(typeof(FoodAcquisitionMethod), source), "source must be defined");
            Contract.Requires(Enum.IsDefined(typeof(Element), element), "element must be defined");
            Contract.Requires(tier >= 1 && tier <= 3, "tier must be 1..3");
            Contract.Requires(rawHealPercent >= 0 && rawHealPercent <= 100, "rawHealPercent must be 0..100");

            Id = id;
            Category = category;
            Source = source;
            Element = element;
            Tier = tier;
            RawHealPercent = rawHealPercent;
            Traits = traits;
            IsProcessed = isProcessed;
        }

        /// <summary>Unique snake_case ingredient id.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Culinary category used by category-based recipes.</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public IngredientCategory Category { get; }

        /// <summary>Life-skill acquisition method that yields this ingredient.</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public FoodAcquisitionMethod Source { get; }

        /// <summary>Elemental affinity of this ingredient.</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public Element Element { get; }

        /// <summary>Quality/rarity tier (1..3).</summary>
        /// <remarks>Pre: none. Post: 1..3. Side effects: none.</remarks>
        public int Tier { get; }

        /// <summary>HP percent restored if eaten raw (0 if inedible raw).</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int RawHealPercent { get; }

        /// <summary>True when this ingredient can be eaten directly without cooking.</summary>
        /// <remarks>Pre: none. Post: true iff RawHealPercent &gt; 0. Side effects: none.</remarks>
        public bool IsEdibleRaw => RawHealPercent > 0;

        /// <summary>Pot-cooking role, tags, heal, and effect contribution.</summary>
        /// <remarks>Pre: none. Post: validated traits. Side effects: none.</remarks>
        public CuisineTraits Traits { get; }

        /// <summary>True for goods made by a <see cref="ProcessSpec"/>; never yielded by gathering.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool IsProcessed { get; }
    }

    /// <summary>One ingredient slot required by a cooking recipe (either exact id or category match).</summary>
    /// <remarks>Pre: constructed via <see cref="ById"/> or <see cref="ByCategory"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct RecipeIngredientRequirement
    {
        private RecipeIngredientRequirement(string ingredientId, IngredientCategory category, bool isCategoryMatch, int quantity)
        {
            IngredientId = ingredientId;
            Category = category;
            IsCategoryMatch = isCategoryMatch;
            Quantity = quantity;
        }

        /// <summary>Specific ingredient id when <see cref="IsCategoryMatch"/> is false; otherwise null.</summary>
        /// <remarks>Pre: none. Post: non-empty iff !IsCategoryMatch. Side effects: none.</remarks>
        public string IngredientId { get; }

        /// <summary>Ingredient category when <see cref="IsCategoryMatch"/> is true.</summary>
        /// <remarks>Pre: none. Post: defined when IsCategoryMatch. Side effects: none.</remarks>
        public IngredientCategory Category { get; }

        /// <summary>True when any ingredient of <see cref="Category"/> satisfies this slot.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool IsCategoryMatch { get; }

        /// <summary>Number of units consumed.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int Quantity { get; }

        /// <summary>Creates a requirement for a specific ingredient id.</summary>
        /// <remarks>
        /// Pre: ingredientId non-empty; quantity &gt;= 1.
        /// Post: IsCategoryMatch == false.
        /// Side effects: none.
        /// </remarks>
        public static RecipeIngredientRequirement ById(string ingredientId, int quantity)
        {
            Contract.Requires(!string.IsNullOrEmpty(ingredientId), "ingredientId must be non-empty");
            Contract.Requires(quantity >= 1, "quantity must be >= 1");
            return new RecipeIngredientRequirement(ingredientId, default, false, quantity);
        }

        /// <summary>Creates a requirement for any ingredient matching <paramref name="category"/>.</summary>
        /// <remarks>
        /// Pre: category is a defined IngredientCategory; quantity &gt;= 1.
        /// Post: IsCategoryMatch == true.
        /// Side effects: none.
        /// </remarks>
        public static RecipeIngredientRequirement ByCategory(IngredientCategory category, int quantity)
        {
            Contract.Requires(Enum.IsDefined(typeof(IngredientCategory), category), "category must be defined");
            Contract.Requires(quantity >= 1, "quantity must be >= 1");
            return new RecipeIngredientRequirement(null, category, true, quantity);
        }
    }

    /// <summary>Healing, revival, cleanse, exploration, and combat buff effects granted by eating a food.</summary>
    /// <remarks>Pre: validated in constructor. Post: immutable. Side effects: none.</remarks>
    public sealed class FoodEffectSpec
    {
        private static readonly EffectSpec[] EmptyCombatEffects = Array.Empty<EffectSpec>();

        /// <summary>Creates a validated food effect specification.</summary>
        /// <remarks>
        /// Pre: all percents in 0..100; rollCooldownModPercent in -100..100; 0 &lt;= durationMs &lt;= EffectBook.MaxDurationMs;
        ///      if combatEffects is non-empty, durationMs &gt; 0 and none of combatEffects are instant.
        /// Post: properties equal arguments (combatEffects defensively copied).
        /// Side effects: allocates array copy if combatEffects is non-empty.
        /// </remarks>
        public FoodEffectSpec(
            int creatureHealPercent,
            int playerHealPercent,
            int revivePercent,
            bool cleanseNegativeEffects,
            int rollCooldownModPercent,
            int catchRateBonusPercent,
            int dropRateBonusPercent,
            int durationMs,
            IReadOnlyList<EffectSpec> combatEffects = null)
        {
            Contract.Requires(creatureHealPercent >= 0 && creatureHealPercent <= 100, "creatureHealPercent must be 0..100");
            Contract.Requires(playerHealPercent >= 0 && playerHealPercent <= 100, "playerHealPercent must be 0..100");
            Contract.Requires(revivePercent >= 0 && revivePercent <= 100, "revivePercent must be 0..100");
            Contract.Requires(rollCooldownModPercent >= -100 && rollCooldownModPercent <= 100, "rollCooldownModPercent must be -100..100");
            Contract.Requires(catchRateBonusPercent >= 0 && catchRateBonusPercent <= 100, "catchRateBonusPercent must be 0..100");
            Contract.Requires(dropRateBonusPercent >= 0 && dropRateBonusPercent <= 100, "dropRateBonusPercent must be 0..100");
            Contract.Requires(durationMs >= 0 && durationMs <= EffectBook.MaxDurationMs, "durationMs must be 0..MaxDurationMs");

            CreatureHealPercent = creatureHealPercent;
            PlayerHealPercent = playerHealPercent;
            RevivePercent = revivePercent;
            CleanseNegativeEffects = cleanseNegativeEffects;
            RollCooldownModPercent = rollCooldownModPercent;
            CatchRateBonusPercent = catchRateBonusPercent;
            DropRateBonusPercent = dropRateBonusPercent;
            DurationMs = durationMs;

            if (combatEffects == null || combatEffects.Count == 0)
            {
                CombatEffects = EmptyCombatEffects;
            }
            else
            {
                Contract.Requires(durationMs > 0, "timed combat effects require durationMs > 0");
                var copy = new EffectSpec[combatEffects.Count];
                for (int i = 0; i < combatEffects.Count; i++)
                {
                    Contract.Requires(!combatEffects[i].IsInstant, "food combatEffects must be timed; use CreatureHealPercent for instant heal");
                    copy[i] = combatEffects[i];
                }

                CombatEffects = copy;
            }
        }

        /// <summary>HP percent restored to target creature(s).</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int CreatureHealPercent { get; }

        /// <summary>HP percent restored to the player character.</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int PlayerHealPercent { get; }

        /// <summary>HP percent restored when reviving a fainted creature (0 if food cannot revive).</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int RevivePercent { get; }

        /// <summary>True when eating this food removes harmful timed effects from target creatures.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool CleanseNegativeEffects { get; }

        /// <summary>Relative percent modifier to player dodge-roll cooldown (negative = faster).</summary>
        /// <remarks>Pre: none. Post: -100..100. Side effects: none.</remarks>
        public int RollCooldownModPercent { get; }

        /// <summary>Flat capture rate bonus (percentage points) while timed buff is active.</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int CatchRateBonusPercent { get; }

        /// <summary>Flat material/food drop rate bonus (percentage points) while timed buff is active.</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int DropRateBonusPercent { get; }

        /// <summary>Duration of timed buffs in milliseconds (0 for instant-only food).</summary>
        /// <remarks>Pre: none. Post: 0..EffectBook.MaxDurationMs. Side effects: none.</remarks>
        public int DurationMs { get; }

        /// <summary>Combat stat/element modifiers applied to target creatures while buff is active.</summary>
        /// <remarks>Pre: none. Post: non-null, no instant effects. Side effects: none.</remarks>
        public IReadOnlyList<EffectSpec> CombatEffects { get; }

        /// <summary>True when this food grants any timed player or creature buff.</summary>
        /// <remarks>Pre: none. Post: bool. Side effects: none.</remarks>
        public bool HasTimedBuff =>
            DurationMs > 0 &&
            (CombatEffects.Count > 0 || RollCooldownModPercent != 0 || CatchRateBonusPercent > 0 || DropRateBonusPercent > 0);
    }

    /// <summary>Definition of a consumable food item (cooked dish or edible ration).</summary>
    /// <remarks>Pre: valid parameters. Post: immutable. Side effects: none.</remarks>
    public sealed class FoodItemSpec
    {
        /// <summary>Creates a food item definition.</summary>
        /// <remarks>
        /// Pre: id non-empty; method is null (raw/ration) or a defined CookingMethod; target and element are defined enums;
        ///      1 &lt;= tier &lt;= 3; effect != null; freeDish is null for catalog dishes.
        /// Post: properties equal arguments.
        /// Side effects: none.
        /// </remarks>
        public FoodItemSpec(
            string id,
            CookingMethod? method,
            FoodTarget target,
            Element element,
            int tier,
            FoodEffectSpec effect,
            FreeDishDescriptor? freeDish = null)
        {
            Contract.Requires(!string.IsNullOrEmpty(id), "id must be non-empty");
            Contract.Requires(!method.HasValue || Enum.IsDefined(typeof(CookingMethod), method.Value), "method must be defined");
            Contract.Requires(Enum.IsDefined(typeof(FoodTarget), target), "target must be defined");
            Contract.Requires(Enum.IsDefined(typeof(Element), element), "element must be defined");
            Contract.Requires(tier >= 1 && tier <= 3, "tier must be 1..3");
            Contract.Requires(effect != null, "effect must not be null");

            Id = id;
            Method = method;
            Target = target;
            Element = element;
            Tier = tier;
            Effect = effect;
            FreeDish = freeDish;
        }

        /// <summary>Unique snake_case food item id.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Cooking method that produced this food, or null if uncooked.</summary>
        /// <remarks>Pre: none. Post: null or defined CookingMethod. Side effects: none.</remarks>
        public CookingMethod? Method { get; }

        /// <summary>Who receives the effects when consumed.</summary>
        /// <remarks>Pre: none. Post: defined FoodTarget. Side effects: none.</remarks>
        public FoodTarget Target { get; }

        /// <summary>Elemental affinity of this dish.</summary>
        /// <remarks>Pre: none. Post: defined Element. Side effects: none.</remarks>
        public Element Element { get; }

        /// <summary>Dish tier (1..3).</summary>
        /// <remarks>Pre: none. Post: 1..3. Side effects: none.</remarks>
        public int Tier { get; }

        /// <summary>Healing, revival, cleanse, and buff specification.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public FoodEffectSpec Effect { get; }

        /// <summary>Naming summary when this item was generated by free-form pot cooking; null for catalog dishes.</summary>
        /// <remarks>Pre: none. Post: null or descriptor. Side effects: none.</remarks>
        public FreeDishDescriptor? FreeDish { get; }
    }

    /// <summary>Cooking recipe turning raw ingredients into a <see cref="FoodItemSpec"/>.</summary>
    /// <remarks>Pre: valid parameters. Post: immutable. Side effects: none.</remarks>
    public sealed class RecipeSpec
    {
        /// <summary>Creates a validated cooking recipe.</summary>
        /// <remarks>
        /// Pre: id non-empty; method and elementAffinity defined; requirements non-null with 1..4 entries;
        ///      outputFoodId non-empty; outputQuantity &gt;= 1.
        /// Post: properties equal arguments (requirements defensively copied).
        /// Side effects: allocates array copy.
        /// </remarks>
        public RecipeSpec(
            string id,
            CookingMethod method,
            IReadOnlyList<RecipeIngredientRequirement> requirements,
            string outputFoodId,
            int outputQuantity,
            Element elementAffinity)
        {
            Contract.Requires(!string.IsNullOrEmpty(id), "id must be non-empty");
            Contract.Requires(Enum.IsDefined(typeof(CookingMethod), method), "method must be defined");
            Contract.Requires(requirements != null && requirements.Count >= 1 && requirements.Count <= 4, "requirements must have 1..4 entries");
            Contract.Requires(!string.IsNullOrEmpty(outputFoodId), "outputFoodId must be non-empty");
            Contract.Requires(outputQuantity >= 1, "outputQuantity must be >= 1");
            Contract.Requires(Enum.IsDefined(typeof(Element), elementAffinity), "elementAffinity must be defined");

            Id = id;
            Method = method;
            OutputFoodId = outputFoodId;
            OutputQuantity = outputQuantity;
            ElementAffinity = elementAffinity;

            var copy = new RecipeIngredientRequirement[requirements.Count];
            for (int i = 0; i < requirements.Count; i++)
            {
                Contract.Requires(requirements[i].Quantity >= 1, "requirement quantity must be >= 1");
                copy[i] = requirements[i];
            }

            Requirements = copy;
        }

        /// <summary>Unique recipe id.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string Id { get; }

        /// <summary>Cooking method required by this recipe.</summary>
        /// <remarks>Pre: none. Post: defined CookingMethod. Side effects: none.</remarks>
        public CookingMethod Method { get; }

        /// <summary>Ingredients consumed per craft.</summary>
        /// <remarks>Pre: none. Post: 1..4 entries. Side effects: none.</remarks>
        public IReadOnlyList<RecipeIngredientRequirement> Requirements { get; }

        /// <summary>Food item id produced on success.</summary>
        /// <remarks>Pre: none. Post: non-empty. Side effects: none.</remarks>
        public string OutputFoodId { get; }

        /// <summary>Base quantity produced per craft.</summary>
        /// <remarks>Pre: none. Post: &gt;= 1. Side effects: none.</remarks>
        public int OutputQuantity { get; }

        /// <summary>Elemental affinity used for quality/synergy checks.</summary>
        /// <remarks>Pre: none. Post: defined Element. Side effects: none.</remarks>
        public Element ElementAffinity { get; }
    }

    /// <summary>Registry of ingredients, food items, and cooking recipes.</summary>
    /// <remarks>Pre: register definitions before querying. Post: deterministic lookups. Side effects: mutates internal dictionaries on registration.</remarks>
    public sealed class FoodCatalog
    {
        private readonly Dictionary<string, IngredientSpec> _ingredients = new Dictionary<string, IngredientSpec>();
        private readonly Dictionary<string, FoodItemSpec> _foods = new Dictionary<string, FoodItemSpec>();
        private readonly Dictionary<string, RecipeSpec> _recipes = new Dictionary<string, RecipeSpec>();
        private readonly List<IngredientSpec> _ingredientList = new List<IngredientSpec>();
        private readonly Dictionary<string, ProcessSpec> _processes = new Dictionary<string, ProcessSpec>();
        private readonly List<ProcessSpec> _processList = new List<ProcessSpec>();

        /// <summary>All registered ingredients in registration order.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public IReadOnlyList<IngredientSpec> Ingredients => _ingredientList;

        /// <summary>Registered foods for inventory presentation.</summary>
        /// <remarks>Pre: do not register during enumeration. Post: all registered foods. Side effects: none.</remarks>
        public IEnumerable<FoodItemSpec> Foods => _foods.Values;

        /// <summary>Registered recipes for cooking presentation.</summary>
        /// <remarks>Pre: do not register during enumeration. Post: all registered recipes. Side effects: none.</remarks>
        public IEnumerable<RecipeSpec> Recipes => _recipes.Values;

        /// <summary>All registered processing recipes in registration order.</summary>
        /// <remarks>Pre: none. Post: non-null. Side effects: none.</remarks>
        public IReadOnlyList<ProcessSpec> Processes => _processList;

        /// <summary>Registers a processing recipe whose ingredients are already registered.</summary>
        /// <remarks>
        /// Pre: spec != null; id unique; input and output registered; output IsProcessed;
        ///      input is raw, except Ferment may take a Smoked good (cuisine.md#1: 燻乾品可再發酵).
        /// Post: Processes contains spec; TryGetProcess(spec.Id, ...) is true.
        /// Side effects: mutates catalog.
        /// </remarks>
        public void RegisterProcess(ProcessSpec spec)
        {
            Contract.Requires(spec != null, "spec must not be null");
            Contract.Requires(!_processes.ContainsKey(spec.Id), "duplicate process id: " + spec.Id);
            bool hasInput = _ingredients.TryGetValue(spec.InputIngredientId, out IngredientSpec input);
            bool hasOutput = _ingredients.TryGetValue(spec.OutputIngredientId, out IngredientSpec output);
            Contract.Requires(hasInput, "unknown process input: " + spec.InputIngredientId);
            Contract.Requires(hasOutput, "unknown process output: " + spec.OutputIngredientId);
            Contract.Requires(!hasOutput || output.IsProcessed, "process output must be a processed good");
            Contract.Requires(!hasInput || !input.IsProcessed || (spec.Kind == ProcessKind.Ferment && (input.Traits.Tags & IngredientTags.Smoked) != 0),
                "processed goods cannot be processed again, except smoked goods into ferment");
            _processes.Add(spec.Id, spec);
            _processList.Add(spec);
        }

        /// <summary>Looks up a processing recipe by id.</summary>
        /// <remarks>Pre: id != null. Post: true iff found. Side effects: none.</remarks>
        public bool TryGetProcess(string id, out ProcessSpec spec)
        {
            Contract.Requires(id != null, "id must not be null");
            return _processes.TryGetValue(id, out spec);
        }

        /// <summary>Registers an ingredient.</summary>
        /// <remarks>
        /// Pre: spec != null; id not already registered.
        /// Post: TryGetIngredient(spec.Id, ...) returns true.
        /// Side effects: mutates catalog.
        /// </remarks>
        public void RegisterIngredient(IngredientSpec spec)
        {
            Contract.Requires(spec != null, "spec must not be null");
            Contract.Requires(!_ingredients.ContainsKey(spec.Id), "duplicate ingredient id: " + spec.Id);
            _ingredients.Add(spec.Id, spec);
            _ingredientList.Add(spec);
        }

        /// <summary>Registers a food item.</summary>
        /// <remarks>
        /// Pre: spec != null; id not already registered.
        /// Post: TryGetFood(spec.Id, ...) returns true.
        /// Side effects: mutates catalog.
        /// </remarks>
        public void RegisterFood(FoodItemSpec spec)
        {
            Contract.Requires(spec != null, "spec must not be null");
            Contract.Requires(!_foods.ContainsKey(spec.Id), "duplicate food id: " + spec.Id);
            _foods.Add(spec.Id, spec);
        }

        /// <summary>Registers a cooking recipe whose <see cref="RecipeSpec.OutputFoodId"/> is already registered.</summary>
        /// <remarks>
        /// Pre: spec != null; id not already registered; outputFoodId exists in catalog.
        /// Post: TryGetRecipe(spec.Id, ...) returns true.
        /// Side effects: mutates catalog.
        /// </remarks>
        public void RegisterRecipe(RecipeSpec spec)
        {
            Contract.Requires(spec != null, "spec must not be null");
            Contract.Requires(!_recipes.ContainsKey(spec.Id), "duplicate recipe id: " + spec.Id);
            Contract.Requires(_foods.ContainsKey(spec.OutputFoodId), "unknown outputFoodId: " + spec.OutputFoodId);
            _recipes.Add(spec.Id, spec);
        }

        /// <summary>Looks up an ingredient by id.</summary>
        /// <remarks>Pre: id != null. Post: true iff found. Side effects: none.</remarks>
        public bool TryGetIngredient(string id, out IngredientSpec spec)
        {
            Contract.Requires(id != null, "id must not be null");
            return _ingredients.TryGetValue(id, out spec);
        }

        /// <summary>Looks up a food item by id.</summary>
        /// <remarks>Pre: id != null. Post: true iff found. Side effects: none.</remarks>
        public bool TryGetFood(string id, out FoodItemSpec spec)
        {
            Contract.Requires(id != null, "id must not be null");
            return _foods.TryGetValue(id, out spec);
        }

        /// <summary>Looks up a recipe by id.</summary>
        /// <remarks>Pre: id != null. Post: true iff found. Side effects: none.</remarks>
        public bool TryGetRecipe(string id, out RecipeSpec spec)
        {
            Contract.Requires(id != null, "id must not be null");
            return _recipes.TryGetValue(id, out spec);
        }

        /// <summary>Collects all registered ingredients yielded by <paramref name="source"/> into <paramref name="into"/>.</summary>
        /// <remarks>
        /// Pre: source is a defined FoodAcquisitionMethod; into != null.
        /// Post: into cleared and populated with matching raw ingredients (processed goods are never gathered).
        /// Side effects: mutates into.
        /// </remarks>
        public void GetIngredientsBySource(FoodAcquisitionMethod source, List<IngredientSpec> into)
        {
            Contract.Requires(Enum.IsDefined(typeof(FoodAcquisitionMethod), source), "source must be defined");
            Contract.Requires(into != null, "into must not be null");
            into.Clear();
            for (int i = 0; i < _ingredientList.Count; i++)
            {
                if (_ingredientList[i].Source == source && !_ingredientList[i].IsProcessed)
                {
                    into.Add(_ingredientList[i]);
                }
            }
        }

        /// <summary>Builds the default catalog matching <c>Data/_demo/ingredients</c> and <c>Data/_demo/foods</c>.</summary>
        /// <remarks>Pre: none. Post: non-null catalog covering all 9 acquisition methods, 8 cooking methods, and 6 process kinds. Side effects: allocates catalog.</remarks>
        public static FoodCatalog CreateDefault()
        {
            var cat = new FoodCatalog();

            // REQ: docs/design/cuisine.md — ingredient traits (role, tags, cooked heal, effect family/points) drive free-form pots.
            // Raw ingredients covering all 9 FoodAcquisitionMethods.
            Raw(cat, "ing_boar_flank", IngredientCategory.MeatGround, FoodAcquisitionMethod.HuntGround, Element.Fire, 1, 0,
                Fx(IngredientTags.RedMeat | IngredientTags.Raw, 15, FoodEffectFamily.Mastery, Element.Fire, 1));
            Raw(cat, "ing_beast_prime_rib", IngredientCategory.MeatGround, FoodAcquisitionMethod.HuntGround, Element.Earth, 2, 0,
                Fx(IngredientTags.RedMeat | IngredientTags.Raw, 25, FoodEffectFamily.Mastery, Element.Earth, 2));
            Raw(cat, "ing_skyfowl_breast", IngredientCategory.MeatSky, FoodAcquisitionMethod.HuntSky, Element.Wind, 1, 0,
                Fx(IngredientTags.Poultry | IngredientTags.Raw, 15, FoodEffectFamily.Swift, Element.Wind, 1));
            Raw(cat, "ing_gale_wing_cut", IngredientCategory.MeatSky, FoodAcquisitionMethod.HuntSky, Element.Thunder, 2, 0,
                Fx(IngredientTags.Poultry | IngredientTags.Raw, 20, FoodEffectFamily.Nimble, Element.Thunder, 2));
            Raw(cat, "ing_snare_hare_meat", IngredientCategory.MeatGround, FoodAcquisitionMethod.SetTrap, Element.Shadow, 1, 0,
                Fx(IngredientTags.RedMeat | IngredientTags.Raw, 12, FoodEffectFamily.Fierce, Element.Shadow, 1));
            Raw(cat, "ing_trapped_truffle_grub", IngredientCategory.SpiceOil, FoodAcquisitionMethod.SetTrap, Element.Earth, 2, 0,
                new CuisineTraits(IngredientRole.Critter, IngredientTags.Critter, 0, FoodEffectFamily.Bounty, Element.Earth, 2));
            Raw(cat, "ing_river_trout", IngredientCategory.Seafood, FoodAcquisitionMethod.Fishing, Element.Water, 1, 5,
                Plain(IngredientTags.Fish | IngredientTags.Raw, 15));
            Raw(cat, "ing_abyssal_salmon", IngredientCategory.Seafood, FoodAcquisitionMethod.Fishing, Element.Ice, 2, 8,
                Fx(IngredientTags.Fish | IngredientTags.Raw, 25, FoodEffectFamily.Resist, Element.Water, 2));
            Raw(cat, "ing_net_shrimp", IngredientCategory.Seafood, FoodAcquisitionMethod.CastNet, Element.Water, 1, 4,
                Fx(IngredientTags.Shellfish | IngredientTags.Raw, 10, FoodEffectFamily.Nimble, Element.Water, 1));
            Raw(cat, "ing_net_kelp_scallop", IngredientCategory.Seafood, FoodAcquisitionMethod.CastNet, Element.Water, 2, 6,
                Fx(IngredientTags.Shellfish | IngredientTags.Raw, 15, FoodEffectFamily.Keen, Element.Metal, 2));
            Raw(cat, "ing_wild_berry", IngredientCategory.BerryHerb, FoodAcquisitionMethod.ForageBerries, Element.Wood, 1, 10,
                Plain(IngredientTags.Fruit, 10));
            Raw(cat, "ing_vital_fern_herb", IngredientCategory.BerryHerb, FoodAcquisitionMethod.ForageBerries, Element.Wood, 2, 15,
                Fx(IngredientTags.Herb, 15, FoodEffectFamily.Revive, Element.Wood, 1));
            Raw(cat, "ing_sun_wheat", IngredientCategory.GrainCrop, FoodAcquisitionMethod.HarvestCrops, Element.Light, 1, 0,
                Fx(IngredientTags.Grain, 10, FoodEffectFamily.Focus, Element.Light, 1));
            Raw(cat, "ing_golden_oilseed", IngredientCategory.SpiceOil, FoodAcquisitionMethod.HarvestCrops, Element.Fire, 2, 0,
                Fx(IngredientTags.Nut, 5, FoodEffectFamily.Bounty, Element.Fire, 2));
            Raw(cat, "ing_chimera_milk", IngredientCategory.Dairy, FoodAcquisitionMethod.ProduceMilk, Element.Wood, 1, 12,
                Plain(IngredientTags.Dairy, 15));
            Raw(cat, "ing_sky_nest_egg", IngredientCategory.Egg, FoodAcquisitionMethod.HuntSky, Element.Light, 1, 8,
                Fx(IngredientTags.Egg, 12, FoodEffectFamily.Revive, Element.Light, 1));
            Raw(cat, "ing_sea_bass", IngredientCategory.Seafood, FoodAcquisitionMethod.Fishing, Element.Water, 1, 5,
                Plain(IngredientTags.Fish | IngredientTags.Raw, 18));
            Raw(cat, "ing_sea_salt", IngredientCategory.SpiceOil, FoodAcquisitionMethod.CastNet, Element.Water, 1, 0,
                new CuisineTraits(IngredientRole.Seasoning, IngredientTags.Salt, 0));
            Raw(cat, "ing_wild_honey", IngredientCategory.BerryHerb, FoodAcquisitionMethod.ForageBerries, Element.Wood, 2, 10,
                Fx(IngredientTags.Sugar, 10, FoodEffectFamily.Tame, Element.Wood, 1));
            Raw(cat, "ing_wild_root", IngredientCategory.GrainCrop, FoodAcquisitionMethod.HarvestCrops, Element.Earth, 1, 0,
                Fx(IngredientTags.Vegetable, 12, FoodEffectFamily.Guard, Element.Earth, 1));
            Raw(cat, "ing_hard_chestnut", IngredientCategory.BerryHerb, FoodAcquisitionMethod.ForageBerries, Element.Earth, 1, 5,
                Fx(IngredientTags.Nut, 8, FoodEffectFamily.Guard, Element.Earth, 1));
            Raw(cat, "ing_fire_pepper", IngredientCategory.SpiceOil, FoodAcquisitionMethod.ForageBerries, Element.Fire, 1, 0,
                Fx(IngredientTags.Vegetable | IngredientTags.Spice, 5, FoodEffectFamily.Resist, Element.Ice, 1));
            Raw(cat, "ing_ice_mint", IngredientCategory.BerryHerb, FoodAcquisitionMethod.ForageBerries, Element.Ice, 1, 5,
                Fx(IngredientTags.Herb, 5, FoodEffectFamily.Focus, Element.Ice, 1));
            Raw(cat, "ing_gale_mushroom", IngredientCategory.Fungi, FoodAcquisitionMethod.ForageFungi, Element.Wind, 1, 0,
                Fx(IngredientTags.Mushroom, 10, FoodEffectFamily.Swift, Element.Wind, 1));
            Raw(cat, "ing_glowcap", IngredientCategory.Fungi, FoodAcquisitionMethod.ForageFungi, Element.Shadow, 1, 0,
                Fx(IngredientTags.Mushroom, 10, FoodEffectFamily.Fierce, Element.Shadow, 1));
            Raw(cat, "ing_truffle", IngredientCategory.Fungi, FoodAcquisitionMethod.ForageFungi, Element.Earth, 2, 0,
                Fx(IngredientTags.Mushroom, 12, FoodEffectFamily.Bounty, Element.Earth, 2));
            Raw(cat, "ing_hopper_bug", IngredientCategory.Critter, FoodAcquisitionMethod.SetTrap, Element.Thunder, 1, 0,
                new CuisineTraits(IngredientRole.Critter, IngredientTags.Critter, 0, FoodEffectFamily.Nimble, Element.Thunder, 1));

            // Processed goods: one raw unit becomes one processed unit (cuisine.md#3).
            Processed(cat, ProcessKind.Freeze, "ing_sea_bass", "ing_chilled_sea_bass");
            Processed(cat, ProcessKind.Freeze, "ing_river_trout", "ing_chilled_trout");
            Processed(cat, ProcessKind.Freeze, "ing_abyssal_salmon", "ing_chilled_salmon");
            Processed(cat, ProcessKind.Freeze, "ing_net_shrimp", "ing_chilled_shrimp");
            Processed(cat, ProcessKind.Freeze, "ing_skyfowl_breast", "ing_chilled_fowl");
            Processed(cat, ProcessKind.Freeze, "ing_wild_berry", "ing_chilled_berry");
            Processed(cat, ProcessKind.Grind, "ing_sun_wheat", "ing_wheat_flour");
            Processed(cat, ProcessKind.Grind, "ing_vital_fern_herb", "ing_fern_powder");
            Processed(cat, ProcessKind.Grind, "ing_fire_pepper", "ing_pepper_powder");
            Processed(cat, ProcessKind.Press, "ing_golden_oilseed", "ing_seed_oil");
            Processed(cat, ProcessKind.Churn, "ing_chimera_milk", "ing_butter");
            Processed(cat, ProcessKind.Ferment, "ing_chimera_milk", "ing_cheese");
            Processed(cat, ProcessKind.Ferment, "ing_sea_bass", "ing_fish_sauce", IngredientRole.Seasoning, 0);
            Processed(cat, ProcessKind.Ferment, "ing_sun_wheat", "ing_wheat_koji");
            Processed(cat, ProcessKind.Smoke, "ing_boar_flank", "ing_smoked_boar");
            Processed(cat, ProcessKind.Smoke, "ing_snare_hare_meat", "ing_smoked_hare");
            Processed(cat, ProcessKind.Smoke, "ing_river_trout", "ing_smoked_trout");
            Processed(cat, ProcessKind.Smoke, "ing_wild_berry", "ing_dried_berry");
            Processed(cat, ProcessKind.Smoke, "ing_beast_prime_rib", "ing_smoked_rib");
            Processed(cat, ProcessKind.Ferment, "ing_smoked_rib", "ing_aged_beast_meat");
            Processed(cat, ProcessKind.Ferment, "ing_wild_berry", "ing_pickled_berry");
            Processed(cat, ProcessKind.Ferment, "ing_wild_root", "ing_pickled_root");

            // Cooked foods covering all 7 CookingMethods
            RegisterDish(cat, "food_charred_boar_skewer", CookingMethod.Grill, FoodTarget.PlayerAndParty, Element.Fire, 1, 1,
                new[] { RecipeIngredientRequirement.ById("ing_boar_flank", 1), RecipeIngredientRequirement.ByCategory(IngredientCategory.BerryHerb, 1) },
                new FoodEffectSpec(25, 20, 0, false, 0, 0, 0, 120_000,
                    new[] { new EffectSpec(EffectType.MasteryMod, false, Element.Fire, 15, 0, 1) }));

            RegisterDish(cat, "food_hearty_beast_stew", CookingMethod.Stew, FoodTarget.PlayerAndParty, Element.Earth, 2, 1,
                new[] { RecipeIngredientRequirement.ById("ing_beast_prime_rib", 1), RecipeIngredientRequirement.ById("ing_chimera_milk", 1), RecipeIngredientRequirement.ByCategory(IngredientCategory.GrainCrop, 1) },
                new FoodEffectSpec(45, 40, 0, false, 0, 0, 0, 180_000,
                    new[] { new EffectSpec(EffectType.ResistMod, false, Element.Fire, 15, 0, 1), new EffectSpec(EffectType.ResistMod, false, Element.Earth, 15, 0, 1) }));

            RegisterDish(cat, "food_gale_wing_stir_fry", CookingMethod.StirFry, FoodTarget.ActiveCreatures, Element.Wind, 2, 2,
                new[] { RecipeIngredientRequirement.ById("ing_skyfowl_breast", 1), RecipeIngredientRequirement.ById("ing_golden_oilseed", 1) },
                new FoodEffectSpec(20, 10, 0, false, -15, 0, 0, 120_000,
                    new[] { new EffectSpec(EffectType.CdModPercent, true, default, -12, 0, 1), new EffectSpec(EffectType.MoveSpeedModPercent, false, default, 15, 0, 1) }));

            RegisterDish(cat, "food_crispy_shrimp_tempura", CookingMethod.DeepFry, FoodTarget.PlayerAndParty, Element.Thunder, 2, 1,
                new[] { RecipeIngredientRequirement.ById("ing_net_shrimp", 2), RecipeIngredientRequirement.ById("ing_golden_oilseed", 1), RecipeIngredientRequirement.ById("ing_sun_wheat", 1) },
                new FoodEffectSpec(30, 25, 0, false, 0, 0, 15, 120_000,
                    new[] { new EffectSpec(EffectType.EvasionModPercent, false, default, 12, 0, 1), new EffectSpec(EffectType.MasteryMod, false, Element.Thunder, 15, 0, 1) }));

            RegisterDish(cat, "food_steamed_abyssal_salmon", CookingMethod.Steam, FoodTarget.Party, Element.Water, 2, 1,
                new[] { RecipeIngredientRequirement.ById("ing_abyssal_salmon", 1), RecipeIngredientRequirement.ById("ing_vital_fern_herb", 1) },
                new FoodEffectSpec(50, 30, 0, true, 0, 0, 0, 150_000,
                    new[] { new EffectSpec(EffectType.ResistMod, false, Element.Water, 20, 0, 1) }));

            RegisterDish(cat, "food_chilled_herb_salad", CookingMethod.ColdMix, FoodTarget.PlayerAndParty, Element.Wood, 1, 1,
                new[] { RecipeIngredientRequirement.ById("ing_wild_berry", 2), RecipeIngredientRequirement.ById("ing_net_kelp_scallop", 1) },
                new FoodEffectSpec(20, 25, 0, false, 0, 10, 0, 120_000,
                    new[] { new EffectSpec(EffectType.RangeModPercent, false, default, 10, 0, 1) }));

            RegisterDish(cat, "food_revival_herb_powder", CookingMethod.Grind, FoodTarget.Party, Element.Light, 2, 2,
                new[] { RecipeIngredientRequirement.ById("ing_vital_fern_herb", 1), RecipeIngredientRequirement.ById("ing_sun_wheat", 1), RecipeIngredientRequirement.ById("ing_sky_nest_egg", 1) },
                new FoodEffectSpec(35, 0, 40, true, 0, 0, 0, 0, null));

            // Signature dishes added with the cuisine routes (cuisine.md#6).
            RegisterDish(cat, "food_steamed_sea_bass", CookingMethod.Steam, FoodTarget.PlayerAndParty, Element.Water, 1, 1,
                new[] { RecipeIngredientRequirement.ById("ing_chilled_sea_bass", 1), RecipeIngredientRequirement.ByCategory(IngredientCategory.BerryHerb, 1) },
                new FoodEffectSpec(40, 30, 0, false, 0, 0, 0, 150_000,
                    new[] { new EffectSpec(EffectType.MasteryMod, false, Element.Water, 10, 0, 1) }));

            RegisterDish(cat, "food_sea_bass_crudo", CookingMethod.ColdMix, FoodTarget.PlayerAndParty, Element.Ice, 1, 1,
                new[] { RecipeIngredientRequirement.ById("ing_chilled_sea_bass", 1), RecipeIngredientRequirement.ById("ing_sea_salt", 1) },
                new FoodEffectSpec(25, 20, 0, true, 0, 0, 0, 120_000,
                    new[] { new EffectSpec(EffectType.ResistMod, false, Element.Ice, 15, 0, 1) }));

            RegisterDish(cat, "food_aged_beast_ragout", CookingMethod.Stew, FoodTarget.PlayerAndParty, Element.Earth, 3, 1,
                new[] { RecipeIngredientRequirement.ById("ing_aged_beast_meat", 1), RecipeIngredientRequirement.ById("ing_chimera_milk", 1), RecipeIngredientRequirement.ById("ing_wild_root", 1) },
                new FoodEffectSpec(50, 40, 0, false, 0, 0, 0, 180_000,
                    new[] { new EffectSpec(EffectType.CritDamageMod, false, Element.Metal, 20, 0, 1), new EffectSpec(EffectType.MasteryMod, false, Element.Earth, 10, 0, 1) }));

            RegisterDish(cat, "food_gale_fried_fowl", CookingMethod.DeepFry, FoodTarget.ActiveCreatures, Element.Wind, 2, 1,
                new[] { RecipeIngredientRequirement.ById("ing_skyfowl_breast", 1), RecipeIngredientRequirement.ById("ing_wheat_flour", 1), RecipeIngredientRequirement.ById("ing_seed_oil", 1) },
                new FoodEffectSpec(25, 15, 0, false, 0, 0, 0, 150_000,
                    new[] { new EffectSpec(EffectType.MoveSpeedModPercent, false, Element.Metal, 10, 0, 1), new EffectSpec(EffectType.MasteryMod, false, Element.Wind, 10, 0, 1) }));

            RegisterDish(cat, "food_meadow_cheese_soup", CookingMethod.Stew, FoodTarget.PlayerAndParty, Element.Wood, 2, 1,
                new[] { RecipeIngredientRequirement.ById("ing_cheese", 1), RecipeIngredientRequirement.ById("ing_chimera_milk", 1), RecipeIngredientRequirement.ByCategory(IngredientCategory.GrainCrop, 1) },
                new FoodEffectSpec(45, 40, 0, false, 0, 0, 0, 180_000,
                    new[] { new EffectSpec(EffectType.StaggerCapacityModPercent, false, Element.Metal, 20, 0, 1), new EffectSpec(EffectType.BlockReductionMod, false, Element.Metal, 10, 0, 1) }));

            RegisterDish(cat, "food_frostberry_smoothie", CookingMethod.ChillBrew, FoodTarget.PlayerAndParty, Element.Ice, 1, 1,
                new[] { RecipeIngredientRequirement.ById("ing_chilled_berry", 2), RecipeIngredientRequirement.ById("ing_wild_honey", 1) },
                new FoodEffectSpec(20, 20, 0, false, 0, 0, 0, 150_000,
                    new[] { new EffectSpec(EffectType.ResistMod, false, Element.Fire, 20, 0, 1) }));

            RegisterDish(cat, "food_glowcap_tonic", CookingMethod.Stew, FoodTarget.Party, Element.Shadow, 2, 1,
                new[] { RecipeIngredientRequirement.ById("ing_glowcap", 2), RecipeIngredientRequirement.ById("ing_wheat_koji", 1) },
                new FoodEffectSpec(25, 10, 0, false, 0, 0, 0, 180_000,
                    new[] { new EffectSpec(EffectType.MasteryMod, false, Element.Shadow, 10, 0, 1), new EffectSpec(EffectType.CritDamageMod, false, Element.Metal, 10, 0, 1) }));

            RegisterDish(cat, "food_truffle_butter_grill", CookingMethod.Grill, FoodTarget.PlayerAndParty, Element.Earth, 2, 1,
                new[] { RecipeIngredientRequirement.ById("ing_truffle", 1), RecipeIngredientRequirement.ById("ing_butter", 1), RecipeIngredientRequirement.ByCategory(IngredientCategory.Fungi, 1) },
                new FoodEffectSpec(30, 25, 0, false, 0, 0, 15, 150_000));

            RegisterDish(cat, "food_honey_glazed_nuts", CookingMethod.ColdMix, FoodTarget.PlayerAndParty, Element.Earth, 1, 1,
                new[] { RecipeIngredientRequirement.ById("ing_hard_chestnut", 2), RecipeIngredientRequirement.ById("ing_wild_honey", 1) },
                new FoodEffectSpec(15, 15, 0, false, 0, 6, 0, 150_000,
                    new[] { new EffectSpec(EffectType.StaggerCapacityModPercent, false, Element.Metal, 10, 0, 1), new EffectSpec(EffectType.BlockReductionMod, false, Element.Metal, 5, 0, 1) }));

            // Route showcase dishes (cuisine.md#5 漁獲冷盤／禽蛋麵點, #3 醃漬); numbers provisional per Q-F3.
            RegisterDish(cat, "food_chilled_seafood_salad", CookingMethod.ColdMix, FoodTarget.PlayerAndParty, Element.Ice, 2, 1,
                new[] { RecipeIngredientRequirement.ById("ing_chilled_shrimp", 1), RecipeIngredientRequirement.ById("ing_net_kelp_scallop", 1), RecipeIngredientRequirement.ById("ing_sea_salt", 1) },
                new FoodEffectSpec(25, 20, 0, true, 0, 0, 0, 150_000,
                    new[] { new EffectSpec(EffectType.AccuracyMod, false, default, 6, 0, 1), new EffectSpec(EffectType.CritRateMod, false, default, 6, 0, 1) }));

            RegisterDish(cat, "food_golden_wheat_tempura", CookingMethod.DeepFry, FoodTarget.PlayerAndParty, Element.Light, 2, 1,
                new[] { RecipeIngredientRequirement.ById("ing_wheat_flour", 1), RecipeIngredientRequirement.ById("ing_seed_oil", 1), RecipeIngredientRequirement.ById("ing_sky_nest_egg", 1), RecipeIngredientRequirement.ByCategory(IngredientCategory.BerryHerb, 1) },
                new FoodEffectSpec(35, 30, 0, false, 0, 0, 0, 150_000,
                    new[] { new EffectSpec(EffectType.CdModPercent, true, default, -8, 0, 1) }));

            RegisterDish(cat, "food_pickled_root_hare_stew", CookingMethod.Stew, FoodTarget.PlayerAndParty, Element.Shadow, 2, 1,
                new[] { RecipeIngredientRequirement.ById("ing_pickled_root", 1), RecipeIngredientRequirement.ById("ing_snare_hare_meat", 1), RecipeIngredientRequirement.ByCategory(IngredientCategory.GrainCrop, 1) },
                new FoodEffectSpec(35, 30, 0, false, 0, 0, 0, 300_000,
                    new[] { new EffectSpec(EffectType.MasteryMod, false, Element.Shadow, 10, 0, 1), new EffectSpec(EffectType.StaggerCapacityModPercent, false, Element.Metal, 10, 0, 1) }));

            RegisterDish(cat, "food_pickled_berry_cooler", CookingMethod.ChillBrew, FoodTarget.PlayerAndParty, Element.Ice, 1, 1,
                new[] { RecipeIngredientRequirement.ById("ing_pickled_berry", 1), RecipeIngredientRequirement.ById("ing_ice_mint", 1), RecipeIngredientRequirement.ById("ing_wild_honey", 1) },
                new FoodEffectSpec(15, 20, 0, false, 0, 3, 0, 300_000,
                    new[] { new EffectSpec(EffectType.CdModPercent, true, default, -4, 0, 1) }));

            // Failure dish for pots that miss their method's conditions (cuisine.md#4).
            cat.RegisterFood(new FoodItemSpec(PotCookingRules.DubiousFoodId, null, FoodTarget.PlayerAndParty, Element.Earth, 1,
                new FoodEffectSpec(5, 5, 0, false, 0, 0, 0, 0)));

            return cat;
        }

        private static CuisineTraits Plain(IngredientTags tags, int cookHeal) => new CuisineTraits(IngredientRole.Base, tags, cookHeal);

        private static CuisineTraits Fx(IngredientTags tags, int cookHeal, FoodEffectFamily family, Element element, int points) =>
            new CuisineTraits(IngredientRole.Effect, tags, cookHeal, family, element, points);

        private static void Raw(FoodCatalog cat, string id, IngredientCategory category, FoodAcquisitionMethod source, Element element,
            int tier, int rawHeal, in CuisineTraits traits)
        {
            cat.RegisterIngredient(new IngredientSpec(id, category, source, element, tier, rawHeal, traits, false));
        }

        private static void Processed(FoodCatalog cat, ProcessKind kind, string inputId, string outputId,
            IngredientRole? role = null, int? cookHeal = null)
        {
            bool known = cat.TryGetIngredient(inputId, out IngredientSpec input);
            Contract.Requires(known, "unknown process input: " + inputId);
            CuisineTraits t = input.Traits;
            IngredientTags tags = t.Tags;
            int points = t.EffectPoints;
            int durationAdd = t.DurationAddMs;
            int heal = t.CookHealPercent;
            int rawHeal = input.RawHealPercent;
            switch (kind)
            {
                case ProcessKind.Freeze:
                    tags = (tags & ~IngredientTags.Raw) | IngredientTags.Chilled;
                    break;
                case ProcessKind.Grind:
                    tags = (tags & IngredientTags.Grain) != 0
                        ? (tags & ~IngredientTags.Grain) | IngredientTags.Flour | IngredientTags.Powder
                        : tags | IngredientTags.Powder;
                    // Grinding concentrates: one slot carries twice the points (cuisine.md#3).
                    points = Math.Min(CuisineTraits.MaxEffectPoints, points * 2);
                    break;
                case ProcessKind.Press:
                    tags = (tags & ~IngredientTags.Nut) | IngredientTags.Oil;
                    break;
                case ProcessKind.Churn:
                    tags |= IngredientTags.Oil;
                    heal = Math.Min(100, heal + 5);
                    break;
                case ProcessKind.Ferment:
                    tags = (tags & ~IngredientTags.Raw) | IngredientTags.Fermented;
                    durationAdd = CuisineTraits.FermentedDurationAddMs;
                    break;
                case ProcessKind.Smoke:
                    tags = (tags & ~IngredientTags.Raw) | IngredientTags.Smoked;
                    // Smoked goods are rations: edible without a pot at half their cooked value.
                    rawHeal = Math.Max(rawHeal, heal / 2);
                    break;
            }

            if (cookHeal.HasValue) heal = cookHeal.Value;
            var traits = new CuisineTraits(role ?? t.Role, tags, heal, t.EffectFamily, t.EffectElement, points, durationAdd);
            cat.RegisterIngredient(new IngredientSpec(outputId, input.Category, input.Source, input.Element, input.Tier, rawHeal, traits, true));
            cat.RegisterProcess(new ProcessSpec("process_" + outputId, kind, inputId, outputId));
        }

        private static void RegisterDish(
            FoodCatalog cat,
            string id,
            CookingMethod method,
            FoodTarget target,
            Element element,
            int tier,
            int outputQty,
            RecipeIngredientRequirement[] reqs,
            FoodEffectSpec effect)
        {
            cat.RegisterFood(new FoodItemSpec(id, method, target, element, tier, effect));
            cat.RegisterRecipe(new RecipeSpec("recipe_" + id, method, reqs, id, outputQty, element));
        }
    }
}
