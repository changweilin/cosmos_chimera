using System.Collections.Generic;
using Cosmimera.Capture;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Crafting.Tests
{
    public sealed class CraftingExpansionTests
    {
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
        public void Quality_affix_rolls_are_reproducible_and_magic_cube_cannot_supply_crafting_skills()
        {
            List<CraftAffix> a = CraftingExpansionRules.RollAffixes(42UL, ItemQuality.Masterwork);
            List<CraftAffix> b = CraftingExpansionRules.RollAffixes(42UL, ItemQuality.Masterwork);
            Assert.That(a.Count, Is.EqualTo(2));
            Assert.That(b.Count, Is.EqualTo(2));
            Assert.That(a[0].StatId, Is.EqualTo(b[0].StatId));
            Assert.That(a[0].BonusPercent, Is.EqualTo(b[0].BonusPercent));

            Assert.That(CraftingExpansionRules.FallbackUtility(CraftSkillCategory.Smelt), Is.EqualTo(FieldUtility.CookFire));
            Assert.That(CraftingExpansionRules.VesselSupportsCraftingSkill(VesselKind.GlassOrb), Is.True);
            Assert.That(CraftingExpansionRules.VesselSupportsCraftingSkill(VesselKind.TalismanCard), Is.True);
            Assert.That(CraftingExpansionRules.VesselSupportsCraftingSkill(VesselKind.MagicCube), Is.False);
        }
    }
}
