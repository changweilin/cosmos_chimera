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

        [Test]
        public void FoodLifeSkills_ClassifyAlignAndComputeModifiers()
        {
            Assert.AreEqual(FieldUtilityKind.Move, FieldUtilityRules.GetKind(FieldUtility.HuntGround));
            Assert.AreEqual(FieldUtilityKind.Move, FieldUtilityRules.GetKind(FieldUtility.HuntSky));
            Assert.AreEqual(FieldUtilityKind.Move, FieldUtilityRules.GetKind(FieldUtility.SetTrap));
            Assert.AreEqual(FieldUtilityKind.Move, FieldUtilityRules.GetKind(FieldUtility.CastNet));
            Assert.AreEqual(FieldUtilityKind.Move, FieldUtilityRules.GetKind(FieldUtility.HarvestCrops));
            Assert.AreEqual(FieldUtilityKind.Trait, FieldUtilityRules.GetKind(FieldUtility.CulinaryPrep));
            Assert.AreEqual(FieldUtilityKind.Trait, FieldUtilityRules.GetKind(FieldUtility.FreezePrep));

            Assert.IsTrue(FieldUtilityRules.IsElementAligned(FieldUtility.HuntGround, Element.Fire));
            Assert.IsTrue(FieldUtilityRules.IsElementAligned(FieldUtility.HuntSky, Element.Wind));
            Assert.IsTrue(FieldUtilityRules.IsElementAligned(FieldUtility.SetTrap, Element.Shadow));
            Assert.IsTrue(FieldUtilityRules.IsElementAligned(FieldUtility.CastNet, Element.Water));
            Assert.IsTrue(FieldUtilityRules.IsElementAligned(FieldUtility.HarvestCrops, Element.Wood));
            Assert.IsTrue(FieldUtilityRules.IsElementAligned(FieldUtility.CulinaryPrep, Element.Ice));
            Assert.IsTrue(FieldUtilityRules.IsElementAligned(FieldUtility.FreezePrep, Element.Ice));
            Assert.IsFalse(FieldUtilityRules.IsElementAligned(FieldUtility.FreezePrep, Element.Fire));

            var active = new HashSet<FieldUtility>
            {
                FieldUtility.HuntGround,
                FieldUtility.HuntSky,
                FieldUtility.SetTrap,
                FieldUtility.CastNet,
                FieldUtility.HarvestCrops,
                FieldUtility.CulinaryPrep,
                FieldUtility.FreezePrep,
            };
            TeamUtilityModifiers mods = FieldUtilityRules.ComputeTeamModifiers(active);
            Assert.IsTrue(mods.CanHuntGround);
            Assert.IsTrue(mods.CanHuntSky);
            Assert.IsTrue(mods.CanSetTrap);
            Assert.IsTrue(mods.CanCastNet);
            Assert.IsTrue(mods.CanHarvestCrops);
            Assert.IsTrue(mods.CanCulinaryPrep);
            Assert.IsTrue(mods.CanFreezePrep);
        }

        [Test]
        public void FieldUtilityRoster_BalancesMoveTraitHalfAndElementSpread()
        {
            int moveCount = 0;
            int traitCount = 0;
            var elementCounts = new Dictionary<Element, int>();
            foreach (Element e in Enum.GetValues(typeof(Element)))
            {
                elementCounts[e] = 0;
            }

            foreach (FieldUtility u in Enum.GetValues(typeof(FieldUtility)))
            {
                if (u == FieldUtility.None) continue;
                FieldUtilityKind kind = FieldUtilityRules.GetKind(u);
                if (kind == FieldUtilityKind.Move) moveCount++;
                else if (kind == FieldUtilityKind.Trait) traitCount++;

                foreach (Element e in Enum.GetValues(typeof(Element)))
                {
                    if (FieldUtilityRules.IsElementAligned(u, e))
                    {
                        elementCounts[e]++;
                    }
                }
            }

            Assert.AreEqual(moveCount, traitCount, "Move and Trait counts should be equal (50/50 split).");
            foreach (KeyValuePair<Element, int> kv in elementCounts)
            {
                Assert.That(kv.Value, Is.InRange(12, 13), $"Element {kv.Key} count {kv.Value} out of balanced range 12..13.");
            }
        }

        [Test]
        public void SpatialTraversalRules_WaterAltitudeAndReturn_EnforceSpecLimits()
        {
            Assert.AreEqual(FieldUtilityKind.Move, FieldUtilityRules.GetKind(FieldUtility.Swim));
            Assert.IsTrue(FieldUtilityRules.IsElementAligned(FieldUtility.Swim, Element.Water));
            Assert.IsFalse(FieldUtilityRules.IsElementAligned(FieldUtility.Swim, Element.Wind));

            TeamUtilityModifiers none = FieldUtilityRules.ComputeTeamModifiers(new HashSet<FieldUtility>());
            WaterAccessResult shallowNone = SpatialTraversalRules.EvaluateWaterAccess(none, 0.8f);
            Assert.IsTrue(shallowNone.CanCrossSurface);
            Assert.IsFalse(shallowNone.CanSubmerge);
            Assert.IsTrue(shallowNone.CanReachTargetDepth);
            Assert.AreEqual(FieldUtilityRules.ShallowWadeSpeedPermille, shallowNone.SurfaceSpeedPermille);

            WaterAccessResult deepNone = SpatialTraversalRules.EvaluateWaterAccess(none, 3f);
            Assert.IsFalse(deepNone.CanCrossSurface);
            Assert.IsFalse(deepNone.CanReachTargetDepth);

            TeamUtilityModifiers swimOnly = FieldUtilityRules.ComputeTeamModifiers(new[] { FieldUtility.Swim });
            Assert.IsTrue(swimOnly.CanSwim);
            WaterAccessResult swimDeepSurface = SpatialTraversalRules.EvaluateWaterAccess(swimOnly, 3f);
            Assert.IsTrue(swimDeepSurface.CanCrossSurface);
            Assert.IsFalse(swimDeepSurface.CanSubmerge);
            Assert.IsFalse(swimDeepSurface.CanReachTargetDepth);
            Assert.AreEqual(FieldUtilityRules.SwimSpeedPermille, swimDeepSurface.SurfaceSpeedPermille);

            TeamUtilityModifiers diveOnly = FieldUtilityRules.ComputeTeamModifiers(new[] { FieldUtility.Dive });
            WaterAccessResult diveWater = SpatialTraversalRules.EvaluateWaterAccess(diveOnly, 3f);
            Assert.IsTrue(diveWater.CanCrossSurface);
            Assert.IsTrue(diveWater.CanSubmerge);
            Assert.IsTrue(diveWater.CanReachTargetDepth);
            Assert.AreEqual(FieldUtilityRules.DiveSurfaceSpeedPermille, diveWater.SurfaceSpeedPermille);
            Assert.Less(diveWater.SurfaceSpeedPermille, swimDeepSurface.SurfaceSpeedPermille);
            Assert.AreEqual(FieldUtilityRules.DiveUnderwaterSpeedPermille, diveWater.UnderwaterSpeedPermille);

            const float R = 960f;
            Assert.AreEqual(R, SpatialTraversalRules.StratosphereAltitudeM(R), 1e-3f);
            Assert.AreEqual(R * 1.25f, SpatialTraversalRules.SpaceStationAltitudeM(R), 1e-3f);
            Assert.AreEqual(R * 1.6f, SpatialTraversalRules.SpaceCeilingAltitudeM(R), 1e-3f);

            TeamUtilityModifiers flyOnly = FieldUtilityRules.ComputeTeamModifiers(new[] { FieldUtility.Fly });
            AltitudeAccessResult flyAtStrato = SpatialTraversalRules.EvaluateAltitudeAccess(flyOnly, R, R);
            Assert.IsTrue(flyAtStrato.CanLiftOff);
            Assert.IsTrue(flyAtStrato.CanReachTargetAltitude);
            Assert.IsFalse(flyAtStrato.IgnoresStratosphereCap);
            Assert.AreEqual(FieldUtilityRules.FlySpeedPermille, flyAtStrato.AirSpeedPermille);

            AltitudeAccessResult flyAtStation = SpatialTraversalRules.EvaluateAltitudeAccess(flyOnly, R, SpatialTraversalRules.SpaceStationAltitudeM(R));
            Assert.IsFalse(flyAtStation.CanReachTargetAltitude);
            Assert.AreEqual(0, flyAtStation.AirSpeedPermille);

            TeamUtilityModifiers levOnly = FieldUtilityRules.ComputeTeamModifiers(new[] { FieldUtility.Levitate });
            AltitudeAccessResult levAtStation = SpatialTraversalRules.EvaluateAltitudeAccess(levOnly, R, SpatialTraversalRules.SpaceStationAltitudeM(R));
            Assert.IsTrue(levAtStation.CanLiftOff);
            Assert.IsTrue(levAtStation.CanReachTargetAltitude);
            Assert.IsTrue(levAtStation.IgnoresStratosphereCap);
            Assert.AreEqual(FieldUtilityRules.LevitateSpeedPermille, levAtStation.AirSpeedPermille);

            TeamUtilityModifiers retOnly = FieldUtilityRules.ComputeTeamModifiers(new[] { FieldUtility.ReturnVillage });
            Assert.IsTrue(SpatialTraversalRules.CanReturnToSettlement(retOnly, Cosmimera.WorldGen.SettlementDomain.Land, true).Allowed);
            Assert.IsTrue(SpatialTraversalRules.CanReturnToSettlement(retOnly, Cosmimera.WorldGen.SettlementDomain.SkyIsland, true).Allowed);
            Assert.IsFalse(SpatialTraversalRules.CanReturnToSettlement(retOnly, Cosmimera.WorldGen.SettlementDomain.Land, false).Allowed);
            ReturnSettlementResult deepNoDive = SpatialTraversalRules.CanReturnToSettlement(retOnly, Cosmimera.WorldGen.SettlementDomain.DeepSea, true);
            Assert.IsFalse(deepNoDive.Allowed);
            Assert.IsTrue(deepNoDive.RequiresDive);

            TeamUtilityModifiers retAndDive = FieldUtilityRules.ComputeTeamModifiers(new[] { FieldUtility.ReturnVillage, FieldUtility.Dive });
            Assert.IsTrue(SpatialTraversalRules.CanReturnToSettlement(retAndDive, Cosmimera.WorldGen.SettlementDomain.DeepSea, true).Allowed);

            ReturnSettlementResult cradleAllowed = SpatialTraversalRules.CanReturnToCradleMoon(retOnly, true);
            Assert.IsTrue(cradleAllowed.Allowed);
            Assert.IsFalse(cradleAllowed.RequiresUtility);
            Assert.IsFalse(cradleAllowed.RequiresVisit);
            Assert.IsFalse(cradleAllowed.RequiresDive);

            ReturnSettlementResult cradleNoTutorial = SpatialTraversalRules.CanReturnToCradleMoon(retOnly, false);
            Assert.IsFalse(cradleNoTutorial.Allowed);
            Assert.IsTrue(cradleNoTutorial.RequiresVisit);

            TeamUtilityModifiers noRet = FieldUtilityRules.ComputeTeamModifiers(new[] { FieldUtility.Fly });
            ReturnSettlementResult cradleNoUtility = SpatialTraversalRules.CanReturnToCradleMoon(noRet, true);
            Assert.IsFalse(cradleNoUtility.Allowed);
            Assert.IsTrue(cradleNoUtility.RequiresUtility);
        }
    }
}
