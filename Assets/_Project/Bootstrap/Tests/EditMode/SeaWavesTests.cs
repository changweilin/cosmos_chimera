using Cosmimera.Bootstrap.Demo;
using Cosmimera.Core.Contracts;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Bootstrap.Tests
{
    // planet-world.md#海浪: swells follow the wind, height grows with wind², and a turning wind never makes the sea jump.
    public sealed class SeaWavesTests
    {
        private static readonly float3 Up = new float3(0f, 1f, 0f);
        private static readonly float3 East = new float3(1f, 0f, 0f);
        private static readonly float3 North = new float3(0f, 0f, 1f);
        private static readonly float3 Anchor = new float3(0f, 640f, 0f);

        [Test]
        public void Wave_height_grows_with_the_square_of_the_wind()
        {
            float calm = SeaWaves.HeightM(0f);
            float half = SeaWaves.HeightM(0.5f) - calm;
            float full = SeaWaves.HeightM(1f) - calm;
            Assert.That(full / half, Is.EqualTo(4f).Within(1e-3f));
            Assert.That(SeaWaves.PeakLengthM(1f), Is.GreaterThan(SeaWaves.PeakLengthM(0.3f)));
        }

        [Test]
        public void Crests_travel_downwind_at_deep_water_speed()
        {
            var sea = new SeaWaves();
            sea.Step(Anchor, Up, East, 0.6f, 0f, 0f);
            float speed = math.sqrt(9.81f * SeaWaves.PeakLengthM(0.6f) / (2f * math.PI));
            var before = new float[20];
            for (int i = 0; i < before.Length; i++) sea.Crest(Anchor + East * i, out before[i]);
            sea.Step(Anchor, Up, East, 0.6f, 0f, 0.25f);
            // What was at x is now at x + c·dt, downwind.
            for (int i = 0; i < before.Length; i++)
            {
                sea.Crest(Anchor + East * (i + speed * 0.25f), out float now);
                Assert.That(now, Is.EqualTo(before[i]).Within(1e-3f));
            }
        }

        [Test]
        public void A_turning_wind_keeps_the_sea_continuous_at_the_anchor()
        {
            var sea = new SeaWaves();
            sea.Step(Anchor, Up, East, 0.7f, 0.3f, 0f);
            float3 point = Anchor + East * 0.5f;
            float h0 = sea.HeightAt(point);
            float3 turned = math.normalize(East + North * 0.02f);
            sea.Step(Anchor, Up, turned, 0.7f, 0.3f, 0f);
            Assert.That(sea.HeightAt(point), Is.EqualTo(h0).Within(0.02f));
        }

        [Test]
        public void Moving_the_anchor_without_turning_changes_nothing()
        {
            var sea = new SeaWaves();
            sea.Step(Anchor, Up, East, 0.5f, 0f, 0f);
            float3 far = Anchor + new float3(37f, 0f, -12f);
            float h0 = sea.HeightAt(far);
            sea.Step(Anchor + new float3(9f, 0f, 4f), Up, East, 0.5f, 0f, 0f);
            Assert.That(sea.HeightAt(far), Is.EqualTo(h0).Within(1e-3f));
        }

        [Test]
        public void Height_never_exceeds_the_amplitude_sum()
        {
            var sea = new SeaWaves();
            sea.Step(Anchor, Up, North, 1f, 1f, 3.7f);
            for (int i = 0; i < 200; i++)
            {
                float h = sea.HeightAt(Anchor + new float3(i * 0.7f, 0f, i * 0.3f));
                Assert.That(math.abs(h), Is.LessThanOrEqualTo(sea.MaxAmplitudeM + 1e-4f));
            }
        }

        [Test]
        public void Step_rejects_out_of_range_wind()
        {
            var sea = new SeaWaves();
            Assert.Throws<ContractException>(() => sea.Step(Anchor, Up, East, 1.5f, 0f, 0f));
            Assert.Throws<ContractException>(() => sea.Step(Anchor, Up, East, 0.5f, 0f, -1f));
        }
    }
}
