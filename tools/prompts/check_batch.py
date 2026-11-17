"""Numeric/semantic gate for a creature batch (P5-01), run before validate_data.

validate_data checks schema, cross-file refs and the exact star-adjusted stat budget; it does not know the
per-stage resist/mastery pattern the batch prompt demands. This closes that gap so a
TERRA/HYDROGEN draft fails fast with a pointer instead of reaching review.

Run: python tools/prompts/check_batch.py Data/_incoming/<batch>/creatures/creatures.json

Pre: file is a JSON array of creatures. Post: prints one line per violation; exit 1 if any.
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
STAGES = ["juvenile", "adolescent", "adult"]
# stage -> (own resist, weakness, main mastery, evolution level band or None)
PATTERN = {
    "juvenile": (20, -20, 15, (20, 30)),
    "adolescent": (35, -30, 25, (35, 50)),
    "adult": (50, -40, 40, None),
}
STAT_KEYS = ["hp", "speed", "yin_atk", "yang_atk", "yin_def", "yang_def"]
# REQ: creatures.md#綜合值預算 — two ★★★ skills cost 4 extra stars. The creature file alone does not know its skills
# stars, so this gate only checks the feasible range; validate_data checks the exact budget against moves/traits.
MAX_EXTRA_STARS = 4
SECONDARY_MASTERY = 15


def check(creatures: list[dict], bands: dict) -> list[str]:
    errs: list[str] = []
    ids = [c.get("id") for c in creatures]
    for i in {x for x in ids if ids.count(x) > 1}:
        errs.append(f"{i}: duplicate id")
    by_line: dict[str, dict[str, dict]] = defaultdict(dict)
    for c in creatures:
        by_line[c["line_id"]][c["stage"]] = c

    for c in creatures:
        cid, stage = c["id"], c["stage"]
        own, weak, main, lvl = PATTERN[stage]
        base = bands["base"][stage]
        tol = bands["tolerance_percent"]
        low = base * (100 - bands["per_extra_star_percent"] * MAX_EXTRA_STARS) * (100 - tol) // 10000
        high = base * (100 + tol) // 100
        stats = [c["stats"][k] for k in STAT_KEYS]
        if not low <= sum(stats) <= high:
            errs.append(f"{cid}: stats total {sum(stats)} outside {low}..{high}")
        if any(s < 1 for s in stats):
            errs.append(f"{cid}: stat outside 1..")
        # REQ: creature-ecology.md#生成順序 — habits are drawn per individual from niches; coverage is validate-data's job.
        if not c.get("niches"):
            errs.append(f"{cid}: needs at least one candidate niche")
        mastery = c["mastery"]
        element = max(mastery, key=mastery.get)
        if mastery[element] != main:
            errs.append(f"{cid}: main mastery {mastery[element]} != {main}")
        for k, v in mastery.items():
            if k != element and (stage != "adult" or v != SECONDARY_MASTERY):
                errs.append(f"{cid}: secondary mastery {k}:{v} only allowed as {SECONDARY_MASTERY} on adults")
        resist = c["resist"]
        if resist.get(element) != own:
            errs.append(f"{cid}: own-element resist {resist.get(element)} != {own}")
        weaknesses = {k: v for k, v in resist.items() if v < 0}
        if len(weaknesses) != 1 or list(weaknesses.values()) != [weak]:
            errs.append(f"{cid}: need exactly one weakness of {weak}, got {weaknesses}")
        if lvl and not lvl[0] <= c["evolution"]["level"] <= lvl[1]:
            errs.append(f"{cid}: evolution level {c['evolution']['level']} outside {lvl}")

    for line, st in by_line.items():
        if set(st) != set(STAGES):
            errs.append(f"{line}: needs all three stages, has {sorted(st)}")
            continue
        for a, b in zip(STAGES, STAGES[1:]):
            if st[a]["evolves_to"] != st[b]["id"]:
                errs.append(f"{st[a]['id']}: evolves_to {st[a]['evolves_to']} != {st[b]['id']}")
            if any(st[b]["stats"][k] < st[a]["stats"][k] for k in STAT_KEYS):
                errs.append(f"{st[b]['id']}: a stat is lower than {st[a]['id']}")
        weak = {tuple(k for k, v in s["resist"].items() if v < 0) for s in st.values()}
        if len(weak) != 1:
            errs.append(f"{line}: weakness element changes between stages")
    return errs


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("file", type=Path)
    ap.add_argument("--balance", type=Path, default=REPO / "Data" / "balance.json")
    args = ap.parse_args(argv)
    for p in (args.file, args.balance):
        if not p.is_file():
            print(f"error: not found: {p}", file=sys.stderr)
            return 2
    bands = json.loads(args.balance.read_text(encoding="utf-8"))["stat_budget"]
    errs = check(json.loads(args.file.read_text(encoding="utf-8")), bands)
    for e in errs:
        print(e)
    print(f"{len(errs)} violation(s)")
    return 1 if errs else 0


if __name__ == "__main__":
    sys.exit(main())
