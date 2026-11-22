"""Procedural outfit pieces (ADR-0013).

Garments are shells cut from the body mesh, so they inherit the body's bone weights and deform
with the rig without re-skinning. Accessories are primitives skinned 100% to one bone, except
"soft" (fabric) ones, which copy blended weights from the nearest body surface so they bend
across joints with the body instead of riding one bone rigidly.
Landmark expressions such as "knee+0.04" are metres relative to rest-pose bone heads.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

import acg_parts

LANDMARKS = {
    "pelvis": "pelvis", "waist": "spine_01", "chest": "spine_03", "neck": "neck_01", "head": "head",
    "hip": "thigh_l", "knee": "calf_l", "ankle": "foot_l", "toe": "ball_l",
    "shoulder": "upperarm_l", "elbow": "lowerarm_l", "wrist": "hand_l",
}


class Body:
    def __init__(self, h, rig):
        self.h, self.rig = h, rig
        self.bone = {b.name: rig.matrix_world @ b.head_local for b in rig.data.bones}
        self.tail = {b.name: rig.matrix_world @ b.tail_local for b in rig.data.bones}
        self.z = {k: self.bone[v].z for k, v in LANDMARKS.items()}
        names = [g.name for g in h.vertex_groups]
        self.w = []
        for v in h.data.vertices:
            self.w.append({names[g.group]: g.weight for g in v.groups if g.weight > 0.01})
        self._bvh = None

    def skin_weights(self, co):
        """Bone weights at the body surface nearest to co: the nearest face's vertex weights,
        inverse-distance blended, at most 4 bones, normalised (Unity skins 4 per vertex)."""
        if self._bvh is None:
            vs = self.h.data.vertices
            self._polys = [list(p.vertices) for p in self.h.data.polygons
                           if all("body" in self.w[i] for i in p.vertices)]
            self._bvh = BVHTree.FromPolygons([v.co for v in vs], self._polys)
            self._bones = set(self.rig.data.bones.keys())
        loc, _n, idx, _d = self._bvh.find_nearest(co)
        acc = {}
        vs = self.h.data.vertices
        for i in self._polys[idx]:
            k = 1.0 / max((vs[i].co - loc).length, 1e-4)
            for b, x in self.w[i].items():
                if b in self._bones:
                    acc[b] = acc.get(b, 0.0) + k * x
        top = sorted(acc.items(), key=lambda t: -t[1])[:4]
        tot = sum(x for _b, x in top)
        return {b: x / tot for b, x in top}

    def zexpr(self, e):
        if isinstance(e, (int, float)):
            return float(e)
        for op in ("+", "-"):
            if op in e[1:]:
                i = e.rindex(op)
                k = float(e[i + 1:])
                return self.z[e[:i]] + (k if op == "+" else -k)
        return self.z[e]

    def arm_frac(self, co, side):
        """0 at the shoulder joint, 1 at the wrist, along the rest-pose arm."""
        a = self.bone["upperarm_" + side]
        b = self.bone["hand_" + side]
        d = b - a
        return (co - a).dot(d) / d.length_squared


def _bones_weight(w, names):
    return sum(w.get(n, 0.0) for n in names)


TORSO = ("pelvis", "spine_01", "spine_02", "spine_03", "clavicle_l", "clavicle_r", "neck_01")
ARM_L = ("upperarm_l", "lowerarm_l")
ARM_R = ("upperarm_r", "lowerarm_r")
LEG_L = ("thigh_l", "calf_l")
LEG_R = ("thigh_r", "calf_r")
HAND = ("hand_l", "hand_r")
FINGERS = tuple("%s_0%d_%s" % (f, i, s) for f in ("thumb", "index", "middle", "ring", "pinky")
                for i in (1, 2, 3) for s in ("l", "r"))
FOOT = ("foot_l", "foot_r", "ball_l", "ball_r")


def _region(body, g):
    """Vertex predicate for a garment spec."""
    parts = set(g.get("parts", []))
    zlo = body.zexpr(g["z"][0]) if "z" in g else -1e9
    zhi = body.zexpr(g["z"][1]) if "z" in g else 1e9
    sleeve = g.get("sleeve")  # [from, to] along the arm, 0 shoulder .. 1 wrist
    open_front = g.get("open_front")  # half-width of the front opening, metres
    groups = []
    if "torso" in parts:
        groups.append(TORSO)
    if "legs" in parts:
        groups += [LEG_L, LEG_R]
    if "hands" in parts:
        groups.append(HAND)
    if "feet" in parts:
        groups += [FOOT, ("calf_l", "calf_r")]

    def pred(w, co):
        if "body" not in w:
            return False
        inside = False
        if groups and zlo <= co.z <= zhi:
            for gr in groups:
                if _bones_weight(w, gr) > 0.5:
                    inside = True
                    break
            if "hands" in parts and _bones_weight(w, FINGERS) > g.get("finger_cut", 0.35):
                inside = False
        if sleeve and not inside:
            for side, arm in (("l", ARM_L), ("r", ARM_R)):
                wa = _bones_weight(w, arm)
                wt = _bones_weight(w, TORSO) if "torso" in parts else 0.0
                # Shoulder seam: vertices split between torso and arm count as garment when the
                # union is dominant, otherwise the seam tears open.
                if wa > 0.05 and wa + wt > 0.5:
                    t = body.arm_frac(co, side)
                    if t <= sleeve[1] and (sleeve[0] <= 0 or t >= sleeve[0]) and (wt < 0.3 or co.z <= zhi):
                        inside = True
        if inside and open_front and co.y < body.bone["spine_03"].y and abs(co.x) < open_front                 and co.z > body.zexpr(g.get("open_from", "pelvis")):
            inside = False
        return inside
    return pred


# Bone pairs whose shared joint bunches fabric when it bends: (parent, child) per side.
FOLD_JOINTS = [("upperarm_%s", "lowerarm_%s"), ("clavicle_%s", "upperarm_%s"), ("thigh_%s", "calf_%s"),
               ("pelvis", "thigh_%s"), ("pelvis", "spine_01"), ("spine_01", "spine_02")]


def _soft_folds(body, g, bm, dl, names):
    """Cloth folds: shallow ripples where two bones share a vertex (the fabric bunches at the joint)
    and a soft wave along flared hems, so shells read as fabric instead of painted skin.
    g["folds"] = ripple depth in metres (default 0.0045, 0 disables)."""
    amp = g.get("folds", 0.0045)
    if not amp:
        return
    pairs = []
    for a, b in FOLD_JOINTS:
        for side in (("l", "r") if "%s" in a or "%s" in b else ("",)):
            pa = a % side if "%s" in a else a
            pb = b % side if "%s" in b else b
            if pb in body.bone:
                axis = (body.tail[pb] - body.bone[pb]).normalized()
                pairs.append((pa, pb, body.bone[pb], axis.normalized()))
    hem = None
    if g.get("flare") and "z" in g:
        hem = body.zexpr(g["z"][0])
    period = 0.03
    for v in bm.verts:
        w = {names[k]: x for k, x in v[dl].items() if x > 0.01}
        d = 0.0
        for pa, pb, joint, axis in pairs:
            zone = min(1.0, 4.0 * w.get(pa, 0.0) * w.get(pb, 0.0))
            if zone <= 0.0:
                continue
            rel = v.co - joint
            along = rel.dot(axis)
            # Ripples run around the limb but break up with the angle so they read as folds, not rings.
            radial = (rel - axis * along)
            ref = axis.orthogonal()
            ang = math.atan2(radial.dot(axis.cross(ref)), radial.dot(ref))
            d += amp * zone * (0.5 + 0.5 * math.sin(2 * math.pi * along / period + 2.0 * math.sin(3 * ang)))
        if hem is not None:
            u = 1.0 - min(1.0, max(0.0, (v.co.z - hem) / 0.08))
            cx = 0.0 if g["flare"].get("radial") else math.copysign(abs(body.bone["calf_l"].x), v.co.x)
            ang = math.atan2(v.co.y - body.bone["pelvis"].y, v.co.x - cx)
            d += 2.5 * amp * u * (0.5 + 0.5 * math.sin(7 * ang))
        if d:
            v.co += v.normal * d


def shell(body, g, mat, spec):
    """Copies the body faces selected by g, offsets them outward and adds cloth thickness."""
    h = body.h
    pred = _region(body, g)
    me = h.data.copy()
    ob = bpy.data.objects.new(g["name"] + "_" + spec["id"], me)
    bpy.context.scene.collection.objects.link(ob)
    if not ob.vertex_groups:
        for vg in h.vertex_groups:
            ob.vertex_groups.new(name=vg.name)
    bm = bmesh.new()
    bm.from_mesh(me)
    # Cut exactly at the hem heights so edges are straight instead of following the quad grid.
    if "z" in g:
        for zc in (body.zexpr(g["z"][0]), body.zexpr(g["z"][1])):
            geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
            bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, zc), plane_no=(0, 0, 1))
    # Trims and patches: "x": [lo, hi] keeps |x| in range (character left is +x; "x_side": "l"/"r"
    # keeps one side); "face": "front"/"back" keeps faces whose normal points that way.
    xr = g.get("x")
    if xr:
        for xc in (xr[0], xr[1], -xr[0], -xr[1]):
            geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
            bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(xc, 0, 0), plane_no=(1, 0, 0))
    bm.normal_update()
    bm.verts.ensure_lookup_table()
    dl = bm.verts.layers.deform.active
    names = [vg.name for vg in h.vertex_groups]
    eps = 1e-5
    zl = body.zexpr(g["z"][0]) - eps if "z" in g else None
    zh = body.zexpr(g["z"][1]) + eps if "z" in g else None
    keep = {}
    for v in bm.verts:
        w = {names[k]: x for k, x in v[dl].items() if x > 0.01}
        co = v.co.copy()
        if zl is not None:
            co.z = min(max(co.z, zl + 2 * eps), zh - 2 * eps) if zl <= co.z <= zh else co.z
        k = pred(w, co)
        if k and xr:
            ax = abs(co.x)
            k = xr[0] - eps <= ax <= xr[1] + eps and (
                g.get("x_side") is None or (co.x >= 0) == (g["x_side"] == "l"))
        if k and g.get("face"):
            k = v.normal.y < -0.2 if g["face"] == "front" else v.normal.y > 0.2
        keep[v] = k
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not keep[v]], context="VERTS")
    bm.normal_update()
    off = g.get("offset", 0.006)
    flare = g.get("flare")  # {"from": z-expr, "to": z-expr, "amount": metres}
    if flare:
        f0, f1 = body.zexpr(flare["from"]), body.zexpr(flare["to"])
    for v in bm.verts:
        d = off
        if flare:
            u = min(1.0, max(0.0, (v.co.z - f0) / (f1 - f0)))
            d += flare["amount"] * u ** flare.get("pow", 1.5)
            if flare.get("radial"):
                c = Vector((0.0, body.bone["pelvis"].y, v.co.z))
                rad = (v.co - c)
                rad.z = 0
                if rad.length > 1e-6:
                    v.co += rad.normalized() * flare["amount"] * u ** flare.get("pow", 1.5)
                    d = off
        v.co += v.normal * d
    _soft_folds(body, g, bm, dl, names)
    bm.to_mesh(me)
    bm.free()
    ob.data.materials.clear()
    ob.data.materials.append(mat)
    ob.parent = body.rig
    sol = ob.modifiers.new("acg_cloth", "SOLIDIFY")
    sol.thickness = g.get("thickness", 0.003)
    sol.offset = -1
    arm = ob.modifiers.new("Armature", "ARMATURE")
    arm.object = body.rig
    ob.modifiers.move(1, 0)
    if g.get("smooth", 0):
        sm = ob.modifiers.new("acg_smooth", "SMOOTH")
        sm.iterations = g["smooth"]
        ob.modifiers.move(len(ob.modifiers) - 1, 1)
    for p in me.polygons:
        p.use_smooth = True
    return ob


def _skin_to_bone(ob, rig, bone):
    vg = ob.vertex_groups.new(name=bone)
    vg.add(list(range(len(ob.data.vertices))), 1.0, "REPLACE")
    ob.parent = rig
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = rig


def _skin_from_body(ob, body, parent=True):
    """Skins a fabric primitive with the body's blended weights (see Body.skin_weights)."""
    groups = {}
    for v in ob.data.vertices:
        for b, x in body.skin_weights(v.co).items():
            if b not in groups:
                groups[b] = ob.vertex_groups.get(b) or ob.vertex_groups.new(name=b)
            groups[b].add([v.index], x, "REPLACE")
    if parent:
        ob.parent = body.rig
        mod = ob.modifiers.new("Armature", "ARMATURE")
        mod.object = body.rig


def _apply(ob):
    """Bakes primitive transforms into the mesh so the object sits at the rig origin."""
    ob.data.transform(ob.matrix_basis)
    ob.matrix_basis = Matrix()


def prim(body, g, mat, spec):
    """Rounded box, cylinder, torus or sphere placed relative to a bone head."""
    kind = g["prim"]
    bone = g.get("bone", "spine_03")
    at = body.bone[bone] + Vector(g.get("at", (0, 0, 0)))
    size = Vector(g.get("size", (0.1, 0.1, 0.1)))
    rot = [math.radians(a) for a in g.get("rot", (0, 0, 0))]
    me = bpy.data.meshes.new(g["name"])
    bm = bmesh.new()
    if kind == "box":
        bmesh.ops.create_cube(bm, size=1.0)
        bev = g.get("bevel", 0.25)
        if bev:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bev * 0.5, segments=3, affect="EDGES",
                            profile=0.5)
        if "hem_rise" in g:
            # Lift only the body-side corner of a hanging sleeve, keeping the arm seam level.
            for v in bm.verts:
                v.co.z += (0.5 - v.co.x) * (0.5 - v.co.z) * g["hem_rise"] / size.z
    elif kind == "cyl":
        # "open": no end caps, for tube-like garments (a skirt cone) that get thickness from Solidify below.
        bmesh.ops.create_cone(bm, cap_ends=not g.get("open"), segments=g.get("segments", 24), radius1=0.5,
                              radius2=0.5 * g.get("taper", 1.0), depth=1.0)
    elif kind == "torus":
        r1, r2 = g.get("radii", (0.5, 0.08))
        segs, rs = 32, 8
        vs = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            for j in range(rs):
                b = 2 * math.pi * j / rs
                vs.append(bm.verts.new(((r1 + r2 * math.cos(b)) * math.cos(a), (r1 + r2 * math.cos(b)) * math.sin(a),
                                        r2 * math.sin(b))))
        for i in range(segs):
            for j in range(rs):
                a = vs[i * rs + j]
                b = vs[i * rs + (j + 1) % rs]
                c = vs[((i + 1) % segs) * rs + (j + 1) % rs]
                d = vs[((i + 1) % segs) * rs + j]
                bm.faces.new((a, d, c, b))
    elif kind == "ribbon":
        # Folded cloth has a raised spine. The pinched end is at the top, against the knot.
        rows = []
        for z, half_width in ((-0.5, 0.04), (-0.33, 0.5), (0.14, 0.39), (0.5, 0.12)):
            rows.append([bm.verts.new((x * half_width, y, z)) for x, y in
                         ((-1, 0), (0, -0.5), (1, 0), (0, 0.5))])
        for lower, upper in zip(rows, rows[1:]):
            for j in range(4):
                k = (j + 1) % 4
                bm.faces.new((lower[j], lower[k], upper[k], upper[j]))
        bm.faces.new(tuple(reversed(rows[0])))
        bm.faces.new(rows[-1])
    elif kind == "hood":
        # Open horseshoe, high at the nape and rolled out around each clavicle.
        rows = []
        for i in range(33):
            a = math.radians(25 + 310 * i / 32)
            lift = (1 - math.cos(a)) * 0.5
            row = []
            for radius, z in ((0.31, -0.46), (0.53, 0.5), (0.56, 0.43), (0.37, -0.5)):
                row.append(bm.verts.new((radius * math.sin(a), -radius * math.cos(a),
                                         z - (1 - lift) * 0.42)))
            rows.append(row)
        for left, right in zip(rows, rows[1:]):
            for j in range(4):
                k = (j + 1) % 4
                face = bm.faces.new((left[j], left[k], right[k], right[j]))
                face.material_index = int(j == 0)
        bm.faces.new(rows[0])
        bm.faces.new(tuple(reversed(rows[-1])))
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    elif kind == "paw":
        # A single weighted applique keeps the pads together when the sleeve swings.
        for x, y, width, height in ((0, -0.19, 0.66, 0.55), (-0.35, 0.15, 0.23, 0.32),
                                    (0, 0.32, 0.24, 0.34), (0.35, 0.15, 0.23, 0.32)):
            pad = bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=8, radius=0.5)
            for v in pad["verts"]:
                v.co.x = x + v.co.x * width
                v.co.y = y + v.co.y * height
    elif kind == "sphere":
        bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=16, radius=0.5)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new(g["name"] + "_" + spec["id"], me)
    bpy.context.scene.collection.objects.link(ob)
    ob.scale = size
    ob.rotation_euler = rot
    ob.location = at
    _apply(ob)
    ob.data.materials.append(mat)
    if kind == "hood":
        ob.data.materials.append(acg_parts.toon_material("hoodlining_" + spec["id"], g["lining"]))
    if g.get("open"):
        sol = ob.modifiers.new("acg_cloth", "SOLIDIFY")
        sol.thickness = g.get("thickness", 0.004)
        sol.offset = 0
    if g.get("soft"):
        _skin_from_body(ob, body)
    else:
        _skin_to_bone(ob, body.rig, bone)
    if g.get("mirror"):
        m2 = ob.copy()
        m2.data = ob.data.copy()
        m2.data.transform(Matrix.Scale(-1, 4, Vector((1, 0, 0))))
        m2.data.flip_normals()
        m2.name = ob.name + "_m"
        bpy.context.scene.collection.objects.link(m2)
        side = bone[-2:]
        if side in ("_l", "_r"):
            other = bone[:-2] + ("_r" if side == "_l" else "_l")
            m2.vertex_groups.clear()
            if g.get("soft"):
                _skin_from_body(m2, body, parent=False)
            else:
                vg = m2.vertex_groups.new(name=other)
                vg.add(list(range(len(m2.data.vertices))), 1.0, "REPLACE")
    return ob


def build(h, rig, spec):
    out = []
    items = []
    body = Body(h, rig)
    for g in spec.get("outfit", []):
        # Fabric gets a soft mid-tone step; hard accessories keep the crisp two-tone cel.
        fabric = "prim" not in g or g.get("soft", False)
        mat = acg_parts.toon_material("%s_%s" % (g["name"], spec["id"]), g["color"], g.get("shade"), soft=fabric)
        if "prim" in g:
            ob = prim(body, g, mat, spec)
            out.append(ob)
            items.append((g, ob, bpy.data.objects.get(ob.name + "_m")))
        else:
            ob = shell(body, g, mat, spec)
            out.append(ob)
            items.append((g, ob, None))
    import acg_swing
    acg_swing.build(body, items, spec)
    tuck_body(body, spec.get("outfit", []), spec.get("tuck", 0.012))
    return out


def tuck_body(body, garments, depth):
    """Pulls skin that a full-coverage shell hides inward, so the body stays inside its clothes
    when joints bend (the shell is offset only millimetres, and smoothing pulls it back toward
    the skin). Trims ("x"/"face") and prims cover too little to count; the covered region is
    eroded two vertex rings so hems never open onto a sunken body.

    Pre: shells built from the same rest-pose body. Side effects: edits h.data vertex positions.
    """
    if not depth:
        return
    h = body.h
    vs = h.data.vertices
    covered = set()
    for g in garments:
        if "prim" in g or g.get("x") or g.get("face"):
            continue
        pred = _region(body, g)
        for i, v in enumerate(vs):
            if i not in covered and pred(body.w[i], v.co):
                covered.add(i)
    nbr = {i: set() for i in covered}
    for e in h.data.edges:
        a, b = e.vertices
        if a in nbr:
            nbr[a].add(b)
        if b in nbr:
            nbr[b].add(a)
    for _ in range(2):
        covered = {i for i in covered if nbr[i] <= covered}
    for i in covered:
        vs[i].co -= vs[i].normal * depth
    h.data.update()
    print("ACG_TUCK", len(covered))
