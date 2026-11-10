using System.Collections.Generic;
using System.IO;
using Cosmimera.Core.Contracts;
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
