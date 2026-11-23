"""Anime face: flattened eye sockets, eyes drawn flat on the skin, chin and smoothing (ADR-0013)."""
import math

import bpy
from mathutils import Vector


def _group_points(h, name, min_w=0.0):
    gi = h.vertex_groups[name].index
    return [(v.index, v.co.copy()) for v in h.data.vertices if any(g.group == gi and g.weight > min_w for g in v.groups)]


def _smooth(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def _centroid(h, name):
    pts = [p for _, p in _group_points(h, name)]
    return sum(pts, Vector()) / len(pts)


def eye_frame(h, side):
    """Eye centre and half-width from the eye joint and the upper-lash helper (body space)."""
    c = _centroid(h, "joint-%s-eye" % side)
    lash = [p for _, p in _group_points(h, "helper-%s-eyelashes-2" % side)]
    half = max(abs(p.x - c.x) for p in lash)
    return c, half


def flatten_eye_sockets(h, radius_k=1.5):
    """Replaces each realistic eye socket and lid fold with a smooth skin surface.

    Anime eyes are drawn flat on the face; the socket would otherwise leave a hollow and lid
    creases around the drawn eye. A quadratic y(x, z) is fitted to the skin ring just outside
    the socket and the inside is blended onto it.
    Pre: un-rigged basemesh in metres. Side effects: edits h.data vertex positions around both eyes.
    """
    import numpy as np
    body = h.vertex_groups["body"].index
    skin = [v for v in h.data.vertices if any(g.group == body for g in v.groups)]
    for side in ("l", "r"):
        c, half = eye_frame(h, side)
        r = half * radius_k
        ring = [v.co for v in skin
                if r <= math.hypot(v.co.x - c.x, v.co.z - c.z) <= r + 0.012 and v.co.y < c.y + 0.01]
        if len(ring) < 12:
            raise ValueError("flatten_eye_sockets: too few ring vertices around eye " + side)
        a = np.array([[1, p.x - c.x, p.z - c.z, (p.x - c.x) ** 2, (p.x - c.x) * (p.z - c.z), (p.z - c.z) ** 2]
                      for p in ring])
        coef = np.linalg.lstsq(a, np.array([p.y for p in ring]), rcond=None)[0]
        for v in skin:
            dx, dz = v.co.x - c.x, v.co.z - c.z
            d = math.hypot(dx, dz)
            # Front half only: the back of the head shares x/z with the eye. The depth cut fades out rather than
            # stopping dead, so socket vertices near it follow a reshaped head smoothly (race.py shape keys).
            if d >= r or v.co.y > c.y + 0.045:
                continue
            w = (1 - _smooth(r * 0.7, r, d)) * (1 - _smooth(c.y + 0.03, c.y + 0.045, v.co.y))
            target = float(coef @ np.array([1, dx, dz, dx * dx, dx * dz, dz * dz]))
            v.co.y = v.co.y + (target - v.co.y) * w
    h.data.update()


def _face_mesh(name, verts, faces, mats, rig, uvs=None, mat_index=None):
    """Single-sided mesh facing the viewer (-Y), skinned 100% to the head bone."""
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    if uvs is not None:
        layer = me.uv_layers.new(name="UVMap")
        for loop in me.loops:
            layer.data[loop.index].uv = uvs[loop.vertex_index]
    # Engines cull back faces, and these strips are single-sided.
    for poly in me.polygons:
        if poly.normal.y > 0:
            poly.flip()
        if mat_index is not None:
            poly.material_index = mat_index[poly.index]
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    for m in mats:
        ob.data.materials.append(m)
    ob.parent = rig
    vg = ob.vertex_groups.new(name="head")
    vg.add(list(range(len(verts))), 1.0, "REPLACE")
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    return ob


def _ribbon(name, pts, widths, mat, rig):
    """Flat ribbon facing forward (-Y) along pts, skinned 100% to the head bone."""
    verts, faces = [], []
    for i, (p, w) in enumerate(zip(pts, widths)):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        up = Vector((0, -1, 0)).cross(t).normalized()
        if up.z < 0:
            up = -up
        verts += [p - up * w * 0.5, p + up * w * 0.5]
        if i:
            j = 2 * i
            faces.append((j - 2, j - 1, j + 1, j))
    return _face_mesh(name, verts, faces, [mat], rig)


def _project_front(h, pts, gap=0.0012):
    """Moves points onto the skin along +Y (front-to-back ray), then out by gap."""
    from mathutils.bvhtree import BVHTree
    dg = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(h, dg)
    out = []
    for p in pts:
        # Start well in front of the face: a ray starting inside the head misses the skin.
        hit = bvh.ray_cast(Vector((p.x, -2.0, p.z)), Vector((0, 1, 0)))[0]
        if hit is None:
            raise ValueError("_project_front: no skin under (%.3f, %.3f)" % (p.x, p.z))
        out.append(hit - Vector((0, gap, 0)))
    return out


def nose_accent(h, rig, mat):
    mouth = _centroid(h, "joint-mouth")
    z = mouth.z + 0.33 * (_centroid(h, "joint-l-eye").z - mouth.z)
    points = [Vector((x, 0, z + dz)) for x, dz in
              ((0.001, 0.006), (-0.004, -0.001), (-0.001, -0.003), (0.002, 0))]
    return _face_mesh("nose_accent", _project_front(h, points), [(0, 1, 2, 3)], [mat], rig)


def mouth_line(h, rig, spec, mat):
    """Project the mouth onto the same skin surface as the eyes."""
    f = spec.get("face", {})
    c = _centroid(h, "joint-mouth")
    half = f.get("mouth_half", 0.011)
    smile = f.get("mouth_smile", 0.0012)
    n = 9
    line = [Vector((c.x + half * (2 * i / (n - 1) - 1), c.y, c.z + f.get("mouth_dz", 0.0) +
                    smile * (2 * i / (n - 1) - 1) ** 2)) for i in range(n)]
    opening = f.get("mouth_open", 0.0)
    if opening:
        from acg_parts import toon_material
        teeth = toon_material("teeth_" + spec["id"], "#ffebe0")
        verts, faces, bands = [], [], []
        for i, top in enumerate(line):
            depth = opening * (1 - (2 * i / (n - 1) - 1) ** 2)
            for frac in (0, 0.14, 0.56, 1):
                verts.append(top - Vector((0, 0, frac * depth)))
            if i:
                for j in range(3):
                    a = (i - 1) * 4 + j
                    faces.append((a, a + 4, a + 5, a + 1))
                    bands.append(int(j == 1))
        return _face_mesh("mouth", _project_front(h, verts), faces, [mat, teeth], rig, mat_index=bands)
    return _ribbon("mouth", _project_front(h, line), [f.get("mouth_w", 0.0013)] * n, mat, rig)


class EyeShape:
    """Anime eye outline in the face plane: u runs inner to outer corner, v up (metres)."""

    def __init__(self, f, half):
        self.w = 2 * half * f.get("eye_w", 1.15)
        self.h = self.w * f.get("eye_h", 0.8)
        self.tilt = f.get("eye_tilt", 0.12)

    def base(self, t):
        return self.h * (-0.12 + (0.12 + self.tilt) * t)

    @staticmethod
    def _bump(t, peak, power):
        t = min(1.0, max(0.0, t))
        k = math.log(0.5) / math.log(peak)
        return math.sin(math.pi * t ** k) ** power

    def u(self, t):
        return (t - 0.5) * self.w

    def upper(self, t):
        return self.base(t) + self.h * 0.62 * self._bump(t, 0.42, 0.7)

    def lower(self, t):
        return self.base(t) - self.h * 0.38 * self._bump(t, 0.55, 1.1)


def _plane_to_face(h, c, sign, pts, gap):
    """Face-plane (u, v) points to skin positions: x/z from the eye frame, y by projection."""
    return _project_front(h, [Vector((c.x + sign * u, 0.0, c.z + v)) for u, v in pts], gap)


def drawn_eyes(h, rig, spec, mats):
    """Builds flat anime eyes on the skin: sclera with a lid shadow band, clipped iris, lash
    lines and brows, all from one outline so they line up and sit flush on the face.

    Pre: flatten_eye_sockets ran on h; h is rigged in rest pose. mats: dict with sclera,
    sclera_shade, iris (textured), lash, brow.
    Post: returns the new objects. Side effects: adds head-skinned mesh objects to the scene.
    """
    f = spec.get("face", {})
    out = []
    for side in ("l", "r"):
        c, half = eye_frame(h, side)
        sign = 1 if c.x > 0 else -1
        c = c + Vector((sign * f.get("eye_dx", 0.0), 0, f.get("eye_dz", 0.0)))
        e = EyeShape(f, half)

        nt, nv = 24, 6
        grid, faces, mi = [], [], []
        for i in range(nt + 1):
            t = i / nt
            lo, up = e.lower(t), e.upper(t)
            for j in range(nv + 1):
                grid.append((e.u(t), lo + (up - lo) * j / nv))
        for i in range(nt):
            for j in range(nv):
                a = i * (nv + 1) + j
                faces.append((a, a + nv + 1, a + nv + 2, a + 1))
                # Top rows take the shade colour: the upper lid casts a shadow on the white.
                mi.append(1 if j >= nv - 2 else 0)
        verts = _plane_to_face(h, c, sign, grid, f.get("sclera_gap", 0.0004))
        out.append(_face_mesh("sclera_" + side, verts, faces, [mats["sclera"], mats["sclera_shade"]], rig,
                              mat_index=mi))

        # Iris: tall ellipse clamped to the outline, so the lids cut it as in the art.
        rx = e.w * f.get("iris_w", 0.31)
        rz = e.h * f.get("iris_h", 0.6)
        uc = e.w * f.get("iris_du", -0.02)
        vc = e.base(0.5) + e.h * 0.08
        rings, segs = 6, 32
        pts, uvs = [], []
        for k in range(rings + 1):
            rr = k / rings
            for m in range(segs):
                a = 2 * math.pi * m / segs
                u, v = uc + rx * rr * math.cos(a), vc + rz * rr * math.sin(a)
                # Texture u follows world x so both eyes get their highlights on the same side.
                uvs.append((0.5 + 0.5 * sign * (u - uc) / rx, 0.5 + 0.5 * (v - vc) / rz))
                t = min(1.0, max(0.0, u / e.w + 0.5))
                pts.append((e.u(t), min(e.upper(t), max(e.lower(t), v))))
        ifaces = []
        for k in range(rings):
            for m in range(segs):
                a, b = k * segs + m, k * segs + (m + 1) % segs
                ifaces.append((a, b, b + segs, a + segs))
        verts = _plane_to_face(h, c, sign, pts, f.get("iris_gap", 0.0008))
        out.append(_face_mesh("iris_" + side, verts, ifaces, [mats["iris"]], rig, uvs=uvs))

        gap = f.get("lash_gap", 0.0012)
        # Upper lash line: thick, flaring and lifting past the outer corner.
        n = 18
        lw = f.get("lash_w", 0.0065)
        flick = f.get("lash_flick", 0.002)
        ts = [-0.03 + 1.11 * i / (n - 1) for i in range(n)]
        widths = [lw * (0.55 + 0.6 * min(1.0, max(0.0, t)) ** 1.5) for t in ts]
        line = [(e.u(t), e.upper(min(t, 1.0)) + w * 0.3 + flick * max(0.0, t - 0.8) / 0.3)
                for t, w in zip(ts, widths)]
        out.append(_ribbon("lash_up_" + side, _plane_to_face(h, c, sign, line, gap), widths, mats["lash"], rig))
        # Lower lash: short thin stroke under the outer half.
        ts = [0.55 + 0.45 * i / 9 for i in range(10)]
        line = [(e.u(t), e.lower(t) - 0.0006) for t in ts]
        out.append(_ribbon("lash_lo_" + side, _plane_to_face(h, c, sign, line, gap),
                           [f.get("lower_lash_w", 0.0013)] * len(ts), mats["lash"], rig))
        # Brow: slim arch above the eye, thinning toward the outer end.
        lift = f.get("brow_lift", 0.016)
        arch = f.get("brow_arch", 0.004)
        k = 14
        # brow_slant raises the inner end and lowers the outer (metres each); brow_gap moves the inner end outward.
        slant, gap_in = f.get("brow_slant", 0.0), f.get("brow_gap", 0.0)
        line = [(e.u(i / (k - 1)) * f.get("brow_spread", 1.08) + gap_in * (1 - i / (k - 1)),
                 e.h * 0.5 + lift + arch * 4 * (i / (k - 1)) * (1 - i / (k - 1)) + slant * (1 - 2 * i / (k - 1)))
                for i in range(k)]
        bw = [f.get("brow_w", 0.0035) * (1.0 - 0.6 * i / (k - 1)) for i in range(k)]
        out.append(_ribbon("brow_" + side, _plane_to_face(h, c, sign, line, gap), bw, mats["brow"], rig))
    return out


def shrink_ears(h, scale=0.75):
    """Shrinks each ear toward its root: anime ears are small and mostly hidden by hair.

    Pre: un-rigged basemesh. Side effects: edits h.data vertex positions in the "ears" group.
    """
    gi = h.vertex_groups["ears"].index
    for sign in (1, -1):
        pts = [(v, g.weight) for v in h.data.vertices for g in v.groups
               if g.group == gi and v.co.x * sign > 0]
        if not pts:
            raise ValueError("shrink_ears: no ear vertices on side %d" % sign)
        c = sum((v.co for v, _ in pts), Vector()) / len(pts)
        # Root sits nearer the skull than the ear's centroid.
        root = Vector((c.x - sign * 0.01, c.y, c.z))
        for v, w in pts:
            k = 1 + (scale - 1) * w
            v.co = root + (v.co - root) * k
    h.data.update()


def shape_chin(h, forward=0.011, down=0.012, taper=0.3, radius=0.045, cheek=0.0, anchor="joint"):
    """Pulls the chin forward and down into a small V: anime faces keep a pointed chin even
    though nose and mouth are flattened.

    anchor "lips" takes the mouth from the lips' own centre. joint-mouth is the jaw hinge: on a 7-head figure it
    is close enough, but on the NPC heads (scaled 1.6x) it sits by the nose and the chin edit lands there.
    Pre: un-rigged basemesh in metres. Side effects: edits h.data vertex positions below the mouth.
    """
    body = h.vertex_groups["body"].index
    if anchor == "lips":
        lips = h.vertex_groups["lips"].index
        pts = [v.co for v in h.data.vertices if any(g.group == lips for g in v.groups) and any(g.group == body for g in v.groups)]
        mouth = sum(pts, Vector()) / len(pts)
    else:
        # joint-mouth sits inside the head; take the chin point on the skin in front of it.
        mouth = _centroid(h, "joint-mouth")
    if anchor == "lips":
        # The chin itself: the most forward midline point under the lower lip. A fixed drop below the mouth missed a
        # short anime chin and landed on the throat.
        under = [v.co for v in h.data.vertices if abs(v.co.x) < 0.004 and mouth.z - 0.09 < v.co.z < mouth.z - 0.012 and
                 any(g.group == body for g in v.groups)]
        if not under:
            raise ValueError("shape_chin: no midline skin vertices under the lips")
        tip = min(under, key=lambda p: p.y).copy()
        tip.x = 0.0
    else:
        chin_z = mouth.z - radius * 0.7
        front = [v.co for v in h.data.vertices if abs(v.co.x) < 0.004 and abs(v.co.z - chin_z) < 0.004 and
                 any(g.group == body for g in v.groups)]
        if not front:
            raise ValueError("shape_chin: no midline skin vertices at chin height")
        tip = Vector((0.0, min(p.y for p in front), chin_z))
    for v in h.data.vertices:
        c = v.co
        if c.y > tip.y + radius or c.z > mouth.z + 0.01:
            continue
        w = 1 - _smooth(0.0, radius, (c - tip).length)
        if w <= 0:
            continue
        low = _smooth(mouth.z, tip.z, c.z)
        v.co = Vector((c.x, c.y - forward * w, c.z - down * w * low))
    # V jaw: narrow the front half of the lower head toward the chin so the face outline ends
    # in a point instead of running straight into the neck.
    eye_z = _centroid(h, "joint-l-eye").z
    ring = [v.co for v in h.data.vertices if abs(v.co.z - eye_z) < 0.005 and any(g.group == body for g in v.groups)]
    mid_y = (min(p.y for p in ring) + max(p.y for p in ring)) / 2
    for v in h.data.vertices:
        c = v.co
        if c.y > mid_y or c.z > eye_z or c.z < tip.z - down - 0.03:
            continue
        # Taper grows toward the chin; a linear ramp pinches the cheeks just under the eyes.
        wy = _smooth(mid_y, tip.y + 0.03, c.y)
        t = _smooth(eye_z - 0.01, tip.z, c.z) ** 1.8 * wy
        # Cheek fill: widen the mid-face so the outline runs smoothly from temple to chin
        # instead of pinching in under the cheekbone.
        f = math.sin(math.pi * min(1.0, max(0.0, (eye_z - c.z) / (eye_z - tip.z)))) ** 2 * wy
        c.x *= 1 - taper * t + cheek * f
    h.data.update()


def simplify_face(h, iterations=10, strength=0.6):
    """Laplacian-smooths nose, mouth and cheek creases: anime faces read as smooth planes.

    Stops just below the mouth so the chin keeps its shape.
    Pre: un-rigged basemesh. Side effects: edits h.data vertex positions in the lower face.
    """
    le = _centroid(h, "joint-l-eye")
    jaw = _centroid(h, "joint-jaw")
    chin_z = _centroid(h, "joint-mouth").z - 0.012
    eye_z = le.z
    body = h.vertex_groups["body"].index
    region = {}
    for v in h.data.vertices:
        c = v.co
        if not any(g.group == body for g in v.groups):
            continue
        if c.y > jaw.y - 0.01 or c.z > eye_z - 0.008 or c.z < chin_z or abs(c.x) > 0.06:
            continue
        w = (_smooth(eye_z - 0.008, eye_z - 0.03, c.z) * (1 - _smooth(0.045, 0.06, abs(c.x))) *
             _smooth(chin_z, chin_z + 0.01, c.z))
        region[v.index] = w * strength
    nbr = {i: [] for i in region}
    for e in h.data.edges:
        a, b = e.vertices
        if a in nbr:
            nbr[a].append(b)
        if b in nbr:
            nbr[b].append(a)
    co = {i: h.data.vertices[i].co.copy() for i in range(len(h.data.vertices))}
    for _ in range(iterations):
        new = {}
        for i, w in region.items():
            ns = nbr[i]
            if not ns:
                continue
            avg = sum((co[j] for j in ns), Vector()) / len(ns)
            new[i] = co[i].lerp(avg, w)
        co.update(new)
    for i in region:
        h.data.vertices[i].co = co[i]
    h.data.update()
