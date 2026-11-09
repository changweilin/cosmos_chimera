import json
import re
from pathlib import Path

root = Path(__file__).resolve().parent
repo = root.parents[2]
names = ["affixes.json", "region-themes.json", "quest-templates.json", "boss-epithets.json", "strings.json", "demo-ui-supplement.json"]
data = {name: json.loads((root / name).read_text(encoding="utf-8")) for name in names}

def check_localized(value):
    if isinstance(value, dict):
        if "zh-TW" in value or "en" in value:
            assert set(value) == {"zh-TW", "en"}
            assert all(isinstance(v, str) and v.strip() for v in value.values())
            assert set(re.findall(r"\{([^{}]+)\}", value["zh-TW"])) == set(re.findall(r"\{([^{}]+)\}", value["en"]))
        else:
            for child in value.values():
                check_localized(child)
    elif isinstance(value, list):
        for child in value:
            check_localized(child)

for value in data.values():
    check_localized(value)
print(f"PASS: {len(names)} UTF-8 JSON files; localized fields and placeholders are complete")

elements = "metal wood water fire earth ice wind thunder light shadow".split()
ids = {f"affix_{element}_surge" for element in elements} | {"affix_frenzy", "affix_pack", "affix_fog", "affix_bounty"}
affixes = data["affixes.json"]
assert len(affixes) == 14 and {a["id"] for a in affixes} == ids
for affix in affixes:
    number = "1" if affix["id"] == "affix_pack" else "10" if affix["id"] == "affix_bounty" else None if affix["id"] == "affix_fog" else "15"
    if number:
        assert all(re.search(rf"(?<!\d){number}(?!\d)", text) for text in affix["desc"].values())
print("PASS: 14 affix IDs and both-language numerical descriptions (fog has no specified number)")

themes = data["region-themes.json"]
assert len(themes) == 8 and {t["id"] for t in themes} == set("ruins forest riverbank highland coast crater ice_shelf canyon".split())
assert all(len(t["desc"]["zh-TW"]) <= 30 and len(t["desc"]["en"].split()) <= 20 for t in themes)
print("PASS: 8 region themes and description length limits")

quest = data["quest-templates.json"]
required = {"reach_poi": {"poi"}, "defeat_wild": {"count", "element"}, "defeat_elite": set(), "capture_one": set(), "defeat_boss": {"boss"}}
assert len(quest["steps"]) == 5 and {s["type"] for s in quest["steps"]} == set(required)
for step in quest["steps"]:
    assert len(step["variants"]) == 3
    for variant in step["variants"]:
        for text in variant.values():
            assert set(re.findall(r"\{([^{}]+)\}", text)) == required[step["type"]]
            assert not re.search(r"[{}]", re.sub(r"\{[^{}]+\}", "", text))
assert len(quest["chains"]) == 3 and len({c["id"] for c in quest["chains"]}) == 3
print("PASS: 5 step types, 3 variants each, required placeholders, 3 chains")

source = (repo / "Data/_incoming/p3-planet-types-draft-01/README.md").read_text(encoding="utf-8")
source_ids = set(re.findall(r"^\| `(\w+_planet)`", source, re.MULTILINE))
bosses = data["boss-epithets.json"]
assert len(bosses) == 6 and {b["planet_type"] for b in bosses} == source_ids
assert all(len(b["epithets"]) == 3 for b in bosses)
print("PASS: 6 source planet types and 3 epithets each")

required_strings = {
    "lobby": "title start_run team choose_starter starter_confirm quit",
    "star_select": "title planet_type size_small size_medium size_large main_elements seed reroll launch loading",
    "hud": "hp cd_ready switch switch_cooldown command_focus command_free command_follow command_retreat capture_prompt capture_success capture_fail knocked_out quest_tracker boss_incoming player_down",
    "vessel": "talisman_card glass_bead magic_cube",
    "results": "victory defeat captured_list eggs_obtained return_lobby abandon_run",
}
strings = data["strings.json"]
assert len(strings) <= 80 and all(re.fullmatch(r"ui\.[a-z_]+\.[a-z0-9_]+", key) for key in strings)
assert {f"ui.{screen}.{key}" for screen, items in required_strings.items() for key in items.split()} <= strings.keys()
assert all(set(re.findall(r"\{([^{}]+)\}", text)) == {"key", "vessel"} for text in strings["ui.hud.capture_prompt"].values())
print(f"PASS: {len(strings)} UI keys, naming and required items, capture placeholders")

supplement = data["demo-ui-supplement.json"]
assert not strings.keys() & supplement.keys()
assert all(re.fullmatch(r"(?:ui\.[a-z_]+\.[a-z0-9_]+|planet\.[a-z_]+|element\.[a-z_]+)", key) for key in supplement)
print(f"PASS: {len(supplement)} supplement keys; no base-key collisions")
