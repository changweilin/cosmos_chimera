using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Combat.Tests
{
    // Regressions for docs/reviews/P2-02-P2-04-sol-r6.md (R2, R3, S1).
    public sealed class CombatReviewSolR6Tests
    {
        private DerivedStats _stats;
        private HitVolumeDefaults _d;

        [SetUp]
        public void SetUp()
        {
            _stats = new DerivedStats(BalanceFixture.Coefficients());
            _d = BalanceFixture.HitDefaults();
        }

        [Test]
        public void R2_exactly_collinear_target_is_at_distance_zero_for_a_front_rect()
        {
            var planet = new PlanetConfig(640f, float3.zero);
            HitVolume front = MoveGeometry.Build(MoveShape.Front, MoveMovement.None, RangeClass.Melee,
                BurstOrigin.Caster, 2000, 0, _stats, _d);
            var anchor = new float3(380f, 236f, 213f);
            // 1025/1024 is exact in binary, so target is exactly on the anchor's ray: geodesic distance 0, height ~0.48 m.
            float3 target = anchor * (1025f / 1024f);
            float3 forward = PlanetFrame.At(planet, anchor).North;

            Assert.IsTrue(MoveGeometry.Overlaps(front, planet, anchor, forward, target, 1e-6f));
            Assert.IsTrue(MoveGeometry.Overlaps(front, planet, anchor, forward, target, 0f));
        }

        [Test]
        public void R3_radial_height_just_above_the_limit_is_rejected()
        {
            var planet = new PlanetConfig(8000f, float3.zero);
            HitVolume burst = MoveGeometry.Build(MoveShape.Burst, MoveMovement.None, RangeClass.Melee,
                BurstOrigin.Caster, 2000, 0, _stats, _d);
            var anchor = new float3(4096f, 32f, 0f);
            // True height 1.5000458 m vs limit HalfHeight 1 m + radius 0.5 m; float subtraction rounded it to 1.5.
            float3 target = anchor * (8195f / 8192f);

            Assert.IsFalse(MoveGeometry.Overlaps(burst, planet, anchor, float3.zero, target, 0.5f));
        }

        [Test]
        public void R3_radial_height_just_below_the_limit_at_an_offset_center_is_accepted()
        {
            var planet = new PlanetConfig(8000f, new float3(-16000f, 16000f, 8000f));
            HitVolume burst = MoveGeometry.Build(MoveShape.Burst, MoveMovement.None, RangeClass.Melee,
                BurstOrigin.Caster, 2000, 0, _stats, _d);
            var anchor = new float3(-7311.14599609375f, 6906.2275390625f, 10301.33984375f);
            // True height 1.49876 m (1.24 mm inside the 1.5 m limit); float subtraction measured 1.50098 m.
            var target = new float3(-7310.12744140625f, 6905.16162109375f, 10301.609375f);

            Assert.IsTrue(MoveGeometry.Overlaps(burst, planet, anchor, float3.zero, target, 0.5f));
        }

        // ShellSlack = 1e-6 × (max |center component| + 2R) = 0.01728 m here. The accepted aims below are outside a
        // 10× smaller slack, so they pin the published coefficient (r6 ST02 survived without them).
        [Test]
        public void S1_shell_slack_coefficient_is_pinned_on_both_sides()
        {
            var planet = new PlanetConfig(640f, new float3(16000f, 16000f, 16000f));
            HitVolume arc = MoveGeometry.Build(MoveShape.Arc, MoveMovement.None, RangeClass.Mid,
                BurstOrigin.Caster, 12000, 0, _stats, _d);
            var caster = new float3(16640f, 16000f, 16000f);

            Assert.DoesNotThrow(() => MoveGeometry.ResolveAnchor(arc, planet, caster, new float3(16000f, 16319.990234375f, 16000f)));
            Assert.DoesNotThrow(() => MoveGeometry.ResolveAnchor(arc, planet, caster, new float3(16000f, 17280.009765625f, 16000f)));
            Assert.Throws<ContractException>(() => MoveGeometry.ResolveAnchor(arc, planet, caster, new float3(16000f, 16319.97f, 16000f)));
            Assert.Throws<ContractException>(() => MoveGeometry.ResolveAnchor(arc, planet, caster, new float3(16000f, 17280.03f, 16000f)));
        }
    }
}
