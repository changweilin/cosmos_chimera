"""Shared encyclopedia schema and cross-record contract tests; no invented lore fixtures."""
import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))
import validate_data


def entry(kind="creature", stage="juvenile"):
    record = {
        "id": "dex_fixture", "kind": kind, "subject_id": "fixture", "game_id": None,
        "status": "draft",
        "basic": dict.fromkeys(("name_zh", "name_en", "appearance", "summary")),
        "sources": [{"id": "wiki", "storage": "drive", "path": "creatures/fixture/fixture_juvenile_05_wiki.md",
                     "sha256": "0" * 64, "drive_file_id": "fixture_only"}],
        "assets": {"wiki": "wiki", "portrait": None, "model": None},
    }
    if kind == "creature":
        record["stage"] = stage
        record["details"] = dict.fromkeys(("ecology", "habits", "habitat", "reproduction", "growth"))
    elif kind == "character":
        record["details"] = dict.fromkeys(("occupation", "biography", "race", "physique", "likes", "dislikes"))
    else:
        record["skill_type"] = "move"
        record["details"] = {"description": None, "effects": None, "field_effects": None, "value_refs": []}
    return record


class EncyclopediaTests(unittest.TestCase):
    def check(self, entries):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            folder = root / "encyclopedia"
            folder.mkdir()
            for index, record in enumerate(entries):
                (folder / f"entry_{index}.json").write_text(json.dumps([record]), encoding="utf-8")
            return validate_data.validate(root, REPO / "Data" / "schemas")

    def test_all_categories_and_creature_stages_allow_explicit_missing_content(self):
        for kind, stage in [("character", None), ("skill", None)] + [
                ("creature", stage) for stage in ("juvenile", "adolescent", "adult", "overload")]:
            with self.subTest(kind=kind, stage=stage):
                self.assertEqual([], self.check([entry(kind, stage)]))

    def test_missing_required_fields_and_cross_category_fields_fail(self):
        for kind in ("creature", "character", "skill"):
            record = entry(kind)
            for key in list(record["details"]):
                invalid = copy.deepcopy(record)
                del invalid["details"][key]
                with self.subTest(kind=kind, missing=key):
                    self.assertIn("schema", [f.code for f in self.check([invalid])])
        record = entry("character")
        record["stage"] = "adult"
        self.assertIn("schema", [f.code for f in self.check([record])])

    def test_duplicate_ids_and_subjects_across_files_fail(self):
        first = entry()
        second = copy.deepcopy(first)
        self.assertIn("duplicate_id", [f.code for f in self.check([first, second])])
        second["id"] = "dex_other"
        self.assertIn("duplicate_subject", [f.code for f in self.check([first, second])])
        second["stage"] = "adult"
        self.assertEqual([], self.check([first, second]))

    def test_source_links_and_duplicate_sources_fail(self):
        record = entry()
        record["assets"]["model"] = "absent"
        self.assertIn("dangling_source", [f.code for f in self.check([record])])
        record["assets"]["model"] = None
        record["sources"].append(copy.deepcopy(record["sources"][0]))
        self.assertIn("duplicate_source", [f.code for f in self.check([record])])

    def test_multiple_entries_cannot_map_to_one_gameplay_subject(self):
        first = entry()
        first["game_id"] = "mapped_form"
        second = copy.deepcopy(first)
        second.update(id="other_entry", subject_id="other_subject")
        self.assertIn("duplicate_game_mapping", [f.code for f in self.check([first, second])])

    def test_drive_sources_require_file_id_and_local_paths(self):
        for change in ({"drive_file_id": None}, {"sha256": "unchecked"},
                       {"path": "../other/wiki.md"}, {"path": "https://example.com/wiki"}):
            record = entry()
            record["sources"][0].update(change)
            with self.subTest(change=change):
                self.assertIn("schema", [f.code for f in self.check([record])])

    def test_numerical_values_are_references_with_known_sources(self):
        record = entry("skill")
        record["details"]["value_refs"] = [{"source": "wiki", "pointer": "/0/power", "unit": "power"}]
        self.assertEqual([], self.check([record]))
        record["details"]["value_refs"][0]["source"] = "absent"
        self.assertIn("dangling_source", [f.code for f in self.check([record])])
        record["details"]["value_refs"][0]["pointer"] = "/bad~escape"
        self.assertIn("schema", [f.code for f in self.check([record])])

    def test_schema_invalid_shapes_do_not_crash_semantic_checks(self):
        for value in (None, [], 1, {"id": []}):
            record = entry()
            record["sources"] = [value]
            record["subject_id"] = value
            self.assertTrue(self.check([record]))


if __name__ == "__main__":
    unittest.main()
