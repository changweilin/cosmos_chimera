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
    public sealed class EncounterTests
    {
        private sealed class FlatSurface : IEncounterSurface
        {
            public float GroundAltitude(float3 direction) => 0f;

            public bool IsSea(float3 direction) => false;

            public bool IsBlocked(float3 direction, float clearanceM) => false;
        }

        private readonly List<Object> _created = new List<Object>();
        private PlanetConfig _planet;
        private CombatBalance _balance;

        [SetUp]
        public void SetUp()
        {
            _planet = new PlanetConfig(640f, float3.zero);
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

        private MoveDef Move(string id, int power, MoveShape shape, RangeClass range, int baseRangeMm)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId(id);
            m.Set(Element.Fire, MoveKind.Damage, power, null, 0, 0, 1500, 100, range, shape, baseRangeMm,
                MoveMovement.None, BurstOrigin.Caster);
            return m;
        }

        // Fierce (20 m) keeps the pre-territory tests' 18 m engagement geometry valid.
        private CreatureDef Creature(string id, MoveDef move, AiStyle style, Temperament temperament = Temperament.Fierce)
        {
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            c.Set("line_" + id, CreatureStage.Juvenile, CreatureSizeClass.M, style, temperament, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(25, 25, 25, 25), move, null, "{}", false);
            return c;
        }

        private static float3 Dir(float degreesEast)
        {
            float a = math.radians(degreesEast);
            return math.normalize(new float3(math.sin(a), math.cos(a), 0f));
        }

        private Encounter OneOnOne(out EncounterUnit mine, out EncounterUnit wild, ulong seed = 7)
        {
            MoveDef bite = Move("fire_bite", 40, MoveShape.Front, RangeClass.Melee, 2000);
            var e = new Encounter(_planet, _balance, new FlatSurface(), seed);
            mine = e.AddCreature(EncounterSide.Player, Creature("mine", bite, AiStyle.Charge), null, Dir(0f), 100);
            wild = e.AddCreature(EncounterSide.Wild, Creature("wild", bite, AiStyle.Charge), null, Dir(0.9f), 100);
            e.Field(mine.Id, Dir(0f), false, 0);
            return e;
        }

        private static long RunUntilFaint(Encounter e, float3 player, int maxTicks)
        {
            var free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
            for (int t = 1; t <= maxTicks; t++)
            {
                e.Tick(t * 50L, 50, player, free, 0);
                foreach (EncounterEvent ev in e.Events)
                {
                    if (ev.Kind == EncounterEventKind.Faint)
                    {
                        return t * 50L;
                    }
                }
            }

            return -1;
        }

        // REQ: Q-C31 (2026-11-09) — level-up effects settle when the battle ends; the result must equal a unit built
        // at the new level, keeping damage taken.
        [Test]
        public void SetLevel_matches_a_unit_built_at_that_level_and_keeps_damage_taken()
        {
            MoveDef bite = Move("fire_bite", 40, MoveShape.Front, RangeClass.Melee, 2000);
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            CreatureDef def = Creature("mine", bite, AiStyle.Charge);
            EncounterUnit grown = e.AddCreature(EncounterSide.Player, def, null, Dir(0f), 10);
            EncounterUnit built = e.AddCreature(EncounterSide.Player, def, null, Dir(1f), 30);
            int before = grown.Combatant.MaxHp;
            e.SetLevel(grown.Id, 30);
            Assert.That(grown.Level, Is.EqualTo(30));
            Assert.That(grown.Combatant.MaxHp, Is.EqualTo(built.Combatant.MaxHp));
            Assert.That(grown.Combatant.Hp, Is.EqualTo(grown.Combatant.MaxHp), "full HP stays full");
            Assert.That(grown.Agi, Is.EqualTo(built.Agi));
            Assert.That(grown.Volumes[0], Is.EqualTo(built.Volumes[0]));
            Assert.That(grown.Combatant.MaxHp, Is.GreaterThan(before));
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => e.SetLevel(grown.Id, 29), "levels never go down");
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => e.SetLevel(grown.Id, 101));
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => e.SetLevel(99, 50));
            e.SetLevel(grown.Id, 30);
            Assert.That(grown.Combatant.MaxHp, Is.EqualTo(built.Combatant.MaxHp), "same level is a no-op");
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, def, null, Dir(2f), 10);
            wild.Engaged = true;
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => e.SetLevel(grown.Id, 31), "not mid-fight");
            wild.Engaged = false;
            e.SetLevel(grown.Id, 31);
        }

        [Test]
        public void Rescale_raises_hp_by_the_max_hp_increase_and_refuses_fainted()
        {
            var c = new Combatant(_balance.Stats, _balance.Effects, 10, 10, 10, new int[10], new int[10], new EffectBook(), 1);
            int max10 = c.MaxHp;
            System.Reflection.PropertyInfo hp = typeof(Combatant).GetProperty(nameof(Combatant.Hp));
            hp.SetValue(c, max10 - 100); // damage taken in the battle that just ended
            c.Rescale(50, 10, 10);
            Assert.That(c.MaxHp, Is.GreaterThan(max10));
            Assert.That(c.Hp, Is.EqualTo(c.MaxHp - 100), "damage taken is kept, not healed");
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => c.Rescale(10, 10, 10), "never lowers MaxHp");
            hp.SetValue(c, 0);
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => c.Rescale(60, 10, 10), "fainted");
        }

        // REQ: docs/design/combat.md#場地 — a field resolves at placement and every field.tick_ms, ceil(life/tick) times.
        [Test]
        public void Field_move_resolves_ceil_lifetime_over_tick_times_then_expires()
        {
            var field = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(field);
            field.SetId("fire_field");
            field.Set(Element.Fire, MoveKind.Damage, 1, null, 0, 3500, 60000, 100, RangeClass.Mid, MoveShape.Field,
                12000, MoveMovement.None, BurstOrigin.Caster);
            MoveDef nibble = Move("fire_nibble", 1, MoveShape.Front, RangeClass.Melee, 2000);
            var e = new Encounter(_planet, _balance, new FlatSurface(), 3);
            EncounterUnit mine = e.AddCreature(EncounterSide.Player, Creature("mine", field, AiStyle.Hold), null, Dir(0f), 100);
            EncounterUnit wild = e.AddCreature(EncounterSide.Wild, Creature("wild", nibble, AiStyle.Hold), null, Dir(0.1f), 100);
            e.Field(mine.Id, Dir(0f), false, 0);
            var free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
            int resolutions = 0;
            long firstAlive = -1;
            long lastAlive = -1;
            for (int t = 1; t <= 200; t++)
            {
                e.Tick(t * 50L, 50, _planet.ToWorld(Dir(-0.05f), 1f), free, mine.Id);
                if (e.ActiveFieldCount > 0)
                {
                    firstAlive = firstAlive < 0 ? t * 50L : firstAlive;
                    lastAlive = t * 50L;
                }
                foreach (EncounterEvent ev in e.Events)
                {
                    if (ev.SourceId == mine.Id && ev.TargetId == wild.Id &&
                        (ev.Kind == EncounterEventKind.Hit || ev.Kind == EncounterEventKind.Miss))
                    {
                        resolutions++;
                    }
                }
            }

            Assert.That(firstAlive, Is.GreaterThan(0));
            Assert.That(lastAlive - firstAlive, Is.EqualTo(3450), "alive for its whole [placed, placed + 3500) life");
            Assert.That(resolutions, Is.EqualTo(4), "3500 ms life / 1000 ms tick → 4 resolutions from one cast");
            Assert.That(e.ActiveFieldCount, Is.EqualTo(0), "the field expires after its last tick");
            Assert.That(wild.Combatant.IsFainted, Is.False);
        }

        [Test]
        public void Two_charging_creatures_close_in_and_one_faints()
        {
            Encounter e = OneOnOne(out EncounterUnit mine, out EncounterUnit wild);
            long when = RunUntilFaint(e, _planet.ToWorld(Dir(-0.5f), 1f), 2400);
            Assert.That(when, Is.GreaterThan(0), "a melee duel must end within 120 s");
            Assert.That(mine.Combatant.IsFainted || wild.Combatant.IsFainted, Is.True);
        }

        [Test]
        public void Same_seed_replays_identically()
        {
            Encounter a = OneOnOne(out EncounterUnit a0, out EncounterUnit a1, 99);
            Encounter b = OneOnOne(out EncounterUnit b0, out EncounterUnit b1, 99);
            float3 player = _planet.ToWorld(Dir(-0.5f), 1f);
            Assert.That(RunUntilFaint(a, player, 2400), Is.EqualTo(RunUntilFaint(b, player, 2400)));
            Assert.That(a0.Combatant.Hp, Is.EqualTo(b0.Combatant.Hp));
            Assert.That(a1.Combatant.Hp, Is.EqualTo(b1.Combatant.Hp));
            Assert.That(a0.Position, Is.EqualTo(b0.Position));
        }

        [Test]
        public void Far_wild_creature_is_not_engaged_and_takes_no_damage()
        {
            MoveDef bite = Move("fire_bite", 40, MoveShape.Front, RangeClass.Melee, 2000);
            var e = new Encounter(_planet, _balance, new FlatSurface(), 1);
            EncounterUnit mine = e.AddCreature(EncounterSide.Player, Creature("mine", bite, AiStyle.Charge), null, Dir(0f), 100);
            EncounterUnit far = e.AddCreature(EncounterSide.Wild, Creature("far", bite, AiStyle.Charge), null, Dir(10f), 100);
            e.Field(mine.Id, Dir(0f), false, 0);
            var free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
            for (int t = 1; t <= 200; t++)
            {
                e.Tick(t * 50L, 50, _planet.ToWorld(Dir(0f), 1f), free, mine.Id);
            }

            Assert.That(far.Engaged, Is.False);
            Assert.That(far.Combatant.Hp, Is.EqualTo(far.Combatant.MaxHp));
        }

        [Test]
        public void Units_stay_on_the_ground()
        {
            Encounter e = OneOnOne(out EncounterUnit mine, out EncounterUnit wild);
            RunUntilFaint(e, _planet.ToWorld(Dir(-0.5f), 1f), 400);
            Assert.That(math.length(mine.Position), Is.EqualTo(640f + mine.BodyRadiusM).Within(1e-3f));
            Assert.That(math.length(wild.Position), Is.EqualTo(640f + wild.BodyRadiusM).Within(1e-3f));
        }

        [Test]
        public void Tick_requires_strictly_increasing_time()
        {
            Encounter e = OneOnOne(out _, out _);
            var free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
            e.Tick(100, 50, _planet.ToWorld(Dir(0f), 1f), free, 0);
            Assert.Throws<ContractException>(() => e.Tick(100, 50, _planet.ToWorld(Dir(0f), 1f), free, 0));
        }

        [Test]
        public void Combat_rejects_planets_below_the_supported_radius()
        {
            Assert.Throws<ContractException>(() =>
                new Encounter(new PlanetConfig(319f, float3.zero), _balance, new FlatSurface(), 1));
            Assert.DoesNotThrow(() => new Encounter(new PlanetConfig(320f, float3.zero), _balance, new FlatSurface(), 1));
        }

        [Test]
        public void Balance_parses_the_capture_section()
        {
            Assert.That(_balance.CaptureBasePercent, Is.InRange(0, 100));
            Assert.That(_balance.CaptureBossPercent, Is.InRange(0, 100));
        }
    }
}
