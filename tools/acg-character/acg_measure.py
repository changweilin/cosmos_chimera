"""Landmark measures on the bare ACG body, for calibrating the UMA race's shape keys (ADR-0015, npc-uma-looks.md).

Each measure is fixed on the base: the vertex set is chosen once from the base's own landmarks, then the same
vertex indices are read on every variant (shape keys keep the topology), so a key's effect is the change of a
named dimension rather than its largest vertex move. Lengths are metres in the build's body space (the figure
faces -Y, +X is its left, feet on z = 0, T-pose).
"""
import math

from mathutils import Vector


def _members(h, name, min_w=0.0):
    if name not in h.vertex_groups:
        return set()
    gi = h.vertex_groups[name].index
    return {v.index for v in h.data.vertices if any(g.group == gi and g.weight > min_w for g in v.groups)}


def _centroid(h, name):
    idx = _members(h, name)
    return sum((h.data.vertices[i].co for i in idx), Vector()) / len(idx)


class Probe:
    """Measures of one base body. Pre: h is the built body mesh (build.build), bare. Side effects: none."""

    def __init__(self, h):
        body = _members(h, "body")
        ears = _members(h, "ears")
        co = [v.co.copy() for v in h.data.vertices]
        self.skin = [i for i in body if i not in ears]
        eye_l, eye_r = _centroid(h, "joint-l-eye"), _centroid(h, "joint-r-eye")
        self.eye = (eye_l + eye_r) / 2
        self.eye_x = abs(eye_l.x - eye_r.x) / 2
        # joint-mouth is the jaw hinge, well above and behind the lips; the mouth line is the lips' own centre.
        lips = [i for i in _members(h, "lips") if i in body]
        self.mouth = sum((co[i] for i in lips), Vector()) / len(lips)
        neck = _centroid(h, "joint-neck")
        # joint-neck sits near the jaw on the ACG head (scaled 1.3-1.7x around the head joint); the chin hangs below it.
        head = [i for i in self.skin if co[i].z > neck.z - 0.08 and abs(co[i].y - self.eye.y) < 0.25]
        self.crown = max(head, key=lambda i: co[i].z)
        self.head_joint = _centroid(h, "joint-head")
        mid = [i for i in head if abs(co[i].x) < 0.004]
        lower = [i for i in mid if self.mouth.z - 0.09 < co[i].z < self.mouth.z - 0.012]
        # Chin: the most forward midline point under the lower lip, then the lowest midline point within 2.5 cm behind
        # it (under the chin, before the throat turns down).
        self.chin_front = min(lower, key=lambda i: co[i].y)
        self.chin = min((i for i in lower if co[i].y < co[self.chin_front].y + 0.025), key=lambda i: co[i].z)
        # A full upper lip can stand further forward than the nose tip, so the search starts a quarter of the way up.
        nose = [i for i in mid if self.mouth.z + 0.25 * (self.eye.z - self.mouth.z) < co[i].z < self.eye.z - 0.01]
        self.nose_tip = min(nose, key=lambda i: co[i].y)
        self.height = max(c.z for c in co)
        # Head radius as the procedural head defines it: crown to chin is about 1.98 radii (NpcMeshes.HeadShape).
        self.hr = (co[self.crown].z - co[self.chin].z) / 1.98

        def band(z0, z1, front=None, near_x=None):
            return [i for i in self.skin if z0 <= co[i].z <= z1 and (front is None or co[i].y < front) and
                    (near_x is None or abs(co[i].x) < near_x) and co[i].z > neck.z - 0.08]

        e, m = self.eye, self.mouth
        tip = co[self.nose_tip]
        self.sets = {
            "temple": band(e.z + 0.02, e.z + 0.035, near_x=0.12),
            "cranium": band(e.z + 0.03, e.z + 0.05),
            "cheekbone": band(e.z - 0.03, e.z - 0.015, front=e.y + 0.03),
            "cheek": band(m.z + 0.0, m.z + 0.015, front=e.y + 0.03),
            # The jaw's sides, as acg_shape widens them: its underside faces down into the neck and is left in place.
            "jaw": [i for i in band(m.z - 0.02, m.z - 0.005, front=self.head_joint.y) if h.data.vertices[i].normal.z > -0.1],
            "chin": band(co[self.chin].z + 0.006, co[self.chin].z + 0.016, front=co[self.chin_front].y + 0.012),
            "nose": [i for i in self.skin if abs(co[i].z - (tip.z + 0.004)) < 0.004 and co[i].y < tip.y + 0.012 and abs(co[i].x) < 0.03],
            "upper_lip": [i for i in _members(h, "lips") & set(self.skin) if co[i].z > m.z and abs(co[i].x) < 0.012],
            "lower_lip": [i for i in _members(h, "lips") & set(self.skin) if co[i].z <= m.z and abs(co[i].x) < 0.012],
        }
        self.rings = {}
        # Rings are cut from the skin the limb's own bone carries (rig weights), so the other leg, the pelvis or the
        # chest never fall into a slab near the root.
        for name, bone, a, b, t in (
                ("neck", "neck_01", "joint-neck", "joint-head", 0.3),
                ("thigh", "thigh_l", "joint-l-upper-leg", "joint-l-knee", 0.15),
                ("thigh_mid", "thigh_l", "joint-l-upper-leg", "joint-l-knee", 0.5),
                ("knee", "calf_l", "joint-l-knee", "joint-l-ankle", 0.04),
                ("calf", "calf_l", "joint-l-knee", "joint-l-ankle", 0.33),
                ("upper_arm", "upperarm_l", "joint-l-shoulder", "joint-l-elbow", 0.15),
                ("upper_arm_mid", "upperarm_l", "joint-l-shoulder", "joint-l-elbow", 0.5),
                ("forearm", "lowerarm_l", "joint-l-elbow", "joint-l-hand", 0.33),
                ("ankle", "calf_l", "joint-l-knee", "joint-l-ankle", 0.93),
                ("elbow", "lowerarm_l", "joint-l-elbow", "joint-l-hand", 0.04),
                ("wrist", "lowerarm_l", "joint-l-elbow", "joint-l-hand", 0.95)):
            p, q = _centroid(h, a), _centroid(h, b)
            self.rings[name] = self._ring(co, _members(h, bone, 0.4) & body, p, q, t)
        # MakeHuman numbers the spine joints top down: spine-1 is the rib cage, spine-3 the waist.
        chest = _centroid(h, "joint-spine-1")
        waist = _centroid(h, "joint-spine-3")
        pelvis = _centroid(h, "joint-pelvis")
        self.sets["chest"] = [i for i in body if abs(co[i].z - chest.z) < 0.01 and abs(co[i].x) < 0.12]
        navel = (pelvis.z + waist.z) / 2
        reach = waist.z - pelvis.z  # a share of the trunk, so a goblin's band is as much belly as a human's
        self.sets["waist"] = [i for i in body if abs(co[i].z - navel) < 0.2 * reach and abs(co[i].x) < 0.6 * reach]
        self.pelvis_z = pelvis.z
        self.pelvis_y = pelvis.y
        self.shoulder_x = abs(_centroid(h, "joint-l-shoulder").x)
        self.hip_x = abs(_centroid(h, "joint-l-upper-leg").x)
        self.base = self.measure(co)

    @staticmethod
    def _ring(co, body, p, q, t):
        axis = (q - p).normalized()
        at = p + (q - p) * t
        reach = 0.6 * (q - p).length
        ring = [i for i in body if abs((co[i] - at).dot(axis)) < 0.012 and ((co[i] - at) - axis * (co[i] - at).dot(axis)).length < reach]
        return ring, at, axis

    def measure(self, co):
        """Dimensions of a variant (or the base) with the base's vertex sets. Pre: co indexed like the base body."""
        def ext(name, k):
            vals = [co[i][k] for i in self.sets[name]]
            return max(vals) - min(vals) if vals else 0.0

        def spread(name):
            # Twice the mean |x|: the outermost vertex of a face band sits at the side, where face edits fade out.
            return 2 * sum(abs(co[i].x) for i in self.sets[name]) / len(self.sets[name])

        def front(name):
            # Mean, not the single most forward vertex: a volume target swells the whole lip, not always its tip.
            return -sum(co[i].y for i in self.sets[name]) / len(self.sets[name])

        def girth(name):
            ring, _, axis = self.rings[name]
            if not ring:
                return 0.0
            c = sum((co[i] for i in ring), Vector()) / len(ring)
            return sum(((co[i] - c) - axis * (co[i] - c).dot(axis)).length for i in ring) / len(ring)

        ez = self.eye.z
        out = {
            "temple_w": ext("temple", 0),
            "cranium_d": ext("cranium", 1),
            "crown_up": co[self.crown].z - self.head_joint.z,
            "cheekbone_w": spread("cheekbone"),
            "cheek_w": spread("cheek"),
            "jaw_w": spread("jaw"),
            "chin_w": spread("chin"),
            "chin_down": ez - co[self.chin].z,
            "chin_fwd": -co[self.chin_front].y,
            "chin_h": self.mouth.z - co[self.chin].z,
            "nose_w": ext("nose", 0),
            "nose_down": ez - co[self.nose_tip].z,
            "nose_fwd": -co[self.nose_tip].y,
            "upper_lip_fwd": front("upper_lip"),
            "lower_lip_fwd": front("lower_lip"),
            "chest_d": ext("chest", 1),
            "belly_fwd": self.pelvis_y - min(co[i].y for i in self.sets["waist"]),
        }
        for name in self.rings:
            out[name + "_r"] = girth(name)
        return out

    def bones(self):
        """The base's values of the bone channels, in the mapper's units (ratios to height)."""
        return {
            "body.height": self.height,
            "body.headRatio": self.hr / self.height,
            "body.legShare": self.pelvis_z / self.height,
            "body.shoulder": self.shoulder_x / self.height,
            "body.hipWidth": self.hip_x / self.height,
        }


def per_of(channel, probe, base_m, var_m, step, mean, spec_value=None):
    """Channel change one full key stands for, from the key's measured effect (race_channels.json "cal").

    rel: the measure scales with the channel value, so per = mean x relative change; k: the channel is an offset of
    k head radii per unit, so per = change / (k x hr). step-based rules read the nudge itself. Returns None when the
    measure did not move (the key carries nothing measurable) or the channel has no rule.
    """
    cal = channel.get("cal")
    if cal is None:
        return channel.get("per")
    if step == 0:
        return None
    by = cal["by"]
    if by == "fixed":
        return cal["per"]
    if by == "measure":
        d = var_m[cal["m"]] - base_m[cal["m"]]
        if abs(d) < 1e-5:
            return None
        # Signed key weight: a target's net-weight step, or for a param its nudge over the channel step (a param whose
        # step is negative still has "+" mean "more of the channel").
        w = step if channel["kind"] == "target" else step / channel["step"]
        if cal.get("rel"):
            return mean * (d / base_m[cal["m"]]) / w * cal.get("k", 1.0)
        return d / (cal["k"] * probe.hr) / w
    if by == "step":
        # Param channels: the key moves the spec value by `step`; per is that move in the channel's own unit.
        s = channel["step"]
        if cal.get("rel"):
            return mean * s / spec_value
        if cal.get("m") == "eye_x":
            return s / probe.eye_x
        return s / (cal["k"] * probe.hr)
    raise ValueError("unknown cal rule " + by)


def per_key(channel, probe, base_m, var_m, step, mean, spec_value=None):
    """Channel change the whole baked key stands for (key weight 1): per_of (one unit: target net weight 1, or one
    channel step of a param) times the units the key was baked at. Measured keys keep their sign, so a key that
    moves its measure the wrong way shows up negative in the gate test."""
    per = per_of(channel, probe, base_m, var_m, step, mean, spec_value)
    if per is None:
        return None
    units = abs(step if channel["kind"] == "target" else step / channel["step"])
    cal = channel.get("cal")
    return per * units if (cal is not None and cal["by"] == "measure") else abs(per) * units
