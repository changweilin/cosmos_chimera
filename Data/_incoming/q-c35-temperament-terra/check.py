#!/usr/bin/env python3
"""Check the proposal against the current demo creature IDs."""
import json
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
LEVELS = {"timid", "cautious", "normal", "territorial", "fierce", "apex"}


def main():
    try:
        demo_ids = []
        for path in sorted((ROOT / "Data/_demo/creatures").glob("*.json")):
            demo_ids.extend(item["id"] for item in json.loads(path.read_text(encoding="utf-8")))
        proposal = json.loads(Path(__file__).with_name("proposal.json").read_text(encoding="utf-8"))
        entries = proposal["entries"]
        ids = [entry["id"] for entry in entries]
        errors = []
        if not demo_ids:
            errors.append("no demo creature IDs found")
        if proposal["confirmed"] is not False:
            errors.append("confirmed must be false")
        duplicates = [key for key, count in Counter(ids).items() if count > 1]
        if duplicates:
            errors.append(f"duplicate proposal IDs: {duplicates}")
        if set(ids) != set(demo_ids):
            errors.append(f"ID mismatch: missing={sorted(set(demo_ids) - set(ids))}, extra={sorted(set(ids) - set(demo_ids))}")
        invalid = [entry["id"] for entry in entries if entry["proposed"] not in LEVELS]
        if invalid:
            errors.append(f"invalid proposed temperament: {invalid}")
        if errors:
            for error in errors:
                print(f"FAIL: {error}", file=sys.stderr)
            return 1
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
    print("ALL PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
