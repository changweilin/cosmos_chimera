"""Q-C32 invariants, exact probability oracles, CLI/document agreement."""

from collections import Counter
from copy import deepcopy
from fractions import Fraction
from itertools import product
import json
from math import comb
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from derived_stats import REPO
from level_rewards import simulate
from material_drops import (Catalog, MaterialDrops, PROPOSAL, ROLES, STAGES,
                            completion_quantile, expected_runs, pacing_table)


class MaterialDropTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.models = [MaterialDrops(option) for option in ("A", "B")]

    def test_inventory_excludes_incoming_and_counts_each_outgoing_cost_once(self):
        catalog = Catalog()
        self.assertTrue(catalog.sources)
        self.assertTrue(all("_incoming" not in Path(p).parts for p in catalog.sources))
        direct = Counter()
        for source in catalog.sources:
            for creature in json.loads((REPO / source).read_text(encoding="utf-8")):
                if creature["evolution"] is not None:
                    for item in creature["evolution"]["materials"]:
                        direct[item["element"], item["tier"]] += item["qty"]
        self.assertEqual(set(catalog.creatures), {c["id"] for line in catalog.lines.values() for c in line})
        self.assertEqual(catalog.demand(), {key: direct[key] for key in catalog.demand()})
        for line in catalog.lines.values():
            self.assertEqual(list(STAGES), [c["stage"] for c in line])
            self.assertIsNone(line[-1]["evolution"])
        for qty in catalog.demand().values():
            self.assertIs(type(qty), int)
            self.assertGreaterEqual(qty, 0)

    def test_formal_planet_biases_match_design_document(self):
        catalog = self.models[0].catalog
        documented = {"verdant_planet": ("wood", "water", "wind"),
                      "lava_planet": ("fire", "earth"),
                      "ice_crystal_planet": ("ice", "water"),
                      "storm_planet": ("thunder", "wind"),
                      "mineral_planet": ("metal", "earth"),
                      "twilight_planet": ("light", "shadow")}
        self.assertEqual(documented, catalog.planets)

    def test_event_distribution_exhaustive_integer_rolls_matches_expectation(self):
        for model in self.models:
            for planet, bias in model.catalog.planets.items():
                for role in ROLES:
                    rule = model.document["rules"][role]
                    victim = model.catalog.wild[0]
                    size = len(bias) if model.option == "B" else 1
                    count = Counter()
                    for roll, element_roll in product(range(100), range(size)):
                        drop = model.event_drop(planet, role, victim["id"], roll, element_roll)
                        self.assertLessEqual(len(drop), 1)
                        for (element, tier), qty in drop.items():
                            self.assertIn(element, model.catalog.elements)
                            self.assertEqual(rule["tier"], tier)
                            self.assertIs(type(qty), int)
                            self.assertGreater(qty, 0)
                            count[element, tier] += qty
                    self.assertEqual(Fraction(rule["chance_percent"] * rule["qty"], 100),
                                     Fraction(sum(count.values()), 100 * size))
                    if model.option == "B":
                        self.assertEqual(set(bias), {e for e, _ in count})
                        for element in bias:
                            self.assertEqual(Fraction(rule["chance_percent"], 100 * size),
                                             Fraction(count[element, rule["tier"]], 100 * size))
                    else:
                        expected = max(victim["mastery"], key=victim["mastery"].get)
                        self.assertEqual({expected}, {e for e, _ in count})

    def test_mastery_uses_highest_entry_and_first_entry_on_ties(self):
        model = self.models[0]
        victim = model.catalog.wild[0]
        victim_id = victim["id"]
        original = victim["mastery"]
        try:
            for mastery in ({"water": 10, "fire": 20}, {"water": 20, "fire": 20}):
                victim["mastery"] = mastery
                expected = max(mastery, key=mastery.get)
                self.assertEqual({(expected, 3): 1},
                                 model.event_drop("lava_planet", "boss", victim_id, 0))
        finally:
            victim["mastery"] = original

    def test_per_stage_probabilities_and_run_yields_conserve_total(self):
        for model in self.models:
            for planet in model.catalog.planets:
                for stage in range(3):
                    probabilities = model.element_probabilities(planet, stage)
                    self.assertEqual(set(model.catalog.elements), set(probabilities))
                    self.assertEqual(1, sum(probabilities.values()))
                    self.assertTrue(all(isinstance(p, Fraction) and p >= 0 for p in probabilities.values()))
                yields = model.run_yield(planet)
                self.assertTrue(all(isinstance(qty, Fraction) and qty >= 0 for qty in yields.values()))
                for role in ROLES:
                    rule = model.document["rules"][role]
                    count = sum(n for _, n in model._trials(role))
                    expected = Fraction(count * rule["qty"] * rule["chance_percent"], 100)
                    actual = sum(yields[e, rule["tier"]] for e in model.catalog.elements)
                    self.assertEqual(expected, actual)
                self.assertEqual(yields, model.run_yield(planet))

    def test_accessibility_cannot_be_hidden_by_high_drop_rates(self):
        expected_missing = {"A": {"light", "shadow", "thunder"}, "B": set()}
        for model in self.models:
            for tier in (1, 2, 3):
                missing = {e for e in model.catalog.elements if all(
                    model.run_yield(p)[e, tier] == 0 for p in model.catalog.planets)}
                self.assertEqual(expected_missing[model.option], missing)
                for element in missing:
                    for planet in model.catalog.planets:
                        self.assertEqual((None, None), model.completion(
                            planet, {"element": element, "tier": tier, "qty": 1}))
        b = self.models[1]
        for creature in b.catalog.creatures.values():
            if creature["evolution"]:
                for cost in creature["evolution"]["materials"]:
                    self.assertTrue(any(b.run_yield(p)[cost["element"], cost["tier"]] > 0
                                        for p in b.catalog.planets))

    def test_expected_runs_matches_independent_small_binomial_linear_system(self):
        for encounters, p in product((1, 2, 3, 13), (Fraction(1, 2), Fraction(1, 20), Fraction(3, 10))):
            for qty in range(1, 6):
                mean = expected_runs(qty, encounters, p)
                # One-run law of total expectation, independently formed PMF.
                continuation = Fraction(1)
                for successes in range(min(encounters, qty - 1) + 1):
                    prob = comb(encounters, successes) * p ** successes * (1-p) ** (encounters-successes)
                    continuation += prob * expected_runs(qty-successes, encounters, p)
                self.assertEqual(mean, continuation)
                self.assertGreaterEqual(mean, Fraction(qty, encounters) / p)
                self.assertLess(mean, Fraction(qty, encounters) / p + 1)
                if encounters == 1:
                    self.assertEqual(Fraction(qty) / p, mean)
                self.assertEqual(mean, expected_runs(qty, encounters, p))

    def test_quantile_is_minimal_and_monotone_with_cost(self):
        for encounters, p in product((1, 2, 13), (Fraction(1, 20), Fraction(3, 10), Fraction(1))):
            previous = 0
            for qty in range(1, 6):
                run = completion_quantile(qty, encounters, p)
                self.assertIs(type(run), int)
                self.assertGreaterEqual(run, previous)
                previous = run
                def complete(runs):
                    n = encounters * runs
                    return sum((comb(n, j) * p ** j * (1-p) ** (n-j)
                                for j in range(qty, n+1)), Fraction(0))
                self.assertGreaterEqual(complete(run), Fraction(4, 5))
                self.assertLess(complete(run - 1), Fraction(4, 5))

    def test_zero_unreachable_and_certain_success_boundaries(self):
        for qty, encounters in product(range(6), (1, 3, 13)):
            for function in (expected_runs, completion_quantile):
                self.assertEqual(0 if qty == 0 else None, function(qty, encounters, 0))
                self.assertEqual(-(-qty // encounters), function(qty, encounters, 1))

    def test_approved_xp_and_route_are_reused_without_per_run_rounding(self):
        for model in self.models:
            for party in (1, 2, 6):
                self.assertEqual(simulate("A", party)["active_xp"], model.xp_per_run[party])
            self.assertEqual(model.run, model.rewards.document["d0_run_model"])

    def test_invalid_probability_and_event_inputs_fail_even_for_zero_drops(self):
        for function in (expected_runs, completion_quantile):
            for bad in (True, 1.0, "1", -1):
                with self.assertRaises((TypeError, ValueError)):
                    function(bad, 1, Fraction(1, 2))
                with self.assertRaises((TypeError, ValueError)):
                    function(1, bad, Fraction(1, 2))
            for bad in (True, 0.5, "1", -1, 2):
                with self.assertRaises((TypeError, ValueError)):
                    function(1, 1, bad)
            with self.assertRaises(ValueError):
                function(1, 0, 1)
        for bad in (True, 0, 100, 80.0):
            with self.assertRaises((TypeError, ValueError)):
                completion_quantile(1, 1, 1, bad)
        for model in self.models:
            victim = model.catalog.wild[0]["id"]
            for roll in (-1, 100, True, 0.0):
                with self.assertRaises((TypeError, ValueError)):
                    model.event_drop("lava_planet", "normal", victim, roll)
            for planet, role, victim_id in (("unknown", "normal", victim),
                                            ("lava_planet", "capture", victim),
                                            ("lava_planet", "normal", "sun_mirror_chick")):
                with self.assertRaises(ValueError):
                    model.event_drop(planet, role, victim_id, 99)
            for bad in (-1, True, 2 if model.option == "B" else 1):
                with self.assertRaises((TypeError, ValueError)):
                    model.event_drop("lava_planet", "normal", victim, 99, bad)
            for stage in (-1, 3, True, 0.0):
                with self.assertRaises((TypeError, ValueError)):
                    model.element_probabilities("lava_planet", stage)
            for cost in ({"element": "none", "tier": 1, "qty": 1},
                         {"element": "water", "tier": True, "qty": 1},
                         {"element": "water", "tier": 1, "qty": 0}):
                with self.assertRaises((TypeError, ValueError)):
                    model.completion("lava_planet", cost)
        with self.assertRaises(ValueError):
            MaterialDrops("C")

    def test_proposals_reject_confirmation_wrong_policy_and_noninteger_coefficients(self):
        base = self.models[0].document
        mutations = [{"confirmed": True}, {"status": "approved"}, {"option": "B"},
                     {"proposal_version": True}, {"tier_substitution": True},
                     {"bank_before_evolution": False}, {"element_source": "uniform_planet_bias"}]
        documents = [{**base, **change} for change in mutations]
        for key, values in {"chance_percent": (True, -1, 101, 10.0),
                            "tier": (True, 0, 2), "qty": (True, 0, -1, 1.0, 2)}.items():
            for value in values:
                document = deepcopy(base)
                document["rules"]["normal"][key] = value
                documents.append(document)
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "invalid.json"
            for document in documents:
                path.write_text(json.dumps(document), encoding="utf-8")
                with self.assertRaises((TypeError, ValueError)):
                    MaterialDrops("A", path)

    def test_cli_table_equals_document_block_exactly(self):
        table = pacing_table()
        output = subprocess.check_output(
            [sys.executable, str(Path(__file__).with_name("material_drops.py"))],
            env={**os.environ, "PYTHONIOENCODING": "utf-8", "PYTHONDONTWRITEBYTECODE": "1"})
        self.assertEqual(table, output.decode("utf-8").replace("\r\n", "\n").strip())
        doc = (PROPOSAL / "proposal.md").read_text(encoding="utf-8")
        begin, end = "<!-- CLI-BEGIN -->", "<!-- CLI-END -->"
        self.assertEqual(1, doc.count(begin))
        self.assertEqual(1, doc.count(end))
        self.assertEqual(table, doc.split(begin)[1].split(end)[0].strip())


if __name__ == "__main__":
    unittest.main()
