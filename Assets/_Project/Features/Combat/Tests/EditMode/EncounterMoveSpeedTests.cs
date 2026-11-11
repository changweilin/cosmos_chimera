using System.Collections.Generic;
using System.IO;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // SOL W-B2-SOL-03: the Combatant.MoveSpeedMmS API tests stayed green when Encounter.Steer reverted to the static
    // AGI value; these pin the wiring by measuring actual displacement in combat and while wandering.
    public sealed class EncounterMoveSpeedTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;

            public bool IsSea(float3 direction) => false;
        }

        private const float R = 640f;
        private const int Dt = 50;
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

        private CreatureDef Creature(Temperament temperament)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId("bite_ms");
            m.Set(Element.Fire, MoveKind.Damage, 40, null, 0, 0, 1500, 100, RangeClass.Melee, MoveShape.Front, 2000,
                MoveMovement.None, BurstOrigin.Caster);
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId("ms");
            c.Set("line_ms", CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Charge, temperament, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(25, 25, 25, 25), m, null, "{}", false);
            return c;
        }

        private static float3 AtMeters(float meters)
        {
            float a = meters / R;
            return math.normalize(new float3(math.sin(a), math.cos(a), 0f));
        }

        private static EffectSpec Speed(int percent) =>
            new EffectSpec(EffectType.MoveSpeedModPercent, false, Element.Fire, percent, 0, 1);

        // Per-tick displacement of an apex wild charging the character 25 m away (inside the 28 m apex territory); the
        // modifier (if any) lasts until durationMs. Returns the step of each tick from 1 to ticks.
        private float[] ChargeSteps(int percent, int durationMs, int ticks)
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 3);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature(Temperament.Apex), null, AtMeters(0f), 10);
            e.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(100000, 600, 400, 800)));
            if (percent != 0)
            {
                wild.Combatant.Book.Apply("ms", 0, Speed(percent), durationMs, 0);
            }

            float3 player = _planet.ToWorld(AtMeters(25f), 0f);
            var steps = new float[ticks];
            for (int k = 0; k < ticks; k++)
            {
                float3 before = wild.Position;
                e.Tick((k + 1) * Dt, Dt, player, Free, CombatAi.NoTarget);
                steps[k] = math.distance(before, wild.Position);
            }

            return steps;
        }

        [Test]
        public void A_speed_effect_changes_how_far_a_charging_wild_moves_until_it_expires()
        {
            float[] plain = ChargeSteps(0, 0, 30);
            float[] fast = ChargeSteps(100, 1000, 30);

            // Pick a tick where the plain wild is already walking and still far from the character.
            int walking = System.Array.FindIndex(plain, s => s > 0.05f);
            Assert.That(walking, Is.GreaterThanOrEqualTo(0).And.LessThan(5), "the plain wild charges");
            Assert.That(fast[walking] / plain[walking], Is.EqualTo(2f).Within(0.05f), "+100 % doubles the step");
            // Tick 25 is at 1250 ms, after the 1000 ms effect expired; the fast wild is closer but not yet in reach.
            Assert.That(fast[24], Is.EqualTo(plain[walking]).Within(0.01f), "the expired effect no longer applies");
        }

        [Test]
        public void A_speed_effect_changes_the_wandering_pace()
        {
            float MaxStep(int percent)
            {
                var e = new Encounter(_planet, _balance, new FlatSurface(), 21);
                EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature(Temperament.Timid), null, AtMeters(0f),
                    10);
                if (percent != 0)
                {
                    wild.Combatant.Book.Apply("ms", 0, Speed(percent), 600_000, 0);
                }

                float3 far = _planet.ToWorld(AtMeters(-300f), 0f);
                float max = 0f;
                for (long now = Dt; now <= 60_000; now += Dt)
                {
                    float3 before = wild.Position;
                    e.Tick(now, Dt, far, Free, CombatAi.NoTarget);
                    max = math.max(max, math.distance(before, wild.Position));
                }

                return max;
            }

            float plain = MaxStep(0);
            float slow = MaxStep(-50);
            Assert.That(plain, Is.GreaterThan(0f), "the timid wild wanders within a minute");
            Assert.That(slow / plain, Is.EqualTo(0.5f).Within(0.02f));
        }
    }
}
