"""Build reviewable encyclopedia JSON from exact Drive exports and existing game data.

Input caches are local exports, never OAuth tokens or credentials. Existing wiki files are read only.
Run from either repository with --wiki-cache and --media-index exported by the Drive connector.
"""
import argparse
import base64
import hashlib
import json
import re
from pathlib import Path

STAGES = ("juvenile", "adolescent", "adult", "overload")
CHARACTER_IDS = {"chr_1286": "chr_yeh_chengxi", "chr_1855": "chr_amemori_sumiha", "chr_3d04": "chr_roen"}


def read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def text(value):
    value = value.strip() if value else ""
    return None if not value or value in ("待核", "未知", "待補") else value


def field(source, label):
    match = re.search(r"^[*\-]\s*" + re.escape(label) + r"[：:]\s*(.+)$", source, re.M)
    return text(match[1]) if match else None


def sections(source):
    chunks = re.split(r"^##\s+(.+)\n", source, flags=re.M)
    return {chunks[i].strip(): chunks[i + 1].strip() for i in range(1, len(chunks), 2)}


def source_ref(source_id, path, raw, **extra):
    return {"id": source_id, "storage": "repository", "path": path,
            "sha256": hashlib.sha256(raw).hexdigest(), **extra}


def entry(entry_id, kind, subject, game_id=None):
    return {"id": entry_id, "kind": kind, "subject_id": subject, "game_id": game_id,
            "status": "review_required", "basic": dict.fromkeys(("name_zh", "name_en", "appearance", "summary")),
            "details": {}, "sources": [], "assets": dict.fromkeys(("wiki", "portrait", "model"))}


def build(repo, wiki_cache, media_index, data_root):
    game = {kind: {} for kind in ("creatures", "moves", "traits")}
    for kind in game:
        for path in sorted((data_root / kind).glob("*.json")):
            raw = path.read_bytes()
            for index, definition in enumerate(json.loads(raw)):
                if definition["id"] in game[kind]:
                    raise ValueError(f"duplicate gameplay id: {definition['id']}")
                game[kind][definition["id"]] = (definition, path.relative_to(repo).as_posix(), raw, index)
    by_form = {(d[0]["line_id"], d[0]["stage"]): d[0]["id"] for d in game["creatures"].values()}
    entries = []
    wiki_by_skill = {}
    for cached in wiki_cache:
        raw = base64.b64decode(cached["b64"], validate=True)
        source = re.sub(r"\r+\n?", "\n", raw.decode("utf-8-sig"))
        parts = sections(source)
        subject = cached["subject"]
        kind = "character" if cached["kind"] == "characters" else "creature"
        stage_match = re.search(r"^-[ \t]*stage:[ \t]*(\w+)", source, re.M)
        stage = stage_match[1] if stage_match else None
        if kind == "creature" and stage not in STAGES:
            raise ValueError(f"missing creature stage: {cached['path']}")
        game_id = CHARACTER_IDS.get(subject) if kind == "character" else by_form.get((subject, stage))
        e = entry("dex_" + subject + ("_" + stage if stage else ""), kind, subject, game_id)
        e["sources"].append({**source_ref("wiki", cached["path"], raw),
                             "storage": "drive", "drive_file_id": cached["id"]})
        e["assets"]["wiki"] = "wiki"
        related = [m for m in media_index if m["path"].startswith(f"{cached['kind']}/{subject}/")
                   and (not stage or f"_{stage}_" in m["path"])]
        for index, media in enumerate(sorted(related, key=lambda m: (m["path"], m["id"]))):
            role = "model" if media["path"].endswith(".glb") else "portrait"
            source_id = f"{role}_{index}"
            e["sources"].append({"id": source_id, "storage": "drive", "path": media["path"],
                                 "sha256": media["sha256"], "drive_file_id": media["id"]})
            # Multiple same-name GLBs have no unambiguous canonical version; retain all evidence without choosing.
            if sum(m["path"].endswith(".glb" if role == "model" else ".png") for m in related) == 1:
                e["assets"][role] = source_id
        if kind == "character":
            name = field(source, "姓名")
            name_match = re.match(r"(.+?)（(.+?)）", name or "")
            e["basic"].update(name_zh=name_match[1] if name_match else name,
                              name_en=name_match[2] if name_match else None,
                              appearance=field(source, "剪影關鍵詞"), summary=field(source, "角色定位"))
            preferences = field(source, "興趣") or ""
            likes = re.search(r"喜好：(.+?)；厭惡：(.+)$", preferences)
            biography = field(source, "背景故事")
            occupation = "調律技師／宙獸探索者" if subject == "chr_1855" else "修補學徒" if subject == "chr_1286" else None
            e["details"] = {"occupation": occupation, "biography": biography, "race": None,
                            "physique": field(source, "體型比例"),
                            "likes": [v.strip() for v in likes[1].split(",")] if likes else None,
                            "dislikes": [v.strip() for v in likes[2].split(",")] if likes else None}
        else:
            e["stage"] = stage
            form = parts.get("本型態專屬設定", "").split("### 模型規格紀錄")[0]
            name = field(form, "型態名稱")
            name_match = re.match(r"(.+?)（(.+?)）", name or "")
            fallback = re.search(r"^\d[.、].+?[：:]【(.+?)】", form, re.M)
            if not name and fallback:
                name = fallback[1]
            if not name:
                bracket = re.search(r"【([^】]+)】", form)
                colon = re.search(r"^\d[.、].+?[：:]\s*([^\n]+)", form, re.M)
                stage_name = re.search(stage + r"\s*[—–-]\s*([^）)]+)", form, re.I)
                row = next((line for line in parts.get("跨階演化路徑", "").splitlines()
                            if "|" in line and re.search(r"\b" + stage + r"\b", line, re.I)), "")
                row_name = re.search(r"【([^】]+)】", row)
                if bracket: name = bracket[1]
                elif colon: name = re.split(r"\s*[(（]", colon[1])[0].strip()
                elif stage_name: name = stage_name[1].strip()
                elif row_name: name = row_name[1]
            appearance = field(form, "外觀描述")
            if appearance is None:
                appearance_match = re.search(r"\* 外觀形態[：:]\s*\n(.+?)(?=\n\* |\Z)", form, re.S)
                appearance = text(appearance_match[1]) if appearance_match else None
            if appearance is None:
                appearance = field(form, "外觀構造")
            if appearance is None and not form.startswith("待依"):
                appearance = text(re.sub(r"^.+\n", "", form, count=1))
            if appearance is None:
                stage_row = next((line for line in parts.get("跨階演化路徑", "").splitlines()
                                  if "|" in line and re.search(r"\b" + stage + r"\b", line, re.I)), "")
                if stage_row:
                    appearance = text(stage_row.strip().strip("|").split("|")[-1])
            e["basic"].update(name_zh=name_match[1] if name_match else name,
                              name_en=name_match[2] if name_match else None,
                              appearance=appearance, summary=field(source, "宙獸分類"))
            birth = text(parts.get("出生與親子生態"))
            habit = field(form, "專屬動態特徵")
            e["details"] = {"ecology": birth, "habits": habit,
                            "habitat": field(source, "棲息地帶"),
                            "reproduction": birth if stage == "juvenile" else None,
                            "growth": text(parts.get("跨階演化路徑")) if stage != "juvenile" else None}
            move = field(form, "專屬主動技")
            move_id = re.search(r"`([a-z0-9_]+)`", move or "")
            if move_id:
                wiki_by_skill.setdefault(move_id[1], (move, e["sources"][0]))
        entries.append(e)

    names_path = repo / "Assets/_Project/Data/Copy/names.json"
    names = {n["id"]: n for n in read(names_path)["entries"]} if names_path.exists() else {}
    strings_path = repo / "Assets/_Project/Data/Copy/strings.json"
    strings = read(strings_path) if strings_path.exists() else {}

    def label(key):
        prefix, value = key.split(".", 1)
        value = "".join(part[:1].upper() + part[1:] for part in value.split("_"))
        return strings.get(prefix + "." + value, {}).get("zh-TW", value)

    mapped_creatures = {e["game_id"] for e in entries if e["kind"] == "creature"}
    for game_id, (definition, path, raw, index) in sorted(game["creatures"].items()):
        if game_id in mapped_creatures:
            continue
        e = entry("dex_" + game_id, "creature", definition["line_id"], game_id)
        e["stage"] = definition["stage"]
        concept = definition.get("concept", {})
        n = names.get(game_id, {})
        e["basic"].update(name_zh=n.get("zh"), name_en=n.get("en"),
                          appearance=concept.get("stage_look"), summary=concept.get("role"))
        e["details"] = dict.fromkeys(("ecology", "habits", "habitat", "reproduction", "growth"))
        e["sources"] = [source_ref("gameplay", path, raw),
                         source_ref("names", names_path.relative_to(repo).as_posix(), names_path.read_bytes())]
        entries.append(e)

    for kind in ("moves", "traits"):
        for game_id, (definition, path, raw, index) in sorted(game[kind].items()):
            e = entry("dex_" + game_id, "skill", game_id, game_id)
            e["skill_type"] = "move" if kind == "moves" else "trait"
            e["sources"] = [source_ref("gameplay", path, raw)]
            n = names.get(game_id, {})
            e["basic"].update(name_zh=n.get("zh"), name_en=n.get("en"))
            if not e["basic"]["name_en"]:
                e["basic"]["name_en"] = game_id.removeprefix("trait_").replace("_", " ").title()
            if n:
                e["sources"].append(source_ref("names", names_path.relative_to(repo).as_posix(), names_path.read_bytes()))
            e["sources"].append(source_ref("labels", strings_path.relative_to(repo).as_posix(), strings_path.read_bytes()))
            if game_id in wiki_by_skill:
                move, wiki = wiki_by_skill[game_id]
                e["sources"].append(wiki)
                e["assets"]["wiki"] = "wiki"
                e["basic"]["appearance"] = move.split("—", 1)[-1].strip()
            if kind == "moves":
                description = label("shape." + definition["shape"])
                effect = definition.get("effect")
                effects = (["造成傷害"] if definition["kind"] in ("damage", "both") else [])
                if effect:
                    effects.append(label("effect." + effect["type"]))
            else:
                description = label("trigger." + definition["trigger"]["type"])
                effects = [label("effect." + effect["type"]) for effect in definition["effects"]]
            e["basic"]["summary"] = description
            e["basic"]["appearance"] = e["basic"]["appearance"] or description
            refs = []

            def numbers(value, pointer):
                if isinstance(value, dict):
                    for key, child in value.items():
                        numbers(child, pointer + "/" + key.replace("~", "~0").replace("/", "~1"))
                elif isinstance(value, list):
                    for child_index, child in enumerate(value):
                        numbers(child, pointer + f"/{child_index}")
                elif isinstance(value, int) and not isinstance(value, bool):
                    key = pointer.rsplit("/", 1)[-1]
                    unit = "ms" if key.endswith("_ms") else "mm" if key.endswith("_mm") else "percent" if key in ("accuracy", "delta", "percent") else key
                    refs.append({"source": "gameplay", "pointer": pointer, "unit": unit})

            numbers(definition, f"/{index}")
            if kind == "traits" and all(effect["type"] == "heal_percent_max_hp" for effect in definition["effects"]):
                refs = [ref for ref in refs if ref["pointer"] != f"/{index}/trigger/params/duration_ms"]
            e["details"] = {"description": e["basic"]["appearance"], "effects": "；".join(effects) or None,
                            "field_effects": None, "value_refs": refs}
            entries.append(e)

    utility_schema = read(repo / "Data/schemas/common.schema.json")["$defs"]["fieldUtility"]["enum"]
    for utility in utility_schema:
        if utility == "none":
            continue
        e = entry("dex_utility_" + utility, "skill", utility, utility)
        e["skill_type"] = "field_utility"
        pascal = "".join(part[:1].upper() + part[1:] for part in utility.split("_"))
        e["basic"].update(name_zh=strings.get("utility." + pascal, {}).get("zh-TW"),
                          name_en=strings.get("utility." + pascal, {}).get("en"),
                          appearance=strings.get("utility.desc." + pascal, {}).get("zh-TW"),
                          summary=strings.get("utility.desc." + pascal, {}).get("zh-TW"))
        e["details"] = {"description": e["basic"]["appearance"], "effects": None,
                        "field_effects": e["basic"]["summary"], "value_refs": []}
        e["sources"] = [source_ref("labels", strings_path.relative_to(repo).as_posix(), strings_path.read_bytes())]
        if utility == "weather_control":
            balance_path = repo / "Data/balance.json"
            e["sources"].append(source_ref("balance", "Data/balance.json", balance_path.read_bytes()))
            e["details"]["value_refs"].append({"source": "balance", "pointer": "/field_utilities/weather_control_cooldown_ms", "unit": "ms"})
        entries.append(e)
    return sorted(entries, key=lambda e: e["id"])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--wiki-cache", type=Path, required=True)
    parser.add_argument("--media-index", type=Path, required=True)
    parser.add_argument("--data-root", type=Path, default=Path("Data/_demo"))
    parser.add_argument("--authored", type=Path, help="Explicitly authorized review-required design overlay")
    parser.add_argument("--output", type=Path, default=Path("Data/_incoming/p4-09-encyclopedia-01/encyclopedia/entries.json"))
    args = parser.parse_args()
    repo = Path.cwd().resolve()
    result = build(repo, read(args.wiki_cache), read(args.media_index), args.data_root.resolve())
    if args.authored:
        overlay = read(args.authored)
        by_id = {entry["id"]: entry for entry in result}
        raw = args.authored.read_bytes()
        for proposal in overlay:
            entry = by_id[proposal["id"]]
            authored = []
            for group in ("basic", "details"):
                for key, value in proposal[group].items():
                    if key != "value_refs" and value is not None and value != entry[group].get(key):
                        entry[group][key] = value
                        authored.append(group + "." + key)
            if authored:
                entry["authored_fields"] = authored
                entry["sources"].append(source_ref("new_design", args.authored.resolve().relative_to(repo).as_posix(), raw))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Built {len(result)} review-required entries")


if __name__ == "__main__":
    main()
