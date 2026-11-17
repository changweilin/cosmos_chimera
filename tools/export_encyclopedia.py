"""Export category catalogs and verified numeric snapshots for authenticated Forge reads."""
import hashlib
import json
from pathlib import Path

from build_encyclopedia import read


def export(repo, output):
    entries = read(repo / "Data/_incoming/p4-09-encyclopedia-01/encyclopedia/entries.json")
    values = {}
    documents = {}
    for entry in entries:
        resolved = []
        sources = {source["id"]: source for source in entry["sources"]}
        for reference in entry["details"].get("value_refs", []):
            source = sources[reference["source"]]
            if source["storage"] != "repository":
                raise ValueError("numeric source must be game data")
            path = (repo / source["path"]).resolve()
            if not path.is_relative_to(repo.resolve()):
                raise ValueError("source outside repository")
            raw = path.read_bytes()
            if hashlib.sha256(raw).hexdigest() != source["sha256"]:
                raise ValueError(f"stale source: {source['path']}")
            if source["path"] not in documents:
                documents[source["path"]] = json.loads(raw)
            value = documents[source["path"]]
            for part in reference["pointer"].split("/")[1:]:
                key = part.replace("~1", "/").replace("~0", "~")
                value = value[int(key)] if isinstance(value, list) else value[key]
            if type(value) is not int:
                raise ValueError("numeric reference must resolve to integer")
            resolved.append({**reference, "value": value})
        values[entry["id"]] = resolved
    output.mkdir(parents=True, exist_ok=True)
    for kind, folder in (("character", "characters"), ("creature", "creatures"), ("skill", "moves")):
        (output / (folder + ".json")).write_text(json.dumps([e for e in entries if e["kind"] == kind], ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    (output / "values.json").write_text(json.dumps(values, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Exported {len(entries)} entries with verified numeric references")


if __name__ == "__main__":
    export(Path.cwd(), Path("Logs/encyclopedia-drive/catalogs"))
