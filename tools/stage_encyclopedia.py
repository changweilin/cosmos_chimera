"""Stage only the new encyclopedia gameplay batch, preserving other demo inputs."""
import json
import shutil
from pathlib import Path


def main():
    repo = Path.cwd()
    batch = repo / "Data/_incoming/p4-09-encyclopedia-01"
    source = batch / "creatures/drive-creatures.json"
    target = repo / "Data/_demo/creatures/p4-09-encyclopedia-01--drive-creatures.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, target)
    path = repo / "Assets/_Project/Data/Copy/names.json"
    names = json.loads(path.read_text(encoding="utf-8"))
    additions = json.loads((batch / "copy-terra.json").read_text(encoding="utf-8"))["creatures"]
    ids = {entry["id"] for entry in additions}
    names["entries"] = [entry for entry in names["entries"] if entry["id"] not in ids]
    for entry in additions:
        names["entries"].append({"id": entry["id"], "zh": entry["name_zh"], "en": entry["name_en"], "ja": entry["name_zh"]})
    names["entries"].sort(key=lambda entry: entry["id"])
    path.write_text(json.dumps(names, ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")
    print(f"Staged {len(additions)} forms; untranslated names retain source-language spelling")


if __name__ == "__main__":
    main()
