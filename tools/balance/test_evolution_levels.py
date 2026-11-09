"""Validate the complete per-species proposal without mutating demo data."""

import json
import unittest

from derived_stats import REPO, _unique_object


class EvolutionLevelProposalTests(unittest.TestCase):
    def test_every_nonadult_species_has_one_fixed_gate_in_its_approved_band(self):
        path = REPO / "Data/_incoming/p2-12-level-proposal/evolution-levels.json"
        proposal = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=_unique_object)
        self.assertEqual("proposal", proposal["status"])
        self.assertIs(proposal["confirmed"], False)
        creatures = {}
        for path in sorted((REPO / "Data/_demo/creatures").glob("*.json")):
            for creature in json.loads(path.read_text(encoding="utf-8")):
                self.assertNotIn(creature["id"], creatures)
                creatures[creature["id"]] = creature
        expected = {key for key, creature in creatures.items() if creature["stage"] != "adult"}
        levels = proposal["levels"]
        self.assertEqual(20, len(expected))
        self.assertEqual(expected, set(levels))
        self.assertEqual(expected | {"pacing_basis"}, set(proposal["rationale"]))
        bands = json.loads((REPO / "Data/balance.json").read_text(encoding="utf-8"))[
            "progression"]["evolution_level_bands"]
        for instance_id, level in levels.items():
            with self.subTest(instance_id=instance_id):
                creature = creatures[instance_id]
                self.assertNotEqual("adult", creature["stage"])
                low, high = bands[creature["stage"]]
                self.assertIs(type(level), int)
                self.assertLessEqual(low, level)
                self.assertLessEqual(level, high)
                self.assertTrue(proposal["rationale"][instance_id].strip())


if __name__ == "__main__":
    unittest.main()
