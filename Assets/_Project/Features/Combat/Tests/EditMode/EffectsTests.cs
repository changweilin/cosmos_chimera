using Cosmimera.Core.Contracts;
using Cosmimera.GameData;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    public sealed class EffectsTests
    {
        private EffectRulesConfig _cfg;
        private DerivedStats _stats;

        [SetUp]
        public void SetUp()
        {
            _cfg = BalanceFixture.EffectConfig();
            _stats = new DerivedStats(BalanceFixture.Coefficients());
        }

        private static EffectSpec Mod(EffectType t, int delta, int maxStacks = 1, Element e = Element.Fire) =>
            new EffectSpec(t, false, e, delta, 0, maxStacks);

        private static EffectSpec Heal(int percent) =>
            new EffectSpec(EffectType.HealPercentMaxHp, false, default, 0, percent, 1);

        private static EffectBook TraitBook(TriggerSpec trigger, params EffectSpec[] effects) =>
            new EffectBook("trait_x", trigger, effects);

        private static ModifierTotals Collect(EffectBook book, long now, int hp = 100, int maxHp = 100)
        {
            var totals = new ModifierTotals();
            book.Collect(now, hp, maxHp, totals);
            return totals;
        }

        // ---- EffectSpec / TriggerSpec contracts ----

        [Test]
        public void EffectSpec_rejects_out_of_domain_values()
        {
            Assert.Throws<ContractException>(() => Mod(EffectType.MasteryMod, 101));
            Assert.Throws<ContractException>(() => Mod(EffectType.MasteryMod, -101));
            Assert.Throws<ContractException>(() => Mod(EffectType.MasteryMod, 10, 0));
            Assert.Throws<ContractException>(() => Mod(EffectType.MasteryMod, 10, EffectDef.MaxStacksLimit + 1));
            Assert.Throws<ContractException>(() => new EffectSpec(EffectType.MasteryMod, true, Element.Fire, 1, 0, 1));
            Assert.Throws<ContractException>(() => new EffectSpec(EffectType.MasteryMod, false, Element.Fire, 1, 5, 1));
            Assert.Throws<ContractException>(() => new EffectSpec(EffectType.HealPercentMaxHp, false, default, 0, 0, 1));
            Assert.Throws<ContractException>(() => new EffectSpec(EffectType.HealPercentMaxHp, false, default, 0, 101, 1));
            Assert.Throws<ContractException>(() => new EffectSpec(EffectType.HealPercentMaxHp, false, default, 0, 10, 2));
            Assert.Throws<ContractException>(() => new EffectSpec(EffectType.HealPercentMaxHp, false, default, 5, 10, 1));
            Assert.Throws<ContractException>(() => new EffectSpec((EffectType)99, false, default, 0, 0, 1));
            Assert.DoesNotThrow(() => new EffectSpec(EffectType.CdModPercent, true, default, -25, 0, 3));
        }

        [Test]
        public void TriggerSpec_rejects_parameters_of_another_type()
        {
            Assert.Throws<ContractException>(() => new TriggerSpec(TriggerType.OnHpBelow, false, default, 0, 0));
            Assert.Throws<ContractException>(() => new TriggerSpec(TriggerType.OnHpBelow, false, default, 30, 1000));
            Assert.Throws<ContractException>(() => new TriggerSpec(TriggerType.OnSummon, false, default, 0, 0));
            Assert.Throws<ContractException>(() => new TriggerSpec(TriggerType.OnSummon, false, default, 0, EffectBook.MaxDurationMs + 1));
            Assert.Throws<ContractException>(() => new TriggerSpec(TriggerType.Always, false, default, 0, 1));
            Assert.Throws<ContractException>(() => new TriggerSpec(TriggerType.OnMoveUsed, true, default, 0, 1000));
            Assert.DoesNotThrow(() => new TriggerSpec(TriggerType.OnHitTaken, true, default, 0, 1000));
        }

        // ---- Stacking ----

        [Test]
        public void Max_stacks_one_refreshes_without_adding()
        {
            var book = new EffectBook();
            EffectSpec buff = Mod(EffectType.MasteryMod, 20);

            Assert.AreEqual(ApplyOutcome.Started, book.Apply("move_a", 0, buff, 5000, 0));
            Assert.AreEqual(ApplyOutcome.Refreshed, book.Apply("move_a", 0, buff, 5000, 4000));

            Assert.AreEqual(20, Collect(book, 8999).MasteryDelta(Element.Fire), "refreshed window runs to 9000");
            Assert.AreEqual(0, Collect(book, 9000).MasteryDelta(Element.Fire), "half-open window");
        }

        [Test]
        public void Stacks_grow_to_max_then_only_refresh()
        {
            var book = new EffectBook();
            EffectSpec debuff = Mod(EffectType.ResistMod, -10, 3, Element.Water);

            Assert.AreEqual(ApplyOutcome.Started, book.Apply("move_b", 0, debuff, 1000, 0));
            Assert.AreEqual(ApplyOutcome.Stacked, book.Apply("move_b", 0, debuff, 1000, 100));
            Assert.AreEqual(ApplyOutcome.Stacked, book.Apply("move_b", 0, debuff, 1000, 200));
            Assert.AreEqual(ApplyOutcome.Refreshed, book.Apply("move_b", 0, debuff, 1000, 300));

            Assert.AreEqual(3, book.StacksOf("move_b", 0, 300));
            Assert.AreEqual(-30, Collect(book, 1299).ResistDelta(Element.Water));
            Assert.AreEqual(0, Collect(book, 1300).ResistDelta(Element.Water), "the whole stack expires together");
        }

        [Test]
        public void Reapplying_after_expiry_starts_over_at_one_stack()
        {
            var book = new EffectBook();
            EffectSpec s = Mod(EffectType.MasteryMod, 10, 5);
            book.Apply("m", 0, s, 1000, 0);
            book.Apply("m", 0, s, 1000, 500);

            Assert.AreEqual(ApplyOutcome.Started, book.Apply("m", 0, s, 1000, 1500));
            Assert.AreEqual(1, book.StacksOf("m", 0, 1500));
        }

        [Test]
        public void Different_sources_and_effect_indices_add_up()
        {
            var book = new EffectBook();
            book.Apply("move_a", 0, Mod(EffectType.MasteryMod, 20), 5000, 0);
            book.Apply("move_b", 0, Mod(EffectType.MasteryMod, 15), 5000, 0);
            book.Apply("move_a", 1, Mod(EffectType.MasteryMod, 5), 5000, 0);

            Assert.AreEqual(40, Collect(book, 10).MasteryDelta(Element.Fire));
            Assert.AreEqual(0, Collect(book, 10).MasteryDelta(Element.Water));
        }

        [Test]
        public void Lowered_max_stacks_clamps_an_existing_stack()
        {
            var book = new EffectBook();
            book.Apply("m", 0, Mod(EffectType.MasteryMod, 10, 3), 1000, 0);
            book.Apply("m", 0, Mod(EffectType.MasteryMod, 10, 3), 1000, 1);
            book.Apply("m", 0, Mod(EffectType.MasteryMod, 10, 3), 1000, 2);

            Assert.AreEqual(ApplyOutcome.Refreshed, book.Apply("m", 0, Mod(EffectType.MasteryMod, 10, 2), 1000, 3));
            Assert.AreEqual(2, book.StacksOf("m", 0, 3));
        }

        [Test]
        public void Cd_any_and_element_scopes_sum_for_a_move()
        {
            var book = new EffectBook();
            book.Apply("a", 0, new EffectSpec(EffectType.CdModPercent, true, default, -10, 0, 1), 1000, 0);
            book.Apply("b", 0, new EffectSpec(EffectType.CdModPercent, false, Element.Thunder, -25, 0, 1), 1000, 0);
            ModifierTotals t = Collect(book, 0);

            Assert.AreEqual(-35, t.CdPercent(Element.Thunder));
            Assert.AreEqual(-10, t.CdPercent(Element.Fire));
        }

        [Test]
        public void Every_scalar_effect_lands_in_its_own_sum()
        {
            var book = new EffectBook();
            book.Apply("e", 0, Mod(EffectType.EvasionModPercent, 25), 1000, 0);
            book.Apply("s", 0, Mod(EffectType.MoveSpeedModPercent, -20), 1000, 0);
            book.Apply("r", 0, Mod(EffectType.RangeModPercent, 30), 1000, 0);
            ModifierTotals t = Collect(book, 0);

            Assert.AreEqual(25, t.EvasionPercent);
            Assert.AreEqual(-20, t.MoveSpeedPercent);
            Assert.AreEqual(30, t.RangePercent);
            t.Clear();
            Assert.AreEqual(0, t.EvasionPercent + t.MoveSpeedPercent + t.RangePercent);
        }

        [Test]
        public void Apply_preconditions()
        {
            var book = new EffectBook();
            EffectSpec s = Mod(EffectType.MasteryMod, 1);
            Assert.Throws<ContractException>(() => book.Apply("", 0, s, 1000, 0));
            Assert.Throws<ContractException>(() => book.Apply(null, 0, s, 1000, 0));
            Assert.Throws<ContractException>(() => book.Apply("m", -1, s, 1000, 0));
            Assert.Throws<ContractException>(() => book.Apply("m", 0, Heal(10), 1000, 0));
            Assert.Throws<ContractException>(() => book.Apply("m", 0, default, 1000, 0));
            Assert.Throws<ContractException>(() => book.Apply("m", 0, s, 0, 0));
            Assert.Throws<ContractException>(() => book.Apply("m", 0, s, EffectBook.MaxDurationMs + 1, 0));
            Assert.Throws<ContractException>(() => book.Apply("m", 0, s, 1000, -1));
            Assert.Throws<ContractException>(() => book.Apply("m", 0, s, 1000, CombatTime.MaxMs + 1));
        }

        [Test]
        public void Time_cannot_go_backwards_and_queries_do_not_advance_it()
        {
            var book = new EffectBook();
            book.Apply("m", 0, Mod(EffectType.MasteryMod, 1), 1000, 500);

            Collect(book, 900);
            Assert.DoesNotThrow(() => book.Apply("m", 0, Mod(EffectType.MasteryMod, 1), 1000, 600), "Collect must not move the watermark");
            Assert.Throws<ContractException>(() => Collect(book, 599));
            Assert.Throws<ContractException>(() => book.StacksOf("m", 0, 599));
            Assert.Throws<ContractException>(() => book.Clear(599));
        }

        [Test]
        public void Clear_removes_every_timed_effect()
        {
            var book = new EffectBook();
            book.Apply("m", 0, Mod(EffectType.MasteryMod, 30), 10000, 0);
            book.Clear(10);

            Assert.AreEqual(0, Collect(book, 10).MasteryDelta(Element.Fire));
        }

        [Test]
        public void Collect_preconditions()
        {
            var book = new EffectBook();
            Assert.Throws<ContractException>(() => book.Collect(0, 1, 1, null));
            Assert.Throws<ContractException>(() => Collect(book, 0, 1, 0));
            Assert.Throws<ContractException>(() => Collect(book, 0, -1, 10));
            Assert.Throws<ContractException>(() => Collect(book, 0, 11, 10));
        }

        // ---- Trait triggers ----

        [Test]
        public void Always_trait_applies_continuously()
        {
            EffectBook book = TraitBook(new TriggerSpec(TriggerType.Always, false, default, 0, 0), Mod(EffectType.MasteryMod, 7));
            Assert.IsTrue(book.HasTrait);
            Assert.AreEqual(7, Collect(book, 0).MasteryDelta(Element.Fire));
            Assert.AreEqual(7, Collect(book, 1_000_000).MasteryDelta(Element.Fire));
        }

        [TestCase(30, 29, 100, true)]
        [TestCase(30, 30, 100, false)]
        [TestCase(30, 299, 1000, true)]
        [TestCase(30, 300, 1000, false)]
        [TestCase(1, 0, 1, true)]
        public void Hp_below_is_strict(int percent, int hp, int maxHp, bool active)
        {
            EffectBook book = TraitBook(new TriggerSpec(TriggerType.OnHpBelow, false, default, percent, 0),
                Mod(EffectType.MasteryMod, 20));
            Assert.AreEqual(active ? 20 : 0, Collect(book, 0, hp, maxHp).MasteryDelta(Element.Fire));
        }

        [TestCase(70, 71, 100, true)]
        [TestCase(70, 70, 100, false)]
        [TestCase(100, 100, 100, false)]
        public void Hp_above_is_strict(int percent, int hp, int maxHp, bool active)
        {
            EffectBook book = TraitBook(new TriggerSpec(TriggerType.OnHpAbove, false, default, percent, 0),
                Mod(EffectType.MasteryMod, 20));
            Assert.AreEqual(active ? 20 : 0, Collect(book, 0, hp, maxHp).MasteryDelta(Element.Fire));
        }

        [Test]
        public void On_summon_opens_a_window_on_every_entry()
        {
            EffectBook book = TraitBook(new TriggerSpec(TriggerType.OnSummon, false, default, 0, 3000),
                Mod(EffectType.MoveSpeedModPercent, 20));
            Assert.AreEqual(0, Collect(book, 0).MoveSpeedPercent, "nothing before the event");

            book.NotifySummoned(100, out int heal);
            Assert.AreEqual(0, heal);
            Assert.AreEqual(20, Collect(book, 3099).MoveSpeedPercent);
            Assert.AreEqual(0, Collect(book, 3100).MoveSpeedPercent);

            book.NotifySwappedIn(5000, out _);
            Assert.AreEqual(0, Collect(book, 5000).MoveSpeedPercent, "on_summon does not listen to the swap notification");
        }

        [Test]
        public void On_ally_swap_fires_only_on_swap_in()
        {
            EffectBook book = TraitBook(new TriggerSpec(TriggerType.OnAllySwap, false, default, 0, 2000),
                Mod(EffectType.EvasionModPercent, 15));
            book.NotifySummoned(0, out _);
            Assert.AreEqual(0, Collect(book, 0).EvasionPercent);

            book.NotifySwappedIn(10, out _);
            Assert.AreEqual(15, Collect(book, 10).EvasionPercent);
        }

        [Test]
        public void On_hit_taken_matches_element_or_any()
        {
            EffectBook fire = TraitBook(new TriggerSpec(TriggerType.OnHitTaken, false, Element.Fire, 0, 1000),
                Mod(EffectType.ResistMod, 20));
            fire.NotifyHitTaken(Element.Water, 0, out _);
            Assert.AreEqual(0, Collect(fire, 0).ResistDelta(Element.Fire));
            fire.NotifyHitTaken(Element.Fire, 1, out _);
            Assert.AreEqual(20, Collect(fire, 1).ResistDelta(Element.Fire));

            EffectBook any = TraitBook(new TriggerSpec(TriggerType.OnHitTaken, true, default, 0, 1000),
                Mod(EffectType.ResistMod, 20));
            any.NotifyHitTaken(Element.Shadow, 0, out _);
            Assert.AreEqual(20, Collect(any, 0).ResistDelta(Element.Fire));
        }

        [Test]
        public void On_move_used_matches_element_and_stacks_per_data()
        {
            EffectBook book = TraitBook(new TriggerSpec(TriggerType.OnMoveUsed, false, Element.Thunder, 0, 4000),
                Mod(EffectType.MasteryMod, 10, 3, Element.Thunder));
            book.NotifyMoveUsed(Element.Fire, 0, out _);
            Assert.AreEqual(0, Collect(book, 0).MasteryDelta(Element.Thunder));

            for (int i = 1; i <= 5; i++)
            {
                book.NotifyMoveUsed(Element.Thunder, i, out _);
            }

            Assert.AreEqual(30, Collect(book, 5).MasteryDelta(Element.Thunder));
            Assert.AreEqual(3, book.StacksOf("trait_x", 0, 5));
        }

        [Test]
        public void Event_triggered_heal_is_returned_not_stored()
        {
            EffectBook book = TraitBook(new TriggerSpec(TriggerType.OnSummon, false, default, 0, 1000),
                Heal(15), Mod(EffectType.MasteryMod, 5));
            book.NotifySummoned(0, out int heal);

            Assert.AreEqual(15, heal);
            Assert.AreEqual(5, Collect(book, 0).MasteryDelta(Element.Fire));
        }

        [Test]
        public void Trait_book_preconditions()
        {
            var always = new TriggerSpec(TriggerType.Always, false, default, 0, 0);
            Assert.Throws<ContractException>(() => new EffectBook("", always, new[] { Mod(EffectType.MasteryMod, 1) }));
            Assert.Throws<ContractException>(() => new EffectBook("t", always, null));
            Assert.Throws<ContractException>(() => new EffectBook("t", always, new EffectSpec[0]));
            Assert.Throws<ContractException>(() => new EffectBook("t", always, new EffectSpec[9]));
            Assert.Throws<ContractException>(() => new EffectBook("t", always, new[] { Heal(10) }), "a heal needs an event trigger");
        }

        [Test]
        public void Book_without_trait_ignores_events()
        {
            var book = new EffectBook();
            book.NotifySummoned(0, out int a);
            book.NotifyHitTaken(Element.Fire, 1, out int b);
            book.NotifyMoveUsed(Element.Fire, 2, out int c);
            book.NotifySwappedIn(3, out int d);

            Assert.IsFalse(book.HasTrait);
            Assert.AreEqual(0, a + b + c + d);
            Assert.Throws<ContractException>(() => book.NotifySummoned(2, out _));
        }

        // ---- EffectMath ----

        [Test]
        public void Mastery_and_resist_clamp_to_damage_model_domain()
        {
            Assert.AreEqual(0, EffectMath.EffectiveMastery(20, -50));
            Assert.AreEqual(45, EffectMath.EffectiveMastery(20, 25));
            Assert.AreEqual(DamageModel.MaxMasteryPercent, EffectMath.EffectiveMastery(20, long.MaxValue / 2));
            Assert.Throws<ContractException>(() => EffectMath.EffectiveMastery(-1, 0));

            Assert.AreEqual(100, EffectMath.EffectiveResist(90, 30));
            Assert.AreEqual(-100, EffectMath.EffectiveResist(-90, -30));
            Assert.AreEqual(5, EffectMath.EffectiveResist(25, -20));
        }

        [Test]
        public void Multiplier_is_clamped_to_config_band()
        {
            Assert.AreEqual(100, EffectMath.Multiplier(0, _cfg));
            Assert.AreEqual(_cfg.MultiplierMinPercent, EffectMath.Multiplier(-1000, _cfg));
            Assert.AreEqual(_cfg.MultiplierMaxPercent, EffectMath.Multiplier(long.MaxValue, _cfg));
            Assert.AreEqual(_cfg.MultiplierMinPercent, EffectMath.Multiplier(long.MinValue, _cfg));
            Assert.Throws<ContractException>(() => EffectMath.Multiplier(0, null));
        }

        [Test]
        public void ApplyPercent_is_identity_at_zero_and_monotone()
        {
            for (int v = 0; v <= 5000; v += 37)
            {
                Assert.AreEqual(v, EffectMath.ApplyPercent(v, 0, _cfg));
                int prev = -1;
                for (int s = -150; s <= 300; s += 5)
                {
                    int r = EffectMath.ApplyPercent(v, s, _cfg);
                    Assert.GreaterOrEqual(r, prev);
                    Assert.GreaterOrEqual(r, 0);
                    prev = r;
                }
            }

            Assert.AreEqual(250, EffectMath.ApplyPercent(200, 25, _cfg));
            Assert.AreEqual(int.MaxValue, EffectMath.ApplyPercent(int.MaxValue, 1000, _cfg));
            Assert.Throws<ContractException>(() => EffectMath.ApplyPercent(-1, 0, _cfg));
        }

        [Test]
        public void Cooldown_identity_floor_and_ceil()
        {
            int floor = _stats.Coefficients.CdFloorMs;
            for (int spi = 0; spi <= 400; spi += 50)
            {
                for (int cd = 0; cd <= 20000; cd += 1250)
                {
                    Assert.AreEqual(_stats.EffectiveCooldownMs(cd, spi), EffectMath.EffectiveCooldownMs(_stats, cd, spi, 0, _cfg));
                    Assert.GreaterOrEqual(EffectMath.EffectiveCooldownMs(_stats, cd, spi, -1000, _cfg), floor);
                    Assert.GreaterOrEqual(EffectMath.EffectiveCooldownMs(_stats, cd, spi, 20, _cfg),
                        EffectMath.EffectiveCooldownMs(_stats, cd, spi, 0, _cfg));
                }
            }

            int baseCd = _stats.EffectiveCooldownMs(1001, 0);
            int expected = (int)(((long)baseCd * 90 + 99) / 100);
            Assert.AreEqual(expected < floor ? floor : expected, EffectMath.EffectiveCooldownMs(_stats, 1001, 0, -10, _cfg));
            Assert.Throws<ContractException>(() => EffectMath.EffectiveCooldownMs(null, 1000, 0, 0, _cfg));
        }

        [Test]
        public void Evasion_is_capped_and_speed_is_relative()
        {
            int cap = _stats.Coefficients.EvasionCapPermille;
            Assert.AreEqual(cap, EffectMath.EvasionPermille(_stats, 10000, 200, _cfg));
            Assert.AreEqual(_stats.EvasionPermille(50), EffectMath.EvasionPermille(_stats, 50, 0, _cfg));
            Assert.LessOrEqual(EffectMath.EvasionPermille(_stats, 50, -20, _cfg), _stats.EvasionPermille(50));

            int speed = _stats.MoveSpeedMmS(100);
            Assert.AreEqual((speed * 125 + 50) / 100, EffectMath.MoveSpeedMmS(_stats, 100, 25, _cfg));
            Assert.Throws<ContractException>(() => EffectMath.EvasionPermille(null, 0, 0, _cfg));
            Assert.Throws<ContractException>(() => EffectMath.MoveSpeedMmS(null, 0, 0, _cfg));
        }

        [Test]
        public void ApplyRange_scales_reach_and_cast_only()
        {
            HitVolumeDefaults d = BalanceFixture.HitDefaults();
            HitVolume line = MoveGeometry.Build(MoveShape.Line, MoveMovement.None, RangeClass.Mid, default, 12000, 0, _stats, d);
            HitVolume shorter = EffectMath.ApplyRange(line, RangeClass.Mid, -25, _stats, _cfg);
            Assert.AreEqual((line.ReachMm * 75 + 50) / 100, shorter.ReachMm);
            Assert.AreEqual(line.WidthMm, shorter.WidthMm);
            Assert.AreEqual(line.HalfHeightMm, shorter.HalfHeightMm);

            HitVolume field = MoveGeometry.Build(MoveShape.Field, MoveMovement.None, RangeClass.Mid, default, 12000, 0, _stats, d);
            HitVolume farField = EffectMath.ApplyRange(field, RangeClass.Mid, 50, _stats, _cfg);
            Assert.AreEqual(field.ReachMm, farField.ReachMm, "a field's radius is not range");
            Assert.Greater(farField.CastDistanceMm, field.CastDistanceMm);

            int cap = _stats.Coefficients.RangedCapMm;
            HitVolume longLine = MoveGeometry.Build(MoveShape.Line, MoveMovement.None, RangeClass.Long, default, cap, 0, _stats, d);
            Assert.AreEqual(cap, EffectMath.ApplyRange(longLine, RangeClass.Long, 100, _stats, _cfg).ReachMm, "horizon cap");

            HitVolume dash = MoveGeometry.Build(MoveShape.Line, MoveMovement.Dash, RangeClass.Melee, default, 5000, 0, _stats, d);
            HitVolume dashMod = EffectMath.ApplyRange(dash, RangeClass.Melee, 100, _stats, _cfg);
            Assert.AreEqual(dash.ReachMm, dashMod.ReachMm);
            Assert.AreEqual(dash.DashDistanceMm, dashMod.DashDistanceMm);

            HitVolume same = EffectMath.ApplyRange(line, RangeClass.Mid, 0, _stats, _cfg);
            Assert.AreEqual(line.ReachMm, same.ReachMm);
            Assert.Throws<ContractException>(() => EffectMath.ApplyRange(line, RangeClass.Mid, 10, null, _cfg));
        }

        [Test]
        public void Heal_amount_rounds_and_has_minimum_one()
        {
            Assert.AreEqual(250, EffectMath.HealAmount(1000, 25));
            Assert.AreEqual(1, EffectMath.HealAmount(1, 1));
            Assert.AreEqual(0, EffectMath.HealAmount(1000, 0));
            Assert.AreEqual(int.MaxValue, EffectMath.HealAmount(int.MaxValue, 100));
            Assert.Throws<ContractException>(() => EffectMath.HealAmount(0, 10));
            Assert.Throws<ContractException>(() => EffectMath.HealAmount(10, -1));
        }

        [Test]
        public void Config_preconditions()
        {
            Assert.Throws<ContractException>(() => new EffectRulesConfig(-1, 300, 1000));
            Assert.Throws<ContractException>(() => new EffectRulesConfig(101, 300, 1000));
            Assert.Throws<ContractException>(() => new EffectRulesConfig(10, 99, 1000));
            Assert.Throws<ContractException>(() => new EffectRulesConfig(10, 1001, 1000));
            Assert.Throws<ContractException>(() => new EffectRulesConfig(10, 300, 0));
            Assert.AreEqual(1000, _cfg.FieldTickMs);
        }

        // ---- Field timeline (Q-C17) ----

        [TestCase(6000, 1000, 6)]
        [TestCase(6001, 1000, 7)]
        [TestCase(1, 1000, 1)]
        [TestCase(999, 1000, 1)]
        public void Field_ticks_at_placement_then_every_tick(int lifetime, int tick, int total)
        {
            var f = new FieldTimeline(500, lifetime, tick);
            Assert.AreEqual(total, f.TotalTicks);
            Assert.AreEqual(total, f.TicksDue(499, CombatTime.MaxMs));
            Assert.AreEqual(1, f.TicksDue(499, 500), "tick 0 at placement");
            Assert.AreEqual(0, f.TicksDue(500, 500 + tick - 1));
        }

        [Test]
        public void Field_ticks_are_counted_once_whatever_the_step()
        {
            var f = new FieldTimeline(1000, 6000, 1000);
            foreach (int step in new[] { 1, 7, 333, 1000, 2500 })
            {
                int sum = 0;
                long prev = 999;
                for (long t = 1000; t < 9000; t += step)
                {
                    sum += f.TicksDue(prev, t);
                    prev = t;
                }

                sum += f.TicksDue(prev, 9000);
                Assert.AreEqual(6, sum, $"step {step}");
            }
        }

        [Test]
        public void Field_alive_window_is_half_open()
        {
            var f = new FieldTimeline(1000, 6000, 1000);
            Assert.IsFalse(f.IsAlive(999));
            Assert.IsTrue(f.IsAlive(1000));
            Assert.IsTrue(f.IsAlive(6999));
            Assert.IsFalse(f.IsAlive(7000));
        }

        [Test]
        public void Field_preconditions()
        {
            Assert.Throws<ContractException>(() => new FieldTimeline(-1, 1000, 1000));
            Assert.Throws<ContractException>(() => new FieldTimeline(0, 0, 1000));
            Assert.Throws<ContractException>(() => new FieldTimeline(0, EffectBook.MaxDurationMs + 1, 1000));
            Assert.Throws<ContractException>(() => new FieldTimeline(0, 1000, 0));
            Assert.Throws<ContractException>(() => new FieldTimeline(CombatTime.MaxMs, 1000, 1000));
            var f = new FieldTimeline(0, 1000, 100);
            Assert.Throws<ContractException>(() => f.TicksDue(-2, 0));
            Assert.Throws<ContractException>(() => f.TicksDue(5, 4));
        }
    }
}
