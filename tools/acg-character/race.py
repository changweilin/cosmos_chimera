"""Bakes the DNA shape keys of the ACG UMA race onto a bare base body (ADR-0014, ADR-0015).

Usage (Blender 5.2 with MPFB, as build.py):
  blender -b --python tools/acg-character/race.py -- --spec tools/acg-character/specs/<id>.json \
      --blend <out.blend> [--fbx <out.fbx>] [--report <out.json>] [--only <channel>,...]

For every "target" and "param" channel of race_channels.json the whole bare build runs again with the
channel nudged up (and down, where it has a negative side), and the vertex difference against the base
becomes a shape key "<channel>+" / "<channel>-" on every mesh it moves. Running the full build, rather
than loading the target onto the finished mesh, keeps the ACG edits that follow the targets (head
scale, face smoothing, chin shaping, drawn eyes projected onto the face) inside each key.

Pre: spec follows specs/README.md. Post: <out.blend> holds the bare base with the shape keys; the report
lists, per key, the largest vertex move per mesh (keys under 0.1 mm are flagged "vanished": the ACG
build erases that target and the channel needs another source). Side effects: overwrites the outputs.
"""
import argparse
import copy
import json
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
VANISHED_M = 1e-4


def _args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--spec", required=True)
    p.add_argument("--blend", required=True)
    p.add_argument("--fbx")
    p.add_argument("--report")
    p.add_argument("--only", help="comma-separated channel names (quick checks)")
    return p.parse_args(argv)


def _bare(spec):
    s = copy.deepcopy(spec)
    s["hair"] = None
    s["outfit"] = []
    s.pop("idle_pose", None)
    return s


def _freeze_head(spec):
    """The head scale that the base build solves for. Each variant reuses it: re-solving per variant
    would undo every head target that changes the head-to-body ratio."""
    solved = {}
    original = build._solve_head_scale

    def capture(h, p):
        solved["s"] = original(h, p)
        return solved["s"]

    build._solve_head_scale = capture
    try:
        build.build(spec)
    finally:
        build._solve_head_scale = original
    s = copy.deepcopy(spec)
    prop = s.setdefault("proportions", {})
    if "s" in solved:
        prop.pop("heads", None)
        prop["head_scale"] = solved["s"]
    return s


def _meshes():
    # Props are hand-held and never deformed by DNA.
    return {o.name: o for o in bpy.data.objects if o.type == "MESH" and not o.name.startswith("prop_")}


def _coords(spec):
    build.build(spec)
    return {n: [v.co.copy() for v in o.data.vertices] for n, o in _meshes().items()}


def _variant(spec, channel, sign):
    """(nudged spec, step actually applied). A target pair is one axis: incr minus decr is its net weight, which
    MakeHuman caps at ±1. A base spec that already leans on the pair (Roen's ear-scale-decr 1.0) leaves less room,
    so the key is baked for the step that fits and the report records it for the converter to divide by."""
    s = copy.deepcopy(spec)
    if channel["kind"] == "target":
        targets = s.setdefault("targets", {})
        neg = channel.get("neg", [])
        steps = []
        for i, name in enumerate(channel["pos"]):
            opp = neg[i] if i < len(neg) else None
            net = targets.get(name, 0.0) - (targets.get(opp, 0.0) if opp else 0.0)
            lo = -1.0 if opp else 0.0
            new = min(1.0, max(lo, net + sign))
            targets[name] = max(new, 0.0)
            if opp:
                targets[opp] = max(-new, 0.0)
            steps.append(new - net)
        return s, min(steps, key=abs)
    section, key = channel["key"].split(".", 1)
    block = s.setdefault(section, {})
    if section == "macro" and key not in block:
        from bl_ext.user_default.mpfb.services.targetservice import TargetService
        block[key] = TargetService.get_default_macro_info_dict()[key]
    if key not in block:
        if "default" not in channel:
            raise KeyError("spec has no %s and race_channels.json gives no default" % channel["key"])
        block[key] = channel["default"]
    block[key] = block[key] + sign * channel["step"]
    return s, sign * channel["step"]


def _sides(channel):
    if channel["kind"] == "target":
        return [1, -1] if channel.get("neg") else [1]
    return [1, -1]


def main():
    a = _args()
    table = json.load(open(os.path.join(HERE, "race_channels.json"), encoding="utf-8"))["channels"]
    spec = _freeze_head(_bare(json.load(open(a.spec, encoding="utf-8"))))
    wanted = set(a.only.split(",")) if a.only else None
    bakes = [(n, c) for n, c in table.items() if c["kind"] in ("target", "param") and (wanted is None or n in wanted)]

    base = _coords(spec)
    deltas, report = {}, {"spec": spec["id"], "head_scale": spec["proportions"].get("head_scale"), "keys": {}}
    for name, ch in bakes:
        for sign in _sides(ch):
            key = name + ("+" if sign > 0 else "-")
            nudged, step = _variant(spec, ch, sign)
            got = _coords(nudged) if step != 0 else base
            if set(got) != set(base) or any(len(got[m]) != len(base[m]) for m in base):
                raise RuntimeError("topology changed for %s: shape keys need the same vertices" % key)
            moved, entry = {}, {}
            for m, pts in got.items():
                d = [p - q for p, q in zip(pts, base[m])]
                peak = max((v.length for v in d), default=0.0)
                if peak >= 1e-6:
                    moved[m] = d
                entry[m] = round(peak * 1000.0, 3)
            deltas[key] = moved
            report["keys"][key] = {"max_mm": {m: v for m, v in entry.items() if v > 0}, "step": round(step, 6),
                                   "vanished": max(entry.values(), default=0.0) < VANISHED_M * 1000.0}
            print("RACE_KEY %s %s" % (key, json.dumps(report["keys"][key])))

    # The final scene is the base itself; keys go on top of its Basis.
    build.build(spec)
    meshes = _meshes()
    for key, moved in deltas.items():
        for m, d in moved.items():
            o = meshes[m]
            if o.data.shape_keys is None:
                o.shape_key_add(name="Basis", from_mix=False)
            sk = o.shape_key_add(name=key, from_mix=False)
            for i, dv in enumerate(d):
                sk.data[i].co = o.data.vertices[i].co + Vector(dv)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(a.blend))

    if a.fbx:
        _export(a.fbx)
    if a.report:
        os.makedirs(os.path.dirname(os.path.abspath(a.report)), exist_ok=True)
        with open(a.report, "w", encoding="utf-8", newline="\n") as f:
            json.dump(report, f, indent=1, sort_keys=True)
            f.write("\n")
    gone = [k for k, v in report["keys"].items() if v["vanished"]]
    print("RACE_OK keys=%d vanished=%s" % (len(report["keys"]), ",".join(gone) or "none"))


def _export(path):
    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    for o in bpy.data.objects:
        for mod in getattr(o, "modifiers", []):
            if mod.name == "acg_outline":
                mod.show_viewport = mod.show_render = False
    coll = bpy.data.collections.new("race_export")
    coll.objects.link(rig)
    for o in rig.children_recursive:
        if o.type == "MESH" and not o.name.startswith("prop_"):
            coll.objects.link(o)
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    # Modifiers stay unapplied: the exporter drops shape keys from meshes whose modifiers it applies.
    bpy.ops.export_scene.fbx(filepath=os.path.abspath(path), collection=coll.name, object_types={"ARMATURE", "MESH"},
                             mesh_smooth_type="FACE", use_mesh_modifiers=False, bake_anim=False, add_leaf_bones=False,
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y")
    bpy.data.collections.remove(coll)


if __name__ == "__main__":
    main()
