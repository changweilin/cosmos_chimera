"""Swing bone chains for hanging cloth (ADR-0013; open-questions Q-T11 → A, own spring bones).

An outfit prim with "swing" gets one or more bone chains hanging from its own bone; its vertices are
re-skinned along the chain so Unity's SpringBones component can swing it with gravity and inertia.
Chain bones are named "sw_<item>_<root>_<i>" (+ "_m" on the mirrored copy) and end in a zero-weight
"..._end" bone that gives the solver the tip position (FBX bones carry no tail).

Spec, on a prim:
  "swing": {"roots": [[dx, dy, dz], ...]   chain starts, metres from the item's "at" centre (T-pose)
            "dir": [x, y, z]               hanging direction in T-pose (default straight down)
            "length": m, "segments": n}
  "swing_with": "<item>"                   reuse that item's chains (trims on a swinging panel)
"""
import bpy
from mathutils import Vector

PREFIX = "sw_"


def _side_swap(bone):
    if bone.endswith("_l"):
        return bone[:-2] + "_r"
    if bone.endswith("_r"):
        return bone[:-2] + "_l"
    return bone


def _chains(body, g, mirrored):
    """[(parent bone, [Vector joint positions root..tip], [bone names])] for one item copy."""
    sw = g["swing"]
    sx = -1.0 if mirrored else 1.0
    bone = g.get("bone", "spine_03")
    parent = _side_swap(bone) if mirrored else bone
    at = body.bone[bone] + Vector(g.get("at", (0, 0, 0)))
    d = Vector(sw.get("dir", (0, 0, -1))).normalized()
    n = sw.get("segments", 3)
    length = sw["length"]
    out = []
    for k, r in enumerate(sw["roots"]):
        root = at + Vector(r)
        if mirrored:
            root.x *= sx
            dd = Vector((d.x * sx, d.y, d.z))
        else:
            dd = d
        joints = [root + dd * (length * i / n) for i in range(n + 1)]
        names = ["%s%s_%d_%d%s" % (PREFIX, g["name"], k, i, "_m" if mirrored else "") for i in range(n)]
        out.append((parent, joints, names))
    return out


def _add_bones(rig, chains):
    inv = rig.matrix_world.inverted()
    eb = rig.data.edit_bones
    for parent, joints, names in chains:
        prev = eb[parent]
        for i, name in enumerate(names):
            b = eb.new(name)
            b.head = inv @ joints[i]
            b.tail = inv @ joints[i + 1]
            b.parent = prev
            b.use_connect = i > 0
            b.roll = 0.0
            prev = b
        end = eb.new(names[-1].rsplit("_", 1)[0] + "_end" if not names[-1].endswith("_m")
                     else names[-1][:-2].rsplit("_", 1)[0] + "_end_m")
        tip = joints[-1]
        end.head = inv @ tip
        end.tail = inv @ (tip + (joints[-1] - joints[-2]).normalized() * 0.01)
        end.parent = prev
        end.use_connect = True


def _ring_chains(body, ob, g, mirrored=False):
    """Chains spaced around the pelvis axis down a flared shell (skirt): joint k of chain c sits on the
    shell's outer surface at angle c and height top..hem. Spec "swing": {"ring": count, "segments": n,
    "from": z-expr (where swinging starts)}; the hem is the shell's lowest point."""
    import math
    sw = g["swing"]
    count, n = sw["ring"], sw.get("segments", 2)
    centre = body.bone["pelvis"]
    vs = [ob.matrix_world @ v.co for v in ob.data.vertices]
    top = body.zexpr(sw["from"])
    hem = min(v.z for v in vs)
    out = []
    for c in range(count):
        a = 2 * math.pi * c / count
        dirn = Vector((math.cos(a), math.sin(a), 0))
        joints = []
        for i in range(n + 1):
            z = top + (hem - top) * i / n
            band = [v for v in vs if abs(v.z - z) < 0.02]
            rad = [((v - centre) * Vector((1, 1, 0))).dot(dirn) for v in band
                   if ((v - centre) * Vector((1, 1, 0))).normalized().dot(dirn) > 0.92]
            r = max(rad) if rad else 0.12
            joints.append(Vector((centre.x, centre.y, z)) + dirn * r)
        names = ["%s%s_%d_%d" % (PREFIX, g["name"], c, i) for i in range(n)]
        out.append(("pelvis", joints, names))
    return out


def _reskin_ring(ob, chains, top):
    """Skirt weights: below "top" each vertex follows the two chains nearest in angle, by height along
    them; above it the body weights stay; a 4 cm band blends the two."""
    import math
    n = len(chains[0][2])
    cx = sum(c[1][0].x for c in chains) / len(chains)
    cy = sum(c[1][0].y for c in chains) / len(chains)
    hem = chains[0][1][-1].z
    count = len(chains)
    names = {vg.index: vg.name for vg in ob.vertex_groups}
    old = {v.index: {names[g.group]: g.weight for g in v.groups} for v in ob.data.vertices}
    for vg in list(ob.vertex_groups):
        ob.vertex_groups.remove(vg)
    groups = {}

    def add(name, idx, w):
        if w <= 1e-4:
            return
        if name not in groups:
            groups[name] = ob.vertex_groups.get(name) or ob.vertex_groups.new(name=name)
        groups[name].add([idx], w, "ADD")

    for v in ob.data.vertices:
        co = ob.matrix_world @ v.co
        blend = max(0.0, min(1.0, (top - co.z) / 0.04))
        for b, w in old[v.index].items():
            add(b, v.index, w * (1 - blend))
        if blend <= 0:
            continue
        a = math.atan2(co.y - cy, co.x - cx) % (2 * math.pi)
        t = a / (2 * math.pi) * count
        k0 = int(t) % count
        f = t - int(t)
        s = max(0.0, min(0.999, (top - co.z) / max(top - hem, 1e-4))) * n
        i = int(s)
        for k, wk in ((k0, 1 - f), ((k0 + 1) % count, f)):
            add(chains[k][2][i], v.index, blend * wk)


def _reskin(ob, chains):
    """Weights along each chain: bone i owns its span, blended half a span into its neighbours
    (the item's own bone before the first span); several roots blend by position across them."""
    parent = chains[0][0]
    n = len(chains[0][2])
    axis = None
    if len(chains) > 1:
        a, b = chains[0][1][0], chains[-1][1][0]
        axis = (b - a)
    for vg in list(ob.vertex_groups):
        ob.vertex_groups.remove(vg)
    groups = {}

    def add(name, idx, w):
        if w <= 1e-4:
            return
        if name not in groups:
            groups[name] = ob.vertex_groups.new(name=name)
        groups[name].add([idx], w, "ADD")

    for v in ob.data.vertices:
        co = v.co
        if axis is None:
            mix = [(0, 1.0)]
        else:
            t = max(0.0, min(1.0, (co - chains[0][1][0]).dot(axis) / axis.length_squared)) * (len(chains) - 1)
            k = min(int(t), len(chains) - 2)
            f = t - k
            mix = [(k, 1.0 - f), (k + 1, f)]
        for k, wk in mix:
            _p, joints, names = chains[k]
            d = joints[-1] - joints[0]
            s = max(0.0, min(0.999, (co - joints[0]).dot(d) / d.length_squared)) * n
            i = int(s)
            f = s - i
            # Bone i dominates its span; within the first half it shares with the bone above.
            if f < 0.5:
                add(names[i], v.index, wk * (0.5 + f))
                add(names[i - 1] if i > 0 else parent, v.index, wk * (0.5 - f))
            else:
                add(names[i], v.index, wk)


def build(body, items, spec):
    """items: [(g, ob, mirror_ob or None)] from acg_outfit. Adds the chains to the rig and re-skins.

    Pre: object mode, rig in T-pose rest. Post: every swing / swing_with item is weighted to its chains.
    Side effects: adds bones to the armature.
    """
    by_name = {g["name"]: (g, ob, m2) for g, ob, m2 in items}
    plans = []
    rings = []
    for g, ob, m2 in items:
        if "ring" in g.get("swing", {}) or "ring" in by_name.get(g.get("swing_with"), ({},))[0].get("swing", {}):
            rings.append((by_name[g.get("swing_with", g["name"])][0], g, ob))
            continue
        src = g if "swing" in g else (by_name[g["swing_with"]][0] if "swing_with" in g else None)
        if src is None:
            continue
        for target, mirrored in ((ob, False), (m2, True)):
            if target is not None:
                plans.append((src, target, mirrored))
    if not plans and not rings:
        return
    rig = body.rig
    made = {}
    ring_made = {}
    for src, _g, _ob in rings:
        if src["name"] not in ring_made:
            ring_made[src["name"]] = _ring_chains(body, by_name[src["name"]][1], src)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for chains in ring_made.values():
        _add_bones(rig, chains)
    for src, _t, mirrored in plans:
        key = (src["name"], mirrored)
        if key not in made:
            made[key] = _chains(body, src, mirrored)
            _add_bones(rig, made[key])
    bpy.ops.object.mode_set(mode="OBJECT")
    for src, target, mirrored in plans:
        _reskin(target, made[(src["name"], mirrored)])
    for src, _g, ob in rings:
        _reskin_ring(ob, ring_made[src["name"]], body.zexpr(src["swing"]["from"]))
    print("ACG_SWING", spec["id"], sorted("%s%s" % (k[0], "_m" if k[1] else "") for k in made) + sorted(ring_made))


def hair(rig, ob, chains_by_group):
    """Hair swing: chains_by_group = [(name, [Vector joints root..tip], (first, last) vertex range,
    segments)]. Adds "sw_hair_<name>_*" chains under the head and weights each group's vertices by
    their position along the strand (the "hair_u" attribute, 0 root .. 1 tip).
    Pre: object mode; ob skinned 100% to "head". Side effects: adds bones, edits ob's vertex groups."""
    chains = []
    for name, joints, _rng, n in chains_by_group:
        names = ["%shair_%s_0_%d" % (PREFIX, name, i) for i in range(n)]
        chains.append(("head", joints, names))
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    _add_bones(rig, chains)
    bpy.ops.object.mode_set(mode="OBJECT")
    hu = ob.data.attributes["hair_u"].data
    head = ob.vertex_groups["head"]
    for (name, _j, (a, b), n), (_p, _jj, names) in zip(chains_by_group, chains):
        groups = [ob.vertex_groups.get(x) or ob.vertex_groups.new(name=x) for x in names]
        for idx in range(a, b):
            s = min(0.999, hu[idx].value) * n
            i = int(s)
            f = s - i
            head.remove([idx])
            if f < 0.5:
                groups[i].add([idx], 0.5 + f, "REPLACE")
                if i > 0:
                    groups[i - 1].add([idx], 0.5 - f, "REPLACE")
                else:
                    head.add([idx], 0.5 - f, "REPLACE")
            else:
                groups[i].add([idx], 1.0, "REPLACE")
    print("ACG_SWING_HAIR", [c[0] for c in chains_by_group])


def settle(rig, down=(0.0, 0.0, -1.0)):
    """Static stand-in for the runtime spring: points every swing bone straight down (parents first),
    so Blender previews of a pose show hanging cloth. Pre: object mode, QUATERNION bones."""
    d = Vector(down)
    for pb in rig.pose.bones:
        if not pb.name.startswith(PREFIX) or "_end" in pb.name:
            continue
        bpy.context.view_layer.update()
        m = pb.matrix.copy()
        cur = (pb.tail - pb.head).normalized()
        # Chains already hanging within 45° of down (skirt, ponytail) keep their modelled shape.
        if cur.dot(d) > 0.7:
            continue
        q = cur.rotation_difference(d)
        head = m.translation.copy()
        from mathutils import Matrix
        pb.matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ m
    bpy.context.view_layer.update()
