#!/usr/bin/env python3
"""Intake check for an AI art delivery (docs/design/art-asset-pipeline.md, step 驗收).

Usage: tools/validate-art TASK_DIR [--apply] [--dry-run] [--json] [--repo-root DIR]
  TASK_DIR     the unzipped package, e.g. Art/_incoming/<task_id> (folder name must equal task_id)
  --apply      only if compliant: move files to their destinations (spec table 〈放置位置〉).
               Refuses to overwrite; computes and verifies the whole plan before moving anything.
  --dry-run    print the move plan without moving (implies plan mode)
  --json       print findings as a JSON list of {code, path, message}; all other output goes to stderr
  --repo-root  root that destination paths are relative to (default: this repository)
Exit: 0 = compliant (and applied / planned if asked), 1 = findings, 2 = usage / environment error.
Every finding is printed as `[<code>] <path>: <message>`; <path> is relative to TASK_DIR.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import struct
import sys
import zlib
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Optional

REPO_ROOT = Path(__file__).resolve().parent.parent

MANIFEST = "manifest.json"
SPEC_ID = "cosmimera-art-task/1"
LEVELS = ("L1", "L2")

# REQ: tools/prompt-forge/tasks.js `prefix` of each task (lines 112, 154, 209, 256, 305, 362, 406).
TASK_PREFIX = {
    "character-concept": "chr",
    "creature-concept": "cr",
    "model-rig": "mdl",
    "animation-set": "anim",
    "move-vfx": "fx",
    "capture-vessel": "vsl",
    "culture-research": "res",
}

# REQ: art-asset-pipeline.md 〈命名〉 — image prefixes chr_/cr_/fx_/vsl_. model-rig and animation-set
# images are named after their subject (tasks.js 224, 287), so either character or creature prefix is legal.
# culture-research delivers no images, so it has no image prefix.
IMAGE_PREFIXES = {
    "character-concept": ("chr_",),
    "creature-concept": ("cr_",),
    "model-rig": ("cr_", "chr_"),
    "animation-set": ("cr_", "chr_"),
    "move-vfx": ("fx_",),
    "capture-vessel": ("vsl_",),
}

# Types whose file names embed subject_id, so subject_id must itself be snake_case. Not enforced for
# move-vfx / capture-vessel / culture-research: tasks.js 146 gives them ids like "region_東亞" or "vessels".
SUBJECT_NAMED = ("character-concept", "creature-concept", "model-rig", "animation-set")

# REQ: tasks.js 98 `order` / catalog.js 139-142 STAGES.
STAGES = ("juvenile", "adolescent", "adult", "overload")
# REQ: art-asset-pipeline.md 〈命名〉 view list ...
SPEC_CREATURE_VIEWS = ("main", "front", "side", "back", "three_quarter", "lineup", "scale")
# ... plus `turnaround`, which the spec list omits but tasks.js 165 asks for.
CREATURE_VIEWS = SPEC_CREATURE_VIEWS + ("turnaround",)
# tasks.js 186-187 asks for `cr_<line>_lineup.png` / `cr_<line>_scale.png` with NO stage, while the spec
# pattern `cr_<line_id>_<stage>_<view>.png` implies one. Both forms are accepted.
STAGELESS_CREATURE_VIEWS = ("lineup", "scale")
# REQ: tasks.js 125-129 (the spec lists no character views).
CHARACTER_VIEWS = ("main", "turnaround", "front", "side", "back", "expressions", "props")
MODEL_VIEWS = ("front", "side", "back")  # tasks.js 224
# REQ: tasks.js 332-336. The spec's `<phase>` slot is, in tasks.js, one of storyboard | layers | flipbook.
VFX_SUFFIXES = ("storyboard", "layers", "flipbook")
# Mirrors catalog.js ELEMENTS (7-17), VFX.shapes (209-213), VFX.tiers (214). Update with catalog major bumps.
ELEMENTS = ("metal", "wood", "water", "fire", "earth", "ice", "wind", "thunder", "light", "shadow")
VFX_SHAPES = ("melee_front", "melee_sweep", "melee_burst", "ranged_line", "ranged_cone", "ranged_arc",
              "support_self", "support_ally", "field")
VFX_TIERS = ("light", "medium", "heavy", "overload")
# tasks.js 371-376: per-vessel deliverables.
VESSEL_IMAGE_SUFFIXES = ("concept", "variants", "icon", "model_front", "model_side", "capture_storyboard")

# tasks.js 240 asks for `rig/README.md`; it is the only legal non-lowercase name.
UPPERCASE_EXEMPT = ("rig/README.md", "pipeline/README.md")
NON_PNG_IMAGE_EXTS = ("jpg", "jpeg", "webp", "gif", "bmp")
TEXTURE_SIZES = (1024, 2048)

REQUIRED_FIELDS = (
    ("spec", str), ("task_header", str), ("task_id", str), ("task_type", str), ("subject_id", str),
    ("produced_by", str), ("files", list), ("missing", list), ("notes", str),
)
OPTIONAL_EMPTY = ("notes",)

_SNAKE = r"[a-z0-9]+(?:_[a-z0-9]+)*"
_SNAKE_RE = re.compile(_SNAKE)
_EXT_RE = re.compile(r"[a-z0-9]+")
_TASK_ID_RE = re.compile(r"[a-z0-9]+(?:-[a-z0-9]+)*")
_HEADER_RE = re.compile(
    r"\[COSMIMERA-TASK spec=(?P<spec>\d+) tool=(?P<tool>\d+\.\d+\.\d+) catalog=(?P<catalog>\d+\.\d+\.\d+)"
    r" template=(?P<template>[a-z0-9-]+)@(?P<tver>\d+\.\d+\.\d+) task_id=(?P<task_id>[^\s\]]+)"
    r" seed=(?P<seed>[^\s\]]+)\]"
)

ART_ROOT = "Assets/_Project/Art"
DATA_INCOMING = "Data/_incoming"
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


@dataclass(frozen=True)
class Finding:
    code: str
    path: str
    message: str

    def __str__(self) -> str:
        return f"[{self.code}] {self.path}: {self.message}"


@dataclass
class Result:
    findings: list[Finding] = field(default_factory=list)
    png_sizes: dict[str, tuple[int, int]] = field(default_factory=dict)
    task_id: str = ""
    task_type: Optional[str] = None  # None when absent or not one of TASK_PREFIX
    subject_id: Optional[str] = None
    files: list[str] = field(default_factory=list)  # valid listed paths, manifest order


@dataclass(frozen=True)
class Move:
    src: str  # relative to the task folder
    dst: str  # relative to the repo root, POSIX separators


class PngError(ValueError):
    pass


def read_png_size(path: Path) -> tuple[int, int]:
    """Return (width, height) from the PNG signature + IHDR chunk.

    Pre: path is a readable file.
    Post: returns positive dimensions, or raises PngError naming the defect.
    Side effects: reads at most 33 bytes.
    """
    with open(path, "rb") as fh:
        head = fh.read(33)
    if len(head) < 33:
        raise PngError("file too short for a PNG header")
    if head[:8] != PNG_SIGNATURE:
        raise PngError("bad PNG signature")
    length, ctype = struct.unpack(">I4s", head[8:16])
    if ctype != b"IHDR" or length != 13:
        raise PngError("first chunk is not a 13-byte IHDR")
    data = head[16:29]
    (crc,) = struct.unpack(">I", head[29:33])
    if zlib.crc32(b"IHDR" + data) != crc:
        raise PngError("IHDR CRC mismatch")
    width, height = struct.unpack(">II", data[:8])
    if width == 0 or height == 0:
        raise PngError(f"zero dimension {width}x{height}")
    return width, height


def _alt(items) -> str:
    return "|".join(items)


def _name_rule(task_type: Optional[str], sid: Optional[str], rel: str) -> Optional[tuple[str, str]]:
    """Return (regex for the basename, human description) or None when no rule applies.

    Pre: rel is a valid relative POSIX path. sid is a snake_case subject id or None.
    Post: None means "only generic snake_case/prefix checks apply" for this file.
    Side effects: none.
    """
    if task_type is None:
        return None
    parts = rel.split("/")
    name = parts[-1]
    top = parts[0] if len(parts) > 1 else ""
    ext = name.rpartition(".")[2].lower() if "." in name else ""
    s = re.escape(sid) if sid else None
    stages = _alt(STAGES)

    if ext == "png":
        if task_type == "character-concept" and s:
            return (rf"{s}_(?:{_alt(CHARACTER_VIEWS)})\.png",
                    f"{sid}_<{_alt(CHARACTER_VIEWS)}>.png")
        if task_type == "creature-concept" and s:
            return (rf"cr_{s}_(?:(?P<stage>{stages})_(?P<view>{_alt(CREATURE_VIEWS)})"
                    rf"|(?P<solo>{_alt(STAGELESS_CREATURE_VIEWS)}))\.png",
                    f"cr_{sid}_<{stages}>_<{_alt(CREATURE_VIEWS)}>.png "
                    f"or cr_{sid}_<{_alt(STAGELESS_CREATURE_VIEWS)}>.png")
        if task_type == "model-rig" and s:
            # REQ: tasks.js model-rig@2 — rigging and animation are one pipeline, so anim sheets live here too.
            tail = rf"(?:model_(?:{_alt(MODEL_VIEWS)})|anim_sheet_\d{{2}})"
            if name.startswith("cr_"):
                return (rf"cr_{s}_(?:{stages})_{tail}\.png",
                        f"cr_{sid}_<stage>_<model_<{_alt(MODEL_VIEWS)}>|anim_sheet_NN>.png")
            if name.startswith("chr_"):
                return (rf"{s}_{tail}\.png",
                        f"{sid}_<model_<{_alt(MODEL_VIEWS)}>|anim_sheet_NN>.png")
        if task_type == "animation-set" and s:
            if name.startswith("cr_"):
                return (rf"cr_{s}_(?:{stages})_anim_sheet_\d{{2}}\.png",
                        f"cr_{sid}_<stage>_anim_sheet_NN.png")
            if name.startswith("chr_"):
                return (rf"{s}_anim_sheet_\d{{2}}\.png", f"{sid}_anim_sheet_NN.png")
        if task_type == "move-vfx":
            return (rf"fx_(?:(?:{_alt(ELEMENTS)})_(?:{_alt(VFX_SHAPES)})_(?:{_alt(VFX_TIERS)})"
                    rf"_(?:{_alt(VFX_SUFFIXES)})|common_{_SNAKE}_storyboard)\.png",
                    f"fx_<element>_<shape>_<tier>_<{_alt(VFX_SUFFIXES)}>.png or fx_common_<id>_storyboard.png")
        if task_type == "capture-vessel":
            return (rf"vsl_(?P<vessel>{_SNAKE})_(?:{_alt(VESSEL_IMAGE_SUFFIXES)})\.png",
                    f"vsl_<vessel>_<{_alt(VESSEL_IMAGE_SUFFIXES)}>.png")
        return None

    if task_type == "capture-vessel":
        if top == "vfx" and ext == "json":
            return (rf"vsl_(?P<vessel>{_SNAKE})_capture_spec\.json", "vsl_<vessel>_capture_spec.json")
        if top == "models" and ext == "glb":
            return (rf"vsl_(?P<vessel>{_SNAKE})\.glb", "vsl_<vessel>.glb")
    if task_type == "model-rig" and s and top == "models" and ext in ("glb", "fbx"):
        return (rf"(?:cr_{s}_(?:{stages})|{s})\.(?:glb|fbx)", f"cr_{sid}_<stage>.<glb|fbx> or {sid}.<glb|fbx>")
    if task_type in ("model-rig", "animation-set") and top == "anims" and ext == "fbx":
        # REQ: art-asset-pipeline.md 〈命名〉 clip names `chr_<action>` / `cr_<stage>_<action>`.
        return (rf"(?:chr_{_SNAKE}|cr_(?:{stages})_{_SNAKE})\.fbx", "chr_<action>.fbx or cr_<stage>_<action>.fbx")
    return None


def _check_naming(findings: list[Finding], task_type: Optional[str], sid: Optional[str], rel: str) -> None:
    parts = rel.split("/")
    if rel not in UPPERCASE_EXEMPT:
        for i, part in enumerate(parts):
            if i == len(parts) - 1 and "." in part:
                stem, ext = part.rsplit(".", 1)
            else:
                stem, ext = part, ""
            ok_ext = ext == "" or _EXT_RE.fullmatch(ext)
            if not (_SNAKE_RE.fullmatch(stem) and ok_ext):
                findings.append(Finding("name_not_snake_case", rel,
                                        f"'{part}' must be lowercase snake_case (a-z, 0-9, single underscores)"))
                return
    name = parts[-1]
    ext = name.rpartition(".")[2].lower() if "." in name else ""
    if ext in NON_PNG_IMAGE_EXTS:
        findings.append(Finding("image_format", rel, f"images must be PNG, got .{ext}"))
        return
    prefixes = IMAGE_PREFIXES.get(task_type or "")
    if ext == "png" and prefixes and not name.startswith(prefixes):
        findings.append(Finding("name_prefix", rel,
                                f"{task_type} images must start with {' or '.join(prefixes)}"))
        return
    rule = _name_rule(task_type, sid, rel)
    if rule and not re.fullmatch(rule[0], name):
        findings.append(Finding("name_pattern", rel, f"'{name}' does not match {task_type} naming: {rule[1]}"))


def _bad_path(p: str) -> Optional[str]:
    if not p:
        return "path is empty"
    if "\\" in p:
        return "backslash not allowed; use '/'"
    if p.startswith("/") or re.match(r"[A-Za-z]:", p):
        return "path must be relative"
    if any(c in ("", ".", "..") for c in p.split("/")):
        return "path must not contain empty, '.' or '..' components"
    if p == MANIFEST:
        return "manifest.json must not list itself"
    return None


def _load_manifest(task_dir: Path, findings: list[Finding]):
    path = task_dir / MANIFEST
    if not path.is_file():
        findings.append(Finding("manifest_missing", MANIFEST, "manifest.json not found in the package root"))
        return None
    try:
        doc = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        findings.append(Finding("manifest_invalid_json", MANIFEST, f"line {e.lineno} col {e.colno}: {e.msg}"))
        return None
    except UnicodeDecodeError as e:
        findings.append(Finding("manifest_invalid_json", MANIFEST, f"not UTF-8: {e}"))
        return None
    if not isinstance(doc, dict):
        findings.append(Finding("manifest_invalid_json", MANIFEST, "top-level value must be an object"))
        return None
    return doc


def _check_header_fields(m: dict, folder: str, findings: list[Finding], result: Result) -> None:
    def add(code: str, message: str) -> None:
        findings.append(Finding(code, MANIFEST, message))

    ok: dict[str, str] = {}
    for key, typ in REQUIRED_FIELDS:
        if key not in m:
            add("manifest_field_missing", f"required field '{key}' is missing")
        elif not isinstance(m[key], typ) or (typ is str and key not in OPTIONAL_EMPTY and not m[key].strip()):
            want = "a non-empty string" if typ is str and key not in OPTIONAL_EMPTY else f"of type {typ.__name__}"
            add("manifest_field_type", f"'{key}' must be {want}")
        elif typ is str:
            ok[key] = m[key]

    spec = ok.get("spec")
    if spec is not None and spec != SPEC_ID:
        add("spec_mismatch", f"spec is '{spec}', expected '{SPEC_ID}'")

    task_type = ok.get("task_type")
    if task_type is not None:
        if task_type in TASK_PREFIX:
            result.task_type = task_type
        else:
            add("task_type_unknown", f"task_type '{task_type}' is not one of {', '.join(TASK_PREFIX)}")

    task_id = ok.get("task_id")
    if task_id is not None:
        result.task_id = task_id
        if not _TASK_ID_RE.fullmatch(task_id):
            add("task_id_invalid", f"task_id '{task_id}' must be lowercase letters, digits and '-'")
        if task_id != folder:
            add("task_id_mismatch", f"task_id '{task_id}' does not equal the folder name '{folder}'")
        if result.task_type and not task_id.startswith(TASK_PREFIX[result.task_type] + "-"):
            add("task_id_prefix", f"task_id must start with '{TASK_PREFIX[result.task_type]}-' for {result.task_type}")

    header = ok.get("task_header")
    if header is not None:
        hm = _HEADER_RE.fullmatch(header.strip())
        if hm is None:
            add("header_malformed", "task_header must match [COSMIMERA-TASK spec=… tool=… catalog=… "
                                    "template=<id>@<ver> task_id=… seed=…]")
        else:
            # REQ: tasks.js model-rig@2 batch — one prompt yields one package per source pack, each with
            # task_id "<batch task_id>-<subject-id>" and batch_id echoing the header's task_id.
            batch_id = m.get("batch_id")
            batched = (isinstance(batch_id, str) and task_id is not None and hm["task_id"] == batch_id
                       and task_id.startswith(batch_id + "-"))
            if task_id is not None and hm["task_id"] != task_id and not batched:
                add("header_mismatch", f"header task_id '{hm['task_id']}' != manifest task_id '{task_id}'")
            if task_type is not None and hm["template"] != task_type:
                add("header_mismatch", f"header template '{hm['template']}' != manifest task_type '{task_type}'")
            if spec is not None and hm["spec"] != spec.rpartition("/")[2]:
                add("header_mismatch", f"header spec={hm['spec']} disagrees with manifest spec '{spec}'")

    sid = ok.get("subject_id")
    if sid is not None and result.task_type in SUBJECT_NAMED:
        if _SNAKE_RE.fullmatch(sid):
            result.subject_id = sid
        else:
            add("subject_id_invalid", f"subject_id '{sid}' must be lowercase snake_case for {result.task_type}")
    elif sid is not None:
        result.subject_id = sid


def _check_files(m: dict, disk: set[str], findings: list[Finding]) -> dict[str, Optional[str]]:
    listed: dict[str, Optional[str]] = {}
    files = m.get("files")
    if not isinstance(files, list):
        return listed
    for i, e in enumerate(files):
        if not isinstance(e, dict) or not isinstance(e.get("path"), str):
            findings.append(Finding("file_path_invalid", MANIFEST, f"files[{i}] must be an object with a string 'path'"))
            continue
        p = e["path"]
        bad = _bad_path(p)
        if bad:
            findings.append(Finding("file_path_invalid", p, bad))
            continue
        if e.get("level") not in LEVELS:
            findings.append(Finding("file_level_invalid", p, f"level must be 'L1' or 'L2', got {e.get('level')!r}"))
        kind = e.get("kind")
        if not isinstance(kind, str) or not kind.strip():
            findings.append(Finding("file_kind_invalid", p, "kind must be a non-empty string"))
            kind = None
        if p in listed:
            findings.append(Finding("file_duplicate", p, "path is listed more than once in files[]"))
            continue
        listed[p] = kind
        if p not in disk:
            findings.append(Finding("file_not_found", p, "listed in files[] but not present in the package"))
    return listed


def _check_missing(m: dict, listed: dict[str, Optional[str]], findings: list[Finding]) -> None:
    missing = m.get("missing")
    if not isinstance(missing, list):
        return
    for i, e in enumerate(missing):
        item = e.get("item") if isinstance(e, dict) else None
        if not isinstance(item, str) or not item.strip():
            findings.append(Finding("missing_entry_invalid", MANIFEST,
                                    f"missing[{i}] must be an object with a non-empty string 'item'"))
            continue
        level = e.get("level")
        if level == "L1":
            # REQ: art-asset-pipeline.md 〈交付層級〉 — an L1 item that is missing means the task failed.
            findings.append(Finding("l1_deliverable_missing", item,
                                    "L1 deliverable missing; only L2 items may be listed in missing[]"))
        elif level != "L2":
            findings.append(Finding("missing_entry_invalid", item, f"missing[{i}].level must be 'L1' or 'L2', got {level!r}"))
        # Exact match only: `missing` items may be globs (e.g. models/*.glb) that legitimately overlap partial deliveries.
        if item in listed:
            findings.append(Finding("missing_also_listed", item, "file is listed in files[] and also reported as missing"))


def _disk_files(task_dir: Path) -> set[str]:
    out: set[str] = set()
    for root, _dirs, names in os.walk(task_dir):
        for n in names:
            rel = Path(root, n).relative_to(task_dir).as_posix()
            if rel != MANIFEST:
                out.add(rel)
    return out


def validate(task_dir: Path) -> Result:
    """Check one unzipped delivery package.

    Pre: task_dir is an existing directory.
    Post: findings are sorted (path, code, message); empty findings == compliant.
          png_sizes holds every parseable PNG found on disk.
    Side effects: reads files only.
    """
    task_dir = Path(task_dir)
    result = Result()
    findings = result.findings
    disk = _disk_files(task_dir)
    manifest = _load_manifest(task_dir, findings)
    listed: dict[str, Optional[str]] = {}

    if manifest is not None:
        _check_header_fields(manifest, task_dir.resolve().name, findings, result)
        listed = _check_files(manifest, disk, findings)
        result.files = list(listed)
        _check_missing(manifest, listed, findings)
        for rel in sorted(disk - set(listed)):
            findings.append(Finding("file_unlisted", rel, "file is in the package but not listed in files[]"))
        sid = result.subject_id if result.task_type in SUBJECT_NAMED else None
        for rel in sorted(set(listed) | disk):
            _check_naming(findings, result.task_type, sid, rel)

    for rel in sorted(disk):
        lower = rel.lower()
        if lower.endswith(".png"):
            try:
                w, h = read_png_size(task_dir / rel)
            except PngError as e:
                findings.append(Finding("png_invalid", rel, str(e)))
                continue
            except OSError as e:
                findings.append(Finding("png_invalid", rel, f"unreadable: {e}"))
                continue
            result.png_sizes[rel] = (w, h)
            # Size rule (user decision 2026-11-06): report only, except `texture` entries.
            if listed.get(rel) == "texture" and not (w == h and w in TEXTURE_SIZES):
                findings.append(Finding("texture_size", rel,
                                        f"texture must be square {' or '.join(map(str, TEXTURE_SIZES))}, got {w}x{h}"))
        elif lower.endswith(".json"):
            try:
                json.loads((task_dir / rel).read_text(encoding="utf-8"))
            except (json.JSONDecodeError, UnicodeDecodeError) as e:
                detail = f"line {e.lineno} col {e.colno}: {e.msg}" if isinstance(e, json.JSONDecodeError) else f"not UTF-8: {e}"
                findings.append(Finding("json_invalid", rel, detail))

    findings.sort(key=lambda f: (f.path, f.code, f.message))
    return result


def _destination(task_type: str, task_id: str, sid: Optional[str], rel: str) -> tuple[Optional[str], str]:
    """Map one file to its destination (relative to the repo root); (None, reason) when the spec has no rule.

    Pre: the package passed validate(); rel is a listed file.
    Post: dst is POSIX-style, under Assets/_Project/Art or Data/_incoming, or None with a reason.
    Side effects: none.
    """
    name = rel.rpartition("/")[2]
    data_dir = f"{DATA_INCOMING}/{task_id}"
    if task_type == "culture-research":
        return f"{data_dir}/{rel}", ""
    if task_type in ("character-concept", "creature-concept", "capture-vessel") and rel == "concept.json":
        return f"{data_dir}/concept.json", ""
    rule = _name_rule(task_type, sid, rel)
    m = re.fullmatch(rule[0], name) if rule else None
    if task_type == "character-concept" and m and sid:
        return f"{ART_ROOT}/Characters/{sid}/Concept/{name}", ""
    if task_type == "creature-concept" and m and sid:
        if m.group("stage") is None:
            return None, "line-level image (no stage in its name) has no folder in the spec's 〈放置位置〉 table"
        return f"{ART_ROOT}/Creatures/{sid}/{m.group('stage')}/Concept/{name}", ""
    if task_type == "capture-vessel" and m:
        return f"{ART_ROOT}/Vessels/{m.group('vessel')}/{name}", ""
    return None, "no destination rule for this file"


def plan_apply(task_dir: Path, repo_root: Path, result: Result) -> tuple[list[Move], list[Finding], list[str]]:
    """Compute and verify the full move plan.

    Pre: result came from validate(task_dir) and has no findings.
    Post: findings empty => every move has a unique, non-existing destination whose parents can be created.
          kept lists files already at their destination (nothing to move).
    Side effects: reads the filesystem only.
    """
    task_dir, repo_root = Path(task_dir), Path(repo_root)
    findings: list[Finding] = []
    moves: list[Move] = []
    kept: list[str] = []
    task_type = result.task_type or ""
    if task_type in ("model-rig", "animation-set", "move-vfx"):
        # Needs a stage/clip/move_id folder that neither manifest nor spec table determines; do not guess.
        findings.append(Finding("no_destination_rule", ".",
                                f"task type '{task_type}' has no destination rule derivable from the spec; "
                                "move the files by hand (see HANDOFF open questions)"))
        return moves, findings, kept
    for rel in result.files:
        dst, why = _destination(task_type, result.task_id, result.subject_id, rel)
        if dst is None:
            findings.append(Finding("no_destination_rule", rel, f"task type '{task_type}': {why}"))
            continue
        if (task_dir / rel).resolve() == (repo_root / dst).resolve():
            kept.append(rel)
            continue
        moves.append(Move(rel, dst))

    seen: dict[str, str] = {}
    for mv in moves:
        dst_abs = repo_root / mv.dst
        if mv.dst in seen:
            findings.append(Finding("destination_conflict", mv.src, f"same destination as '{seen[mv.dst]}': {mv.dst}"))
            continue
        seen[mv.dst] = mv.src
        if os.path.lexists(dst_abs):
            findings.append(Finding("destination_exists", mv.src, f"destination already exists, refusing to overwrite: {mv.dst}"))
            continue
        anc = dst_abs.parent
        while not os.path.lexists(anc) and anc != anc.parent:
            anc = anc.parent
        if os.path.lexists(anc) and not anc.is_dir():
            findings.append(Finding("destination_blocked", mv.src, f"'{anc.relative_to(repo_root).as_posix()}' is a file; cannot create {mv.dst}"))
    findings.sort(key=lambda f: (f.path, f.code, f.message))
    return moves, findings, kept


def execute_moves(task_dir: Path, repo_root: Path, moves: list[Move]) -> Optional[Finding]:
    """Move files per a verified plan; roll back and return a finding if any move fails.

    Pre: plan_apply returned these moves with no findings.
    Post: None => every file is at its destination; Finding => nothing moved (or rollback failure is stated).
    Side effects: creates directories under repo_root and moves files out of task_dir.
    """
    done: list[tuple[Path, Path]] = []
    made: list[Path] = []
    current = ""
    try:
        for mv in moves:
            current = mv.src
            src, dst = Path(task_dir) / mv.src, Path(repo_root) / mv.dst
            missing_dirs: list[Path] = []
            p = dst.parent
            while not p.exists():
                missing_dirs.append(p)
                p = p.parent
            for d in reversed(missing_dirs):
                d.mkdir()
                made.append(d)
            shutil.move(str(src), str(dst))
            done.append((src, dst))
    except OSError as e:
        stuck = 0
        for src, dst in reversed(done):
            try:
                shutil.move(str(dst), str(src))
            except OSError:
                stuck += 1
        for d in reversed(made):
            try:
                d.rmdir()
            except OSError:
                pass
        tail = "rolled back" if not stuck else f"ROLLBACK INCOMPLETE: {stuck} file(s) left at destination"
        return Finding("apply_failed", current, f"{e}; {tail}")
    return None


def _json_findings(findings: list[Finding]) -> str:
    return json.dumps([asdict(f) for f in findings], indent=2, ensure_ascii=False)


def main(argv: Optional[list[str]] = None) -> int:
    ap = argparse.ArgumentParser(prog="validate-art", description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("task_dir", type=Path)
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--json", action="store_true", dest="as_json")
    ap.add_argument("--repo-root", type=Path, default=REPO_ROOT)
    try:
        args = ap.parse_args(argv)
    except SystemExit as e:
        return e.code if isinstance(e.code, int) else 2
    if not args.task_dir.is_dir():
        print(f"error: task folder not found: {args.task_dir}", file=sys.stderr)
        return 2
    plan_mode = args.apply or args.dry_run
    if plan_mode and not args.repo_root.is_dir():
        print(f"error: repo root not found: {args.repo_root}", file=sys.stderr)
        return 2

    out = sys.stderr if args.as_json else sys.stdout

    def say(line: str = "") -> None:
        print(line, file=out)

    result = validate(args.task_dir)
    findings = list(result.findings)
    say(f"validate-art: {args.task_dir}  task_id={result.task_id or '?'}  task_type={result.task_type or '?'}")
    if result.png_sizes:
        say("PNG images (width x height):")
        for rel, (w, h) in sorted(result.png_sizes.items()):
            say(f"  {rel}  {w}x{h}")

    moves: list[Move] = []
    if plan_mode and not findings:
        moves, plan_findings, kept = plan_apply(args.task_dir, args.repo_root, result)
        findings.extend(plan_findings)
        if not plan_findings:
            say("Plan:")
            for rel in kept:
                say(f"  keep  {rel}  (already at its destination)")
            for mv in moves:
                say(f"  move  {mv.src} -> {mv.dst}")
            if not moves:
                say("  (nothing to move)")

    if findings:
        say("Findings:")
        for f in findings:
            say(f"  {f}")

    if not findings and args.apply and not args.dry_run:
        failure = execute_moves(args.task_dir, args.repo_root, moves)
        if failure:
            findings.append(failure)
            say(f"  {failure}")
        else:
            say(f"Applied: moved {len(moves)} file(s).")

    if args.as_json:
        print(_json_findings(findings))
    else:
        say(f"{len(findings)} finding(s)" if findings else "OK: package is compliant")
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
