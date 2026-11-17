"""Author the requested cr_* gameplay draft against existing elemental archetypes.

Numbers are proposals, not extracted wiki facts. Preserve the wiki and asset identities.
"""
import copy
import json
from pathlib import Path

from build_encyclopedia import read

PROFILES = {
    "cr_aqua_slime": ("water", None, "hold", [25, 10, 15, 10, 20, 20]),
    "cr_chrono_beetle": ("metal", "thunder", "charge", [18, 18, 22, 12, 15, 15]),
    "cr_earth_golem": ("earth", None, "hold", [25, 5, 20, 20, 15, 15]),
    "cr_fire_wisp": ("fire", None, "flank", [12, 24, 20, 24, 10, 10]),
    "cr_frostblade_penguin": ("ice", "water", "flank", [16, 20, 12, 24, 14, 14]),
    "cr_ice_snowman": ("ice", None, "hold", [26, 6, 18, 10, 20, 20]),
    "cr_ink_stele": ("shadow", "earth", "hold", [22, 8, 24, 10, 20, 16]),
    "cr_light_khepri": ("light", None, "hold", [22, 8, 20, 12, 20, 18]),
    "cr_metal_vault": ("metal", None, "hold", [24, 6, 12, 18, 18, 22]),
    "cr_shadow_eye": ("shadow", None, "keep_distance", [12, 22, 28, 12, 16, 10]),
    "cr_solmane_lion": ("fire", "light", "charge", [20, 16, 16, 24, 12, 12]),
    "cr_thunder_drone": ("thunder", None, "keep_distance", [12, 24, 26, 12, 14, 12]),
    "cr_verdant_aquila": ("wood", "wind", "flank", [14, 24, 16, 22, 12, 12]),
    "cr_wind_bird": ("wind", None, "keep_distance", [12, 28, 22, 16, 10, 12]),
    "cr_wood_puppet": ("wood", None, "hold", [25, 5, 10, 20, 15, 25]),
}
ARCHETYPES = {
    "metal": "line_metal_gearclock", "wood": "line_wood_vinecrest", "water": "line_water_tidecraft",
    "fire": "line_fire_blazelion", "earth": "line_earth_gildedgolem", "ice": "line_ice_frostbeast",
    "wind": "line_wind_fluteglider", "thunder": "starter_star_line", "light": "starter_sun_line", "shadow": "starter_moon_line",
}
STAGES = ("juvenile", "adolescent", "adult", "overload")


def main():
    repo = Path.cwd()
    batch = repo / "Data/_incoming/p4-09-encyclopedia-01"
    lore = {(e["subject_id"], e.get("stage")): e for e in read(batch / "encyclopedia/entries.json") if e["kind"] == "creature"}
    templates = {}
    for path in (repo / "Data/_demo/creatures").glob("*.json"):
        for creature in read(path):
            if creature["line_id"] in ARCHETYPES.values():
                templates[(creature["line_id"], creature["stage"])] = creature
    budgets = read(repo / "Data/balance.json")["stat_budget"]["base"]
    creatures, names = [], []
    for line, (primary, secondary, ai, weights) in sorted(PROFILES.items()):
        for index, stage in enumerate(STAGES):
            template = templates[(ARCHETYPES[primary], "adult" if stage == "overload" else stage)]
            creature = copy.deepcopy(template)
            creature.update(id=f"{line}_{stage}", line_id=line, stage=stage, ai_style=ai)
            e = lore[(line, stage)]
            creature["size_class"] = ("XS", "S", "M", "L")[index]
            creature["evolves_to"] = f"{line}_{STAGES[index+1]}" if index < 2 else None
            if index < 2:
                creature["evolution"] = {"level": 25 if index == 0 else 45,
                                         "materials": [{"element": primary, "tier": index+1, "qty": 3 if index == 0 else 5}]}
            else:
                creature["evolution"] = None
            if stage == "overload":
                creature["overload_of"] = f"{line}_adult"
            creature["mastery"] = {primary: (15, 20, 25, 30)[index]}
            if secondary:
                creature["mastery"][secondary] = (5, 10, 15, 20)[index]
            budget = budgets[stage]
            values = [budget * weight // 100 for weight in weights]
            values[0] += budget - sum(values)
            creature["stats"] = dict(zip(("hp", "speed", "yin_atk", "yang_atk", "yin_def", "yang_def"), values))
            creature["concept"] = {"name_zh": e["basic"]["name_zh"], "name_en": e["basic"]["name_en"],
                                   "stage_look": e["basic"]["appearance"],
                                   "source_wiki": e["sources"][0], "status": "review_required",
                                   "gameplay_archetype": template["id"],
                                   "gameplay_note": "數值與候選棲位沿現有屬性原型草擬；技能暫共用既有招式，不代表 wiki 所有武裝效果均已實裝。"}
            creatures.append(creature)
            names.append({"id": creature["id"], "name_zh": e["basic"]["name_zh"], "name_en": e["basic"]["name_en"] or e["basic"]["name_zh"]})
    target = batch / "creatures/drive-creatures.json"
    target.parent.mkdir(exist_ok=True)
    target.write_text(json.dumps(creatures, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    (batch / "copy-terra.json").write_text(json.dumps({"creatures": names}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Designed {len(creatures)} creature forms; review required")


if __name__ == "__main__":
    main()
