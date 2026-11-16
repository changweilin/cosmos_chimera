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

        internal static bool HasUtility(IReadOnlyCollection<FieldUtility> utilities, FieldUtility required)
        {
            foreach (FieldUtility utility in utilities) if (utility == required) return true;
            return false;
        }
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
            bool resource = false;
            foreach (CraftItem item in catalog.Items)
            {
                if (item.Season != "all" && item.Season != season) continue;
                bool matches = false;
                foreach (string kind in item.Sources) if (source == kind) { matches = true; break; }
                if (!matches) continue;
                resource = true;
                if (CraftRules.HasUtility(utilities, item.Utility) ||
                    (item.GatherTool != null && inventory.Count(item.GatherTool) > 0))
                    yields.Add(new CraftStack(item.Id, item.Quantity));
            }
            if (yields.Count == 0) return resource ? CraftResult.MissingSkill : CraftResult.NoResource;
            if (inventory.CanExchange(Array.Empty<CraftStack>(), yields)) return CraftResult.Success;
            yields.Clear();
            return CraftResult.CapacityExceeded;
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
