"""Regenerate designed.js (prompt-forge「已設計項目」) from Data/ batches.

Data JSON stays the single source of truth (data-content.md); designed.js is a generated view so the
browser tool works offline without fetch(). Re-run after editing a batch:
    python tools/prompt-forge/gen_designed.py
Stdlib only. Exit 1 if a referenced id is missing.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
OUT = Path(__file__).with_name("designed.js")

# Visual-only fields the data schema does not carry (concept pipeline step 6). Keyed by line_id.
# REQ: docs/design/creatures.md#御三家「日月星」（Q-C6 定案）; batch README for the design rationale.
LINES = {
    "starter_sun_line": {
        "batch": "Data/_incoming/p2-08-starters-draft-01",
        "code_zh": "日",
        "element": "light", "elements": ["light（光）", "fire（火，成年起）"],
        "base_parents": ["生物·鳥類", "器械·天文/測量儀器"], "bases": ["鷺鳥", "太陽鏡陣飛行器"],
        "timeline": "近代科幻想像（20 世紀初）", "temperament": "沉穩專注、喜歡曬太陽",
        "adult_dims": {"height_length": "2.2 m", "weight": "95 kg", "body_fat": "12%"},
    },
    "starter_moon_line": {
        "batch": "Data/_incoming/p2-08-starters-draft-01",
        "code_zh": "月",
        "element": "shadow", "elements": ["shadow（影）", "ice（冰，成年起）"],
        "base_parents": ["生物·哺乳類"], "bases": ["灰狼"],
        "timeline": "遠古神話（極地口傳）", "temperament": "孤僻警覺、只親近主人",
        "adult_dims": {"height_length": "2.6 m", "weight": "420 kg", "body_fat": "18%"},
    },
    "starter_star_line": {
        "batch": "Data/_incoming/p2-08-starters-draft-01",
        "code_zh": "星",
        "element": "thunder", "elements": ["thunder（雷）", "wind（風，少年起）"],
        "base_parents": ["生物·哺乳類", "生物·鳥類"], "bases": ["駿馬", "金色雙翼"],
        "timeline": "遠古神話（草原史詩）", "temperament": "熱血莽撞、愛衝在最前面",
        "adult_dims": {"height_length": "2.7 m", "weight": "540 kg", "body_fat": "14%"},
    },
}

# 御三家超載型態（Overload）定案設定
OVERLOADS = {
    "starter_sun_line": {
        "stage": "overload",
        "creature_id": "sun_coronavault_skyheron",
        "name_zh": "鏡冕天鷺",
        "name_en": "Mirracorona",
        "size_class": "XL",
        "look": "太陽鏡陣飛行器與天鷺完全共鳴；背部環形鏡陣激發為三層旋轉日冕光環，光羽噴射耀眼金炎，以太管道引導星雲光子流",
        "dex_zh": "當以太能量在鏡陣中超載激盪，背部的聚光環便會如初升之日般耀眼綻放。合攏所有鏡面射出的神聖日冕光柱，能在一瞬間照亮整顆星球的夜空。",
        "signature_move": {
            "move_id": "light_coronavault_burst",
            "name_zh": "日冕貫天神光",
            "element": "light",
            "range_class": "long",
            "shape": "line",
            "movement": "none",
            "visual": "全鏡面合攏聚光鎖定，射出貫穿天際的金色日冕光柱，伴隨刺目光波震盪"
        }
    },
    "starter_moon_line": {
        "stage": "overload",
        "creature_id": "moon_eclipsvast_wolf",
        "name_zh": "夜痕蝕月狼",
        "name_en": "Niverclis",
        "size_class": "XL",
        "look": "極地霜原巨狼超載爆發；極夜暗影與萬年玄冰包覆全身，背脊升起星圖冰棘，雙瞳燃燒極光幽火，踩踏之處地面化為幽冥暗影冰鏡",
        "dex_zh": "在極寒與暗影達到頂點的瞬間，牠的身軀化作吞噬光芒的蝕月巨狼。踏冰而過時留下的夜痕凍結空間，揮動的霜影重爪足以撕裂冰河裂谷。",
        "signature_move": {
            "move_id": "shadow_eclipsvast_claw",
            "name_zh": "極夜蝕月裂空爪",
            "element": "shadow",
            "range_class": "melee",
            "shape": "sweep",
            "movement": "forward",
            "visual": "撕開暗影裂隙暴風突進，巨爪帶起幽藍極光與霜凍暗影大範圍橫掃"
        }
    },
    "starter_star_line": {
        "stage": "overload",
        "creature_id": "star_tempest_celestial",
        "name_zh": "星蹄雷皇驥",
        "name_en": "Voltrastorm",
        "size_class": "XL",
        "look": "天馬雷霆超載；金色雙翼展開為暴風雷雲之翼，周身奔湧萬鈞高壓電弧，鬃毛如雷漿流淌，馬蹄踏空激起轟鳴雷爆",
        "dex_zh": "體內的雷霆核心完全解放，四蹄踏擊雷雲如金鼓擂動。牠能裹挾萬重狂風與熾盛天雷在天際翱翔，如同一顆劃破長夜的金色流星。",
        "signature_move": {
            "move_id": "thunder_tempest_stride",
            "name_zh": "萬鈞星雷破空撞",
            "element": "thunder",
            "range_class": "melee",
            "shape": "burst",
            "movement": "forward",
            "visual": "雙翼聚斂百萬伏特雷暴，以超音速突進引發全場範圍的金色雷暴轟鳴"
        }
    }
}


def load(batch: Path, kind: str) -> dict[str, dict]:
    out: dict[str, dict] = {}
    for p in sorted((batch / kind).glob("*.json")):
        for item in json.loads(p.read_text(encoding="utf-8")):
            out[item["id"]] = item
    return out


def main() -> int:
    items = []
    for line_id, extra in LINES.items():
        batch = REPO / extra["batch"]
        creatures, moves, traits = load(batch, "creatures"), load(batch, "moves"), load(batch, "traits")
        copy = json.loads((batch / "copy-terra.json").read_text(encoding="utf-8"))
        copy_by_id = {x["id"]: x for k in ("creatures", "moves", "traits") for x in copy[k]}
        stages = sorted((c for c in creatures.values() if c["line_id"] == line_id),
                        key=lambda c: ["juvenile", "adolescent", "adult"].index(c["stage"]))
        if len(stages) != 3:
            print(f"error: {line_id} has {len(stages)} stages in {batch}", file=sys.stderr)
            return 1
        adult = stages[-1]
        concept = adult["concept"]
        trait_id = adult["trait"]
        locked_stages = []
        for c in stages:
            mv, cp, mcp = moves.get(c["signature_move"]), copy_by_id.get(c["id"], {}), copy_by_id.get(c["signature_move"], {})
            if mv is None or trait_id not in traits:
                print(f"error: {c['id']} references a missing move/trait", file=sys.stderr)
                return 1
            locked_stages.append({
                "stage": c["stage"], "creature_id": c["id"], "name_zh": cp.get("name_zh", ""), "name_en": cp.get("name_en", ""),
                "size_class": c["size_class"], "look": c["concept"]["base"] + "；" + c["concept"]["stage_look"],
                "dex_zh": cp.get("dex_zh", ""),
                "signature_move": {"move_id": mv["id"], "name_zh": mcp.get("name_zh", ""), "element": mv["element"],
                                   "range_class": mv["range_class"], "shape": mv["shape"],
                                   "movement": mv.get("movement", "none"), "visual": mcp.get("desc_zh", "")},
            })
        if line_id in OVERLOADS:
            locked_stages.append(OVERLOADS[line_id])
        tcp = copy_by_id.get(trait_id, {})
        ov = OVERLOADS.get(line_id, {})
        items.append({
            "line_id": line_id, "code_zh": extra["code_zh"], "element": extra["element"],
            "title_zh": " → ".join(s["name_zh"] for s in locked_stages),
            "source": extra["batch"],
            "concept": {
                "line_id": line_id,
                "elements": extra["elements"],
                "base_parents": extra["base_parents"], "bases": extra["bases"],
                "appearance_palette": [p.strip() for p in concept["palette"].split("、")],
                "timeline": extra["timeline"], "region": concept["region"],
                "archetype": concept["archetype"], "archetype_type": "P5-02 考據庫", "motifs": "、".join(concept["motifs_used"]),
                "sensitivity": "ok", "role": concept["role"], "temperament": extra["temperament"],
                "size_profile": "→".join(s["size_class"] for s in locked_stages),
                **extra["adult_dims"],
                "formula": " + ".join(extra["elements"]) + " × " + " + ".join(extra["bases"]) + " × " + concept["archetype"],
                "locked": {
                    "status": "已設計（Data/_incoming 草稿；id 與名稱照抄，不可改）",
                    "stages": locked_stages,
                    "trait": {"trait_id": trait_id, "name_zh": tcp.get("name_zh", ""), "idea_zh": tcp.get("desc_zh", "")},
                    "overload": ov.get("name_zh", "") + "（" + ov.get("creature_id", "") + " · XL）：" + ov.get("look", "超載爆發型態"),
                },
            },
        })

    payload = json.dumps({"version": "1.0.0", "items": items}, ensure_ascii=False, indent=2)
    OUT.write_text(
        "// GENERATED by tools/prompt-forge/gen_designed.py from Data/_incoming batches — do not edit by hand.\n"
        "window.COSMI_DESIGNED = " + payload + ";\n",
        encoding="utf-8", newline="\n")
    print(f"wrote {OUT.relative_to(REPO)} ({len(items)} lines)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
