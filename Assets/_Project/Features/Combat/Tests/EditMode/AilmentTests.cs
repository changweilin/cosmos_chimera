using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    public sealed class AilmentTests
    {
        private EffectRulesConfig _cfg;
        private DerivedStats _stats;

        [SetUp]
        public void SetUp()
        {
            _cfg = BalanceFixture.EffectConfig();
            _stats = new DerivedStats(BalanceFixture.Coefficients());
        }

        private static int[] Uniform(int value)
        {
            var t = new int[10];
            for (int i = 0; i < 10; i++) t[i] = value;
            return t;
        }

        private Combatant MakeCombatant(int hp = 100, int resist = 100, EffectBook book = null, bool isBoss = false, int speed = 0)
        {
            var c = new Combatant(_stats, _cfg, new CreatureStats(hp, speed, 100, 100, 100, 100),
                Uniform(100), Uniform(resist), book ?? new EffectBook(), 2);
            c.Ailments.IsBoss = isBoss;
            return c;
        }

        private static CastMove AilmentMove(Element element, Ailment ailment, int buildup, int power = 20)
        {
            var effect = new EffectSpec(EffectType.ApplyAilment, false, default, 0, 0, 1, false, ailment, buildup);
            return new CastMove($"move_{ailment}", element, power > 0 ? MoveKind.Both : MoveKind.Effect,
                power, 100, 1000, true, effect, 0, Polarity.Yang, RangeClass.Melee);
        }

        [Test]
        public void Buildup_ScalesWithResistAndAilmentResist_HalvesOnBlock_ZeroOnPerfectBlock()
        {
            var book = new AilmentBook();

            // Perfect block / invulnerable adds zero buildup.
            book.ApplyBuildup(Ailment.Burn, 80, 100, 0, false, true, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 100);
            Assert.That(book.Gauge(Ailment.Burn), Is.EqualTo(0));

            // Normal block halves buildup at neutral resist (100 -> 1x, 80 / 2 = 40).
            book.ApplyBuildup(Ailment.Burn, 80, 100, 0, true, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 200);
            Assert.That(book.Gauge(Ailment.Burn), Is.EqualTo(40));

            // +50 resist halves buildup again (40 -> 20).
            book.ApplyBuildup(Ailment.Burn, 40, 150, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 300);
            Assert.That(book.Gauge(Ailment.Burn), Is.EqualTo(60));

            // +50 ailment_resist_mod also halves buildup (40 -> 20).
            book.ApplyBuildup(Ailment.Burn, 40, 100, 50, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 400);
            Assert.That(book.Gauge(Ailment.Burn), Is.EqualTo(80));
        }

        [Test]
        public void Gauge_DecaysAfter3Seconds_AndThresholdIncreasesWithDR()
        {
            var book = new AilmentBook();
            book.ApplyBuildup(Ailment.Burn, 60, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 1000);
            Assert.That(book.Gauge(Ailment.Burn), Is.EqualTo(60));

            // Before 3000 ms delay (at 3999 ms), no decay.
            book.Step(3000, 1000, 100, 100, 100, 100, 100);
            Assert.That(book.Gauge(Ailment.Burn), Is.EqualTo(60));

            // At 4000 ms (3000 ms after hit), decays by 15.
            book.Step(4000, 1000, 100, 100, 100, 100, 100);
            Assert.That(book.Gauge(Ailment.Burn), Is.EqualTo(45));

            // Triggering Burn raises threshold by +35% (100 -> 135 -> 170 -> 205 -> 240 cap).
            Assert.That(book.Threshold(Ailment.Burn), Is.EqualTo(100));
            AilmentActionResult res = book.ApplyBuildup(Ailment.Burn, 100, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 5000);
            Assert.That(res.Triggered, Is.True);
            Assert.That(book.Threshold(Ailment.Burn), Is.EqualTo(135));
        }

        [Test]
        public void Boss_HasDoubleThreshold_AndDowngradedFreeze_AndCappedToxin()
        {
            var bossBook = new AilmentBook(isBoss: true);
            Assert.That(bossBook.Threshold(Ailment.Frostbite), Is.EqualTo(200));

            // 200 buildup triggers Frostbite on BOSS, but IsFrozen stays false (downgraded to 3s heavy slow/CD).
            bossBook.ApplyBuildup(Ailment.Frostbite, 200, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 3000, 1000);
            Assert.That(bossBook.IsActive(Ailment.Frostbite, 1500), Is.True);
            Assert.That(bossBook.IsFrozen(1500), Is.False);

            var totals = new ModifierTotals();
            bossBook.CollectModifiers(totals, 1500);
            Assert.That(totals.MoveSpeedPercent, Is.EqualTo(-40));
            Assert.That(totals.CdPercent(Element.Fire), Is.EqualTo(40));

            // Toxin on BOSS caps MaxHp at 1/3 (3000 -> 1000, 2.5% = 25 per tick).
            bossBook.ApplyBuildup(Ailment.Toxin, 200, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 3000, 2000);
            AilmentStepResult step = bossBook.Step(3000, 3000, 100, 100, 100, 100, 100);
            Assert.That(step.ToxinDamage, Is.EqualTo(25));
        }

        [Test]
        public void AllTenAilments_ApplyTheirDistinctCombatModifiers()
        {
            var totals = new ModifierTotals();
            var book = new AilmentBook();

            // Sunder: BlockReduction -25
            book.ApplyBuildup(Ailment.Sunder, 100, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 100);
            totals.Clear();
            book.CollectModifiers(totals, 200);
            Assert.That(totals.BlockReductionDelta, Is.EqualTo(-25));

            // Soaked: MoveSpeed -15
            book.Clear();
            book.ApplyBuildup(Ailment.Soaked, 100, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 100);
            totals.Clear();
            book.CollectModifiers(totals, 200);
            Assert.That(totals.MoveSpeedPercent, Is.EqualTo(-15));

            // Weighted: MoveSpeed -20
            book.Clear();
            book.ApplyBuildup(Ailment.Weighted, 100, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 100);
            totals.Clear();
            book.CollectModifiers(totals, 200);
            Assert.That(totals.MoveSpeedPercent, Is.EqualTo(-20));

            // Dazzle: Accuracy -25
            book.Clear();
            book.ApplyBuildup(Ailment.Dazzle, 100, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 100);
            totals.Clear();
            book.CollectModifiers(totals, 200);
            Assert.That(totals.AccuracyDelta, Is.EqualTo(-25));

            // Eclipse: All resists -15, Range -20
            book.Clear();
            book.ApplyBuildup(Ailment.Eclipse, 100, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 100);
            totals.Clear();
            book.CollectModifiers(totals, 200);
            Assert.That(totals.ResistDelta(Element.Fire), Is.EqualTo(-15));
            Assert.That(totals.ResistDelta(Element.Water), Is.EqualTo(-15));
            Assert.That(totals.RangePercent, Is.EqualTo(-20));
        }

        [Test]
        public void Burn_ReducesAttackAndIsShortenedByRoll_ToxinHalvesHealing()
        {
            var rng = new Pcg32(1, 1);
            var accBook = new EffectBook("acc", new TriggerSpec(TriggerType.Always, false, default, 0, 0),
                new[] { new EffectSpec(EffectType.AccuracyMod, false, default, 100, 0, 1) });
            Combatant attacker = MakeCombatant(book: accBook);
            Combatant target = MakeCombatant(hp: 200);

            CastMove burnMove = AilmentMove(Element.Fire, Ailment.Burn, 100, power: 10);
            attacker.BeginCast(0, burnMove, 100, out CastContext ctx);
            target.ReceiveHostile(ctx, true, ref rng, 100);
            Assert.That(target.Ailments.IsActive(Ailment.Burn, 200), Is.True);

            // Attack reduced by 10 while Burn is active.
            target.BeginCast(0, AilmentMove(Element.Fire, Ailment.Burn, 10, power: 10), 200, out CastContext burnCast);
            Assert.That(burnCast.Attack, Is.EqualTo(target.Core.Attack(Polarity.Yang) - 10));

            // 3 rolls x 2000 ms extinguishes 6000 ms Burn.
            target.Ailments.OnRoll(300);
            target.Ailments.OnRoll(400);
            target.Ailments.OnRoll(500);
            Assert.That(target.Ailments.IsActive(Ailment.Burn, 600), Is.False);

            // Toxin halves healing (ensure enough missing HP so neither heal caps at MaxHp).
            target.ApplyDirectDamage(300, 650);
            int hpBefore = target.Hp;
            target.Ailments.ApplyBuildup(Ailment.Toxin, 100, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, target.MaxHp, 700);
            int healedWithToxin = target.HealByPercent(10, 750);
            target.Ailments.Clear();
            Combatant cleanTarget = MakeCombatant(hp: 200);
            cleanTarget.ApplyDirectDamage(cleanTarget.MaxHp - hpBefore, 700);
            int healedClean = cleanTarget.HealByPercent(10, 750);
            Assert.That(healedWithToxin, Is.LessThan(healedClean));
        }

        [Test]
        public void CrossElementReactions_FireAsSpecified()
        {
            var book = new AilmentBook();

            // 1. Freeze + Earth -> Shatter (clears freeze, deals damage + stagger).
            book.ApplyBuildup(Ailment.Frostbite, 100, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 100);
            Assert.That(book.IsFrozen(200), Is.True);
            AilmentActionResult shatter = book.OnElementalHit(Element.Earth, 100, 1000, 100, 100, LevelCurve.PpmOne, 200);
            Assert.That(shatter.Reaction, Is.EqualTo(AilmentReactionKind.Shatter));
            Assert.That(shatter.BonusDamage, Is.GreaterThan(0));
            Assert.That(shatter.StaggerDamage, Is.GreaterThan(0));
            Assert.That(book.IsFrozen(300), Is.False);

            // 2. Freeze + Fire -> Melt Burst (clears freeze, applies Soaked).
            book.Clear();
            book.ApplyBuildup(Ailment.Frostbite, 100, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 100);
            AilmentActionResult melt = book.OnElementalHit(Element.Fire, 100, 1000, 100, 100, LevelCurve.PpmOne, 200);
            Assert.That(melt.Reaction, Is.EqualTo(AilmentReactionKind.MeltBurst));
            Assert.That(book.IsActive(Ailment.Soaked, 300), Is.True);

            // 3. Burn + Water -> Steam Extinguish.
            book.Clear();
            book.ApplyBuildup(Ailment.Burn, 100, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 100);
            AilmentActionResult steam = book.OnElementalHit(Element.Water, 100, 1000, 100, 100, LevelCurve.PpmOne, 200);
            Assert.That(steam.Reaction, Is.EqualTo(AilmentReactionKind.SteamExtinguish));
            Assert.That(book.IsActive(Ailment.Burn, 300), Is.False);

            // 4. Toxin + Fire -> Toxic Ignition.
            book.Clear();
            book.ApplyBuildup(Ailment.Toxin, 100, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 100);
            AilmentActionResult toxicIgn = book.OnElementalHit(Element.Fire, 100, 1000, 100, 100, LevelCurve.PpmOne, 200);
            Assert.That(toxicIgn.Reaction, Is.EqualTo(AilmentReactionKind.ToxicIgnition));
            Assert.That(toxicIgn.BonusDamage, Is.GreaterThan(0));
            Assert.That(book.IsActive(Ailment.Toxin, 300), Is.False);
            Assert.That(book.IsActive(Ailment.Burn, 300), Is.True);

            // 5. Dazzle + Eclipse -> Light-Shadow Clash.
            book.Clear();
            book.ApplyBuildup(Ailment.Dazzle, 100, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 100);
            AilmentActionResult clash = book.ApplyBuildup(Ailment.Eclipse, 100, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 200);
            Assert.That(clash.Reaction, Is.EqualTo(AilmentReactionKind.LightShadowClash));
            Assert.That(clash.BonusDamage, Is.GreaterThan(0));
            Assert.That(book.IsActive(Ailment.Dazzle, 300), Is.False);
            Assert.That(book.IsActive(Ailment.Eclipse, 300), Is.False);
        }

        [Test]
        public void Contracts_RejectInvalidArguments()
        {
            var book = new AilmentBook();
            Assert.Throws<ContractException>(() => book.IsActive((Ailment)99, 0));
            Assert.Throws<ContractException>(() => book.ApplyBuildup(Ailment.Burn, 0, 100, 0, false, false, 1000, 1000, 100, 100, LevelCurve.PpmOne, 1000, 0));
            Assert.Throws<ContractException>(() => new EffectSpec(EffectType.ApplyAilment, false, default, 0, 0, 1, false, Ailment.Burn, 0));
            Assert.Throws<ContractException>(() => new EffectSpec(EffectType.ApplyAilment, true, default, 0, 0, 1, false, Ailment.Burn, 50));
        }
    }
}
