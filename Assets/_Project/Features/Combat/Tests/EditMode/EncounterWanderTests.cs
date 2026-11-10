using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/combat.md#遊蕩 (D0-W1, decided 2026-11-10; values confirmed, Q-D2).
    public sealed class EncounterWanderTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;
        }

        private const float R = 640f;
        private const int Dt = 50;
        // Steering stops within this distance of a waypoint (Encounter.ArriveM).
        private const float ArriveM = 0.4f;
        private readonly List<Object> _created = new List<Object>();
        private PlanetConfig _planet;
        private CombatBalance _balance;
        private static readonly AiCommand Free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);

        [SetUp]
        public void SetUp()
        {
            _planet = new PlanetConfig(R, float3.zero);
            _balance = CombatBalance.FromJson(File.ReadAllText(BalanceFixture.RepoPath("Data", "balance.json")));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        private CreatureDef Creature(string id, Temperament temperament)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId("bite_" + id);
            m.Set(Element.Fire, MoveKind.Damage, 40, null, 0, 0, 1500, 100, RangeClass.Melee, MoveShape.Front, 2000,
                MoveMovement.None, BurstOrigin.Caster);
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            c.Set("line_" + id, CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Charge, temperament, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(25, 25, 25, 25), m, null, "{}", false);
            return c;
        }

        private static float3 AtMeters(float meters)
        {
            float a = meters / R;
            return math.normalize(new float3(math.sin(a), math.cos(a), 0f));
        }

        // Far beyond every territory, so the wild never engages.
        private float3 FarPlayer => _planet.ToWorld(AtMeters(300f), 0f);

        private (Encounter, EncounterUnit) Lone(Temperament temperament, ulong seed)
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), seed);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", temperament), null, AtMeters(0f), 10);
            return (e, wild);
        }

        [Test]
        public void Wander_radius_reads_balance_per_temperament()
        {
            JToken w = BalanceFixture.BalanceJson()["ai"]["wander_mm"];
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            Temperament[] all =
            {
                Temperament.Timid, Temperament.Cautious, Temperament.Normal, Temperament.Territorial, Temperament.Fierce,
                Temperament.Apex,
            };
            string[] keys = { "timid", "cautious", "normal", "territorial", "fierce", "apex" };
            for (int i = 0; i < all.Length; i++)
            {
                EncounterUnit u = e.AddCreature(EncounterSide.Wild, Creature("c" + i, all[i]), null, AtMeters(30f * i), 10);
                Assert.That(e.WanderRadiusM(u.Id), Is.EqualTo((int)w[keys[i]] * 0.001f).Within(1e-5f), keys[i]);
                Assert.That(e.WanderRadiusM(u.Id), Is.LessThan(Encounter.DisengageRadiusM));
            }

            Assert.Throws<ContractException>(() => e.WanderRadiusM(99));
        }

        [Test]
        public void WanderTuning_preconditions()
        {
            Assert.Throws<ContractException>(() => _ = new WanderTuning(-1, 1, 1, 1, 1, 1, 0, 0, 400));
            Assert.Throws<ContractException>(() => _ = new WanderTuning(1, 1, 1, 1, 1, 40000, 0, 0, 400), "below disengage");
            Assert.Throws<ContractException>(() => _ = new WanderTuning(1, 1, 1, 1, 1, 1, -1, 0, 400));
            Assert.Throws<ContractException>(() => _ = new WanderTuning(1, 1, 1, 1, 1, 1, 3000, 2000, 400), "min > max");
            Assert.Throws<ContractException>(() => _ = new WanderTuning(1, 1, 1, 1, 1, 1, 0, 0, 0), "speed 0");
            Assert.Throws<ContractException>(() => _ = new WanderTuning(1, 1, 1, 1, 1, 1, 0, 0, 1001));
            var t = new WanderTuning(0, 2, 3, 4, 5, 39999, 0, 0, 1000);
            Assert.That(t.RadiusMm(Temperament.Timid), Is.EqualTo(0));
            Assert.That(t.RadiusMm(Temperament.Apex), Is.EqualTo(39999));
            Assert.That(t.PauseMinMs, Is.EqualTo(0));
            Assert.That(t.PauseMaxMs, Is.EqualTo(0));
            Assert.That(t.SpeedPermille, Is.EqualTo(1000));
            Assert.Throws<ContractException>(() => t.RadiusMm((Temperament)99));
        }

        [Test]
        public void A_fresh_wild_idles_for_at_least_the_minimum_pause()
        {
            (Encounter e, EncounterUnit wild) = Lone(Temperament.Normal, 7);
            float3 start = wild.Position;
            long until = _balance.Wander.PauseMinMs;
            for (long now = Dt; now <= until; now += Dt)
            {
                e.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                Assert.That(wild.Position, Is.EqualTo(start), $"t={now}");
            }
        }

        [TestCase(Temperament.Timid)]
        [TestCase(Temperament.Normal)]
        [TestCase(Temperament.Apex)]
        public void An_idle_wild_wanders_but_never_leaves_its_radius(Temperament temperament)
        {
            (Encounter e, EncounterUnit wild) = Lone(temperament, 11);
            float radius = e.WanderRadiusM(wild.Id);
            float maxStep = _balance.Stats.MoveSpeedMmS(wild.Agi) * 0.001f * _balance.Wander.SpeedPermille / 1000f *
                Dt * 0.001f;
            float farthest = 0f;
            float3 previous = wild.Position;
            for (long now = Dt; now <= 180_000; now += Dt)
            {
                e.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                Assert.That(wild.Engaged, Is.False);
                float fromHome = math.distance(wild.Position, wild.Home);
                Assert.That(fromHome, Is.LessThanOrEqualTo(radius + ArriveM + 1e-3f), $"t={now}");
                Assert.That(math.distance(wild.Position, previous), Is.LessThanOrEqualTo(maxStep + 1e-3f), $"t={now}");
                Assert.That(math.length(wild.Position), Is.EqualTo(R + wild.BodyRadiusM).Within(1e-2f), "stays grounded");
                farthest = math.max(farthest, fromHome);
                previous = wild.Position;
            }

            Assert.That(farthest, Is.GreaterThan(math.min(1f, radius * 0.25f)), "it actually moved");
        }

        [Test]
        public void Wandering_is_reproducible_per_seed()
        {
            (Encounter a, EncounterUnit wa) = Lone(Temperament.Fierce, 42);
            (Encounter b, EncounterUnit wb) = Lone(Temperament.Fierce, 42);
            (Encounter c, EncounterUnit wc) = Lone(Temperament.Fierce, 43);
            bool diverged = false;
            for (long now = Dt; now <= 60_000; now += Dt)
            {
                a.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                b.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                c.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                Assert.That(wb.Position, Is.EqualTo(wa.Position), $"t={now}");
                diverged |= math.any(wc.Position != wa.Position);
            }

            Assert.That(diverged, Is.True, "another seed takes another path");
        }

        [Test]
        public void Wandering_does_not_consume_the_combat_rng()
        {
            (Encounter e, EncounterUnit _) = Lone(Temperament.Apex, 5);
            Pcg32 before = e.RngForTests;
            for (long now = Dt; now <= 30_000; now += Dt)
            {
                e.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
            }

            Pcg32 after = e.RngForTests;
            Assert.That(after.NextUInt(), Is.EqualTo(before.NextUInt()));
        }

        [Test]
        public void A_wild_outside_its_radius_walks_home_without_pausing()
        {
            (Encounter e, EncounterUnit wild) = Lone(Temperament.Normal, 3);
            float radius = e.WanderRadiusM(wild.Id);
            // Stand-in for where a fight left it: 25 m from home, past the 6 m wander radius.
            wild.Position = _planet.ToWorld(AtMeters(25f), wild.BodyRadiusM);
            float last = math.distance(wild.Position, wild.Home);
            long now = Dt;
            for (; last > radius + ArriveM; now += Dt)
            {
                Assert.That(now, Is.LessThan(60_000), "should reach its radius");
                e.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                float d = math.distance(wild.Position, wild.Home);
                Assert.That(d, Is.LessThan(last), $"t={now}: closes in every tick, no pause");
                last = d;
            }
        }

        [Test]
        public void Engaging_cancels_the_current_leg()
        {
            (Encounter e, EncounterUnit wild) = Lone(Temperament.Normal, 9);
            long now = Dt;
            for (; !wild.HasWanderTarget; now += Dt)
            {
                Assert.That(now, Is.LessThan(30_000), "should start a leg");
                e.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
            }

            e.Tick(now, Dt, wild.Position, Free, CombatAi.NoTarget);
            Assert.That(wild.Engaged, Is.True);
            Assert.That(wild.HasWanderTarget, Is.False);
            Assert.That(wild.WanderResumeMs, Is.EqualTo(Encounter.WanderUnscheduled));
        }
    }
}
