using Cosmimera.Combat;
using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Food.Tests
{
    public class FoodConsumptionTests
    {
        private FoodCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = FoodCatalog.CreateDefault();
        }

        private static Combatant MakeCombatant(int hp = 90)
        {
            var coeffs = new DerivedStatsCoefficients(
                DerivedStatsCoefficients.SupportedModelVersion,
                10000, 600, 900, 100, 1000, 100, 500, 100, 36000,
                400, 100, 250, 4000, 2000, 100, 400, 150, 50, 10, 100, 250, 4000);
            var stats = new DerivedStats(coeffs);
            var rules = new EffectRulesConfig(10, 300, 1000);
            int[] mastery = new int[10];
            int[] resist = new int[10];
            return new Combatant(stats, rules, new CreatureStats(hp, 50, 50, 50, 50, 50), mastery, resist, new EffectBook(), 4);
        }

        private static PlayerVitals MakeDamagedPlayer(int damage, long nowMs)
        {
            var vitals = new PlayerVitals(new PlayerVitalsConfig(1000, 600, 400, 800));
            vitals.ApplyDamage(damage, nowMs);
            return vitals;
        }

        private static void DealDamage(Combatant victim, int power, long nowMs)
        {
            Combatant attacker = MakeCombatant();
            var move = new CastMove("test_hit", Element.Fire, MoveKind.Damage, power, 100, 0, false, default, 0);
            Assert.AreEqual(CastResult.Accepted, attacker.BeginCast(0, move, nowMs, out CastContext ctx));
            var rng = new Pcg32(7UL, 1UL);
            // Shared base evasion (creatures.md#六大能力值) can make a roll miss; hit until the damage lands.
            int before = victim.Hp;
            for (int i = 0; i < 20 && victim.Hp == before; i++) victim.ReceiveHostile(in ctx, true, ref rng, nowMs);
        }

        [Test]
        public void TryEatFood_HealsPlayerAndCreaturesAndAppliesTimedBuffs()
        {
            var inv = new FoodInventory();
            inv.AddFood("food_charred_boar_skewer", 1);

            PlayerVitals player = MakeDamagedPlayer(500, 100);
            Combatant c = MakeCombatant();
            DealDamage(c, 200, 100);
            int hpAfterHit = c.Hp;
            Assert.Less(hpAfterHit, c.MaxHp);

            var buffs = new ActiveFoodBuffs();
            ConsumeRefusalReason res = FoodConsumptionRules.TryEatFood(
                "food_charred_boar_skewer",
                _catalog,
                inv,
                player,
                new[] { c },
                buffs,
                1000,
                out ConsumeOutcome outcome);

            Assert.AreEqual(ConsumeRefusalReason.None, res);
            Assert.AreEqual(0, inv.CountFood("food_charred_boar_skewer"));
            Assert.Greater(outcome.PlayerHpRestored, 0);
            Assert.Greater(outcome.CreatureHpRestored, 0);
            Assert.Greater(player.Hp, 500);
            Assert.Greater(c.Hp, hpAfterHit);
            Assert.AreEqual(1, outcome.CombatBuffsApplied);
            Assert.AreEqual(1, c.Book.StacksOf("food_charred_boar_skewer", 0, 1000));
            Assert.AreEqual(1, buffs.CountActive(1000));
        }

        [Test]
        public void TryEatFood_RevivesFaintedCreatureAndCleansesDebuffs()
        {
            var inv = new FoodInventory();
            inv.AddFood("food_revival_herb_powder", 1);

            Combatant fainted = MakeCombatant();
            DealDamage(fainted, 50_000, 100);
            Assert.IsTrue(fainted.IsFainted);

            Combatant debuffed = MakeCombatant();
            debuffed.Book.Apply("enemy_curse", 0, new EffectSpec(EffectType.MasteryMod, false, Element.Fire, -20, 0, 1), 60_000, 500);
            debuffed.Book.Apply("enemy_slow", 0, new EffectSpec(EffectType.CdModPercent, true, default, 25, 0, 1), 60_000, 500);
            debuffed.Book.Apply("ally_haste", 0, new EffectSpec(EffectType.MoveSpeedModPercent, false, default, 20, 0, 1), 60_000, 500);

            ConsumeRefusalReason res = FoodConsumptionRules.TryEatFood(
                "food_revival_herb_powder",
                _catalog,
                inv,
                null,
                new[] { fainted, debuffed },
                null,
                1000,
                out ConsumeOutcome outcome);

            Assert.AreEqual(ConsumeRefusalReason.None, res);
            Assert.IsFalse(fainted.IsFainted);
            Assert.AreEqual(1, outcome.CreaturesRevived);
            Assert.AreEqual(2, outcome.EffectsCleansed);
            // Positive buff remains untouched
            Assert.AreEqual(1, debuffed.Book.StacksOf("ally_haste", 0, 1000));
        }

        [Test]
        public void TryEatRawIngredient_HealsEdibleAndRefusesInedible()
        {
            var inv = new FoodInventory();
            inv.AddIngredient("ing_wild_berry", 1);
            inv.AddIngredient("ing_boar_flank", 1);

            PlayerVitals player = MakeDamagedPlayer(300, 100);

            Assert.AreEqual(ConsumeRefusalReason.NotEdible,
                FoodConsumptionRules.TryEatRawIngredient("ing_boar_flank", _catalog, inv, player, null, 200, out _));
            Assert.AreEqual(1, inv.CountIngredient("ing_boar_flank"));

            Assert.AreEqual(ConsumeRefusalReason.None,
                FoodConsumptionRules.TryEatRawIngredient("ing_wild_berry", _catalog, inv, player, null, 300, out ConsumeOutcome outcome));
            Assert.AreEqual(100, outcome.PlayerHpRestored);
            Assert.AreEqual(0, inv.CountIngredient("ing_wild_berry"));
        }

        [Test]
        public void ActiveFoodBuffs_TracksMaxSlotsAndExpiresCleanly()
        {
            var buffs = new ActiveFoodBuffs();
            var catchBuff = new FoodEffectSpec(0, 0, 0, false, 0, 10, 0, 5000);
            var dropBuff = new FoodEffectSpec(0, 0, 0, false, 0, 0, 15, 10_000);
            var rollBuff = new FoodEffectSpec(0, 0, 0, false, -20, 0, 0, 15_000);
            var fourthBuff = new FoodEffectSpec(0, 0, 0, false, 0, 25, 0, 20_000);

            buffs.Apply("f1", catchBuff, 5000, 1000);
            buffs.Apply("f2", dropBuff, 10_000, 1000);
            buffs.Apply("f3", rollBuff, 15_000, 1000);
            Assert.AreEqual(3, buffs.CountActive(1000));
            Assert.AreEqual(10, buffs.GetCatchRateBonusPercent(1000));
            Assert.AreEqual(15, buffs.GetDropRateBonusPercent(1000));
            Assert.AreEqual(-20, buffs.GetRollCooldownModPercent(1000));

            // 4th distinct dish replaces earliest-expiring (f1)
            buffs.Apply("f4", fourthBuff, 20_000, 1000);
            Assert.AreEqual(3, buffs.CountActive(1000));
            Assert.AreEqual(25, buffs.GetCatchRateBonusPercent(1000));

            buffs.Tick(12_000);
            Assert.AreEqual(2, buffs.CountActive(12_000));
            Assert.AreEqual(0, buffs.GetDropRateBonusPercent(12_000));
        }

        [Test]
        public void TryEatFood_ViolatingContracts_ThrowsContractException()
        {
            var inv = new FoodInventory();
            PlayerVitals player = MakeDamagedPlayer(100, 0);

            Assert.Throws<ContractException>(() => FoodConsumptionRules.TryEatFood("", _catalog, inv, player, null, null, 0, out _));
            Assert.Throws<ContractException>(() => FoodConsumptionRules.TryEatFood("food_charred_boar_skewer", null, inv, player, null, null, 0, out _));
            Assert.Throws<ContractException>(() => FoodConsumptionRules.TryEatFood("food_charred_boar_skewer", _catalog, null, player, null, null, 0, out _));
            Assert.Throws<ContractException>(() => FoodConsumptionRules.TryEatFood("food_charred_boar_skewer", _catalog, inv, null, null, null, 0, out _));
        }
    }
}
