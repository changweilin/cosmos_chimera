using System.Collections.Generic;
using System.IO;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/combat.md#只在陸地 (decided 2026-11-11): wild units never stand on the sea.
    public sealed class EncounterSeaTests
    {
        // Positions sit on the great circle x = sin(m/R), y = cos(m/R); the sea is the band of arc metres [from, to).
        private sealed class BandSea : IEncounterSurface
        {
            private readonly float _fromM;
            private readonly float _toM;

            public BandSea(float fromM, float toM)
            {
                _fromM = fromM;
                _toM = toM;
            }

            public float GroundAltitude(float3 direction) => 0f;

            public bool IsSea(float3 direction)
            {
                float m = math.atan2(direction.x, direction.y) * R;
                return m >= _fromM && m < _toM;
            }
        }

        private const float R = 640f;
        private const int Dt = 50;
        // Float world positions at R = 640 m are good to ~6e-5 m; 5 mm leaves room for re-grounding.
        private const float ArcTolM = 0.005f;
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

        private float MetersOf(float3 position)
        {
            float3 d = _planet.ToDirection(position);
            return math.atan2(d.x, d.y) * R;
        }

        private float3 FarPlayer => _planet.ToWorld(AtMeters(-300f), 0f);

        [Test]
        public void A_coastal_wild_wanders_but_never_onto_the_sea()
        {
            // Apex wanders 12 m; the sea starts 2 m east of home, so most of its disk is water.
            var sea = new BandSea(2f, 1000f);
            var e = new Encounter(_planet, _balance, sea, 21);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", Temperament.Apex), null, AtMeters(0f), 10);
            float3 start = wild.Position;
            bool moved = false;
            for (long now = Dt; now <= 120_000; now += Dt)
            {
                e.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
                Assert.That(sea.IsSea(_planet.ToDirection(wild.Position)), Is.False, $"t={now}");
                moved |= math.distance(wild.Position, start) > 1f;
            }

            Assert.That(moved, Is.True, "the land half of the disk is still wandered");
        }

        [Test]
        public void A_wild_whose_whole_disk_is_sea_stays_home()
        {
            // Land is a 2 cm strip around home: every 12 m draw (and every step off home) is sea.
            var sea = new BandSea(0.01f, 1000f);
            var e = new Encounter(_planet, _balance, new CompositeSea(sea), 5);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", Temperament.Apex), null, AtMeters(0f), 10);
            float3 start = wild.Position;
            for (long now = Dt; now <= 60_000; now += Dt)
            {
                e.Tick(now, Dt, FarPlayer, Free, CombatAi.NoTarget);
            }

            Assert.That(math.distance(wild.Position, start), Is.LessThan(ArcTolM));
        }

        // Sea on both sides of a 2 cm land strip around 0 m.
        private sealed class CompositeSea : IEncounterSurface
        {
            private readonly BandSea _east;

            public CompositeSea(BandSea east)
            {
                _east = east;
            }

            public float GroundAltitude(float3 direction) => 0f;

            public bool IsSea(float3 direction) =>
                _east.IsSea(direction) || _east.IsSea(new float3(-direction.x, direction.y, direction.z));
        }

        [Test]
        public void A_charging_wild_stops_at_the_shore()
        {
            // The character stands 10 m east, across a strait from 2 m to 8 m: inside apex territory, out of reach.
            var sea = new BandSea(2f, 8f);
            var e = new Encounter(_planet, _balance, sea, 3);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", Temperament.Apex), null, AtMeters(0f), 10);
            float3 player = _planet.ToWorld(AtMeters(10f), 0f);
            // Without vitals the character is not a target and the engaged wild would just hold position.
            e.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(100000, 600, 400, 800)));
            for (long now = Dt; now <= 20_000; now += Dt)
            {
                e.Tick(now, Dt, player, Free, CombatAi.NoTarget);
                Assert.That(MetersOf(wild.Position), Is.LessThan(2f), $"t={now}");
            }

            Assert.That(wild.Engaged, Is.True);
            Assert.That(MetersOf(wild.Position), Is.GreaterThan(1f), "it did charge up to the shore");
        }

        private CreatureDef Dasher()
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId("dash_d");
            m.Set(Element.Fire, MoveKind.Damage, 40, null, 0, 0, 1500, 100, RangeClass.Melee, MoveShape.Line, 2000,
                MoveMovement.Dash, BurstOrigin.Caster);
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId("d");
            c.Set("line_d", CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Charge, Temperament.Apex, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(25, 25, 25, 25), m, null, "{}", false);
            return c;
        }

        // SOL W-B2-SOL-02: TryCast wrote the dash end straight to the position, bypassing the sea check.
        [Test]
        public void A_wild_dash_never_ends_on_the_sea()
        {
            // The character stands 1 m east; a 2 m dash from 0 m ends near 2 m.
            float RunDash(IEncounterSurface surface, out bool cast)
            {
                var e = new Encounter(_planet, _balance, surface, 3);
                EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Dasher(), null, AtMeters(0f), 10);
                e.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(100000, 600, 400, 800)));
                e.Provoke(wild.Id);
                float3 player = _planet.ToWorld(AtMeters(1f), 0f);
                cast = false;
                float maxM = MetersOf(wild.Position);
                for (long now = Dt; now <= 2_000; now += Dt)
                {
                    e.Tick(now, Dt, player, Free, CombatAi.NoTarget);
                    foreach (EncounterEvent ev in e.Events)
                    {
                        cast |= ev.Kind == EncounterEventKind.Cast && ev.SourceId == wild.Id;
                    }

                    maxM = math.max(maxM, MetersOf(wild.Position));
                }

                return maxM;
            }

            float dry = RunDash(new BandSea(1000f, 1000f), out bool dryCast);
            Assert.That(dryCast, Is.True);
            Assert.That(dry, Is.GreaterThan(1.5f), "without the sea the dash crosses 1.5 m");

            float wet = RunDash(new BandSea(1.5f, 1000f), out bool wetCast);
            Assert.That(wetCast, Is.True, "the wild still casts; only the displacement is withheld");
            Assert.That(wet, Is.LessThan(1.5f));
        }

        [Test]
        public void Separation_never_pushes_a_wild_onto_the_sea()
        {
            // Two timid M wilds (0.7 m bodies) 0.3 m apart; the eastern one sits 0.1 m from the shore.
            float3 Run(IEncounterSurface surface, out float3 westStart, out EncounterUnit west, out float3 eastStart)
            {
                var e = new Encounter(_planet, _balance, surface, 9);
                west = e.AddCreature(EncounterSide.Wild, Creature("a", Temperament.Timid), null, AtMeters(1.6f), 10);
                EncounterUnit east = e.AddCreature(EncounterSide.Wild, Creature("b", Temperament.Timid), null,
                    AtMeters(1.9f), 10);
                westStart = west.Position;
                eastStart = east.Position;
                e.Tick(Dt, Dt, FarPlayer, Free, CombatAi.NoTarget);
                return east.Position;
            }

            float3 dryEast = Run(new BandSea(1000f, 1000f), out _, out _, out _);
            Assert.That(MetersOf(dryEast), Is.GreaterThan(2f), "without the sea the push would cross 2 m");

            var sea = new BandSea(2f, 1000f);
            float3 wetEast = Run(sea, out float3 westStart, out EncounterUnit west, out float3 eastStart);
            Assert.That(sea.IsSea(_planet.ToDirection(wetEast)), Is.False);
            Assert.That(math.distance(wetEast, eastStart), Is.LessThan(ArcTolM), "the coastal wild keeps its place");
            Assert.That(MetersOf(west.Position), Is.LessThan(MetersOf(westStart) - 0.1f), "the inland wild still yields");
        }
    }
}
