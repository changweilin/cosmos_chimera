#!/usr/bin/env python3
"""Validate game data under a data root against Data/schemas plus cross-file rules.

Usage: tools/validate-data [--root DIR] [--schemas DIR]
  --root     data root containing creatures/ moves/ traits/ relations/ planet-types/ characters/ env-materials/ (default: Data)
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

from encyclopedia_checks import check_encyclopedia

try:
    from jsonschema import Draft202012Validator
    from referencing import Registry, Resource
except ImportError:
    print("error: requires `pip install jsonschema>=4.18`", file=sys.stderr)
    sys.exit(2)

REPO_ROOT = Path(__file__).resolve().parent.parent

# Folder name → schema stem. Folder layout mirrors the real Data/ tree so an _incoming
# batch can be validated with the exact same command before it is moved in.
INT32_MAX = 2**31 - 1
KINDS = {
    "encyclopedia": "encyclopedia",
    "creatures": "creature",
    "moves": "move",
    "traits": "trait",
    "relations": "relation",
    "planet-types": "planet-type",
    "characters": "character",
    "ingredients": "ingredient",
    "foods": "food",
    "eggs": "egg",
    "craft-items": "craft-item",
    "craft-recipes": "craft-recipe",
    "craft-grips": "craft-grip",
    "niches": "niche",
}
ID_KINDS = ("creatures", "moves", "traits", "planet-types", "characters", "ingredients", "foods", "eggs", "craft-items", "craft-recipes", "niches", "encyclopedia")
# Mirrors LandformSynth.KindIds (Assets/_Project/Features/WorldGen/Runtime/Model/LandformSynth.cs): every spawn point's
# habitat is one of these (creature-ecology.md#生成順序), so a form's niche habitats must cover all of them.
LANDFORM_KINDS = (
    "forest", "river_valley", "volcano", "rift_valley", "ice_field", "fjord",
    "plateau", "archipelago", "canyon", "cave", "eternal_day", "eternal_night",
    "grassland", "desert", "swamp", "mountain",
    "tundra", "glacier", "cirque", "moraine",
    "karst", "basalt_terrace", "granite_domes", "slate_cliffs",
)
STAGE_NEXT = {"juvenile": "adolescent", "adolescent": "adult"}
ENV_MATERIALS_FOLDER = "env-materials"
ENV_MATERIALS_SCHEMA = "env-materials"
NINE_ELEMENTS = ("metal", "wood", "water", "fire", "earth", "ice", "wind", "thunder", "light")
# JSON null must still reach schema validation; it cannot double as a parse-failure sentinel.
_INVALID_JSON = object()


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
    """Parse strictly: reject float literals, nonstandard constants and duplicate keys accepted by json.load."""
    floats: list[str] = []

    def on_float(s: str):
        floats.append(s)
        return float(s)

    def on_constant(s: str):
        raise ValueError(f"nonstandard JSON constant '{s}' is not allowed")

    def on_pairs(pairs):
        seen = set()
        for k, _ in pairs:
            if k in seen:
                findings.append(Finding("error", "duplicate_key", rel, "", f"duplicate key '{k}'"))
            seen.add(k)
        return dict(pairs)

    try:
        text = path.read_text(encoding="utf-8")
        doc = json.loads(text, parse_float=on_float, parse_constant=on_constant, object_pairs_hook=on_pairs)
    except json.JSONDecodeError as e:
        findings.append(Finding("error", "parse_error", rel, "", f"line {e.lineno} col {e.colno}: {e.msg}"))
        return _INVALID_JSON
    except UnicodeDecodeError as e:
        findings.append(Finding("error", "parse_error", rel, "", f"not UTF-8: {e}"))
        return _INVALID_JSON
    except ValueError as e:
        findings.append(Finding("error", "parse_error", rel, "", str(e)))
        return _INVALID_JSON
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
    if branch is not None and err.validator == "not" and "max_stacks" in str(err.validator_value):
        return "'max_stacks' is not allowed on instant heals (heal_percent_max_hp)"
    if branch is not None and err.validator == "not" and "heal_percent_max_hp" in str(err.validator_value)             and "effects" in err.absolute_path:
        return "heal_percent_max_hp needs an event trigger; always/on_hp_below/on_hp_above are continuous"
    if branch is not None and "allOf" in err.absolute_schema_path and any(
        p in ("trigger", "effect", "effects") for p in err.absolute_path
    ):
        # Per-type params of a trigger/effect (common.schema.json allOf of if/then).
        return f"{err.message} (params do not match this rule type)"
    if branch is not None:
        if "allOf" in err.absolute_schema_path:
            if err.validator == "not" and "effect_duration_ms" in str(err.validator_value):
                return "'effect_duration_ms' must be omitted for damage moves and instant heals"
            if "field_duration_ms" in str(err.validator_value):
                return "'field_duration_ms' is required iff shape is field (Q-C17)"
            return err.message
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


def validate(root: Path, schema_dir: Path, retired_ids_path: Path | None = None,
             balance_path: Path | None = None) -> list[Finding]:
    """Validate every data file under root.

    Pre: schema_dir contains one <stem>.schema.json per KINDS value plus common.schema.json; balance_path (default
         Data/balance.json) holds stat_budget.
    Post: returned findings are sorted (file, pointer); empty list == data is valid.
    Side effects: reads files only.
    """
    findings: list[Finding] = []
    registry, schemas = _registry(schema_dir)
    missing = [s for s in list(KINDS.values()) + [ENV_MATERIALS_SCHEMA] if s not in schemas]
    if missing:
        raise FileNotFoundError(f"missing schemas in {schema_dir}: {missing}")

    records: dict[str, list[Record]] = {k: [] for k in KINDS}
    validators = {stem: Draft202012Validator(schemas[stem], registry=registry) for stem in KINDS.values()}
    for folder, stem in KINDS.items():
        validator = validators[stem]
        for path in sorted((root / folder).glob("*.json")):
            rel = path.relative_to(root).as_posix()
            doc = _load_json(path, rel, findings)
            if doc is _INVALID_JSON:
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
    check_encyclopedia(records["encyclopedia"], findings, Finding)
    balance = json.loads((balance_path or REPO_ROOT / "Data" / "balance.json").read_text(encoding="utf-8"))
    _check_skill_stars(records, findings, balance["stat_budget"], validators)
    _check_niches(records, findings)
    _check_env_materials(root, schemas[ENV_MATERIALS_SCHEMA], registry, records, findings, retired_ids_path)
    findings.sort(key=lambda f: (f.file, f.pointer, f.code))
    return findings


STAT_KEYS = ("hp", "speed", "yin_atk", "yang_atk", "yin_def", "yang_def")


def _check_skill_stars(records: dict[str, list[Record]], findings: list[Finding], budget: dict, validators) -> None:
    """REQ: creatures.md#綜合值預算, #技能星級 — star-adjusted six-stat budget, ★★★ unique to one line, and every
    ★ downgraded version must still be a valid move / trait once merged over the full one."""
    skills: dict[str, Record] = {}
    for kind in ("moves", "traits"):
        for r in records[kind]:
            if isinstance(r.data.get("id"), str):
                skills[r.data["id"]] = r
            down = r.data.get("downgraded")
            if isinstance(down, dict):
                merged = {k: v for k, v in r.data.items() if k != "downgraded"}
                merged.update(down)
                merged["star"] = 1
                stem = KINDS[kind]
                for err in validators[stem].iter_errors([merged]):
                    for leaf in _leaf_errors(err):
                        findings.append(Finding("error", "downgraded_invalid", r.file, f"/{r.index}/downgraded",
                                                f"★ version is not a valid {stem}: {_schema_message(leaf)}"))

    lines_by_skill: dict[str, set] = {}
    for r in records["creatures"]:
        c = r.data
        stars = []
        for field in ("signature_move", "trait"):
            hit = skills.get(c.get(field)) if isinstance(c.get(field), str) else None
            star = hit.data.get("star") if hit is not None else None
            if isinstance(star, int):
                stars.append(star)
                if star == 3:
                    lines_by_skill.setdefault(c[field], set()).add(c.get("line_id"))
        stats = c.get("stats")
        stage = c.get("stage")
        base = budget["base"].get(stage) if isinstance(stage, str) else None
        complete = isinstance(stats, dict) and all(isinstance(stats.get(k), int) for k in STAT_KEYS)
        if len(stars) != 2 or not complete or base is None:
            continue  # schema / dangling-ref findings already cover these
        target = base * (100 - budget["per_extra_star_percent"] * (sum(stars) - 2)) / 100
        total = sum(stats[k] for k in STAT_KEYS)
        tol = target * budget["tolerance_percent"] / 100
        if abs(total - target) > tol:
            findings.append(Finding("error", "stat_budget", r.file, f"/{r.index}/stats",
                                    f"six-stat total {total} outside {target:g} ±{budget['tolerance_percent']}% "
                                    f"({c.get('stage')}, skill stars {stars[0]}+{stars[1]})"))
    for sid, lines in sorted(lines_by_skill.items()):
        if len(lines) > 1:
            r = skills[sid]
            findings.append(Finding("error", "unique_skill_shared", r.file, f"/{r.index}/star",
                                    f"★★★ skill '{sid}' is used by lines {sorted(map(str, lines))}; ★★★ is unique to one line"))


def _check_niches(records: dict[str, list[Record]], findings: list[Finding]) -> None:
    """REQ: docs/design/creature-ecology.md — distribution order, landform ids, creature niche refs and coverage."""
    niches: dict[str, Record] = {}
    for r in records["niches"]:
        nid = r.data.get("id")
        if isinstance(nid, str):
            niches.setdefault(nid, r)
        kinds = r.data.get("habitat_kinds")
        if isinstance(kinds, list):
            for i, k in enumerate(kinds):
                if k not in LANDFORM_KINDS:
                    findings.append(Finding("error", "niche_habitat", r.file, f"/{r.index}/habitat_kinds/{i}",
                                            f"'{k}' is not a landform kind (LandformSynth.Kinds)"))
        wander = r.data.get("wander")
        if isinstance(wander, dict):
            for key in ("speed_permille", "move_ms", "pause_ms"):
                d = wander.get(key)
                if isinstance(d, dict) and all(isinstance(d.get(k), int) for k in ("min", "mean", "max")):
                    if not d["min"] <= d["mean"] <= d["max"]:
                        findings.append(Finding("error", "niche_distribution", r.file, f"/{r.index}/wander/{key}",
                                                "needs min <= mean <= max"))
    for r in records["creatures"]:
        cands = r.data.get("niches")
        if not isinstance(cands, list):
            continue
        if r.data.get("stage") != "overload" and not cands:
            findings.append(Finding("error", "niche_candidates", r.file, f"/{r.index}/niches",
                                    "non-overload forms need at least one candidate niche"))
            continue
        covered: set[str] = set()
        seen: set[str] = set()
        for i, c in enumerate(cands):
            nid = c.get("niche") if isinstance(c, dict) else None
            if not isinstance(nid, str):
                continue
            if nid in seen:
                findings.append(Finding("error", "niche_candidates", r.file, f"/{r.index}/niches/{i}/niche",
                                        f"'{nid}' listed twice"))
            seen.add(nid)
            hit = niches.get(nid)
            if hit is None:
                findings.append(Finding("error", "dangling_ref", r.file, f"/{r.index}/niches/{i}/niche",
                                        f"'{nid}' is not a known niche id"))
                continue
            kinds = hit.data.get("habitat_kinds")
            if isinstance(kinds, list):
                covered.update(k for k in kinds if isinstance(k, str))
        missing = [k for k in LANDFORM_KINDS if k not in covered]
        if cands and missing:
            findings.append(Finding("error", "niche_coverage", r.file, f"/{r.index}/niches",
                                    f"candidate habitats miss landforms {missing}; a spawn there would fail"))


def _load_retired_ids(retired_ids_path: Path | None) -> set[str]:
    retired = set()
    if retired_ids_path and retired_ids_path.exists():
        for line in retired_ids_path.read_text(encoding="utf-8").splitlines():
            line = line.split("#", 1)[0].strip()
            if line:
                retired.add(line)
    return retired


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

    retired = _load_retired_ids(retired_ids_path)
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

    overload_targets: dict[str, Record] = {}
    for r in records["creatures"]:
        ref(r, "signature_move", "moves")
        ref(r, "trait", "traits")
        nxt = ref(r, "evolves_to", "creatures")
        if nxt is not None:
            stage = r.data.get("stage")
            if nxt.data.get("line_id") != r.data.get("line_id"):
                findings.append(Finding("error", "evolution_chain", r.file, f"/{r.index}/evolves_to",
                                        f"'{nxt.data.get('id')}' is in line '{nxt.data.get('line_id')}', not '{r.data.get('line_id')}'"))
            if isinstance(stage, str) and STAGE_NEXT.get(stage) != nxt.data.get("stage"):
                findings.append(Finding("error", "evolution_chain", r.file, f"/{r.index}/evolves_to",
                                        f"{stage} must evolve into {STAGE_NEXT.get(stage)}, got {nxt.data.get('stage')}"))
        ov_target = ref(r, "overload_of", "creatures")
        if ov_target is not None:
            if ov_target.data.get("line_id") != r.data.get("line_id"):
                findings.append(Finding("error", "overload_chain", r.file, f"/{r.index}/overload_of",
                                        f"'{ov_target.data.get('id')}' is in line '{ov_target.data.get('line_id')}', not '{r.data.get('line_id')}'"))
            if ov_target.data.get("stage") != "adult":
                findings.append(Finding("error", "overload_chain", r.file, f"/{r.index}/overload_of",
                                        f"overload_of must point to an adult form, got {ov_target.data.get('stage')}"))
            target_id = ov_target.data.get("id")
            if target_id in overload_targets:
                other = overload_targets[target_id]
                findings.append(Finding("error", "overload_duplicate", r.file, f"/{r.index}/overload_of",
                                        f"'{target_id}' already has overload form '{other.data.get('id')}'"))
            elif isinstance(target_id, str):
                overload_targets[target_id] = r
        _warn_material_elements(r, findings)
        _check_material_sums(r, findings)

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

    for r in records["craft-items"]:
        tool = r.data.get("gather_tool")
        if isinstance(tool, str):
            hit = by_id.get(tool)
            if hit is None or hit.kind != "craft-items" or hit.data.get("category") != "tool":
                findings.append(Finding("error", "dangling_ref", r.file, f"/{r.index}/gather_tool", "unknown gathering tool"))

    for r in records["craft-recipes"]:
        for field in ("costs", "gains"):
            for i, stack in enumerate(r.data.get(field, []) if isinstance(r.data.get(field), list) else []):
                if not isinstance(stack, dict) or not isinstance(stack.get("item"), str):
                    continue
                hit = by_id.get(stack["item"])
                if hit is None or hit.kind != "craft-items":
                    findings.append(Finding("error", "dangling_ref", r.file, f"/{r.index}/{field}/{i}/item", "unknown crafting item"))
        for i, tool in enumerate(r.data.get("tools", []) if isinstance(r.data.get("tools", []), list) else []):
            if not isinstance(tool, str):
                continue
            hit = by_id.get(tool)
            if hit is None or hit.kind != "craft-items" or hit.data.get("category") != "tool":
                findings.append(Finding("error", "dangling_ref", r.file, f"/{r.index}/tools/{i}", "unknown recipe tool"))
            costs = r.data.get("costs", [])
            if isinstance(costs, list) and any(isinstance(c, dict) and c.get("item") == tool for c in costs):
                findings.append(Finding("error", "tool_consumed", r.file, f"/{r.index}/tools/{i}", "reusable tool cannot also be a consumed cost"))

    # A crafting batch validated on its own has no creatures; species refs are checked once both are staged together.
    seen_grip_species: dict[str, Record] = {}
    for r in records["craft-grips"]:
        species = r.data.get("species")
        if not isinstance(species, str):
            continue
        if species in seen_grip_species:
            o = seen_grip_species[species]
            findings.append(Finding("error", "duplicate_id", r.file, f"/{r.index}/species",
                                    f"grips for '{species}' already defined in {o.file}/{o.index}"))
        else:
            seen_grip_species[species] = r
        if records["creatures"]:
            ref(r, "species", "creatures")

    for r in records["foods"]:
        ingredients = r.data.get("ingredients")
        if not isinstance(ingredients, list):
            continue
        for i, ing in enumerate(ingredients):
            if not isinstance(ing, dict):
                continue
            ing_id = ing.get("ingredient_id")
            if isinstance(ing_id, str):
                hit = by_id.get(ing_id)
                if hit is None or hit.kind != "ingredients":
                    findings.append(
                        Finding("error", "dangling_ref", r.file, f"/{r.index}/ingredients/{i}/ingredient_id",
                                f"'{ing_id}' is not a known ingredient id")
                    )

    for r in records["eggs"]:
        outcomes = r.data.get("outcomes")
        if not isinstance(outcomes, list):
            continue
        seen_species: set[str] = set()
        for i, o in enumerate(outcomes):
            if not isinstance(o, dict) or not isinstance(o.get("species"), str):
                continue
            sid = o["species"]
            hit = by_id.get(sid)
            if hit is None or hit.kind != "creatures":
                findings.append(Finding("error", "dangling_ref", r.file, f"/{r.index}/outcomes/{i}/species",
                                        f"'{sid}' is not a known creature id"))
                continue
            if hit.data.get("stage") != "juvenile":
                findings.append(Finding("error", "egg_outcome_stage", r.file, f"/{r.index}/outcomes/{i}/species",
                                        f"'{sid}' is {hit.data.get('stage')}; eggs hatch juveniles only"))
            if sid in seen_species:
                findings.append(Finding("error", "egg_outcome_duplicate", r.file, f"/{r.index}/outcomes/{i}/species",
                                        f"'{sid}' listed twice in one egg"))
            seen_species.add(sid)


def _check_material_sums(r: Record, findings: list[Finding]) -> None:
    # EvolutionRequirement (C#) merges repeated (element, tier) entries into one Int32 qty; the schema bounds each
    # entry only, so a legal file could otherwise pass here and throw at evolution time (Sol P2-05 review C2).
    evo = r.data.get("evolution")
    if not isinstance(evo, dict):
        return
    materials = evo.get("materials")
    if not isinstance(materials, list):
        return
    sums: dict[tuple, int] = {}
    for m in materials:
        # Schema-invalid entries still reach cross-file checks; an unhashable element/tier used to raise TypeError
        # and drop every finding (Sol P2-05/07 r2 C8). Skip them here — the schema finding already reports them.
        if not isinstance(m, dict):
            continue
        qty, el, tier = m.get("qty"), m.get("element"), m.get("tier")
        if (isinstance(qty, int) and not isinstance(qty, bool) and isinstance(el, str)
                and isinstance(tier, int) and not isinstance(tier, bool)):
            key = (el, tier)
            sums[key] = sums.get(key, 0) + m["qty"]
    for (el, tier), total in sums.items():
        if total > INT32_MAX:
            findings.append(Finding("error", "material_sum_overflow", r.file, f"/{r.index}/evolution/materials",
                                    f"{el}/tier {tier} summed qty {total} exceeds {INT32_MAX}"))


def _warn_material_elements(r: Record, findings: list[Finding]) -> None:
    # REQ: docs/design/creatures.md#進化 — Q-C9 decided 2026-11-06: material element must be a mastery element.
    evo, mastery = r.data.get("evolution"), r.data.get("mastery")
    if not (isinstance(evo, dict) and isinstance(mastery, dict)):
        return
    materials = evo.get("materials")
    if not isinstance(materials, list):
        return
    for i, m in enumerate(materials):
        el = m.get("element") if isinstance(m, dict) else None
        if isinstance(el, str) and el not in mastery:
            findings.append(Finding("error", "material_not_mastery", r.file, f"/{r.index}/evolution/materials/{i}/element",
                                    f"'{el}' is not one of this creature's mastery elements {sorted(mastery)} (Q-C9)"))


def _check_env_materials(
    root: Path,
    schema: dict,
    registry: Registry,
    records: dict[str, list[Record]],
    findings: list[Finding],
    retired_ids_path: Path | None,
) -> None:
    folder = root / ENV_MATERIALS_FOLDER
    if not folder.is_dir():
        return
    paths = sorted(folder.glob("*.json"))
    if not paths:
        return

    # env_materials_multiple: more than one *.json in env-materials/ (report on the second file, pointer "").
    if len(paths) > 1:
        for extra in paths[1:]:
            findings.append(
                Finding("error", "env_materials_multiple", extra.relative_to(root).as_posix(), "",
                        f"more than one *.json in {ENV_MATERIALS_FOLDER}/")
            )

    target_path = paths[0]
    rel = target_path.relative_to(root).as_posix()
    doc = _load_json(target_path, rel, findings)
    if doc is _INVALID_JSON:
        return

    validator = Draft202012Validator(schema, registry=registry)
    for err in sorted(validator.iter_errors(doc), key=lambda e: list(e.absolute_path)):
        for leaf in _leaf_errors(err):
            findings.append(Finding("error", "schema", rel, _pointer(leaf.absolute_path), _schema_message(leaf)))

    if not isinstance(doc, dict):
        return

    id_kinds_records: dict[str, Record] = {}
    for kind in ID_KINDS:
        for r in records.get(kind, []):
            rid = r.data.get("id")
            if isinstance(rid, str) and rid not in id_kinds_records:
                id_kinds_records[rid] = r

    retired = _load_retired_ids(retired_ids_path)

    substances = doc.get("substances")
    seen_substance_ids: dict[str, int] = {}
    defined_substances: dict[str, dict] = {}
    elements_present: set[str] = set()

    if isinstance(substances, list):
        for i, s in enumerate(substances):
            if not isinstance(s, dict):
                continue
            sid = s.get("id")
            if isinstance(sid, str):
                if sid in seen_substance_ids:
                    first_idx = seen_substance_ids[sid]
                    findings.append(
                        Finding("error", "duplicate_id", rel, f"/substances/{i}/id",
                                f"id '{sid}' already defined in this file at /substances/{first_idx}/id; ids are global")
                    )
                else:
                    seen_substance_ids[sid] = i
                    defined_substances[sid] = s
                    if sid in id_kinds_records:
                        other = id_kinds_records[sid]
                        findings.append(
                            Finding("error", "duplicate_id", rel, f"/substances/{i}/id",
                                    f"id '{sid}' already defined in {other.file}/{other.index}; ids are global")
                        )
                if sid in retired:
                    findings.append(
                        Finding("error", "retired_id", rel, f"/substances/{i}/id",
                                f"id '{sid}' is retired (Data/retired-ids.txt); ids are never reused")
                    )
            el = s.get("element")
            if isinstance(el, str):
                elements_present.add(el)

    for el in NINE_ELEMENTS:
        if el not in elements_present:
            findings.append(
                Finding("error", "element_without_source", rel, "/substances",
                        f"element '{el}' has no substance source")
            )

    drops = doc.get("drops")
    if isinstance(drops, dict):
        for name in ("material", "gem"):
            item = drops.get(name)
            if isinstance(item, dict):
                base = item.get("base_permille")
                cap = item.get("cap_permille")
                if (isinstance(base, int) and not isinstance(base, bool)
                        and isinstance(cap, int) and not isinstance(cap, bool)):
                    if cap < base:
                        findings.append(
                            Finding("error", "drop_cap_below_base", rel, f"/drops/{name}",
                                    f"drops/{name} cap_permille ({cap}) is less than base_permille ({base})")
                        )

    buildings = doc.get("buildings")
    seen_building_kinds: set[str] = set()
    if isinstance(buildings, list):
        for i, b in enumerate(buildings):
            if not isinstance(b, dict):
                continue
            bkind = b.get("kind")
            if isinstance(bkind, str):
                if bkind in seen_building_kinds:
                    findings.append(
                        Finding("error", "building_duplicate", rel, f"/buildings/{i}/kind",
                                f"building kind '{bkind}' repeated")
                    )
                else:
                    seen_building_kinds.add(bkind)
            composition = b.get("composition")
            if isinstance(composition, list):
                seen_comp_substances: set[str] = set()
                for j, comp in enumerate(composition):
                    if not isinstance(comp, dict):
                        continue
                    csub = comp.get("substance")
                    if isinstance(csub, str):
                        if csub not in defined_substances:
                            findings.append(
                                Finding("error", "dangling_ref", rel, f"/buildings/{i}/composition/{j}/substance",
                                        f"'{csub}' is not a known substance id in this file")
                            )
                        if csub in seen_comp_substances:
                            findings.append(
                                Finding("error", "building_duplicate", rel, f"/buildings/{i}/composition/{j}/substance",
                                        f"substance '{csub}' repeated in composition of building '{bkind}'")
                            )
                        else:
                            seen_comp_substances.add(csub)

    ground = doc.get("ground_materials")
    if isinstance(ground, dict):
        for key, gsub in ground.items():
            if not isinstance(gsub, str):
                continue
            if gsub not in defined_substances:
                findings.append(
                    Finding("error", "dangling_ref", rel, f"/ground_materials/{key}",
                            f"'{gsub}' is not a known substance id in this file")
                )
            elif key != "gem":
                sub_el = defined_substances[gsub].get("element")
                if isinstance(sub_el, str) and sub_el != key:
                    findings.append(
                        Finding("error", "ground_element_mismatch", rel, f"/ground_materials/{key}",
                                f"ground material for '{key}' points to substance '{gsub}' with element '{sub_el}'")
                    )


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
