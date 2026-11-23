"""Unbounded body and face reshaping for the NPC race's DNA channels (ADR-0015, npc-uma-looks.md#種族基底).

MakeHuman targets stop at weight 1 and several of them barely survive the ACG face smoothing, so channels whose
residents spread wider than a target reaches are carried by these edits instead. Each takes a multiplier from
spec["shape"] (1 = unchanged) and runs on the rigged T-pose rest mesh: bones are fitted already and none of these
moves a joint (limb and trunk edits are radial about the bone; face edits stay inside the head).

Pre: build.build has fitted the rig and baked the T-pose (build._pose_to_rest). Side effects: edits h.data positions.
"""
import math

from mathutils import Vector

# Limb nodes: (key, bone, t along the bone, half-width of the bump in bone lengths). The nodes are the procedural
# figure's (NpcMeshes.Rig: root, belly of the segment, joint, belly of the next, far joint).
LIMB_NODES = (
    ("thigh", ("thigh_l", "thigh_r"), 0.12, 0.2),
    ("thigh_mid", ("thigh_l", "thigh_r"), 0.5, 0.25),
    ("knee", ("calf_l", "calf_r"), 0.0, 0.12),
    ("calf", ("calf_l", "calf_r"), 0.33, 0.22),
    ("ankle", ("calf_l", "calf_r"), 0.95, 0.12),
    ("upper_arm", ("upperarm_l", "upperarm_r"), 0.12, 0.2),
    ("upper_arm_mid", ("upperarm_l", "upperarm_r"), 0.5, 0.25),
    ("elbow", ("lowerarm_l", "lowerarm_r"), 0.0, 0.12),
    ("forearm", ("lowerarm_l", "lowerarm_r"), 0.33, 0.22),
    ("wrist", ("lowerarm_l", "lowerarm_r"), 0.97, 0.1),
)


def _smooth(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def _weights(h, names):
    """vertex index -> summed weight of the named groups."""
    idx = {h.vertex_groups[n].index for n in names if n in h.vertex_groups}
    out = {}
    for v in h.data.vertices:
        w = sum(g.weight for g in v.groups if g.group in idx)
        if w > 0:
            out[v.index] = min(1.0, w)
    return out


def _centroid(h, name):
    gi = h.vertex_groups[name].index
    pts = [v.co for v in h.data.vertices if any(g.group == gi for g in v.groups)]
    return sum(pts, Vector()) / len(pts)


def _limbs(h, rig, s):
    bones = rig.data.bones
    for key, names, t, half in LIMB_NODES:
        k = s.get(key, 1.0)
        if k == 1.0:
            continue
        for name in names:
            b = bones[name]
            p, q = b.head_local, b.tail_local
            axis = (q - p).normalized()
            length = (q - p).length
            # A joint node sits where two bones meet: take the skin of both, so the bump is not cut at the joint.
            near = [name]
            if t <= 0.05 and b.parent is not None:
                near.append(b.parent.name)
            if t >= 0.95:
                near += [c.name for c in b.children]
            w = _weights(h, near)
            for i, wb in w.items():
                c = h.data.vertices[i].co
                along = (c - p).dot(axis) / length
                bump = 1 - _smooth(0.0, half, abs(along - t))
                if bump <= 0:
                    continue
                foot = p + axis * (along * length)
                h.data.vertices[i].co = foot + (c - foot) * (1 + (k - 1) * bump * wb)


def _trunk(h, rig, k):
    # Chest depth: front-to-back about the spine, carried by the spine bones' skin only.
    w = _weights(h, ("spine_01", "spine_02", "spine_03"))
    spine = [rig.data.bones[n].head_local for n in ("spine_01", "spine_02", "spine_03")]
    for i, wb in w.items():
        c = h.data.vertices[i].co
        y0 = min(spine, key=lambda p: abs(p.z - c.z)).y
        c.y = y0 + (c.y - y0) * (1 + (k - 1) * wb)


def _neck(h, rig, k):
    b = rig.data.bones["neck_01"]
    p, q = b.head_local, b.tail_local
    axis = (q - p).normalized()
    for i, wb in _weights(h, ("neck_01",)).items():
        c = h.data.vertices[i].co
        foot = p + axis * (c - p).dot(axis)
        h.data.vertices[i].co = foot + (c - foot) * (1 + (k - 1) * wb)


def _belly(h, rig, k):
    # Belly depth: push the front of the lower trunk forward from the spine; the back stays put.
    # spine_02 sits at the chest in this rig; the belly ends below it, or the chest (and a woman's bust) moves instead.
    # Fades are shares of the pelvis-to-spine_02 span: fixed centimetres left a goblin's belly with no full-strength band.
    pelvis, spine2 = rig.data.bones["pelvis"].head_local, rig.data.bones["spine_02"].head_local
    span = spine2.z - pelvis.z
    for i, wb in _weights(h, ("pelvis", "spine_01", "spine_02")).items():
        c = h.data.vertices[i].co
        if c.y >= pelvis.y or not pelvis.z - 0.35 * span < c.z < spine2.z:
            continue
        band = _smooth(pelvis.z - 0.35 * span, pelvis.z + 0.2 * span, c.z) * (1 - _smooth(spine2.z - 0.45 * span, spine2.z, c.z))
        c.y = pelvis.y + (c.y - pelvis.y) * (1 + (k - 1) * band * wb)


def _face(h, s):
    eye = (_centroid(h, "joint-l-eye") + _centroid(h, "joint-r-eye")) / 2
    neck = _centroid(h, "joint-neck")
    # The whole head skin, not only acg_head: on short big-headed races (dwarf, goblin) the chin hangs below where
    # acg_head starts to weigh in. acg_head still limits the cheekbone band.
    weight = _weights(h, ("acg_head",))
    head = {v.index: weight.get(v.index, 0.0) for v in h.data.vertices if v.co.z > neck.z - 0.1 and abs(v.co.x) < 0.2}
    body = h.vertex_groups["body"].index
    lips_g = h.vertex_groups["lips"].index
    co = {i: h.data.vertices[i].co.copy() for i in head}
    normal = {i: h.data.vertices[i].normal.copy() for i in head}
    lips = [i for i in head if any(g.group == lips_g for g in h.data.vertices[i].groups)]
    mouth = sum((co[i] for i in lips), Vector()) / len(lips) if lips else eye - Vector((0, 0, 0.1))
    skin = [i for i in head if any(g.group == body for g in h.data.vertices[i].groups)]
    lower_mid = [i for i in skin if abs(co[i].x) < 0.004 and mouth.z - 0.09 < co[i].z < mouth.z - 0.012]
    front = min(lower_mid, key=lambda i: co[i].y)
    chin = co[min((i for i in lower_mid if co[i].y < co[front].y + 0.025), key=lambda i: co[i].z)]
    mid_y = (min(co[i].y for i in skin) + max(co[i].y for i in skin)) / 2

    face_len = s.get("face_len", 1.0)
    chin_w = s.get("chin_w", 1.0)
    cheek_w = s.get("cheekbone_w", 1.0)
    lip_up, lip_low = s.get("lip_up", 1.0), s.get("lip_low", 1.0)
    nose_w, nose_up = s.get("nose_w", 1.0), s.get("nose_up", 0.0)
    jaw_w, jowl = s.get("jaw_w", 1.0), s.get("jowl", 0.0)
    skull_d = s.get("skull_d", 1.0)
    head_y = _centroid(h, "joint-head").y
    crown = max(co[i].z for i in skin)
    hr = (crown - chin.z) / 1.98  # head radius as NpcMeshes.HeadShape counts it
    # A full upper lip can stand further forward than the nose tip, so the search starts a quarter of the way up.
    nose_mid = [i for i in skin if abs(co[i].x) < 0.004 and mouth.z + 0.25 * (eye.z - mouth.z) < co[i].z < eye.z - 0.01]
    tip = co[min(nose_mid, key=lambda i: co[i].y)] if nose_mid else eye
    for i, wh in head.items():
        c = h.data.vertices[i].co
        x, y, z = c.x, c.y, c.z
        frontness = 1 - _smooth(mid_y - 0.02, mid_y + 0.03, co[i].y)
        # Cheekbones: widen a band just under the eyes, on the face only: reaching back to the ears pulled the sides
        # of the head out into wings.
        if cheek_w != 1.0:
            band = 1 - _smooth(0.0, 0.03, abs(co[i].z - (eye.z - 0.025)))
            face = 1 - _smooth(eye.y + 0.005, eye.y + 0.035, co[i].y)
            x *= 1 + (cheek_w - 1) * band * face * wh
        # Skull depth: the back of the cranium moves out or in behind the head joint, so a long head shows as a
        # rounder occiput and the face stays where it is.
        if skull_d != 1.0 and co[i].y > head_y:
            # A long fade down the back of the head: a short one left a shelf above the nape.
            wd = _smooth(chin.z - 0.02, eye.z + 0.04, co[i].z) * min(1.0, wh * 2.0)
            y = head_y + (y - head_y) * (1 + (skull_d - 1) * wd)
        # Jaw: widen a band at the angle of the jaw (just under the mouth), face side only.
        if jaw_w != 1.0:
            # Below the chin line the band fades out, or the jaw corners flare away from the neck.
            band = (1 - _smooth(0.0, 0.035, abs(co[i].z - (mouth.z - 0.01)))) * _smooth(chin.z - 0.015, chin.z + 0.015, co[i].z)
            # The whole jaw in front of the head joint, back to its angle: stopping at the cheek left the angle as it was.
            face = 1 - _smooth(head_y - 0.03, head_y, co[i].y)
            # The underside of the jaw faces down into the neck; widening it pulled the corners out into wings.
            side = 1 - _smooth(-0.1, -0.6, normal[i].z)
            x *= 1 + (jaw_w - 1) * band * face * side
        # Double chin: fill under the chin, down and forward (NpcMeshes.HeadShape: bottom -0.06, depth +0.08 hr).
        if jowl != 0.0:
            under = Vector((0, co[front].y + 0.02, chin.z - 0.015))
            wj = (1 - _smooth(0.0, 0.045, (co[i] - under).length)) * frontness
            z -= jowl * 0.06 * hr * wj
            y -= jowl * 0.04 * hr * wj
        # Chin: narrow or widen the point of the face around the chin.
        if chin_w != 1.0:
            d = (co[i] - Vector((0, co[front].y, chin.z + 0.01))).length
            x *= 1 + (chin_w - 1) * (1 - _smooth(0.01, 0.05, d)) * frontness
        # Nose: wings wider or narrower, tip turned up or down (metres), around the tip only.
        if nose_w != 1.0 or nose_up != 0.0:
            d = (co[i] - tip).length
            wn = (1 - _smooth(0.014, 0.04, d)) * frontness
            x *= 1 + (nose_w - 1) * wn
            z += nose_up * (1 - _smooth(0.0, 0.018, d)) * frontness
        # Lips: swell forward from the mouth line, upper and lower apart.
        if i in lips and (lip_up != 1.0 or lip_low != 1.0):
            k = lip_up if co[i].z >= mouth.z else lip_low
            y -= (k - 1) * 0.004 * (1 - _smooth(0.0, 0.02, abs(co[i].x)))
        # Face length: stretch the head below the eye line; under the chin the stretch fades out into the neck.
        if face_len != 1.0 and co[i].z < eye.z:
            fade = _smooth(chin.z - 0.05, chin.z, co[i].z)
            # Added as a displacement: the nose and lip edits above have already moved z.
            low = min(co[i].z, chin.z)
            z += (low - eye.z) * (face_len - 1) * fade
        c.x, c.y, c.z = x, y, z
    h.data.update()


def reshape(h, rig, spec):
    """Applies spec["shape"]; a spec without it is left untouched (the protagonists' builds)."""
    s = spec.get("shape")
    if not s:
        return
    _limbs(h, rig, s)
    if s.get("trunk_d", 1.0) != 1.0:
        _trunk(h, rig, s["trunk_d"])
    if s.get("neck", 1.0) != 1.0:
        _neck(h, rig, s["neck"])
    if s.get("belly", 1.0) != 1.0:
        _belly(h, rig, s["belly"])
    _face(h, s)
    h.data.update()
