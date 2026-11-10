"""Stage the draft batches the D0 demo plays with into one importable data root.

Why: drafts stay in Data/_incoming until human finalization (data-content.md), but the demo needs them loaded.
The importer takes -dataRoot, so we stage copies instead of promoting drafts into Data/.
Pre: run from the repo root with complete, valid inputs.
Post: staged demo, copy and geo files reflect their sources.
Side effects: updates Data/_demo/, runtime copy and geo outputs after validation.
"""
import json
import shutil
import sys
from pathlib import Path

BATCHES = [
    "p2-08-starters-draft-01", "p5-03-creatures-draft-01", "p3-planet-types-draft-01",
    "p5-05-moves-draft-01", "p5-05-moves-draft-02", "p5-05-moves-draft-03", "p5-05-moves-draft-04",
    "p5-05-moves-draft-05", "p5-05-moves-draft-06",
    "p5-05-traits-draft-01", "p5-05-traits-draft-02", "p5-05-traits-draft-03",
    "p5-06-relations-draft-01",
]
KINDS = ["creatures", "moves", "traits", "relations", "planet-types"]
# Runtime copy for the demo UI. Unity cannot read Data/ in a build, so the text is staged as TextAssets.
COPY_OUT = Path("Assets/_Project/Data/Copy")
D0_COPY = ["affixes", "region-themes", "quest-templates", "boss-epithets", "strings"]
NAME_SOURCES = ["p2-08-starters-draft-01", "p5-03-creatures-draft-01"]


GEO_OUT = Path("Assets/_Project/Data/GeoTemplates")


def _read_json(path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"{path}: duplicate key: {key}")
            result[key] = value
        return result

    data = path.read_bytes()
    return data, json.loads(data.decode("utf-8"), object_pairs_hook=unique)


def _prepare_geo(root):
    files = {}
    count = 0
    for d in sorted((root / "geo-templates").iterdir()):
        if not (d / "meta.json").is_file():
            continue
        files[f"{d.name}.meta.json"], _ = _read_json(d / "meta.json")
        files[f"{d.name}.height.bytes"] = (d / "height.r16").read_bytes()
        files[f"{d.name}.water.bytes"] = (d / "water.u8").read_bytes()
        count += 1
    return files, count


def _write_geo(files):
    GEO_OUT.mkdir(parents=True, exist_ok=True)
    for path in GEO_OUT.iterdir():
        if path.is_file() and path.name.endswith((".meta.json", ".height.bytes", ".water.bytes")) and path.name not in files:
            path.unlink()
            meta = path.with_name(path.name + ".meta")
            if meta.is_file():
                meta.unlink()
    for name, data in files.items():
        path = GEO_OUT / name
        if not path.is_file() or path.read_bytes() != data:
            path.write_bytes(data)


def stage_geo(root: Path) -> int:
    """Stage templates as .bytes/.json TextAssets while preserving Unity asset identities.

    Why the renamed extensions: Unity loads only known extensions as TextAsset.
    Why no rmtree: Demo.unity references these assets by the GUIDs in their .meta files (Sol D0-r2 S1).

    Pre: root contains complete templates with valid JSON metadata.
    Post: generated files match sources; retired files and their metadata are removed.
    Side effects: reads templates and updates GEO_OUT.
    """
    files, count = _prepare_geo(root)
    _write_geo(files)
    return count


def _merge_ja(base, translated, path):
    """Merge translation leaves while checking identity and rejecting collisions."""
    if isinstance(base, dict) and isinstance(translated, dict):
        for key, value in translated.items():
            if key == "ja":
                if key in base:
                    raise ValueError(f"{path}: duplicate translation key: ja")
                base[key] = value
            elif key in ("en", "zh-TW", "zh"):
                raise ValueError(f"{path}: duplicate translation key: {key}")
            elif key not in base:
                raise ValueError(f"{path}: unknown key: {key}")
            else:
                _merge_ja(base[key], value, f"{path}.{key}")
    elif isinstance(base, list) and isinstance(translated, list):
        if len(base) != len(translated):
            raise ValueError(f"{path}: list lengths differ")
        for i, (left, right) in enumerate(zip(base, translated)):
            _merge_ja(left, right, f"{path}[{i}]")
    elif base != translated:
        raise ValueError(f"{path}: identity mismatch")


def _prepare_copy(root):
    files = {}
    incoming = root / "_incoming" / "d0-copy-terra"
    for name in D0_COPY:
        data, value = _read_json(incoming / f"{name}.json")
        _, translated = _read_json(root / "_incoming" / "d0-copy-flash-ja" / f"{name}.json")
        _merge_ja(value, translated, name)
        files[f"{name}.json"] = (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
        if name == "strings":
            strings = value
    # Supplement keys are additive UI strings; a key collision would silently shadow reviewed copy, so fail loudly.
    _, supplement = _read_json(incoming / "demo-ui-supplement.json")
    clash = sorted(set(strings) & set(supplement))
    if clash:
        raise SystemExit(f"error: demo-ui-supplement.json overrides strings.json keys: {clash}")
    _, translated_supplement = _read_json(root / "_incoming" / "d0-copy-flash-ja" / "demo-ui-supplement.json")
    _merge_ja(supplement, translated_supplement, "demo-ui-supplement")
    strings.update(supplement)
    files["strings.json"] = (json.dumps(strings, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    files["balance.json"], _ = _read_json(root / "balance.json")
    names = {}
    for batch in NAME_SOURCES:
        _, copy = _read_json(root / "_incoming" / batch / "copy-terra.json")
        for section in ("creatures", "moves", "traits"):
            for entry in copy.get(section, []):
                if entry["id"] in names:
                    raise ValueError(f"names: duplicate id: {entry['id']}")
                names[entry["id"]] = {"zh": entry.get("name_zh", entry["id"]), "en": entry.get("name_en", entry["id"])}
    path = root / "_incoming" / "d0-names-flash-ja" / "names-ja.json"
    _, translated = _read_json(path)
    ja_names = {}
    for entry in translated["entries"]:
        id = entry["id"]
        if id in ja_names:
            raise ValueError(f"{path}: duplicate id: {id}")
        ja_names[id] = entry
    if set(names) != set(ja_names):
        raise ValueError(f"{path}: id sets differ: missing={sorted(set(names) - set(ja_names))}, extra={sorted(set(ja_names) - set(names))}")
    for id, name in names.items():
        translated_name = ja_names[id]
        for lang in ("zh", "en"):
            if translated_name.get(lang) != name[lang]:
                raise ValueError(f"{path}: {id}: {lang} identity mismatch")
        ja = translated_name.get("ja")
        if not isinstance(ja, str) or not ja.strip():
            raise ValueError(f"{path}: {id}: missing or empty ja")
        name["ja"] = ja
    entries = [{"id": k, **v} for k, v in sorted(names.items())]
    files["names.json"] = (json.dumps({"entries": entries}, ensure_ascii=False, indent=1) + "\n").encode("utf-8")
    return files


def _write_copy(files):
    COPY_OUT.mkdir(parents=True, exist_ok=True)
    for name, data in files.items():
        (COPY_OUT / name).write_bytes(data)


def stage_copy(root: Path) -> None:
    """Stage validated runtime copy.

    Pre: root contains valid inputs with no overlapping string keys.
    Post: COPY_OUT contains merged copy and names.
    Side effects: reads inputs and writes COPY_OUT after validation.
    """
    _write_copy(_prepare_copy(root))


def main() -> int:
    """Stage demo inputs after validating them.

    Pre: run from the repo root with complete, valid inputs.
    Post: demo, runtime copy and geo outputs reflect the inputs.
    Side effects: reads inputs and updates the three output directories.
    """
    root = Path("Data")
    out = root / "_demo"
    batches = {}
    for batch in BATCHES:
        src = root / "_incoming" / batch
        if not src.is_dir():
            print(f"error: missing batch {src}", file=sys.stderr)
            return 1
        for kind in KINDS:
            for f in sorted((src / kind).glob("*.json")) if (src / kind).is_dir() else []:
                data, _ = _read_json(f)
                batches[Path(kind) / f"{batch}--{f.name}"] = data
    copy = _prepare_copy(root)
    geo, count = _prepare_geo(root)
    if out.exists():
        shutil.rmtree(out)
    for name, data in batches.items():
        dst = out / name
        dst.parent.mkdir(parents=True, exist_ok=True)
        dst.write_bytes(data)
    _write_copy(copy)
    _write_geo(geo)
    print(f"staged {count} geo templates into {GEO_OUT}")
    print(f"staged {len(batches)} files into {out}; copy into {COPY_OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
