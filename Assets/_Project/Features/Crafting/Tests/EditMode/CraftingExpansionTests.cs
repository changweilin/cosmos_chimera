using System.Collections.Generic;
using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.Creatures;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Crafting.Tests
{
    public sealed class CraftingExpansionTests
    {
        private sealed class MapSpeciesLookup : ISpeciesLookup
        {
            private readonly Dictionary<string, SpeciesInfo> _map = new Dictionary<string, SpeciesInfo>();

            public void Add(string id, FieldUtility utility) =>
                _map[id] = new SpeciesInfo(id, "m_" + id, "t_" + id, CreatureStage.Juvenile, utility);

            public bool TryGet(string speciesId, out SpeciesInfo info) => _map.TryGetValue(speciesId, out info);
        }

        [Test]
        public void Cube_partner_grants_full_strength_element_and_top_three_resists()
        {
            var resist = new[] { 100, 100, 100, 150, 100, 130, 100, 100, 130, 100 };
            IReadOnlyList<EffectSpec> fx = CraftingExpansionRules.CubePartnerEffects(Element.Light, resist);

            Assert.That(fx.Count, Is.EqualTo(4));
            Assert.That(fx[0].Type, Is.EqualTo(EffectType.MasteryMod));
            Assert.That(fx[0].Element, Is.EqualTo(Element.Light));
            Assert.That(fx[0].Delta, Is.EqualTo(CraftingExpansionRules.AccessoryElementMastery));
            Assert.That(fx[1].Element, Is.EqualTo(Element.Fire));
            Assert.That(fx[2].Element, Is.EqualTo(Element.Ice), "tie 130 goes to the lower element index");
            Assert.That(fx[3].Element, Is.EqualTo(Element.Light));
            for (int i = 1; i < 4; i++)
            {
                Assert.That(fx[i].Type, Is.EqualTo(EffectType.ResistMod));
                Assert.That(fx[i].Delta, Is.EqualTo(CraftingExpansionRules.AccessoryResist), "no 25% partner share");
            }
        }

        [Test]
        public void Cube_partner_rejects_a_short_resist_table()
        {
            Assert.Throws<ContractException>(() => CraftingExpansionRules.CubePartnerEffects(Element.Fire, new int[9]));
        }

        [Test]
        public void Durability_degrades_to_zero_and_repairs_to_max()
        {
            var d = new DurabilityState(10);
            Assert.That(d.IsBroken, Is.False);
            d.Degrade(4);
            Assert.That(d.Current, Is.EqualTo(6));
            d.Degrade(10);
            Assert.That(d.Current, Is.EqualTo(0));
            Assert.That(d.IsBroken, Is.True);
            d.Repair();
            Assert.That(d.Current, Is.EqualTo(10));
            Assert.That(d.IsBroken, Is.False);

            Assert.Throws<ContractException>(() => new DurabilityState(0));
            Assert.Throws<ContractException>(() => d.Degrade(-1));
        }

        [Test]
        public void Crafting_utilities_respect_category_fallback_and_sub_partner_vessels()
        {
            var lookup = new MapSpeciesLookup();
            lookup.Add("sp_smelt_bead", FieldUtility.CraftSmeltFire);
            lookup.Add("sp_smelt_talisman", FieldUtility.CraftSmeltMetal);
            lookup.Add("sp_enchant_bead", FieldUtility.CraftEnchantLight);
            lookup.Add("sp_essence", FieldUtility.GatherEssence);

            Assert.That(FieldUtilityRules.GetKind(FieldUtility.CraftSmeltFire), Is.EqualTo(FieldUtilityKind.Trait));
            Assert.That(FieldUtilityRules.GetKind(FieldUtility.CraftSmeltMetal), Is.EqualTo(FieldUtilityKind.Move));
            Assert.That(FieldUtilityRules.GetKind(FieldUtility.GatherEssence), Is.EqualTo(FieldUtilityKind.Trait));

            var subs = new[]
            {
                new OwnedCreature(1, "sp_smelt_bead", VesselKind.GlassBead, 0),
                new OwnedCreature(2, "sp_smelt_talisman", VesselKind.GlassBead, 0), // mismatched vessel (needs talisman)
                new OwnedCreature(3, "sp_enchant_bead", VesselKind.MagicCube, 0),    // magic cube grants no utility
                new OwnedCreature(4, "sp_essence", VesselKind.GlassBead, 0),
            };
            HashSet<FieldUtility> team = FieldUtilityRules.CollectTeamUtilities(System.Array.Empty<OwnedCreature>(), subs, lookup);
            Assert.That(team.Contains(FieldUtility.CraftSmeltFire), Is.True);
            Assert.That(team.Contains(FieldUtility.CraftSmeltMetal), Is.False);
            Assert.That(team.Contains(FieldUtility.CraftEnchantLight), Is.False);
            Assert.That(team.Contains(FieldUtility.GatherEssence), Is.True);

            TeamUtilityModifiers mods = FieldUtilityRules.ComputeTeamModifiers(team);
            Assert.That(mods.CanGatherEssence, Is.True);

            Assert.That(CraftingExpansionRules.SatisfiesSkill(team, FieldUtility.CraftSmeltThunder), Is.True);
            Assert.That(CraftingExpansionRules.SatisfiesSkill(new[] { FieldUtility.CookFire }, FieldUtility.CraftSmeltMetal), Is.True);
            Assert.That(CraftingExpansionRules.SatisfiesSkill(new[] { FieldUtility.ScoutPotential }, FieldUtility.CraftEnchantShadow), Is.True);
            Assert.That(CraftingExpansionRules.SatisfiesSkill(new[] { FieldUtility.DropRateUp }, FieldUtility.GatherEssence), Is.True);
            Assert.That(CraftingExpansionRules.FallbackUtility(CraftSkillCategory.Enchant), Is.EqualTo(FieldUtility.ScoutPotential));
            Assert.That(FieldUtilityRules.VesselSupportsCraftingSkill(VesselKind.GlassBead), Is.True);
            Assert.That(FieldUtilityRules.VesselSupportsCraftingSkill(VesselKind.TalismanCard), Is.True);
            Assert.That(FieldUtilityRules.VesselSupportsCraftingSkill(VesselKind.MagicCube), Is.False);
        }

        [Test]
        public void Node_tier_drops_and_equipment_recipe_costs_match_16_4_1_ratio()
        {
            for (int i = 0; i < 30; i++)
            {
                Assert.That(CraftingExpansionRules.RollNodeMaterialTier(CraftMaterialTier.Tier1, 99UL, 10, i),
                    Is.EqualTo(CraftMaterialTier.Tier1));
                Assert.That(CraftingExpansionRules.RollNodeMaterialTier(CraftMaterialTier.Tier2, 99UL, 10, i),
                    Is.Not.EqualTo(CraftMaterialTier.Tier3));
            }

            Assert.That(CraftingExpansionRules.RollNodeMaterialTier(CraftMaterialTier.Tier3, 12345UL, 7, 3),
                Is.EqualTo(CraftingExpansionRules.RollNodeMaterialTier(CraftMaterialTier.Tier3, 12345UL, 7, 3)));

            Assert.That(CraftingExpansionRules.GetEquipmentRecipeCosts(CraftMaterialTier.Tier1, false), Is.EqualTo((4, 0, 0)));
            Assert.That(CraftingExpansionRules.GetEquipmentRecipeCosts(CraftMaterialTier.Tier2, false), Is.EqualTo((16, 4, 0)));
            Assert.That(CraftingExpansionRules.GetEquipmentRecipeCosts(CraftMaterialTier.Tier3, false), Is.EqualTo((64, 16, 4)));
            Assert.That(CraftingExpansionRules.GetEquipmentRecipeCosts(CraftMaterialTier.Tier3, true), Is.EqualTo((128, 32, 8)));
        }

        [Test]
        public void Six_stat_budgets_and_weapon_type_specs_match_crafting_design()
        {
            SixStatBonus w1 = CraftingExpansionRules.ComputeWeaponSixStats(CraftMaterialTier.Tier1, false, WeaponPolarity.Balanced);
            SixStatBonus w2h3 = CraftingExpansionRules.ComputeWeaponSixStats(CraftMaterialTier.Tier3, true, WeaponPolarity.Yang);
            SixStatBonus a3 = CraftingExpansionRules.ComputeArmorSixStats(CraftMaterialTier.Tier3, WeaponPolarity.Yin);

            Assert.That(w1.Total, Is.EqualTo(15));
            Assert.That(w2h3.Total, Is.EqualTo(90));
            Assert.That(w2h3.YangAtk, Is.GreaterThan(w2h3.YinAtk));
            Assert.That(a3.Total, Is.EqualTo(45));
            Assert.That(a3.YinDef, Is.GreaterThan(a3.YangDef));

            WeaponTypeSpec dagger = CraftingExpansionRules.GetWeaponTypeSpec(WeaponType.DaggerClaw);
            Assert.That((dagger.Range, dagger.IsTwoHanded, dagger.PowerPermille, dagger.SpeedPermille),
                Is.EqualTo((WeaponRange.Melee, false, 820, 1200)));
            WeaponTypeSpec cannon = CraftingExpansionRules.GetWeaponTypeSpec(WeaponType.HeavyCrossbowCannon);
            Assert.That((cannon.Range, cannon.IsTwoHanded, cannon.PowerPermille, cannon.SpeedPermille),
                Is.EqualTo((WeaponRange.Ranged, true, 1400, 750)));
        }

        [Test]
        public void Enchanting_rolls_are_reproducible_overwrite_slots_and_fashion_dyes()
        {
            EffectSpec w1 = CraftingExpansionRules.RollWeaponEnchant(77UL, CraftMaterialTier.Tier2, Element.Metal, false);
            EffectSpec w2 = CraftingExpansionRules.RollWeaponEnchant(77UL, CraftMaterialTier.Tier2, Element.Metal, true);
            Assert.That(w1.Type, Is.EqualTo(w2.Type));
            Assert.That(w2.Delta, Is.EqualTo(w1.Delta * 2));

            AccessoryEnchantResult elemRoll = CraftingExpansionRules.RollAccessoryEnchant(55UL, 0, Element.Fire, forceElemental: true);
            Assert.That(elemRoll.IsElemental, Is.True);
            Assert.That(elemRoll.ElementFloor100, Is.EqualTo(Element.Fire));
            Assert.That(elemRoll.Effects.Count, Is.EqualTo(1));

            AccessoryEnchantResult resRoll = CraftingExpansionRules.RollAccessoryEnchant(55UL, 1, Element.Fire, forceElemental: false);
            Assert.That(resRoll.IsElemental, Is.False);
            Assert.That(resRoll.ElementFloor100, Is.Null);
            Assert.That(resRoll.Effects.Count, Is.EqualTo(3));
            Assert.That(resRoll.Effects[0].Element, Is.EqualTo(Element.Fire));
            Assert.That(resRoll.Effects[1].Element, Is.Not.EqualTo(Element.Fire));
            Assert.That(resRoll.Effects[2].Element, Is.Not.EqualTo(Element.Fire));
            Assert.That(resRoll.Effects[1].Element, Is.Not.EqualTo(resRoll.Effects[2].Element));

            var gear = new EquipmentInstance("iron_armor", 50);
            gear.ApplyEnchant(0, elemRoll.Effects, elemRoll.ElementFloor100);
            gear.ApplyEnchant(0, resRoll.Effects, resRoll.ElementFloor100);
            Assert.That(gear.EnchantSlot0.Count, Is.EqualTo(3));
            Assert.That(gear.FloorElement0, Is.Null);

            Assert.Throws<ContractException>(() => gear.ApplyDye("dye_crimson"));
            gear.SetFashion("fashion_coat");
            gear.ApplyDye("dye_crimson");
            Assert.That(gear.DyeColorId, Is.EqualTo("dye_crimson"));
            gear.SetFashion(null);
            Assert.That(gear.DyeColorId, Is.Null);

            Assert.That(CraftingExpansionRules.CanRepair(RepairLocation.Lobby, CraftSkillCategory.Smith, null), Is.True);
            Assert.That(CraftingExpansionRules.CanRepair(RepairLocation.VillageNpc, CraftSkillCategory.Tailor, null), Is.True);
            Assert.That(CraftingExpansionRules.CanRepair(RepairLocation.FieldWithSkill, CraftSkillCategory.Smith,
                new[] { FieldUtility.ForageBerries }), Is.False);
            Assert.That(CraftingExpansionRules.CanRepair(RepairLocation.FieldWithSkill, CraftSkillCategory.Smith,
                new[] { FieldUtility.CraftSmithFire }), Is.True);
        }

        [Test]
        public void Quality_affix_rolls_are_reproducible()
        {
            List<CraftAffix> a = CraftingExpansionRules.RollAffixes(42UL, ItemQuality.Masterwork);
            List<CraftAffix> b = CraftingExpansionRules.RollAffixes(42UL, ItemQuality.Masterwork);
            Assert.That(a.Count, Is.EqualTo(2));
            Assert.That(b.Count, Is.EqualTo(2));
            Assert.That(a[0].StatId, Is.EqualTo(b[0].StatId));
            Assert.That(a[0].BonusPercent, Is.EqualTo(b[0].BonusPercent));
        }
    }
}
