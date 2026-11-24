using System;
using System.Collections.Generic;
using System.Text;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Food
{
    /// <summary>What kind of dish a pot produced.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum PotDishKind
    {
        /// <summary>The pot exactly matched a catalog recipe; the recipe's fixed effects apply.</summary>
        Signature = 0,
        /// <summary>No recipe matched; heal and effect are derived from the ingredients.</summary>
        Free = 1,
        /// <summary>The method's conditions were not met; ingredients are still consumed.</summary>
        Dubious = 2,
    }

    /// <summary>Outcome of resolving (and optionally cooking) a pot.</summary>
    /// <remarks>Pre: constructed by <see cref="PotCookingRules"/>. Post: immutable. Side effects: none.</remarks>
    public readonly struct PotResolution
    {
        internal PotResolution(CookRefusalReason refusal, PotDishKind kind, RecipeSpec recipe, FoodItemSpec food, CookingQuality quality, int quantity)
        {
            Refusal = refusal;
            Kind = kind;
            Recipe = recipe;
            Food = food;
            Quality = quality;
            Quantity = quantity;
        }

        /// <summary>None when the pot can be (or was) cooked.</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public CookRefusalReason Refusal { get; }

        /// <summary>Dish kind; meaningful only when Refusal is None.</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public PotDishKind Kind { get; }

        /// <summary>Matched recipe for Signature dishes; null otherwise.</summary>
        /// <remarks>Pre: none. Post: non-null iff Kind == Signature and Refusal == None. Side effects: none.</remarks>
        public RecipeSpec Recipe { get; }

        /// <summary>Produced food definition; null when refused.</summary>
        /// <remarks>Pre: none. Post: non-null iff Refusal == None. Side effects: none.</remarks>
        public FoodItemSpec Food { get; }

        /// <summary>Deterministic quality (always Normal for Dubious).</summary>
        /// <remarks>Pre: none. Post: defined enum. Side effects: none.</remarks>
        public CookingQuality Quality { get; }

        /// <summary>Units produced (0 when refused).</summary>
        /// <remarks>Pre: none. Post: &gt;= 1 iff Refusal == None. Side effects: none.</remarks>
        public int Quantity { get; }
    }

    /// <summary>
    /// Free-form pot cooking: 1–5 ingredients + a method → signature, free-form, or dubious dish.
    /// Same-family effects add up, mixed families cancel; no random crit, so every client resolves the same dish.
    /// REQ: docs/design/cuisine.md#4-料理判定potcookingrules純函式
    /// </summary>
    /// <remarks>Pre: valid catalog and pot. Post: deterministic. Side effects: TryCook mutates inventory and may register a generated food in the catalog.</remarks>
    public static class PotCookingRules
    {
        /// <summary>Maximum ingredient units in one pot.</summary>
        public const int MaxPotSize = 5;

        /// <summary>Catalog id of the failure dish.</summary>
        public const string DubiousFoodId = "food_dubious_mash";

        // 暫定 numbers (cuisine.md#4); kept beside the rules until Data/balance.json#food exists.
        private const int BaseDurationMs = 60_000;
        private const int DubiousHealPercent = 5;

        private const IngredientTags MeatTags = IngredientTags.RedMeat | IngredientTags.Poultry | IngredientTags.Fish | IngredientTags.Shellfish;
        private const IngredientTags ChillBrewTags = IngredientTags.Fruit | IngredientTags.Dairy | IngredientTags.Herb | IngredientTags.Sugar |
                                                     IngredientTags.Critter | IngredientTags.Chilled;
        private const IngredientTags GrindTags = IngredientTags.Powder | IngredientTags.Herb | IngredientTags.Egg | IngredientTags.Grain |
                                                 IngredientTags.Critter | IngredientTags.Mushroom;

        /// <summary>Resolves what <paramref name="pot"/> would produce with <paramref name="method"/> without touching any inventory.</summary>
        /// <remarks>
        /// Pre: method defined; catalog non-null; pot non-null with 1..MaxPotSize entries, each a registered ingredient id.
        /// Post: Refusal is MissingCookingSkill when the method is locked, UnknownOutputFood when a dubious result has no catalog entry,
        ///       else None with Food set; a free dish's Food is generated and not yet registered.
        /// Side effects: allocates.
        /// </remarks>
        public static PotResolution Resolve(CookingMethod method, IReadOnlyList<string> pot, FoodCatalog catalog, in CookingContext context)
        {
            Contract.Requires(Enum.IsDefined(typeof(CookingMethod), method), "method must be defined");
            Contract.Requires(catalog != null, "catalog must not be null");
            Contract.Requires(pot != null && pot.Count >= 1 && pot.Count <= MaxPotSize, "pot must hold 1..MaxPotSize ingredients");

            var items = new IngredientSpec[pot.Count];
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < pot.Count; i++)
            {
                bool known = pot[i] != null && catalog.TryGetIngredient(pot[i], out items[i]);
                Contract.Requires(known, "pot ingredient must be registered: " + pot[i]);
                counts[pot[i]] = (counts.TryGetValue(pot[i], out int c) ? c : 0) + 1;
            }

            if (!CookingRules.HasSkillForMethod(method, context))
            {
                return Refused(CookRefusalReason.MissingCookingSkill);
            }

            bool usedChilled = false;
            for (int i = 0; i < items.Length; i++)
            {
                usedChilled |= (items[i].Traits.Tags & IngredientTags.Chilled) != 0;
            }

            RecipeSpec recipe = FindSignature(method, counts, pot.Count, catalog);
            if (recipe != null && catalog.TryGetFood(recipe.OutputFoodId, out FoodItemSpec signature))
            {
                CookingQuality quality = CookingRules.EvaluateQuality(recipe.ElementAffinity, method, usedChilled, context);
                return new PotResolution(CookRefusalReason.None, PotDishKind.Signature, recipe, signature, quality,
                    recipe.OutputQuantity + MasterworkBonus(quality));
            }

            if (!MeetsMethodConditions(method, items))
            {
                return catalog.TryGetFood(DubiousFoodId, out FoodItemSpec dubious)
                    ? new PotResolution(CookRefusalReason.None, PotDishKind.Dubious, null, dubious, CookingQuality.Normal, 1)
                    : Refused(CookRefusalReason.UnknownOutputFood);
            }

            FoodItemSpec free = BuildFreeDish(method, items);
            CookingQuality freeQuality = CookingRules.EvaluateQuality(free.Element, method, usedChilled, context);
            return new PotResolution(CookRefusalReason.None, PotDishKind.Free, null, free, freeQuality, 1 + MasterworkBonus(freeQuality));
        }

        /// <summary>Cooks <paramref name="pot"/>: consumes every listed unit and adds the resolved dish.</summary>
        /// <remarks>
        /// Pre: same as <see cref="Resolve"/>; inventory non-null.
        /// Post: Refusal == MissingIngredients (inventory unchanged) when any listed unit is not held; on None every listed unit
        ///       was consumed (dubious included), Food is registered in catalog, and CountFood(Food.Id) rose by Quantity.
        /// Side effects: mutates inventory; may register a generated free dish in catalog.
        /// </remarks>
        public static PotResolution TryCook(
            CookingMethod method,
            IReadOnlyList<string> pot,
            FoodCatalog catalog,
            FoodInventory inventory,
            in CookingContext context)
        {
            Contract.Requires(inventory != null, "inventory must not be null");
            PotResolution resolution = Resolve(method, pot, catalog, context);
            if (resolution.Refusal != CookRefusalReason.None)
            {
                return resolution;
            }

            var counts = new Dictionary<string, int>();
            for (int i = 0; i < pot.Count; i++)
            {
                counts[pot[i]] = (counts.TryGetValue(pot[i], out int c) ? c : 0) + 1;
            }

            foreach (KeyValuePair<string, int> kv in counts)
            {
                if (inventory.CountIngredient(kv.Key) < kv.Value)
                {
                    return Refused(CookRefusalReason.MissingIngredients);
                }
            }

            foreach (KeyValuePair<string, int> kv in counts)
            {
                bool consumed = inventory.TryConsumeIngredient(kv.Key, kv.Value);
                Contract.Requires(consumed, "stock was checked so consumption must succeed");
            }

            // A generated id encodes every number of its spec, so an already-registered entry is the same dish.
            if (!catalog.TryGetFood(resolution.Food.Id, out _))
            {
                catalog.RegisterFood(resolution.Food);
            }

            inventory.AddFood(resolution.Food.Id, resolution.Quantity);
            return resolution;
        }

        /// <summary>Maps accumulated effect points to an effect level.</summary>
        /// <remarks>Pre: points &gt;= 0. Post: 0 for 0 points; 1 for 1–3, 2 for 4–6, 3 for ≥7. Side effects: none.</remarks>
        public static int LevelForPoints(int points)
        {
            Contract.Requires(points >= 0, "points must be >= 0");
            if (points == 0) return 0;
            if (points <= 3) return 1;
            return points <= 6 ? 2 : 3;
        }

        private static PotResolution Refused(CookRefusalReason reason) =>
            new PotResolution(reason, PotDishKind.Dubious, null, null, CookingQuality.Normal, 0);

        private static int MasterworkBonus(CookingQuality quality) => quality == CookingQuality.Masterwork ? 1 : 0;

        private static RecipeSpec FindSignature(CookingMethod method, Dictionary<string, int> counts, int total, FoodCatalog catalog)
        {
            RecipeSpec best = null;
            int bestSpecificity = -1;
            foreach (RecipeSpec recipe in catalog.Recipes)
            {
                if (recipe.Method != method || !MatchesExactly(recipe, counts, total, catalog))
                {
                    continue;
                }

                int specificity = 0;
                for (int i = 0; i < recipe.Requirements.Count; i++)
                {
                    if (!recipe.Requirements[i].IsCategoryMatch) specificity += recipe.Requirements[i].Quantity;
                }

                // Dictionary enumeration order is not a contract; the ordinal id tie-break keeps every client on one recipe.
                if (specificity > bestSpecificity || (specificity == bestSpecificity && string.CompareOrdinal(recipe.Id, best.Id) < 0))
                {
                    best = recipe;
                    bestSpecificity = specificity;
                }
            }

            return best;
        }

        private static bool MatchesExactly(RecipeSpec recipe, Dictionary<string, int> counts, int total, FoodCatalog catalog)
        {
            int required = 0;
            for (int i = 0; i < recipe.Requirements.Count; i++) required += recipe.Requirements[i].Quantity;
            if (required != total) return false;

            var remaining = new Dictionary<string, int>(counts);
            for (int i = 0; i < recipe.Requirements.Count; i++)
            {
                RecipeIngredientRequirement req = recipe.Requirements[i];
                if (req.IsCategoryMatch) continue;
                if (!remaining.TryGetValue(req.IngredientId, out int held) || held < req.Quantity) return false;
                remaining[req.IngredientId] = held - req.Quantity;
            }

            // Each ingredient has exactly one category, so filling category slots greedily cannot block another slot.
            for (int i = 0; i < recipe.Requirements.Count; i++)
            {
                RecipeIngredientRequirement req = recipe.Requirements[i];
                if (!req.IsCategoryMatch) continue;
                int needed = req.Quantity;
                foreach (string id in new List<string>(remaining.Keys))
                {
                    if (needed == 0) break;
                    if (!catalog.TryGetIngredient(id, out IngredientSpec spec) || spec.Category != req.Category) continue;
                    int take = Math.Min(needed, remaining[id]);
                    remaining[id] -= take;
                    needed -= take;
                }

                if (needed > 0) return false;
            }

            return true;
        }

        private static bool MeetsMethodConditions(CookingMethod method, IngredientSpec[] items)
        {
            IngredientTags all = IngredientTags.None;
            int mains = 0;
            int critters = 0;
            bool rawMeat = false;
            bool fishSauce = false;
            bool onlyBrewable = true;
            bool onlyGrindable = true;
            for (int i = 0; i < items.Length; i++)
            {
                IngredientTags tags = items[i].Traits.Tags;
                all |= tags;
                if (items[i].Traits.IsMain) mains++;
                if (items[i].Traits.Role == IngredientRole.Critter) critters++;
                rawMeat |= (tags & IngredientTags.Raw) != 0 && (tags & MeatTags) != 0;
                fishSauce |= (tags & IngredientTags.Fish) != 0 && (tags & IngredientTags.Fermented) != 0;
                onlyBrewable &= (tags & ChillBrewTags) != 0 && (tags & MeatTags) == 0;
                onlyGrindable &= (tags & GrindTags) != 0;
            }

            // REQ: cuisine.md#4 — a pot of seasoning alone is always a failure.
            if (mains == 0 && critters == 0) return false;

            switch (method)
            {
                case CookingMethod.Grill:
                case CookingMethod.Steam:
                    return mains > 0;
                case CookingMethod.Stew:
                    return mains > 0 && (items.Length >= 3 || (all & IngredientTags.Dairy) != 0 || fishSauce);
                case CookingMethod.StirFry:
                    return mains > 0 && (all & IngredientTags.Oil) != 0;
                case CookingMethod.DeepFry:
                    return mains > 0 && (all & IngredientTags.Oil) != 0 && (all & IngredientTags.Flour) != 0;
                case CookingMethod.ColdMix:
                    return mains > 0 && !rawMeat;
                case CookingMethod.ChillBrew:
                    return onlyBrewable;
                case CookingMethod.Grind:
                    return onlyGrindable;
                default:
                    return false;
            }
        }

        private static FoodItemSpec BuildFreeDish(CookingMethod method, IngredientSpec[] items)
        {
            int healSum = 0;
            int durationSum = BaseDurationMs;
            for (int i = 0; i < items.Length; i++)
            {
                healSum += items[i].Traits.CookHealPercent;
                if (items[i].Traits.Role != IngredientRole.Base) durationSum += items[i].Traits.DurationAddMs;
            }

            int heal = Math.Min(100, healSum * HealMultiplierPercent(method) / 100);
            bool cleanse = method == CookingMethod.ColdMix;

            FoodEffectFamily family = FoodEffectFamily.None;
            Element familyElement = Element.Metal;
            Element effectSourceElement = items[0].Element;
            Element mainElement = items[0].Element;
            bool mixed = false;
            int points = 0;
            bool foundMain = false;
            for (int i = 0; i < items.Length; i++)
            {
                CuisineTraits t = items[i].Traits;
                if (!foundMain && t.IsMain)
                {
                    foundMain = true;
                    mainElement = items[i].Element;
                }

                if (t.EffectFamily == FoodEffectFamily.None) continue;
                // REQ: cuisine.md#效果家族與等級 — revival only survives grinding or stewing; elsewhere it is simply lost, not a conflict.
                if (t.EffectFamily == FoodEffectFamily.Revive && method != CookingMethod.Grind && method != CookingMethod.Stew) continue;

                Element keyElement = t.IsElementalEffect ? t.EffectElement : Element.Metal;
                if (family == FoodEffectFamily.None)
                {
                    family = t.EffectFamily;
                    familyElement = keyElement;
                    effectSourceElement = items[i].Element;
                }
                else if (family != t.EffectFamily || familyElement != keyElement)
                {
                    mixed = true;
                }

                points += t.EffectPoints;
            }

            if (mixed)
            {
                family = FoodEffectFamily.None;
                points = 0;
            }

            if (family != FoodEffectFamily.None && (method == CookingMethod.StirFry || method == CookingMethod.DeepFry))
            {
                points += 1;
            }

            // REQ: cuisine.md#4 品質 — the dish element is the effect ingredient's, else the first main ingredient's.
            Element dishElement = family != FoodEffectFamily.None ? effectSourceElement : mainElement;
            int level = family == FoodEffectFamily.None ? 0 : LevelForPoints(points);
            int duration = 0;
            if (family != FoodEffectFamily.None && family != FoodEffectFamily.Revive)
            {
                long scaled = (long)durationSum * (method == CookingMethod.Stew ? 150 : 100) / 100;
                duration = scaled > EffectBook.MaxDurationMs ? EffectBook.MaxDurationMs : (int)scaled;
            }

            FoodEffectSpec effect = BuildEffect(family, familyElement, level, heal, duration, cleanse);
            DishTheme theme = ThemeFor(items);
            var descriptor = new FreeDishDescriptor(method, theme, family, familyElement, level);
            string id = FreeDishId(method, theme, family, familyElement, level, heal, duration, cleanse);
            return new FoodItemSpec(id, method, FoodTarget.PlayerAndParty, dishElement, Math.Max(1, level), effect, descriptor);
        }

        private static int HealMultiplierPercent(CookingMethod method)
        {
            switch (method)
            {
                case CookingMethod.DeepFry: return 110;
                case CookingMethod.Steam: return 130;
                case CookingMethod.ColdMix: return 90;
                case CookingMethod.ChillBrew:
                case CookingMethod.Grind: return 80;
                default: return 100;
            }
        }

        private static FoodEffectSpec BuildEffect(FoodEffectFamily family, Element element, int lv, int heal, int durationMs, bool cleanse)
        {
            int revive = 0, roll = 0, catchBonus = 0, dropBonus = 0;
            var effects = new List<EffectSpec>(2);
            switch (family)
            {
                case FoodEffectFamily.Mastery: effects.Add(new EffectSpec(EffectType.MasteryMod, false, element, 5 * lv, 0, 1)); break;
                case FoodEffectFamily.Resist: effects.Add(new EffectSpec(EffectType.ResistMod, false, element, 10 * lv, 0, 1)); break;
                case FoodEffectFamily.Swift: effects.Add(new EffectSpec(EffectType.MoveSpeedModPercent, false, Element.Metal, 5 * lv, 0, 1)); break;
                case FoodEffectFamily.Nimble:
                    roll = -5 * lv;
                    effects.Add(new EffectSpec(EffectType.EvasionModPercent, false, Element.Metal, 3 * lv, 0, 1));
                    break;
                case FoodEffectFamily.Focus: effects.Add(new EffectSpec(EffectType.CdModPercent, true, Element.Metal, -4 * lv, 0, 1)); break;
                case FoodEffectFamily.Keen:
                    effects.Add(new EffectSpec(EffectType.AccuracyMod, false, Element.Metal, 3 * lv, 0, 1));
                    effects.Add(new EffectSpec(EffectType.CritRateMod, false, Element.Metal, 3 * lv, 0, 1));
                    break;
                case FoodEffectFamily.Fierce: effects.Add(new EffectSpec(EffectType.CritDamageMod, false, Element.Metal, 10 * lv, 0, 1)); break;
                case FoodEffectFamily.Guard:
                    effects.Add(new EffectSpec(EffectType.StaggerCapacityModPercent, false, Element.Metal, 10 * lv, 0, 1));
                    effects.Add(new EffectSpec(EffectType.BlockReductionMod, false, Element.Metal, 5 * lv, 0, 1));
                    break;
                case FoodEffectFamily.Bounty: dropBonus = 5 * lv; break;
                case FoodEffectFamily.Tame: catchBonus = 3 * lv; break;
                case FoodEffectFamily.Revive: revive = 20 * lv; break;
            }

            return new FoodEffectSpec(heal, heal, revive, cleanse, roll, catchBonus, dropBonus, durationMs, effects);
        }

        private static DishTheme ThemeFor(IngredientSpec[] items)
        {
            bool any = false;
            DishTheme found = DishTheme.Tonic;
            for (int i = 0; i < items.Length; i++)
            {
                if (!items[i].Traits.IsMain || !TryTheme(items[i].Traits.Tags, out DishTheme theme)) continue;
                if (!any)
                {
                    found = theme;
                    any = true;
                }
                else if (found != theme)
                {
                    return DishTheme.Mixed;
                }
            }

            return found;
        }

        private static bool TryTheme(IngredientTags tags, out DishTheme theme)
        {
            if ((tags & IngredientTags.RedMeat) != 0) theme = DishTheme.Meat;
            else if ((tags & IngredientTags.Poultry) != 0) theme = DishTheme.Fowl;
            else if ((tags & IngredientTags.Fish) != 0) theme = DishTheme.Fish;
            else if ((tags & IngredientTags.Shellfish) != 0) theme = DishTheme.Seafood;
            else if ((tags & IngredientTags.Mushroom) != 0) theme = DishTheme.Mushroom;
            else if ((tags & IngredientTags.Egg) != 0) theme = DishTheme.Egg;
            else if ((tags & IngredientTags.Vegetable) != 0) theme = DishTheme.Vegetable;
            else if ((tags & IngredientTags.Fruit) != 0) theme = DishTheme.Fruit;
            else if ((tags & (IngredientTags.Grain | IngredientTags.Flour)) != 0) theme = DishTheme.Grain;
            else if ((tags & IngredientTags.Dairy) != 0) theme = DishTheme.Dairy;
            else if ((tags & IngredientTags.Nut) != 0) theme = DishTheme.Nut;
            else if ((tags & IngredientTags.Herb) != 0) theme = DishTheme.Herb;
            else
            {
                theme = DishTheme.Tonic;
                return false;
            }

            return true;
        }

        private static string FreeDishId(CookingMethod method, DishTheme theme, FoodEffectFamily family, Element element, int level,
            int heal, int durationMs, bool cleanse)
        {
            var sb = new StringBuilder("food_free_");
            sb.Append(Snake(method.ToString())).Append('_').Append(Snake(theme.ToString()));
            if (family != FoodEffectFamily.None)
            {
                sb.Append('_').Append(Snake(family.ToString()));
                if (family == FoodEffectFamily.Mastery || family == FoodEffectFamily.Resist) sb.Append('_').Append(Snake(element.ToString()));
                sb.Append("_lv").Append(level);
            }

            sb.Append("_h").Append(heal).Append("_d").Append(durationMs);
            if (cleanse) sb.Append("_c");
            return sb.ToString();
        }

        private static string Snake(string pascal)
        {
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                char ch = pascal[i];
                if (char.IsUpper(ch))
                {
                    if (i > 0) sb.Append('_');
                    sb.Append(char.ToLowerInvariant(ch));
                }
                else
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }
    }
}
