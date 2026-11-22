"""Hand-held props for ACG characters (ADR-0013): Rubik's cube, glass bead, talisman.

Every character gets all three, skinned 100% to hand_l, so any of them can be swapped in.
Only spec["props"]["hold"] is visible; the others are exported hidden with the FBX user property
acg_hidden=1, which Unity's AcgPropImport postprocessor turns into a disabled renderer.
Placement is derived from the rest-pose hand bones, so it follows each body's hand size.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

import acg_parts

KINDS = ("cube", "bead", "talisman")

CUBE_FACES = ("#f4f4f0", "#f0c020", "#f07820", "#d83030", "#30a050", "#2a70d0")


def _frame(rig):
    """Grip frame in rest pose: origin at the base of the middle finger, u along the fingers,
    n out of the palm (the side the fingers curl toward), v completes the right-handed frame."""
    b = rig.data.bones
    mw = rig.matrix_world
    root = mw @ b["middle_01_l"].head_local
    tip = mw @ b["middle_03_l"].tail_local
    u = (tip - root).normalized()
    n = (mw.to_3x3() @ b["middle_01_l"].matrix_local.col[2].to_3d()).normalized()
    n = (n - u * n.dot(u)).normalized()
    v = n.cross(u)
    return root, u, v, n


def _box(bm, centre, size, frame):
    o, u, v, n = frame
    m = Matrix((u, v, n)).transposed()
    geom = bmesh.ops.create_cube(bm, size=1.0)["verts"]
    for vert in geom:
        c = vert.co
        vert.co = o + m @ Vector((c.x * size[0] + centre[0], c.y * size[1] + centre[1], c.z * size[2] + centre[2]))
    return geom


def _sphere(bm, centre, r, frame):
    o, u, v, n = frame
    m = Matrix((u, v, n)).transposed()
    geom = bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=16, radius=r)["verts"]
    for vert in geom:
        vert.co = o + m @ (vert.co + Vector(centre))
    return geom


def _mesh(name, parts, mats):
    """parts: [(material index, builder(bm))]. One object, one material slot per index."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    for mi, fn in parts:
        before = set(bm.faces)
        fn(bm)
        for f in set(bm.faces) - before:
            f.material_index = mi
            f.smooth = True
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    return me


def _cube(spec, frame, s):
    """3x3 puzzle cube with s edge length, held in front of the palm."""
    h = s / 2
    c = (0.4 * s, 0.0, h + 0.006)
    mats = [acg_parts.toon_material("propcube_core_" + spec["id"], "#1a1a1e")] + [
        acg_parts.toon_material("propcube_%d_%s" % (i, spec["id"]), col) for i, col in enumerate(CUBE_FACES)]
    parts = [(0, lambda bm: _box(bm, c, (s, s, s), frame))]
    cell = s / 3
    st = cell * 0.82
    th = s * 0.02
    for fi, (axis, sign) in enumerate(((0, 1), (0, -1), (1, 1), (1, -1), (2, 1), (2, -1))):
        for i in (-1, 0, 1):
            for j in (-1, 0, 1):
                ctr = list(c)
                size = [st, st, st]
                ctr[axis] += sign * (h + th / 2)
                size[axis] = th
                a, b = [k for k in range(3) if k != axis]
                ctr[a] += i * cell
                ctr[b] += j * cell
                parts.append((fi + 1, lambda bm, ctr=tuple(ctr), size=tuple(size): _box(bm, ctr, size, frame)))
    return parts, mats


def _bead(spec, frame, s):
    """Glass bead: a pale-blue sphere with a white glint and a darker inner swirl."""
    r = s / 2
    c = Vector((0.35 * s, 0.0, r + 0.006))
    mats = [acg_parts.toon_material("propbead_glass_" + spec["id"], "#8fd0f4", "#3c7cc0"),
            acg_parts.toon_material("propbead_glint_" + spec["id"], "#ffffff"),
            acg_parts.toon_material("propbead_core_" + spec["id"], "#4060c8", "#283a90")]
    parts = [(0, lambda bm: _sphere(bm, c, r, frame)),
             (1, lambda bm: _sphere(bm, c + Vector((0.3 * r, -0.35 * r, 0.75 * r)), 0.2 * r, frame)),
             (2, lambda bm: _sphere(bm, c + Vector((-0.1 * r, 0.1 * r, 0.25 * r)), 0.45 * r, frame))]
    return parts, mats


def _talisman(spec, frame, s):
    """Paper talisman (符籙) pinched at its lower end: yellow paper, red border and script strokes."""
    w, length, t = s * 0.55, s, 0.0015
    base = (length * 0.42, 0.0, 0.012)
    mats = [acg_parts.toon_material("proptalisman_paper_" + spec["id"], "#f2d878", "#c8a848"),
            acg_parts.toon_material("proptalisman_ink_" + spec["id"], "#c02020")]

    def at(du, dv, sz, dz=t):
        ctr = (base[0] + du, base[1] + dv, base[2] + dz)
        return lambda bm: _box(bm, ctr, sz, frame)

    parts = [(0, at(0, 0, (length, w, t), 0))]
    b = 0.004
    for sgn in (-1, 1):
        parts.append((1, at(sgn * (length / 2 - b), 0, (b, w - b, t))))
        parts.append((1, at(0, sgn * (w / 2 - b), (length - b, b, t))))
    # Script: a spine with cross strokes, the usual talisman layout read along its length.
    parts.append((1, at(0.04 * length, 0, (length * 0.62, 0.006, t))))
    for k in (-0.22, -0.05, 0.12, 0.28):
        parts.append((1, at(k * length, 0, (0.005, w * 0.55, t))))
    return parts, mats


BUILDERS = {"cube": _cube, "bead": _bead, "talisman": _talisman}
SIZES = {"cube": 0.056, "bead": 0.05, "talisman": 0.13}


def build(h, rig, spec):
    """Builds every prop for the character; returns {kind: object}.

    Pre: rig in its T-pose rest; spec["props"] optional ({"hold": kind, "size": {kind: m}}).
    Post: objects "prop_<kind>_<id>" parented to the rig, skinned to hand_l; all but "hold" hidden.
    """
    cfg = spec.get("props") or {}
    hold = cfg.get("hold")
    if hold is not None and hold not in KINDS:
        raise ValueError("props.hold must be one of %s, got %r" % (KINDS, hold))
    frame = _frame(rig)
    out = {}
    for kind in KINDS:
        size = cfg.get("size", {}).get(kind, SIZES[kind])
        parts, mats = BUILDERS[kind](spec, frame, size)
        ob = bpy.data.objects.new("prop_%s_%s" % (kind, spec["id"]), _mesh("prop_" + kind, parts, mats))
        bpy.context.scene.collection.objects.link(ob)
        vg = ob.vertex_groups.new(name="hand_l")
        vg.add(list(range(len(ob.data.vertices))), 1.0, "REPLACE")
        ob.parent = rig
        mod = ob.modifiers.new("Armature", "ARMATURE")
        mod.object = rig
        ob.hide_viewport = ob.hide_render = kind != hold
        # Unity ignores FBX Visibility on skinned meshes; AcgPropImport reads these user properties.
        ob["acg_prop"] = kind
        ob["acg_hidden"] = int(kind != hold)
        out[kind] = ob
    return out
