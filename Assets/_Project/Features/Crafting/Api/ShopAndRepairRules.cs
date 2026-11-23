using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;

namespace Cosmimera.Crafting
{
    /// <summary>
    /// Pure rules for gold currency, 1-star equipment shop access (unlocked after dwarf planet),
    /// material selling, and durability repair across all tiers (50% of crafting materials scaled by damage ratio).
    /// </summary>
    /// <remarks>REQ: docs/design/crafting.md#製造擴充, docs/design/lobby-meta.md#進度保存.</remarks>
    public static class ShopAndRepairRules
    {
        /// <summary>Base gold price for a 1-star one-handed weapon or armor piece.</summary>
        /// <remarks>Pre: none. Post: positive constant. Side effects: none.</remarks>
        public const int Tier1BasePriceGold = 120;

        /// <summary>Gold reward per defeated trainer opponent creature.</summary>
        /// <remarks>Pre: none. Post: positive constant. Side effects: none.</remarks>
        public const int GoldPerTrainerOpponent = 25;

        /// <summary>Gold reward granted upon completing a main story or side quest milestone.</summary>
        /// <remarks>Pre: none. Post: positive constant. Side effects: none.</remarks>
        public const int QuestCompletionGold = 100;

        /// <summary>Checks whether the 1-star equipment shop is unlocked (requires dwarf planet unlock).</summary>
        /// <remarks>Pre: none. Post: returns dwarfPlanetUnlocked. Side effects: none.</remarks>
        public static bool CanAccessTier1EquipmentShop(bool dwarfPlanetUnlocked) => dwarfPlanetUnlocked;

        /// <summary>Computes gold purchase price for a 1-star weapon or armor piece.</summary>
        /// <remarks>Pre: none. Post: positive gold amount (2x for two-handed weapons). Side effects: none.</remarks>
        public static int Tier1EquipmentGoldPrice(bool isTwoHanded)
            => isTwoHanded ? Tier1BasePriceGold * 2 : Tier1BasePriceGold;

        /// <summary>Computes gold sell price per unit of a material or processed good.</summary>
        /// <remarks>Pre: tier is defined. Post: positive gold amount. Side effects: none.</remarks>
        public static int MaterialSellPrice(CraftMaterialTier tier, bool isProcessed)
        {
            Contract.Requires(Enum.IsDefined(typeof(CraftMaterialTier), tier), "invalid tier");
            int baseRaw = tier switch
            {
                CraftMaterialTier.Tier1 => 5,
                CraftMaterialTier.Tier2 => 20,
                CraftMaterialTier.Tier3 => 80,
                _ => 5,
            };
            return isProcessed ? baseRaw * 10 : baseRaw;
        }

        /// <summary>Computes gold earned from defeating a trainer battle with <paramref name="opponentCount"/> creatures.</summary>
        /// <remarks>Pre: opponentCount &gt;= 0. Post: result &gt;= 0. Side effects: none.</remarks>
        public static int TrainerVictoryGoldReward(int opponentCount)
        {
            Contract.Requires(opponentCount >= 0, "opponentCount must be non-negative");
            return opponentCount * GoldPerTrainerOpponent;
        }

        /// <summary>
        /// Computes material costs to fully repair equipment of any tier from its crafting inputs,
        /// scaled by 50% of the damage ratio: ceil(qty * 0.5 * (Max - Current) / Max), minimum 1 per input when damaged.
        /// </summary>
        /// <remarks>
        /// Pre: recipeInputs != null; maxDurability &gt; 0; 0 &lt;= currentDurability &lt;= maxDurability.
        /// Post: empty list when currentDurability == maxDurability; otherwise one positive CraftStack per unique input id.
        /// Side effects: allocates result list.
        /// </remarks>
        public static List<CraftStack> ComputeRepairCosts(IReadOnlyList<CraftStack> recipeInputs, int currentDurability, int maxDurability)
        {
            Contract.Requires(recipeInputs != null, "recipeInputs required");
            Contract.Requires(maxDurability > 0, "maxDurability must be positive");
            Contract.Requires(currentDurability >= 0 && currentDurability <= maxDurability, "invalid currentDurability");

            var result = new List<CraftStack>();
            int missing = maxDurability - currentDurability;
            if (missing <= 0 || recipeInputs.Count == 0)
            {
                return result;
            }

            var merged = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();
            for (int i = 0; i < recipeInputs.Count; i++)
            {
                CraftStack s = recipeInputs[i];
                if (s.Quantity <= 0 || !CraftInventory.ValidId(s.Id)) continue;
                if (!merged.TryGetValue(s.Id, out int existing))
                {
                    order.Add(s.Id);
                    merged[s.Id] = s.Quantity;
                }
                else
                {
                    merged[s.Id] = existing + s.Quantity;
                }
            }

            // Integer ceiling of (qty * missing) / (2 * max) avoids floating-point drift.
            long denom = 2L * maxDurability;
            for (int i = 0; i < order.Count; i++)
            {
                string id = order[i];
                long num = (long)merged[id] * missing;
                int required = (int)((num + denom - 1) / denom);
                if (required < 1) required = 1;
                result.Add(new CraftStack(id, required));
            }

            return result;
        }

        /// <summary>Repairs <paramref name="instance"/> by consuming 50% damage-scaled crafting materials from <paramref name="stock"/>.</summary>
        /// <remarks>
        /// Pre: stock != null; recipeInputs != null; instance != null.
        /// Post: returns true and restores instance.CurrentDurability to MaxDurability if stock has enough materials; false leaves both unchanged.
        /// Side effects: mutates stock and instance on success.
        /// </remarks>
        public static bool TryRepairEquipment(CraftInventory stock, IReadOnlyList<CraftStack> recipeInputs, EquipmentInstance instance)
        {
            Contract.Requires(stock != null && recipeInputs != null && instance != null, "stock, recipeInputs, and instance required");
            List<CraftStack> costs = ComputeRepairCosts(recipeInputs, instance.CurrentDurability, instance.MaxDurability);
            if (costs.Count == 0)
            {
                instance.Repair();
                return true;
            }

            if (!stock.TryExchange(costs, Array.Empty<CraftStack>()))
            {
                return false;
            }

            instance.Repair();
            return true;
        }

        /// <summary>Attempts to purchase a 1-star equipment item with gold.</summary>
        /// <remarks>
        /// Pre: stock != null; CraftInventory.ValidId(itemId); CraftInventory.ValidId(origin.PlanetTypeId); priceGold &gt; 0; currentGold &gt;= 0.
        /// Post: returns true, deducts priceGold into remainingGold, and adds 1 tagged copy to stock if affordable and bag has room;
        ///       false leaves stock unchanged and remainingGold == currentGold.
        /// Side effects: mutates stock on success.
        /// </remarks>
        public static bool TryBuyTier1Equipment(CraftInventory stock, string itemId, GearOrigin origin,
            int priceGold, int currentGold, out int remainingGold)
        {
            Contract.Requires(stock != null, "stock required");
            Contract.Requires(CraftInventory.ValidId(itemId), "invalid itemId");
            Contract.Requires(CraftInventory.ValidId(origin.PlanetTypeId), "invalid origin");
            Contract.Requires(priceGold > 0 && currentGold >= 0, "invalid gold amounts");

            remainingGold = currentGold;
            if (currentGold < priceGold)
            {
                return false;
            }

            var gains = new[] { new CraftStack(itemId, 1) };
            var lookIds = new[] { itemId };
            if (!stock.TryExchangeCrafted(Array.Empty<CraftStack>(), gains, lookIds, origin))
            {
                return false;
            }

            remainingGold = currentGold - priceGold;
            return true;
        }

        /// <summary>Attempts to sell <paramref name="quantity"/> units of a material or item for gold.</summary>
        /// <remarks>
        /// Pre: stock != null; CraftInventory.ValidId(itemId); quantity &gt; 0; unitPriceGold &gt; 0; currentGold &gt;= 0.
        /// Post: returns true, deducts quantity from stock, and sets newGold = currentGold + quantity * unitPriceGold;
        ///       false leaves stock unchanged and newGold == currentGold.
        /// Side effects: mutates stock on success.
        /// </remarks>
        public static bool TrySellMaterial(CraftInventory stock, string itemId, int quantity,
            int unitPriceGold, int currentGold, out int newGold)
        {
            Contract.Requires(stock != null, "stock required");
            Contract.Requires(CraftInventory.ValidId(itemId), "invalid itemId");
            Contract.Requires(quantity > 0 && unitPriceGold > 0 && currentGold >= 0, "invalid sell parameters");

            newGold = currentGold;
            var costs = new[] { new CraftStack(itemId, quantity) };
            if (!stock.TryExchange(costs, Array.Empty<CraftStack>()))
            {
                return false;
            }

            newGold = currentGold + quantity * unitPriceGold;
            return true;
        }
    }
}
