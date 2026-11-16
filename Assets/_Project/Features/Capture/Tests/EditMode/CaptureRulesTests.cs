using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Capture.Tests
{
    public sealed class CaptureRulesTests
    {
        [Test]
        public void Chance_uses_boss_or_base_plus_bounty_clamped()
        {
            Assert.That(CaptureRules.ChancePercent(false, false, 60, 100), Is.EqualTo(60));
            Assert.That(CaptureRules.ChancePercent(false, true, 60, 100), Is.EqualTo(70));
            Assert.That(CaptureRules.ChancePercent(true, true, 60, 100), Is.EqualTo(100));
            Assert.That(CaptureRules.ChancePercent(true, false, 60, 95), Is.EqualTo(95));
        }

        [Test]
        public void Chance_rejects_out_of_range_percents()
        {
            Assert.Throws<ContractException>(() => CaptureRules.ChancePercent(false, false, 101, 100));
            Assert.Throws<ContractException>(() => CaptureRules.ChancePercent(false, false, 50, -1));
            Assert.Throws<ContractException>(() => CaptureRules.ChancePercent(false, false, 50, 100, -1));
        }

        [Test]
        public void Chance_includes_utility_bonus_and_clamps()
        {
            Assert.That(CaptureRules.ChancePercent(false, false, 60, 100, 15), Is.EqualTo(75));
            Assert.That(CaptureRules.ChancePercent(false, true, 85, 100, 15), Is.EqualTo(100));
        }

        [Test]
        public void Roll_extremes_are_certain()
        {
            var rng = new Pcg32(1, 2);
            for (int i = 0; i < 1000; i++)
            {
                Assert.That(CaptureRules.Roll(100, ref rng), Is.True);
                Assert.That(CaptureRules.Roll(0, ref rng), Is.False);
            }
        }

        [Test]
        public void Roll_rate_matches_chance_within_one_percent()
        {
            var rng = new Pcg32(42, 7);
            int hits = 0;
            const int n = 200000;
            for (int i = 0; i < n; i++)
            {
                if (CaptureRules.Roll(60, ref rng))
                {
                    hits++;
                }
            }

            Assert.That(hits / (double)n, Is.EqualTo(0.6).Within(0.01));
        }

        [Test]
        public void Roll_rejects_out_of_range_chance()
        {
            var rng = new Pcg32(1, 2);
            Assert.Throws<ContractException>(() => CaptureRules.Roll(101, ref rng));
        }

        [Test]
        public void Basic_four_tiers_scale_by_stage_difference_and_clamp()
        {
            var t1 = CaptureVesselSpec.Basic(CreatureStage.Juvenile, 50);
            var t3 = CaptureVesselSpec.Basic(CreatureStage.Adult, 70);
            var env = new CaptureEnvironment(false, 0);

            Assert.That(CaptureRules.ChancePercent(t1, CaptureTarget.Wild(CreatureStage.Juvenile), env), Is.EqualTo(50));
            Assert.That(CaptureRules.ChancePercent(t3, CaptureTarget.Wild(CreatureStage.Juvenile), env), Is.EqualTo(100));
            Assert.That(CaptureRules.ChancePercent(t1, CaptureTarget.Wild(CreatureStage.Adult), env), Is.EqualTo(0));
            Assert.That(CaptureRules.ChancePercent(t1, CaptureTarget.Wild(CreatureStage.Adolescent), env), Is.EqualTo(25));
        }

        [Test]
        public void Basic_vessel_guarantees_planet_boss_only_when_tier_meets_boss_stage()
        {
            var t1 = CaptureVesselSpec.Basic(CreatureStage.Juvenile, 40);
            var t2 = CaptureVesselSpec.Basic(CreatureStage.Adolescent, 45);
            var t4 = CaptureVesselSpec.Basic(CreatureStage.Overload, 60);
            var env = new CaptureEnvironment(false, 0);

            Assert.That(CaptureRules.ChancePercent(t1, CaptureTarget.PlanetBoss(PlanetSize.Asteroid), env), Is.EqualTo(100));
            Assert.That(CaptureRules.ChancePercent(t2, CaptureTarget.PlanetBoss(PlanetSize.Asteroid), env), Is.EqualTo(100));
            Assert.That(CaptureRules.ChancePercent(t1, CaptureTarget.PlanetBoss(PlanetSize.Moon), env), Is.LessThan(100));
            Assert.That(CaptureRules.ChancePercent(t4, CaptureTarget.PlanetBoss(PlanetSize.Supergiant), env), Is.EqualTo(100));
            Assert.That(CaptureRules.ChancePercent(t4, CaptureTarget.DungeonBoss(CreatureStage.Adult, Element.Fire), env), Is.LessThan(100));
        }

        [Test]
        public void Advanced_vessel_boosts_matching_terrain_and_guarantees_matching_dungeon_boss()
        {
            var fireVessel = CaptureVesselSpec.Advanced(Element.Fire, 50, 25);
            var surfaceEnv = new CaptureEnvironment(false, 0);
            var fireDungeonEnv = new CaptureEnvironment(false, 0, inAdvancedTerrain: true, Element.Fire);
            var iceDungeonEnv = new CaptureEnvironment(false, 0, inAdvancedTerrain: true, Element.Ice);

            Assert.That(CaptureRules.ChancePercent(fireVessel, CaptureTarget.Wild(CreatureStage.Adult), surfaceEnv), Is.EqualTo(50));
            Assert.That(CaptureRules.ChancePercent(fireVessel, CaptureTarget.Wild(CreatureStage.Adult), fireDungeonEnv), Is.EqualTo(75));
            Assert.That(CaptureRules.ChancePercent(fireVessel, CaptureTarget.Wild(CreatureStage.Adult), iceDungeonEnv), Is.EqualTo(50));

            Assert.That(CaptureRules.ChancePercent(fireVessel, CaptureTarget.DungeonBoss(CreatureStage.Adult, Element.Fire), fireDungeonEnv), Is.EqualTo(100));
            Assert.That(CaptureRules.ChancePercent(fireVessel, CaptureTarget.DungeonBoss(CreatureStage.Adult, Element.Fire), surfaceEnv), Is.EqualTo(50));
            Assert.That(CaptureRules.ChancePercent(fireVessel, CaptureTarget.DungeonBoss(CreatureStage.Adult, Element.Ice), iceDungeonEnv), Is.EqualTo(50));
            Assert.That(CaptureRules.ChancePercent(fireVessel, CaptureTarget.PlanetBoss(PlanetSize.Asteroid), surfaceEnv), Is.EqualTo(50));
        }

        [Test]
        public void Tiered_capture_specs_reject_invalid_preconditions()
        {
            Assert.Throws<ContractException>(() => CaptureVesselSpec.Basic((CreatureStage)9, 50));
            Assert.Throws<ContractException>(() => CaptureVesselSpec.Basic(CreatureStage.Juvenile, 101));
            Assert.Throws<ContractException>(() => CaptureVesselSpec.Advanced((Element)99, 50, 20));
            Assert.Throws<ContractException>(() => CaptureVesselSpec.Advanced(Element.Fire, 50, -1));
            Assert.Throws<ContractException>(() => CaptureTarget.PlanetBoss((PlanetSize)9));
            Assert.Throws<ContractException>(() => CaptureTarget.DungeonBoss((CreatureStage)9, Element.Fire));
            Assert.Throws<ContractException>(() => new CaptureEnvironment(false, -1));
            Assert.Throws<ContractException>(() => new CaptureStageCurve(-1, 10));
        }
    }
}
