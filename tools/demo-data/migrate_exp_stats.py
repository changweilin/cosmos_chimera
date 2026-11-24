"""Migrate creature resist & mastery to the exponential 1000/300 share model.

Idempotent: creatures whose resist sum == 1000 with values in 25..175 and mastery sum == 300
with 3..5 non-zero values (max == 125) are left untouched unless stats need rounding alignment.

Run: python tools/demo-data/migrate_exp_stats.py
"""
from __future__ import annotations

import json
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ELEMENTS = ("metal", "wood", "water", "fire", "earth", "ice", "wind", "thunder", "light", "shadow")
STAT_KEYS = ("hp", "speed", "yin_atk", "yang_atk", "yin_def", "yang_def")

BALANCE = json.loads((REPO / "Data" / "balance.json").read_text(encoding="utf-8"))
BUDGET = BALANCE["stat_budget"]


def _load_skill_stars() -> dict[str, int]:
    stars: dict[str, int] = {}
    for root in [REPO / "Data" / "_demo", *(REPO / "Data" / "_incoming").iterdir()]:
        if not root.is_dir():
            continue
        for kind in ("moves", "traits"):
            d = root / kind
            if not d.is_dir():
                continue
            for p in sorted(d.glob("*.json")):
                data = json.loads(p.read_text(encoding="utf-8"))
                if isinstance(data, list):
                    for item in data:
                        if isinstance(item, dict) and isinstance(item.get("id"), str) and isinstance(item.get("star"), int):
                            stars[item["id"]] = item["star"]
    return stars


def migrate_resist(old_resist: dict[str, int]) -> dict[str, int]:
    vals = {el: max(25, min(175, 100 + int(old_resist.get(el, 0)))) for el in ELEMENTS}
    # Adjust deterministically to sum == 1000 while keeping every entry in 25..175.
    diff = 1000 - sum(vals.values())
    if diff > 0:
        order = sorted(ELEMENTS, key=lambda el: (vals[el], ELEMENTS.index(el)))
        i = 0
        while diff > 0:
            el = order[i % len(order)]
            if vals[el] < 175:
                vals[el] += 1
                diff -= 1
            i += 1
    elif diff < 0:
        order = sorted(ELEMENTS, key=lambda el: (-vals[el], ELEMENTS.index(el)))
        i = 0
        while diff < 0:
            el = order[i % len(order)]
            if vals[el] > 25:
                vals[el] -= 1
                diff += 1
            i += 1
    return vals


def migrate_mastery(old_mastery: dict[str, int], new_resist: dict[str, int]) -> dict[str, int]:
    ranked_old = sorted(old_mastery.keys(), key=lambda el: (-old_mastery[el], ELEMENTS.index(el)))
    # Pick 3..4 elements: keep all old mastery elements, fill up to 3 (or 4 if >=2 old) from highest resist.
    target_count = 3 if len(ranked_old) <= 1 else 4
    picked = list(ranked_old)
    candidates = sorted(
        (el for el in ELEMENTS if el not in picked),
        key=lambda el: (-new_resist[el], ELEMENTS.index(el)),
    )
    for el in candidates:
        if len(picked) >= target_count:
            break
        picked.append(el)
    weights = [125, 100, 75] if len(picked) == 3 else [125, 80, 55, 40]
    return {el: w for el, w in zip(picked, weights)}


def is_migrated_resist(r: dict[str, int]) -> bool:
    return len(r) == 10 and sum(r.values()) == 1000 and all(25 <= v <= 175 for v in r.values())


def is_migrated_mastery(m: dict[str, int]) -> bool:
    nz = [v for v in m.values() if v > 0]
    return 3 <= len(nz) <= 5 and sum(m.values()) == 300 and max(m.values()) == 125 and all(0 <= v <= 125 for v in m.values())


def migrate_file(path: Path, skill_stars: dict[str, int]) -> int:
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, list):
        return 0
    changed = 0
    for c in data:
        if not isinstance(c, dict) or "resist" not in c or "mastery" not in c or "stats" not in c:
            continue
        item_changed = False
        if not is_migrated_resist(c["resist"]):
            c["resist"] = migrate_resist(c["resist"])
            item_changed = True
        if not is_migrated_mastery(c["mastery"]):
            c["mastery"] = migrate_mastery(c["mastery"], c["resist"])
            item_changed = True

        stage = c.get("stage")
        base = BUDGET["base"].get(stage)
        s_move = skill_stars.get(c.get("signature_move"), 1)
        s_trait = skill_stars.get(c.get("trait"), 1)
        if base is not None:
            target = base * (100 - BUDGET["per_extra_star_percent"] * (s_move + s_trait - 2)) // 100
            total = sum(c["stats"][k] for k in STAT_KEYS)
            if total != target:
                c["stats"]["hp"] += target - total
                item_changed = True
        if item_changed:
            changed += 1
    if changed:
        path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return changed


def main() -> None:
    skill_stars = _load_skill_stars()
    targets = [
        REPO / "Data" / "_demo" / "creatures" / "p2-08-starters-draft-01--creatures.json",
        REPO / "Data" / "_demo" / "creatures" / "p5-03-creatures-draft-01--creatures.json",
        REPO / "Data" / "_incoming" / "p0-05-samples" / "valid" / "creatures" / "creatures.json",
        REPO / "Data" / "_incoming" / "p2-08-starters-draft-01" / "creatures" / "creatures.json",
        REPO / "Data" / "_incoming" / "p4-09-encyclopedia-01" / "creatures" / "drive-creatures.json",
        REPO / "Data" / "_incoming" / "p5-03-creatures-draft-01" / "creatures" / "creatures.json",
        REPO / "Data" / "_incoming" / "p5-03-creatures-draft-02" / "creatures" / "creatures.json",
        REPO / "Data" / "_incoming" / "p5-10-trial-bosses-draft-01" / "creatures" / "creatures.json",
    ]
    for p in targets:
        if p.exists():
            n = migrate_file(p, skill_stars)
            print(f"{p.relative_to(REPO)}: {n} creature(s) updated")


if __name__ == "__main__":
    main()
