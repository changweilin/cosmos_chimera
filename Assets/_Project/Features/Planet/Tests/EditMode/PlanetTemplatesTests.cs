using Cosmimera.Core.Contracts;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Planet.Tests
{
    public sealed class PlanetTemplatesTests
    {
        private static readonly PlanetConfig Planet640 = new PlanetConfig(640f, float3.zero);
        private static readonly float3 Center = math.normalize(new float3(0.3f, 0.8f, 0.2f));

        // West half sea (height 0, water 1), east half a ramp up to the full relief.
        private static TemplateSource Ramp()
        {
            const int n = 64;
            var h = new ushort[n * n];
            var w = new byte[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    bool sea = x < n / 2;
                    h[y * n + x] = sea ? (ushort)0 : (ushort)(65535 * (x - n / 2) / (n / 2 - 1));
                    w[y * n + x] = sea ? (byte)1 : (byte)0;
                }
            }

            return new TemplateSource("ramp", n, h, w, 0f, 2500f);
        }

        private static PlanetTemplates Make() =>
            PlanetTemplates.Create(Planet640, new[] { Ramp() }, new[] { new TemplatePlacement(0, Center, 0f, 0.3f) });

        private static float3 Offset(float angle, float3 tangent) =>
            math.normalize(Center * math.cos(angle) + tangent * math.sin(angle));

        [Test]
        public void Outside_every_stamp_the_noise_height_is_unchanged()
        {
            using PlanetTemplates t = Make();
            float3 far = -Center;
            Assert.That(t.View.Apply(far, 3.25f), Is.EqualTo(3.25f));
            Assert.That(default(TemplateView).Apply(Center, 1.5f), Is.EqualTo(1.5f), "default view = no templates");
        }

        [Test]
        public void Blend_is_continuous_across_the_stamp_edge()
        {
            using PlanetTemplates t = Make();
            float3 tangent = PlanetFrame.FromUp(Center).North;
            float prev = t.View.Apply(Offset(0f, tangent), 2f);
            // Walk outward past the edge in small steps: no step may jump more than a fraction of the amplitude.
            for (int i = 1; i <= 400; i++)
            {
                float h = t.View.Apply(Offset(0.45f * i / 400f, tangent), 2f);
                Assert.That(math.abs(h - prev), Is.LessThan(1.5f), $"step {i}");
                prev = h;
            }

            Assert.That(prev, Is.EqualTo(2f), "beyond the edge the noise height returns exactly");
        }

        [Test]
        public void Sea_half_is_below_the_land_half_and_reported_as_sea()
        {
            using PlanetTemplates t = Make();
            TemplateView v = t.View;
            float3 east = math.normalize(v.Apply(Center, 0f) * 0f + StampRight(t));
            float3 seaDir = Offset(0.15f, -east);
            float3 landDir = Offset(0.15f, east);
            Assert.That(v.IsSea(seaDir), Is.True);
            Assert.That(v.IsSea(landDir), Is.False);
            Assert.That(v.Apply(seaDir, 0f), Is.LessThan(0f));
            Assert.That(v.Apply(landDir, 0f), Is.GreaterThan(5f));
        }

        [Test]
        public void Max_contribution_bounds_the_offset()
        {
            using PlanetTemplates t = Make();
            float3 tangent = PlanetFrame.FromUp(Center).East;
            for (int i = 0; i < 200; i++)
            {
                float3 d = Offset(0.3f * i / 200f, tangent);
                Assert.That(math.abs(t.View.Apply(d, 0f)), Is.LessThanOrEqualTo(t.MaxAbsContribution));
            }
        }

        [Test]
        public void Placement_rejects_a_half_angle_beyond_the_gnomonic_limit()
        {
            Assert.Throws<ContractException>(() => new TemplatePlacement(0, Center, 0f, 0.7f));
        }

        // The stamp's +x tangent: −East at rotation 0 (Q-T5 un-mirroring in PlanetTemplates.Create).
        private static float3 StampRight(PlanetTemplates t) => -PlanetFrame.FromUp(Center).East;
    }
}
