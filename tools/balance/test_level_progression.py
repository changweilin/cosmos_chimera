"""Independent numerical oracles and contract boundaries for P2-12."""

import copy
from decimal import Decimal, ROUND_CEILING, localcontext
from fractions import Fraction
import json
from pathlib import Path
import tempfile
import unittest

from derived_stats import DerivedStats, INT32_MAX
from level_progression import (CONFIG, EventParticipation, LevelProgression,
                               allocate_main_xp, main_reward_weights)


class LevelProgressionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.model = LevelProgression()
        cls.config = json.loads(CONFIG.read_text(encoding="utf-8"))

    def test_all_level_thresholds_against_independent_decimal_oracle(self):
        c = self.config["progression"]
        total = 0
        with localcontext() as context:
            context.prec = 180
            growth = Decimal(c["xp_growth_numerator"]) / c["xp_growth_denominator"]
            for level in range(1, 100):
                self.assertEqual(total, self.model.xp_threshold(level))
                cost = int((Decimal(c["xp_first_level_cost"]) * growth ** (level - 1))
                           .to_integral_value(rounding=ROUND_CEILING))
                self.assertEqual(cost, self.model.xp_to_next(level))
                self.assertGreater(cost, 0)
                total += cost
        self.assertEqual(total, self.model.xp_threshold(100))
        self.assertLessEqual(total, INT32_MAX)
        self.assertEqual(0, self.model.xp_to_next(100))

    def test_xp_mapping_at_every_threshold_and_both_sides(self):
        for level in range(1, 101):
            threshold = self.model.xp_threshold(level)
            self.assertEqual(level, self.model.level_from_xp(threshold))
            self.assertEqual(level, self.model.level_from_xp(threshold + 1))
            if level > 1:
                self.assertEqual(level - 1, self.model.level_from_xp(threshold - 1))
        self.assertEqual(100, self.model.level_from_xp(INT32_MAX))

    def test_stat_rounding_monotonicity_bounds_and_endpoint_identity(self):
        for base in (*range(201), 9999, 10000, INT32_MAX - 1, INT32_MAX):
            previous = -1
            for level in range(1, 101):
                exact = base * (Fraction(1, 10) + Fraction(9, 10) * (level - 1) / 99)
                expected = (exact + Fraction(1, 2)).numerator // (exact + Fraction(1, 2)).denominator
                actual = self.model.scaled_stat(base, level)
                self.assertEqual(expected, actual)
                self.assertLessEqual(abs(actual - exact), Fraction(1, 2))
                self.assertGreaterEqual(actual, previous)
                self.assertLessEqual(actual, base)
                previous = actual
            self.assertEqual(base, previous)

    def test_scaled_stats_feed_existing_derived_model_without_breaking_bounds(self):
        derived = DerivedStats()
        previous = None
        for level in range(1, 101):
            stats = [self.model.scaled_stat(base, level) for base in (40, 20, 10, 30)]
            result = derived.derive(*stats)
            if previous is not None:
                for key, value in result.items():
                    if key in {"effective_cd_ms", "cd_multiplier_permille", "hit_chance_permille"}:
                        self.assertLessEqual(value, previous[key])
                    else:
                        self.assertGreaterEqual(value, previous[key])
            previous = result
        self.assertEqual(derived.derive(40, 20, 10, 30), previous)

    def test_method_preconditions_reject_wrong_types_and_out_of_domain_values(self):
        for bad in (True, False, 1.0, "1", None):
            for method in (self.model.xp_threshold, self.model.xp_to_next,
                           self.model.level_from_xp):
                with self.assertRaises(TypeError):
                    method(bad)
            for args in ((bad, 1), (1, bad)):
                with self.assertRaises(TypeError):
                    self.model.scaled_stat(*args)
        for bad in (-1, INT32_MAX + 1):
            with self.assertRaises(ValueError):
                self.model.level_from_xp(bad)
            with self.assertRaises(ValueError):
                self.model.scaled_stat(bad, 1)
        for bad in (0, 101, -1, INT32_MAX):
            for method in (self.model.xp_threshold, self.model.xp_to_next):
                with self.assertRaises(ValueError):
                    method(bad)
            with self.assertRaises(ValueError):
                self.model.scaled_stat(1, bad)

    def test_invalid_configuration_fails_before_building_an_unsupported_model(self):
        invalid = []
        for key, value in (("level_max", 101), ("appeared_weight", 0),
                           ("reserve_weight", 0),
                           ("xp_first_level_cost", 0), ("xp_growth_denominator", 0),
                           ("xp_growth_numerator", 100),
                           ("xp_growth_numerator", INT32_MAX)):
            document = copy.deepcopy(self.config)
            document["progression"][key] = value
            invalid.append(document)
        document = copy.deepcopy(self.config)
        document["model_version"] = 2
        invalid.append(document)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "progression.json"
            for document in invalid:
                path.write_text(json.dumps(document), encoding="utf-8")
                with self.subTest(document=document), self.assertRaises(ValueError):
                    LevelProgression(path)
            path.write_text('{"model_version":1,"model_version":1}', encoding="utf-8")
            with self.assertRaises(ValueError):
                LevelProgression(path)


class RewardWeightTests(unittest.TestCase):
    def test_swapped_out_and_current_creatures_keep_equal_participation_weights(self):
        appeared = [True, True, True, False, False, False]
        dead = [False] * 6
        before = appeared.copy(), dead.copy()
        self.assertEqual((4, 4, 4, 2, 2, 2), main_reward_weights(appeared, dead))
        self.assertEqual(before, (appeared, dead))

    def test_death_overrides_both_appeared_and_reserve_weights(self):
        self.assertEqual((0, 4, 0, 2), main_reward_weights(
            [True, True, False, False], [True, False, True, False]))
        self.assertEqual((0, 0), main_reward_weights([True, False], [True, True]))

    def test_all_participation_and_death_combinations(self):
        from itertools import product

        for count in range(1, 7):
            for appeared in product((False, True), repeat=count):
                for dead in product((False, True), repeat=count):
                    result = main_reward_weights(appeared, dead)
                    self.assertEqual(count, len(result))
                    for index, weight in enumerate(result):
                        if dead[index]:
                            self.assertEqual(0, weight)
                        else:
                            self.assertGreater(weight, 0)
                            self.assertEqual(2 if not appeared[index] else 4, weight)

                    for total in (0, 1, 120, INT32_MAX):
                        rewards = allocate_main_xp(total, appeared, dead)
                        self.assertTrue(all(isinstance(value, Fraction) for value in rewards))
                        self.assertEqual(total if any(result) else 0, sum(rewards))
                        per_weight = set()
                        for reward, weight in zip(rewards, result):
                            self.assertGreaterEqual(reward, 0)
                            if weight == 0:
                                self.assertEqual(0, reward)
                            else:
                                per_weight.add(reward / weight)
                        self.assertLessEqual(len(per_weight), 1)

    def test_invalid_flag_types_party_size_and_mismatched_parties(self):
        with self.assertRaises(TypeError):
            main_reward_weights([True], [False], curve=True)
        for bad in (None, "yes", True, {True}):
            for args in ((bad, [False]), ([True], bad)):
                with self.assertRaises(TypeError):
                    main_reward_weights(*args)
        for bad in ([0], [1], [None], ["true"]):
            for args in ((bad, [False]), ([True], bad)):
                with self.assertRaises(TypeError):
                    main_reward_weights(*args)
        for args in (([], []), ([True] * 7, [False] * 7), ([True], [False] * 2)):
            with self.assertRaises(ValueError):
                main_reward_weights(*args)


class EventAllocationTests(unittest.TestCase):
    def test_confirmed_death_example_redistributes_only_among_survivors(self):
        self.assertEqual((0, 40, 20, 20, 20, 20), allocate_main_xp(
            120, [True, True, False, False, False, False],
            [True, False, False, False, False, False]))

    def test_zero_xp_and_all_dead_never_divide_by_zero(self):
        self.assertEqual((0, 0), allocate_main_xp(0, [True, False], [False, False]))
        self.assertEqual((0, 0), allocate_main_xp(120, [True, False], [True, True]))

    def test_fractional_rewards_are_preserved_without_inventing_a_rounding_rule(self):
        self.assertEqual((Fraction(2, 3), Fraction(1, 3)),
                         allocate_main_xp(1, [True, False], [False, False]))

    def test_swaps_reentries_death_snapshots_and_new_battles(self):
        event = EventParticipation([10, 20, 30])
        event.record_entry(10)
        event.record_entry(20)
        first = event.allocate(120, [False] * 3)
        event.record_entry(10)
        self.assertEqual(first, event.allocate(120, [False] * 3))
        self.assertEqual((0, 80, 40), event.allocate(120, [True, False, False]))
        self.assertEqual(first, event.allocate(120, [False] * 3))
        event.reset_event()
        event.record_entry(30)
        self.assertEqual((30, 30, 60), event.allocate(120, [False] * 3))

    def test_overlapping_battle_capture_and_quest_records_are_independent(self):
        party = [1, 2, 3]
        battle = EventParticipation(party)
        capture = EventParticipation(party)
        quest = EventParticipation(party)
        party.reverse()
        battle.record_entry(1)
        capture.record_entry(2)
        quest.record_entry(3)
        self.assertEqual((60, 30, 30), battle.allocate(120, [False] * 3))
        self.assertEqual((30, 60, 30), capture.allocate(120, [False] * 3))
        self.assertEqual((30, 30, 60), quest.allocate(120, [False] * 3))
        capture.reset_event()
        self.assertEqual((40, 40, 40), capture.allocate(120, [False] * 3))
        self.assertEqual((60, 30, 30), battle.allocate(120, [False] * 3))

    def test_reward_and_participation_preconditions(self):
        for value in (True, 1.0, "1", None):
            with self.assertRaises(TypeError):
                allocate_main_xp(value, [True], [False])
        for value in (-1, INT32_MAX + 1):
            with self.assertRaises(ValueError):
                allocate_main_xp(value, [True], [False])
        for party in (None, "1", {1}):
            with self.assertRaises(TypeError):
                EventParticipation(party)
        for party in ([], list(range(1, 8)), [1, 1], [0], [-1], [INT32_MAX + 1]):
            with self.assertRaises(ValueError):
                EventParticipation(party)
        for party in ([True], [1.0], ["1"]):
            with self.assertRaises(TypeError):
                EventParticipation(party)
        event = EventParticipation([1])
        for value in (True, 1.0, "1", None):
            with self.assertRaises(TypeError):
                event.record_entry(value)
        for value in (0, -1, 2, INT32_MAX + 1):
            with self.assertRaises(ValueError):
                event.record_entry(value)
        with self.assertRaises(ValueError):
            event.allocate(1, [False, False])
        with self.assertRaises(TypeError):
            event.allocate(1, [0])


if __name__ == "__main__":
    unittest.main()
