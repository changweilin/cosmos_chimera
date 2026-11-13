"""Remaining creature-batch quota (P5-01).

Reads every creature line already drafted under Data/_incoming/*/creatures and
Data/creatures, then prints how many lines each element / region still needs and
which `ok` archetypes are unused, so a batch prompt can be filled with a quota row.

Run: python tools/prompts/quota.py [--target-lines 60] [--batch-size 10]

Pre: Data/ readable; creature JSON follows creature.schema.json (main element =
     highest mastery key; region/archetype come from concept.region / concept.archetype).
Post: prints a markdown report; exit 0. Never writes files.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from collections import Counter
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ELEMENTS = ["metal", "wood", "water", "fire", "earth", "ice", "wind", "thunder", "light", "shadow"]


def _load_array(path: Path) -> list[dict]:
    data = json.loads(path.read_text(encoding="utf-8"))
    return data if isinstance(data, list) else []


def load_lines(data_root: Path) -> dict[str, dict]:
    """line_id -> {element, region, archetype}, from the stage with the lowest id order seen."""
    files = sorted(data_root.glob("_incoming/*/creatures/*.json")) + sorted(data_root.glob("creatures/*.json"))
    lines: dict[str, dict] = {}
    for f in files:
        for c in _load_array(f):
            if not isinstance(c, dict) or "line_id" not in c or "mastery" not in c:
                continue
            if c["line_id"] in lines:
                continue
            concept = c.get("concept") or {}
            lines[c["line_id"]] = {
                "element": max(c["mastery"], key=c["mastery"].get),
                "region": concept.get("region"),
                "archetype": concept.get("archetype"),
            }
    return lines


def load_archetypes(data_root: Path) -> list[dict]:
    seen: dict[str, dict] = {}
    for f in sorted(data_root.glob("_incoming/p5-02-archetypes-*/archetypes-*.json")):
        for a in _load_array(f):
            if isinstance(a, dict) and "id" in a:
                seen.setdefault(a["id"], a)
    return list(seen.values())


def compute(lines: dict[str, dict], archetypes: list[dict], target_lines: int) -> dict:
    regions = sorted({a["region"] for a in archetypes if a.get("region")})
    elem_target = math.ceil(target_lines / len(ELEMENTS))
    region_target = math.ceil(target_lines / len(regions)) if regions else 0
    elem_have = Counter(line["element"] for line in lines.values())
    region_have = Counter(line["region"] for line in lines.values() if line["region"])
    used = {line["archetype"] for line in lines.values() if line["archetype"]}
    free = [a for a in archetypes if a.get("sensitivity") == "ok" and a["id"] not in used]
    free_by_region = Counter(a["region"] for a in free)
    return {
        "have": len(lines),
        "need": max(0, target_lines - len(lines)),
        "elements": {e: (elem_have[e], max(0, elem_target - elem_have[e])) for e in ELEMENTS},
        "regions": {r: (region_have[r], max(0, region_target - region_have[r]), free_by_region[r]) for r in regions},
        "free_archetypes": sorted(a["id"] for a in free),
    }


def render(report: dict, target_lines: int, batch_size: int) -> str:
    out = [f"# 配額報告：{report['have']} / {target_lines} 條線，還缺 {report['need']}", ""]
    out += ["## 屬性（主屬性＝最高專精）", "", "| 屬性 | 已有 | 還缺 |", "|---|---|---|"]
    out += [f"| {e} | {h} | {n} |" for e, (h, n) in report["elements"].items()]
    out += ["", "## 地區", "", "| 地區 | 已有 | 還缺 | 可用 ok 原型 |", "|---|---|---|---|"]
    out += [f"| {r} | {h} | {n} | {f} |" for r, (h, n, f) in report["regions"].items()]
    batch = min(batch_size, report["need"])
    out += ["", f"建議下一批 {batch} 條線：優先填「還缺」最大的屬性與地區；每條線只用一個尚未使用的 ok 原型。"]
    return "\n".join(out)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--data-root", type=Path, default=REPO / "Data")
    ap.add_argument("--target-lines", type=int, default=60)
    ap.add_argument("--batch-size", type=int, default=10)
    ap.add_argument("--list-archetypes", action="store_true", help="also print the unused ok archetype ids")
    args = ap.parse_args(argv)
    if not args.data_root.is_dir():
        print(f"error: data root not found: {args.data_root}", file=sys.stderr)
        return 2
    report = compute(load_lines(args.data_root), load_archetypes(args.data_root), args.target_lines)
    print(render(report, args.target_lines, args.batch_size))
    if args.list_archetypes:
        print("\n" + "\n".join(report["free_archetypes"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
