using System;
using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Creatures.Tests
{
    public class FieldUtilityTests
    {
        [Test]
        public void GetKind_ClassifiesNoneMovesAndTraits()
        {
            Assert.AreEqual(FieldUtilityKind.None, FieldUtilityRules.GetKind(FieldUtility.None));
            Assert.AreEqual(FieldUtilityKind.Move, FieldUtilityRules.GetKind(FieldUtility.Dive));
            Assert.AreEqual(FieldUtilityKind.Move, FieldUtilityRules.GetKind(FieldUtility.CookFire));
            Assert.AreEqual(FieldUtilityKind.Move, FieldUtilityRules.GetKind(FieldUtility.ReturnVillage));
            Assert.AreEqual(FieldUtilityKind.Trait, FieldUtilityRules.GetKind(FieldUtility.ImmuneVolcano));
            Assert.AreEqual(FieldUtilityKind.Trait, FieldUtilityRules.GetKind(FieldUtility.CatchRateUp));
            Assert.AreEqual(FieldUtilityKind.Trait, FieldUtilityRules.GetKind(FieldUtility.EggHatchBoost));
        }

        [Test]
        public void ImmunityForTerrain_MapsAllTenElements()
        {
            var seen = new HashSet<FieldUtility>();
            foreach (Element e in Enum.GetValues(typeof(Element)))
            {
                FieldUtility immunity = FieldUtilityRules.ImmunityForTerrain(e);
                Assert.AreEqual(FieldUtilityKind.Trait, FieldUtilityRules.GetKind(immunity));
                Assert.IsTrue(seen.Add(immunity), $"Duplicate immunity for {e}");
            }

            Assert.AreEqual(10, seen.Count);
        }

        [Test]
        public void CollectTeamUtilities_MainsContributeAnyKind_SubsRequireMatchingVessel()
        {
            var lookup = new FakeSpeciesLookup()
                .AddUtility("main_fire", FieldUtility.CookFire)             // Move
                .AddUtility("main_ice", FieldUtility.ImmuneIceCap)          // Trait
                .AddUtility("main_none", FieldUtility.None)
                .AddUtility("sub_card_move", FieldUtility.Dive)             // Move
                .AddUtility("sub_card_trait", FieldUtility.CatchRateUp)     // Trait (ignored by TalismanCard)
                .AddUtility("sub_bead_trait", FieldUtility.ImmuneVolcano)   // Trait
                .AddUtility("sub_bead_move", FieldUtility.Fly)              // Move (ignored by GlassBead)
                .AddUtility("sub_cube_move", FieldUtility.Mount);           // Move (ignored by MagicCube)

            var mains = new[]
            {
                Make.Creature(1, "main_fire", VesselKind.MagicCube),
                Make.Creature(2, "main_ice", VesselKind.TalismanCard),
                Make.Creature(3, "main_none", VesselKind.GlassBead),
            };

            var subs = new[]
            {
                Make.Creature(10, "sub_card_move", VesselKind.TalismanCard),
                Make.Creature(11, "sub_card_trait", VesselKind.TalismanCard),
                Make.Creature(12, "sub_bead_trait", VesselKind.GlassBead),
                Make.Creature(13, "sub_bead_move", VesselKind.GlassBead),
                Make.Creature(14, "sub_cube_move", VesselKind.MagicCube),
            };

            HashSet<FieldUtility> active = FieldUtilityRules.CollectTeamUtilities(mains, subs, lookup);

            Assert.IsFalse(active.Contains(FieldUtility.None));
            Assert.IsTrue(active.Contains(FieldUtility.CookFire));
            Assert.IsTrue(active.Contains(FieldUtility.ImmuneIceCap));
            Assert.IsTrue(active.Contains(FieldUtility.Dive));
            Assert.IsTrue(active.Contains(FieldUtility.ImmuneVolcano));

            Assert.IsFalse(active.Contains(FieldUtility.CatchRateUp));
            Assert.IsFalse(active.Contains(FieldUtility.Fly));
            Assert.IsFalse(active.Contains(FieldUtility.Mount));
        }

        [Test]
        public void CanEnterAdvancedTerrain_ChecksRequiredImmunity()
        {
            var active = new HashSet<FieldUtility>
            {
                FieldUtility.ImmuneVolcano,
                FieldUtility.Dive,
            };

            Assert.IsTrue(FieldUtilityRules.CanEnterAdvancedTerrain(active, Element.Fire));
            Assert.IsFalse(FieldUtilityRules.CanEnterAdvancedTerrain(active, Element.Water));
            Assert.IsFalse(FieldUtilityRules.CanEnterAdvancedTerrain(active, Element.Ice));
        }

        [Test]
        public void CollectTeamUtilities_RejectsNullArguments()
        {
            var lookup = new FakeSpeciesLookup();
            Assert.Throws<ContractException>(() => FieldUtilityRules.CollectTeamUtilities(null, null, lookup));
            Assert.Throws<ContractException>(() => FieldUtilityRules.CollectTeamUtilities(Array.Empty<OwnedCreature>(), null, null));
        }

        [Test]
        public void TryRequiredElementForTerrain_And_CanEnterTerrain_CoverAllTenTerrainsAndAliases()
        {
            Assert.IsTrue(FieldUtilityRules.TryRequiredElementForTerrain("laboratory", out Element labEl));
            Assert.AreEqual(Element.Metal, labEl);
            Assert.IsTrue(FieldUtilityRules.TryRequiredElementForTerrain("server_room", out Element aliasEl));
            Assert.AreEqual(Element.Metal, aliasEl);
            Assert.IsTrue(FieldUtilityRules.TryRequiredElementForTerrain("temple", out Element templeEl));
            Assert.AreEqual(Element.Light, templeEl);
            Assert.IsFalse(FieldUtilityRules.TryRequiredElementForTerrain("grassland", out _));
            Assert.IsFalse(FieldUtilityRules.TryRequiredElementForTerrain(null, out _));

            var utilities = new HashSet<FieldUtility> { FieldUtility.ImmuneVolcano };
            Assert.IsTrue(FieldUtilityRules.CanEnterTerrain(utilities, "grassland"));
            Assert.IsTrue(FieldUtilityRules.CanEnterTerrain(utilities, "volcano"));
            Assert.IsTrue(FieldUtilityRules.CanEnterTerrain(utilities, "caldera"));
            Assert.IsFalse(FieldUtilityRules.CanEnterTerrain(utilities, "temple"));

            Assert.Throws<ContractException>(() => FieldUtilityRules.CanEnterTerrain(null, "volcano"));
            Assert.Throws<ContractException>(() => FieldUtilityRules.CanEnterTerrain(utilities, ""));
        }

        [Test]
        public void ComputeTeamModifiers_AggregatesActiveUtilities()
        {
            var utilities = new HashSet<FieldUtility>
            {
                FieldUtility.CatchRateUp,
                FieldUtility.DropRateUp,
                FieldUtility.MagnetPickupDouble,
                FieldUtility.PartyStealth,
                FieldUtility.Mount,
                FieldUtility.PreventEscape,
                FieldUtility.WeatherControl,
                FieldUtility.CookFire,
                FieldUtility.ForageBerries,
            };

            TeamUtilityModifiers mods = FieldUtilityRules.ComputeTeamModifiers(utilities);
            Assert.AreEqual(FieldUtilityRules.CatchRateBonusPercentValue, mods.CatchRateBonusPercent);
            Assert.AreEqual(FieldUtilityRules.DropRateBonusPercentValue, mods.DropRateBonusPercent);
            Assert.AreEqual(2, mods.PickupMultiplier);
            Assert.AreEqual(FieldUtilityRules.StealthDetectionPermille, mods.WildDetectionPermille);
            Assert.AreEqual(FieldUtilityRules.MountSpeedPermille, mods.MoveSpeedPermille);
            Assert.IsTrue(mods.IgnoresTerrainSlow);
            Assert.IsTrue(mods.PreventsWildEscape);
            Assert.IsTrue(mods.CanWeatherControl);
            Assert.IsTrue(mods.CanCookFire);
            Assert.IsTrue(mods.CanForageBerries);
            Assert.IsFalse(mods.CanDive);

            Assert.Throws<ContractException>(() => FieldUtilityRules.ComputeTeamModifiers(null));
        }

        [Test]
        public void IsElementAligned_ChecksThematicCompatibility()
        {
            Assert.IsTrue(FieldUtilityRules.IsElementAligned(FieldUtility.None, Element.Fire));
            Assert.IsTrue(FieldUtilityRules.IsElementAligned(FieldUtility.Mount, Element.Thunder));
            Assert.IsTrue(FieldUtilityRules.IsElementAligned(FieldUtility.Mount, Element.Ice));
            Assert.IsFalse(FieldUtilityRules.IsElementAligned(FieldUtility.CampfireRevive, Element.Ice));
            Assert.IsTrue(FieldUtilityRules.IsElementAligned(FieldUtility.CampfireRevive, Element.Fire));
            Assert.IsFalse(FieldUtilityRules.IsElementAligned(FieldUtility.ProduceMilk, Element.Fire));
            Assert.Throws<ContractException>(() => FieldUtilityRules.IsElementAligned((FieldUtility)999, Element.Fire));
            Assert.Throws<ContractException>(() => FieldUtilityRules.IsElementAligned(FieldUtility.CookFire, (Element)999));
        }

        [Test]
        public void TryEvaluateCookedMeal_RequiresCookFireAndMatchingIngredient()
        {
            var missingCook = new HashSet<FieldUtility> { FieldUtility.ForageBerries };
            Assert.IsFalse(FieldUtilityRules.TryEvaluateCookedMeal(missingCook, FieldUtility.ForageBerries, out _, out _, out _));

            var complete = new HashSet<FieldUtility> { FieldUtility.CookFire, FieldUtility.Fishing };
            Assert.IsTrue(FieldUtilityRules.TryEvaluateCookedMeal(complete, FieldUtility.Fishing, out Element el, out int mastery, out int heal));
            Assert.AreEqual(Element.Water, el);
            Assert.Greater(mastery, 0);
            Assert.Greater(heal, 0);
            Assert.IsFalse(FieldUtilityRules.TryEvaluateCookedMeal(complete, FieldUtility.ForageBerries, out _, out _, out _));

            Assert.Throws<ContractException>(() => FieldUtilityRules.TryEvaluateCookedMeal(null, FieldUtility.Fishing, out _, out _, out _));
        }
    }
}
