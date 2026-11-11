"""Independent arithmetic, edge cases and approved data checks for level rewards."""

from collections import Counter
import copy
from fractions import Fraction
from itertools import product
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from derived_stats import INT32_MAX, REPO
from level_rewards import CONFIG, PROPOSAL, Creature, LevelRewards, pacing_table, simulate
from level_progression import allocate_main_xp, main_reward_weights


class LevelRewardsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.models = [LevelRewards("A"), LevelRewards("B")]

    def assert_ledger(self, result):
        self.assertEqual(result["main_pool"], result["main_paid"] + result["main_loss"])
        self.assertEqual(result["sidekick_entitlement"],
                         result["sidekick_paid"] + result["sidekick_loss"])
        self.assertEqual(result["total_paid"], sum(result["paid"].values()))
        self.assertEqual(result["extra_issued"], result["sidekick_paid"])
        self.assertGreaterEqual(result["main_loss"], 0)
        self.assertGreaterEqual(result["sidekick_loss"], 0)
        for amount in result["paid"].values():
            self.assertIs(type(amount), int)
            self.assertGreaterEqual(amount, 0)
        self.assertTrue(all(0 <= xp <= 248528 for xp in result["stored"].values()))

    def test_historical_proposal_remains_unconfirmed_and_curve_is_unchanged(self):
        data = json.loads(PROPOSAL.read_text(encoding="utf-8"))
        self.assertEqual("proposal", data["status"])
        self.assertIs(data["confirmed"], False)
        self.assertEqual("A", data["recommended_option"])
        for model in self.models:
            self.assertEqual(248528, model.cap)
            self.assertEqual(1106, model.curve.xp_threshold(10))
        self.assertEqual("proposal", self.models[1].rules["status"])
        approved = json.loads(CONFIG.read_text(encoding="utf-8"))["progression"]["rewards"]
        self.assertEqual(approved["base_xp"], self.models[0].rules["source_base_xp"])
        with self.assertRaises(ValueError):
            LevelRewards("C")
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "rewards.json"
            for field, value in (("confirmed", True), ("status", "confirmed"), ("proposal_version", 2)):
                altered = copy.deepcopy(data)
                altered[field] = value
                path.write_text(json.dumps(altered), encoding="utf-8")
                with self.assertRaises(ValueError):
                    LevelRewards("B", path=path)

    def test_source_formulas_independent_oracle_for_every_level_stage_difficulty(self):
        for model in self.models:
            for level, stage, difficulty in product(range(1, 101), range(3), (1, 2, 10)):
                for source in ("wild", "capture", "quest_step", "quest_chain", "boss"):
                    roles = ("boss",) if source == "boss" else ("normal", "elite")
                    for role in roles:
                        base = model.rules["source_base_xp"][source]
                        elite = 2 if source == "wild" and role == "elite" else 1
                        if model.option == "A":
                            # Derive C independently; never trust the model's table.
                            power = min(level, 99) - 1
                            raw_cost = Fraction(100 * 105 ** power, 100 ** power)
                            cost = -(-raw_cost.numerator // raw_cost.denominator)
                            expected = int(Fraction(base * elite * cost, 156))
                        else:
                            expected = int(Fraction(base * elite * (1 << stage) * (difficulty + 1), 2))
                            if source == "boss":
                                expected += 60 * (1 << stage) * (difficulty + 1) // 2
                        self.assertEqual(expected, model.source_xp(source, level, stage, difficulty, role))

    def test_approved_coefficients_control_every_calculation_without_proposal_reads(self):
        data = json.loads(CONFIG.read_text(encoding="utf-8"))
        c = data["progression"]
        c.update(xp_first_level_cost=200, xp_growth_numerator=1060, xp_growth_denominator=1000,
                 appeared_weight=7, reserve_weight=3,
                 migration_stage_floor_levels=[11, 22, 36])
        c["rewards"].update(base_xp={"wild": 17, "capture": 19, "quest_step": 23,
                                    "quest_chain": 29, "boss": 31},
                            reference_level=9, elite_multiplier=3, sidekick_share_divisor=3,
                            wild_level={"boss_level_min": 12, "boss_level_max": 75, "per_tier": 4,
                                        "role_bonus": {"normal": 1, "elite": 2, "boss": 6}})
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "balance.json"
            path.write_text(json.dumps(data), encoding="utf-8")
            model = LevelRewards(path=path)
            self.assertNotIn("document", model.__dict__)
            self.assertEqual(200, model.curve.xp_to_next(1))
            self.assertEqual(212, model.curve.xp_to_next(2))
            self.assertEqual((7, 3), main_reward_weights([True, False], [False] * 2,
                                                        curve=model.curve))
            self.assertEqual((7, 3), allocate_main_xp(10, [True, False], [False] * 2,
                                                     curve=model.curve))
            for source, base in c["rewards"]["base_xp"].items():
                role = "boss" if source == "boss" else "normal"
                self.assertEqual(base, model.source_xp(source, 9, 0, role=role))
            self.assertEqual(51, model.source_xp("wild", 9, 0, role="elite"))
            self.assertEqual(29, model.initial_level(4, 2, 0, "elite"))
            for stage, floor in enumerate((11, 22, 36)):
                self.assertEqual(model.curve.xp_threshold(floor), model.migrate_xp(999, stage))
            result = model.settle(10, [Creature(1, 0), Creature(2, 0)], [True, False],
                                  {1: [Creature(3, 0)], 2: []})
            self.assertEqual({1: 7, 2: 3, 3: 2}, result["paid"])
            self.assertEqual(Fraction(1, 3), result["sidekick_loss"])

    def test_invalid_approved_reward_configuration_is_rejected(self):
        data = json.loads(CONFIG.read_text(encoding="utf-8"))
        mutations = (("reference_level", 0), ("reference_level", 100),
                     ("reference_level", True), ("elite_multiplier", 0),
                     ("sidekick_share_divisor", 0), ("sidekick_share_divisor", True),
                     ("base_xp", {"wild": 80}))
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "balance.json"
            for key, value in mutations:
                altered = copy.deepcopy(data)
                altered["progression"]["rewards"][key] = value
                path.write_text(json.dumps(altered), encoding="utf-8")
                with self.subTest(key=key, value=value), self.assertRaises((TypeError, ValueError)):
                    LevelRewards(path=path)
            for floors in ([], [10, 20], [10, 20, 101], [True, 20, 35]):
                altered = copy.deepcopy(data)
                altered["progression"]["migration_stage_floor_levels"] = floors
                path.write_text(json.dumps(altered), encoding="utf-8")
                with self.subTest(floors=floors), self.assertRaises((TypeError, ValueError)):
                    LevelRewards(path=path)

    def test_boss_double_counting_and_capture_role_factors(self):
        a, b = self.models
        self.assertEqual(640, a.source_xp("boss", 10, 2, role="boss"))
        self.assertEqual(1680, b.source_xp("boss", 37, 2, role="boss"))
        for model in self.models:
            with self.assertRaises(ValueError):
                model.source_xp("wild", 10, 2, role="boss")
            with self.assertRaises(ValueError):
                model.source_xp("boss", 10, 2)
            captures = [model.source_xp("capture", 10, 2, role=role)
                        for role in ("normal", "elite", "boss")]
            self.assertEqual([captures[0]] * 3, captures)

    def test_initial_levels_capture_thresholds_and_difficulty_clamp(self):
        a, b = self.models
        self.assertEqual([1, 1, 1], [a.initial_level(1, tier, tier) for tier in range(3)])
        self.assertEqual(1, a.initial_level(1, 1, 2, "elite"))
        self.assertEqual(5, a.initial_level(1, 2, 2, "boss"))
        # D0-X3: boss LV5..75 across d=1..10, half-up.
        self.assertEqual([5, 13, 21, 28, 36, 44, 52, 59, 67, 75],
                         [a.initial_level(d, 2, 2, "boss") for d in range(1, 11)])
        self.assertEqual([66, 68, 70], [a.initial_level(10, t, 0) for t in range(3)])
        self.assertEqual([10, 21, 37], [b.initial_level(1, tier, tier) for tier in range(3)])
        self.assertEqual(36, b.initial_level(1, 1, 2, "elite"))
        for model in self.models:
            for tier, stage, role in product(range(3), range(3), ("normal", "elite", "boss")):
                previous = 0
                for difficulty in range(1, 11):
                    level = model.initial_level(difficulty, tier, stage, role)
                    self.assertGreaterEqual(level, previous)
                    self.assertEqual(level, model.curve.level_from_xp(model.curve.xp_threshold(level)))
                    previous = level
        self.assertEqual(75, a.initial_level(10, 2, 2, "boss"))

    def test_migration_is_one_time_cumulative_and_int32_safe(self):
        a, b = self.models
        for stage, baseline in enumerate((1106, 3062, 8523)):
            self.assertEqual(baseline, a.migrate_xp(100, stage))
            self.assertEqual(baseline + 100, b.migrate_xp(100, stage))
            for model in self.models:
                migrated = model.migrate_xp(100, stage)
                self.assertEqual(migrated, model.migrate_xp(migrated, stage, True))
                self.assertEqual(0, model.migrate_xp(0, stage, True))
                self.assertEqual(model.cap, model.migrate_xp(INT32_MAX, stage, True))
                self.assertEqual(baseline, model.migrate_xp(0, stage))
        self.assertEqual(1106, a.migrate_xp(INT32_MAX, 0))
        self.assertEqual(b.cap, b.migrate_xp(INT32_MAX, 0))

    def test_approved_weights_survive_integer_settlement(self):
        for model in self.models:
            mains = [Creature(i, 0) for i in range(1, 7)]
            groups = {i: [] for i in range(1, 7)}
            result = model.settle(120, mains, [True, True, False, False, False, False], groups)
            self.assertEqual([30, 30, 15, 15, 15, 15], list(result["paid"].values()))
            mains[1] = Creature(2, 0, True)
            result = model.settle(120, mains, [True, True, False, False, False, False], groups)
            self.assertEqual([40, 0, 20, 20, 20, 20], list(result["paid"].values()))
            self.assert_ledger(result)

    def test_integer_conservation_vs_documented_floor_loss(self):
        a, b = self.models
        mains = [Creature(20, 0), Creature(10, 0), Creature(30, 0)]
        groups = {c.id: [] for c in mains}
        self.assertEqual({20: 0, 10: 1, 30: 0}, a.settle(1, mains, [True] * 3, groups)["paid"])
        self.assertEqual(1, b.settle(1, mains, [True] * 3, groups)["main_loss"])
        for option in (a, b):
            for total in range(250):
                result = option.settle(total, mains, [True, False, False], groups)
                self.assert_ledger(result)
                if option is a:
                    self.assertEqual(total, result["main_paid"])
                else:
                    self.assertEqual([total // 2, total // 4, total // 4], list(result["paid"].values()))
                    self.assertLess(result["main_loss"], 3)

    def test_sidekick_group_vs_each_half_is_additional_issuance(self):
        a, b = self.models
        mains = [Creature(1, 0)]
        groups = {1: [Creature(12, 0), Creature(11, 0)]}
        ra = a.settle(7, mains, [True], groups)
        rb = b.settle(7, mains, [True], groups)
        self.assertEqual({1: 7, 12: 1, 11: 2}, ra["paid"])
        self.assertEqual({1: 7, 12: 3, 11: 3}, rb["paid"])
        self.assertEqual(Fraction(1, 2), ra["sidekick_loss"])
        self.assertEqual(Fraction(1), rb["sidekick_loss"])
        self.assertEqual(3, ra["extra_issued"])
        self.assertEqual(6, rb["extra_issued"])
        for model in self.models:
            self.assert_ledger(model.settle(7, mains, [True], groups))
            # Dead sidekicks leave the group denominator; no all-dead division.
            dead_group = {1: [Creature(12, 0, True), Creature(11, 0)]}
            self.assertEqual({1: 7, 12: 0, 11: 3}, model.settle(7, mains, [True], dead_group)["paid"])
            all_dead = {1: [Creature(12, 0, True), Creature(11, 0, True)]}
            self.assertEqual(0, model.settle(7, mains, [True], all_dead)["sidekick_entitlement"])

    def test_zero_all_dead_and_all_capped_events(self):
        for model in self.models:
            groups = {1: [Creature(3, 0)], 2: [Creature(4, 0)]}
            for dead in (False, True):
                mains = [Creature(1, 0, dead), Creature(2, 0, dead)]
                result = model.settle(0, mains, [True, False], groups)
                self.assertEqual(0, result["total_paid"])
                self.assert_ledger(result)
            for mains in ([Creature(1, 0, True), Creature(2, 0, True)],
                          [Creature(1, model.cap), Creature(2, model.cap)]):
                result = model.settle(INT32_MAX, mains, [True, False], groups)
                self.assertEqual(INT32_MAX, result["main_loss"])
                self.assertEqual(0, result["total_paid"])
                self.assert_ledger(result)

    def test_cap_shares_and_sidekick_basis_use_main_actual_after_cap(self):
        a, b = self.models
        for model in (a, b):
            cap = model.cap
            mains = [Creature(1, cap), Creature(2, 0)]
            result = model.settle(120, mains, [True, False], {1: [Creature(3, 0)], 2: []})
            self.assertEqual(40 if model is a else 120, result["paid"][2])
            self.assertEqual(0, result["paid"][3])
            self.assert_ledger(result)
            result = model.settle(INT32_MAX, [Creature(1, cap - 3)], [True],
                                  {1: [Creature(3, 0), Creature(4, cap)]})
            self.assertEqual(3, result["paid"][1])
            self.assertEqual(0, result["paid"][4])
            self.assertEqual(1, result["paid"][3])
            self.assertEqual(Fraction(1, 2) if model is a else 2, result["sidekick_loss"])
            self.assertEqual(cap, result["stored"][1])
            self.assert_ledger(result)
            # Oversized legacy cumulative values cannot become a sidekick source.
            result = model.settle(1, [Creature(1, INT32_MAX)], [True], {1: [Creature(2, 0)]})
            self.assertEqual(cap, result["stored"][1])
            self.assertEqual(0, result["total_paid"])

    def test_int32_extremes_and_all_flag_combinations_have_exact_ledgers(self):
        for model in self.models:
            for appeared, dead, total in product(product((False, True), repeat=3),
                                                 product((False, True), repeat=3),
                                                 (0, 1, 7, INT32_MAX - 1, INT32_MAX)):
                mains = [Creature(i + 1, 0, flag) for i, flag in enumerate(dead)]
                groups = {c.id: [Creature(10 + c.id, 0)] for c in mains}
                result = model.settle(total, mains, appeared, groups)
                self.assert_ledger(result)
                for c in mains:
                    if c.dead:
                        self.assertEqual(0, result["paid"][c.id])
                        self.assertEqual(0, result["paid"][10 + c.id])

    def test_int32_assignments_are_computed_before_storage_clipping(self):
        for model in self.models:
            mains = [Creature(i, 0) for i in range(1, 4)]
            result = model.settle(INT32_MAX, mains, [True, False, False], {c.id: [] for c in mains})
            self.assertEqual({1: 1073741823, 2: 536870912, 3: 536870912}
                             if model.option == "A" else
                             {1: 1073741823, 2: 536870911, 3: 536870911}, result["main_assigned"])
            self.assertEqual(3 * model.cap, result["main_paid"])
            self.assert_ledger(result)

    def test_capped_sidekick_assigned_share_is_lost_without_redistribution(self):
        for model in self.models:
            groups = {1: [Creature(11, model.cap), Creature(12, 0)]}
            result = model.settle(120, [Creature(1, 0)], [True], groups)
            expected = 30 if model.option == "A" else 60
            self.assertEqual({11: expected, 12: expected}, result["sidekick_assigned"])
            self.assertEqual({1: 120, 11: 0, 12: expected}, result["paid"])
            self.assertEqual(expected, result["sidekick_loss"])
            self.assert_ledger(result)
            groups[1][1] = Creature(12, model.cap - 2)
            result = model.settle(120, [Creature(1, 0)], [True], groups)
            self.assertEqual(2, result["paid"][12])
            self.assertEqual(2 * expected - 2, result["sidekick_loss"])
            self.assert_ledger(result)

    def test_determinism_id_ties_input_immutability_and_reordering(self):
        for model in self.models:
            mains = [Creature(9, 0), Creature(1, 0), Creature(4, 0)]
            groups = {9: [Creature(20, 0), Creature(19, 0)], 1: [], 4: []}
            before = copy.deepcopy((mains, groups))
            first = model.settle(19, mains, [True] * 3, groups)
            for _ in range(10):
                self.assertEqual(first, model.settle(19, mains, [True] * 3, groups))
            reordered = {9: list(reversed(groups[9])), 4: [], 1: []}
            self.assertEqual(first, model.settle(19, list(reversed(mains)), [True] * 3, reordered))
            self.assertEqual(before, (mains, groups))

    def test_preconditions_fail_before_any_settlement(self):
        for model in self.models:
            for bad in (True, False, 1.0, "1", None, -1, INT32_MAX + 1):
                with self.assertRaises((TypeError, ValueError)):
                    model.settle(bad, [Creature(1, 0)], [True], {1: []})
                with self.assertRaises((TypeError, ValueError)):
                    model.migrate_xp(bad, 0)
            invalid = [([], [], {}), ([Creature(1, 0)] * 7, [True] * 7, {1: []}),
                       ([Creature(1, 0)], [1], {1: []}), ([Creature(1, 0)], [], {1: []}),
                       ([Creature(1, 0)], [True], {}), ([Creature(1, 0)], [True], {True: []}),
                       ([Creature(1, 0)], [True], {1: [Creature(1, 0)]}),
                       ([Creature(1, 0), Creature(2, 0)], [True, True],
                        {1: [Creature(3, 0)], 2: [Creature(3, 0)]}),
                       ([Creature(True, 0)], [True], {1: []}),
                       ([Creature(1, -1)], [True], {1: []}),
                       ([Creature(1, 0, 1)], [True], {1: []}),
                       ([Creature(1, 0)], [True], {1: None})]
            for mains, appeared, groups in invalid:
                with self.assertRaises((TypeError, ValueError)):
                    model.settle(7, mains, appeared, groups)
            for bad in (True, 1.0, "1", None, -1, INT32_MAX):
                for args in ((bad, 0, 0), (1, bad, 0), (1, 0, bad)):
                    with self.assertRaises((TypeError, ValueError)):
                        model.initial_level(*args)
            for args in (("other", 10, 0), ("wild", 0, 0), ("wild", 101, 0),
                         ("wild", True, 0), ("wild", 10, 3), ("wild", 10, 0, INT32_MAX)):
                with self.assertRaises((TypeError, ValueError)):
                    model.source_xp(*args)
            with self.assertRaises(TypeError):
                model.migrate_xp(0, 0, 1)

    def test_d0_counts_and_capture_expectation_are_explicit_assumptions(self):
        for model in self.models:
            events = model.d0_events()
            self.assertEqual(Counter(wild=14, boss=1, capture=3, quest_step=5, quest_chain=1),
                             Counter(source for source, _ in events))
            run = model.document["d0_run_model"]
            expected = Fraction(run["normal_capture_attempts"] * 60, 100) + 1
            declared = run["expected_total_captures_for_these_attempts"]
            self.assertEqual(expected, Fraction(declared["numerator"], declared["denominator"]))
            self.assertEqual(13, sum(run["normal_victims_by_region_tier"]))
            self.assertEqual(1726 if model.option == "A" else 5280, sum(xp for _, xp in events))

    def test_boss_auto_skipped_steps_and_incomplete_chain_never_pay(self):
        for model in self.models:
            events = model.d0_events([False, False, False, False, True])
            self.assertEqual(1, sum(source == "quest_step" for source, _ in events))
            self.assertFalse(any(source == "quest_chain" for source, _ in events))
            events = model.d0_events([False] * 5)
            self.assertFalse(any(source.startswith("quest_") for source, _ in events))
            for flags in ([], [True] * 4, [True] * 6, [1] * 5, "11111"):
                with self.assertRaises(ValueError):
                    model.d0_events(flags)

    def test_pacing_gate_counts_against_independent_threshold_and_repetition(self):
        expected = {("A", 1): (1726, 19, (2, 2, 3)), ("A", 2): (874, 15, (3, 4, 6)),
                    ("A", 6): (437, 12, (5, 8, 12)), ("B", 1): (5280, 30, (1, 1, 1)),
                    ("B", 2): (2640, 22, (1, 2, 2)), ("B", 6): (1320, 17, (2, 3, 4))}
        for (option, party), (gain, level, gates) in expected.items():
            result = simulate(option, party)
            self.assertEqual(result, simulate(option, party))
            self.assertEqual(gain, result["active_xp"])
            self.assertEqual(level, result["level_after_one"])
            self.assertEqual(gates, tuple(result["gates"][gate] for gate in (20, 25, 30)))
            # Independent threshold oracle: delta from T(10) to gates.
            for gate, delta in ((20, 1956), (25, 3354), (30, 5140)):
                self.assertEqual(-(-delta // gain), result["gates"][gate])
        for bad in (0, 7, True, INT32_MAX):
            with self.assertRaises((TypeError, ValueError)):
                simulate("A", bad)
        # The proposal's pacing_target was computed with the pre-D0-X3 wild levels (boss LV19 at d=1); it stays
        # as history in Data/_incoming and no longer has to match the approved config (2026-11-10).

    def test_printed_pacing_table_is_identical_to_doc_table(self):
        table = pacing_table()
        output = subprocess.check_output(
            [sys.executable, str(Path(__file__).with_name("level_rewards.py"))],
            env={**os.environ, "PYTHONIOENCODING": "utf-8"})
        self.assertEqual(table, output.decode("utf-8").replace("\r\n", "\n").strip())
        doc = (REPO / "docs/design/level-progression.md").read_text(encoding="utf-8")
        self.assertIn(table, doc)


if __name__ == "__main__":
    unittest.main()
