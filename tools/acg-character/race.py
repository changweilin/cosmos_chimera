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
build erases that target and the channel needs another source), the step that fitted, and "per": the
channel change the whole baked key stands for (key weight 1), measured by acg_measure.py under the channel's "cal"
rule; it also gives the base's head radius, landmark measures and bone-channel values (the zero point of
the bone DNA). Side effects: overwrites the outputs.
"""
import argparse
import copy
import json
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import acg_measure  # noqa: E402
import build  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
VANISHED_M = 1e-4
TARGET_CAP = 3.0


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
    """The head scale, jaw plane and height factor that the base build derives. Each variant reuses them: re-solving
    the scale would undo every head target that changes the head-to-body ratio, re-deriving the plane lets it jump,
    and re-fitting the height would fold a shrink of the whole figure into every key that grows it."""
    solved = {}
    solve, shape, fit = build._solve_head_scale, build._acg_proportions, build._scale_to_height

    def capture_scale(h, p):
        solved["s"] = solve(h, p)
        return solved["s"]

    def capture_plane(h, p):
        out = shape(h, p)
        solved["plane"] = out["jaw_plane"]  # the last call is the real one; the solver's trials come first
        return out

    def capture_height(objs, target_m):
        solved["k"] = fit(objs, target_m)
        return solved["k"]

    build._solve_head_scale, build._acg_proportions, build._scale_to_height = capture_scale, capture_plane, capture_height
    try:
        build.build(spec)
    finally:
        build._solve_head_scale, build._acg_proportions, build._scale_to_height = solve, shape, fit
    s = copy.deepcopy(spec)
    prop = s.setdefault("proportions", {})
    if "s" in solved:
        prop.pop("heads", None)
        prop["head_scale"] = solved["s"]
    prop["jaw_plane"] = solved["plane"]
    s["height_scale"] = solved["k"]
    return s


def _meshes():
    # Props are hand-held and never deformed by DNA.
    return {o.name: o for o in bpy.data.objects if o.type == "MESH" and not o.name.startswith("prop_")}


def _coords(spec):
    build.build(spec)
    return {n: [v.co.copy() for v in o.data.vertices] for n, o in _meshes().items()}


def _param_value(spec, channel):
    section, key = channel["key"].split(".", 1)
    return spec.get(section, {}).get(key, channel.get("default"))


def _variant(spec, channel, amount):
    """(nudged spec, units actually applied). amount is signed, in units: target net weight or channel steps.

    A target pair is one axis: incr minus decr is its net weight. MakeHuman designs targets for [-1, 1]; being linear
    offsets they extrapolate, and up to TARGET_CAP is accepted (暫定). A base that already leans on the pair leaves
    less room, so the key is baked for what fits and the report records it."""
    s = copy.deepcopy(spec)
    if channel["kind"] == "target":
        targets = s.setdefault("targets", {})
        neg = channel.get("neg", [])
        steps = []
        for i, name in enumerate(channel["pos"]):
            opp = neg[i] if i < len(neg) else None
            net = targets.get(name, 0.0) - (targets.get(opp, 0.0) if opp else 0.0)
            lo = -TARGET_CAP if opp else 0.0
            new = min(TARGET_CAP, max(lo, net + amount))
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
    old = block[key]
    block[key] = old + amount * channel["step"]
    lo, hi = channel.get("range", (0.0, 1.0) if section == "macro" else (-1e9, 1e9))
    block[key] = min(hi, max(lo, block[key]))
    return s, (block[key] - old) / channel["step"]


def _far(spec, name, sign):
    """How far the race's farthest resident sits from the base on this side, or None without a race table."""
    npc = spec.get("npc", {})
    if name not in npc.get("reach", {}) or name not in npc.get("dna", {}):
        return None
    lo, hi = npc["reach"][name]
    return (hi - npc["dna"][name]) if sign > 0 else (npc["dna"][name] - lo)


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
    body = spec["id"]
    probe = acg_measure.Probe(bpy.data.objects[body])
    # The race base's own DNA means (npc_bases.py); a spec without them is calibrated around the human unit values.
    means = spec.get("npc", {}).get("dna", {})
    deltas = {}
    report = {"spec": spec["id"], "head_scale": spec["proportions"].get("head_scale"), "hr_m": round(probe.hr, 5),
              "bones": {k: round(v, 5) for k, v in probe.bones().items()},
              "measures": {k: round(v, 5) for k, v in probe.base.items()}, "keys": {}}
    for name, ch in bakes:
        value = _param_value(spec, ch) if ch["kind"] == "param" else None
        for sign in _sides(ch):
            key = name + ("+" if sign > 0 else "-")

            def bake(amount):
                nudged, units = _variant(spec, ch, amount)
                got = _coords(nudged) if units != 0 else base
                if set(got) != set(base) or any(len(got[m]) != len(base[m]) for m in base):
                    raise RuntimeError("topology changed for %s: shape keys need the same vertices" % key)
                # units is a target's net weight or a param's channel steps; per_key wants the spec-side step.
                step = units if ch["kind"] == "target" else units * ch["step"]
                var_m = probe.measure(got[body]) if units != 0 else probe.base
                return got, step, units, acg_measure.per_key(ch, probe, probe.base, var_m, step, means.get(name, 1.0), value)

            got, step, units, per = bake(float(sign))
            # Bake the key at the race's farthest resident on this side: inside a baked range the converter only
            # interpolates. Targets stop at TARGET_CAP; the gate test reports a key that still falls short.
            far = _far(spec, name, sign)
            if far is not None and far > 0 and per and per > 0:
                want = sign * far / (per / abs(units))
                # Short of the farthest resident by any margin is a re-bake; past it by up to 10 % is kept.
                if abs(want) > abs(units) * 1.001 or abs(want) < 0.9 * abs(units):
                    got, step, units, per = bake(want)
            moved, entry = {}, {}
            for m, pts in got.items():
                d = [p - q for p, q in zip(pts, base[m])]
                peak = max((v.length for v in d), default=0.0)
                if peak >= 1e-6:
                    moved[m] = d
                entry[m] = round(peak * 1000.0, 3)
            deltas[key] = moved
            report["keys"][key] = {"max_mm": {m: v for m, v in entry.items() if v > 0}, "step": round(step, 6),
                                   "units": round(units, 6), "per": None if per is None else round(per, 6),
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
