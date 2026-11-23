"""Builds a Japanese-ACG style character from an MPFB (CC0) base in Blender (ADR-0013).

Usage (Blender 5.2 with the MPFB extension and the CC0 system asset pack installed):
  blender -b --python tools/acg-character/build.py -- --spec tools/acg-character/specs/<id>.json \
      --blend <out.blend> [--render <dir>] [--fbx <out.fbx>]

Pre: spec JSON follows specs/README.md. Post: <out.blend> holds the rigged character; optional
renders (front/side/back/3-4) and FBX. Side effects: overwrites the output files.
"""
import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import acg_face  # noqa: E402
import acg_parts  # noqa: E402
import acg_render  # noqa: E402
import acg_shape  # noqa: E402

from bl_ext.user_default.mpfb.services.humanservice import HumanService  # noqa: E402
from bl_ext.user_default.mpfb.services.locationservice import LocationService  # noqa: E402
from bl_ext.user_default.mpfb.services.targetservice import TargetService  # noqa: E402


def _args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--spec", required=True)
    p.add_argument("--blend", required=True)
    p.add_argument("--render")
    p.add_argument("--fbx")
    p.add_argument("--bare", action="store_true", help="base body only: no hair or outfit (proportion checks)")
    return p.parse_args(argv)


def _clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.armatures, bpy.data.curves):
        for d in list(coll):
            coll.remove(d)


def _group_centroid(obj, name):
    gi = obj.vertex_groups[name].index
    pts = [v.co for v in obj.data.vertices if any(g.group == gi and g.weight > 0.5 for g in v.groups)]
    return sum(pts, Vector()) / len(pts)


def _smooth(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def _acg_proportions(h, p):
    """Bends a realistic MakeHuman body toward anime proportions by direct vertex edits.

    All vertices move, helpers and joint cubes included, so proxies and the rig fitted
    afterwards follow the new shape.
    """
    neck = _group_centroid(h, "joint-neck")
    head = _group_centroid(h, "joint-head")
    pelvis = _group_centroid(h, "joint-pelvis")
    knee = (_group_centroid(h, "joint-l-knee") + _group_centroid(h, "joint-r-knee")) / 2
    wrists = [_group_centroid(h, "joint-l-hand"), _group_centroid(h, "joint-r-hand")]
    ankles = [_group_centroid(h, "joint-l-ankle"), _group_centroid(h, "joint-r-ankle")]

    s_head = p.get("head_scale", 1.25)
    s_leg = p.get("leg_length", 1.06)
    s_neck = p.get("neck_thin", 0.85)
    s_hand = p.get("hand_scale", 0.9)
    s_foot = p.get("foot_scale", 0.9)
    s_waist = p.get("waist_thin", 0.95)
    s_thigh = p.get("thigh_length", 1.0)
    crotch_z = pelvis.z - 0.08 * (pelvis.z / 0.95)
    head_group = h.vertex_groups.new(name="acg_head")

    # Jaw plane from under the chin to the nape. The head scales rigidly above it and the neck
    # blends below it. A height-only falloff scaled the eyes more than the mouth and chin and
    # thinned the lower face with the neck, which tilted the profile back from brow to chin.
    # "jaw_plane" pins the plane (race.py): the most forward point can jump from nose to chin between two face shapes,
    # and the 1.25-1.7x head scale then moves whole regions across the plane instead of following the shape.
    if "jaw_plane" in p:
        jaw_f, jaw_b = Vector(p["jaw_plane"][0]), Vector(p["jaw_plane"][1])
    else:
        mid = [v.co for v in h.data.vertices if abs(v.co.x) < 0.004 and neck.z < v.co.z < head.z - 0.01]
        tip = min(mid, key=lambda c: c.y)
        jaw_f = Vector((0, tip.y, tip.z - 0.025))
        jaw_b = Vector((0, head.y + 0.05, head.z - 0.015))

    def above_jaw(c):
        t = min(1.0, max(0.0, (c.y - jaw_f.y) / (jaw_b.y - jaw_f.y)))
        return c.z - (jaw_f.z + (jaw_b.z - jaw_f.z) * t)

    for v in h.data.vertices:
        c = v.co.copy()
        # Hands and feet first, around their own joints, before limbs move.
        for w in wrists:
            if (c - w).length < 0.25 and abs(c.x) > abs(w.x) - 0.01:
                c = w + (c - w) * s_hand
        for a in ankles:
            if c.z < a.z + 0.02 and (Vector((c.x, 0, 0)) - Vector((a.x, 0, 0))).length < 0.15:
                c = Vector((a.x + (c.x - a.x) * s_foot, a.y + (c.y - a.y) * s_foot, c.z * (0.85 + 0.15 * s_foot)))
        # Thin the neck between the neck joint and the jaw plane.
        dj = above_jaw(c)
        wn = _smooth(neck.z - 0.03, neck.z + 0.02, c.z) * (1 - _smooth(-0.03, 0.0, dj))
        if wn > 0:
            k = 1 + (s_neck - 1) * wn
            c.x = neck.x + (c.x - neck.x) * k
            c.y = neck.y + (c.y - neck.y) * k
        # Waist: narrow the band between ribs and hips.
        ww = _smooth(pelvis.z, pelvis.z + 0.12, c.z) * (1 - _smooth(pelvis.z + 0.18, pelvis.z + 0.30, c.z))
        if ww > 0 and abs(c.x) < 0.25:
            c.x *= 1 + (s_waist - 1) * ww
        # Enlarge the head around the head joint; blend through the neck.
        wh = _smooth(-0.025, 0.005, dj)
        if wh > 0:
            head_group.add([v.index], _smooth(neck.z + 0.03, head.z - 0.03, c.z), "REPLACE")
            k = 1 + (s_head - 1) * wh
            c = head + (c - head) * k
        # Slim the legs: anime legs are long and narrow.
        wl = (1 - _smooth(crotch_z - 0.02, crotch_z + 0.04, c.z)) * _smooth(ankles[0].z, ankles[0].z + 0.06, c.z)
        if wl > 0 and abs(c.x) > 0.01:
            k = 1 + (p.get("leg_thin", 1.0) - 1) * wl
            x0 = math.copysign(abs(knee.x), c.x)
            c.x = x0 + (c.x - x0) * k
            c.y = knee.y + (c.y - knee.y) * k
        # Anime thighs are long relative to the shin: stretch knee..crotch, shift everything above.
        dz_thigh = min(max(c.z - knee.z, 0.0), crotch_z - knee.z) * (s_thigh - 1)
        # Lengthen the legs below the crotch.
        if c.z < crotch_z:
            c.z *= s_leg
        else:
            c.z += crotch_z * (s_leg - 1)
        c.z += dz_thigh * s_leg
        v.co = c
    h.data.update()
    return {"neck": neck, "head": head, "knee": knee, "jaw_plane": [list(jaw_f), list(jaw_b)]}


def _head_height(h):
    """(crown to under-chin height, crown height) of the body mesh, helpers excluded. The chin line is where the midline front
    profile crosses halfway from the neck front to the most forward point of the lower face."""
    neck = _group_centroid(h, "joint-neck")
    head = _group_centroid(h, "joint-head")
    gi = h.vertex_groups["body"].index
    vs = [v.co for v in h.data.vertices if any(g.group == gi for g in v.groups)]

    def front(z):
        s = [c.y for c in vs if abs(c.x) < 0.006 and abs(c.z - z) < 0.004]
        return min(s) if s else 0.0

    nf = front(neck.z)
    cf = min(front(neck.z + 0.005 * i) for i in range(int((head.z - neck.z) / 0.005)))
    z = neck.z
    while front(z) > (nf + cf) / 2 and z < head.z:
        z += 0.002
    top = max(c.z for c in vs)
    return top - z, top  # feet on the ground, as in _scale_to_height


def _solve_head_scale(h, p):
    """Returns the head_scale that gives proportions["heads"] heads after _acg_proportions.

    The head scales rigidly, so the total height grows by (s - 1) * head height and
    heads = (H0 + (s - 1) * hH) / (s * hH)  =>  s = (H0 - hH) / (hH * (heads - 1)).
    The neck blend makes that only approximate, so a few trial passes (vertex edits undone each
    time) refine it by the secant method.
    """
    saved = [v.co.copy() for v in h.data.vertices]

    def trial(s):
        _acg_proportions(h, dict(p, head_scale=s))
        hh, h0 = _head_height(h)
        for v, c in zip(h.data.vertices, saved):
            v.co = c
        h.vertex_groups.remove(h.vertex_groups["acg_head"])
        h.data.update()
        return h0 / hh, hh, h0

    natural, hh, h0 = trial(1.0)
    s0, f0 = 1.0, natural - p["heads"]
    s1 = (h0 - hh) / (hh * (p["heads"] - 1))
    for _ in range(3):
        f1 = trial(s1)[0] - p["heads"]
        if abs(f1) < 0.01 or f1 == f0:
            break
        s0, f0, s1 = s1, f1, s1 - f1 * (s1 - s0) / (f1 - f0)
    print("ACG_HEADS natural=%.2f target=%.2f head_scale=%.3f" % (natural, p["heads"], s1))
    return s1


def _scale_to_height(objs, target_m):
    h = objs[0]
    zs = [(h.matrix_world @ v.co).z for v in h.data.vertices if v.co.z > 0]
    top = max(zs)
    k = target_m / top
    for v in h.data.vertices:
        v.co *= k
    h.data.update()
    return k


def _pose_to_rest(rig):
    """Bakes the current pose into the meshes and makes it the rest pose.

    The T-pose becomes the bind pose: Unity Humanoid expects it, and garments and accessories
    can then be authored in the same space as the turnaround sheets.
    """
    meshes = [o for o in bpy.data.objects if o.type == "MESH" and o.parent is rig]
    for o in meshes:
        mod = next((m for m in o.modifiers if m.type == "ARMATURE"), None)
        if mod is None:
            continue
        bpy.context.view_layer.objects.active = o
        if o.data.shape_keys:
            o.shape_key_clear()
        with bpy.context.temp_override(object=o):
            bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.select_all(action="SELECT")
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    for o in meshes:
        mod = o.modifiers.new("Armature", "ARMATURE")
        mod.object = rig


def _data(*parts):
    return os.path.join(LocationService.get_user_data(), *parts)


def build(spec):
    _clear_scene()
    m = TargetService.get_default_macro_info_dict()
    for k, v in spec["macro"].items():
        if k == "race":
            m["race"].update(v)
        else:
            m[k] = v
    h = HumanService.create_human(scale=0.1, macro_detail_dict=m, feet_on_ground=True)
    h.name = spec["id"]
    for name, w in spec.get("targets", {}).items():
        path = TargetService.target_full_path(name)
        if path is None:
            raise IOError("target not found: " + name)
        TargetService.load_target(h, path, weight=w)
        if not 0.0 <= w <= 1.0:
            # A target is a linear vertex offset; Blender clamps the key to its slider range, so widen it first.
            sk = h.data.shape_keys.key_blocks[-1]
            sk.slider_min, sk.slider_max = min(0.0, w), max(1.0, w)
            sk.value = w
    TargetService.bake_targets(h)

    prop = dict(spec.get("proportions", {}))
    # Head ratio: the macros pick a younger/shorter body (bigger head relative to it, whole-body
    # proportions stay natural); only the remainder up to "heads" comes from a rigid head scale.
    if "heads" in prop:
        prop["head_scale"] = _solve_head_scale(h, prop)
    _acg_proportions(h, prop)
    if "height_scale" in spec:
        # race.py pins the base's factor: a DNA key must not shrink the whole figure to hold its height (height has
        # its own bone DNA).
        for v in h.data.vertices:
            v.co *= spec["height_scale"]
        h.data.update()
    else:
        _scale_to_height([h], spec["height_cm"] / 100.0 * spec.get("height_body_fraction", 1.0))

    ef = spec.get("face", {})
    acg_face.flatten_eye_sockets(h, ef.get("socket_radius", 1.5))
    acg_face.shrink_ears(h, ef.get("ear_scale", 0.75))
    acg_face.simplify_face(h, ef.get("smooth_iter", 20), ef.get("smooth_strength", 0.8))
    acg_face.shape_chin(h, ef.get("chin_forward", 0.011), ef.get("chin_down", 0.012), ef.get("chin_taper", 0.3),
                        cheek=ef.get("cheek_fill", 0.0), anchor=ef.get("chin_anchor", "joint"))

    rig = HumanService.add_builtin_rig(h, spec.get("rig", "game_engine"))
    for part in ("eyebrows", "eyelashes"):
        if part in spec:
            HumanService.add_mhclo_asset(_data(part, spec[part], spec[part] + ".mhclo"), h,
                                         asset_type=part.capitalize(), subdiv_levels=0)
    acg_render.t_pose(rig, spec)
    _pose_to_rest(rig)
    acg_shape.reshape(h, rig, spec)
    acg_parts.dress(h, rig, spec)
    return h, rig


def main():
    a = _args()
    spec = json.load(open(a.spec, encoding="utf-8"))
    if a.bare:
        if a.fbx:
            raise SystemExit("--bare is for checking proportions; do not export it")
        spec["hair"] = None
        spec["outfit"] = []
    h, rig = build(spec)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(a.blend))
    if a.render:
        acg_render.render_views(rig, spec, a.render)
    if acg_render.idle_action(rig, spec) and a.render:
        acg_render.render_views(rig, spec, a.render, views=(("front", 0.0), ("q34", 25.0), ("side", -90.0)),
                                faces=False, prefix="idle_")
    if a.fbx:
        acg_render.export_fbx(rig, a.fbx)
    print("BUILD_OK", spec["id"])


if __name__ == "__main__":
    main()
