"""D0-S1 mechanical migration: four stats -> six stats, move polarity, skill stars (Q-C74).

Why: the six-stat rework (creatures.md#六大能力值) changes the creature/move/trait schema. The user chose a
mechanical first draft applied in place (all skills ★, so every budget is the stage base), with a report in
Data/_incoming/d0-s1-six-stats/ for later human review. Idempotent: already-converted records are left as they are,
so re-running after merge.py or on new batches is safe.

Rules (Q-C74):
- polarity: move id keywords first (yang = cut/pierce/claw/burn/corrode; yin = blunt/shock/wave/electric/radiation),
  else element tendency (yang: metal fire wood ice; yin: thunder light shadow earth water wind).
- stats: hp <- con, speed <- agi, attack of the signature move's polarity <- str, the other attack <- str x 0.6,
  both defences <- (con + spi) / 2; then scaled to the stage budget with largest-remainder rounding.

Run: python tools/demo-data/six_stats.py [--report PATH] ROOT [ROOT ...]
  ROOT is a data root holding creatures/ moves/ traits/ (e.g. Data/_demo, Data/_incoming/<batch>).
Pre: run from the repo root; JSON files are arrays. Post: only the converted fields change; edits are textual so each
file keeps its own formatting (several drafts are not canonical 2-space JSON). Side effects: writes data files and
the report.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SIX = ["hp", "speed", "yin_atk", "yang_atk", "yin_def", "yang_def"]
OLD = ["con", "str", "spi", "agi"]
STAGES = ("juvenile", "adolescent", "adult", "overload")

YANG_ELEMENTS = {"metal", "fire", "wood", "ice"}
YANG_WORDS = {
    "claw", "fang", "bite", "slash", "cleave", "blade", "slicer", "cutter", "razor", "shear", "edge", "needle",
    "thorn", "pierce", "piercing", "lance", "spear", "javelin", "jab", "thrust", "dart", "spike", "prick", "drill",
    "flechette", "shrapnel", "shot", "slug", "ballista", "sting", "swipe", "rake", "rip", "whip", "lash", "bramble",
    "briar", "caltrop", "splinter", "shard", "icicle", "cinder", "ember", "blaze", "scorch", "inferno", "pyre",
    "searing", "corrosion", "nightpaw", "antler", "talon",
}
YIN_WORDS = {
    "charge", "rush", "ram", "slam", "crush", "strike", "stomp", "quake", "tremor", "seismic", "impact", "crash",
    "bash", "pulse", "wave", "shock", "bolt", "spark", "volt", "voltage", "discharge", "beam", "ray", "nova",
    "burst", "detonation", "clap", "plasma", "photon", "radiant", "boulder", "anvil", "collapse", "curse",
    "nightmare", "megavolt", "static", "stampede", "tectonic", "pounce",
}
STATS_OBJECT = re.compile(r'"stats"\s*:\s*\{[^{}]*\}')


def polarity_of(move: dict) -> tuple[str, str]:
    """Returns (polarity, reason)."""
    words = move["id"].split("_")[1:]
    yang = [w for w in words if w in YANG_WORDS]
    yin = [w for w in words if w in YIN_WORDS]
    if yang and not yin:
        return "yang", f"keyword {yang[0]}"
    if yin and not yang:
        return "yin", f"keyword {yin[0]}"
    tendency = "yang" if move["element"] in YANG_ELEMENTS else "yin"
    why = f"element {move['element']}" + (f" (keywords tie: {yang[0]}/{yin[0]})" if yang and yin else "")
    return tendency, why


def scale_to_budget(raw: dict[str, float], budget: int) -> dict[str, int]:
    total = sum(raw.values())
    exact = {k: raw[k] * budget / total for k in SIX}
    out = {k: int(exact[k]) for k in SIX}
    rest = budget - sum(out.values())
    # Largest remainder, ties in SIX order: deterministic so reruns and reviewers see the same numbers.
    for k in sorted(SIX, key=lambda k: (-(exact[k] - out[k]), SIX.index(k)))[:rest]:
        out[k] += 1
    return out


def six_stats_for(c: dict, moves: dict[str, dict], budget: int) -> tuple[dict[str, int], str]:
    stats = c["stats"]
    sig = moves.get(c.get("signature_move"))
    if sig is not None and sig.get("kind") != "effect":
        main = sig.get("polarity") or polarity_of(sig)[0]
    else:
        mastery = c.get("mastery") or {"earth": 0}
        top = max(mastery, key=mastery.get)
        main = "yang" if top in YANG_ELEMENTS else "yin"
    con, st, spi, agi = (stats[k] for k in OLD)
    other = "yin" if main == "yang" else "yang"
    raw = {"hp": con, "speed": agi, f"{main}_atk": st, f"{other}_atk": st * 0.6,
           "yin_def": (con + spi) / 2, "yang_def": (con + spi) / 2}
    if sum(raw.values()) <= 0:
        raw = {k: 1 for k in SIX}
    return scale_to_budget(raw, budget), main


def insert_after_id(text: str, rid: str, fields: list[tuple[str, object]]) -> str:
    # Inserted right after the record's own "id" member, reusing the whitespace after its comma so multi-line and
    # single-line records both keep their style.
    pat = re.compile(r'("id"\s*:\s*' + re.escape(json.dumps(rid, ensure_ascii=False)) + r'\s*,)(\s*)')
    m = pat.search(text)
    if m is None:
        raise SystemExit(f"error: cannot locate id {rid}")
    ws = m.group(2)
    extra = "".join(f"{json.dumps(k)}: {json.dumps(v, ensure_ascii=False)},{ws}" for k, v in fields)
    return text[:m.end()] + extra + text[m.end():]


def replace_stats(text: str, rid: str, stats: dict[str, int]) -> str:
    m_id = re.compile(r'"id"\s*:\s*' + re.escape(json.dumps(rid, ensure_ascii=False))).search(text)
    if m_id is None:
        raise SystemExit(f"error: cannot locate id {rid}")
    m = STATS_OBJECT.search(text, m_id.end())
    if m is None:
        raise SystemExit(f"error: no stats object after {rid}")
    body = "{" + ", ".join(f'"{k}": {stats[k]}' for k in SIX) + "}"
    return text[:m.start()] + '"stats": ' + body + text[m.end():]


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def rewrite(path: Path, edit) -> None:
    text = path.read_text(encoding="utf-8")
    new = edit(text)
    if new != text:
        path.write_text(new, encoding="utf-8", newline="")


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("roots", nargs="+", type=Path)
    ap.add_argument("--report", type=Path, default=None)
    args = ap.parse_args(argv)
    balance = load(REPO / "Data" / "balance.json")
    budgets = {s: balance["stat_budget"]["base"][s] for s in STAGES}

    move_log: list[str] = []
    moves: dict[str, dict] = {}
    for p in [p for r in args.roots for p in sorted((r / "moves").glob("*.json"))]:
        doc = load(p)
        if not isinstance(doc, list):
            continue
        edits: list[tuple[str, list]] = []
        for m in doc:
            if not (isinstance(m, dict) and isinstance(m.get("id"), str) and isinstance(m.get("element"), str)):
                continue
            fields: list[tuple[str, object]] = []
            if "star" not in m:
                fields.append(("star", 1))
            if m.get("kind") != "effect" and "polarity" not in m:
                pol, why = polarity_of(m)
                fields.append(("polarity", pol))
                move_log.append(f"| `{m['id']}` | {m['element']} | {pol} | {why} |")
            m.update(dict(fields))
            if fields:
                edits.append((m["id"], fields))
            moves.setdefault(m["id"], m)
        rewrite(p, lambda text, edits=edits: _apply_inserts(text, edits))

    for p in [p for r in args.roots for p in sorted((r / "traits").glob("*.json"))]:
        doc = load(p)
        if not isinstance(doc, list):
            continue
        edits = [(t["id"], [("star", 1)]) for t in doc
                 if isinstance(t, dict) and isinstance(t.get("id"), str) and "star" not in t]
        rewrite(p, lambda text, edits=edits: _apply_inserts(text, edits))

    creature_log: list[str] = []
    for p in [p for r in args.roots for p in sorted((r / "creatures").glob("*.json"))]:
        doc = load(p)
        if not isinstance(doc, list):
            continue
        changed: list[tuple[str, dict[str, int]]] = []
        for c in doc:
            stats = c.get("stats") if isinstance(c, dict) else None
            if not (isinstance(stats, dict) and all(isinstance(stats.get(k), int) for k in OLD)
                    and c.get("stage") in budgets and isinstance(c.get("id"), str)):
                continue
            new, main = six_stats_for(c, moves, budgets[c["stage"]])
            changed.append((c["id"], new))
            creature_log.append(f"| `{c['id']}` | {c['stage']} | " + "/".join(str(stats[k]) for k in OLD)
                                + f" | {main} | " + "/".join(str(new[k]) for k in SIX) + " |")

        def apply_creatures(text, changed=changed):
            for rid, new in changed:
                text = replace_stats(text, rid, new)
            return text
        rewrite(p, apply_creatures)

    if args.report is not None:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        lines = [
            "# D0-S1 六能力值機械轉換報告（Q-C74，待人工審核）",
            "",
            "由 `tools/demo-data/six_stats.py` 產生；規則見該檔 docstring。全部技能先設 ★，預算＝階段基準。",
            "",
            "## 能力值",
            "",
            "| id | 階段 | 舊 CON/STR/SPI/AGI | 主陰陽 | HP/速度/陰攻/陽攻/陰守/陽守 |",
            "|---|---|---|---|---|",
            *sorted(set(creature_log)),
            "",
            "## 招式陰陽",
            "",
            "| id | 屬性 | 陰陽 | 依據 |",
            "|---|---|---|---|",
            *sorted(set(move_log)),
            "",
        ]
        args.report.write_text("\n".join(lines), encoding="utf-8", newline="\n")
    print(f"converted {len(set(creature_log))} creatures, tagged {len(set(move_log))} moves")
    return 0


def _apply_inserts(text: str, edits: list[tuple[str, list]]) -> str:
    for rid, fields in edits:
        text = insert_after_id(text, rid, fields)
    return text


if __name__ == "__main__":
    sys.exit(main())
