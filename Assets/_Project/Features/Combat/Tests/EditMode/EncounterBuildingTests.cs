using System.Collections.Generic;
using System.IO;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/planet-world.md#道路與建築 (user 2026-11-11): buildings collide with the character and creatures.
    public sealed class EncounterBuildingTests
    {
        // A box footprint in (along, lateral) arc metres: along = atan2(x, y)·R on the x–y great circle, lateral = asin(z)·R.
        private sealed class Wall : IEncounterSurface
        {
            private readonly float _fromM;
            private readonly float _toM;
            private readonly float _halfM;

            public Wall(float fromM, float toM, float halfM)
            {
                _fromM = fromM;
                _toM = toM;
                _halfM = halfM;
            }

            public float GroundAltitude(float3 direction) => 0f;

            public bool IsSea(float3 direction) => false;

            public bool IsBlocked(float3 direction, float clearanceM)
            {
                float m = math.atan2(direction.x, direction.y) * R;
                float lat = math.asin(math.clamp(direction.z, -1f, 1f)) * R;
                return m >= _fromM - clearanceM && m < _toM + clearanceM && math.abs(lat) <= _halfM + clearanceM;
            }
        }

        private const float R = 640f;
        private const int Dt = 50;
        // Float positions at R = 640 m are good to ~6e-5 m; 5 mm leaves room for re-grounding.
        private const float TolM = 0.005f;
        private static readonly AiCommand Free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
        private readonly List<Object> _created = new List<Object>();
        private PlanetConfig _planet;
        private CombatBalance _balance;

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

        private CreatureDef Creature(string id, Temperament temperament, MoveShape shape = MoveShape.Front,
            MoveMovement movement = MoveMovement.None)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId("move_" + id);
            m.Set(Element.Fire, MoveKind.Damage, 40, null, 0, 0, 1500, 100, RangeClass.Melee, shape, 2000, movement,
                BurstOrigin.Caster);
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            c.Set("line_" + id, CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Charge, temperament, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(25, 25, 25, 25), m, null, "{}", false);
            return c;
        }

        private static float3 At(float alongM, float lateralM = 0f)
        {
            float a = alongM / R;
            float l = lateralM / R;
            return math.normalize(new float3(math.sin(a) * math.cos(l), math.cos(a) * math.cos(l), math.sin(l)));
        }

        private float AlongOf(float3 position)
        {
            float3 d = _planet.ToDirection(position);
            return math.atan2(d.x, d.y) * R;
        }

        private float LateralOf(float3 position) => math.asin(math.clamp(_planet.ToDirection(position).z, -1f, 1f)) * R;

        private bool Inside(Wall wall, EncounterUnit u) =>
            wall.IsBlocked(_planet.ToDirection(u.Position), math.max(0f, u.CollisionRadiusM - TolM));

        private Encounter Engaged(IEncounterSurface surface, CreatureDef def, float3 start, out EncounterUnit wild)
        {
            var e = new Encounter(_planet, _balance, surface, 3);
            wild = e.AddCreature(EncounterSide.Wild, def, null, start, 10);
            // Without vitals the character is not a target and the engaged wild would just hold position.
            e.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(100000, 600, 400, 800)));
            e.Provoke(wild.Id);
            return e;
        }

        [Test]
        public void A_charging_wild_never_walks_into_a_wall()
        {
            // A wall across the whole way (±50 m) between the wild at 0 m and the character at 10 m.
            var wall = new Wall(3f, 5f, 50f);
            Encounter e = Engaged(wall, Creature("w", Temperament.Apex), At(0f), out EncounterUnit wild);
            float3 player = _planet.ToWorld(At(10f), 0f);
            float closest = 0f;
            for (long now = Dt; now <= 10_000; now += Dt)
            {
                e.Tick(now, Dt, player, Free, CombatAi.NoTarget);
                Assert.That(Inside(wall, wild), Is.False, $"t={now}");
                closest = math.max(closest, AlongOf(wild.Position));
            }

            Assert.That(closest, Is.GreaterThan(3f - wild.CollisionRadiusM - 0.5f), "it did charge up to the wall");
        }

        [Test]
        public void A_charging_wild_slides_around_a_house()
        {
            // A 4 m-wide house between the wild at 0 m and the character at 10 m.
            var wall = new Wall(3f, 6f, 2f);
            Encounter e = Engaged(wall, Creature("w", Temperament.Apex), At(0f), out EncounterUnit wild);
            float3 player = _planet.ToWorld(At(10f), 0f);
            bool passed = false;
            for (long now = Dt; now <= 20_000 && !passed; now += Dt)
            {
                e.Tick(now, Dt, player, Free, CombatAi.NoTarget);
                Assert.That(Inside(wall, wild), Is.False, $"t={now}");
                passed = AlongOf(wild.Position) > 6f;
            }

            Assert.That(passed, Is.True, "it got past the house instead of pinning itself on the near wall");
        }

        [Test]
        public void A_wild_that_starts_inside_a_building_can_walk_out()
        {
            var wall = new Wall(-2f, 2f, 2f);
            Encounter e = Engaged(wall, Creature("w", Temperament.Apex), At(0f), out EncounterUnit wild);
            float3 player = _planet.ToWorld(At(10f), 0f);
            for (long now = Dt; now <= 10_000; now += Dt)
            {
                e.Tick(now, Dt, player, Free, CombatAi.NoTarget);
            }

            Assert.That(Inside(wall, wild), Is.False);
            Assert.That(AlongOf(wild.Position), Is.GreaterThan(2f));
        }

        [Test]
        public void A_wandering_wild_never_enters_a_building()
        {
            // Apex wanders 12 m around home; a 6 × 6 m house sits 2 m east of it.
            var wall = new Wall(2f, 8f, 3f);
            var e = new Encounter(_planet, _balance, wall, 21);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", Temperament.Apex), null, At(0f), 10);
            float3 far = _planet.ToWorld(At(-300f), 0f);
            float3 start = wild.Position;
            bool moved = false;
            for (long now = Dt; now <= 120_000; now += Dt)
            {
                e.Tick(now, Dt, far, Free, CombatAi.NoTarget);
                Assert.That(Inside(wall, wild), Is.False, $"t={now}");
                moved |= math.distance(wild.Position, start) > 1f;
            }

            Assert.That(moved, Is.True);
        }

        [Test]
        public void A_dash_never_ends_inside_a_building()
        {
            float RunDash(IEncounterSurface surface, out bool cast)
            {
                Encounter e = Engaged(surface, Creature("d", Temperament.Apex, MoveShape.Line, MoveMovement.Dash), At(0f),
                    out EncounterUnit wild);
                float3 player = _planet.ToWorld(At(1f), 0f);
                cast = false;
                float maxM = AlongOf(wild.Position);
                for (long now = Dt; now <= 2_000; now += Dt)
                {
                    e.Tick(now, Dt, player, Free, CombatAi.NoTarget);
                    foreach (EncounterEvent ev in e.Events)
                    {
                        cast |= ev.Kind == EncounterEventKind.Cast && ev.SourceId == wild.Id;
                    }

                    maxM = math.max(maxM, AlongOf(wild.Position));
                }

                return maxM;
            }

            float open = RunDash(new Wall(1000f, 1000f, 0f), out bool openCast);
            Assert.That(openCast, Is.True);
            Assert.That(open, Is.GreaterThan(1.5f), "without the wall the dash crosses 1.5 m");

            // Wall face at 2.0 m; grown by the 0.35 m-ish body it starts near 1.6 m.
            var wall = new Wall(2f, 1000f, 50f);
            float walled = RunDash(wall, out bool walledCast);
            Assert.That(walledCast, Is.True, "the wild still casts; only the displacement is withheld");
            Assert.That(walled, Is.LessThan(2f));
        }

        [Test]
        public void Lateral_slide_keeps_the_wall_side()
        {
            // Wall face across the path; the slide moves along the face, never through it.
            var wall = new Wall(3f, 5f, 50f);
            Encounter e = Engaged(wall, Creature("w", Temperament.Apex), At(0f), out EncounterUnit wild);
            float3 player = _planet.ToWorld(At(10f, 1f), 0f);
            for (long now = Dt; now <= 5_000; now += Dt)
            {
                e.Tick(now, Dt, player, Free, CombatAi.NoTarget);
                Assert.That(AlongOf(wild.Position), Is.LessThan(3f - wild.CollisionRadiusM + TolM), $"t={now}");
                Assert.That(math.abs(LateralOf(wild.Position)), Is.LessThan(50f));
            }
        }
    }
}
