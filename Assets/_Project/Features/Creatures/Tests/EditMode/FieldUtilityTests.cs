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
    }
}
