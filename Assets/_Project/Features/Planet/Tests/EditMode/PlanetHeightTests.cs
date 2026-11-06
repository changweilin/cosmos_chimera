using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Planet.Tests
{
    public sealed class PlanetHeightTests
    {
        private static readonly PlanetConfig Planet640 = new PlanetConfig(640f, float3.zero);

        internal static float3 RandomDirection(ref Pcg32 rng)
        {
            while (true)
            {
                var p = new float3(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()) * 2f - 1f;
                float l = math.lengthsq(p);
                if (l > 1e-4f && l <= 1f)
                {
                    return p / math.sqrt(l);
                }
            }
        }

        // FNV-1a over the raw float bits of 4096 samples: catches any change to the algorithm or its inputs.
        private static ulong SnapshotHash(in PlanetHeight height)
        {
            var rng = new Pcg32(2026UL, 11UL);
            ulong hash = 14695981039346656037UL;
            for (int k = 0; k < 4096; k++)
            {
                uint bits = math.asuint(height.Sample(RandomDirection(ref rng)));
                for (int b = 0; b < 4; b++)
                {
                    hash = unchecked((hash ^ ((bits >> (8 * b)) & 0xFF)) * 1099511628211UL);
                }
            }

            return hash;
        }

        [Test]
        public void Same_seed_gives_same_heights_and_different_seed_differs()
        {
            PlanetHeight a = PlanetHeight.ForPlanet(Planet640, 7UL);
            PlanetHeight b = PlanetHeight.ForPlanet(Planet640, 7UL);
            PlanetHeight c = PlanetHeight.ForPlanet(Planet640, 8UL);
            var rng = new Pcg32(1UL, 1UL);
            int differ = 0;
            for (int k = 0; k < 1000; k++)
            {
                float3 d = RandomDirection(ref rng);
                Assert.AreEqual(math.asuint(a.Sample(d)), math.asuint(b.Sample(d)));
                if (a.Sample(d) != c.Sample(d))
                {
                    differ++;
                }
            }

            Assert.Greater(differ, 990);
        }

        [Test]
        public void Heights_stay_within_MaxAbsHeight_and_use_most_of_the_range()
        {
            PlanetHeight h = PlanetHeight.ForPlanet(Planet640, 3UL);
            var rng = new Pcg32(5UL, 5UL);
            float lo = float.MaxValue;
            float hi = float.MinValue;
            for (int k = 0; k < 20000; k++)
            {
                float s = h.Sample(RandomDirection(ref rng));
                Assert.LessOrEqual(math.abs(s), h.MaxAbsHeight);
                lo = math.min(lo, s);
                hi = math.max(hi, s);
            }

            // Not a tuning assertion: guards against an accidentally flat or constant field.
            Assert.Greater(hi - lo, 0.5f * h.MaxAbsHeight);
        }

        [Test]
        public void MaxAbsHeight_is_the_geometric_sum_of_octave_amplitudes()
        {
            Assert.AreEqual(10f, new PlanetHeight(0UL, 10f, 1f, 1).MaxAbsHeight);
            Assert.AreEqual(15f, new PlanetHeight(0UL, 10f, 1f, 2).MaxAbsHeight);
            Assert.AreEqual(19.6875f, new PlanetHeight(0UL, 10f, 1f, 6).MaxAbsHeight);
        }

        [Test]
        public void ForPlanet_amplitude_scales_with_radius()
        {
            foreach (float r in new[] { 320f, 640f, 1280f })
            {
                PlanetHeight h = PlanetHeight.ForPlanet(new PlanetConfig(r, float3.zero), 1UL);
                Assert.AreEqual(0.03f * r, h.Amplitude, 1e-4f);
            }
        }

        [Test]
        public void Field_is_continuous_at_metre_scale()
        {
            // Neighbouring vertices 1 m apart must not jump by more than a slope bound; a broken offset or
            // frequency would show up as noise-like spikes.
            PlanetHeight h = PlanetHeight.ForPlanet(Planet640, 9UL);
            var rng = new Pcg32(9UL, 2UL);
            for (int k = 0; k < 2000; k++)
            {
                float3 d = RandomDirection(ref rng);
                float3 t = math.normalize(math.cross(d, math.abs(d.x) < 0.9f ? new float3(1f, 0f, 0f) : new float3(0f, 0f, 1f)));
                float3 d2 = math.normalize(d + t * (1f / Planet640.Radius));
                Assert.Less(math.abs(h.Sample(d) - h.Sample(d2)), 2f);
            }
        }

        [Test]
        public void Snapshot_matches_GeneratorVersion()
        {
            Assert.AreEqual(2, PlanetHeight.GeneratorVersion, "GeneratorVersion changed: update the snapshot file and note it in the handoff log");
            string path = Path.Combine(Application.dataPath, "_Project/Features/Planet/Tests/Snapshots/planet-height-v2.txt");
            ulong actual = SnapshotHash(PlanetHeight.ForPlanet(Planet640, 42UL));
            Assert.IsTrue(File.Exists(path), $"missing snapshot {path}; computed 0x{actual:X16}");
            string expected = File.ReadAllText(path).Trim();
            Assert.AreEqual(expected, $"0x{actual:X16}", "height output changed without a GeneratorVersion bump");
        }

        [Test]
        public void Preconditions_are_enforced()
        {
            Assert.Throws<ContractException>(() => new PlanetHeight(0UL, -1f, 1f, 1));
            Assert.Throws<ContractException>(() => new PlanetHeight(0UL, float.NaN, 1f, 1));
            Assert.Throws<ContractException>(() => new PlanetHeight(0UL, 1f, 0f, 1));
            Assert.Throws<ContractException>(() => new PlanetHeight(0UL, 1f, 1f, 0));
            Assert.Throws<ContractException>(() => new PlanetHeight(0UL, 1f, 1f, PlanetHeight.MaxOctaves + 1));
            Assert.Throws<ContractException>(() => PlanetHeight.ForPlanet(default, 1UL));
            Assert.Throws<ContractException>(() => default(PlanetHeight).Sample(new float3(0f, 0f, 1f)));
            PlanetHeight h = PlanetHeight.ForPlanet(Planet640, 1UL);
            Assert.Throws<ContractException>(() => h.Sample(new float3(0f, 0f, 2f)));
            Assert.Throws<ContractException>(() => h.Sample(new float3(float.NaN, 0f, 1f)));
        }
    }
}
