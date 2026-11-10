using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using Cosmimera.Planet;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Cosmimera.Combat.Tests
{
    // REQ: docs/design/combat.md#野生領域 (D0-E2, Q-C33) and planet-world.md BOSS 戰數值 (D0-R2), decided 2026-11-10.
    public sealed class EncounterTerritoryTests
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

        private MoveDef Move(string id, RangeClass range, int baseRangeMm)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId(id);
            m.Set(Element.Fire, MoveKind.Damage, 40, null, 0, 0, 1500, 100, range, MoveShape.Front, baseRangeMm,
                MoveMovement.None, BurstOrigin.Caster);
            return m;
        }

        private CreatureDef Creature(string id, Temperament temperament, MoveDef move = null)
        {
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            c.Set("line_" + id, CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Charge, temperament, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(25, 25, 25, 25), move ?? Move("bite_" + id, RangeClass.Melee, 2000), null, "{}", false);
            return c;
        }

        // Unit direction whose great-circle distance from Dir(0) is `meters` on the ground.
        private static float3 AtMeters(float meters)
        {
            float a = meters / R;
            return math.normalize(new float3(math.sin(a), math.cos(a), 0f));
        }

        private float3 Feet(float meters) => _planet.ToWorld(AtMeters(meters), 0f);

        [TestCase(Temperament.Normal, 7f, true)]
        [TestCase(Temperament.Normal, 9f, false)]
        [TestCase(Temperament.Fierce, 19f, true)]
        [TestCase(Temperament.Fierce, 21f, false)]
        [TestCase(Temperament.Timid, 0.5f, false)]
        [TestCase(Temperament.Cautious, 3.5f, true)]
        [TestCase(Temperament.Cautious, 4.5f, false)]
        [TestCase(Temperament.Territorial, 13f, true)]
        [TestCase(Temperament.Territorial, 15f, false)]
        [TestCase(Temperament.Apex, 27f, true)]
        [TestCase(Temperament.Apex, 29f, false)]
        public void Proximity_engagement_follows_the_species_territory(Temperament temperament, float meters, bool engaged)
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", temperament), null, AtMeters(0f), 10);
            e.Tick(50, 50, Feet(meters), Free, CombatAi.NoTarget);
            Assert.That(wild.Engaged, Is.EqualTo(engaged));
        }

        [Test]
        public void Territory_radius_reads_balance_per_temperament()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit timid = e.AddCreature(EncounterSide.Wild, Creature("t", Temperament.Timid), null, AtMeters(0f), 10);
            EncounterUnit cautious = e.AddCreature(EncounterSide.Wild, Creature("c", Temperament.Cautious), null, AtMeters(25f), 10);
            EncounterUnit normal = e.AddCreature(EncounterSide.Wild, Creature("n", Temperament.Normal), null, AtMeters(50f), 10);
            EncounterUnit territorial = e.AddCreature(EncounterSide.Wild, Creature("tr", Temperament.Territorial), null, AtMeters(75f), 10);
            EncounterUnit fierce = e.AddCreature(EncounterSide.Wild, Creature("f", Temperament.Fierce), null, AtMeters(100f), 10);
            EncounterUnit apex = e.AddCreature(EncounterSide.Wild, Creature("a", Temperament.Apex), null, AtMeters(125f), 10);
            Assert.That(e.TerritoryRadiusM(timid.Id), Is.EqualTo(0f));
            // mm × 0.001f is not exact in float (28000 × 0.001f = 28.0000019); 1e-5 m covers that rounding at 28 m.
            Assert.That(e.TerritoryRadiusM(cautious.Id), Is.EqualTo(4f).Within(1e-5f));
            Assert.That(e.TerritoryRadiusM(normal.Id), Is.EqualTo(8f).Within(1e-5f));
            Assert.That(e.TerritoryRadiusM(territorial.Id), Is.EqualTo(14f).Within(1e-5f));
            Assert.That(e.TerritoryRadiusM(fierce.Id), Is.EqualTo(20f).Within(1e-5f));
            Assert.That(e.TerritoryRadiusM(apex.Id), Is.EqualTo(28f).Within(1e-5f));
            Assert.That(e.TerritoryRadiusM(apex.Id), Is.LessThan(Encounter.DisengageRadiusM));
            Assert.Throws<ContractException>(() => e.TerritoryRadiusM(99));
        }

        [Test]
        public void An_engaged_wild_stays_engaged_until_the_disengage_distance()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", Temperament.Normal), null, AtMeters(0f), 10);
            e.Tick(50, 50, Feet(5f), Free, CombatAi.NoTarget);
            Assert.That(wild.Engaged, Is.True);
            e.Tick(100, 50, Feet(30f), Free, CombatAi.NoTarget);
            Assert.That(wild.Engaged, Is.True, "30 m is past the 8 m territory but inside the 40 m hysteresis");
            e.Tick(150, 50, Feet(200f), Free, CombatAi.NoTarget);
            Assert.That(wild.Engaged, Is.False);
        }

        [Test]
        public void Provoke_starts_a_fight_with_a_timid_wild_and_checks_its_target()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit mine = e.AddCreature(EncounterSide.Player, Creature("m", Temperament.Normal), null, AtMeters(0f), 10);
            EncounterUnit timid = e.AddCreature(EncounterSide.Wild, Creature("t", Temperament.Timid), null, AtMeters(3f), 10);
            e.Field(mine.Id, AtMeters(0f), false, 0);
            e.Tick(50, 50, Feet(0f), Free, mine.Id);
            Assert.That(timid.Engaged, Is.False, "a timid wild never starts a fight");
            e.Provoke(timid.Id);
            Assert.That(timid.Engaged, Is.False, "takes effect on the next Tick, so between-battle steps still run");
            e.SetLevel(mine.Id, 11);
            e.Tick(100, 50, Feet(0f), Free, mine.Id);
            Assert.That(timid.Engaged, Is.True);
            e.Tick(150, 50, Feet(0f), Free, mine.Id);
            Assert.That(timid.Engaged, Is.True, "the intruder is inside the disengage distance");
            Assert.That(mine.TargetId, Is.EqualTo(timid.Id));
            Assert.Throws<ContractException>(() => e.Provoke(mine.Id), "only wild units");
            Assert.Throws<ContractException>(() => e.Provoke(99));
            e.Remove(timid.Id);
            Assert.Throws<ContractException>(() => e.Provoke(timid.Id), "removed units are inactive");
        }

        private MoveDef ShapedMove(string id, MoveShape shape, RangeClass range, int baseRangeMm, int accuracy, int fieldMs)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId(id);
            m.Set(Element.Fire, MoveKind.Damage, 1, null, 0, fieldMs, 1500, accuracy, range, shape, baseRangeMm,
                MoveMovement.None, BurstOrigin.Caster);
            return m;
        }

        // E2-SOL-01: a non-field volume aimed at an engaged wild must also hit (and so engage) a timid one it touches.
        [Test]
        public void A_burst_aimed_at_an_engaged_wild_also_hits_and_engages_a_timid_one_in_reach()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit mine = e.AddCreature(EncounterSide.Player,
                Creature("m", Temperament.Normal, ShapedMove("burst_m", MoveShape.Burst, RangeClass.Melee, 3000, 100, 0)),
                null, AtMeters(0f), 10);
            EncounterUnit normal = e.AddCreature(EncounterSide.Wild, Creature("n", Temperament.Normal), null, AtMeters(1f), 10);
            EncounterUnit timid = e.AddCreature(EncounterSide.Wild, Creature("t", Temperament.Timid), null, AtMeters(2f), 10);
            e.Field(mine.Id, AtMeters(0f), false, 0);
            bool timidHit = false;
            for (int t = 1; t <= 40 && !timidHit; t++)
            {
                Assert.That(timid.Engaged, Is.False, "timid stays out until something actually hits it");
                e.Tick(t * 50L, 50, Feet(0f), Free, mine.Id);
                foreach (EncounterEvent ev in e.Events)
                {
                    timidHit |= ev.Kind == EncounterEventKind.Hit && ev.SourceId == mine.Id && ev.TargetId == timid.Id;
                }
            }

            Assert.That(normal.Engaged, Is.True, "the normal wild engaged by territory and drew the burst");
            Assert.That(timidHit, Is.True, "the burst reached the timid wild it overlaps");
            Assert.That(timid.Engaged || timid.Combatant.IsFainted, Is.True, "being hit starts the fight");
        }

        // E2-SOL-02: a field roll that misses must not engage a timid wild; the first Hit does.
        [Test]
        public void A_field_miss_does_not_engage_a_timid_wild_but_a_hit_does()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit mine = e.AddCreature(EncounterSide.Player,
                Creature("m", Temperament.Normal, ShapedMove("field_m", MoveShape.Field, RangeClass.Mid, 12000, 1, 3500)),
                null, AtMeters(0f), 10);
            e.AddCreature(EncounterSide.Wild, Creature("n", Temperament.Normal), null, AtMeters(1f), 10);
            EncounterUnit timid = e.AddCreature(EncounterSide.Wild, Creature("t", Temperament.Timid), null, AtMeters(2f), 10);
            e.Field(mine.Id, AtMeters(0f), false, 0);
            int misses = 0;
            bool hit = false;
            for (int t = 1; t <= 200 && !hit; t++)
            {
                e.Tick(t * 50L, 50, Feet(0f), Free, mine.Id);
                foreach (EncounterEvent ev in e.Events)
                {
                    if (ev.SourceId == mine.Id && ev.TargetId == timid.Id)
                    {
                        misses += ev.Kind == EncounterEventKind.Miss ? 1 : 0;
                        hit |= ev.Kind == EncounterEventKind.Hit;
                    }
                }

                if (!hit)
                {
                    Assert.That(timid.Engaged, Is.False, "tick " + t + ": only misses so far");
                }
            }

            Assert.That(misses, Is.GreaterThan(0), "accuracy 1 % produced at least one MISS on the timid wild");
            if (hit)
            {
                Assert.That(timid.Engaged || timid.Combatant.IsFainted, Is.True, "the first Hit engages it");
            }
        }

        // E2-SOL-05: each geometric contact with a vulnerable player costs exactly one bounded combat draw
        // (NextUInt(1000)); an i-frame dodge costs none. Same-seed self-replay cannot see an extra draw, so compare a
        // run that dodges the first contact against one that takes it.
        [Test]
        public void A_player_contact_costs_one_combat_draw_and_an_iframe_dodge_costs_none()
        {
            long contactAt = -1;
            Encounter taken = NewPlayerFight(out _);
            for (int t = 1; t <= 400 && contactAt < 0; t++)
            {
                taken.Tick(t * 50L, 50, Feet(1.5f), Free, CombatAi.NoTarget);
                foreach (EncounterEvent ev in taken.Events)
                {
                    if (ev.TargetId == Encounter.PlayerCharacterId &&
                        (ev.Kind == EncounterEventKind.Hit || ev.Kind == EncounterEventKind.Miss))
                    {
                        contactAt = t;
                    }
                }
            }

            Assert.That(contactAt, Is.GreaterThan(0), "the wild reached the player");
            Encounter dodged = NewPlayerFight(out PlayerVitals vitals);
            Pcg32 beforeContact = default;
            for (long t = 1; t <= contactAt; t++)
            {
                if (t == contactAt)
                {
                    Assert.That(vitals.TryRoll(t * 50L), Is.EqualTo(RollResult.Ok));
                    beforeContact = dodged.RngForTests;
                }

                dodged.Tick(t * 50L, 50, Feet(1.5f), Free, CombatAi.NoTarget);
            }

            // Absolute half: the only roll in the contact tick is the player's, so a dodge leaves the stream untouched.
            // The relative half below cannot see an extra draw added on both paths.
            Pcg32 afterDodge = dodged.RngForTests;
            Assert.That(afterDodge.NextUInt(), Is.EqualTo(beforeContact.NextUInt()), "an i-frame dodge costs no draw");

            Pcg32 expected = dodged.RngForTests;
            expected.NextUInt(1000);
            Pcg32 actual = taken.RngForTests;
            for (int i = 0; i < 4; i++)
            {
                Assert.That(actual.NextUInt(), Is.EqualTo(expected.NextUInt()), "stream position after the contact, draw " + i);
            }
        }

        private Encounter NewPlayerFight(out PlayerVitals vitals)
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 3);
            e.AddCreature(EncounterSide.Wild, Creature("w", Temperament.Fierce), null, AtMeters(0f), 50);
            vitals = new PlayerVitals(new PlayerVitalsConfig(100000, 600, 400, 800));
            e.AttachPlayer(vitals);
            return e;
        }

        // E2-SOL-07: the 40 m disengage test is inclusive. The player is placed at the exact float chord the encounter
        // computes, so arc/chord and body-height differences cannot blur the boundary.
        [TestCase(0f, true)]
        [TestCase(0.01f, false)]
        public void Disengage_distance_is_inclusive_at_40_m(float beyond, bool staysEngaged)
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", Temperament.Timid), null, AtMeters(0f), 10);
            e.Provoke(wild.Id);
            e.Tick(50, 50, Feet(30f), Free, CombatAi.NoTarget);
            Assert.That(wild.Engaged, Is.True);

            float3 east = PlanetFrame.FromUp(math.normalize(wild.Position)).East;
            float s = Encounter.DisengageRadiusM + beyond;
            float3 player = wild.Position + east * s;
            for (int i = 0; i < 64 && beyond == 0f && math.distance(wild.Position, player) != Encounter.DisengageRadiusM; i++)
            {
                s = math.asfloat(math.asint(s) + (math.distance(wild.Position, player) > Encounter.DisengageRadiusM ? -1 : 1));
                player = wild.Position + east * s;
            }

            if (beyond == 0f)
            {
                Assert.That(math.distance(wild.Position, player), Is.EqualTo(Encounter.DisengageRadiusM), "exact boundary");
            }

            e.Tick(100, 50, player, Free, CombatAi.NoTarget);
            Assert.That(wild.Engaged, Is.EqualTo(staysEngaged));
        }

        // E2-SOL-01 (r2): the rect (front) path must not regain the Engaged gate either.
        [Test]
        public void A_front_strike_aimed_at_an_engaged_wild_also_hits_and_engages_a_timid_one_behind_it()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit mine = e.AddCreature(EncounterSide.Player,
                Creature("m", Temperament.Normal, ShapedMove("front_m", MoveShape.Front, RangeClass.Melee, 4000, 100, 0)),
                null, AtMeters(0f), 10);
            e.AddCreature(EncounterSide.Wild, Creature("n", Temperament.Normal), null, AtMeters(1f), 10);
            EncounterUnit timid = e.AddCreature(EncounterSide.Wild, Creature("t", Temperament.Timid), null, AtMeters(2f), 10);
            e.Field(mine.Id, AtMeters(0f), false, 0);
            bool timidHit = false;
            for (int t = 1; t <= 40 && !timidHit; t++)
            {
                Assert.That(timid.Engaged, Is.False, "timid stays out until something actually hits it");
                e.Tick(t * 50L, 50, Feet(0f), Free, mine.Id);
                foreach (EncounterEvent ev in e.Events)
                {
                    timidHit |= ev.Kind == EncounterEventKind.Hit && ev.SourceId == mine.Id && ev.TargetId == timid.Id;
                }
            }

            Assert.That(timidHit, Is.True, "the front rect reached the timid wild behind the target");
            Assert.That(timid.Engaged || timid.Combatant.IsFainted, Is.True, "being hit starts the fight");
        }

        // E2-SOL-02 (r2): the positive half must always run — a field that does hit engages a timid wild, even with
        // zero damage (the hit, not the damage, starts the fight).
        [TestCase(1)]
        [TestCase(0)]
        public void A_field_hit_engages_a_timid_wild(int power)
        {
            var field = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(field);
            field.SetId("field_hit_m");
            field.Set(Element.Fire, MoveKind.Damage, power, null, 0, 3500, 1500, 100, RangeClass.Mid, MoveShape.Field, 12000,
                MoveMovement.None, BurstOrigin.Caster);
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit mine = e.AddCreature(EncounterSide.Player, Creature("m", Temperament.Normal, field), null, AtMeters(0f), 10);
            e.AddCreature(EncounterSide.Wild, Creature("n", Temperament.Normal), null, AtMeters(1f), 10);
            EncounterUnit timid = e.AddCreature(EncounterSide.Wild, Creature("t", Temperament.Timid), null, AtMeters(2f), 10);
            e.Field(mine.Id, AtMeters(0f), false, 0);
            bool hit = false;
            for (int t = 1; t <= 200 && !hit; t++)
            {
                e.Tick(t * 50L, 50, Feet(0f), Free, mine.Id);
                foreach (EncounterEvent ev in e.Events)
                {
                    hit |= ev.Kind == EncounterEventKind.Hit && ev.SourceId == mine.Id && ev.TargetId == timid.Id;
                }
            }

            Assert.That(hit, Is.True, "accuracy 100 % field reached the timid wild");
            Assert.That(timid.Engaged || timid.Combatant.IsFainted, Is.True, "the Hit engaged it");
        }

        // E2-SOL-05 (r2): once the character is Downed and nothing else can fight, ticks consume no combat draws.
        [Test]
        public void After_the_player_is_downed_a_lone_wild_draws_nothing()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 3);
            e.AddCreature(EncounterSide.Wild, Creature("w", Temperament.Fierce), null, AtMeters(0f), 50);
            var vitals = new PlayerVitals(new PlayerVitalsConfig(1, 600, 400, 800));
            e.AttachPlayer(vitals);
            int t = 1;
            for (; t <= 400 && !vitals.IsDowned; t++)
            {
                e.Tick(t * 50L, 50, Feet(1.5f), Free, CombatAi.NoTarget);
            }

            Assert.That(vitals.IsDowned, Is.True);
            Pcg32 before = e.RngForTests;
            for (int k = 0; k < 40; k++, t++)
            {
                e.Tick(t * 50L, 50, Feet(1.5f), Free, CombatAi.NoTarget);
                Assert.That(e.Events, Is.Empty, "no target left");
            }

            Pcg32 after = e.RngForTests;
            Assert.That(after.NextUInt(), Is.EqualTo(before.NextUInt()), "the stream did not move after Downed");
        }

        // E2-SOL-05 (r2): one wild burst over several targets resolves units in ascending id, the character last. A
        // replay of the same seed would faithfully repeat a reversed order, so pin the order itself.
        [Test]
        public void A_wild_burst_resolves_targets_in_id_order_with_the_player_last()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 5);
            EncounterUnit a = e.AddCreature(EncounterSide.Player, Creature("a", Temperament.Normal), null, AtMeters(1f), 10);
            EncounterUnit b = e.AddCreature(EncounterSide.Player, Creature("b", Temperament.Normal), null, AtMeters(-1f), 10);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild,
                Creature("w", Temperament.Fierce, ShapedMove("burst_w", MoveShape.Burst, RangeClass.Melee, 3000, 100, 0)),
                null, AtMeters(0f), 50);
            e.Field(a.Id, AtMeters(1f), false, 0);
            e.Field(b.Id, AtMeters(-1f), false, 0);
            e.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(100000, 600, 400, 800)));
            var order = new List<int>();
            for (int t = 1; t <= 200 && order.Count == 0; t++)
            {
                e.Tick(t * 50L, 50, Feet(0.5f), Free, a.Id);
                foreach (EncounterEvent ev in e.Events)
                {
                    if (ev.SourceId == wild.Id && (ev.Kind == EncounterEventKind.Hit || ev.Kind == EncounterEventKind.Miss))
                    {
                        order.Add(ev.TargetId);
                    }
                }
            }

            Assert.That(order, Is.EqualTo(new[] { a.Id, b.Id, Encounter.PlayerCharacterId }),
                "every target touched, ascending id, character last");
        }

        [Test]
        public void Wild_attacks_the_player_character_and_downs_it_once()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 3);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", Temperament.Fierce), null, AtMeters(0f), 50);
            var vitals = new PlayerVitals(new PlayerVitalsConfig(150, 600, 400, 800));
            e.AttachPlayer(vitals);
            int hits = 0;
            int downed = 0;
            long downedAt = -1;
            for (int t = 1; t <= 400; t++)
            {
                e.Tick(t * 50L, 50, Feet(1.5f), Free, CombatAi.NoTarget);
                foreach (EncounterEvent ev in e.Events)
                {
                    Assert.That(ev.Kind, Is.Not.EqualTo(EncounterEventKind.Faint), "the character never Faints");
                    if (ev.Kind == EncounterEventKind.Hit && ev.TargetId == Encounter.PlayerCharacterId)
                    {
                        hits++;
                        Assert.That(ev.Amount, Is.GreaterThanOrEqualTo(0));
                    }

                    if (ev.Kind == EncounterEventKind.PlayerDowned)
                    {
                        downed++;
                        downedAt = t;
                        Assert.That(ev.TargetId, Is.EqualTo(Encounter.PlayerCharacterId));
                        Assert.That(ev.SourceId, Is.EqualTo(wild.Id));
                    }
                }
            }

            Assert.That(hits, Is.GreaterThan(0));
            Assert.That(downed, Is.EqualTo(1), "PlayerDowned is raised exactly once");
            Assert.That(vitals.IsDowned, Is.True);
            Assert.That(downedAt, Is.GreaterThan(0));
            Assert.That(wild.TargetId, Is.EqualTo(CombatAi.NoTarget), "a downed character is no longer a target");
        }

        [Test]
        public void Without_an_attached_player_the_character_is_never_targeted()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 3);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", Temperament.Fierce), null, AtMeters(0f), 50);
            for (int t = 1; t <= 100; t++)
            {
                e.Tick(t * 50L, 50, Feet(1.5f), Free, CombatAi.NoTarget);
                Assert.That(e.Events, Is.Empty);
            }

            Assert.That(wild.Engaged, Is.True);
            Assert.That(wild.TargetId, Is.EqualTo(CombatAi.NoTarget));
        }

        [Test]
        public void A_nearer_creature_is_preferred_over_the_player_character()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 3);
            EncounterUnit mine = e.AddCreature(EncounterSide.Player, Creature("m", Temperament.Normal), null, AtMeters(2f), 50);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("w", Temperament.Fierce), null, AtMeters(0f), 50);
            e.Field(mine.Id, AtMeters(2f), false, 0);
            var vitals = new PlayerVitals(new PlayerVitalsConfig(1000, 600, 400, 800));
            e.AttachPlayer(vitals);
            e.Tick(50, 50, Feet(10f), new AiCommand(AiCommandKind.Follow, CombatAi.NoTarget), mine.Id);
            Assert.That(wild.TargetId, Is.EqualTo(mine.Id));
        }

        [Test]
        public void Dodge_roll_iframes_turn_a_hit_on_the_player_into_a_miss()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 3);
            e.AddCreature(EncounterSide.Wild, Creature("w", Temperament.Fierce), null, AtMeters(0f), 50);
            var vitals = new PlayerVitals(new PlayerVitalsConfig(1000, 600, 400, 800));
            e.AttachPlayer(vitals);
            int misses = 0;
            for (int t = 1; t <= 200; t++)
            {
                long now = t * 50L;
                // Roll whenever allowed: i-frames cover [start, start+400) of every 800 ms window.
                vitals.TryRoll(now);
                e.Tick(now, 50, Feet(1.5f), Free, CombatAi.NoTarget);
                foreach (EncounterEvent ev in e.Events)
                {
                    if (ev.TargetId == Encounter.PlayerCharacterId && ev.Kind == EncounterEventKind.Miss)
                    {
                        misses++;
                        Assert.That(ev.Amount, Is.EqualTo(0));
                    }
                }
            }

            Assert.That(misses, Is.GreaterThan(0), "accuracy 100 never misses, so every miss here is an i-frame");
        }

        [Test]
        public void AttachPlayer_preconditions()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            Assert.Throws<ContractException>(() => e.AttachPlayer(null));
            e.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(1000, 600, 400, 800)));
            Assert.Throws<ContractException>(() => e.AttachPlayer(new PlayerVitals(new PlayerVitalsConfig(1000, 600, 400, 800))));
        }

        [Test]
        public void TerritoryTuning_preconditions()
        {
            Assert.Throws<ContractException>(() => _ = new TerritoryTuning(-1, 4000, 8000, 14000, 20000, 28000));
            Assert.Throws<ContractException>(() => _ = new TerritoryTuning(0, 40000, 8000, 14000, 20000, 28000), "must stay below disengage");
            Assert.Throws<ContractException>(() => _ = new TerritoryTuning(0, 4000, 8000, 14000, 40000, 28000));
            var t = new TerritoryTuning(1, 2, 3, 4, 5, 6);
            Assert.That(t.RadiusMm(Temperament.Timid), Is.EqualTo(1));
            Assert.That(t.RadiusMm(Temperament.Cautious), Is.EqualTo(2));
            Assert.That(t.RadiusMm(Temperament.Normal), Is.EqualTo(3));
            Assert.That(t.RadiusMm(Temperament.Territorial), Is.EqualTo(4));
            Assert.That(t.RadiusMm(Temperament.Fierce), Is.EqualTo(5));
            Assert.That(t.RadiusMm(Temperament.Apex), Is.EqualTo(6));
            Assert.Throws<ContractException>(() => t.RadiusMm((Temperament)7));
        }

        [Test]
        public void Boss_has_double_hp_and_a_larger_body_and_melee_reach()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            MoveDef ranged = Move("bolt", RangeClass.Long, 24000);
            CreatureDef def = Creature("b", Temperament.Fierce);
            CreatureDef rangedDef = Creature("r", Temperament.Fierce, ranged);
            EncounterUnit normal = e.AddCreature(EncounterSide.Wild, def, ranged, AtMeters(0f), 30);
            EncounterUnit boss = e.AddBoss(def, ranged, AtMeters(100f), 30);
            Assert.That(normal.IsBoss, Is.False);
            Assert.That(boss.IsBoss, Is.True);
            Assert.That(boss.Side, Is.EqualTo(EncounterSide.Wild));
            Assert.That(boss.Fielded, Is.True);
            Assert.That(boss.Combatant.MaxHp, Is.EqualTo(Encounter.BossHpMultiplier * normal.Combatant.MaxHp));
            Assert.That(boss.Combatant.Hp, Is.EqualTo(boss.Combatant.MaxHp));
            Assert.That(boss.BodyRadiusM, Is.EqualTo(normal.BodyRadiusM * 1.5f).Within(1e-6f));
            HitVolume nv = normal.Volumes[0];
            HitVolume bv = boss.Volumes[0];
            Assert.That(bv.ReachMm, Is.EqualTo(nv.ReachMm * Encounter.BossSizePermille / 1000));
            Assert.That(bv.WidthMm, Is.EqualTo(nv.WidthMm * Encounter.BossSizePermille / 1000));
            Assert.That(bv.HalfHeightMm, Is.EqualTo(nv.HalfHeightMm * Encounter.BossSizePermille / 1000));
            Assert.That(boss.Volumes[1], Is.EqualTo(normal.Volumes[1]), "ranged volumes are not scaled");
            EncounterUnit rangedBoss = e.AddBoss(rangedDef, null, AtMeters(200f), 30);
            EncounterUnit rangedNormal = e.AddCreature(EncounterSide.Wild, rangedDef, null, AtMeters(300f), 30);
            Assert.That(rangedBoss.Volumes[0], Is.EqualTo(rangedNormal.Volumes[0]));
            Assert.Throws<ContractException>(() => e.AddBoss(null, null, AtMeters(0f), 30));
            Assert.Throws<ContractException>(() => e.AddBoss(def, null, AtMeters(0f), 0));
        }

        [Test]
        public void SetLevel_keeps_the_boss_multipliers()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            CreatureDef def = Creature("b", Temperament.Fierce);
            EncounterUnit grown = e.AddBoss(def, null, AtMeters(0f), 10);
            EncounterUnit built = e.AddBoss(def, null, AtMeters(100f), 40);
            e.SetLevel(grown.Id, 40);
            Assert.That(grown.Combatant.MaxHp, Is.EqualTo(built.Combatant.MaxHp));
            Assert.That(grown.Volumes[0], Is.EqualTo(built.Volumes[0]));
        }

        [Test]
        public void MultiplyMaxHp_is_once_and_before_damage()
        {
            var c = new Combatant(_balance.Stats, _balance.Effects, 10, 10, 10, new int[10], new int[10], new EffectBook(), 1);
            int max = c.MaxHp;
            Assert.Throws<ContractException>(() => c.MultiplyMaxHp(0));
            c.MultiplyMaxHp(2);
            Assert.That(c.MaxHp, Is.EqualTo(2 * max));
            Assert.That(c.Hp, Is.EqualTo(c.MaxHp));
            Assert.Throws<ContractException>(() => c.MultiplyMaxHp(2), "only once");
        }
    }
}
