using System.Collections.Generic;
using System.IO;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/combat.md#交戰上限 (Q-C63) — at most two wild units fight one player; bosses are exempt.
    public sealed class EncounterEngageCapTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;

            public bool IsSea(float3 direction) => false;

            public bool IsBlocked(float3 direction, float clearanceM) => false;
        }

        private const float R = 640f;
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
            m.Set(Element.Fire, MoveKind.Damage, 1, null, 0, 0, 60000, 100, RangeClass.Melee, MoveShape.Front, 500,
                MoveMovement.None, BurstOrigin.Caster);
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            TestWild.WithHabit(c, habit).Set("line_" + id, CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Hold, null, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(78, 25, 25, 25, 25, 25), m, null, "{}", false);
            return c;
        }

        private static float3 AtMeters(float meters)
        {
            float a = meters / R;
            return math.normalize(new float3(math.sin(a), math.cos(a), 0f));
        }

        private float3 Feet(float meters) => _planet.ToWorld(AtMeters(meters), 0f);

        private Encounter Three(out EncounterUnit near, out EncounterUnit mid, out EncounterUnit far)
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            // Tolerant: 8 m territory. Player at 0 m: all three are inside it, at 3, 5 and 7 m.
            far = e.AddWildT(Creature("far", Habit.Tolerant), null, AtMeters(7f), 10);
            near = e.AddWildT(Creature("near", Habit.Tolerant), null, AtMeters(3f), 10);
            mid = e.AddWildT(Creature("mid", Habit.Tolerant), null, AtMeters(5f), 10);
            return e;
        }

        [Test]
        public void Only_the_two_nearest_wilds_engage()
        {
            Encounter e = Three(out EncounterUnit near, out EncounterUnit mid, out EncounterUnit far);
            e.Tick(50, 50, Feet(0f), Free, CombatAi.NoTarget);
            Assert.That(near.Engaged, Is.True);
            Assert.That(mid.Engaged, Is.True);
            Assert.That(far.Engaged, Is.False, "the third waits even inside its territory");
            Assert.That(Encounter.WildEngageCapPerPlayer, Is.EqualTo(2));
        }

        [Test]
        public void A_waiting_wild_fills_the_slot_a_removed_one_frees()
        {
            Encounter e = Three(out EncounterUnit near, out EncounterUnit mid, out EncounterUnit far);
            e.Tick(50, 50, Feet(0f), Free, CombatAi.NoTarget);
            e.Remove(near.Id);
            e.Tick(100, 50, Feet(0f), Free, CombatAi.NoTarget);
            Assert.That(mid.Engaged, Is.True);
            Assert.That(far.Engaged, Is.True, "captured / removed frees a slot for the waiting unit");
        }

        [Test]
        public void Provoking_a_third_wild_does_not_add_it_while_full()
        {
            Encounter e = Three(out EncounterUnit near, out EncounterUnit mid, out EncounterUnit far);
            e.Tick(50, 50, Feet(0f), Free, CombatAi.NoTarget);
            e.Provoke(far.Id);
            e.Tick(100, 50, Feet(0f), Free, CombatAi.NoTarget);
            Assert.That(far.Engaged, Is.False);
            Assert.That(e.NearestInteractable(Feet(7f)), Is.Not.EqualTo(far.Id), "a waiting unit is not idle");
        }

        [Test]
        public void A_waiting_wild_gives_up_when_the_intruders_leave()
        {
            Encounter e = Three(out EncounterUnit near, out EncounterUnit mid, out EncounterUnit far);
            e.Tick(50, 50, Feet(0f), Free, CombatAi.NoTarget);
            e.Tick(100, 50, Feet(300f), Free, CombatAi.NoTarget);
            Assert.That(near.Engaged || mid.Engaged || far.Engaged, Is.False);
            e.Tick(150, 50, Feet(300f), Free, CombatAi.NoTarget);
            Assert.That(far.Engaged, Is.False, "the queued wish does not survive the player leaving");
        }

        [Test]
        public void A_boss_fight_lifts_the_cap()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit boss = e.AddBossT(Creature("boss", Habit.Tolerant), null, AtMeters(2f), 10);
            EncounterUnit a = e.AddWildT(Creature("a", Habit.Tolerant), null, AtMeters(4f), 10);
            EncounterUnit b = e.AddWildT(Creature("b", Habit.Tolerant), null, AtMeters(5f), 10);
            EncounterUnit c = e.AddWildT(Creature("c", Habit.Tolerant), null, AtMeters(6f), 10);
            e.Tick(50, 50, Feet(0f), Free, CombatAi.NoTarget);
            Assert.That(boss.Engaged && a.Engaged && b.Engaged && c.Engaged, Is.True, "boss takes no slot and lifts the cap");
        }
    }
}
