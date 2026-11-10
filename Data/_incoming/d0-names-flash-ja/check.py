#!/usr/bin/env python3
"""Validation script for Data/_incoming/d0-names-flash-ja/names-ja.json.

Verifies:
1. Entry count and exact ID order match Assets/_Project/Data/Copy/names.json.
2. zh and en fields match byte-identically.
3. Every ja field is non-empty and contains valid Japanese characters.
4. No duplicate ja values across all entries.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

JP_CHAR_PATTERN = re.compile(r"[\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FFF\u31F0-\u31FF\u30FC]")

def find_paths() -> tuple[Path, Path]:
    script_dir = Path(__file__).resolve().parent
    ja_path = script_dir / "names-ja.json"

    candidates = [
        script_dir.parents[2] / "Assets" / "_Project" / "Data" / "Copy" / "names.json",
        Path.cwd() / "Assets" / "_Project" / "Data" / "Copy" / "names.json",
    ]
    base_path = None
    for c in candidates:
        if c.is_file():
            base_path = c
            break

    if not base_path:
        print("ERROR: could not locate Assets/_Project/Data/Copy/names.json", file=sys.stderr)
        sys.exit(2)

    if not ja_path.is_file():
        print(f"ERROR: could not locate {ja_path}", file=sys.stderr)
        sys.exit(2)

    return base_path, ja_path


def main() -> None:
    base_path, ja_path = find_paths()

    with base_path.open(encoding="utf-8") as f:
        base_data = json.load(f)
    with ja_path.open(encoding="utf-8") as f:
        ja_data = json.load(f)

    base_entries = base_data.get("entries", [])
    ja_entries = ja_data.get("entries", [])

    errors: list[str] = []

    if len(base_entries) != len(ja_entries):
        errors.append(f"Entry count mismatch: base has {len(base_entries)}, ja has {len(ja_entries)}")

    seen_ja: dict[str, str] = {}

    for idx, (b, j) in enumerate(zip(base_entries, ja_entries)):
        b_id, j_id = b.get("id"), j.get("id")
        if b_id != j_id:
            errors.append(f"[{idx}] ID order mismatch: expected '{b_id}', got '{j_id}'")

        b_zh, j_zh = b.get("zh"), j.get("zh")
        if b_zh != j_zh:
            errors.append(f"[{idx} {b_id}] zh mismatch: expected '{b_zh}', got '{j_zh}'")

        b_en, j_en = b.get("en"), j.get("en")
        if b_en != j_en:
            errors.append(f"[{idx} {b_id}] en mismatch: expected '{b_en}', got '{j_en}'")

        ja_val = j.get("ja")
        if not isinstance(ja_val, str) or not ja_val.strip():
            errors.append(f"[{idx} {b_id}] ja is empty or not string: {ja_val!r}")
            continue

        ja_str = ja_val.strip()
        if not JP_CHAR_PATTERN.search(ja_str):
            errors.append(f"[{idx} {b_id}] ja does not contain Japanese characters: '{ja_str}'")

        if ja_str in seen_ja:
            errors.append(f"[{idx} {b_id}] duplicate ja value '{ja_str}' (first seen at id '{seen_ja[ja_str]}')")
        else:
            seen_ja[ja_str] = b_id

    if errors:
        print(f"Validation FAILED with {len(errors)} error(s):", file=sys.stderr)
        for err in errors:
            print(f"  - {err}", file=sys.stderr)
        sys.exit(1)

    print("ALL PASS")
    sys.exit(0)


if __name__ == "__main__":
    main()
