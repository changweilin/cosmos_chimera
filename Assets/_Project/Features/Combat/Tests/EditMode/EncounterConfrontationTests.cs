using System.Collections.Generic;
using System.IO;
using Cosmimera.GameData;
using Cosmimera.Planet;
using Cosmimera.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/creature-ecology.md#棲位食物鏈階級與野生對抗
    public sealed class EncounterConfrontationTests
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
        private NicheDef _basalNiche;
        private NicheDef _apexNiche;
        private static readonly AiCommand Free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);

        [SetUp]
        public void SetUp()
        {
            _planet = new PlanetConfig(R, float3.zero);
            _balance = CombatBalance.FromJson(File.ReadAllText(BalanceFixture.RepoPath("Data", "balance.json")));
            var habits = new List<HabitWeight> { new HabitWeight(Habit.Aggressive, 1) };
            var habitats = new List<string> { "grassland" };
            var dist = new WanderDistribution(300, 0, 0, 100, 600);
            _basalNiche = ScriptableObject.CreateInstance<NicheDef>();
            _basalNiche.SetId("test_basal");
            _basalNiche.Set("Basal", habitats, habits, WanderMode.BoundedRandom, 10000, 0, dist, dist, dist, 1, TrophicGroup.FoodWeb);
            _apexNiche = ScriptableObject.CreateInstance<NicheDef>();
            _apexNiche.SetId("test_apex");
            _apexNiche.Set("Apex", habitats, habits, WanderMode.BoundedRandom, 10000, 0, dist, dist, dist, 4, TrophicGroup.FoodWeb);
            _created.Add(_basalNiche);
            _created.Add(_apexNiche);
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

        private CreatureDef Creature(string id, int power = 100)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId("hit_" + id);
            m.Set(Element.Fire, MoveKind.Damage, power, null, 0, 0, 500, 100, RangeClass.Melee, MoveShape.Front, 50,
                MoveMovement.None, BurstOrigin.Caster);
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            c.Set("line_" + id, CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Charge, null, null, 0,
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

        private WildIndividual Individual(NicheDef niche, ulong key)
            => new WildIndividual(niche, Habit.Aggressive, 300, 500, 500, key);

        [Test]
        public void Standoff_units_do_not_aggro_by_territory_and_remain_interactable()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            int cid = e.RegisterConfrontation(ConfrontationKind.SameNiche, AtMeters(2f));
            EncounterUnit a = e.AddWildInConfrontation(Creature("a"), null, AtMeters(1f), 10, Individual(_basalNiche, 1), cid, 0, 0);
            EncounterUnit b = e.AddWildInConfrontation(Creature("b"), null, AtMeters(3f), 10, Individual(_basalNiche, 2), cid, 1, 0);

            e.Tick(50, 50, Feet(1f), Free, CombatAi.NoTarget);

            Assert.That(a.Engaged, Is.False);
            Assert.That(b.Engaged, Is.False);
            Assert.That(e.IsConfrontationStandoff(a.Id), Is.True);
            Assert.That(e.NearestInteractable(Feet(1f)), Is.EqualTo(a.Id));
        }

        [Test]
        public void Joining_confrontation_caps_player_to_one_and_fields_1_ally_vs_2_enemies_with_relays()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit p1 = e.AddCreature(EncounterSide.Player, Creature("p1", 5000), null, AtMeters(0f), 10);
            EncounterUnit p2 = e.AddCreature(EncounterSide.Player, Creature("p2", 5000), null, AtMeters(0.5f), 10);
            e.Field(p1.Id, AtMeters(0f), false, 0);
            e.Field(p2.Id, AtMeters(0.5f), false, 0);

            int cid = e.RegisterConfrontation(ConfrontationKind.SameNiche, AtMeters(4f));
            CreatureDef allyDef = Creature("ally", 1);
            CreatureDef enemyDef = Creature("enemy", 1);
            EncounterUnit a0 = e.AddWildInConfrontation(allyDef, null, AtMeters(2f), 10, Individual(_basalNiche, 1), cid, 0, 0);
            EncounterUnit a1 = e.AddWildInConfrontation(allyDef, null, AtMeters(1.5f), 10, Individual(_basalNiche, 2), cid, 0, 1);
            EncounterUnit e0 = e.AddWildInConfrontation(enemyDef, null, AtMeters(5f), 10, Individual(_basalNiche, 3), cid, 1, 0);
            EncounterUnit e1 = e.AddWildInConfrontation(enemyDef, null, AtMeters(5.5f), 10, Individual(_basalNiche, 4), cid, 1, 1);
            EncounterUnit e2 = e.AddWildInConfrontation(enemyDef, null, AtMeters(6f), 10, Individual(_basalNiche, 5), cid, 1, 2);

            Assert.That(e.JoinConfrontation(a0.Id), Is.True);
            Assert.That(e.HasActiveConfrontation, Is.True);
            Assert.That(e.PlayerFieldCap, Is.EqualTo(1));
            Assert.That(p1.Fielded, Is.True);
            Assert.That(p2.Fielded, Is.False, "second player creature recalled for 1+1 vs 2");
            Assert.That(a0.Fielded && a0.IsConfrontationAlly, Is.True);
            Assert.That(a1.Fielded, Is.False, "ally reserve waits off-field");
            Assert.That(e0.Fielded && e0.Engaged, Is.True);
            Assert.That(e1.Fielded && e1.Engaged, Is.True);
            Assert.That(e2.Fielded, Is.False, "3rd enemy waits as reserve");

            // Knock out first enemy -> 3rd enemy relays in.
            p1.Combatant.BeginCast(0, p1.Moves[0], 0, out CastContext ctx);
            e0.Combatant.ReceiveReflect(in ctx, 1000, 10);
            e.Tick(50, 50, Feet(0f), Free, p1.Id);
            Assert.That(e2.Fielded && e2.Engaged, Is.True, "enemy reserve relays in when e0 faints");
            Assert.That(e.HasActiveConfrontation, Is.True);

            // Knock out remaining enemies -> confrontation resolves and ally becomes friendly.
            e1.Combatant.ReceiveReflect(in ctx, 1000, 20);
            e2.Combatant.ReceiveReflect(in ctx, 1000, 30);
            e.Tick(100, 50, Feet(0f), Free, p1.Id);
            Assert.That(e.HasActiveConfrontation, Is.False);
            Assert.That(e.PlayerFieldCap, Is.EqualTo(2));
            Assert.That(a0.IsFriendlyAfterConfrontation, Is.True);
            Assert.That(a0.IsConfrontationAlly, Is.False);
        }

        [Test]
        public void Provoking_standoff_unit_joins_opposing_side()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            int cid = e.RegisterConfrontation(ConfrontationKind.Trophic, AtMeters(3f));
            EncounterUnit upper = e.AddWildInConfrontation(Creature("upper"), null, AtMeters(2f), 10, Individual(_apexNiche, 1), cid, 0, 0);
            EncounterUnit lower0 = e.AddWildInConfrontation(Creature("lower"), null, AtMeters(4f), 10, Individual(_basalNiche, 2), cid, 1, 0);
            EncounterUnit lower1 = e.AddWildInConfrontation(Creature("lower"), null, AtMeters(4.5f), 10, Individual(_basalNiche, 3), cid, 1, 1);

            e.Provoke(upper.Id);
            Assert.That(e.HasActiveConfrontation, Is.True);
            Assert.That(upper.Engaged, Is.True);
            Assert.That(lower0.IsConfrontationAlly, Is.True);
            Assert.That(lower1.IsConfrontationAlly && !lower1.Fielded, Is.True);
        }
    }
}
