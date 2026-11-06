using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Planet.Tests
{
    public sealed class PlanetFrameTests
    {
        private const float Eps = 1e-5f;

        internal static float3 RandomDirection(ref Pcg32 rng)
        {
            while (true)
            {
                var p = new float3(rng.NextFloat() * 2f - 1f, rng.NextFloat() * 2f - 1f, rng.NextFloat() * 2f - 1f);
                float l = math.lengthsq(p);
                if (l > 1e-4f && l <= 1f)
                {
                    return math.normalize(p);
                }
            }
        }

        // Poles, equator, face centres, cube corners, and points just off the poles where East is ill-conditioned.
        private static readonly float3[] SpecialDirections =
        {
            new float3(0, 1, 0), new float3(0, -1, 0), new float3(1, 0, 0), new float3(-1, 0, 0),
            new float3(0, 0, 1), new float3(0, 0, -1), math.normalize(new float3(1, 1, 1)),
            math.normalize(new float3(-1, 1, -1)), math.normalize(new float3(1e-6f, 1, 0)),
            math.normalize(new float3(0, -1, 3e-6f)), math.normalize(new float3(1e-3f, 1, -1e-3f)),
        };

        private static void AssertOrthonormalRightHanded(PlanetFrame f, string label)
        {
            Assert.AreEqual(1f, math.length(f.East), Eps, label + " |East|");
            Assert.AreEqual(1f, math.length(f.North), Eps, label + " |North|");
            Assert.AreEqual(1f, math.length(f.Up), Eps, label + " |Up|");
            Assert.AreEqual(0f, math.dot(f.East, f.North), Eps, label + " East·North");
            Assert.AreEqual(0f, math.dot(f.East, f.Up), Eps, label + " East·Up");
            Assert.AreEqual(0f, math.dot(f.North, f.Up), Eps, label + " North·Up");
            Assert.Less(math.distance(math.cross(f.East, f.North), f.Up), 1e-4f, label + " right-handed");
        }

        [Test]
        public void Frame_is_orthonormal_and_up_points_away_from_centre_everywhere()
        {
            var config = new PlanetConfig(640f, new float3(10f, -20f, 30f));
            var rng = new Pcg32(7UL, 1UL);
            for (int i = 0; i < 2000; i++)
            {
                float3 dir = RandomDirection(ref rng);
                float3 pos = config.ToWorld(dir, rng.NextFloat() * 100f - 20f);
                PlanetFrame f = PlanetFrame.At(config, pos);
                AssertOrthonormalRightHanded(f, $"#{i}");
                Assert.Greater(math.dot(f.Up, pos - config.Center), 0f, "up points away from centre");
                Assert.Less(math.distance(f.Up, dir), 1e-4f);
            }

            foreach (float3 dir in SpecialDirections)
            {
                AssertOrthonormalRightHanded(PlanetFrame.FromUp(dir), dir.ToString());
            }
        }

        [Test]
        public void North_points_to_the_pole_and_east_follows_longitude()
        {
            PlanetFrame f = PlanetFrame.FromUp(new float3(1, 0, 0));
            Assert.Less(math.distance(f.North, PlanetFrame.NorthPole), Eps);
            Assert.Less(math.distance(f.East, new LatLon(0f, math.PI * 0.5f).ToDirection()), Eps);
        }

        [Test]
        public void Exact_pole_uses_the_documented_east_fallback()
        {
            Assert.Less(math.distance(PlanetFrame.FromUp(new float3(0, 1, 0)).East, new float3(0, 0, -1)), Eps);
            Assert.Less(math.distance(PlanetFrame.FromUp(new float3(0, -1, 0)).East, new float3(0, 0, -1)), Eps);
        }

        [Test]
        public void Local_world_round_trip_preserves_vectors()
        {
            var rng = new Pcg32(3UL, 9UL);
            for (int i = 0; i < 200; i++)
            {
                PlanetFrame f = PlanetFrame.FromUp(RandomDirection(ref rng));
                var v = new float3(rng.NextFloat() * 10f - 5f, rng.NextFloat() * 10f - 5f, rng.NextFloat() * 10f - 5f);
                Assert.Less(math.distance(f.LocalToWorld(f.WorldToLocal(v)), v), 1e-4f);
                Assert.AreEqual(math.length(v), math.length(f.WorldToLocal(v)), 1e-4f);
            }
        }

        [Test]
        public void ProjectOnTangent_is_unit_and_tangent_or_falls_back_to_north()
        {
            PlanetFrame f = PlanetFrame.FromUp(math.normalize(new float3(1, 2, 3)));
            float3 t = f.ProjectOnTangent(new float3(0.3f, -1f, 0.2f));
            Assert.AreEqual(1f, math.length(t), Eps);
            Assert.AreEqual(0f, math.dot(t, f.Up), Eps);
            Assert.AreEqual(f.North, f.ProjectOnTangent(f.Up * 4f));
        }

        [Test]
        public void Contract_violations_throw()
        {
            Assert.Throws<ContractException>(() => PlanetFrame.FromUp(new float3(0, 2, 0)));
            Assert.Throws<ContractException>(() => default(PlanetFrame).LocalToWorld(new float3(1, 0, 0)));
            Assert.Throws<ContractException>(() => default(PlanetFrame).WorldToLocal(new float3(1, 0, 0)));
            Assert.Throws<ContractException>(() => default(PlanetFrame).ProjectOnTangent(new float3(1, 0, 0)));
            Assert.Throws<ContractException>(() =>
                PlanetFrame.FromUp(new float3(0, 1, 0)).ProjectOnTangent(new float3(float.NaN, 0, 0)));
            var config = new PlanetConfig(640f, float3.zero);
            Assert.Throws<ContractException>(() => PlanetFrame.At(config, float3.zero));
        }
    }

    public sealed class PlanetConfigTests
    {
        [Test]
        public void Horizon_matches_the_planet_tech_table()
        {
            // REQ: docs/architecture/planet-tech.md#地平線 — eye 1.7 m: R=320 → 33 m, 640 → 47 m, 1280 → 66 m.
            Assert.AreEqual(33f, new PlanetConfig(320f, float3.zero).HorizonDistance(1.7f), 0.5f);
            Assert.AreEqual(47f, new PlanetConfig(640f, float3.zero).HorizonDistance(1.7f), 0.5f);
            Assert.AreEqual(66f, new PlanetConfig(1280f, float3.zero).HorizonDistance(1.7f), 0.5f);
            Assert.AreEqual(0f, new PlanetConfig(640f, float3.zero).HorizonDistance(0f));
        }

        [Test]
        public void World_direction_altitude_round_trip()
        {
            var config = new PlanetConfig(1280f, new float3(5f, 6f, 7f));
            var rng = new Pcg32(11UL, 2UL);
            for (int i = 0; i < 500; i++)
            {
                float3 dir = PlanetFrameTests.RandomDirection(ref rng);
                float alt = rng.NextFloat() * 200f - 100f;
                float3 p = config.ToWorld(dir, alt);
                Assert.Less(math.distance(config.ToDirection(p), dir), 1e-5f);
                Assert.AreEqual(alt, config.Altitude(p), 2e-3f);
            }
        }

        [Test]
        public void Contract_violations_throw()
        {
            Assert.Throws<ContractException>(() => new PlanetConfig(0f, float3.zero));
            Assert.Throws<ContractException>(() => new PlanetConfig(-1f, float3.zero));
            Assert.Throws<ContractException>(() => new PlanetConfig(float.NaN, float3.zero));
            Assert.Throws<ContractException>(() => new PlanetConfig(float.PositiveInfinity, float3.zero));
            Assert.Throws<ContractException>(() => new PlanetConfig(PlanetConfig.MaxSupportedRadius * 2f, float3.zero));
            Assert.Throws<ContractException>(() => new PlanetConfig(640f, new float3(float.NaN, 0, 0)));
            var c = new PlanetConfig(640f, float3.zero);
            Assert.Throws<ContractException>(() => c.HorizonDistance(-1f));
            Assert.Throws<ContractException>(() => default(PlanetConfig).HorizonDistance(1f));
            Assert.Throws<ContractException>(() => c.ToWorld(new float3(0, 2, 0), 0f));
            Assert.Throws<ContractException>(() => c.ToWorld(new float3(0, 1, 0), -700f));
            Assert.Throws<ContractException>(() => c.ToDirection(float3.zero));
            Assert.Throws<ContractException>(() => c.Altitude(new float3(float.NaN, 0, 0)));
            Assert.IsFalse(default(PlanetConfig).IsValid);
        }
    }

    public sealed class LatLonTests
    {
        [Test]
        public void Direction_round_trip_including_near_poles()
        {
            var rng = new Pcg32(5UL, 5UL);
            for (int i = 0; i < 1000; i++)
            {
                float3 dir = PlanetFrameTests.RandomDirection(ref rng);
                LatLon ll = LatLon.FromDirection(dir);
                Assert.LessOrEqual(math.abs(ll.Latitude), math.PI * 0.5f + 1e-6f);
                Assert.Greater(ll.Longitude, -math.PI - 1e-6f);
                Assert.LessOrEqual(ll.Longitude, math.PI + 1e-6f);
                Assert.Less(math.distance(ll.ToDirection(), dir), 1e-5f);
            }

            Assert.AreEqual(math.PI * 0.5f, LatLon.FromDirection(new float3(0, 1, 0)).Latitude, 1e-6f);
            Assert.AreEqual(0f, LatLon.FromDirection(new float3(0, -1, 0)).Longitude);
        }

        [Test]
        public void Contract_violations_throw()
        {
            Assert.Throws<ContractException>(() => new LatLon(2f, 0f));
            Assert.Throws<ContractException>(() => new LatLon(0f, float.NaN));
            Assert.Throws<ContractException>(() => LatLon.FromDirection(new float3(0, 0, 0)));
        }
    }
}
