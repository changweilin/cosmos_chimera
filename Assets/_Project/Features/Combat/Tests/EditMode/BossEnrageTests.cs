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
    // REQ: docs/design/combat.md#BOSS-階段 (Q-C48: enrage raises move speed and shortens CD; boss HP ×3).
    public sealed class BossEnrageTests
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
            foreach (Object o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private MoveDef Move(string id, int power)
        {
            var m = ScriptableObject.CreateInstance<MoveDef>();
            _created.Add(m);
            m.SetId(id);
            m.Set(Element.Fire, MoveKind.Damage, power, null, 0, 0, 1500, 100, RangeClass.Melee, MoveShape.Front, 4000,
                MoveMovement.None, BurstOrigin.Caster);
            return m;
        }

        private CreatureDef Creature(string id, MoveDef move)
        {
            var c = ScriptableObject.CreateInstance<CreatureDef>();
            _created.Add(c);
            c.SetId(id);
            c.Set("line_" + id, CreatureStage.Juvenile, CreatureSizeClass.M, AiStyle.Charge, Temperament.Fierce, null, 0,
                new List<EvolutionMaterial>(), new int[10], new List<ElementValue> { new ElementValue(Element.Fire, 15) },
                new CreatureStats(25, 25, 25, 25), move, null, "{}", false);
            return c;
        }

        private static float3 AtMeters(float meters)
        {
            float a = meters / R;
            return math.normalize(new float3(math.sin(a), math.cos(a), 0f));
        }

        [Test]
        public void Shipped_enrage_stages_are_valid_and_strict_at_each_line()
        {
            BossEnrageTuning t = _balance.Enrage;
            Assert.That(t.StageCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(t.StageFor(100, 100), Is.EqualTo(0));
            Assert.That(t.StageFor(0, 100), Is.EqualTo(t.StageCount));
            for (int s = 1; s <= t.StageCount; s++)
            {
                Assert.That(t.MoveSpeedPercent(s), Is.InRange(0, 100));
                Assert.That(t.CdPercent(s), Is.InRange(-100, 0));
            }

            var two = new BossEnrageTuning(new[] { 66, 33 }, new[] { 10, 20 }, new[] { -10, -20 });
            Assert.That(two.StageFor(66, 100), Is.EqualTo(0), "exactly on the line is still the opening stage");
            Assert.That(two.StageFor(65, 100), Is.EqualTo(1));
            Assert.That(two.StageFor(33, 100), Is.EqualTo(1));
            Assert.That(two.StageFor(32, 100), Is.EqualTo(2));
        }

        [Test]
        public void Enrage_tuning_rejects_bad_stages()
        {
            Assert.Throws<ContractException>(() => new BossEnrageTuning(new int[0], new int[0], new int[0]));
            Assert.Throws<ContractException>(() => new BossEnrageTuning(new[] { 33, 66 }, new[] { 1, 1 }, new[] { 0, 0 }));
            Assert.Throws<ContractException>(() => new BossEnrageTuning(new[] { 100 }, new[] { 1 }, new[] { 0 }));
            Assert.Throws<ContractException>(() => new BossEnrageTuning(new[] { 50 }, new[] { -1 }, new[] { 0 }));
            Assert.Throws<ContractException>(() => new BossEnrageTuning(new[] { 50 }, new[] { 1 }, new[] { 1 }));
            Assert.Throws<ContractException>(() => new BossEnrageTuning(new[] { 50 }, new[] { 1, 2 }, new[] { 0 }));
            Assert.Throws<ContractException>(() => _balance.Enrage.StageFor(2, 1));
            Assert.Throws<ContractException>(() => _balance.Enrage.MoveSpeedPercent(0));
        }

        [Test]
        public void Phase_layer_adds_once_survives_clear_and_can_be_removed()
        {
            var book = new EffectBook();
            var totals = new ModifierTotals();
            book.SetPhaseEffects(new[]
            {
                new EffectSpec(EffectType.MoveSpeedModPercent, false, default, 15, 0, 1),
                new EffectSpec(EffectType.CdModPercent, true, default, -15, 0, 1),
            });
            book.Collect(10, 50, 100, totals);
            Assert.That(totals.MoveSpeedPercent, Is.EqualTo(15));
            Assert.That(totals.CdPercent(Element.Ice), Is.EqualTo(-15));
            book.Clear(20);
            book.Collect(20, 50, 100, totals);
            Assert.That(totals.MoveSpeedPercent, Is.EqualTo(15));
            book.SetPhaseEffects(new EffectSpec[0]);
            book.Collect(30, 50, 100, totals);
            Assert.That(totals.MoveSpeedPercent, Is.EqualTo(0));
            Assert.Throws<ContractException>(() => book.SetPhaseEffects(null));
            Assert.Throws<ContractException>(() => book.SetPhaseEffects(new[]
            {
                new EffectSpec(EffectType.HealPercentMaxHp, false, default, 0, 10, 1),
            }));
        }

        [Test]
        public void Boss_enrages_at_each_hp_line_once_and_moves_faster()
        {
            var e = new Encounter(_planet, _balance, new FlatSurface(), 7);
            EncounterUnit mine = e.AddCreature(EncounterSide.Player, Creature("m", Move("smash", 400)), null, AtMeters(0f), 100);
            CreatureDef bossDef = Creature("b", Move("bite", 1));
            EncounterUnit boss = e.AddBoss(bossDef, null, AtMeters(2f), 1);
            EncounterUnit plain = e.AddCreature(EncounterSide.Wild, bossDef, null, AtMeters(300f), 1);
            Assert.That(boss.Combatant.MaxHp, Is.EqualTo(3 * plain.Combatant.MaxHp), "Q-C48: boss HP is three times normal");
            e.Field(mine.Id, AtMeters(0f), false, 0);
            float3 feet = _planet.ToWorld(AtMeters(0f), 0f);

            int lastStage = 0;
            long now = 0;
            for (int i = 1; i <= 1000 && boss.Combatant.Hp > 0; i++)
            {
                now = i * 50L;
                e.Tick(now, 50, feet, Free, mine.Id);
                Assert.That(boss.BossStage, Is.GreaterThanOrEqualTo(lastStage), "stages never go back");
                Assert.That(boss.BossStage, Is.EqualTo(boss.Combatant.Hp > 0
                    ? _balance.Enrage.StageFor(boss.Combatant.Hp, boss.Combatant.MaxHp)
                    : lastStage), "stage follows the HP line while alive");
                lastStage = boss.BossStage;
                if (lastStage == _balance.Enrage.StageCount && boss.Combatant.Hp > 0)
                {
                    int expected = _balance.Enrage.MoveSpeedPercent(lastStage);
                    if (expected > 0)
                    {
                        Assert.That(boss.Combatant.MoveSpeedMmS(now), Is.GreaterThan(plain.Combatant.MoveSpeedMmS(now)));
                    }

                    break;
                }
            }

            Assert.That(lastStage, Is.EqualTo(_balance.Enrage.StageCount), "the fight must reach the last stage");
            Assert.That(plain.BossStage, Is.EqualTo(0));
        }
    }
}
