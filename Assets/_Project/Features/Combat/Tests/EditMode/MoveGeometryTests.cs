using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Combat.Tests
{
    // Caster stands on the surface of an R = 640 m planet; targets are placed by geodesic distance + heading + altitude,
    // which is the coordinate system MoveGeometry promises to measure in.
    public sealed class MoveGeometryTests
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
            _planet = new PlanetConfig(R, new float3(10f, -20f, 30f));
            _up = math.normalize(new float3(0.3f, 0.8f, -0.5f));
            _caster = _planet.Center + _up * R;
            _forward = math.normalize(math.cross(_up, new float3(1f, 0f, 0f)));
        }

        private float3 At(float distanceM, float headingDeg, float altitudeM = 0f)
        {
            float3 right = math.cross(_up, _forward);
            float h = math.radians(headingDeg);
            float3 planar = math.cos(h) * _forward + math.sin(h) * right;
            float a = distanceM / R;
            return _planet.Center + (math.cos(a) * _up + math.sin(a) * planar) * (R + altitudeM);
        }

        private HitVolume Build(MoveShape shape, RangeClass rc, int baseMm, int str,
            MoveMovement movement = MoveMovement.None, BurstOrigin origin = BurstOrigin.Caster)
        {
            return MoveGeometry.Build(shape, movement, rc, origin, baseMm, str, _stats, _d);
        }

        private bool Hits(in HitVolume v, float3 target, float radius = 0f, float3? anchor = null)
        {
            return MoveGeometry.Overlaps(v, _planet, anchor ?? _caster, _forward, target, radius);
        }

        private static readonly int[] Strengths = { 0, 25, 100, 200, 1000, 10000 };

        [Test]
        public void Melee_front_scales_every_dimension_by_the_linear_factor()
        {
            foreach (int str in Strengths)
            {
                HitVolume v = Build(MoveShape.Front, RangeClass.Melee, 2000, str);
                int lin = _stats.MeleeLinearPermille(str);
                Assert.AreEqual(2000L * lin / 1000, v.ReachMm, $"str {str}");
                Assert.AreEqual((long)_d.FrontWidthMm * lin / 1000, v.WidthMm, $"str {str}");
                Assert.AreEqual((int)((long)_d.HeightMm * lin / 1000) / 2, v.HalfHeightMm, $"str {str}");

                float reach = v.ReachMm / 1000f;
                float half = v.WidthMm / 2000f;
                Assert.IsTrue(Hits(v, At(reach - 0.002f, 0f)), $"inside reach, str {str}");
                Assert.IsFalse(Hits(v, At(reach + 0.003f, 0f)), $"beyond reach, str {str}");
                float side = math.degrees(math.atan2(half - 0.002f, 1f));
                Assert.IsTrue(Hits(v, At(math.sqrt(1f + (half - 0.002f) * (half - 0.002f)), side)), $"inside width, str {str}");
                Assert.IsFalse(Hits(v, At(-0.01f, 0f)), "behind the caster");
            }
        }

        [Test]
        public void Ranged_line_extends_reach_by_g_but_keeps_width()
        {
            foreach (int str in Strengths)
            {
                HitVolume v = Build(MoveShape.Line, RangeClass.Long, 24000, str);
                Assert.AreEqual(_stats.RangedDistanceMm(24000, str), v.ReachMm, $"str {str}");
                Assert.AreEqual(_d.LineWidthMm, v.WidthMm);
                Assert.AreEqual(_d.HeightMm / 2, v.HalfHeightMm);
                Assert.IsTrue(Hits(v, At(v.ReachMm / 1000f - 0.005f, 0f)), $"str {str}");
                Assert.IsFalse(Hits(v, At(v.ReachMm / 1000f + 0.01f, 0f)), $"str {str}");
            }
        }

        [Test]
        public void Level_target_at_capped_range_hits_despite_curvature()
        {
            // 35.9 m on R = 640 m sits ~1.007 m below the caster's tangent plane — more than the 1 m half height,
            // so a flat-plane test would miss a target standing on the same ground.
            HitVolume v = Build(MoveShape.Line, RangeClass.Long, 1_000_000, 10000);
            Assert.AreEqual(_stats.Coefficients.RangedCapMm, v.ReachMm);
            Assert.IsTrue(Hits(v, At(35.9f, 0f)));
            Assert.IsFalse(Hits(v, At(20f, 0f, v.HalfHeightMm / 1000f + 0.05f)), "radially too high");
            Assert.IsTrue(Hits(v, At(20f, 0f, v.HalfHeightMm / 1000f + 0.05f), 0.1f), "target radius closes the gap");
        }

        [Test]
        public void Cone_uses_its_half_angle_and_target_radius_reaches_past_the_edges()
        {
            HitVolume v = Build(MoveShape.Cone, RangeClass.Mid, 12000, 0);
            Assert.AreEqual(_d.ConeAngleDeg * 5, v.HalfAngleDeciDeg);
            float half = _d.ConeAngleDeg / 2f;
            Assert.IsTrue(Hits(v, At(8f, half - 0.5f)));
            Assert.IsFalse(Hits(v, At(8f, half + 0.5f)));
            float gap = 8f * math.sin(math.radians(0.5f));
            Assert.IsTrue(Hits(v, At(8f, half + 0.5f), gap + 0.01f));
            Assert.IsFalse(Hits(v, At(8f, -(half + 0.5f)), gap - 0.01f));
            Assert.IsTrue(Hits(v, At(0.05f, 180f), 0.1f), "a target overlapping the apex is hit");
        }

        [Test]
        public void Sweep_covers_the_front_half_plane_and_grows_with_str()
        {
            HitVolume weak = Build(MoveShape.Sweep, RangeClass.Melee, 2000, 0);
            HitVolume strong = Build(MoveShape.Sweep, RangeClass.Melee, 2000, 200);
            Assert.AreEqual(_d.SweepAngleDeg * 5, weak.HalfAngleDeciDeg);
            Assert.That(strong.ReachMm, Is.GreaterThan(weak.ReachMm));
            Assert.IsTrue(Hits(weak, At(1.5f, 89f)));
            Assert.IsFalse(Hits(weak, At(1.5f, 100f)));
            float between = (weak.ReachMm + strong.ReachMm) / 2000f;
            Assert.IsFalse(Hits(weak, At(between, 0f)));
            Assert.IsTrue(Hits(strong, At(between, 0f)));
        }

        [Test]
        public void Target_burst_keeps_its_radius_and_clamps_the_aim_to_g_scaled_cast_distance()
        {
            HitVolume weak = Build(MoveShape.Burst, RangeClass.Mid, 3000, 0, origin: BurstOrigin.Target);
            HitVolume strong = Build(MoveShape.Burst, RangeClass.Mid, 3000, 200, origin: BurstOrigin.Target);
            Assert.AreEqual(HitVolumeAnchor.AimPoint, weak.Anchor);
            Assert.AreEqual(3000, weak.ReachMm);
            Assert.AreEqual(3000, strong.ReachMm, "AoE radius never STR-scaled for ranged moves");
            Assert.AreEqual(_stats.RangedDistanceMm(_d.TargetCastMidMm, 0), weak.CastDistanceMm);
            Assert.AreEqual(_stats.RangedDistanceMm(_d.TargetCastMidMm, 200), strong.CastDistanceMm);

            float3 near = At(5f, 30f);
            Assert.AreEqual(near, MoveGeometry.ResolveAnchor(weak, _planet, _caster, near));

            float3 far = At(30f, 30f);
            float3 anchor = MoveGeometry.ResolveAnchor(weak, _planet, _caster, far);
            float landed = math.atan2(math.length(math.cross(_up, math.normalize(anchor - _planet.Center))),
                math.dot(_up, math.normalize(anchor - _planet.Center))) * R;
            Assert.AreEqual(weak.CastDistanceMm / 1000f, landed, 0.01f);
            Assert.AreEqual(R, math.length(anchor - _planet.Center), 0.01f, "keeps the aim point's radius");

            Assert.IsTrue(Hits(weak, At(14f, 30f), 0f, anchor), "inside the landed disc");
            Assert.IsFalse(Hits(weak, far, 0f, anchor), "the unclamped aim point is outside");
        }

        [Test]
        public void Caster_burst_is_centered_on_the_caster_and_scales_when_melee()
        {
            HitVolume v = Build(MoveShape.Burst, RangeClass.Melee, 2000, 100);
            Assert.AreEqual(HitVolumeAnchor.Caster, v.Anchor);
            Assert.AreEqual(2000L * _stats.MeleeLinearPermille(100) / 1000, v.ReachMm);
            Assert.IsTrue(Hits(v, At(v.ReachMm / 1000f - 0.01f, 170f)));
            Assert.IsFalse(Hits(v, At(v.ReachMm / 1000f + 0.01f, 170f)));
        }

        [Test]
        public void Dash_path_and_displacement_equal_base_range_at_every_str()
        {
            foreach (int str in Strengths)
            {
                HitVolume v = Build(MoveShape.Line, RangeClass.Melee, 5000, str, MoveMovement.Dash);
                Assert.AreEqual(5000, v.DashDistanceMm);
                Assert.AreEqual(5000, v.ReachMm);
                Assert.AreEqual(_d.LineWidthMm, v.WidthMm);
            }

            Assert.AreEqual(0, Build(MoveShape.Line, RangeClass.Mid, 5000, 0).DashDistanceMm);
        }

        [Test]
        public void Ally_requires_the_teammate_within_cast_distance_ignoring_height()
        {
            HitVolume v = Build(MoveShape.Ally, RangeClass.Mid, 12000, 0);
            Assert.AreEqual(HitVolumeKind.Ally, v.Kind);
            Assert.AreEqual(12000, v.CastDistanceMm);
            Assert.IsTrue(Hits(v, At(11.9f, 120f, 5f)));
            Assert.IsFalse(Hits(v, At(12.1f, 120f)));
            Assert.AreEqual(HitVolumeKind.Caster, Build(MoveShape.Self, RangeClass.Melee, 0, 0).Kind);
        }

        [Test]
        public void Arc_and_field_land_at_the_aim_point()
        {
            HitVolume arc = Build(MoveShape.Arc, RangeClass.Long, 24000, 0);
            HitVolume field = Build(MoveShape.Field, RangeClass.Mid, 12000, 0);
            Assert.AreEqual(HitVolumeAnchor.AimPoint, arc.Anchor);
            Assert.AreEqual(_d.ArcLandingRadiusMm, arc.ReachMm);
            Assert.AreEqual(24000, arc.CastDistanceMm);
            Assert.AreEqual(_d.FieldRadiusMm, field.ReachMm);
            Assert.AreEqual(12000, field.CastDistanceMm);
        }

        [Test]
        public void Every_dimension_is_non_decreasing_in_str()
        {
            var cases = new (MoveShape shape, RangeClass rc, BurstOrigin origin)[]
            {
                (MoveShape.Front, RangeClass.Melee, BurstOrigin.Caster), (MoveShape.Sweep, RangeClass.Melee, BurstOrigin.Caster),
                (MoveShape.Burst, RangeClass.Melee, BurstOrigin.Caster), (MoveShape.Burst, RangeClass.Long, BurstOrigin.Target),
                (MoveShape.Line, RangeClass.Mid, BurstOrigin.Caster), (MoveShape.Cone, RangeClass.Long, BurstOrigin.Caster),
                (MoveShape.Arc, RangeClass.Mid, BurstOrigin.Caster), (MoveShape.Field, RangeClass.Mid, BurstOrigin.Caster),
                (MoveShape.Ally, RangeClass.Mid, BurstOrigin.Caster),
            };
            foreach (var c in cases)
            {
                HitVolume prev = Build(c.shape, c.rc, 12000, 0, origin: c.origin);
                for (int str = 1; str <= 2000; str += 7)
                {
                    HitVolume v = Build(c.shape, c.rc, 12000, str, origin: c.origin);
                    Assert.That(v.ReachMm >= prev.ReachMm && v.WidthMm >= prev.WidthMm && v.HalfHeightMm >= prev.HalfHeightMm
                        && v.CastDistanceMm >= prev.CastDistanceMm && v.HalfAngleDeciDeg == prev.HalfAngleDeciDeg,
                        $"{c.shape}/{c.rc} at str {str}");
                    prev = v;
                }
            }
        }

        [Test]
        public void Preconditions_are_enforced()
        {
            Assert.Throws<ContractException>(() => Build(MoveShape.Burst, RangeClass.Melee, 2000, 0, origin: BurstOrigin.Target));
            Assert.Throws<ContractException>(() => Build(MoveShape.Front, RangeClass.Melee, 2000, 0, MoveMovement.Dash));
            Assert.Throws<ContractException>(() => Build(MoveShape.Line, RangeClass.Melee, 0, 0, MoveMovement.Dash));
            Assert.Throws<ContractException>(() => Build(MoveShape.Front, RangeClass.Melee, -1, 0));
            Assert.Throws<ContractException>(() => Build(MoveShape.Front, RangeClass.Melee, 2000, -1));
            Assert.Throws<ContractException>(() => MoveGeometry.Build(MoveShape.Front, MoveMovement.None, RangeClass.Melee,
                BurstOrigin.Caster, 2000, 0, null, _d));

            HitVolume self = Build(MoveShape.Self, RangeClass.Melee, 0, 0);
            Assert.Throws<ContractException>(() => Hits(self, At(1f, 0f)));
            HitVolume front = Build(MoveShape.Front, RangeClass.Melee, 2000, 0);
            Assert.Throws<ContractException>(() => MoveGeometry.Overlaps(front, _planet, _caster, _up, At(1f, 0f), 0f));
            Assert.Throws<ContractException>(() => MoveGeometry.Overlaps(front, _planet, _caster, _forward * 2f, At(1f, 0f), 0f));
            Assert.Throws<ContractException>(() => Hits(front, At(1f, 0f), -0.1f));
            Assert.Throws<ContractException>(() => Hits(front, _planet.Center));
            Assert.Throws<ContractException>(() => MoveGeometry.ResolveAnchor(front, _planet, _caster, At(1f, 0f)));
            Assert.Throws<ContractException>(() => MoveGeometry.Overlaps(front, default, _caster, _forward, At(1f, 0f), 0f));
        }

        [Test]
        public void Hit_volume_defaults_reject_out_of_range_values()
        {
            Assert.Throws<ContractException>(() => new HitVolumeDefaults(0, 1, 1, 1, 1, 1, 1, 1, 1));
            Assert.Throws<ContractException>(() => new HitVolumeDefaults(2, 1, 361, 1, 1, 1, 1, 1, 1));
            Assert.Throws<ContractException>(() => new HitVolumeDefaults(2, 1, 1, 0, 1, 1, 1, 1, 1));
            Assert.Throws<ContractException>(() => new HitVolumeDefaults(2, 1, 1, 1, 1, 1, 1, 1, 1_000_001));
        }
    }
}
