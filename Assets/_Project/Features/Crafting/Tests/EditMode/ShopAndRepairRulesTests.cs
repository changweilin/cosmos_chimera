using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Crafting;
using NUnit.Framework;

namespace Cosmimera.Crafting.Tests.EditMode
{
    public sealed class ShopAndRepairRulesTests
    {
        [Test]
        public void Tier1_shop_access_requires_dwarf_planet_unlock()
        {
            Assert.That(ShopAndRepairRules.CanAccessTier1EquipmentShop(false), Is.False);
            Assert.That(ShopAndRepairRules.CanAccessTier1EquipmentShop(true), Is.True);
        }

        [Test]
        public void Repair_costs_scale_by_fifty_percent_of_damage_ratio_for_all_tiers()
        {
            var t1Inputs = new[] { new CraftStack("bronze_ingot", 4) };
            var t3Inputs = new[]
            {
                new CraftStack("bronze_ingot", 64),
                new CraftStack("steel_ingot", 16),
                new CraftStack("mithril_ingot", 4),
            };

            // Full durability -> no cost
            Assert.That(ShopAndRepairRules.ComputeRepairCosts(t1Inputs, 100, 100), Is.Empty);

            // Half damaged (50% missing) -> 25% of crafting materials (4 * 0.25 = 1)
            List<CraftStack> halfT1 = ShopAndRepairRules.ComputeRepairCosts(t1Inputs, 50, 100);
            Assert.That(halfT1.Count, Is.EqualTo(1));
            Assert.That(halfT1[0], Is.EqualTo(new CraftStack("bronze_ingot", 1)));

            // Fully broken (100% missing) -> 50% of crafting materials for Tier 3 (32, 8, 2)
            List<CraftStack> brokenT3 = ShopAndRepairRules.ComputeRepairCosts(t3Inputs, 0, 100);
            Assert.That(brokenT3, Is.EqualTo(new[]
            {
                new CraftStack("bronze_ingot", 32),
                new CraftStack("steel_ingot", 8),
                new CraftStack("mithril_ingot", 2),
            }));

            // Slight damage (99/100) still requires at least 1 of each input
            List<CraftStack> slightT1 = ShopAndRepairRules.ComputeRepairCosts(t1Inputs, 99, 100);
            Assert.That(slightT1[0].Quantity, Is.EqualTo(1));
        }

        [Test]
        public void Compute_repair_costs_validates_contracts()
        {
            Assert.Throws<ContractException>(() =>
                ShopAndRepairRules.ComputeRepairCosts(null, 50, 100));
            Assert.Throws<ContractException>(() =>
                ShopAndRepairRules.ComputeRepairCosts(Array.Empty<CraftStack>(), 0, 0));
            Assert.Throws<ContractException>(() =>
                ShopAndRepairRules.ComputeRepairCosts(Array.Empty<CraftStack>(), 120, 100));
        }

        [Test]
        public void Try_repair_equipment_consumes_materials_and_restores_durability()
        {
            var stock = new CraftInventory();
            stock.TryExchange(Array.Empty<CraftStack>(), new[] { new CraftStack("bronze_ingot", 2) });

            var gear = new EquipmentInstance("bronze_sword", 100);
            gear.Degrade(100);
            Assert.That(gear.IsBroken, Is.True);

            var inputs = new[] { new CraftStack("bronze_ingot", 4) }; // 100% damaged -> 50% = 2
            Assert.That(ShopAndRepairRules.TryRepairEquipment(stock, inputs, gear), Is.True);
            Assert.That(gear.CurrentDurability, Is.EqualTo(100));
            Assert.That(stock.Count("bronze_ingot"), Is.EqualTo(0));

            // Insufficient stock leaves damaged state unchanged
            gear.Degrade(100);
            Assert.That(ShopAndRepairRules.TryRepairEquipment(stock, inputs, gear), Is.False);
            Assert.That(gear.IsBroken, Is.True);
        }

        [Test]
        public void Buy_tier1_equipment_and_sell_material_update_gold_and_stock()
        {
            var stock = new CraftInventory();
            stock.TryExchange(Array.Empty<CraftStack>(), new[] { new CraftStack("copper_ore", 10) });

            int gold = 50;
            int sellPrice = ShopAndRepairRules.MaterialSellPrice(CraftMaterialTier.Tier1, false);
            Assert.That(ShopAndRepairRules.TrySellMaterial(stock, "copper_ore", 10, sellPrice, gold, out gold), Is.True);
            Assert.That(stock.Count("copper_ore"), Is.EqualTo(0));
            Assert.That(gold, Is.EqualTo(50 + 10 * sellPrice));

            int buyPrice = ShopAndRepairRules.Tier1EquipmentGoldPrice(false);
            GearOrigin origin = new GearOrigin("lobby_planet", 123UL);

            // Not enough gold
            Assert.That(ShopAndRepairRules.TryBuyTier1Equipment(stock, "bronze_sword", origin, buyPrice, 10, out int remFail), Is.False);
            Assert.That(remFail, Is.EqualTo(10));
            Assert.That(stock.Count("bronze_sword"), Is.EqualTo(0));

            // Enough gold
            Assert.That(ShopAndRepairRules.TryBuyTier1Equipment(stock, "bronze_sword", origin, buyPrice, 200, out int remOk), Is.True);
            Assert.That(remOk, Is.EqualTo(200 - buyPrice));
            Assert.That(stock.Count("bronze_sword"), Is.EqualTo(1));
        }
    }
}
