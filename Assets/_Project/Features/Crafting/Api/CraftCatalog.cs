using System;
using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cosmimera.Crafting
{
    /// <summary>Data-defined crafting item and its eligible gathering sources.</summary>
    /// <remarks>Pre: loaded through CraftCatalog. Post: immutable. Side effects: none.</remarks>
    public sealed class CraftItem
    {
        internal CraftItem(string id, string category, string[] sources, FieldUtility utility, int quantity, string season,
            string gatherTool, int captureBonusPercent, EquipmentSpec equipment)
        { Id = id; Category = category; Sources = Array.AsReadOnly(sources); Utility = utility; Quantity = quantity;
            Season = season; GatherTool = gatherTool; CaptureBonusPercent = captureBonusPercent; Equipment = equipment; }

        /// <summary>Stable item id.</summary>
        /// <remarks>Pre: none. Post: valid snake_case id. Side effects: none.</remarks>
        public string Id { get; }
        /// <summary>Inventory category, independent of rendering.</summary>
        /// <remarks>Pre: none. Post: schema-defined category. Side effects: none.</remarks>
        public string Category { get; }
        /// <summary>Existing scatter-kind ids that can yield this item.</summary>
        /// <remarks>Pre: none. Post: immutable list; empty for crafted-only items. Side effects: none.</remarks>
        public IReadOnlyList<string> Sources { get; }
        /// <summary>Utility needed to gather the item.</summary>
        /// <remarks>Pre: none. Post: defined utility. Side effects: none.</remarks>
        public FieldUtility Utility { get; }
        /// <summary>Units yielded per gathering node.</summary>
        /// <remarks>Pre: none. Post: positive. Side effects: none.</remarks>
        public int Quantity { get; }
        /// <summary>Required season or all.</summary>
        /// <remarks>Pre: none. Post: lowercase season id or all. Side effects: none.</remarks>
        public string Season { get; }
        /// <summary>Optional reusable tool that can supply the gathering qualification.</summary>
        /// <remarks>Pre: none. Post: null or known tool id. Side effects: none.</remarks>
        public string GatherTool { get; }
        /// <summary>Additional capture chance in percentage points when this crafted vessel is consumed.</summary>
        /// <remarks>Pre: none. Post: 0..100. Side effects: none.</remarks>
        public int CaptureBonusPercent { get; }
        /// <summary>Wearable bonuses, or null for other categories.</summary>
        /// <remarks>Pre: none. Post: non-null iff equipment category. Side effects: none.</remarks>
        public EquipmentSpec Equipment { get; }
    }

    /// <summary>One atomic crafting recipe.</summary>
    /// <remarks>Pre: loaded through CraftCatalog. Post: immutable lists and identifiers. Side effects: none.</remarks>
    public sealed class CraftRecipe
    {
        internal CraftRecipe(string id, FieldUtility utility, CraftStack[] costs, CraftStack[] gains, string[] tools)
        { Id = id; Utility = utility; Costs = Array.AsReadOnly(costs); Gains = Array.AsReadOnly(gains); Tools = Array.AsReadOnly(tools); }

        /// <summary>Stable recipe id.</summary>
        /// <remarks>Pre: none. Post: valid snake_case id. Side effects: none.</remarks>
        public string Id { get; }
        /// <summary>Required active utility.</summary>
        /// <remarks>Pre: none. Post: defined utility. Side effects: none.</remarks>
        public FieldUtility Utility { get; }
        /// <summary>Materials consumed on success.</summary>
        /// <remarks>Pre: none. Post: nonempty immutable list. Side effects: none.</remarks>
        public IReadOnlyList<CraftStack> Costs { get; }
        /// <summary>Outputs granted on success.</summary>
        /// <remarks>Pre: none. Post: nonempty immutable list. Side effects: none.</remarks>
        public IReadOnlyList<CraftStack> Gains { get; }
        /// <summary>Reusable tools required in stock, preserved by the recipe.</summary>
        /// <remarks>Pre: none. Post: immutable known tool ids. Side effects: none.</remarks>
        public IReadOnlyList<string> Tools { get; }
    }

    /// <summary>Strict runtime reader for the schema-validated crafting TextAssets staged from Data.</summary>
    /// <remarks>Pre: source JSON follows craft-item and craft-recipe schemas. Post: immutable catalog; malformed input throws InvalidDataException. Side effects: allocates.</remarks>
    public sealed class CraftCatalog
    {
        private readonly Dictionary<string, CraftItem> _items = new Dictionary<string, CraftItem>(StringComparer.Ordinal);
        private readonly List<CraftItem> _orderedItems = new List<CraftItem>();
        private readonly List<CraftRecipe> _recipes = new List<CraftRecipe>();

        /// <summary>Parses item and recipe arrays, rejecting duplicate ids and missing item references.</summary>
        /// <remarks>Pre: both JSON strings non-null. Post: fully validated catalog or exception, no partial publication. Side effects: allocates.</remarks>
        public CraftCatalog(string itemsJson, string recipesJson)
        {
            Contract.Requires(itemsJson != null && recipesJson != null, "both JSON sources required");
            foreach (JToken item in Read(itemsJson))
            {
                Check(item is JObject, "item must be an object");
                string id = Id(item["id"]);
                Check(!_items.ContainsKey(id), "duplicate item id: " + id);
                string category = Text(item["category"]);
                Check(Array.IndexOf(new[] { "raw", "processed", "capture", "trap", "tool", "equipment" }, category) >= 0, "invalid category");
                var sources = new List<string>();
                Check(item["sources"] is JArray, "sources must be an array");
                foreach (JToken source in (JArray)item["sources"]) sources.Add(Id(source));
                FieldUtility utility = ParseUtility(item["utility"]);
                Check(sources.Count == 0 || utility != FieldUtility.None, "gathering needs a utility");
                string season = Text(item["season"]);
                Check(Array.IndexOf(new[] { "all", "spring", "summer", "autumn", "winter" }, season) >= 0, "invalid season");
                string tool = item["gather_tool"] == null ? null : Id(item["gather_tool"]);
                int bonus = item["capture_bonus_percent"] == null ? 0 : Positive(item["capture_bonus_percent"]);
                Check(bonus <= 100 && (bonus == 0 || category == "capture"), "invalid capture bonus");
                EquipmentSpec equipment = item["equipment"] == null ? null : new EquipmentSpec(item["equipment"]);
                Check((category == "equipment") == (equipment != null), "equipment data must match category");
                var spec = new CraftItem(id, category, sources.ToArray(), utility, Positive(item["quantity"]), season, tool, bonus, equipment);
                _items.Add(id, spec);
                _orderedItems.Add(spec);
            }
            foreach (CraftItem item in _orderedItems)
                if (item.GatherTool != null) Check(_items.TryGetValue(item.GatherTool, out CraftItem tool) && tool.Category == "tool", "unknown gathering tool");
            var ids = new HashSet<string>(_items.Keys, StringComparer.Ordinal);
            foreach (JToken recipe in Read(recipesJson))
            {
                Check(recipe is JObject, "recipe must be an object");
                string id = Id(recipe["id"]);
                Check(ids.Add(id), "duplicate recipe id: " + id);
                FieldUtility utility = ParseUtility(recipe["utility"]);
                Check(utility != FieldUtility.None, "recipe needs a utility");
                var tools = new List<string>();
                if (recipe["tools"] != null)
                {
                    Check(recipe["tools"] is JArray, "tools must be an array");
                    foreach (JToken token in (JArray)recipe["tools"])
                    {
                        string tool = Id(token);
                        Check(_items.TryGetValue(tool, out CraftItem spec) && spec.Category == "tool", "unknown recipe tool");
                        tools.Add(tool);
                    }
                }
                CraftStack[] costs = Stacks(recipe["costs"]);
                foreach (CraftStack cost in costs) Check(!tools.Contains(cost.Id), "reusable tool cannot also be consumed");
                _recipes.Add(new CraftRecipe(id, utility, costs, Stacks(recipe["gains"]), tools.ToArray()));
            }
        }

        /// <summary>Items in stable source order.</summary>
        /// <remarks>Pre: none. Post: immutable view. Side effects: allocates view.</remarks>
        public IReadOnlyList<CraftItem> Items => _orderedItems.AsReadOnly();
        /// <summary>Recipes in stable source order.</summary>
        /// <remarks>Pre: none. Post: immutable view. Side effects: allocates view.</remarks>
        public IReadOnlyList<CraftRecipe> Recipes => _recipes.AsReadOnly();

        /// <summary>Finds an item by stable id.</summary>
        /// <remarks>Pre: id non-null. Post: true iff defined; unknown retired ids remain valid inventory entries. Side effects: none.</remarks>
        public bool TryGetItem(string id, out CraftItem item)
        {
            Contract.Requires(id != null, "id required");
            return _items.TryGetValue(id, out item);
        }

        private CraftStack[] Stacks(JToken value)
        {
            Check(value is JArray array && array.Count > 0, "costs and gains must be nonempty arrays");
            var result = new List<CraftStack>();
            foreach (JToken stack in (JArray)value)
            {
                Check(stack is JObject, "stack must be an object");
                string id = Id(stack["item"]);
                Check(_items.ContainsKey(id), "unknown crafting item: " + id);
                result.Add(new CraftStack(id, Positive(stack["quantity"])));
            }
            return result.ToArray();
        }

        private static JArray Read(string json)
        {
            try { return JArray.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); }
            catch (JsonException e) { throw new InvalidDataException("Invalid crafting JSON", e); }
        }

        private static string Text(JToken value)
        { Check(value?.Type == JTokenType.String, "expected string"); return (string)value; }
        private static string Id(JToken value)
        { string id = Text(value); Check(CraftInventory.ValidId(id), "invalid id: " + id); return id; }
        private static int Positive(JToken value)
        {
            Check(value?.Type == JTokenType.Integer && (long)value >= 1 && (long)value <= int.MaxValue, "quantity out of range");
            return (int)value;
        }
        private static FieldUtility ParseUtility(JToken value)
        {
            string id = Text(value);
            foreach (FieldUtility utility in Enum.GetValues(typeof(FieldUtility)))
                if (UtilityId(utility) == id) return utility;
            throw new InvalidDataException("Unknown field utility: " + id);
        }

        /// <summary>Returns the stable snake_case data id for a defined utility.</summary>
        /// <remarks>Pre: utility defined. Post: lowercase snake_case. Side effects: allocates.</remarks>
        public static string UtilityId(FieldUtility utility)
        {
            Contract.Requires(Enum.IsDefined(typeof(FieldUtility), utility), "undefined utility");
            return System.Text.RegularExpressions.Regex.Replace(utility.ToString(), "([a-z])([A-Z])", "$1_$2").ToLowerInvariant();
        }

        private static void Check(bool valid, string message)
        { if (!valid) throw new InvalidDataException(message); }
    }
}
