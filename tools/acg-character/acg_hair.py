"""Procedural anime hair: a cap shell plus tapered lens-section clumps (ADR-0013).

Directions: the character faces -Y, +X is the character's left, Z is up. Azimuth 0 = front,
90 = character's left, 180 = back. Elevation 0 = head-centre height, 90 = crown.
"""
import math
import random

import bpy
from mathutils import Vector

import acg_parts


GRADIENT_WIDTH = 64


class Head:
    """Ellipsoid fitted to the rest-pose cranium; clumps root on it and are kept outside it."""

    def __init__(self, h):
        gi = h.vertex_groups["acg_head"].index
        eye = h.vertex_groups["joint-l-eye"].index
        eyes = [v.co for v in h.data.vertices if any(g.group == eye for g in v.groups)]
        self.eye_z = sum(e.z for e in eyes) / len(eyes)
        pts = [v.co for v in h.data.vertices
               if any(g.group == gi and g.weight > 0.95 for g in v.groups) and v.co.z > self.eye_z - 0.02]
        xs = [p.x for p in pts]
        ys = [p.y for p in pts]
        zs = [p.z for p in pts]
        self.c = Vector((0.0, (min(ys) + max(ys)) / 2, self.eye_z))
        self.r = Vector(((max(xs) - min(xs)) / 2, (max(ys) - min(ys)) / 2, max(zs) - self.eye_z))

    def point(self, az, el, k=1.0):
        a, e = math.radians(az), math.radians(el)
        d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        return self.c + Vector((d.x * self.r.x, d.y * self.r.y, d.z * self.r.z)) * k

    def normal(self, p):
        q = p - self.c
        return Vector((q.x / self.r.x ** 2, q.y / self.r.y ** 2, q.z / self.r.z ** 2)).normalized()

    def inside(self, p, k):
        q = p - self.c
        return (q.x / (self.r.x * k)) ** 2 + (q.y / (self.r.y * k)) ** 2 + (q.z / (self.r.z * k)) ** 2 < 1.0

    def push_out(self, p, k):
        if not self.inside(p, k):
            return p
        q = p - self.c
        s = math.sqrt((q.x / (self.r.x * k)) ** 2 + (q.y / (self.r.y * k)) ** 2 + (q.z / (self.r.z * k)) ** 2)
        return self.c + q / max(s, 1e-6)


FLOWS = {
    "down": Vector((0, 0, -1)),
    "back": Vector((0, 0.6, -0.8)),
    "front": Vector((0, -0.75, -0.65)),
    "up": Vector((0, -0.2, 1)),
}


def _clump(head, root_az, root_el, g, rng):
    """Returns centreline points and widths for one clump."""
    k = g.get("shell", 1.06)
    anchor = g.get("anchor")
    if anchor:
        # Ponytail / bun: strands start around a tie point held out from the skull.
        a = head.point(anchor[0], anchor[1], anchor[2])
        n = head.normal(a)
        u = n.cross(Vector((0, 0, 1)))
        u = u.normalized() if u.length > 1e-6 else Vector((1, 0, 0))
        v = n.cross(u).normalized()
        r = g.get("anchor_r", 0.015)
        ang = rng.uniform(0, 2 * math.pi)
        rad = r * math.sqrt(rng.uniform(0, 1))
        p = a + (u * math.cos(ang) + v * math.sin(ang)) * rad
    else:
        p = head.point(root_az, root_el, k * 0.97)
        n = head.normal(p)
    flow = Vector(g.get("flow_vec") or FLOWS[g.get("flow", "down")]).normalized()
    # Sideways component follows azimuth so side hair falls outward, not toward the centre.
    side = Vector((math.sin(math.radians(root_az)), 0, 0)) * g.get("spread", 0.25)
    t = (n * g.get("lift", 0.6) + flow + side).normalized()
    length = rng.uniform(*g["length"])
    steps = g.get("steps", 12)
    ds = length / steps
    curl = Vector(g.get("curl", (0, 0, 0)))
    jit = g.get("jitter", 0.15)
    end_z = g.get("end_z")
    pts = [p.copy()]
    for i in range(steps):
        u = (i + 1) / steps
        t = (t + flow * g.get("gravity", 0.25) + curl * u +
             Vector((rng.uniform(-jit, jit), rng.uniform(-jit, jit), rng.uniform(-jit, jit))) * 0.3).normalized()
        p = p + t * ds
        p = head.push_out(p, k)
        if end_z is not None and p.z < head.eye_z + end_z:
            p.z = head.eye_z + end_z + (p.z - head.eye_z - end_z) * 0.2
        pts.append(p.copy())
    # Wave: sideways S-bends along the strand, phase-shifted per clump.
    wave = g.get("wave", 0.0)
    if wave:
        freq = g.get("wave_freq", 1.5)
        ph = rng.uniform(0, 2 * math.pi)
        m = len(pts)
        for i in range(1, m):
            tt = (pts[min(i + 1, m - 1)] - pts[i - 1]).normalized()
            sdir = tt.cross(head.normal(pts[i])).normalized()
            u = i / (m - 1)
            pts[i] = pts[i] + sdir * wave * length * math.sin(2 * math.pi * freq * u + ph) * u
    # Flick: bend the last third outward from the head for a spiky silhouette.
    flick = g.get("flick", 0.0)
    if flick:
        m = len(pts)
        for i in range(m * 2 // 3, m):
            u = (i - m * 2 // 3) / max(1, m - m * 2 // 3)
            pts[i] = pts[i] + head.normal(pts[i]) * flick * u * u * length
    w = rng.uniform(*g["width"])
    # Broad along most of the length, then a sharp point: the anime clump silhouette.
    tp = g.get("taper", 1.6)
    widths = [w * (0.75 + 0.25 * min(1.0, 4 * i / (len(pts) - 1))) * (1 - (i / (len(pts) - 1)) ** tp)
              for i in range(len(pts))]
    return pts, widths


def _tube(pts, widths, head, flat, verts, faces, us, sides=6):
    base = len(verts)
    m = len(pts)
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, m - 1)] - pts[max(i - 1, 0)]).normalized()
        r = head.normal(p)
        s = t.cross(r).normalized()
        nn = s.cross(t).normalized()
        w = max(widths[i], 0.0002)
        for j in range(sides):
            a = 2 * math.pi * j / sides
            verts.append(p + s * math.cos(a) * w + nn * math.sin(a) * w * flat)
            us.append(i / (m - 1))
    for i in range(m - 1):
        for j in range(sides):
            a = base + i * sides + j
            b = base + i * sides + (j + 1) % sides
            faces.append((a, a + sides, b + sides, b))
    faces.append(tuple(base + j for j in range(sides)))


def _cap(head, hl, verts, faces, us, res=96, rings=24):
    """Ellipsoid shell from the hairline up to the crown; rings follow the hairline, so no stair steps."""
    idx = {}
    for j in range(res):
        az = 360 * j / res
        e0 = hl(az)
        for i in range(rings + 1):
            el = e0 + (89.0 - e0) * i / rings
            idx[(i, j)] = len(verts)
            verts.append(head.point(az, el, 1.02))
            us.append(0.35)
    for j in range(res):
        for i in range(rings):
            faces.append((idx[(i, j)], idx[(i, (j + 1) % res)], idx[(i + 1, (j + 1) % res)], idx[(i + 1, j)]))


def _hairline(spec_hl):
    front, side, back = spec_hl.get("front", 40), spec_hl.get("side", 5), spec_hl.get("back", -35)

    def hl(az):
        a = math.radians(az)
        f = (1 + math.cos(a)) / 2  # 1 at front, 0 at back
        return front * f * f + side * 2 * f * (1 - f) + back * (1 - f) * (1 - f)
    return hl


def _gradient_material(name, root_hex, mid_hex, tip_hex, mid_at=0.5):
    if not 0 < mid_at < 1:
        raise ValueError(f"{name}: tip_from must fall between root and tip")
    m = acg_parts.toon_material(name, mid_hex)
    nt = m.node_tree
    ramp_light = next(n for n in nt.nodes if n.type == "VALTORGB")
    # Use a UV ramp so the same authored strand colours survive both Blender and FBX.
    root, mid, tip = [acg_parts._srgb(c) for c in (root_hex, mid_hex, tip_hex)]
    row = []
    for i in range(GRADIENT_WIDTH):
        u = i / (GRADIENT_WIDTH - 1)
        if u <= mid_at:
            a, b, t = root, mid, u / mid_at
        else:
            a, b, t = mid, tip, (u - mid_at) / (1 - mid_at)
        row.extend(x + (y - x) * t for x, y in zip(a, b))
    img = bpy.data.images.new(name + ".png", width=GRADIENT_WIDTH, height=4, alpha=False)
    img.pixels.foreach_set(row * 4)
    img.update()
    img.pack()
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    m.diffuse_color = (1, 1, 1, 1)
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs[0].default_value = 1.0
    # Light ramp outputs white when lit and a grey shade otherwise; multiply by the strand colour.
    ramp_light.color_ramp.elements[0].color = (0.55, 0.52, 0.6, 1)
    ramp_light.color_ramp.elements[1].color = (1, 1, 1, 1)
    emit = next(n for n in nt.nodes if n.type == "EMISSION")
    nt.links.new(ramp_light.outputs[0], mix.inputs[6])
    nt.links.new(tex.outputs["Color"], mix.inputs[7])
    nt.links.new(mix.outputs[2], emit.inputs[0])
    return m


def _scaled(g, s):
    """Copy of a hair group with its metre-valued sizes multiplied by s ("hair.scale")."""
    g = dict(g)
    for k in ("length", "width"):
        g[k] = [x * s for x in g[k]]
    for k in ("anchor_r", "end_z"):
        if k in g:
            g[k] *= s
    return g


def build(h, rig, spec):
    hs = spec.get("hair")
    if not hs:
        return []
    head = Head(h)
    rng = random.Random(hs.get("seed", 1))
    verts, faces, us = [], [], []
    _cap(head, _hairline(hs.get("hairline", {})), verts, faces, us)
    look = spec["look"]
    mats = [_gradient_material("hair_" + spec["id"], look["hair_root"], look["hair"],
                               look.get("hair_tip", look["hair"]), hs.get("tip_from", 0.55))]
    face_mat = [0] * len(faces)
    swing = []
    for g in hs["groups"]:
        g = _scaled(g, hs.get("scale", 1.0))
        mi = 0
        if "colors" in g:
            mats.append(_gradient_material("hair_%s_%s" % (g["name"], spec["id"]), *g["colors"],
                                           hs.get("tip_from", 0.55)))
            mi = len(mats) - 1
        n = g["count"]
        first = len(verts)
        lines = []
        for i in range(n):
            if "anchor" in g:
                az, el = g["anchor"][0], g["anchor"][1]
            elif "roots" in g:
                az, el = g["roots"][i % len(g["roots"])]
                if i >= len(g["roots"]):
                    az += rng.uniform(-6, 6)
                    el += rng.uniform(-6, 6)
            else:
                t = (i + 0.5) / n
                az = g["az"][0] + (g["az"][1] - g["az"][0]) * t + rng.uniform(-1, 1) * g.get("az_jit", 4)
                el = rng.uniform(*g["el"])
            pts, widths = _clump(head, az, el, g, rng)
            lines.append(pts)
            _tube(pts, widths, head, g.get("flat", 0.22), verts, faces, us)
        if g.get("swing"):
            # One chain per group along the group's mean centreline (all clumps share the step count).
            seg = g["swing"]
            m = len(lines[0])
            mean = [sum((ln[j] for ln in lines), Vector()) / len(lines) for j in range(m)]
            joints = [mean[round(k * (m - 1) / seg)] for k in range(seg + 1)]
            swing.append((g["name"], joints, (first, len(verts)), seg))
        face_mat.extend([mi] * (len(faces) - len(face_mat)))
    me = bpy.data.meshes.new("hair_" + spec["id"])
    me.from_pydata(verts, [], faces)
    for m in mats:
        me.materials.append(m)
    me.polygons.foreach_set("material_index", face_mat)
    me.validate()
    attr = me.attributes.new("hair_u", "FLOAT", "POINT")
    attr.data.foreach_set("value", us)
    uv = me.uv_layers.new(name="Strand")
    for loop in me.loops:
        u = us[loop.vertex_index]
        uv.data[loop.index].uv = ((0.5 + u * (GRADIENT_WIDTH - 1)) / GRADIENT_WIDTH, 0.5)
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new("hair_" + spec["id"], me)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = rig
    vg = ob.vertex_groups.new(name="head")
    vg.add(list(range(len(verts))), 1.0, "REPLACE")
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    if swing:
        import acg_swing
        acg_swing.hair(rig, ob, swing)
    return [ob] + ornaments(h, rig, spec, head)


def ornaments(h, rig, spec, head):
    """Hair ties, clips, goggles: prims placed on the head ellipsoid by direction, so they
    follow the head when its size or shape changes.

    Each entry is an acg_outfit prim with "az"/"el"/"k" (k: multiple of the head radius)
    instead of "at"; optional "offset" adds metres in body space.
    """
    import acg_outfit
    out = []
    body = acg_outfit.Body(h, rig)
    for o in spec.get("hair", {}).get("ornaments", []):
        g = dict(o)
        g.setdefault("bone", "head")
        sc = spec["hair"].get("scale", 1.0)
        g["size"] = [x * sc for x in g.get("size", (0.1, 0.1, 0.1))]
        pt = h.matrix_world @ head.point(o["az"], o["el"], o.get("k", 1.1))
        g["at"] = tuple(pt - body.bone[g["bone"]] + Vector(o.get("offset", (0, 0, 0))) * sc)
        mat = acg_parts.toon_material("%s_%s" % (g["name"], spec["id"]), g["color"], g.get("shade"))
        out.append(acg_outfit.prim(body, g, mat, spec))
    return out
