"""Stage the draft batches the D0 demo plays with into one importable data root.

Why: drafts stay in Data/_incoming until human finalization (data-content.md), but the demo needs them loaded.
The importer takes -dataRoot, so we stage copies instead of promoting drafts into Data/.
Pre: run from the repo root. Post: Data/_demo/<kind>/<batch>.json mirrors the batches below; nothing else changes.
Side effects: deletes and rewrites Data/_demo/.
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


def stage_geo(root: Path) -> int:
    """Copy baked templates as .bytes/.json TextAssets (Unity loads only known extensions as TextAsset)."""
    if GEO_OUT.exists():
        shutil.rmtree(GEO_OUT)
    GEO_OUT.mkdir(parents=True)
    count = 0
    for d in sorted((root / "geo-templates").iterdir()):
        if not (d / "meta.json").is_file():
            continue
        shutil.copyfile(d / "meta.json", GEO_OUT / f"{d.name}.meta.json")
        shutil.copyfile(d / "height.r16", GEO_OUT / f"{d.name}.height.bytes")
        shutil.copyfile(d / "water.u8", GEO_OUT / f"{d.name}.water.bytes")
        count += 1
    return count


def stage_copy(root: Path) -> None:
    COPY_OUT.mkdir(parents=True, exist_ok=True)
    for name in D0_COPY:
        shutil.copyfile(root / "_incoming" / "d0-copy-terra" / f"{name}.json", COPY_OUT / f"{name}.json")
    # Supplement keys are additive UI strings; a key collision would silently shadow reviewed copy, so fail loudly.
    incoming = root / "_incoming" / "d0-copy-terra"
    strings = json.loads((incoming / "strings.json").read_text(encoding="utf-8"))
    supplement = json.loads((incoming / "demo-ui-supplement.json").read_text(encoding="utf-8"))
    clash = sorted(set(strings) & set(supplement))
    if clash:
        raise SystemExit(f"error: demo-ui-supplement.json overrides strings.json keys: {clash}")
    strings.update(supplement)
    (COPY_OUT / "strings.json").write_text(json.dumps(strings, ensure_ascii=False, indent=2) + "\n",
                                           encoding="utf-8", newline="\n")
    shutil.copyfile(root / "balance.json", COPY_OUT / "balance.json")
    names = {}
    for batch in NAME_SOURCES:
        copy = json.loads((root / "_incoming" / batch / "copy-terra.json").read_text(encoding="utf-8"))
        for section in ("creatures", "moves", "traits"):
            for entry in copy.get(section, []):
                names[entry["id"]] = {"zh-TW": entry.get("name_zh", entry["id"]), "en": entry.get("name_en", entry["id"])}
    entries = [{"id": k, "zh": v["zh-TW"], "en": v["en"]} for k, v in sorted(names.items())]
    (COPY_OUT / "names.json").write_text(json.dumps({"entries": entries}, ensure_ascii=False, indent=1) + "\n",
                                         encoding="utf-8", newline="\n")


def main() -> int:
    root = Path("Data")
    out = root / "_demo"
    if out.exists():
        shutil.rmtree(out)
    for batch in BATCHES:
        src = root / "_incoming" / batch
        if not src.is_dir():
            print(f"error: missing batch {src}", file=sys.stderr)
            return 1
        for kind in KINDS:
            for f in sorted((src / kind).glob("*.json")) if (src / kind).is_dir() else []:
                dst = out / kind / f"{batch}--{f.name}"
                dst.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(f, dst)
    stage_copy(root)
    print(f"staged {stage_geo(root)} geo templates into {GEO_OUT}")
    print(f"staged {sum(1 for _ in out.rglob('*.json'))} files into {out}; copy into {COPY_OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
