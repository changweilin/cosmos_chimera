"""Invariant tests; golden comparisons guard serialization/port interoperability only."""

import copy
from fractions import Fraction
import json
from pathlib import Path
import random
import tempfile
import unittest

from derived_stats import DerivedStats, GOLDEN, INT32_MAX, PERMILLE, PROPOSAL, golden_text


class DerivedStatsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.model = DerivedStats()
        cls.c = json.loads(PROPOSAL.read_text(encoding="utf-8"))

    def assert_bounds(self, result, cooldown):
        c = self.c
        self.assertTrue(c["hp"]["base"] <= result["max_hp"] <= c["hp"]["base"] + c["hp"]["gain"])
        self.assertTrue(PERMILLE <= result["melee_volume_permille"] <= PERMILLE + c["melee_volume"]["gain_permille"])
        self.assertTrue(PERMILLE <= result["ranged_multiplier_permille"] <= PERMILLE + c["ranged_distance"]["gain_permille"])
        self.assertTrue(c["cd"]["min_permille"] <= result["cd_multiplier_permille"] <= PERMILLE)
        self.assertTrue(c["cd"]["floor_ms"] <= result["effective_cd_ms"] <= max(c["cd"]["floor_ms"], cooldown))
        self.assertTrue(c["speed"]["base_mm_s"] <= result["move_speed_mm_s"] <= c["speed"]["base_mm_s"] + c["speed"]["gain_mm_s"])
        self.assertTrue(0 <= result["evasion_permille"] <= c["evasion"]["cap_permille"])
        self.assertTrue(c["hit"]["floor_permille"] <= result["hit_chance_permille"] <= PERMILLE)
        self.assertTrue(0 <= result["ranged_distance_mm"] <= c["ranged_distance"]["cap_mm"])
        for value in result.values():
            self.assertIs(type(value), int)
            self.assertTrue(0 <= value <= INT32_MAX)
        linear = result["melee_linear_permille"]
        volume = result["melee_volume_permille"] * PERMILLE ** 2
        self.assertLessEqual(linear ** 3, volume)
        self.assertLess(volume, (linear + 1) ** 3)

    def test_entire_effective_domain_is_monotone_and_bounded(self):
        # Every raw stat is either in this exhaustive domain or in its single
        # saturated tail equivalence class; no billion-iteration sampling claim.
        decreasing = {"cd_multiplier_permille", "effective_cd_ms", "hit_chance_permille"}
        previous = None
        for stat in range(self.c["stat_effective_cap"] + 1):
            result = self.model.derive(stat, stat, stat, stat)
            self.assert_bounds(result, self.c["reference_move"]["cooldown_ms"])
            if previous is not None:
                for key in result:
                    if key in decreasing:
                        self.assertLessEqual(result[key], previous[key], (stat, key))
                    else:
                        self.assertGreaterEqual(result[key], previous[key], (stat, key))
            previous = result

    def test_move_boundaries_across_entire_effective_domain(self):
        for stat in range(self.c["stat_effective_cap"] + 1):
            for cooldown, accuracy, base_range in ((0, 0, 0), (1, 1, 1), (INT32_MAX, 100, INT32_MAX)):
                result = self.model.derive(stat, stat, stat, stat, cooldown_ms=cooldown,
                                           accuracy_percent=accuracy, base_range_mm=base_range)
                self.assert_bounds(result, cooldown)

    def test_unbounded_schema_tail_and_int32_extremes(self):
        cap = self.c["stat_effective_cap"]
        for field in ("con", "str", "spi", "agi"):
            args = dict(con=0, str=0, spi=0, agi=0)
            args[field] = cap
            expected = self.model.derive(**args)
            for value in (cap + 1, INT32_MAX - 1, INT32_MAX, INT32_MAX + 1, 10 ** 100):
                args[field] = value
                self.assertEqual(expected, self.model.derive(**args))

    def test_independent_stats_do_not_leak_into_other_outputs(self):
        influences = {
            "con": {"max_hp"},
            "str": {"melee_volume_permille", "melee_linear_permille", "ranged_multiplier_permille", "ranged_distance_mm"},
            "spi": {"cd_multiplier_permille", "effective_cd_ms"},
            "agi": {"move_speed_mm_s", "evasion_permille", "hit_chance_permille"},
        }
        baseline = self.model.derive(0, 0, 0, 0)
        for field, affected in influences.items():
            args = dict(con=0, str=0, spi=0, agi=0)
            args[field] = self.c["stat_effective_cap"]
            result = self.model.derive(**args)
            for key in baseline.keys() - affected:
                self.assertEqual(baseline[key], result[key], (field, key))

    def test_all_accuracy_percentages_and_move_monotonicity(self):
        for agi in (0, 1, 100, self.c["stat_effective_cap"], INT32_MAX):
            hits = [self.model.derive(0, 0, 0, agi, accuracy_percent=a)["hit_chance_permille"]
                    for a in range(101)]
            self.assertEqual(hits, sorted(hits))
            for hit in hits:
                self.assertTrue(self.c["hit"]["floor_permille"] <= hit <= PERMILLE)
        for stat in (0, 100, INT32_MAX):
            results = [self.model.derive(0, stat, stat, 0, cooldown_ms=x, base_range_mm=x)
                       for x in (0, 1, 249, 250, 251, 999, 1000, INT32_MAX)]
            for key in ("effective_cd_ms", "ranged_distance_mm"):
                values = [r[key] for r in results]
                self.assertEqual(values, sorted(values))

    def test_rounding_matches_rational_bounds_without_float(self):
        rng = random.Random(20261106)
        cap = self.c["stat_effective_cap"]
        for _ in range(1000):
            stats = [rng.randrange(INT32_MAX + 1) if rng.randrange(2) else rng.randrange(cap + 1)
                     for _ in range(4)]
            cooldown, accuracy, distance = rng.randrange(INT32_MAX + 1), rng.randrange(101), rng.randrange(INT32_MAX + 1)
            result = self.model.derive(*stats, cooldown_ms=cooldown, accuracy_percent=accuracy, base_range_mm=distance)
            self.assert_bounds(result, cooldown)
            con, strength, spi, agi = [min(x, cap) for x in stats]
            c = self.c
            rational_outputs = {
                "max_hp": c["hp"]["base"] + Fraction(c["hp"]["gain"] * con, con + c["hp"]["half_stat"]),
                "melee_volume_permille": PERMILLE + Fraction(c["melee_volume"]["gain_permille"] * strength, strength + c["melee_volume"]["half_stat"]),
                "ranged_multiplier_permille": PERMILLE + Fraction(c["ranged_distance"]["gain_permille"] * strength, strength + c["ranged_distance"]["half_stat"]),
                "move_speed_mm_s": c["speed"]["base_mm_s"] + Fraction(c["speed"]["gain_mm_s"] * agi, agi + c["speed"]["half_stat"]),
                "evasion_permille": Fraction(c["evasion"]["cap_permille"] * agi, agi + c["evasion"]["half_stat"]),
            }
            for key, value in rational_outputs.items():
                self.assertTrue(result[key] <= value < result[key] + 1)
            cd_rational = PERMILLE - Fraction((PERMILLE - c["cd"]["min_permille"]) * spi, spi + c["cd"]["half_stat"])
            self.assertTrue(result["cd_multiplier_permille"] - 1 < cd_rational <= result["cd_multiplier_permille"])
            cd_product = Fraction(cooldown * result["cd_multiplier_permille"], PERMILLE)
            if cd_product > c["cd"]["floor_ms"]:
                self.assertTrue(result["effective_cd_ms"] - 1 < cd_product <= result["effective_cd_ms"])
            hit = Fraction(accuracy * (PERMILLE - result["evasion_permille"]), 100)
            if hit >= c["hit"]["floor_permille"]:
                self.assertTrue(result["hit_chance_permille"] <= hit < result["hit_chance_permille"] + 1)
            ranged = Fraction(distance * result["ranged_multiplier_permille"], PERMILLE)
            if ranged < c["ranged_distance"]["cap_mm"]:
                self.assertTrue(result["ranged_distance_mm"] <= ranged < result["ranged_distance_mm"] + 1)
            self.assertEqual(result, self.model.derive(*stats, cooldown_ms=cooldown, accuracy_percent=accuracy, base_range_mm=distance))

    def test_csharp_int64_intermediate_bounds(self):
        # Conservative bounds for every accepted coefficient file, not just this
        # proposal: coefficients <= 10^6, multipliers <= 2000, root search <=2000.
        bounds = (1_000_000 + 1_000_000, 1_000_000 ** 2,
                  INT32_MAX * (2 * PERMILLE), INT32_MAX * PERMILLE + PERMILLE - 1,
                  (2 * PERMILLE) ** 3, 100 * PERMILLE,
                  2 * PERMILLE * PERMILLE ** 2)
        for bound in bounds:
            self.assertLessEqual(bound, (1 << 63) - 1)

    def test_invalid_inputs_fail_loudly(self):
        for field in ("con", "str", "spi", "agi"):
            for value in (-1, True, 1.5, "1", None):
                args = dict(con=0, str=0, spi=0, agi=0)
                args[field] = value
                with self.subTest(field=field, value=value), self.assertRaises((TypeError, ValueError)):
                    self.model.derive(**args)
        for field in ("cooldown_ms", "accuracy_percent", "base_range_mm"):
            maximum = 100 if field == "accuracy_percent" else INT32_MAX
            for value in (-1, True, 1.5, "1", maximum + 1):
                with self.subTest(field=field, value=value), self.assertRaises((TypeError, ValueError)):
                    self.model.derive(0, 0, 0, 0, **{field: value})

    def test_loading_rejects_invalid_coefficients(self):
        changes = (("stat_effective_cap", 0), ("hp.half_stat", 0),
                   ("hp.gain", 1.5), ("hp.base", True), ("hp.base", 0),
                   ("cd.min_permille", 0), ("cd.floor_ms", 0),
                   ("hit.floor_permille", PERMILLE + 1),
                   ("melee_volume.gain_permille", PERMILLE + 1),
                   ("ranged_distance.gain_permille", PERMILLE + 1),
                   ("evasion.cap_permille", PERMILLE), ("speed.gain_mm_s", 1_000_001),
                   ("reference_move.accuracy_percent", 101), ("stage_bands.adult.total_stats", 0))
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "proposal.json"
            for key, value in changes:
                config = copy.deepcopy(self.c)
                target = config
                parts = key.split(".")
                for part in parts[:-1]:
                    target = target[part]
                target[parts[-1]] = value
                path.write_text(json.dumps(config), encoding="utf-8")
                with self.subTest(key=key), self.assertRaises((TypeError, ValueError)):
                    DerivedStats(path)
            path.write_text('{"model_version": 1, "model_version": 1}', encoding="utf-8")
            with self.assertRaises(ValueError):
                DerivedStats(path)

    def test_coefficients_are_read_from_requested_file(self):
        config = copy.deepcopy(self.c)
        config["hp"]["gain"] = 0
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "proposal.json"
            path.write_text(json.dumps(config), encoding="utf-8")
            model = DerivedStats(path)
            self.assertEqual(model.derive(0, 0, 0, 0)["max_hp"],
                             model.derive(INT32_MAX, 0, 0, 0)["max_hp"])

    def test_golden_regeneration_is_byte_stable(self):
        # Exact bytes are an interoperability artifact, never a balance target.
        self.assertEqual(GOLDEN.read_bytes(), golden_text(self.model).encode("utf-8"))
        self.assertEqual(golden_text(self.model), golden_text(DerivedStats()))


if __name__ == "__main__":
    unittest.main()
