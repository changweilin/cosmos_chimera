using Cosmimera.Core.Contracts;
using Cosmimera.Core.Random;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    public sealed class PlayerVitalsTests
    {
        // Small, distinct numbers so an off-by-one between duration / i-frames / cooldown is visible.
        private static PlayerVitals Make() => new PlayerVitals(new PlayerVitalsConfig(100, 600, 400, 800));

        [Test]
        public void Shipped_balance_builds_a_valid_config()
        {
            PlayerVitalsConfig c = BalanceFixture.PlayerConfig();
            var p = new PlayerVitals(c);
            Assert.AreEqual(c.MaxHp, p.Hp);
            Assert.That(c.RollInvulnerableMs, Is.GreaterThan(0));
        }

        [Test]
        public void Config_rejects_inconsistent_windows()
        {
            Assert.Throws<ContractException>(() => new PlayerVitalsConfig(0, 600, 400, 800));
            Assert.Throws<ContractException>(() => new PlayerVitalsConfig(100, 600, -1, 800));
            Assert.Throws<ContractException>(() => new PlayerVitalsConfig(100, 600, 601, 800));
            Assert.Throws<ContractException>(() => new PlayerVitalsConfig(100, 600, 400, 599));
            Assert.DoesNotThrow(() => new PlayerVitalsConfig(1, 0, 0, 0));
        }

        [Test]
        public void Default_config_is_rejected()
        {
            Assert.Throws<ContractException>(() => new PlayerVitals(default));
        }

        [Test]
        public void Starts_full_and_not_invulnerable()
        {
            PlayerVitals p = Make();
            Assert.AreEqual(100, p.Hp);
            Assert.AreEqual(100, p.MaxHp);
            Assert.IsFalse(p.IsDowned);
            Assert.IsFalse(p.IsInvulnerable(0));
            Assert.IsFalse(p.IsRolling(0));
        }

        [Test]
        public void I_frames_are_half_open_from_roll_start()
        {
            PlayerVitals p = Make();
            Assert.AreEqual(RollResult.Ok, p.TryRoll(1000));
            Assert.IsTrue(p.IsInvulnerable(1000));
            Assert.IsTrue(p.IsInvulnerable(1399));
            Assert.IsFalse(p.IsInvulnerable(1400));
            Assert.IsTrue(p.IsRolling(1599));
            Assert.IsFalse(p.IsRolling(1600));
        }

        [Test]
        public void Roll_cooldown_counts_from_roll_start()
        {
            PlayerVitals p = Make();
            p.TryRoll(0);
            Assert.AreEqual(RollResult.OnCooldown, p.TryRoll(799));
            Assert.IsFalse(p.IsInvulnerable(799));
            Assert.AreEqual(RollResult.Ok, p.TryRoll(800));
            Assert.IsTrue(p.IsInvulnerable(800));
        }

        [Test]
        public void Damage_reduces_hp_and_clamps_at_zero()
        {
            PlayerVitals p = Make();
            Assert.AreEqual(30, p.ApplyDamage(30, 0));
            Assert.AreEqual(70, p.Hp);
            Assert.AreEqual(0, p.ApplyDamage(0, 0));
            Assert.AreEqual(70, p.ApplyDamage(int.MaxValue, 0));
            Assert.AreEqual(0, p.Hp);
            Assert.IsTrue(p.IsDowned);
            Assert.AreEqual(0, p.ApplyDamage(5, 1));
        }

        [Test]
        public void Damage_during_i_frames_is_ignored_and_resumes_after()
        {
            PlayerVitals p = Make();
            p.TryRoll(0);
            Assert.AreEqual(0, p.ApplyDamage(50, 399));
            Assert.AreEqual(100, p.Hp);
            Assert.AreEqual(50, p.ApplyDamage(50, 400));
        }

        [Test]
        public void Downed_player_cannot_roll()
        {
            PlayerVitals p = Make();
            p.ApplyDamage(100, 0);
            Assert.AreEqual(RollResult.Downed, p.TryRoll(0));
            Assert.IsFalse(p.IsInvulnerable(0));
        }

        [Test]
        public void Negative_damage_and_backwards_time_are_contract_violations()
        {
            PlayerVitals p = Make();
            Assert.Throws<ContractException>(() => p.ApplyDamage(-1, 0));
            p.TryRoll(100);
            Assert.Throws<ContractException>(() => p.TryRoll(99));
            Assert.Throws<ContractException>(() => p.ApplyDamage(1, 99));
            Assert.Throws<ContractException>(() => p.IsInvulnerable(99));
            Assert.Throws<ContractException>(() => p.IsRolling(99));
        }

        // ---- SOL P2-06 review regressions (docs/reviews/P2-06-sol.md) ----

        [Test]
        public void V1_old_roll_is_over_at_the_end_of_the_time_domain()
        {
            PlayerVitals p = Make();
            p.TryRoll(0);
            Assert.IsFalse(p.IsInvulnerable(CombatTime.MaxMs));
            Assert.IsFalse(p.IsRolling(CombatTime.MaxMs));
            Assert.AreEqual(1, p.ApplyDamage(1, CombatTime.MaxMs));
            Assert.AreEqual(RollResult.Ok, p.TryRoll(CombatTime.MaxMs));
            Assert.IsTrue(p.IsInvulnerable(CombatTime.MaxMs));
        }

        [Test]
        public void V1_time_outside_the_domain_is_contract_violation()
        {
            PlayerVitals p = Make();
            Assert.Throws<ContractException>(() => p.TryRoll(-1));
            Assert.Throws<ContractException>(() => p.ApplyDamage(1, CombatTime.MaxMs + 1));
            Assert.Throws<ContractException>(() => p.IsInvulnerable(long.MaxValue));
        }

        [TestCase(-1L)]
        [TestCase(CombatTime.MaxMs + 1)]
        [TestCase(long.MinValue)]
        public void N3_every_timed_entry_rejects_time_outside_the_domain(long bad)
        {
            PlayerVitals p = Make();
            Assert.Throws<ContractException>(() => p.TryRoll(bad));
            Assert.Throws<ContractException>(() => p.ApplyDamage(1, bad));
            Assert.Throws<ContractException>(() => p.IsRolling(bad));
            Assert.Throws<ContractException>(() => p.IsInvulnerable(bad));
            Assert.AreEqual(RollResult.Ok, p.TryRoll(0), "a rejected call does not move the watermark");
        }

        [Test]
        public void V2_refused_roll_advances_the_watermark_queries_do_not()
        {
            PlayerVitals p = Make();
            p.TryRoll(0);
            Assert.IsTrue(p.IsRolling(100));
            Assert.AreEqual(RollResult.OnCooldown, p.TryRoll(99), "a query is not a command");
            Assert.AreEqual(RollResult.OnCooldown, p.TryRoll(200));
            Assert.Throws<ContractException>(() => p.ApplyDamage(1, 199));
        }

        [Test]
        public void M51_M52_zero_length_windows()
        {
            var p = new PlayerVitals(new PlayerVitalsConfig(100, 0, 0, 0));
            Assert.AreEqual(RollResult.Ok, p.TryRoll(5));
            Assert.IsFalse(p.IsInvulnerable(5), "zero i-frames means none, even on the start tick");
            Assert.IsFalse(p.IsRolling(5));
            Assert.AreEqual(RollResult.Ok, p.TryRoll(5), "zero cooldown allows an immediate roll");
        }

        [Test]
        public void M53_downed_is_reported_before_cooldown()
        {
            PlayerVitals p = Make();
            p.TryRoll(0);
            p.ApplyDamage(100, 400);
            Assert.AreEqual(RollResult.Downed, p.TryRoll(400));
        }

        [Test]
        public void M54_M55_elapsed_is_not_narrowed_to_int()
        {
            const long later = 1L << 32;
            PlayerVitals p = Make();
            p.TryRoll(0);
            Assert.IsFalse(p.IsInvulnerable(later));
            Assert.IsFalse(p.IsRolling(later));
            Assert.AreEqual(RollResult.Ok, p.TryRoll(later));
        }

        [Test]
        public void V3_default_config_reads_zero_hp()
        {
            Assert.AreEqual(0, default(PlayerVitalsConfig).MaxHp);
        }

        [Test]
        public void I_frames_skip_the_hit_roll_and_consume_no_rng()
        {
            PlayerVitals p = Make();
            p.TryRoll(0);
            var rng = new Pcg32(42UL, 3UL);
            var untouched = new Pcg32(42UL, 3UL);
            Assert.AreEqual(HitOutcome.Invulnerable, HitResolver.Resolve(true, p.IsInvulnerable(10), 1000, ref rng));
            Assert.AreEqual(untouched.NextUInt(1000), rng.NextUInt(1000));
        }
    }
}
