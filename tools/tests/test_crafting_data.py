"""Crafting references and permanent-equipment effects must fail before staging."""
import json
import shutil
import sys
from pathlib import Path

import pytest
REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))
import validate_data

SOURCE = REPO / "Data/_incoming/life-crafting-draft-01"


@pytest.fixture
def crafting(tmp_path):
    for kind in ("craft-items", "craft-recipes", "craft-grips"):
        shutil.copytree(SOURCE / kind, tmp_path / kind)
    return tmp_path


def mutate(root, kind, change):
    path = root / kind / f"{kind}.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    change(data)
    path.write_text(json.dumps(data), encoding="utf-8")


def errors(root):
    return [f for f in validate_data.validate(root, REPO / "Data/schemas") if f.level == "error"]


def test_complete_crafting_batch_validates(crafting):
    assert errors(crafting) == []


@pytest.mark.parametrize("kind,change", [
    ("craft-items", lambda rows: rows[0].update(gather_tool="missing_tool")),
    ("craft-recipes", lambda rows: rows[0]["costs"][0].update(item="missing_item")),
    ("craft-recipes", lambda rows: rows[0].update(tools=["raw_iron_ore"])),
])
def test_unknown_or_non_tool_references_are_rejected(crafting, kind, change):
    mutate(crafting, kind, change)
    assert any(f.code == "dangling_ref" for f in errors(crafting))


def test_permanent_equipment_cannot_contain_healing(crafting):
    def change(rows):
        item = next(row for row in rows if row["category"] == "equipment")
        item["equipment"]["creature_effects"] = [{"type": "heal_percent_max_hp", "params": {"percent": 10}}]
    mutate(crafting, "craft-items", change)
    assert any(f.code == "schema" for f in errors(crafting))


def test_recipe_rejects_nonpositive_cost_and_unqualified_utility(crafting):
    mutate(crafting, "craft-recipes", lambda rows: rows[0].update(utility="none", costs=[{"item": "raw_iron_ore", "quantity": 0}]))
    assert len(errors(crafting)) >= 2


def test_recipe_cannot_consume_its_reusable_tool(crafting):
    mutate(crafting, "craft-recipes", lambda rows: rows[0].update(tools=["craft_needle"], costs=[{"item": "craft_needle", "quantity": 1}]))
    assert any(f.code == "tool_consumed" for f in errors(crafting))


def test_weapon_requires_profile_and_gives_player_nothing(crafting):
    def change(rows):
        blade = next(row for row in rows if row["id"] == "craft_iron_blade")
        del blade["equipment"]["weapon"]
        blade["equipment"]["player"]["max_hp_percent"] = 5
    mutate(crafting, "craft-items", change)
    assert len([f for f in errors(crafting) if f.code == "schema"]) >= 2


def test_non_weapon_cannot_carry_weapon_profile(crafting):
    def change(rows):
        coat = next(row for row in rows if row["id"] == "craft_down_coat")
        coat["equipment"]["weapon"] = {"element": "ice", "element_mastery": 0, "power_permille": 1000, "speed_permille": 1000}
    mutate(crafting, "craft-items", change)
    assert any(f.code == "schema" for f in errors(crafting))


@pytest.mark.parametrize("grips", [[], ["two_handed", "one_hand_shield"], ["two_handed", "dual_wield", "one_hand_shield"], ["dual_wield", "dual_wield"], ["sword"]])
def test_grip_rows_hold_exactly_one_known_grip(crafting, grips):
    mutate(crafting, "craft-grips", lambda rows: rows[0].update(grips=grips))
    assert any(f.code == "schema" for f in errors(crafting))


def test_starters_belong_to_distinct_grip_modes(crafting):
    rows = json.loads((crafting / "craft-grips" / "craft-grips.json").read_text(encoding="utf-8"))
    by_species = {r["species"]: r["grips"] for r in rows}
    sun = {tuple(by_species[s]) for s in ("sun_mirror_chick", "sun_heliostat_heron", "sun_array_skyheron")}
    moon = {tuple(by_species[s]) for s in ("moon_night_pup", "moon_shade_wolf", "moon_longnight_wolf")}
    star = {tuple(by_species[s]) for s in ("star_blaze_foal", "star_storm_colt", "star_skyhoof_steed")}
    assert sun == {("one_hand_shield",)}
    assert moon == {("dual_wield",)}
    assert star == {("two_handed",)}


def test_grip_species_listed_once(crafting):
    mutate(crafting, "craft-grips", lambda rows: rows.append(dict(rows[0])))
    assert any(f.code == "duplicate_id" for f in errors(crafting))
