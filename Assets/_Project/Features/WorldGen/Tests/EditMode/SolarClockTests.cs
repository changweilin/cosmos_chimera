using Cosmimera.Core.Contracts;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.WorldGen.Tests
{
    public sealed class SolarClockTests
    {
        [Test]
        public void Integer_sine_tracks_the_true_sine_and_is_exact_at_anchor_angles()
        {
            for (int a = -3600; a <= 7200; a += 7)
            {
                double truth = System.Math.Sin(a / 10.0 * System.Math.PI / 180.0) * 1000.0;
                Assert.That(SolarClock.SinPermille(a), Is.EqualTo(truth).Within(3.0), a.ToString());
                Assert.That(SolarClock.CosPermille(a), Is.EqualTo(SolarClock.SinPermille(a + 900)));
            }

            Assert.That(SolarClock.SinPermille(300), Is.EqualTo(500));
            Assert.That(SolarClock.SinPermille(900), Is.EqualTo(1000));
            Assert.That(SolarClock.SinPermille(2700), Is.EqualTo(-1000));
            Assert.That(SolarClock.SinPermille(0), Is.Zero);
        }

        [Test]
        public void Equator_cycles_dawn_day_dusk_night_with_the_spec_durations()
        {
            var equator = new float3(1f, 0f, 0f);
            var counts = new int[4];
            DayPhase previous = SolarClock.SunPhaseAt(equator, 0);
            var order = new System.Collections.Generic.List<DayPhase> { previous };
            for (long t = 0; t < SolarClock.DayLengthMs; t += 100)
            {
                DayPhase p = SolarClock.SunPhaseAt(equator, t);
                counts[(int)p]++;
                if (p != previous) order.Add(p);
                previous = p;
            }

            // 600 s day with a ±30° twilight band: dawn and dusk 100 s each, day and night 200 s each.
            Assert.That(counts[(int)DayPhase.Dawn] / 10.0, Is.EqualTo(100.0).Within(2.0));
            Assert.That(counts[(int)DayPhase.Dusk] / 10.0, Is.EqualTo(100.0).Within(2.0));
            Assert.That(counts[(int)DayPhase.Day] / 10.0, Is.EqualTo(200.0).Within(2.0));
            Assert.That(counts[(int)DayPhase.Night] / 10.0, Is.EqualTo(200.0).Within(2.0));
            for (int i = 1; i < order.Count; i++)
                Assert.That((int)order[i], Is.EqualTo(((int)order[i - 1] + 1) % 4), "phases advance dawn → day → dusk → night");
        }

        [Test]
        public void Opposite_sides_of_the_planet_see_opposite_phases()
        {
            for (long t = 0; t < SolarClock.DayLengthMs; t += 7_000)
            {
                int a = SolarClock.SunElevationPermille(new float3(0f, 0f, 1f), t);
                int b = SolarClock.SunElevationPermille(new float3(0f, 0f, -1f), t);
                Assert.That(a, Is.EqualTo(-b).Within(1));
            }
        }

        [Test]
        public void Poles_never_leave_twilight_and_eternal_terrains_are_fixed()
        {
            float3 pole = math.normalize(new float3(0.1f, 1f, 0f));
            for (long t = 0; t < SolarClock.DayLengthMs; t += 5_000)
            {
                DayPhase p = SolarClock.SunPhaseAt(pole, t);
                Assert.That(p == DayPhase.Dawn || p == DayPhase.Dusk, Is.True, t.ToString());
                Assert.That(SolarClock.PhaseAt(new float3(1f, 0f, 0f), t, "eternal_day"), Is.EqualTo(DayPhase.Day));
                Assert.That(SolarClock.PhaseAt(new float3(1f, 0f, 0f), t, "eternal_night"), Is.EqualTo(DayPhase.Night));
            }
        }

        [Test]
        public void Sun_direction_matches_the_rule_elevation()
        {
            var site = math.normalize(new float3(0.6f, 0.3f, -0.74f));
            for (long t = 0; t < SolarClock.DayLengthMs; t += 13_000)
            {
                float e = math.dot(site, SolarClock.SunDirection(t)) * 1000f;
                Assert.That(SolarClock.SunElevationPermille(site, t), Is.EqualTo(e).Within(4f));
            }
        }

        [Test]
        public void Contracts_reject_invalid_arguments()
        {
            Assert.Throws<ContractException>(() => SolarClock.SunAngleTenths(-1));
            Assert.Throws<ContractException>(() => SolarClock.SunPhaseAt(new float3(2f, 0f, 0f), 0));
            Assert.Throws<ContractException>(() => SolarClock.SunElevationPermille(new float3(1f, 0f, 0f), -1));
            Assert.Throws<ContractException>(() => SolarClock.PhaseAt(new float3(0f, 0f, 0f), 0, "eternal_day"));
            Assert.Throws<ContractException>(() => SolarClock.PhaseAt(new float3(1f, 0f, 0f), -1, "eternal_night"));
        }
    }
}
