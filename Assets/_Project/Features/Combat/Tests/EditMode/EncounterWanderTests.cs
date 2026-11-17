using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/creature-ecology.md#四種遊蕩模式 (P3-31), combat.md#遊蕩.
    public sealed class EncounterWanderTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;

            public bool IsSea(float3 direction) => false;

            public bool IsBlocked(float3 direction, float clearanceM) => false;
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

        private CreatureDef Creature(string id, Habit habit)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId("bite_" + id);
            m.Set(Element.Fire, MoveKind.Damage, 40, null, 0, 0, 1500, 100, RangeClass.Melee, MoveShape.Front, 2000,
                MoveMovement.None, BurstOrigin.Caster);
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            TestWild.WithHabit(c, habit).Set("line_" + id, CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Charge, null, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(78, 25, 25, 25, 25, 25), m, null, "{}", false);
            return c;
        }

        private static float3 AtMeters(float meters)
        {
            float a = meters / R;
            return math.normalize(new float3(math.sin(a), math.cos(a), 0f));
        }

        // Far beyond every territory, so the wild never engages.
        private float3 FarPlayer => _planet.ToWorld(AtMeters(300f), 0f);

        private (Encounter, EncounterUnit) Lone(Habit habit, ulong key) => Lone(TestWild.Niche(habit), habit, key);

        private (Encounter, EncounterUnit) Lone(NicheDef niche, Habit habit, ulong key)
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            var ind = new WildIndividual(niche, habit, TestWild.SpeedPermille, TestWild.MoveMs, TestWild.PauseMs, key);
            EncounterUnit wild = e.AddWild(Creature("w", habit), null, AtMeters(0f), 10, ind);
            return (e, wild);
        }

        private float MaxStep(EncounterUnit wild)
            => _balance.Stats.MoveSpeedMmS(wild.Speed) * 0.001f * TestWild.SpeedPermille / 1000f * Dt * 0.001f;

        [Test]
        public void Wander_radius_reads_the_individuals_niche()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit bounded = e.AddWild(Creature("b", Habit.Tolerant), null, AtMeters(0f), 10,
                new WildIndividual(TestWild.Niche(Habit.Tolerant, WanderMode.Loop, 17000), Habit.Tolerant,
                    TestWild.SpeedPermille, TestWild.MoveMs, TestWild.PauseMs, 0));
            EncounterUnit free = e.AddWild(Creature("u", Habit.Tolerant), null, AtMeters(60f), 10,
                new WildIndividual(TestWild.Niche(Habit.Tolerant, WanderMode.UnboundedRandom, 9000), Habit.Tolerant,
                    TestWild.SpeedPermille, TestWild.MoveMs, TestWild.PauseMs, 1));
            Assert.That(e.WanderRadiusM(bounded.Id), Is.EqualTo(17f).Within(1e-5f));
            Assert.That(e.WanderRadiusM(free.Id), Is.EqualTo(0f), "unbounded niches have no home range");
            Assert.Throws<ContractException>(() => e.WanderRadiusM(99));
        }

        [Test]
        public void AddWild_requires_an_individual_and_AddCreature_rejects_wild()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            CreatureDef def = Creature("x", Habit.Tolerant);
            Assert.Throws<ContractException>(() => e.AddWild(def, null, AtMeters(0f), 10, null));
            Assert.Throws<ContractException>(() => e.AddBoss(def, null, AtMeters(0f), 10, null));
            Assert.Throws<ContractException>(() => e.AddCreature(EncounterSide.Wild, def, null, AtMeters(0f), 10));
        }

        [Test]
        public void A_fresh_wild_idles_for_its_pause_first()
        {
            (Encounter e, EncounterUnit wild) = Lone(Habit.Tolerant, 7);
            float3 start = wild.Position;
            for (long now = Dt; now <= TestWild.PauseMs; now += Dt)
            {
                e.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                Assert.That(wild.Position, Is.EqualTo(start), $"t={now}");
            }
        }

        [TestCase(WanderMode.BoundedRandom)]
        [TestCase(WanderMode.Shuttle)]
        [TestCase(WanderMode.Loop)]
        public void A_bounded_wild_wanders_but_never_leaves_its_cap(WanderMode mode)
        {
            (Encounter e, EncounterUnit wild) = Lone(TestWild.Niche(Habit.Tolerant, mode, 8000), Habit.Tolerant, 11);
            float radius = e.WanderRadiusM(wild.Id);
            float maxStep = MaxStep(wild);
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

            Assert.That(farthest, Is.GreaterThan(1f), "it actually moved");
        }

        [TestCase(WanderMode.Shuttle)]
        [TestCase(WanderMode.Loop)]
        public void A_route_wild_only_heads_for_its_waypoints(WanderMode mode)
        {
            (Encounter e, EncounterUnit wild) = Lone(TestWild.Niche(Habit.Tolerant, mode, 10000), Habit.Tolerant, 5);
            var targets = new HashSet<float3>();
            for (long now = Dt; now <= 240_000; now += Dt)
            {
                e.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                if (wild.HasWanderTarget)
                {
                    targets.Add(wild.WanderTarget);
                }
            }

            Assert.That(wild.Route, Is.Not.Null);
            Assert.That(wild.Route.Length, Is.EqualTo(3), "Home plus two seeded waypoints on open land");
            Assert.That(targets, Is.SubsetOf(wild.Route), "every leg heads for a route waypoint");
            Assert.That(targets.Count, Is.GreaterThanOrEqualTo(2), "it walks the route, not one point");
        }

        [Test]
        public void An_unbounded_wild_has_no_home_range()
        {
            (Encounter e, EncounterUnit wild) = Lone(TestWild.Niche(Habit.Tolerant, WanderMode.UnboundedRandom, 5000),
                Habit.Tolerant, 3);
            float farthest = 0f;
            for (long now = Dt; now <= 600_000; now += Dt)
            {
                e.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                farthest = math.max(farthest, math.distance(wild.Position, wild.Home));
            }

            Assert.That(farthest, Is.GreaterThan(30f), "a random walk with no home drifts past any bounded radius");
        }

        [Test]
        public void Wandering_is_reproducible_per_individual_key()
        {
            (Encounter a, EncounterUnit wa) = Lone(Habit.Aggressive, 42);
            (Encounter b, EncounterUnit wb) = Lone(Habit.Aggressive, 42);
            (Encounter c, EncounterUnit wc) = Lone(Habit.Aggressive, 43);
            bool diverged = false;
            for (long now = Dt; now <= 60_000; now += Dt)
            {
                a.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                b.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                c.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                Assert.That(wb.Position, Is.EqualTo(wa.Position), $"t={now}");
                diverged |= math.any(wc.Position != wa.Position);
            }

            Assert.That(diverged, Is.True, "another individual key takes another path");
        }

        [Test]
        public void One_units_wander_does_not_shift_anothers()
        {
            var lone = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit solo = lone.AddWild(Creature("s", Habit.Tolerant), null, AtMeters(0f), 10,
                TestWild.Individual(Habit.Tolerant, 77));
            var crowd = new Encounter(_planet, _balance, new FlatSurface(), 1);
            crowd.AddWild(Creature("n", Habit.Tolerant), null, AtMeters(100f), 10, TestWild.Individual(Habit.Tolerant, 78));
            EncounterUnit same = crowd.AddWild(Creature("s2", Habit.Tolerant), null, AtMeters(0f), 10,
                TestWild.Individual(Habit.Tolerant, 77));
            for (long now = Dt; now <= 60_000; now += Dt)
            {
                lone.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                crowd.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                Assert.That(same.Position, Is.EqualTo(solo.Position), $"t={now}");
            }
        }

        // SOL W-B2-SOL-05: self-replay cannot tell the stream id apart from any other. Literals come from an independent
        // PCG32 (XSH-RR, stream << 1 | 1) reimplementation in Python, not from this codebase: seed = StreamKey, stream 'W'.
        [TestCase(5UL, 3330189048u, 2709907303u, 3518766685u)]
        [TestCase(42UL, 3744784469u, 971515108u, 15015028u)]
        public void Wander_stream_is_the_individuals_key_and_W(ulong key, uint first, uint second, uint third)
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 999);
            EncounterUnit u = e.AddWild(Creature("k", Habit.Tolerant), null, AtMeters(0f), 10,
                TestWild.Individual(Habit.Tolerant, key));
            Pcg32 rng = e.WanderRngForTests(u.Id);
            Assert.That(new[] { rng.NextUInt(), rng.NextUInt(), rng.NextUInt() }, Is.EqualTo(new[] { first, second, third }));
        }

        [Test]
        public void Wandering_does_not_consume_the_combat_rng()
        {
            (Encounter e, EncounterUnit _) = Lone(Habit.Dominant, 5);
            Pcg32 before = e.RngForTests;
            for (long now = Dt; now <= 30_000; now += Dt)
            {
                e.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
            }

            Pcg32 after = e.RngForTests;
            Assert.That(after.NextUInt(), Is.EqualTo(before.NextUInt()));
        }

        [Test]
        public void A_bounded_wild_outside_its_cap_walks_home_without_pausing()
        {
            (Encounter e, EncounterUnit wild) = Lone(Habit.Tolerant, 3);
            float radius = e.WanderRadiusM(wild.Id);
            // Stand-in for where a fight left it: 25 m from home, past the 6 m test niche radius.
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
        public void An_unbounded_wild_does_not_walk_home_after_a_fight()
        {
            (Encounter e, EncounterUnit wild) = Lone(TestWild.Niche(Habit.Tolerant, WanderMode.UnboundedRandom, 5000),
                Habit.Tolerant, 3);
            wild.Position = _planet.ToWorld(AtMeters(25f), wild.BodyRadiusM);
            float3 left = wild.Position;
            for (long now = Dt; now <= TestWild.PauseMs; now += Dt)
            {
                e.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                Assert.That(wild.Position, Is.EqualTo(left), $"t={now}: pauses where the fight left it");
            }
        }

        [Test]
        public void Engaging_cancels_the_current_leg()
        {
            (Encounter e, EncounterUnit wild) = Lone(Habit.Tolerant, 9);
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
