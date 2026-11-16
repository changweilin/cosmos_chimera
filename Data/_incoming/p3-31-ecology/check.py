#!/usr/bin/env python3
"""Validate the P3-31 draft without registering unfinished runtime data kinds."""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools"))
from validate_data import Draft202012Validator, _INVALID_JSON, _load_json, _registry


def main():
    findings = []
    path = Path(__file__).with_name("proposal.json")
    data = _load_json(path, path.relative_to(ROOT).as_posix(), findings)
    registry, schemas = _registry(ROOT / "Data/schemas")
    if data is not _INVALID_JSON:
        validator = Draft202012Validator(schemas["ecological-niche"], registry=registry)
        findings.extend(str(error) for error in validator.iter_errors(data))
    if findings or data is _INVALID_JSON:
        for finding in findings:
            print(f"FAIL: {finding}", file=sys.stderr)
        return 1

    source = (ROOT / "Assets/_Project/Features/WorldGen/Runtime/Model/LandformSynth.cs").read_text(encoding="utf-8")
    kinds_block = re.search(r"string\[\] KindIds\s*=\s*\{(.*?)\};", source, re.S)
    if kinds_block is None:
        print("FAIL: cannot locate LandformSynth.KindIds", file=sys.stderr)
        return 1
    kinds = set(re.findall(r'"([a-z_]+)"', kinds_block[1]))
    retired = {line.split("#", 1)[0].strip() for line in (ROOT / "Data/retired-ids.txt").read_text(encoding="utf-8").splitlines()}
    ids = set()
    modes = set()
    for niche in data:
        ident = niche["id"]
        if ident in ids or ident in retired:
            findings.append(f"duplicate or retired ID: {ident}")
        ids.add(ident)
        unknown = set(niche["habitat_kinds"]) - kinds
        if unknown:
            findings.append(f"{ident}: unknown habitat kinds: {sorted(unknown)}")
        if sum(niche["temperament_weights"].values()) > 2147483647:
            findings.append(f"{ident}: temperament weight total exceeds Int32")
        wander = niche["wander"]
        modes.add(wander["mode"])
        for field in ("speed_permille", "move_ms", "pause_ms"):
            distribution = wander[field]
            if not distribution["min"] <= distribution["mean"] <= distribution["max"]:
                findings.append(f"{ident}/{field}: require min <= mean <= max")
    expected_modes = {"shuttle", "loop", "bounded_random", "unbounded_random"}
    if modes != expected_modes:
        findings.append(f"draft must cover all four modes: missing {sorted(expected_modes - modes)}")
    if findings:
        for finding in findings:
            print(f"FAIL: {finding}", file=sys.stderr)
        return 1
    print(f"PASS: {len(data)} niches, {len(modes)} modes, {len(data) * 3} distributions")
    return 0


if __name__ == "__main__":
    sys.exit(main())
