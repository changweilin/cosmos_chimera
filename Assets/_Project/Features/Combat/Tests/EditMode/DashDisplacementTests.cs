using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Combat.Tests
{
    public sealed class DashDisplacementTests
    {
        private const float R = 640f;
        private DerivedStats _stats;
        private HitVolumeDefaults _d;
        private PlanetConfig _planet;
        private float3 _up;
        private float3 _caster;
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

        private HitVolume Dash(int baseMm, int str = 0)
        {
            return MoveGeometry.Build(MoveShape.Line, MoveMovement.Dash, RangeClass.Melee, default, baseMm, str, _stats, _d);
        }

        private static double GeodesicM(in PlanetConfig planet, float3 a, float3 b)
        {
            double3 da = (double3)a - (double3)planet.Center;
            double3 db = (double3)b - (double3)planet.Center;
            return math.atan2(math.length(math.cross(da, db)), math.dot(da, db)) * math.length(da);
        }

        private static double PostSlackM(float3 result)
        {
            return 0.001 + 1e-6 * math.cmax(math.abs((double3)result));
        }

        [Test]
        public void Dash_end_is_base_range_away_along_forward_at_the_caster_radius()
        {
            HitVolume v = Dash(5000);
            float3 end = MoveGeometry.ResolveDashEnd(v, _planet, _caster, _forward);

            Assert.AreEqual(5.0, GeodesicM(_planet, _caster, end), PostSlackM(end));
            Assert.AreEqual(R, math.length((double3)end - (double3)_planet.Center), PostSlackM(end));
            float3 right = math.cross(_up, _forward);
            Assert.AreEqual(0.0, math.dot((double3)end - (double3)_planet.Center, (double3)right), PostSlackM(end));
            Assert.Greater(math.dot(end - _caster, _forward), 0f);
        }

        [Test]
        public void Dash_end_does_not_scale_with_str()
        {
            float3 weak = MoveGeometry.ResolveDashEnd(Dash(5000, 0), _planet, _caster, _forward);
            float3 strong = MoveGeometry.ResolveDashEnd(Dash(5000, 10000), _planet, _caster, _forward);
            Assert.AreEqual(weak, strong);
        }

        [Test]
        public void Dash_end_sits_on_the_far_edge_of_its_own_rect()
        {
            HitVolume v = Dash(8000);
            float3 end = MoveGeometry.ResolveDashEnd(v, _planet, _caster, _forward);
            Assert.IsTrue(MoveGeometry.Overlaps(v, _planet, _caster, _forward, end, 0.002f));

            HitVolume shorter = Dash(7990);
            Assert.IsFalse(MoveGeometry.Overlaps(shorter, _planet, _caster, _forward, end, 0f));
        }

        [Test]
        public void Forward_length_within_tolerance_does_not_change_the_end()
        {
            HitVolume v = Dash(24000);
            float3 unit = MoveGeometry.ResolveDashEnd(v, _planet, _caster, _forward);
            float3 shortF = MoveGeometry.ResolveDashEnd(v, _planet, _caster, _forward * 0.9995f);
            float3 longF = MoveGeometry.ResolveDashEnd(v, _planet, _caster, _forward * 1.0005f);
            Assert.AreEqual(0.0, GeodesicM(_planet, unit, shortF), PostSlackM(unit));
            Assert.AreEqual(0.0, GeodesicM(_planet, unit, longF), PostSlackM(unit));
        }

        [Test]
        public void Random_probes_meet_the_post_and_compose_with_overlaps()
        {
            HitVolume burst = MoveGeometry.Build(MoveShape.Burst, MoveMovement.None, RangeClass.Melee, BurstOrigin.Caster,
                1000, 0, _stats, _d);
            var rng = new Pcg32(0xDA5Eul, 7ul);
            for (int i = 0; i < 20000; i++)
            {
                float radius = 640f + rng.NextUInt(7361);
                var center = new float3(Signed(ref rng, 16000), Signed(ref rng, 16000), Signed(ref rng, 16000));
                var planet = new PlanetConfig(radius, center);
                float3 up = math.normalize(new float3(Signed(ref rng, 1000) + 0.5f, Signed(ref rng, 1000), Signed(ref rng, 1000)));
                // Caster radius across the whole shell, ends included.
                float casterRadius = radius * (0.5f + 1.5f * rng.NextUInt(1001) / 1000f);
                float3 caster = center + up * casterRadius;
                float3 seed = math.abs(up.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f);
                float3 forward = math.normalize(math.cross(up, seed));
                int dashMm = 1 + (int)rng.NextUInt(60000);
                HitVolume v = Dash(dashMm);

                float3 end = MoveGeometry.ResolveDashEnd(v, planet, caster, forward);

                Assert.IsTrue(math.all(math.isfinite(end)), "finite at probe " + i);
                double rc = math.length((double3)caster - (double3)center);
                double3 de = (double3)end - (double3)center;
                double3 dc = (double3)caster - (double3)center;
                double distance = math.atan2(math.length(math.cross(dc, de)), math.dot(dc, de)) * rc;
                Assert.AreEqual(dashMm * 0.001, distance, PostSlackM(end), "distance at probe " + i);
                // Composes: the end is a legal Overlaps anchor/target (would throw ContractException otherwise).
                Assert.IsTrue(MoveGeometry.Overlaps(burst, planet, end, forward, end, 0f), "composes at probe " + i);
            }
        }

        private static float Signed(ref Pcg32 rng, uint max)
        {
            return (int)rng.NextUInt(2 * max + 1) - (int)max;
        }

        [Test]
        public void Caster_on_the_shell_edge_keeps_the_end_in_the_shell()
        {
            var planet = new PlanetConfig(R, new float3(16000f, -16000f, 16000f));
            foreach (float k in new[] { 0.5f, 2.0f })
            {
                float3 caster = planet.Center + _up * (R * k);
                float3 end = MoveGeometry.ResolveDashEnd(Dash(30000), planet, caster, _forward);
                double r = math.length((double3)end - (double3)planet.Center);
                Assert.GreaterOrEqual(r, R * 0.5 - 1e-6 * (16000 + 2 * R));
                Assert.LessOrEqual(r, R * 2.0 + 1e-6 * (16000 + 2 * R));
            }
        }

        [Test]
        public void Pre_violations_throw()
        {
            HitVolume dash = Dash(5000);
            HitVolume front = MoveGeometry.Build(MoveShape.Front, MoveMovement.None, RangeClass.Melee, default, 2000, 0, _stats, _d);
            Assert.Throws<ContractException>(() => MoveGeometry.ResolveDashEnd(front, _planet, _caster, _forward));
            Assert.Throws<ContractException>(() => MoveGeometry.ResolveDashEnd(default, _planet, _caster, _forward));

            var small = new PlanetConfig(320f, _planet.Center);
            Assert.Throws<ContractException>(() =>
                MoveGeometry.ResolveDashEnd(dash, small, small.Center + _up * 320f, _forward));
            Assert.Throws<ContractException>(() => MoveGeometry.ResolveDashEnd(dash, default, _caster, _forward));

            Assert.Throws<ContractException>(() =>
                MoveGeometry.ResolveDashEnd(dash, _planet, _planet.Center + _up * (R * 0.49f), _forward));
            Assert.Throws<ContractException>(() =>
                MoveGeometry.ResolveDashEnd(dash, _planet, _planet.Center + _up * (R * 2.01f), _forward));

            Assert.Throws<ContractException>(() => MoveGeometry.ResolveDashEnd(dash, _planet, _caster, _forward * 0.998f));
            Assert.Throws<ContractException>(() =>
                MoveGeometry.ResolveDashEnd(dash, _planet, _caster, math.normalize(_forward + _up * 0.01f)));
            Assert.Throws<ContractException>(() => MoveGeometry.ResolveDashEnd(dash, _planet, _caster, float3.zero));
        }
    }
}
