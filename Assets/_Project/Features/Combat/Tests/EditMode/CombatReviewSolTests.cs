using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Combat.Tests
{
    // Regressions for docs/reviews/P2-02-P2-04-sol.md. Each test is named after the finding it pins.
    public sealed class CombatReviewSolTests
    {
        private const float R = 640f;
        private DerivedStats _stats;
        private HitVolumeDefaults _d;
        private PlanetConfig _planet;
        private float3 _caster;
        private float3 _up;
        private float3 _forward;

        [SetUp]
        public void SetUp()
        {
            _stats = new DerivedStats(BalanceFixture.Coefficients());
            _d = BalanceFixture.HitDefaults();
            _planet = new PlanetConfig(R, float3.zero);
            _up = new float3(0f, 1f, 0f);
            _caster = _up * R;
            _forward = new float3(0f, 0f, 1f);
        }

        // Azimuthal-equidistant (x right, z forward) meters around the caster, plus altitude.
        private float3 AtXZ(float x, float z, float altitude = 0f)
        {
            float d = math.sqrt(x * x + z * z);
            float3 right = math.cross(_up, _forward);
            float3 planar = d > 0f ? (x * right + z * _forward) / d : _forward;
            float a = d / R;
            return (math.cos(a) * _up + math.sin(a) * planar) * (R + altitude);
        }

        private HitVolume Build(MoveShape shape, RangeClass rc, int baseMm, int str,
            MoveMovement movement = MoveMovement.None, BurstOrigin origin = BurstOrigin.Caster)
        {
            return MoveGeometry.Build(shape, movement, rc, origin, baseMm, str, _stats, _d);
        }

        private bool Hits(in HitVolume v, float3 target, float radius = 0f, float3? anchor = null, float3? forward = null)
        {
            return MoveGeometry.Overlaps(v, _planet, anchor ?? _caster, forward ?? _forward, target, radius);
        }

        // ---- G1 ----
        [Test]
        public void G1_antipodal_target_is_far_away_not_at_distance_zero()
        {
            float3 antipode = -_caster;
            HitVolume[] volumes =
            {
                Build(MoveShape.Front, RangeClass.Melee, 2000, 0),
                Build(MoveShape.Line, RangeClass.Long, 24000, 0),
                Build(MoveShape.Sweep, RangeClass.Melee, 2000, 0),
                Build(MoveShape.Cone, RangeClass.Mid, 12000, 0),
                Build(MoveShape.Burst, RangeClass.Melee, 2000, 0),
                Build(MoveShape.Ally, RangeClass.Long, 24000, 0),
            };
            foreach (HitVolume v in volumes)
            {
                Assert.IsFalse(Hits(v, antipode), v.Kind.ToString());
            }

            Assert.IsTrue(Hits(volumes[4], _caster), "a coincident target still sits at distance 0");
        }

        // ---- r2 N1: off-axis antipode and near-antipode (exact witness from the review) ----
        [Test]
        public void N1_off_axis_and_near_antipodal_targets_miss_directional_volumes()
        {
            float3 anchor = math.normalize(new float3(1f, 1f, 1f)) * R;
            float3 forward = new float3(-0.4082483f, 0.8164966f, -0.4082483f);
            float3[] targets = { new float3(-369.8092f, -369.8092f, -369.809174f), -anchor };
            HitVolume[] volumes =
            {
                Build(MoveShape.Front, RangeClass.Melee, 2000, 0),
                Build(MoveShape.Line, RangeClass.Melee, 2000, 0),
                Build(MoveShape.Sweep, RangeClass.Melee, 2000, 0),
                Build(MoveShape.Cone, RangeClass.Melee, 2000, 0),
            };
            foreach (float3 target in targets)
            {
                foreach (HitVolume v in volumes)
                {
                    Assert.IsFalse(Hits(v, target, 0f, anchor, forward), $"{v.Kind} vs {target}");
                }
            }

            Assert.IsTrue(Hits(volumes[0], anchor + forward * 1f, 0f, anchor, forward), "a real front target still hits");
        }

        // ---- r2 N2 / N3: landing point keeps radius and range near the antipode and at large coordinates ----
        [Test]
        public void N2_near_antipodal_aim_keeps_the_aim_radius()
        {
            float3 up = math.normalize(new float3(1f, 1f, 1f));
            float3 caster = up * R;
            float3 north = PlanetFrame.FromUp(up).North;
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Mid, 12000, 0);
            foreach (float offset in new[] { 0.0007f, 0.002f, 0.01f, 0.1f })
            {
                float3 aim = -caster + north * offset;
                float3 landed = MoveGeometry.ResolveAnchor(arc, _planet, caster, aim);
                Assert.IsTrue(math.all(math.isfinite(landed)), $"offset {offset}");
                Assert.AreEqual(math.length(aim), math.length(landed), 0.002f, $"radius, offset {offset}");
                float3 u = math.normalize(landed);
                float d = math.atan2(math.length(math.cross(up, u)), math.dot(up, u)) * R;
                Assert.AreEqual(arc.CastDistanceMm / 1000f, d, 0.002f, $"distance, offset {offset}");
            }
        }

        [Test]
        public void N3_landing_distance_stays_within_quantization_slack_at_the_center_limit()
        {
            var planet = new PlanetConfig(8000f, new float3(16000f, 16000f, 16000f));
            float3 up = math.normalize(new float3(7f, 13f, 16f));
            float3 caster = planet.ToWorld(up, 0f);
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Mid, 12000, 0);
            float3 landed = MoveGeometry.ResolveAnchor(arc, planet, caster, planet.ToWorld(-up, 5f));
            double3 c = (double3)(caster - planet.Center);
            double3 l = (double3)(landed - planet.Center);
            double angle = math.atan2(math.length(math.cross(c, l)), math.dot(c, l));
            double slackM = 0.001 + 1e-7 * math.cmax(math.abs((double3)landed));
            Assert.LessOrEqual(angle * math.length(c), arc.CastDistanceMm / 1000.0 + slackM);
        }

        // ---- r2 N4 ----
        [Test]
        public void N4_points_within_a_meter_of_the_center_are_contract_violations()
        {
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Mid, 12000, 0);
            var tiny = new float3(1e-30f, 0f, 0f);
            Assert.Throws<ContractException>(() => MoveGeometry.ResolveAnchor(arc, _planet, tiny, tiny));
            Assert.Throws<ContractException>(() => Hits(arc, new float3(0.5f, 0f, 0f)));
        }

        // ---- r3 P1: combat shell R/2..2R and the landing precision it buys ----
        [Test]
        public void P1_positions_outside_the_combat_shell_are_contract_violations()
        {
            HitVolume burst = Build(MoveShape.Burst, RangeClass.Melee, 2000, 0);
            float3 dir = new float3(1f, 0f, 0f);
            Assert.DoesNotThrow(() => Hits(burst, dir * (R * 0.5f)));
            Assert.DoesNotThrow(() => Hits(burst, dir * (R * 2f)));
            Assert.Throws<ContractException>(() => Hits(burst, dir * (R * 0.49f)));
            Assert.Throws<ContractException>(() => Hits(burst, dir * (R * 2.01f)));
            Assert.Throws<ContractException>(() => Hits(burst, AtXZ(0f, 1f), 0f, dir * (R * 2.01f)));
        }

        [Test]
        public void P1_landing_distance_holds_at_the_extreme_radius_ratio()
        {
            var planet = new PlanetConfig(8000f, new float3(16000f, 16000f, 16000f));
            float3 up = math.normalize(new float3(7f, 13f, 16f));
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Mid, 12000, 0);
            float3[] casters = { planet.ToWorld(up, 7990f), planet.ToWorld(up, 0f), planet.ToWorld(up, -3990f) };
            float3[] aims =
            {
                planet.ToWorld(-up, -3990f), planet.ToWorld(-up, 7990f),
                planet.ToWorld(math.normalize(new float3(1f, -2f, 0.5f)), -3990f),
            };
            foreach (float3 caster in casters)
            {
                foreach (float3 aim in aims)
                {
                    float3 landed = MoveGeometry.ResolveAnchor(arc, planet, caster, aim);
                    double3 c = (double3)(caster - planet.Center);
                    double3 l = (double3)(landed - planet.Center);
                    double angle = math.atan2(math.length(math.cross(c, l)), math.dot(c, l));
                    double slackM = 0.001 + 1e-6 * math.cmax(math.abs((double3)landed));
                    Assert.LessOrEqual(angle * math.length(c), arc.CastDistanceMm / 1000.0 + slackM, $"{caster} -> {aim}");
                    Assert.AreEqual(math.length((double3)(aim - planet.Center)), math.length(l), slackM, "radius");
                }
            }
        }

        // ---- r3 P2: the North fallback covers exactly the documented degenerate band ----
        [Test]
        public void P2_north_fallback_only_inside_the_degenerate_band()
        {
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Mid, 12000, 0);
            float3 north = PlanetFrame.FromUp(_up).North;
            float3 inside = MoveGeometry.ResolveAnchor(arc, _planet, _caster, new float3(0f, -R, 3.2e-7f));
            Assert.Greater(math.dot(math.normalize(inside - _caster), north), 0.99f, "sin <= 1e-9: heads North");
            // r3's witness (sin 5e-7) used to go North; in double it is well determined and follows +Z.
            foreach (float zOffset in new[] { 0.00032f, 0.01f })
            {
                float3 outside = MoveGeometry.ResolveAnchor(arc, _planet, _caster, new float3(0f, -R, zOffset));
                Assert.Greater(math.dot(math.normalize(outside - _caster), new float3(0f, 0f, 1f)), 0.99f,
                    $"outside the band it follows the aim's great circle ({zOffset})");
            }
        }

        // ---- r4 Q1–Q4 (exact witnesses from docs/reviews/P2-02-P2-04-sol-r4.md) ----
        private static double CastSlackM(float3 landed) => 0.001 + 1e-6 * math.cmax(math.abs((double3)landed));

        private static double GeodesicAtCaster(in PlanetConfig planet, float3 caster, float3 landed)
        {
            double3 c = (double3)caster - (double3)planet.Center;
            double3 l = (double3)landed - (double3)planet.Center;
            return math.atan2(math.length(math.cross(c, l)), math.dot(c, l)) * math.length(c);
        }

        [Test]
        public void Q1_landing_distance_post_holds_with_an_offset_center()
        {
            var planet = new PlanetConfig(8000f, new float3(8963.729f, -4765.608f, -6447.495f));
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Mid, 12000, 0);
            float3 caster = new float3(-2776.36914f, 1476.06982f, 1997.00684f);
            float3 landed = MoveGeometry.ResolveAnchor(arc, planet, caster, new float3(4114.003f, 5671.88525f, -2959.14917f));
            Assert.LessOrEqual(GeodesicAtCaster(planet, caster, landed), arc.CastDistanceMm / 1000.0 + CastSlackM(landed));
        }

        [Test]
        public void Q2_near_same_direction_aim_follows_its_great_circle()
        {
            var planet = new PlanetConfig(8000f, float3.zero);
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Mid, 15, 0);
            Assert.AreEqual(15, arc.CastDistanceMm);
            float3 caster = new float3(0f, 15990f, 0f);
            float3 landed = MoveGeometry.ResolveAnchor(arc, planet, caster, new float3(0f, 15990f, 0.0155f));
            Assert.AreEqual(0f, landed.x, 0.001f, "no sideways jump to North");
            Assert.AreEqual(0.015f, landed.z, 0.002f);
        }

        [Test]
        public void Q3_resolved_anchor_on_the_shell_edge_is_accepted_by_overlaps()
        {
            var planet = new PlanetConfig(8000f, float3.zero);
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Mid, 12000, 0);
            float3 landed = MoveGeometry.ResolveAnchor(arc, planet, new float3(4463.85547f, 6257.311f, 2218.11963f),
                new float3(-9735.791f, 11681.6406f, 4975.30225f));
            Assert.IsTrue(MoveGeometry.Overlaps(arc, planet, landed, float3.zero, landed, 0f));
        }

        [Test]
        public void R1_inner_shell_landing_with_a_far_center_is_accepted_by_overlaps()
        {
            var planet = new PlanetConfig(640f, new float3(16000f, 16000f, 16000f));
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Mid, 12000, 0);
            float3 caster = planet.ToWorld(new float3(1f, 0f, 0f), 0f);
            float3 aim = planet.ToWorld(new float3(0f, 1f, 0f), -320f);
            float3 landed = MoveGeometry.ResolveAnchor(arc, planet, caster, aim);
            Assert.IsTrue(MoveGeometry.Overlaps(arc, planet, landed, float3.zero, landed, 0f));
            float3 outerAim = planet.ToWorld(new float3(0f, 1f, 0f), 640f);
            float3 outerLanded = MoveGeometry.ResolveAnchor(arc, planet, caster, outerAim);
            Assert.IsTrue(MoveGeometry.Overlaps(arc, planet, outerLanded, float3.zero, outerLanded, 0f));
        }

        // Fuzz of the ResolveAnchor Post over the whole legal domain: R 640..8000, center components ±16000 m, caster
        // and aim anywhere in the strict shell (a fifth of the radii pinned to the exact edges), random directions.
        [Test]
        public void ResolveAnchor_output_always_composes_with_overlaps_and_meets_its_post()
        {
            var rng = new Pcg32(0x5EEDUL, 11UL);
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Long, 24000, 10000);
            for (int i = 0; i < 20_000; i++)
            {
                float radius = 640f + rng.NextFloat() * 7360f;
                var center = new float3(Signed(ref rng), Signed(ref rng), Signed(ref rng)) * 16000f;
                var planet = new PlanetConfig(radius, center);
                float3 caster = center + Direction(ref rng) * ShellRadius(ref rng, radius);
                float3 aim = center + Direction(ref rng) * ShellRadius(ref rng, radius);
                if (math.lengthsq(caster - center) < 1f)
                {
                    continue;
                }

                float3 landed = MoveGeometry.ResolveAnchor(arc, planet, caster, aim);
                Assert.IsTrue(math.all(math.isfinite(landed)), $"probe {i}");
                Assert.DoesNotThrow(() => MoveGeometry.Overlaps(arc, planet, landed, float3.zero, landed, 0f), $"probe {i}");
                Assert.LessOrEqual(GeodesicAtCaster(planet, caster, landed),
                    arc.CastDistanceMm / 1000.0 + CastSlackM(landed), $"probe {i}");
            }
        }

        private static float Signed(ref Pcg32 rng) => rng.NextFloat() * 2f - 1f;

        private static float3 Direction(ref Pcg32 rng)
        {
            while (true)
            {
                var v = new float3(Signed(ref rng), Signed(ref rng), Signed(ref rng));
                float l2 = math.lengthsq(v);
                if (l2 > 1e-4f && l2 <= 1f)
                {
                    return v / math.sqrt(l2);
                }
            }
        }

        private static float ShellRadius(ref Pcg32 rng, float radius)
        {
            uint pick = rng.NextUInt(10);
            if (pick == 0)
            {
                return radius * 0.5f;
            }

            if (pick == 1)
            {
                return radius * 2f;
            }

            return radius * (0.5f + rng.NextFloat() * 1.5f);
        }

        [Test]
        public void Q4_near_antipodal_aim_outside_the_band_lands_on_its_great_circle()
        {
            var planet = new PlanetConfig(8000f, float3.zero);
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Mid, 12000, 0);
            float3 caster = new float3(2572.16528f, 4776.87842f, 5879.235f);
            float3 landed = MoveGeometry.ResolveAnchor(arc, planet, caster, new float3(-2572.15747f, -4776.878f, -5879.239f));
            var expected = new double3(2582.90879357694, 4777.597988035, 5873.93777181676);
            Assert.Less(math.distance((double3)landed, expected), 0.01, "within 1 cm of the double-precision oracle");
        }

        // ---- r3 P3: far-hemisphere targets never overlap, whatever the reach ----
        [Test]
        public void P3_far_hemisphere_never_overlaps_even_with_a_huge_reach()
        {
            float3 anchor = math.normalize(new float3(1f, 1f, 2f)) * R;
            float3 up = anchor / R;
            float3 forward = PlanetFrame.FromUp(up).North;
            HitVolume huge = Build(MoveShape.Front, RangeClass.Melee, 3_000_000, 0);
            HitVolume hugeBurst = Build(MoveShape.Burst, RangeClass.Melee, 3_000_000, 0);
            Assert.IsFalse(Hits(huge, -anchor, 0f, anchor, forward), "exact antipode");
            Assert.IsFalse(Hits(hugeBurst, -anchor, 0f, anchor, forward), "even a 3 km disc");
            Assert.IsFalse(Hits(hugeBurst, math.normalize(-up + forward * 0.01f) * R, 0f, anchor, forward));
            Assert.IsTrue(Hits(hugeBurst, math.normalize(up + forward * 0.5f) * R, 0f, anchor, forward), "near side still hits");
        }

        // ---- G2 ----
        [Test]
        public void G2_aiming_at_the_antipode_gives_a_finite_clamped_landing_point()
        {
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Mid, 12000, 0);
            float3 landed = MoveGeometry.ResolveAnchor(arc, _planet, _caster, -_caster);
            Assert.IsTrue(math.all(math.isfinite(landed)));
            Assert.AreEqual(R, math.length(landed), 0.01f);
            float3 u = math.normalize(landed);
            float distance = math.atan2(math.length(math.cross(_up, u)), math.dot(_up, u)) * R;
            Assert.AreEqual(arc.CastDistanceMm / 1000f, distance, 0.01f);
            Assert.AreEqual(landed, MoveGeometry.ResolveAnchor(arc, _planet, _caster, -_caster), "deterministic");
        }

        // ---- G3 ----
        [Test]
        // Q-D1 (2026-11-11) lowered the supported radius from 640 m to 320 m (asteroids).
        public void G3_planets_below_the_supported_radius_are_rejected_by_the_geometry()
        {
            HitVolume line = Build(MoveShape.Line, RangeClass.Long, 1_000_000, 0);
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Mid, 12000, 0);
            var tooSmall = new PlanetConfig(319f, float3.zero);
            float3 c319 = new float3(0f, 319f, 0f);
            Assert.Throws<ContractException>(() =>
                MoveGeometry.Overlaps(line, tooSmall, c319, _forward, c319 + _forward, 0f));
            Assert.Throws<ContractException>(() => MoveGeometry.ResolveAnchor(arc, tooSmall, c319, c319));

            var asteroid = new PlanetConfig(320f, float3.zero);
            float3 c320 = new float3(0f, 320f, 0f);
            Assert.That(MoveGeometry.Overlaps(line, asteroid, c320, _forward, c320 + _forward, 0f), Is.True);
            Assert.DoesNotThrow(() => MoveGeometry.ResolveAnchor(arc, asteroid, c320, c320));
            Assert.DoesNotThrow(() => Hits(line, AtXZ(0f, 1f)));
        }

        // ---- G4 ----
        [Test]
        public void G4_height_must_leave_a_positive_half_height()
        {
            Assert.Throws<ContractException>(() => new HitVolumeDefaults(1, 1, 1, 1, 1, 1, 1, 1, 1));
            _d = new HitVolumeDefaults(2, 1, 1, 1, 1, 1, 1, 1, 1);
            Assert.AreEqual(1, Build(MoveShape.Front, RangeClass.Melee, 2000, 0).HalfHeightMm);
            Assert.AreEqual(1, Build(MoveShape.Line, RangeClass.Melee, 2000, 0, MoveMovement.Dash).HalfHeightMm);
        }

        // ---- G5 ----
        [TestCase(1f)]
        [TestCase(0.9995f)]
        [TestCase(1.0005f)]
        public void G5_forward_length_within_tolerance_does_not_change_reach(float length)
        {
            HitVolume line = Build(MoveShape.Line, RangeClass.Long, 24000, 0);
            Assert.AreEqual(24000, line.ReachMm);
            float3 f = _forward * length;
            Assert.IsFalse(Hits(line, AtXZ(0f, 24.005f), 0f, null, f), "beyond reach");
            Assert.IsTrue(Hits(line, AtXZ(0f, 23.995f), 0f, null, f), "inside reach");
        }

        [Test]
        public void G5_slightly_non_tangent_forward_is_projected_before_measuring()
        {
            HitVolume line = Build(MoveShape.Line, RangeClass.Long, 24000, 0);
            float3 tilted = math.normalize(_forward + _up * 0.0009f);
            Assert.IsFalse(Hits(line, AtXZ(0f, 24.005f), 0f, null, tilted));
            Assert.IsTrue(Hits(line, AtXZ(0f, 23.995f), 0f, null, tilted));
        }

        // ---- G6 ----
        [Test]
        public void G6_coordinates_outside_the_domain_are_contract_violations()
        {
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Mid, 12000, 0);
            HitVolume burst = Build(MoveShape.Burst, RangeClass.Melee, 2000, 0);
            var huge = new float3(1e20f, 0f, 0f);
            var nan = new float3(float.NaN, 0f, 0f);
            Assert.Throws<ContractException>(() => MoveGeometry.ResolveAnchor(arc, _planet, huge, huge));
            Assert.Throws<ContractException>(() => MoveGeometry.ResolveAnchor(arc, _planet, _caster, nan));
            Assert.Throws<ContractException>(() => Hits(burst, huge));
            Assert.Throws<ContractException>(() => Hits(burst, AtXZ(0f, 1f), 0f, nan));
            var edge = new float3(PlanetFrame.MaxVectorComponent, 0f, 0f);
            Assert.Throws<ContractException>(() => Hits(burst, edge), "inside the component bound but outside the shell");
        }

        // ---- G7 ----
        [Test]
        public void G7_rect_uses_half_the_width_and_the_target_radius()
        {
            HitVolume front = Build(MoveShape.Front, RangeClass.Melee, 2000, 0);
            float half = front.WidthMm / 2000f;
            Assert.IsFalse(Hits(front, AtXZ(half + 0.25f, 1f)), "outside the half width");
            Assert.IsTrue(Hits(front, AtXZ(half - 0.01f, 1f)));
            Assert.IsTrue(Hits(front, AtXZ(half + 0.05f, 1f), 0.1f), "target radius reaches inside");
            Assert.IsFalse(Hits(front, AtXZ(half + 0.05f, 1f), 0.02f));
        }

        [Test]
        public void G7_circle_is_widened_by_the_target_radius()
        {
            HitVolume burst = Build(MoveShape.Burst, RangeClass.Melee, 2000, 0);
            Assert.AreEqual(2000, burst.ReachMm);
            Assert.IsFalse(Hits(burst, AtXZ(2.05f, 0f)));
            Assert.IsTrue(Hits(burst, AtXZ(2.05f, 0f), 0.1f));
        }

        [Test]
        public void G7_landing_point_keeps_the_aim_points_radius()
        {
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Mid, 12000, 0);
            float3 landed = MoveGeometry.ResolveAnchor(arc, _planet, _caster, AtXZ(0f, 30f, 5f));
            Assert.AreEqual(R + 5f, math.length(landed), 0.01f);
        }

        [Test]
        public void G7_dash_height_is_never_str_scaled()
        {
            HitVolume dash = Build(MoveShape.Line, RangeClass.Melee, 5000, 100, MoveMovement.Dash);
            Assert.AreEqual(_d.HeightMm / 2, dash.HalfHeightMm);
            Assert.IsFalse(Hits(dash, AtXZ(0f, 1f, _d.HeightMm / 2000f + 0.1f)));
        }

        [Test]
        public void G7_melee_field_radius_scales_and_long_target_burst_uses_long_cast()
        {
            int lin = _stats.MeleeLinearPermille(100);
            Assert.That(lin, Is.GreaterThan(1000));
            HitVolume field = Build(MoveShape.Field, RangeClass.Melee, 12000, 100);
            Assert.AreEqual((int)((long)_d.FieldRadiusMm * lin / 1000), field.ReachMm);
            HitVolume longBurst = Build(MoveShape.Burst, RangeClass.Long, 3000, 0, origin: BurstOrigin.Target);
            Assert.AreEqual(_stats.RangedDistanceMm(_d.TargetCastLongMm, 0), longBurst.CastDistanceMm);
            Assert.AreNotEqual(_stats.RangedDistanceMm(_d.TargetCastMidMm, 0), longBurst.CastDistanceMm);
        }

        // ---- H1 ----
        private static void AssertSameStream(Pcg32 a, Pcg32 b, string because)
        {
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(b.NextUInt(), a.NextUInt(), because);
            }
        }

        [TestCase(1000)]
        [TestCase(500)]
        [TestCase(1)]
        public void H1_resolve_consumes_exactly_one_bounded_draw(int chance)
        {
            var rng = new Pcg32(99UL, 5UL);
            var twin = new Pcg32(99UL, 5UL);
            HitResolver.Resolve(true, false, chance, ref rng);
            twin.NextUInt(DerivedStats.Permille);
            AssertSameStream(rng, twin, $"chance {chance}");
        }

        [Test]
        public void H1_draw_equal_to_the_chance_is_a_miss()
        {
            const int chance = 500;
            for (ulong seed = 0; seed < 100_000; seed++)
            {
                var probe = new Pcg32(seed, 5UL);
                if (probe.NextUInt(DerivedStats.Permille) != chance)
                {
                    continue;
                }

                var atChance = new Pcg32(seed, 5UL);
                var above = new Pcg32(seed, 5UL);
                Assert.AreEqual(HitOutcome.Miss, HitResolver.Resolve(true, false, chance, ref atChance));
                Assert.AreEqual(HitOutcome.Hit, HitResolver.Resolve(true, false, chance + 1, ref above));
                return;
            }

            Assert.Fail("no seed drew exactly the chance value");
        }

        // ---- D1 ----
        [TestCase("stat_effective_cap", 1, 1_000_000)]
        [TestCase("hp.base", 1, 1_000_000)]
        [TestCase("hp.gain", 0, 1_000_000)]
        [TestCase("hp.half_stat", 1, 1_000_000)]
        [TestCase("melee_volume.gain_permille", 0, 1000)]
        [TestCase("melee_volume.half_stat", 1, 1_000_000)]
        [TestCase("ranged_distance.gain_permille", 0, 1000)]
        [TestCase("ranged_distance.half_stat", 1, 1_000_000)]
        [TestCase("ranged_distance.cap_mm", 1, 1_000_000)]
        [TestCase("cd.min_permille", 1, 1000)]
        [TestCase("cd.half_stat", 1, 1_000_000)]
        [TestCase("cd.floor_ms", 1, 1_000_000)]
        [TestCase("speed.base_mm_s", 1, 1_000_000)]
        [TestCase("speed.gain_mm_s", 0, 1_000_000)]
        [TestCase("speed.half_stat", 1, 1_000_000)]
        [TestCase("evasion.cap_permille", 0, 999)]
        [TestCase("evasion.half_stat", 1, 1_000_000)]
        [TestCase("hit.floor_permille", 1, 1000)]
        public void D1_each_coefficient_accepts_its_envelope_ends_and_rejects_one_past(string path, int min, int max)
        {
            Assert.DoesNotThrow(() => FromShippedWith(path, min), "min");
            Assert.DoesNotThrow(() => FromShippedWith(path, max), "max");
            Assert.Throws<ContractException>(() => FromShippedWith(path, min - 1), "min - 1");
            Assert.Throws<ContractException>(() => FromShippedWith(path, max + 1), "max + 1");
        }

        [Test]
        public void D1_curve_multiplies_in_64_bits()
        {
            JObject b = BalanceFixture.BalanceJson();
            b.SelectToken("stat_effective_cap").Replace(1_000_000);
            b.SelectToken("hp.base").Replace(1_000_000);
            b.SelectToken("hp.gain").Replace(1_000_000);
            b.SelectToken("hp.half_stat").Replace(1);
            var m = new DerivedStats(BalanceFixture.FromJson(b));
            // 1e6 + floor(1e6 * 1e6 / (1e6 + 1)); the product overflows Int32 by ~465x.
            Assert.AreEqual(1_999_999, m.MaxHp(1_000_000));
        }

        private static DerivedStatsCoefficients FromShippedWith(string path, int value)
        {
            JObject b = BalanceFixture.BalanceJson();
            b.SelectToken(path).Replace(value);
            return BalanceFixture.FromJson(b);
        }
    }
}
