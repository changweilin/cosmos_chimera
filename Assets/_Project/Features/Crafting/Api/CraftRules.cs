using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;

namespace Cosmimera.Crafting
{
    /// <summary>Expected refusal reasons for gathering and crafting.</summary>
    /// <remarks>Pre: none. Post: stable values. Side effects: none.</remarks>
    public enum CraftResult
    {
        Success = 0, MissingSkill = 1, MissingMaterials = 2, CapacityExceeded = 3,
        Depleted = 4, NoResource = 5,
        MissingTool = 6,
    }

    /// <summary>Atomic crafting rules independent of Unity presentation and equipment statistics.</summary>
    /// <remarks>Pre: single-threaded inventory access. Post: refused actions never consume materials. Side effects: inventory changes on success.</remarks>
    public static class CraftRules
    {
        /// <summary>Previews a recipe against current team skills and inventory.</summary>
        /// <remarks>Pre: recipe, utilities and inventory non-null. Post: success iff the whole recipe can complete. Side effects: allocates scratch state.</remarks>
        public static CraftResult CanCraft(CraftRecipe recipe, IReadOnlyCollection<FieldUtility> utilities, CraftInventory inventory)
        {
            Contract.Requires(recipe != null && utilities != null && inventory != null, "recipe, utilities and inventory required");
            if (!HasUtility(utilities, recipe.Utility)) return CraftResult.MissingSkill;
            foreach (string tool in recipe.Tools) if (inventory.Count(tool) == 0) return CraftResult.MissingTool;
            if (!inventory.CanExchange(recipe.Costs, Array.Empty<CraftStack>())) return CraftResult.MissingMaterials;
            return inventory.CanExchange(recipe.Costs, recipe.Gains) ? CraftResult.Success : CraftResult.CapacityExceeded;
        }

        /// <summary>Commits a successful recipe once; failure preserves every stack.</summary>
        /// <remarks>Pre: recipe, utilities and inventory non-null. Post: result agrees with CanCraft; success exchanges costs for gains. Side effects: mutates inventory only on success.</remarks>
        public static CraftResult TryCraft(CraftRecipe recipe, IReadOnlyCollection<FieldUtility> utilities, CraftInventory inventory)
        {
            CraftResult result = CanCraft(recipe, utilities, inventory);
            if (result != CraftResult.Success) return result;
            return inventory.TryExchange(recipe.Costs, recipe.Gains) ? CraftResult.Success : CraftResult.CapacityExceeded;
        }

        /// <summary>Commits a recipe once and tags every crafted equipment copy as made on <paramref name="origin"/>.</summary>
        /// <remarks>
        /// REQ: docs/design/gear-looks.md#外觀與數值.
        /// Pre: recipe, utilities, inventory and catalog non-null; origin constructed.
        /// Post: as TryCraft; on success each gained copy whose catalog item has Equipment carries an ItemLook from origin.
        /// Side effects: mutates inventory only on success.
        /// </remarks>
        public static CraftResult TryCraft(CraftRecipe recipe, IReadOnlyCollection<FieldUtility> utilities, CraftInventory inventory,
            CraftCatalog catalog, GearOrigin origin)
        {
            Contract.Requires(catalog != null, "catalog required");
            CraftResult result = CanCraft(recipe, utilities, inventory);
            if (result != CraftResult.Success) return result;
            var lookIds = new List<string>();
            foreach (CraftStack gain in recipe.Gains)
                if (catalog.TryGetItem(gain.Id, out CraftItem item) && item.Equipment != null) lookIds.Add(gain.Id);
            return inventory.TryExchangeCrafted(recipe.Costs, recipe.Gains, lookIds, origin)
                ? CraftResult.Success : CraftResult.CapacityExceeded;
        }

        /// <summary>Consumes one unit of a consumable capture vessel from <paramref name="inventory"/>.</summary>
        /// <remarks>
        /// Pre: <paramref name="item"/> and <paramref name="inventory"/> non-null; <paramref name="item"/>.Category == "capture".
        /// Post: returns Success and decrements stock by 1 iff stock &gt;= 1; otherwise returns MissingMaterials and leaves stock unchanged.
        /// Side effects: mutates <paramref name="inventory"/> on Success.
        /// </remarks>
        public static CraftResult TryConsumeCaptureVessel(CraftItem item, CraftInventory inventory)
        {
            Contract.Requires(item != null && inventory != null, "item and inventory required");
            Contract.Requires(item.Category == "capture", "item must be a capture vessel");
            if (inventory.Count(item.Id) == 0) return CraftResult.MissingMaterials;
            return inventory.TryExchange(new[] { new CraftStack(item.Id, 1) }, Array.Empty<CraftStack>())
                ? CraftResult.Success
                : CraftResult.MissingMaterials;
        }

        internal static bool HasUtility(IReadOnlyCollection<FieldUtility> utilities, FieldUtility required) =>
            CraftingExpansionRules.SatisfiesSkill(utilities, required);
    }

    /// <summary>Run-scoped depletion of gathering nodes without changing scatter generation or meshes.</summary>
    /// <remarks>Pre: host/main-thread use; callers verify object proximity and integrity. Post: one successful harvest per object per run. Side effects: remembers harvested ids.</remarks>
    public sealed class CraftGathering
    {
        private readonly HashSet<long> _harvested = new HashSet<long>();

        /// <summary>Previews all eligible resources on an intact nearby object's existing scatter kind.</summary>
        /// <remarks>Pre: nodeId nonnegative; source nonempty; season spring/summer/autumn/winter; other arguments non-null. Post: yields contains eligible resources, empty on refusal. Side effects: replaces yields.</remarks>
        public CraftResult Preview(long nodeId, string source, string season, CraftCatalog catalog,
            IReadOnlyCollection<FieldUtility> utilities, CraftInventory inventory, List<CraftStack> yields)
        {
            Contract.Requires(nodeId >= 0 && !string.IsNullOrEmpty(source), "valid node and source required");
            Contract.Requires(season == "spring" || season == "summer" || season == "autumn" || season == "winter", "invalid season");
            Contract.Requires(catalog != null && utilities != null && inventory != null && yields != null, "all arguments required");
            yields.Clear();
            if (_harvested.Contains(nodeId)) return CraftResult.Depleted;
            bool resource = CollectYields(source, season, catalog, utilities, inventory, yields);
            if (yields.Count == 0) return resource ? CraftResult.MissingSkill : CraftResult.NoResource;
            if (inventory.CanExchange(Array.Empty<CraftStack>(), yields)) return CraftResult.Success;
            yields.Clear();
            return CraftResult.CapacityExceeded;
        }

        /// <summary>Harvest of an object a creature's life-skill hit just knocked down (destructibles.md#傷害 擊倒＝收成).</summary>
        /// <remarks>
        /// REQ: destructibles.md#傷害 (Q-C77) — only the caster's life skills count (tools never do); a full bag still
        /// spends the harvest, and the caller drops the yields on the ground.
        /// Pre: as Preview; utilities are the caster's harvest utilities.
        /// Post: Depleted (node already harvested) or NoResource/MissingSkill leave yields empty and depletion unchanged;
        ///       otherwise the node is depleted and yields holds the harvest: Success means it is already in inventory,
        ///       CapacityExceeded means inventory is unchanged and the caller must place yields on the ground.
        /// Side effects: replaces yields; mutates depletion and, on Success, inventory.
        /// </remarks>
        public CraftResult HarvestOnKnockdown(long nodeId, string source, string season, CraftCatalog catalog,
            IReadOnlyCollection<FieldUtility> utilities, CraftInventory inventory, List<CraftStack> yields)
        {
            Contract.Requires(nodeId >= 0 && !string.IsNullOrEmpty(source), "valid node and source required");
            Contract.Requires(season == "spring" || season == "summer" || season == "autumn" || season == "winter", "invalid season");
            Contract.Requires(catalog != null && utilities != null && inventory != null && yields != null, "all arguments required");
            yields.Clear();
            if (_harvested.Contains(nodeId)) return CraftResult.Depleted;
            bool resource = CollectYields(source, season, catalog, utilities, null, yields);
            if (yields.Count == 0) return resource ? CraftResult.MissingSkill : CraftResult.NoResource;
            _harvested.Add(nodeId);
            return inventory.TryExchange(Array.Empty<CraftStack>(), yields) ? CraftResult.Success : CraftResult.CapacityExceeded;
        }

        // toolStock null: tools do not count (knockdown harvest). Returns whether the source has any resource this season.
        private static bool CollectYields(string source, string season, CraftCatalog catalog,
            IReadOnlyCollection<FieldUtility> utilities, CraftInventory toolStock, List<CraftStack> yields)
        {
            bool resource = false;
            foreach (CraftItem item in catalog.Items)
            {
                if (item.Season != "all" && item.Season != season) continue;
                bool matches = false;
                foreach (string kind in item.Sources) if (source == kind) { matches = true; break; }
                if (!matches) continue;
                resource = true;
                if (CraftRules.HasUtility(utilities, item.Utility) ||
                    (toolStock != null && item.GatherTool != null && toolStock.Count(item.GatherTool) > 0))
                    yields.Add(new CraftStack(item.Id, item.Quantity));
            }
            return resource;
        }

        /// <summary>Grants a node's previewed resources and depletes it, or leaves stock and depletion unchanged.</summary>
        /// <remarks>Pre: same as Preview. Post: success grants all yields once; refusal does not deplete the node. Side effects: replaces yields, mutates inventory and depletion on success.</remarks>
        public CraftResult TryGather(long nodeId, string source, string season, CraftCatalog catalog,
            IReadOnlyCollection<FieldUtility> utilities, CraftInventory inventory, List<CraftStack> yields)
        {
            CraftResult result = Preview(nodeId, source, season, catalog, utilities, inventory, yields);
            if (result != CraftResult.Success) return result;
            if (!inventory.TryExchange(Array.Empty<CraftStack>(), yields)) return CraftResult.CapacityExceeded;
            _harvested.Add(nodeId);
            return CraftResult.Success;
        }
    }
}
