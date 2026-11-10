"""Acceptance tests for tools/validate_data.py against Data/_incoming/p0-05-samples.

Run: python -m unittest discover -s tools/tests
Each invalid fixture is overlaid onto a copy of the valid set so cross-file checks
(dangling refs, edge endpoints) have real context, mirroring how a batch lands in Data/.
"""
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))
import validate_data  # noqa: E402

SAMPLES = REPO / "Data" / "_incoming" / "p0-05-samples"
SCHEMAS = REPO / "Data" / "schemas"

# fixture → (expected code, pointer prefix the error must point at)
EXPECTED = {
    "creatures/dangling_signature_move.json": ("dangling_ref", "/0/signature_move"),
    "creatures/evolves_to_on_adult.json": ("schema", "/0/evolves_to"),
    "creatures/mastery_too_many.json": ("schema", "/0/mastery"),
    "creatures/missing_resist_key.json": ("schema", "/0/resist"),
    "moves/bad_id_case.json": ("schema", "/0/id"),
    "moves/field_missing_duration.json": ("schema", "/0"),
    "moves/float_number.json": ("float_literal", ""),
    "moves/unknown_element.json": ("schema", "/0/element"),
    "relations/edge_wrong_order.json": ("edge_order", "/0"),
    "traits/unknown_trigger.json": ("schema", "/0/trigger/type"),
    "traits/effect_bad_params.json": ("schema", "/0/effects/0/params/element"),
    "traits/heal_max_stacks.json": ("schema", "/0/effects/0"),
    "characters/missing_vessel.json": ("schema", "/0"),
}


class ValidSamples(unittest.TestCase):
    def test_valid_set_has_no_findings(self):
        findings = validate_data.validate(SAMPLES / "valid", SCHEMAS)
        self.assertEqual([], [str(f) for f in findings])

    def test_every_invalid_fixture_is_covered(self):
        on_disk = {p.relative_to(SAMPLES / "invalid").as_posix() for p in (SAMPLES / "invalid").rglob("*.json")}
        self.assertEqual(set(EXPECTED), on_disk)


class InvalidSamples(unittest.TestCase):
    def _run_overlay(self, rel: str):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "data"
            shutil.copytree(SAMPLES / "valid", root)
            dest = root / rel
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(SAMPLES / "invalid" / rel, dest)
            return validate_data.validate(root, SCHEMAS)

    def test_each_invalid_fixture_reports_its_defect(self):
        for rel, (code, pointer) in EXPECTED.items():
            with self.subTest(fixture=rel):
                findings = self._run_overlay(rel)
                hits = [f for f in findings if f.level == "error" and f.file == rel
                        and f.code == code and f.pointer.startswith(pointer)]
                self.assertTrue(hits, f"expected [{code}] at {pointer}; got:\n" + "\n".join(map(str, findings)))
                # Readable: names the file and a JSON pointer, not a raw validator dump.
                self.assertIn(rel, str(hits[0]))
                self.assertLess(len(hits[0].message), 200)


class CrossFileRules(unittest.TestCase):
    def _write(self, root: Path, rel: str, text: str):
        p = root / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text, encoding="utf-8")

    def _with_valid(self, extra: dict[str, str], retired: str | None = None):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "data"
            shutil.copytree(SAMPLES / "valid", root)
            for rel, text in extra.items():
                self._write(root, rel, text)
            retired_path = None
            if retired is not None:
                retired_path = Path(tmp) / "retired-ids.txt"
                retired_path.write_text(retired, encoding="utf-8")
            return validate_data.validate(root, SCHEMAS, retired_path)

    def codes(self, findings):
        return {f.code for f in findings}

    def test_duplicate_id_across_kinds(self):
        dup = '[{"id":"sample_ember_strike","trigger":{"type":"on_spawn"},"effects":[{"type":"noop"}]}]'
        self.assertIn("duplicate_id", self.codes(self._with_valid({"traits/dup.json": dup})))

    def test_duplicate_json_key(self):
        self.assertIn("duplicate_key", self.codes(self._with_valid({"traits/k.json": '[{"id":"x_a","id":"x_b"}]'})))

    def test_parse_error_is_reported_not_raised(self):
        self.assertIn("parse_error", self.codes(self._with_valid({"moves/broken.json": "[{"})))

    def test_retired_id(self):
        self.assertIn("retired_id", self.codes(self._with_valid({}, retired="# header\nsample_fox_cub\n")))

    def test_duplicate_edge_in_other_file(self):
        edge = '[{"a":"sample_fox_beast","b":"sample_fox_cub","kind":"mentor"}]'
        self.assertIn("edge_duplicate", self.codes(self._with_valid({"relations/more.json": edge})))

    def test_edge_to_unknown_creature(self):
        edge = '[{"a":"sample_fox_cub","b":"sample_zz_ghost","kind":"mentor"}]'
        self.assertIn("dangling_ref", self.codes(self._with_valid({"relations/more.json": edge})))

    def test_edge_kind_outside_enum(self):
        edge = '[{"a":"sample_fox_cub","b":"sample_fox_lord","kind":"friends"}]'
        self.assertIn("schema", self.codes(self._with_valid({"relations/more.json": edge})))

    def test_trait_heal_needs_event_trigger_and_effects_are_bounded(self):
        # SOL P2-09 E4: these were schema-valid but rejected by EffectBook.
        heal = '{"type":"heal_percent_max_hp","params":{"percent":10}}'
        mod = '{"type":"mastery_mod","params":{"element":"fire","delta":5}}'

        def trait(trigger, effects):
            return '[{"id":"sample_extra_trait","trigger":' + trigger + ',"effects":[' + ",".join(effects) + ']}]'

        for trigger in ('{"type":"always"}', '{"type":"on_hp_below","params":{"percent":30}}',
                        '{"type":"on_hp_above","params":{"percent":30}}'):
            findings = self._with_valid({"traits/extra.json": trait(trigger, [heal])})
            self.assertTrue(any("event trigger" in f.message for f in findings), trigger)
        summon = '{"type":"on_summon","params":{"duration_ms":1000}}'
        self.assertEqual([], self._with_valid({"traits/extra.json": trait(summon, [heal])}))
        self.assertEqual([], self._with_valid({"traits/extra.json": trait(summon, [mod] * 8)}))
        self.assertIn("schema", self.codes(self._with_valid({"traits/extra.json": trait(summon, [mod] * 9)})))

    def test_hp_cross_below_is_an_event_trigger_with_percent_and_duration(self):
        # Q-C18 (user decision 2026-11-07): the heal-on-threshold traits use this event trigger.
        heal = '{"type":"heal_percent_max_hp","params":{"percent":12}}'

        def trait(params):
            return ('[{"id":"sample_extra_trait","trigger":{"type":"on_hp_cross_below","params":' + params
                    + '},"effects":[' + heal + ']}]')

        self.assertEqual([], self._with_valid({"traits/extra.json": trait('{"percent":25,"duration_ms":1000}')}))
        for params in ('{"percent":25}', '{"duration_ms":1000}', '{"percent":0,"duration_ms":1000}',
                       '{"percent":25,"duration_ms":1000,"element":"fire"}'):
            self.assertIn("schema", self.codes(self._with_valid({"traits/extra.json": trait(params)})), params)

    def test_effect_move_requires_effect(self):
        move = ('[{"id":"sample_buff","element":"light","kind":"effect","power":0,"cooldown_ms":1000,'
                '"accuracy":100,"range_class":"mid","shape":"burst","origin":"target","base_range_mm":12000}]')
        self.assertIn("schema", self.codes(self._with_valid({"moves/buff.json": move})))

    def test_target_burst_must_not_be_melee(self):
        move = ('{"id":"sample_blast","element":"fire","kind":"damage","power":30,"cooldown_ms":1000,'
                '"accuracy":100,"range_class":"%s","shape":"burst","origin":"%s","base_range_mm":3000}')
        for rc, origin, valid in [("melee", "target", False), ("mid", "target", True), ("melee", "caster", True)]:
            with self.subTest(rc=rc, origin=origin):
                errors = [f for f in self._with_valid({"moves/blast.json": "[" + move % (rc, origin) + "]"})
                          if f.level == "error"]
                self.assertEqual(valid, not errors, "\n".join(map(str, errors)))

    def test_effect_duration_required_iff_timed_effect(self):
        base = ('{"id":"sample_buff","element":"light","kind":"effect","power":0,"cooldown_ms":1000,'
                '"accuracy":100,"range_class":"melee","shape":"self","base_range_mm":0')
        haste = ',"effect":{"type":"move_speed_mod_percent","params":{"delta":20}}'
        heal = ',"effect":{"type":"heal_percent_max_hp","params":{"percent":20}}'
        cases = [
            (haste + ',"effect_duration_ms":5000', True),
            (haste, False),
            (haste + ',"effect_duration_ms":0', False),
            (heal, True),
            (heal + ',"effect_duration_ms":5000', False),
        ]
        for extra, valid in cases:
            with self.subTest(extra=extra):
                errors = [f for f in self._with_valid({"moves/buff.json": "[" + base + extra + "}]"})
                          if f.level == "error"]
                self.assertEqual(valid, not errors, "\n".join(map(str, errors)))
        damage = json.loads((SAMPLES / "valid" / "moves/moves.json").read_text(encoding="utf-8"))
        damage[0]["effect_duration_ms"] = 5000
        errors = [str(f) for f in self._with_valid({"moves/moves.json": json.dumps(damage)}) if f.level == "error"]
        self.assertTrue(any("must be omitted" in e for e in errors), errors)

    def test_numeric_representation_boundaries(self):
        cases = [
            ("moves", ("power",), 100000),
            ("moves", ("cooldown_ms",), 2147483647),
            ("moves", ("base_range_mm",), 2147483647),
            ("creatures", ("mastery", "fire"), 1000),
            ("creatures", ("stats", "con"), 2147483647),
            ("creatures", ("evolution", "materials", 0, "tier"), 2147483647),
            ("creatures", ("evolution", "materials", 0, "qty"), 2147483647),
        ]
        for kind, path, maximum in cases:
            for value, valid in [(maximum, True), (maximum + 1, False), (-1, False)]:
                with self.subTest(kind=kind, path=path, value=value):
                    relative = f"{kind}/{kind}.json"
                    records = json.loads((SAMPLES / "valid" / relative).read_text(encoding="utf-8"))
                    parent = records[0]
                    if path == ("base_range_mm",):
                        parent["movement"] = "none"  # the sample is a dash, capped at 36000 by Q-C19
                    for key in path[:-1]:
                        parent = parent[key]
                    parent[path[-1]] = value
                    errors = [f for f in self._with_valid({relative: json.dumps(records)}) if f.level == "error"]
                    self.assertEqual(valid, not errors, "\n".join(map(str, errors)))

    def test_evolution_level_bands_per_stage(self):
        # REQ: docs/design/level-progression.md — juvenile gates LV20-30, adolescent LV35-50, inclusive.
        records = json.loads((SAMPLES / "valid/creatures/creatures.json").read_text(encoding="utf-8"))
        by_stage = {r["stage"]: i for i, r in enumerate(records) if r.get("evolution")}
        for stage, low, high in (("juvenile", 20, 30), ("adolescent", 35, 50)):
            if stage not in by_stage:
                continue
            for value, valid in ((low, True), (high, True), (low - 1, False), (high + 1, False)):
                with self.subTest(stage=stage, value=value):
                    edited = json.loads(json.dumps(records))
                    edited[by_stage[stage]]["evolution"]["level"] = value
                    errors = [f for f in self._with_valid({"creatures/creatures.json": json.dumps(edited)})
                              if f.level == "error"]
                    self.assertEqual(valid, not errors, "\n".join(map(str, errors)))

    def test_schema_bands_match_balance(self):
        schema = json.loads((REPO / "Data/schemas/creature.schema.json").read_text(encoding="utf-8"))
        bands = json.loads((REPO / "Data/balance.json").read_text(encoding="utf-8"))["progression"]["evolution_level_bands"]
        for rule in schema["$defs"]["creature"]["allOf"]:
            stage = rule["if"]["properties"]["stage"]["const"]
            level = rule["then"]["properties"]["evolution"]["properties"]["level"]
            self.assertEqual(bands[stage], [level["minimum"], level["maximum"]], stage)

    def test_move_geometry_contracts(self):
        source = json.loads((SAMPLES / "valid/moves/moves.json").read_text(encoding="utf-8"))[0]
        for shape in ("front", "sweep", "burst", "line", "cone", "arc", "self", "ally", "field"):
            with self.subTest(shape=shape):
                move = {**source, "id": "review_move", "shape": shape, "movement": "none"}
                if shape == "burst":
                    move["origin"] = "target"
                    move["range_class"] = "mid"  # a target-point burst cannot be melee
                if shape == "field":
                    move["field_duration_ms"] = 6000  # Q-C17: required iff field
                self.assertEqual(set(), self.codes(self._with_valid({"moves/review.json": json.dumps([move])})))
        for changes in [
            {"shape": "aoe"}, {"shape": "dash"}, {"shape": "burst"},
            {"origin": "caster"}, {"shape": "cone"}, {"base_range_mm": 0}, {"base_range_mm": 36001},
        ]:
            with self.subTest(changes=changes):
                move = {**source, "id": "review_move", **changes}
                findings = self._with_valid({"moves/review.json": json.dumps([move])})
                self.assertIn("schema", self.codes(findings))

    def test_dash_range_cap_is_inclusive(self):
        # Q-C19: dash path length is capped at 36000 mm in data, same as the ranged cap.
        source = json.loads((SAMPLES / "valid/moves/moves.json").read_text(encoding="utf-8"))[0]
        self.assertEqual("dash", source["movement"])
        move = {**source, "id": "review_move", "base_range_mm": 36000}
        self.assertEqual(set(), self.codes(self._with_valid({"moves/review.json": json.dumps([move])})))
        undashed = {**source, "id": "review_move", "movement": "none", "base_range_mm": 36001}
        self.assertEqual(set(), self.codes(self._with_valid({"moves/review.json": json.dumps([undashed])})))

    def test_material_outside_mastery_is_an_error(self):
        src = (SAMPLES / "valid" / "creatures" / "creatures.json").read_text(encoding="utf-8")
        mutated = src.replace('"element": "fire",\n          "tier": 1', '"element": "ice",\n          "tier": 1', 1)
        self.assertNotEqual(src, mutated)
        findings = self._with_valid({"creatures/creatures.json": mutated})
        self.assertEqual({"material_not_mastery"}, self.codes(findings))
        self.assertTrue(all(f.level == "error" for f in findings))

    def test_repeated_material_sum_is_bounded_by_int32(self):
        # Sol P2-05 review C2: each qty is legal alone, the merged (element, tier) total is not.
        creatures = json.loads((SAMPLES / "valid/creatures/creatures.json").read_text(encoding="utf-8"))
        mats = creatures[0]["evolution"]["materials"]
        base = dict(mats[0])
        mats[:] = [{**base, "qty": 2147483647}, {**base, "qty": 1}]
        findings = self._with_valid({"creatures/creatures.json": json.dumps(creatures)})
        self.assertEqual({"material_sum_overflow"}, self.codes(findings))
        mats[:] = [{**base, "qty": 2147483646}, {**base, "qty": 1}]
        self.assertEqual(set(), self.codes(self._with_valid({"creatures/creatures.json": json.dumps(creatures)})))
        mats[:] = [{**base, "qty": 2147483647}, {**base, "tier": base["tier"] + 1, "qty": 1}]
        self.assertEqual(set(), self.codes(self._with_valid({"creatures/creatures.json": json.dumps(creatures)})))

    def test_malformed_material_key_is_a_schema_error_not_a_crash(self):
        # Sol P2-05/07 r2 C8: unhashable element/tier crashed the material-sum check.
        creatures = json.loads((SAMPLES / "valid/creatures/creatures.json").read_text(encoding="utf-8"))
        mats = creatures[0]["evolution"]["materials"]
        base = dict(mats[0])
        for bad in ({"tier": []}, {"tier": {}}, {"element": []}, {"element": {}}, {"tier": True}):
            with self.subTest(bad=bad):
                mats[:] = [{**base, **bad, "qty": 1}]
                findings = self._with_valid({"creatures/creatures.json": json.dumps(creatures)})
                self.assertIn("schema", self.codes(findings))
                self.assertNotIn("material_sum_overflow", self.codes(findings))

    def test_malformed_material_entries_are_excluded_from_the_sum(self):
        # Sol P2-05/07 r3: with qty=1 a malformed entry could be summed wrongly yet stay below Int32 and pass.
        # Pair each with a legal MaxValue entry on the same key so wrongly summing it overflows.
        creatures = json.loads((SAMPLES / "valid/creatures/creatures.json").read_text(encoding="utf-8"))
        mats = creatures[0]["evolution"]["materials"]
        base = dict(mats[0])
        for bad in ({"tier": True}, {"tier": float(base["tier"])}, {"tier": None}, {"qty": True},
                    {"element": None}):
            with self.subTest(bad=bad):
                for pair in ([{**base, "qty": 2147483647}, {**base, "qty": 1, **bad}],
                             [{**base, "qty": 2147483647, **bad}, {**base, "qty": 1, **bad}]):
                    if "qty" in bad and pair[0].get("qty") is True:
                        continue
                    mats[:] = pair
                    findings = self._with_valid({"creatures/creatures.json": json.dumps(creatures)})
                    self.assertNotIn("material_sum_overflow", self.codes(findings))
                    self.assertTrue(self.codes(findings) & {"schema", "float_literal"})

    def test_ai_style_is_required_and_closed(self):
        # Q-C21: P2-10 reads the positioning style from data; a missing or unknown style must not default silently.
        creatures = json.loads((SAMPLES / "valid/creatures/creatures.json").read_text(encoding="utf-8"))
        for value in ("keep_distance", "flank", "charge", "hold"):
            creatures[0]["ai_style"] = value
            self.assertEqual(set(), self.codes(self._with_valid({"creatures/creatures.json": json.dumps(creatures)})))
        creatures[0]["ai_style"] = "kite"
        self.assertIn("schema", self.codes(self._with_valid({"creatures/creatures.json": json.dumps(creatures)})))
        del creatures[0]["ai_style"]
        self.assertIn("schema", self.codes(self._with_valid({"creatures/creatures.json": json.dumps(creatures)})))

    def test_temperament_is_required_and_closed(self):
        # D0-E2: territory radius comes from the species temperament; a missing value must not default silently.
        creatures = json.loads((SAMPLES / "valid/creatures/creatures.json").read_text(encoding="utf-8"))
        for value in ("timid", "cautious", "normal", "territorial", "fierce", "apex"):
            creatures[0]["temperament"] = value
            self.assertEqual(set(), self.codes(self._with_valid({"creatures/creatures.json": json.dumps(creatures)})))
        creatures[0]["temperament"] = "shy"
        self.assertIn("schema", self.codes(self._with_valid({"creatures/creatures.json": json.dumps(creatures)})))
        del creatures[0]["temperament"]
        self.assertIn("schema", self.codes(self._with_valid({"creatures/creatures.json": json.dumps(creatures)})))

    def test_cli_exit_codes(self):
        self.assertEqual(0, validate_data.main(["--root", str(SAMPLES / "valid")]))
        self.assertEqual(2, validate_data.main(["--root", str(SAMPLES / "does_not_exist")]))


if __name__ == "__main__":
    unittest.main()
