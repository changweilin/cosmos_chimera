using System.IO;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Combat.Tests
{
    // REQ: tech-debt D0-E4 · docs/reviews/D0-E4-proposal-sol.md (acceptance items 1–2).
    public sealed class TangentTowardTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;
        }

        private CombatBalance _balance;

        [SetUp]
        public void SetUp()
        {
            _balance = CombatBalance.FromJson(File.ReadAllText(BalanceFixture.RepoPath("Data", "balance.json")));
        }

        private Encounter At(float radius, float3 center) =>
            new Encounter(new PlanetConfig(radius, center), _balance, new FlatSurface(), 1);

        private static void AssertTangentUnit(float3 forward, float3 from, float3 center)
        {
            double3 up = math.normalize((double3)from - (double3)center);
            Assert.That(math.all(math.isfinite(forward)), Is.True, "finite");
            Assert.That(math.abs(math.length(forward) - 1f), Is.LessThan(1e-5f), "unit");
            Assert.That(math.abs(math.dot((double3)forward, up)), Is.LessThan(1e-5), "tangent to the true up");
        }

        // Witnesses from SOL P2-12 r5 R5-07 and the D0-E4 proposal: the old float projection gave |dot(f, up)| ≈ 0.0135.
        [TestCase(413.63202f, 96.05307f, -479.63602f, 414.2777f, 96.20301f, -480.38474f)]
        [TestCase(392.2076f, 506.4875f, 3.457169f, 391.59537f, 505.69684f, 3.4517725f)]
        public void Near_vertical_target_still_gives_a_tangent_unit_forward(
            float fx, float fy, float fz, float tx, float ty, float tz)
        {
            Encounter e = At(640f, float3.zero);
            var from = new float3(fx, fy, fz);
            float3 fallback = PlanetFrame.FromUp(math.normalize(from)).North;
            float3 forward = e.TangentToward(from, new float3(tx, ty, tz), fallback);
            AssertTangentUnit(forward, from, float3.zero);
        }

        [TestCase(640f)]
        [TestCase(1280f)]
        public void A_target_straight_overhead_keeps_the_carried_heading(float radius)
        {
            Encounter e = At(radius, float3.zero);
            float3 up = math.normalize(new float3(0.3f, 0.8f, -0.5f));
            PlanetFrame frame = PlanetFrame.FromUp(up);
            float3 from = up * (radius + 1f);
            float3 forward = e.TangentToward(from, up * (radius + 11f), frame.East);
            AssertTangentUnit(forward, from, float3.zero);
            Assert.That(math.dot(forward, frame.East), Is.GreaterThan(0.9999f), "no bearing → heading kept");
        }

        [Test]
        public void A_radial_or_zero_fallback_with_no_bearing_falls_back_to_north()
        {
            Encounter e = At(640f, float3.zero);
            float3 up = math.normalize(new float3(-0.2f, 0.1f, 0.97f));
            float3 from = up * 641f;
            float3 north = PlanetFrame.FromUp(up).North;
            Assert.That(math.dot(e.TangentToward(from, from, float3.zero), north), Is.GreaterThan(0.9999f));
            Assert.That(math.dot(e.TangentToward(from, from, up), north), Is.GreaterThan(0.9999f));
        }

        // Above tau (~0.6 mm at R=640, tens of mm with a 16 km centre) the target bearing wins.
        [TestCase(640f, 0f, 0f, 0f, 0.01f)]
        [TestCase(640f, 0f, 0f, 0f, 6f)]
        [TestCase(1280f, 0f, 0f, 0f, 0.1f)]
        [TestCase(640f, 16000f, -16000f, 8000f, 0.1f)]
        [TestCase(1280f, 16000f, -16000f, 8000f, 6f)]
        public void A_target_above_the_uncertainty_is_faced(float radius, float cx, float cy, float cz, float lateral)
        {
            var center = new float3(cx, cy, cz);
            Encounter e = At(radius, center);
            float3 up = math.normalize(new float3(0.6f, -0.3f, 0.74f));
            PlanetFrame frame = PlanetFrame.FromUp(up);
            float3 from = center + up * (radius + 1f);
            float3 to = center + up * (radius + 2f) + frame.East * lateral;
            float3 forward = e.TangentToward(from, to, frame.North);
            AssertTangentUnit(forward, from, center);
            Assert.That(math.dot(forward, frame.East), Is.GreaterThan(0.999f), "faces the target");
        }
    }
}
