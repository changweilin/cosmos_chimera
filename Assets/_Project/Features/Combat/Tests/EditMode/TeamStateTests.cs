using System.Collections.Generic;
using Cosmimera.Core.Contracts;
using NUnit.Framework;

namespace Cosmimera.Combat.Tests
{
    public sealed class TeamStateTests
    {
        private const int Cd = 3000;

        private static TeamState FullTeamInCombat()
        {
            var t = new TeamState(new[] { 10, 11, 12, 13, 14, 15 }, Cd);
            Assert.AreEqual(TeamResult.Ok, t.SetPool(new[] { 10, 11, 12, 13 }));
            t.BeginCombat();
            return t;
        }

        [Test]
        public void Shipped_balance_has_a_usable_switch_cooldown()
        {
            Assert.That(BalanceFixture.SwitchCooldownMs(), Is.GreaterThan(0));
            Assert.DoesNotThrow(() => new TeamState(new[] { 1 }, BalanceFixture.SwitchCooldownMs()));
        }

        [Test]
        public void New_team_is_empty_and_out_of_combat()
        {
            var t = new TeamState(new[] { 5, 3 }, Cd);
            CollectionAssert.AreEqual(new[] { 5, 3 }, t.Roster);
            Assert.IsEmpty(t.Pool);
            Assert.IsEmpty(t.Active);
            Assert.IsFalse(t.InCombat);
            Assert.IsFalse(t.IsWipedOut);
            Assert.AreEqual(0, t.SwitchCooldownRemainingMs(0));
        }

        [Test]
        public void Constructor_rejects_null_empty_oversized_duplicate_negative()
        {
            Assert.Throws<ContractException>(() => new TeamState(null, Cd));
            Assert.Throws<ContractException>(() => new TeamState(new int[0], Cd));
            Assert.Throws<ContractException>(() => new TeamState(new[] { 1, 2, 3, 4, 5, 6, 7 }, Cd));
            Assert.Throws<ContractException>(() => new TeamState(new[] { 1, 1 }, Cd));
            Assert.Throws<ContractException>(() => new TeamState(new[] { -1 }, Cd));
            Assert.Throws<ContractException>(() => new TeamState(new[] { 1 }, -1));
        }

        [Test]
        public void Roster_of_exactly_six_is_allowed()
        {
            Assert.AreEqual(6, new TeamState(new[] { 0, 1, 2, 3, 4, 5 }, 0).Roster.Count);
        }

        [Test]
        public void Pool_size_must_be_one_to_four()
        {
            var t = new TeamState(new[] { 1, 2, 3, 4, 5, 6 }, Cd);
            Assert.AreEqual(TeamResult.InvalidPool, t.SetPool(new int[0]));
            Assert.AreEqual(TeamResult.InvalidPool, t.SetPool(new[] { 1, 2, 3, 4, 5 }));
            Assert.AreEqual(TeamResult.Ok, t.SetPool(new[] { 1, 2, 3, 4 }));
            Assert.AreEqual(TeamResult.Ok, t.SetPool(new[] { 6 }));
            CollectionAssert.AreEqual(new[] { 6 }, t.Pool);
        }

        [Test]
        public void Pool_refusals_leave_pool_unchanged()
        {
            var t = new TeamState(new[] { 1, 2, 3 }, Cd);
            t.SetPool(new[] { 1, 2 });
            Assert.AreEqual(TeamResult.NotInRoster, t.SetPool(new[] { 1, 9 }));
            Assert.AreEqual(TeamResult.InvalidPool, t.SetPool(new[] { 3, 3 }));
            CollectionAssert.AreEqual(new[] { 1, 2 }, t.Pool);
        }

        [Test]
        public void Pool_is_locked_during_combat_and_unlocked_after()
        {
            var t = new TeamState(new[] { 1, 2 }, Cd);
            t.SetPool(new[] { 1 });
            t.BeginCombat();
            Assert.AreEqual(TeamResult.InCombat, t.SetPool(new[] { 2 }));
            CollectionAssert.AreEqual(new[] { 1 }, t.Pool);
            t.EndCombat();
            Assert.AreEqual(TeamResult.Ok, t.SetPool(new[] { 2 }));
        }

        [Test]
        public void Pool_cannot_drop_a_member_on_the_field()
        {
            var t = new TeamState(new[] { 1, 2 }, Cd);
            t.SetPool(new[] { 1 });
            Assert.AreEqual(TeamResult.Ok, t.Summon(1, 0));
            Assert.AreEqual(TeamResult.ActiveNotInPool, t.SetPool(new[] { 2 }));
            Assert.AreEqual(TeamResult.Ok, t.SetPool(new[] { 2, 1 }));
        }

        [Test]
        public void SetPool_null_is_contract_violation()
        {
            Assert.Throws<ContractException>(() => new TeamState(new[] { 1 }, Cd).SetPool(null));
        }

        [Test]
        public void Combat_begin_end_must_alternate()
        {
            var t = new TeamState(new[] { 1 }, Cd);
            t.SetPool(new[] { 1 });
            Assert.Throws<ContractException>(() => t.EndCombat());
            t.BeginCombat();
            Assert.Throws<ContractException>(() => t.BeginCombat());
        }

        [Test]
        public void At_most_two_on_the_field_and_opening_summons_are_free()
        {
            TeamState t = FullTeamInCombat();
            Assert.AreEqual(TeamResult.Ok, t.Summon(10, 0));
            Assert.AreEqual(TeamResult.Ok, t.Summon(11, 0));
            Assert.AreEqual(0, t.SwitchCooldownRemainingMs(0));
            Assert.AreEqual(TeamResult.NoFreeSlot, t.Summon(12, 0));
            CollectionAssert.AreEqual(new[] { 10, 11 }, t.Active);
        }

        [Test]
        public void Summon_refusal_reasons()
        {
            TeamState t = FullTeamInCombat();
            Assert.AreEqual(TeamResult.NotInRoster, t.Summon(99, 0));
            Assert.AreEqual(TeamResult.NotInPool, t.Summon(14, 0));
            t.Summon(10, 0);
            Assert.AreEqual(TeamResult.AlreadyActive, t.Summon(10, 0));
            CollectionAssert.AreEqual(new[] { 10 }, t.Active);
        }

        [Test]
        public void Switch_swaps_in_place_and_starts_cooldown()
        {
            TeamState t = FullTeamInCombat();
            t.Summon(10, 0);
            t.Summon(11, 0);
            Assert.AreEqual(TeamResult.Ok, t.Switch(10, 12, 100));
            CollectionAssert.AreEqual(new[] { 12, 11 }, t.Active);
            Assert.AreEqual(Cd, t.SwitchCooldownRemainingMs(100));
        }

        [Test]
        public void Cooldown_blocks_switch_recall_and_summon_until_exactly_elapsed()
        {
            TeamState t = FullTeamInCombat();
            t.Summon(10, 0);
            t.Summon(11, 0);
            t.Switch(10, 12, 1000);
            long last = 1000 + Cd - 1;
            Assert.AreEqual(TeamResult.SwitchOnCooldown, t.Switch(11, 13, last));
            Assert.AreEqual(TeamResult.SwitchOnCooldown, t.Recall(11, last));
            Assert.AreEqual(1, t.SwitchCooldownRemainingMs(last));
            Assert.AreEqual(TeamResult.Ok, t.Switch(11, 13, last + 1));
        }

        [Test]
        public void Recall_starts_cooldown_so_recall_then_summon_is_not_a_free_switch()
        {
            TeamState t = FullTeamInCombat();
            t.Summon(10, 0);
            Assert.AreEqual(TeamResult.Ok, t.Recall(10, 500));
            Assert.IsEmpty(t.Active);
            Assert.AreEqual(TeamResult.SwitchOnCooldown, t.Summon(11, 500));
            Assert.AreEqual(TeamResult.Ok, t.Summon(11, 500 + Cd));
        }

        [Test]
        public void Recall_and_switch_need_a_field_member()
        {
            TeamState t = FullTeamInCombat();
            Assert.AreEqual(TeamResult.NotActive, t.Recall(10, 0));
            Assert.AreEqual(TeamResult.NotActive, t.Switch(10, 11, 0));
            Assert.AreEqual(0, t.SwitchCooldownRemainingMs(0));
        }

        [Test]
        public void Switch_refusals_for_incoming_member_leave_state_unchanged()
        {
            TeamState t = FullTeamInCombat();
            t.Summon(10, 0);
            t.Summon(11, 0);
            Assert.AreEqual(TeamResult.AlreadyActive, t.Switch(10, 11, 0));
            Assert.AreEqual(TeamResult.AlreadyActive, t.Switch(10, 10, 0));
            Assert.AreEqual(TeamResult.NotInPool, t.Switch(10, 15, 0));
            Assert.AreEqual(TeamResult.NotInRoster, t.Switch(10, 99, 0));
            CollectionAssert.AreEqual(new[] { 10, 11 }, t.Active);
            Assert.AreEqual(0, t.SwitchCooldownRemainingMs(0));
        }

        [Test]
        public void Faint_retires_for_the_rest_of_combat_and_refill_is_free()
        {
            TeamState t = FullTeamInCombat();
            t.Summon(10, 0);
            t.Faint(10);
            Assert.IsTrue(t.IsRetired(10));
            Assert.IsEmpty(t.Active);
            Assert.AreEqual(TeamResult.Ok, t.Summon(11, 0));
            Assert.AreEqual(TeamResult.Retired, t.Switch(11, 10, 0));
            Assert.AreEqual(TeamResult.Retired, t.Summon(10, 10_000));
        }

        [Test]
        public void Faint_does_not_cancel_a_running_cooldown()
        {
            TeamState t = FullTeamInCombat();
            t.Summon(10, 0);
            t.Summon(11, 0);
            t.Switch(10, 12, 0);
            t.Faint(11);
            Assert.AreEqual(TeamResult.SwitchOnCooldown, t.Summon(13, 1));
            Assert.AreEqual(TeamResult.Ok, t.Summon(13, Cd));
        }

        [Test]
        public void EndCombat_clears_retirement()
        {
            TeamState t = FullTeamInCombat();
            t.Summon(10, 0);
            t.Faint(10);
            t.EndCombat();
            Assert.IsFalse(t.IsRetired(10));
            Assert.AreEqual(TeamResult.Ok, t.Summon(10, 0));
        }

        [Test]
        public void Faint_preconditions()
        {
            var t = new TeamState(new[] { 1, 2 }, Cd);
            t.SetPool(new[] { 1, 2 });
            t.Summon(1, 0);
            Assert.Throws<ContractException>(() => t.Faint(1));
            t.BeginCombat();
            Assert.Throws<ContractException>(() => t.Faint(2));
        }

        [Test]
        public void Wiped_out_only_when_nobody_on_field_and_pool_all_retired()
        {
            TeamState t = FullTeamInCombat();
            Assert.IsFalse(t.IsWipedOut);
            int[] pool = { 10, 11, 12, 13 };
            foreach (int id in pool)
            {
                Assert.AreEqual(TeamResult.Ok, t.Summon(id, 0));
                Assert.IsFalse(t.IsWipedOut);
                t.Faint(id);
            }

            Assert.IsTrue(t.IsWipedOut);
            t.EndCombat();
            Assert.IsFalse(t.IsWipedOut);
        }

        [Test]
        public void Time_going_backwards_is_contract_violation()
        {
            TeamState t = FullTeamInCombat();
            t.Summon(10, 100);
            Assert.Throws<ContractException>(() => t.Summon(11, 99));
            Assert.Throws<ContractException>(() => t.Recall(10, 99));
            Assert.Throws<ContractException>(() => t.Switch(10, 11, 99));
            Assert.Throws<ContractException>(() => t.SwitchCooldownRemainingMs(99));
        }

        [Test]
        public void Zero_cooldown_allows_back_to_back_switches()
        {
            var t = new TeamState(new[] { 1, 2 }, 0);
            t.SetPool(new[] { 1, 2 });
            t.Summon(1, 0);
            Assert.AreEqual(TeamResult.Ok, t.Switch(1, 2, 0));
            Assert.AreEqual(TeamResult.Ok, t.Switch(2, 1, 0));
        }

        // ---- SOL P2-06 review regressions (docs/reviews/P2-06-sol.md) ----

        [Test]
        public void T1_exposed_lists_cannot_be_mutated_by_casting()
        {
            var t = new TeamState(new[] { 1, 2 }, Cd);
            t.SetPool(new[] { 1, 2 });
            t.Summon(1, 0);
            Assert.Throws<System.NotSupportedException>(() => ((IList<int>)t.Pool).Clear());
            Assert.Throws<System.NotSupportedException>(() => ((IList<int>)t.Roster).Clear());
            Assert.Throws<System.NotSupportedException>(() => ((IList<int>)t.Active).Add(99));
            CollectionAssert.AreEqual(new[] { 1 }, t.Active);
        }

        [Test]
        public void T2_setting_the_pool_to_its_own_view_keeps_it()
        {
            var t = new TeamState(new[] { 1, 2 }, Cd);
            t.SetPool(new[] { 1, 2 });
            t.Summon(1, 0);
            Assert.AreEqual(TeamResult.Ok, t.SetPool(t.Pool));
            CollectionAssert.AreEqual(new[] { 1, 2 }, t.Pool);
        }

        [Test]
        public void T3_cooldown_holds_at_the_end_of_the_time_domain()
        {
            TeamState t = FullTeamInCombat();
            long now = CombatTime.MaxMs - 1;
            t.Summon(10, now);
            Assert.AreEqual(TeamResult.Ok, t.Recall(10, now));
            Assert.AreEqual(Cd, t.SwitchCooldownRemainingMs(now));
            Assert.AreEqual(TeamResult.SwitchOnCooldown, t.Summon(11, CombatTime.MaxMs));
        }

        [Test]
        public void T3_time_outside_the_domain_is_contract_violation()
        {
            TeamState t = FullTeamInCombat();
            Assert.Throws<ContractException>(() => t.Summon(10, -1));
            Assert.Throws<ContractException>(() => t.Summon(10, CombatTime.MaxMs + 1));
            Assert.Throws<ContractException>(() => t.SwitchCooldownRemainingMs(long.MaxValue));
        }

        [TestCase(-1L)]
        [TestCase(CombatTime.MaxMs + 1)]
        [TestCase(long.MinValue)]
        public void N3_every_timed_entry_rejects_time_outside_the_domain(long bad)
        {
            TeamState t = FullTeamInCombat();
            Assert.Throws<ContractException>(() => t.Summon(10, bad));
            Assert.Throws<ContractException>(() => t.Recall(10, bad));
            Assert.Throws<ContractException>(() => t.Switch(10, 11, bad), "even with outId not on the field");
            Assert.Throws<ContractException>(() => t.SwitchCooldownRemainingMs(bad));
            Assert.Throws<ContractException>(() => CombatTime.Require(bad, bad));
            Assert.AreEqual(TeamResult.Ok, t.Summon(10, 0), "a rejected call does not move the watermark");
        }

        [Test]
        public void N3_combat_time_accepts_both_ends_and_rejects_below_watermark()
        {
            Assert.DoesNotThrow(() => CombatTime.Require(0, 0));
            Assert.DoesNotThrow(() => CombatTime.Require(CombatTime.MaxMs, CombatTime.MaxMs));
            Assert.Throws<ContractException>(() => CombatTime.Require(5, 6));
        }

        [Test]
        public void N1_duplicated_and_foreign_pool_reports_invalid_pool()
        {
            var t = new TeamState(new[] { 1, 2 }, Cd);
            Assert.AreEqual(TeamResult.InvalidPool, t.SetPool(new[] { 99, 99 }));
        }

        [Test]
        public void M33_cooldown_is_not_narrowed_to_int()
        {
            TeamState t = FullTeamInCombat();
            long now = int.MaxValue;
            t.Summon(10, now);
            t.Recall(10, now);
            Assert.AreEqual(Cd, t.SwitchCooldownRemainingMs(now));
        }

        [Test]
        public void T4_queries_do_not_move_the_watermark_but_refused_commands_do()
        {
            TeamState t = FullTeamInCombat();
            t.Summon(10, 0);
            Assert.AreEqual(0, t.SwitchCooldownRemainingMs(100));
            Assert.AreEqual(TeamResult.Ok, t.Summon(11, 99), "a query is not a command");
            Assert.AreEqual(TeamResult.NotInRoster, t.Summon(99, 200));
            Assert.Throws<ContractException>(() => t.Recall(10, 199));
        }

        [Test]
        public void M32_switch_with_inactive_out_still_advances_the_watermark()
        {
            TeamState t = FullTeamInCombat();
            Assert.AreEqual(TeamResult.NotActive, t.Switch(10, 11, 200));
            Assert.Throws<ContractException>(() => t.Summon(10, 199));
        }

        [Test]
        public void T5_combat_needs_a_chosen_pool()
        {
            var t = new TeamState(new[] { 1 }, Cd);
            Assert.Throws<ContractException>(() => t.BeginCombat());
            Assert.IsFalse(t.InCombat);
        }

        [Test]
        public void T6_retired_is_reported_before_no_free_slot()
        {
            var t = new TeamState(new[] { 1, 2, 3 }, Cd);
            t.SetPool(new[] { 1, 2, 3 });
            t.BeginCombat();
            t.Summon(1, 0);
            t.Faint(1);
            t.Summon(2, 0);
            t.Summon(3, 0);
            Assert.AreEqual(TeamResult.Retired, t.Summon(1, 0));
            Assert.AreEqual(TeamResult.SwitchOnCooldown, Cooling().Summon(1, 1), "cooldown before free slot");

            TeamState Cooling()
            {
                var c = new TeamState(new[] { 1, 2, 3 }, Cd);
                c.SetPool(new[] { 1, 2, 3 });
                c.Summon(1, 0);
                c.Summon(2, 0);
                c.Switch(1, 3, 0);
                return c;
            }
        }

        [Test]
        public void M22_recall_is_allowed_exactly_when_cooldown_ends()
        {
            TeamState t = FullTeamInCombat();
            t.Summon(10, 0);
            t.Recall(10, 0);
            Assert.AreEqual(TeamResult.SwitchOnCooldown, t.Summon(11, Cd - 1), "still cooling 1 ms before the end");
            Assert.AreEqual(TeamResult.Ok, t.Summon(11, Cd));
            Assert.AreEqual(TeamResult.Ok, t.Recall(11, Cd), "summon does not restart it, so recall is free at the end");
        }

        [Test]
        public void M29_M31_combat_boundaries_do_not_reset_cooldown()
        {
            var t = new TeamState(new[] { 1, 2 }, Cd);
            t.SetPool(new[] { 1, 2 });
            t.Summon(1, 0);
            t.Recall(1, 0);
            t.BeginCombat();
            Assert.AreEqual(TeamResult.SwitchOnCooldown, t.Summon(2, 1), "BeginCombat keeps the cooldown");
            t.EndCombat();
            Assert.AreEqual(TeamResult.SwitchOnCooldown, t.Summon(2, 2), "EndCombat keeps the cooldown");
        }

        [Test]
        public void M30_combat_boundaries_keep_the_field()
        {
            TeamState t = FullTeamInCombat();
            t.Summon(10, 0);
            t.EndCombat();
            CollectionAssert.AreEqual(new[] { 10 }, t.Active);
            t.BeginCombat();
            CollectionAssert.AreEqual(new[] { 10 }, t.Active);
        }
    }
}
