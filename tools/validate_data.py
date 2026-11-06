#!/usr/bin/env python3
"""Validate game data under a data root against Data/schemas plus cross-file rules.

Usage: tools/validate-data [--root DIR] [--schemas DIR]
  --root     data root containing creatures/ moves/ traits/ relations/ planet-types/ (default: Data)
  --schemas  schema directory (default: Data/schemas)
Exit: 0 = no errors (warnings allowed), 1 = errors found, 2 = usage / environment error.
Every finding is printed as `<level> [<code>] <file>:<json-pointer>: <message>`.
"""
from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass
from pathlib import Path

try:
    from jsonschema import Draft202012Validator
    from referencing import Registry, Resource
except ImportError:
    print("error: requires `pip install jsonschema>=4.18`", file=sys.stderr)
    sys.exit(2)

REPO_ROOT = Path(__file__).resolve().parent.parent

# Folder name → schema stem. Folder layout mirrors the real Data/ tree so an _incoming
# batch can be validated with the exact same command before it is moved in.
KINDS = {
    "creatures": "creature",
    "moves": "move",
    "traits": "trait",
    "relations": "relation",
    "planet-types": "planet-type",
}
ID_KINDS = ("creatures", "moves", "traits", "planet-types")
STAGE_NEXT = {"juvenile": "adolescent", "adolescent": "adult"}


@dataclass(frozen=True)
class Finding:
    level: str  # "error" | "warning"
    code: str
    file: str
    pointer: str
    message: str

    def __str__(self) -> str:
        return f"{self.level} [{self.code}] {self.file}:{self.pointer or '/'}: {self.message}"


@dataclass
class Record:
    kind: str
    file: str
    index: int
    data: dict


def _pointer(parts) -> str:
    return "".join(f"/{p}" for p in parts)


def _load_json(path: Path, rel: str, findings: list[Finding]):
    """Parse strictly: reject float literals and duplicate keys, which plain json.load accepts silently."""
    floats: list[str] = []

    def on_float(s: str):
        floats.append(s)
        return float(s)

    def on_pairs(pairs):
        seen = set()
        for k, _ in pairs:
            if k in seen:
                findings.append(Finding("error", "duplicate_key", rel, "", f"duplicate key '{k}'"))
            seen.add(k)
        return dict(pairs)

    try:
        text = path.read_text(encoding="utf-8")
        doc = json.loads(text, parse_float=on_float, object_pairs_hook=on_pairs)
    except json.JSONDecodeError as e:
        findings.append(Finding("error", "parse_error", rel, "", f"line {e.lineno} col {e.colno}: {e.msg}"))
        return None
    except UnicodeDecodeError as e:
        findings.append(Finding("error", "parse_error", rel, "", f"not UTF-8: {e}"))
        return None
    for s in floats:
        # REQ: .claude/rules/data-content.md — integers only, floats format differently across tools.
        findings.append(Finding("error", "float_literal", rel, "", f"float literal {s} not allowed; use an integer (percent as int)"))
    return doc


def _registry(schema_dir: Path) -> tuple[Registry, dict[str, dict]]:
    schemas = {}
    resources = []
    for p in sorted(schema_dir.glob("*.schema.json")):
        doc = json.loads(p.read_text(encoding="utf-8"))
        Draft202012Validator.check_schema(doc)
        schemas[p.name[: -len(".schema.json")]] = doc
        res = Resource.from_contents(doc)
        resources.append((doc["$id"], res))
        resources.append((p.name, res))
    return Registry().with_resources(resources), schemas


def _schema_message(err) -> str:
    # Conditional branches give bare type errors ("is not of type 'null'"); name the rule instead.
    branch = next((p for p in err.absolute_schema_path if p in ("then", "else")), None)
    if branch is not None:
        hint = "adult stage requires null" if branch == "then" else "non-adult stage requires a value"
        if err.validator == "not":
            hint = "'effect' must be omitted when kind is damage"
        elif err.validator == "required":
            hint = "'effect' is required when kind is effect or both"
        return f"{err.message} ({hint})"
    v = err.validator
    if v == "required":
        return err.message
    if v == "enum":
        return f"{err.instance!r} is not one of {err.validator_value}"
    if v == "pattern":
        return f"{err.instance!r} must be snake_case (pattern {err.validator_value})"
    if v in ("maxProperties", "minProperties"):
        return f"has {len(err.instance)} entries; {v} is {err.validator_value}"
    return err.message


def _leaf_errors(err):
    # oneOf/if-then report the umbrella error; the useful message is in the deepest context.
    if err.context:
        for sub in err.context:
            yield from _leaf_errors(sub)
    else:
        yield err


def validate(root: Path, schema_dir: Path, retired_ids_path: Path | None = None) -> list[Finding]:
    """Validate every data file under root.

    Pre: schema_dir contains one <stem>.schema.json per KINDS value plus common.schema.json.
    Post: returned findings are sorted (file, pointer); empty list == data is valid.
    Side effects: reads files only.
    """
    findings: list[Finding] = []
    registry, schemas = _registry(schema_dir)
    missing = [s for s in KINDS.values() if s not in schemas]
    if missing:
        raise FileNotFoundError(f"missing schemas in {schema_dir}: {missing}")

    records: dict[str, list[Record]] = {k: [] for k in KINDS}
    for folder, stem in KINDS.items():
        validator = Draft202012Validator(schemas[stem], registry=registry)
        for path in sorted((root / folder).glob("*.json")):
            rel = path.relative_to(root).as_posix()
            doc = _load_json(path, rel, findings)
            if doc is None:
                continue
            for err in sorted(validator.iter_errors(doc), key=lambda e: list(e.absolute_path)):
                for leaf in _leaf_errors(err):
                    findings.append(Finding("error", "schema", rel, _pointer(leaf.absolute_path), _schema_message(leaf)))
            # Cross-ref checks still run on schema-invalid items so one bad field doesn't hide dangling refs.
            if isinstance(doc, list):
                for i, item in enumerate(doc):
                    if isinstance(item, dict):
                        records[folder].append(Record(folder, rel, i, item))

    _check_cross_refs(records, findings, retired_ids_path)
    findings.sort(key=lambda f: (f.file, f.pointer, f.code))
    return findings


def _check_cross_refs(records: dict[str, list[Record]], findings: list[Finding], retired_ids_path: Path | None) -> None:
    by_id: dict[str, Record] = {}
    for kind in ID_KINDS:
        for r in records[kind]:
            rid = r.data.get("id")
            if not isinstance(rid, str):
                continue
            if rid in by_id:
                other = by_id[rid]
                findings.append(Finding("error", "duplicate_id", r.file, f"/{r.index}/id",
                                        f"id '{rid}' already defined in {other.file}/{other.index}; ids are global"))
            else:
                by_id[rid] = r

    retired = set()
    if retired_ids_path and retired_ids_path.exists():
        for line in retired_ids_path.read_text(encoding="utf-8").splitlines():
            line = line.split("#", 1)[0].strip()
            if line:
                retired.add(line)
    for rid, r in by_id.items():
        if rid in retired:
            findings.append(Finding("error", "retired_id", r.file, f"/{r.index}/id",
                                    f"id '{rid}' is retired (Data/retired-ids.txt); ids are never reused"))

    def ref(r: Record, field: str, kind: str):
        target = r.data.get(field)
        if not isinstance(target, str):
            return None
        hit = by_id.get(target)
        if hit is None or hit.kind != kind:
            findings.append(Finding("error", "dangling_ref", r.file, f"/{r.index}/{field}",
                                    f"'{target}' is not a known {kind[:-1]} id"))
            return None
        return hit

    for r in records["creatures"]:
        ref(r, "signature_move", "moves")
        ref(r, "trait", "traits")
        nxt = ref(r, "evolves_to", "creatures")
        if nxt is not None:
            stage = r.data.get("stage")
            if nxt.data.get("line_id") != r.data.get("line_id"):
                findings.append(Finding("error", "evolution_chain", r.file, f"/{r.index}/evolves_to",
                                        f"'{nxt.data.get('id')}' is in line '{nxt.data.get('line_id')}', not '{r.data.get('line_id')}'"))
            if STAGE_NEXT.get(stage) != nxt.data.get("stage"):
                findings.append(Finding("error", "evolution_chain", r.file, f"/{r.index}/evolves_to",
                                        f"{stage} must evolve into {STAGE_NEXT.get(stage)}, got {nxt.data.get('stage')}"))
        _warn_material_elements(r, findings)

    seen_edges: dict[tuple[str, str], Record] = {}
    for r in records["relations"]:
        a, b = r.data.get("a"), r.data.get("b")
        if not (isinstance(a, str) and isinstance(b, str)):
            continue
        if a == b:
            findings.append(Finding("error", "edge_self", r.file, f"/{r.index}", f"self-edge '{a}'"))
            continue
        if not a < b:
            # REQ: data-content.md — undirected edges stored once with a < b so both ends can't disagree.
            findings.append(Finding("error", "edge_order", r.file, f"/{r.index}",
                                    f"a must sort before b: swap to a='{b}', b='{a}'"))
        key = (min(a, b), max(a, b))
        if key in seen_edges:
            o = seen_edges[key]
            findings.append(Finding("error", "edge_duplicate", r.file, f"/{r.index}",
                                    f"edge {key[0]}<->{key[1]} already defined in {o.file}/{o.index}"))
        else:
            seen_edges[key] = r
        ref(r, "a", "creatures")
        ref(r, "b", "creatures")


def _warn_material_elements(r: Record, findings: list[Finding]) -> None:
    # Q-C9 is provisional (material element == mastery element), so this warns instead of failing CI.
    evo, mastery = r.data.get("evolution"), r.data.get("mastery")
    if not (isinstance(evo, dict) and isinstance(mastery, dict)):
        return
    for i, m in enumerate(evo.get("materials") or []):
        el = m.get("element") if isinstance(m, dict) else None
        if isinstance(el, str) and el not in mastery:
            findings.append(Finding("warning", "material_not_mastery", r.file, f"/{r.index}/evolution/materials/{i}/element",
                                    f"'{el}' is not one of this creature's mastery elements {sorted(mastery)} (Q-C9 provisional rule)"))


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--root", type=Path, default=REPO_ROOT / "Data")
    ap.add_argument("--schemas", type=Path, default=REPO_ROOT / "Data" / "schemas")
    ap.add_argument("--retired-ids", type=Path, default=REPO_ROOT / "Data" / "retired-ids.txt")
    args = ap.parse_args(argv)
    if not args.root.is_dir():
        print(f"error: data root not found: {args.root}", file=sys.stderr)
        return 2
    try:
        findings = validate(args.root, args.schemas, args.retired_ids)
    except FileNotFoundError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    for f in findings:
        print(f)
    errors = sum(f.level == "error" for f in findings)
    warnings = len(findings) - errors
    print(f"{errors} error(s), {warnings} warning(s) in {args.root}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
