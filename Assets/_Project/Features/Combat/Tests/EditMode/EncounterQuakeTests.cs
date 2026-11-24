using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/planet-world.md#地震噴發 — foreshock, cracks that throw and hurt, bombs, lava that cools.
    public sealed class EncounterQuakeTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;

            public bool IsSea(float3 direction) => false;

            public bool IsBlocked(float3 direction, float clearanceM) => false;
        }

        private const float R = 640f;
        private const int Dt = 50;
        private const ulong Seed = 21;
        private static readonly AiCommand Free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
        private PlanetConfig _planet;
        private CombatBalance _balance;

        [SetUp]
        public void SetUp()
        {
            _planet = new PlanetConfig(R, float3.zero);
            _balance = CombatBalance.FromJson(File.ReadAllText(BalanceFixture.RepoPath("Data", "balance.json")));
        }

        private static float3 Origin => new float3(0f, 1f, 0f);

        private Encounter Volcanic(out PlayerVitals vitals, bool volcanic = true)
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 7);
            vitals = new PlayerVitals(new PlayerVitalsConfig(1000, 600, 400, 800));
            e.AttachPlayer(vitals, 100, 100);
            e.AttachQuakes(Seed, _ => 0, _ => Origin, 30f, true, _ => volcanic);
            return e;
        }

        private static long FirstStart()
        {
            QuakeSchedule.Timing(Seed, 0, 0, true, out long start, out _, out _);
            return start;
        }

        // One tick shortly before the foreshock builds the quake, so the test can stand the player relative to it.
        private Quake Build(Encounter e, long at)
        {
            e.Tick(at, Dt, _planet.ToWorld(Origin, 0f) + new float3(200f, 0f, 0f), Free, CombatAi.NoTarget);
            Assert.That(e.Quakes.Count, Is.EqualTo(1));
            return e.Quakes[0];
        }

        private float3 Beside(in QuakeCrack crack, float acrossM)
        {
            float3 across = math.cross(crack.Center, crack.Along);
            return _planet.ToWorld(math.normalize(crack.Center + across * (acrossM / R)), 0f);
        }

        private static List<EncounterEvent> Run(Encounter e, float3 player, long from, long until)
        {
            var events = new List<EncounterEvent>();
            for (long now = from; now <= until; now += Dt)
            {
                e.Tick(now, Dt, player, Free, CombatAi.NoTarget);
                events.AddRange(e.Events);
                foreach (VolcanicBomb b in e.PendingBombs) Assert.That(b.LandsAtMs, Is.GreaterThan(now));
            }

            return events;
        }

        [Test]
        public void Phases_follow_the_schedule()
        {
            Encounter e = Volcanic(out _);
            long start = FirstStart();
            Quake q = Build(e, start - 500);
            Assert.That(q.StartMs, Is.EqualTo(start));
            Assert.That(q.CrackMs, Is.EqualTo(start + QuakeSchedule.ForeshockMs));
            Assert.That(q.EruptEndMs - q.CrackMs, Is.InRange(6000, 10_000));
            Assert.That(q.CoolEndMs, Is.EqualTo(q.EruptEndMs + QuakeSchedule.LavaCoolMs));
            Assert.That(q.Cracks.Count, Is.InRange(1, QuakeSchedule.MaxCracks));
            foreach (QuakeCrack c in q.Cracks)
            {
                Assert.That(c.LengthM, Is.InRange(15f, 40f));
                Assert.That(math.abs(math.dot(c.Center, c.Along)), Is.LessThan(1e-4f));
            }
        }

        [Test]
        public void Standing_on_a_crack_as_it_opens_throws_and_hurts_the_player()
        {
            Encounter e = Volcanic(out PlayerVitals vitals);
            long start = FirstStart();
            Quake q = Build(e, start - 500);
            float3 on = Beside(q.Cracks[0], 0.3f);
            bool launched = false;
            var events = new List<EncounterEvent>();
            for (long now = start - 450; now <= q.CrackMs + 100; now += Dt)
            {
                e.Tick(now, Dt, on, Free, CombatAi.NoTarget);
                events.AddRange(e.Events);
                if (math.lengthsq(e.PlayerLaunchDirection) > 0f)
                {
                    launched = true;
                    float3 up = _planet.ToDirection(on);
                    Assert.That(math.abs(math.dot(e.PlayerLaunchDirection, up)), Is.LessThan(1e-3f), "thrown along the ground");
                }
            }

            EncounterEvent hit = events.Find(ev => ev.Kind == EncounterEventKind.Quaked);
            Assert.That(hit.Kind, Is.EqualTo(EncounterEventKind.Quaked));
            Assert.That(hit.TargetId, Is.EqualTo(Encounter.PlayerCharacterId));
            Assert.That(hit.Amount, Is.InRange(1, 120));
            Assert.That(launched, Is.True);
            Assert.That(events.FindAll(ev => ev.Kind == EncounterEventKind.Quaked).Count, Is.EqualTo(1), "a crack opens once");
        }

        [Test]
        public void Nothing_happens_before_the_cracks_open_or_far_from_them()
        {
            Encounter e = Volcanic(out PlayerVitals vitals);
            long start = FirstStart();
            Quake q = Build(e, start - 500);
            float3 far = Beside(q.Cracks[0], 60f);
            int hp = vitals.Hp;
            List<EncounterEvent> events = Run(e, far, start - 450, q.CrackMs - Dt);
            Assert.That(events.Exists(ev => ev.Kind == EncounterEventKind.Quaked || ev.Kind == EncounterEventKind.Scorched), Is.False);
            Assert.That(e.PendingBombs.Count, Is.Zero, "no bombs during the foreshock");
            Assert.That(vitals.Hp, Is.EqualTo(hp));
        }

        [Test]
        public void Lava_beside_a_crack_burns_every_second_until_it_cools()
        {
            Encounter e = Volcanic(out _);
            long start = FirstStart();
            Quake q = Build(e, start - 500);
            // Outside the opening crack (1.5 m + body 0.6 m) but on the lava (3 m).
            float3 lava = Beside(q.Cracks[0], 2.5f);
            if (q.Cracks.Count > 1)
            {
                // Another crack crossing this spot would muddy the count; the seed is fixed, so this is a real check.
                for (int i = 1; i < q.Cracks.Count; i++)
                    Assert.That(Encounter.CrackDistanceM(q.Cracks[i], _planet.ToDirection(lava), R), Is.GreaterThan(3.5f));
            }

            List<EncounterEvent> events = Run(e, lava, start - 450, q.CrackMs + 3000);
            Assert.That(events.Exists(ev => ev.Kind == EncounterEventKind.Quaked), Is.False);
            int burns = events.FindAll(ev => ev.Kind == EncounterEventKind.Scorched && ev.TargetId == Encounter.PlayerCharacterId).Count;
            Assert.That(burns, Is.InRange(3, 8), "one lava burn per second, plus any bombs that hit");
        }

        [Test]
        public void Bombs_fall_within_reach_of_their_crack_during_the_eruption()
        {
            Encounter e = Volcanic(out _);
            long start = FirstStart();
            Quake q = Build(e, start - 500);
            float3 far = Beside(q.Cracks[0], 80f);
            int landed = 0;
            for (long now = start - 450; now <= q.EruptEndMs + QuakeSchedule.BombWarningMs + 200; now += Dt)
            {
                e.Tick(now, Dt, far, Free, CombatAi.NoTarget);
                foreach (VolcanicBomb b in e.LandedBombs)
                {
                    landed++;
                    Assert.That(b.LandsAtMs, Is.InRange(q.CrackMs + QuakeSchedule.BombWarningMs, q.EruptEndMs + QuakeSchedule.BombWarningMs));
                    float best = float.MaxValue;
                    foreach (QuakeCrack c in q.Cracks) best = math.min(best, Encounter.CrackDistanceM(c, b.Direction, R));
                    Assert.That(best, Is.LessThanOrEqualTo(14.5f));
                }
            }

            Assert.That(landed, Is.GreaterThan(0));
            Assert.That(e.PendingBombs.Count, Is.Zero);
        }

        [Test]
        public void Quakes_are_identical_on_two_clients_and_absent_off_volcanic_ground()
        {
            Encounter a = Volcanic(out _);
            Encounter b = Volcanic(out _);
            Encounter none = Volcanic(out _, volcanic: false);
            long start = FirstStart();
            Quake qa = Build(a, start - 500);
            Quake qb = Build(b, start - 500);
            Assert.That(qa.Cracks.Count, Is.EqualTo(qb.Cracks.Count));
            for (int i = 0; i < qa.Cracks.Count; i++)
            {
                Assert.That(qa.Cracks[i].Center, Is.EqualTo(qb.Cracks[i].Center));
                Assert.That(qa.Cracks[i].LengthM, Is.EqualTo(qb.Cracks[i].LengthM));
            }

            none.Tick(start + 5000, Dt, _planet.ToWorld(Origin, 0f), Free, CombatAi.NoTarget);
            Assert.That(none.Quakes.Count, Is.Zero);
        }

        [Test]
        public void AttachQuakes_rejects_missing_lookups_and_a_second_attach()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 7);
            Assert.Throws<ContractException>(() => e.AttachQuakes(Seed, null, _ => Origin, 30f, true, _ => true));
            Assert.Throws<ContractException>(() => e.AttachQuakes(Seed, _ => 0, _ => Origin, 0f, true, _ => true));
            e.AttachQuakes(Seed, _ => 0, _ => Origin, 30f, true, _ => true);
            Assert.Throws<ContractException>(() => e.AttachQuakes(Seed, _ => 0, _ => Origin, 30f, true, _ => true));
        }
    }
}
