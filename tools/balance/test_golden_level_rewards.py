"""Regeneration, coverage and independent ledger checks for the C# fixture."""

from fractions import Fraction
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from derived_stats import INT32_MAX
from generate_level_rewards import GOLDEN, RANDOM_CASES, build_vectors, render_vectors
from level_rewards import LevelRewards


class GoldenLevelRewardsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.vectors = json.loads(GOLDEN.read_text(encoding="utf-8"))

    def test_regenerated_reference_matches_checked_in_json_and_bytes(self):
        with patch("level_rewards.PROPOSAL", GOLDEN.with_name("missing-proposal.json")):
            generated = build_vectors()
        self.assertEqual(self.vectors, json.loads(render_vectors(generated)))
        self.assertEqual(GOLDEN.read_bytes(), render_vectors(generated).encode("utf-8"))
        self.assertLess(GOLDEN.stat().st_size, 400_000)

    def test_cli_regeneration_matches_fixture(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "golden.json"
            subprocess.run([sys.executable, str(GOLDEN.with_name("generate_level_rewards.py")),
                            "--output", str(output)], check=True)
            self.assertEqual(GOLDEN.read_bytes(), output.read_bytes())

    def test_required_numerical_domains_are_covered(self):
        v = self.vectors
        model = LevelRewards()
        self.assertEqual(101, len(v["thresholds"]["T"]))
        self.assertEqual(100, len(v["thresholds"]["C"]))
        self.assertEqual(0, v["thresholds"]["T"][1])
        self.assertGreaterEqual(len(v["level_of_xp"]), 60)
        xp_cases = {row["xp"] for row in v["level_of_xp"]}
        self.assertTrue({0, model.cap, model.cap + 1, INT32_MAX} <= xp_cases)
        for level in range(2, 101):
            self.assertTrue({model.curve.xp_threshold(level) - 1,
                             model.curve.xp_threshold(level)} <= xp_cases)
        self.assertGreaterEqual(len(v["scaled_stat"]), 40)
        self.assertEqual({(b, l) for b in (0, 1, 54, 55, 109, 110, 200, INT32_MAX // 110, INT32_MAX)
                          for l in (1, 10, 50, 99, 100)},
                         {(r["base"], r["level"]) for r in v["scaled_stat"]})
        self.assertEqual({(d, t, r) for d in range(1, 11) for t in range(3)
                          for r in ("normal", "elite", "boss")},
                         {(r["difficulty"], r["tier"], r["role"]) for r in v["wild_level"]})
        roles = {"wild": ("normal", "elite"), "capture": ("normal", "elite", "boss"),
                 "quest_step": ("normal",), "quest_chain": ("boss",), "boss": ("boss",)}
        self.assertEqual({(s, l, r) for s, rs in roles.items() for r in rs
                          for l in (1, 9, 10, 11, 50, 98, 99, 100)},
                         {(r["source"], r["level"], r["role"]) for r in v["source_xp"]})
        self.assertEqual({0, 1, 2}, {r["stage"] for r in v["migrate"]})
        self.assertEqual(RANDOM_CASES, sum(r["name"].startswith("seeded_") for r in v["settle"]))
        self.assertGreaterEqual(RANDOM_CASES, 200)

    def test_settlement_fixture_has_exact_ledgers_and_required_edges(self):
        cases = self.vectors["settle"]
        cap = LevelRewards().cap
        by_name = {case["name"]: case for case in cases}
        self.assertEqual(len(cases), len(by_name))
        self.assertEqual({"9": 0, "1": 1, "4": 0}, by_name["main_ties_1"]["outputs"]["paid"])
        self.assertEqual("1/2", by_name["single_odd_actual"]["outputs"]["sidekick_loss"])
        self.assertEqual(str(INT32_MAX) + "/1", by_name["all_dead"]["outputs"]["main_loss"])
        group_sizes = set()
        for case in cases:
            inputs, outputs = case["inputs"], case["outputs"]
            mains, groups = inputs["mains"], inputs["sidekicks"]
            paid = outputs["paid"]
            main_paid = sum(paid[str(c["id"])] for c in mains)
            side_paid = sum(paid[str(c["id"])] for group in groups.values() for c in group)
            entitlement = sum((Fraction(paid[str(c["id"])], 2) for c in mains
                               if any(not s["dead"] for s in groups[str(c["id"])])), Fraction(0))
            self.assertEqual(inputs["E"], main_paid + Fraction(outputs["main_loss"]))
            self.assertEqual(entitlement, side_paid + Fraction(outputs["sidekick_loss"]))
            for key in ("main_loss", "sidekick_loss"):
                value = Fraction(outputs[key])
                self.assertEqual(f"{value.numerator}/{value.denominator}", outputs[key])
                self.assertGreaterEqual(value, 0)
            for c in mains + [s for group in groups.values() for s in group]:
                self.assertIs(type(paid[str(c["id"])]), int)
                if c["dead"] or c["xp"] >= cap:
                    self.assertEqual(0, paid[str(c["id"])])
            group_sizes.update(map(len, groups.values()))
        self.assertEqual(set(range(5)), group_sizes)

    def test_fixture_contains_no_float_values(self):
        def walk(value):
            self.assertNotIsInstance(value, float)
            if isinstance(value, dict):
                for item in value.values():
                    walk(item)
            elif isinstance(value, list):
                for item in value:
                    walk(item)
        walk(self.vectors)


if __name__ == "__main__":
    unittest.main()
