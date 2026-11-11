using System.Collections.Generic;
using System.IO;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/combat.md#近身互動 (D0-W3, decided 2026-11-10; reach confirmed, Q-D2).
    public sealed class EncounterInteractTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;

            public bool IsSea(float3 direction) => false;
        }

        private const float R = 640f;
        private readonly List<Object> _created = new List<Object>();
        private PlanetConfig _planet;
        private CombatBalance _balance;
        private float _character;

        [SetUp]
        public void SetUp()
        {
            _planet = new PlanetConfig(R, float3.zero);
            _balance = CombatBalance.FromJson(File.ReadAllText(BalanceFixture.RepoPath("Data", "balance.json")));
            _character = _balance.Collision.CharacterMm * 0.001f;
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

        private float3 Feet => _planet.ToWorld(AtMeters(0f), 0f);

        // Arc from the feet to a unit whose body edge sits `gap` metres from the character's edge.
        private float AtGap(EncounterUnitProbe probe, float gap) => gap + probe.Radius + _character;

        private readonly struct EncounterUnitProbe
        {
            public EncounterUnitProbe(float radius) => Radius = radius;
            public float Radius { get; }
        }

        private EncounterUnitProbe MProbe => new EncounterUnitProbe(_balance.Collision.BodyMm(CreatureSizeClass.M) * 0.001f);

        [Test]
        public void Reach_is_measured_between_body_edges()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit inside = e.AddCreature(EncounterSide.Wild, Creature("in", Temperament.Timid), null,
                AtMeters(AtGap(MProbe, Encounter.InteractRadiusM - 0.05f)), 10);
            Assert.That(e.NearestInteractable(Feet), Is.EqualTo(inside.Id));

            var far = new Encounter(_planet, _balance, new FlatSurface(), 1);
            far.AddCreature(EncounterSide.Wild, Creature("out", Temperament.Timid), null,
                AtMeters(AtGap(MProbe, Encounter.InteractRadiusM + 0.05f)), 10);
            Assert.That(far.NearestInteractable(Feet), Is.EqualTo(-1));
        }

        [Test]
        public void Nearest_wins_and_ties_go_to_the_lower_id()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            e.AddCreature(EncounterSide.Wild, Creature("far", Temperament.Timid), null, AtMeters(AtGap(MProbe, 2f)), 10);
            EncounterUnit near = e.AddCreature(EncounterSide.Wild, Creature("near", Temperament.Timid), null,
                AtMeters(AtGap(MProbe, 1f)), 10);
            Assert.That(e.NearestInteractable(Feet), Is.EqualTo(near.Id));

            var tie = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit first = tie.AddCreature(EncounterSide.Wild, Creature("a", Temperament.Timid), null,
                AtMeters(AtGap(MProbe, 1f)), 10);
            tie.AddCreature(EncounterSide.Wild, Creature("b", Temperament.Timid), null, AtMeters(-AtGap(MProbe, 1f)), 10);
            Assert.That(tie.NearestInteractable(Feet), Is.EqualTo(first.Id));
        }

        [Test]
        public void Engaged_provoked_and_player_side_units_are_not_offered()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", Temperament.Timid), null,
                AtMeters(AtGap(MProbe, 1f)), 10);
            EncounterUnit pet = e.AddCreature(EncounterSide.Player, Creature("p", Temperament.Timid), null,
                AtMeters(-AtGap(MProbe, 0.5f)), 10);
            e.Field(pet.Id, AtMeters(-AtGap(MProbe, 0.5f)), false, 0);
            Assert.That(e.NearestInteractable(Feet), Is.EqualTo(wild.Id), "own creature is never offered");

            e.Provoke(wild.Id);
            Assert.That(e.NearestInteractable(Feet), Is.EqualTo(-1), "provoked: fight pending");

            e.Tick(50, 50, Feet, new AiCommand(AiCommandKind.Free, CombatAi.NoTarget), pet.Id);
            Assert.That(wild.Engaged, Is.True);
            Assert.That(e.NearestInteractable(Feet), Is.EqualTo(-1), "engaged");
        }

        [Test]
        public void Timid_and_apex_are_both_offered_while_idle()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit apex = e.AddCreature(EncounterSide.Wild, Creature("apex", Temperament.Apex), null,
                AtMeters(AtGap(MProbe, 1f)), 10);
            Assert.That(e.NearestInteractable(Feet), Is.EqualTo(apex.Id), "idle until the first Tick decides engagement");
        }
    }
}
