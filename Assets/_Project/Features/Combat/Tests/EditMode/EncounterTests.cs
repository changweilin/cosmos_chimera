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
        private CreatureDef Creature(string id, MoveDef move, AiStyle style, Habit habit = Habit.Aggressive)
        {
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            TestWild.WithHabit(c, habit).Set("line_" + id, CreatureStage.Juvenile, CreatureSizeClass.M, style, null, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(78, 25, 25, 25, 25, 25), move, null, "{}", false);
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
            wild = e.AddWildT(Creature("wild", bite, AiStyle.Charge), null, Dir(0.9f), 100);
            e.Field(mine.Id, Dir(0f), false, 0);
            return e;
        }

        [Test]
        public void Equipped_cards_get_independent_slots_and_relocation_preserves_combat_state()
        {
            MoveDef bite = Move("native", 40, MoveShape.Front, RangeClass.Melee, 2000);
            CreatureDef def = Creature("mine", bite, AiStyle.Charge);
            var encounter = new Encounter(_planet, _balance, new FlatSurface(), 7);
            var cards = new List<MoveDef>();
            for (int i = 0; i < 4; i++) cards.Add(Move("card" + i, 50, MoveShape.Front, RangeClass.Melee, 2000));
            EncounterUnit unit = encounter.AddEquippedCreature(def, cards, new TraitDef[0], Dir(0), 100);
            Assert.That(unit.Moves.Count, Is.EqualTo(5));
            Assert.That(unit.Combatant.Cooldowns.SlotCount, Is.EqualTo(5));
            for (int i = 0; i < 4; i++) Assert.That(unit.Moves[i + 1].Id, Is.EqualTo(cards[i].Id));
            encounter.Field(unit.Id, Dir(0), false, 100);
            int hp = unit.Combatant.Hp;
            encounter.RelocateParty(Dir(10));
            Assert.That(math.distance(_planet.ToDirection(unit.Position), Dir(10)), Is.LessThan(1e-5f));
            Assert.That(unit.Combatant.Hp, Is.EqualTo(hp));
            Assert.That(unit.Fielded, Is.True);
            Assert.Throws<ContractException>(() => encounter.AddEquippedCreature(def, null, new TraitDef[0], Dir(0), 100));
            Assert.Throws<ContractException>(() => encounter.AddEquippedCreature(def, cards, null, Dir(0), 100));
            Assert.Throws<ContractException>(() => encounter.AddEquippedCreature(def, new[] { (MoveDef)null }, new TraitDef[0], Dir(0), 100));
            Assert.Throws<ContractException>(() => encounter.AddEquippedCreature(def, new MoveDef[5], new TraitDef[0], Dir(0), 100));
            Assert.Throws<ContractException>(() => encounter.RelocateParty(float3.zero));
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
            Assert.That(grown.Speed, Is.EqualTo(built.Speed));
            Assert.That(grown.Volumes[0], Is.EqualTo(built.Volumes[0]));
            Assert.That(grown.Combatant.MaxHp, Is.GreaterThan(before));
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => e.SetLevel(grown.Id, 29), "levels never go down");
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => e.SetLevel(grown.Id, 101));
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => e.SetLevel(99, 50));
            e.SetLevel(grown.Id, 30);
            Assert.That(grown.Combatant.MaxHp, Is.EqualTo(built.Combatant.MaxHp), "same level is a no-op");
            EncounterUnit wild = e.AddWildT(def, null, Dir(2f), 10);
            wild.Engaged = true;
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => e.SetLevel(grown.Id, 31), "not mid-fight");
            wild.Engaged = false;
            e.SetLevel(grown.Id, 31);
        }

        [Test]
        public void Rescale_raises_hp_by_the_max_hp_increase_and_refuses_fainted()
        {
            var c = new Combatant(_balance.Stats, _balance.Effects, BalanceFixture.Core(68, 10, 10), new int[10], new int[10], new EffectBook(), 1);
            int max10 = c.MaxHp;
            System.Reflection.PropertyInfo hp = typeof(Combatant).GetProperty(nameof(Combatant.Hp));
            hp.SetValue(c, max10 - 100); // damage taken in the battle that just ended
            c.Rescale(BalanceFixture.Core(90, 10, 10));
            Assert.That(c.MaxHp, Is.GreaterThan(max10));
            Assert.That(c.Hp, Is.EqualTo(c.MaxHp - 100), "damage taken is kept, not healed");
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => c.Rescale(BalanceFixture.Core(68, 10, 10)), "never lowers MaxHp");
            hp.SetValue(c, 0);
            Assert.Throws<Cosmimera.Core.Contracts.ContractException>(() => c.Rescale(BalanceFixture.Core(95, 10, 10)), "fainted");
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
            EncounterUnit wild = e.AddWildT(Creature("wild", nibble, AiStyle.Hold), null, Dir(0.1f), 100);
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
            EncounterUnit far = e.AddWildT(Creature("far", bite, AiStyle.Charge), null, Dir(10f), 100);
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

        [Test]
        public void CopyActiveFields_exposes_live_fields_and_checks_preconditions()
        {
            var field = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(field);
            field.SetId("fire_field");
            field.Set(Element.Fire, MoveKind.Damage, 10, null, 0, 3500, 60000, 100, RangeClass.Mid, MoveShape.Field,
                12000, MoveMovement.None, BurstOrigin.Caster);
            MoveDef nibble = Move("fire_nibble", 1, MoveShape.Front, RangeClass.Melee, 2000);
            var e = new Encounter(_planet, _balance, new FlatSurface(), 3);
            EncounterUnit mine = e.AddCreature(EncounterSide.Player, Creature("mine", field, AiStyle.Hold), null, Dir(0f), 100);
            e.AddWildT(Creature("wild", nibble, AiStyle.Hold), null, Dir(0.1f), 100);
            e.Field(mine.Id, Dir(0f), false, 0);

            var list = new List<ActiveFieldSnapshot>();
            Assert.Throws<ContractException>(() => e.CopyActiveFields(null, 50));
            Assert.Throws<ContractException>(() => e.CopyActiveFields(list, -1));

            var free = new AiCommand(AiCommandKind.Free, CombatAi.NoTarget);
            e.Tick(50, 50, _planet.ToWorld(Dir(-0.05f), 1f), free, mine.Id);
            e.CopyActiveFields(list, 50);
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list[0].CasterId, Is.EqualTo(mine.Id));
            Assert.That(list[0].Side, Is.EqualTo(EncounterSide.Player));
            Assert.That(list[0].Element, Is.EqualTo(Element.Fire));
            Assert.That(list[0].RadiusM, Is.GreaterThan(0f));
            Assert.That(list[0].DurationMs, Is.EqualTo(3500));
            Assert.That(list[0].RemainingMs, Is.EqualTo(3500));
        }

        [Test]
        public void Overload_switches_to_overload_form_and_reverts_when_combat_ends()
        {
            MoveDef baseMove = Move("base_move", 20, MoveShape.Front, RangeClass.Melee, 2000);
            MoveDef ovMove = Move("ov_move", 60, MoveShape.Line, RangeClass.Mid, 12000);
            MoveDef partnerMove = Move("partner_move", 35, MoveShape.Burst, RangeClass.Mid, 8000);

            var baseTrait = ScriptableObject.CreateInstance<TraitDef>();
            _created.Add(baseTrait);
            baseTrait.SetId("base_trait");
            baseTrait.Set(new TriggerDef(TriggerType.Always, false, Element.Fire, 0, 0),
                new List<EffectDef> { new EffectDef(EffectType.MasteryMod, false, Element.Fire, 10, 0, 1) });

            var ovTrait = ScriptableObject.CreateInstance<TraitDef>();
            _created.Add(ovTrait);
            ovTrait.SetId("ov_trait");
            ovTrait.Set(new TriggerDef(TriggerType.Always, false, Element.Fire, 0, 0),
                new List<EffectDef> { new EffectDef(EffectType.MasteryMod, false, Element.Fire, 40, 0, 1) });

            var partnerTrait = ScriptableObject.CreateInstance<TraitDef>();
            _created.Add(partnerTrait);
            partnerTrait.SetId("partner_trait");
            partnerTrait.Set(new TriggerDef(TriggerType.Always, false, Element.Fire, 0, 0),
                new List<EffectDef> { new EffectDef(EffectType.MasteryMod, false, Element.Fire, 15, 0, 1) });

            var resist = new int[10];
            var baseDef = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(baseDef);
            baseDef.SetId("fox_adult");
            TestWild.WithHabit(baseDef, Habit.Tolerant).Set("fox", CreatureStage.Adult, CreatureSizeClass.L, AiStyle.Charge, null, null, 0,
                new List<EvolutionMaterial>(), resist,
                new List<ElementValue> { new ElementValue(Element.Fire, 100) },
                new CreatureStats(105, 100, 100, 100, 100, 100), baseMove, baseTrait, "{}", false);

            var ovDef = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(ovDef);
            ovDef.SetId("fox_overload");
            TestWild.WithHabit(ovDef, Habit.Tolerant).Set("fox", CreatureStage.Overload, CreatureSizeClass.XL, AiStyle.Charge, null, null, 0,
                new List<EvolutionMaterial>(), resist,
                new List<ElementValue> { new ElementValue(Element.Fire, 150) },
                new CreatureStats(120, 130, 140, 140, 120, 120), ovMove, ovTrait, "{}", false);

            baseDef.SetOverloadLinks(null, ovDef);
            ovDef.SetOverloadLinks(baseDef, null);

            var e = new Encounter(_planet, _balance, new FlatSurface(), 7);
            EncounterUnit mine = e.AddEquippedCreature(baseDef, new[] { partnerMove }, new[] { partnerTrait }, Dir(0f), 100);
            e.Field(mine.Id, Dir(0f), false, 0);

            int baseMaxHp = mine.Combatant.MaxHp;
            float baseRadius = mine.BodyRadiusM;
            Assert.That(mine.Def, Is.SameAs(baseDef));
            Assert.That(mine.Moves.Count, Is.EqualTo(2));
            Assert.That(mine.Moves[0].Id, Is.EqualTo("base_move"));

            // Refused outside combat or when not unlocked.
            Assert.That(e.TryTriggerOverload(mine.Id, false, true), Is.EqualTo(OverloadResult.NotInCombat));
            Assert.That(e.TryTriggerOverload(mine.Id, true, false), Is.EqualTo(OverloadResult.NotUnlocked));

            // Triggering switches form, stats, signature move, size, and innate trait (keeping partner move & trait).
            Assert.That(e.TryTriggerOverload(mine.Id, true, true), Is.EqualTo(OverloadResult.Ok));
            Assert.That(mine.Combatant.IsOverloaded, Is.True);
            Assert.That(mine.BaseDef, Is.SameAs(baseDef));
            Assert.That(mine.Def, Is.SameAs(ovDef));
            Assert.That(mine.Moves[0].Id, Is.EqualTo("ov_move"));
            Assert.That(mine.Moves[1].Id, Is.EqualTo("partner_move"));
            Assert.That(mine.BodyRadiusM, Is.GreaterThan(baseRadius));
            Assert.That(mine.Combatant.MaxHp, Is.GreaterThan(baseMaxHp));
            Assert.That(mine.Combatant.Hp, Is.EqualTo(mine.Combatant.MaxHp));
            Assert.That(mine.Combatant.BeginCast(0, mine.Moves[0], 100, out CastContext ovCtx), Is.EqualTo(CastResult.Accepted));
            // Overload mastery 150 + ovTrait 40 + partnerTrait 15 = 205.
            Assert.That(ovCtx.MasteryPercent, Is.EqualTo(205));

            // Re-triggering in the same combat is rejected.
            Assert.That(e.TryTriggerOverload(mine.Id, true, true), Is.EqualTo(OverloadResult.AlreadyOverloaded));
            e.EndCombatOverload();

            // Reverted back to base adult form.
            Assert.That(mine.Combatant.IsOverloaded, Is.False);
            Assert.That(mine.Def, Is.SameAs(baseDef));
            Assert.That(mine.Moves[0].Id, Is.EqualTo("base_move"));
            Assert.That(mine.Moves[1].Id, Is.EqualTo("partner_move"));
            Assert.That(mine.BodyRadiusM, Is.EqualTo(baseRadius).Within(1e-5f));
            Assert.That(mine.Combatant.MaxHp, Is.EqualTo(baseMaxHp));
            Assert.That(mine.Combatant.BeginCast(0, mine.Moves[0], 20000, out CastContext revertedCtx), Is.EqualTo(CastResult.Accepted));
            // Base mastery 100 + baseTrait 10 + partnerTrait 15 = 125.
            Assert.That(revertedCtx.MasteryPercent, Is.EqualTo(125));
        }
    }
}
