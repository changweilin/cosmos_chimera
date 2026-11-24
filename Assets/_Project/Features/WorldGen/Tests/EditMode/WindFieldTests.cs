using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class WindFieldTests
    {
        private const float Radius = 640f;

        private static float3 LatLonDir(float latDeg, float lonDeg)
        {
            float lat = math.radians(latDeg), lon = math.radians(lonDeg);
            return new float3(math.cos(lat) * math.cos(lon), math.sin(lat), math.cos(lat) * math.sin(lon));
        }

        private static int Turn(int a, int b)
        {
            int d = math.abs(a - b) % 3600;
            return math.min(d, 3600 - d);
        }

        [Test]
        public void Prevailing_heading_is_reproducible_and_in_range()
        {
            for (int k = 0; k < 200; k++)
            {
                float3 up = LatLonDir(-80f + k * 0.8f, k * 7.3f);
                long t = k * 17_011L;
                int a = WindField.PrevailingHeadingTenths(42UL, Radius, up, t);
                Assert.That(a, Is.InRange(0, 3599));
                Assert.That(WindField.PrevailingHeadingTenths(42UL, Radius, up, t), Is.EqualTo(a));
            }
        }

        [Test]
        public void Prevailing_flow_differs_between_latitude_bands()
        {
            // Averaged over longitudes and seeds so the meander cancels: trades blow west, westerlies east.
            float tradesEast = 0f, westerliesEast = 0f;
            for (int lon = 0; lon < 360; lon += 10)
            {
                for (ulong seed = 1; seed <= 4; seed++)
                {
                    tradesEast += math.cos(math.radians(WindField.PrevailingHeadingTenths(seed, Radius, LatLonDir(15f, lon), 0) / 10f));
                    westerliesEast += math.cos(math.radians(WindField.PrevailingHeadingTenths(seed, Radius, LatLonDir(45f, lon), 0) / 10f));
                }
            }

            Assert.That(tradesEast, Is.LessThan(0f), "trade winds blow toward the west");
            Assert.That(westerliesEast, Is.GreaterThan(0f), "westerlies blow toward the east");
        }

        [Test]
        public void Prevailing_heading_varies_over_the_surface_but_smoothly()
        {
            var seen = new HashSet<int>();
            float3 start = LatLonDir(40f, 10f);
            PlanetFrame frame = PlanetFrame.FromUp(start);
            int prev = WindField.PrevailingHeadingTenths(9UL, Radius, start, 0);
            for (int m = 1; m <= 400; m++)
            {
                float3 up = math.normalize(start + frame.East * (m / Radius));
                int now = WindField.PrevailingHeadingTenths(9UL, Radius, up, 0);
                // ±70° over ~180 m cannot turn more than a few degrees per metre.
                Assert.That(Turn(now, prev), Is.LessThan(60), $"m={m}");
                seen.Add(now / 100);
                prev = now;
            }

            Assert.That(seen.Count, Is.GreaterThan(2), "the wind must not blow one way across 400 m");
        }

        [Test]
        public void Vortices_are_reproducible_and_their_peaks_bounded()
        {
            var a = new List<WindVortex>();
            var b = new List<WindVortex>();
            float3 near = LatLonDir(15f, 30f);
            for (long t = 0; t < 1_800_000; t += 60_000)
            {
                a.Clear();
                b.Clear();
                WindField.ActiveVortices(5UL, Radius, near, t, 150f, null, a);
                WindField.ActiveVortices(5UL, Radius, near, t, 150f, null, b);
                Assert.That(a.Count, Is.EqualTo(b.Count));
                Assert.That(a.Count, Is.LessThanOrEqualTo(WindField.MaxVortices));
                for (int i = 0; i < a.Count; i++)
                {
                    Assert.That(a[i].Center, Is.EqualTo(b[i].Center));
                    Assert.That(a[i].PeakPermille, Is.InRange(0, 1000));
                    Assert.That(a[i].CoreM, Is.GreaterThan(0f).And.LessThan(a[i].OuterM));
                    Assert.That(math.abs(a[i].Spin), Is.EqualTo(1));
                }
            }
        }

        [Test]
        public void Every_vortex_scale_occurs_over_an_hour()
        {
            var kinds = new HashSet<VortexKind>();
            var list = new List<WindVortex>();
            for (int k = 0; k < 40 && kinds.Count < 4; k++)
            {
                float3 near = LatLonDir(-60f + k * 3f, k * 37f);
                for (long t = 0; t < 3_600_000 && kinds.Count < 4; t += 15_000)
                {
                    list.Clear();
                    WindField.ActiveVortices(77UL, Radius, near, t, 100f, null, list);
                    foreach (WindVortex v in list) kinds.Add(v.Kind);
                }
            }

            Assert.That(kinds, Is.EquivalentTo(new[] { VortexKind.DustDevil, VortexKind.Tornado, VortexKind.Eddy, VortexKind.Cyclone }));
        }

        [Test]
        public void Allow_filter_removes_kinds()
        {
            var list = new List<WindVortex>();
            for (long t = 0; t < 600_000; t += 20_000)
            {
                WindField.ActiveVortices(3UL, Radius, LatLonDir(10f, 0f), t, 200f, (kind, _) => kind == VortexKind.Eddy, list);
            }

            Assert.That(list.TrueForAll(v => v.Kind == VortexKind.Eddy), Is.True);
        }

        [Test]
        public void Sample_without_vortices_is_the_prevailing_wind()
        {
            float3 up = LatLonDir(20f, 50f);
            WindSample s = WindField.Sample(1UL, Radius, up, 1234, new List<WindVortex>());
            Assert.That(s.HeadingTenths, Is.EqualTo(WindField.PrevailingHeadingTenths(1UL, Radius, up, 1234)));
            Assert.That(s.VortexPermille, Is.Zero);
        }

        [Test]
        public void Wind_circles_the_eye_in_the_vortex_spin()
        {
            float3 eye = LatLonDir(20f, 50f);
            PlanetFrame frame = PlanetFrame.FromUp(eye);
            foreach (int spin in new[] { 1, -1 })
            {
                var vortex = new List<WindVortex> { new WindVortex(VortexKind.Tornado, eye, 6f, 20f, spin, 1000, 0) };
                // 6 m east of the eye the swirl blows north for spin +1 (east → north is +), south for −1.
                float3 east = math.normalize(eye + frame.East * (6f / Radius));
                WindSample s = WindField.Sample(1UL, Radius, east, 0, vortex);
                float north = math.sin(math.radians(s.HeadingTenths / 10f));
                Assert.That(north * spin, Is.GreaterThan(0.5f), $"spin {spin}");
                Assert.That(s.VortexPermille, Is.GreaterThan(900));
                // Past the rim nothing is added.
                float3 far = math.normalize(eye + frame.East * (25f / Radius));
                Assert.That(WindField.Sample(1UL, Radius, far, 0, vortex).VortexPermille, Is.Zero);
            }
        }

        [Test]
        public void Gust_is_bounded_by_the_wind_and_zero_in_calm()
        {
            bool any = false;
            for (long t = 0; t < 600_000; t += 250)
            {
                Assert.That(WindField.GustPermille(5UL, 2, t, 0), Is.Zero);
                int g = WindField.GustPermille(5UL, 2, t, 800);
                Assert.That(g, Is.InRange(0, 800));
                any |= g > 200;
            }

            Assert.That(any, Is.True, "a strong wind must gust");
        }

        [Test]
        public void Tangent_is_unit_and_on_the_surface()
        {
            float3 up = math.normalize(new float3(0.3f, 0.5f, -0.8f));
            for (int h = -3600; h < 7200; h += 337)
            {
                float3 w = WindField.Tangent(up, h);
                Assert.That(math.length(w), Is.EqualTo(1f).Within(1e-4f));
                Assert.That(math.dot(w, up), Is.EqualTo(0f).Within(1e-4f));
            }
        }

        [Test]
        public void Bad_arguments_are_rejected()
        {
            float3 up = new float3(0f, 0f, 1f);
            var list = new List<WindVortex>();
            Assert.Throws<ContractException>(() => WindField.PrevailingHeadingTenths(1UL, 0f, up, 0));
            Assert.Throws<ContractException>(() => WindField.PrevailingHeadingTenths(1UL, Radius, up, -1));
            Assert.Throws<ContractException>(() => WindField.ActiveVortices(1UL, Radius, up, 0, -1f, null, list));
            Assert.Throws<ContractException>(() => WindField.ActiveVortices(1UL, Radius, up, 0, 0f, null, null));
            Assert.Throws<ContractException>(() => WindField.Sample(1UL, Radius, up, 0, null));
            Assert.Throws<ContractException>(() => WindField.GustPermille(1UL, 0, 0, 1001));
            Assert.Throws<ContractException>(() => WindField.GustPermille(1UL, 0, 0, -1));
        }
    }
}
