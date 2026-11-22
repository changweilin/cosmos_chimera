"""Toon materials, anime eyes, hair and outfit parts for ACG characters (ADR-0013)."""
import bpy
from mathutils import Vector


def _rgb(hexstr):
    h = hexstr.lstrip("#")
    srgb = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb) + (1.0,)


def _srgb(hexstr):
    h = hexstr.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)) + (1.0,)


def toon_material(name, color_hex, shade_hex=None, shade_at=0.45, soft=False):
    """Two-tone cel material: lit colour above the threshold, shade colour below.
    soft=True adds a mid-tone band between them: fabric reads softer than hard plastic or metal."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    diff = nt.nodes.new("ShaderNodeBsdfDiffuse")
    s2r = nt.nodes.new("ShaderNodeShaderToRGB")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = "CONSTANT"
    base = _rgb(color_hex)
    shade = _rgb(shade_hex) if shade_hex else tuple(c * 0.62 for c in base[:3]) + (1.0,)
    ramp.color_ramp.elements[0].color = shade
    ramp.color_ramp.elements[1].position = shade_at
    ramp.color_ramp.elements[1].color = base
    if soft:
        mid = ramp.color_ramp.elements.new(shade_at - 0.1)
        mid.color = tuple((a + b) / 2 for a, b in zip(shade[:3], base[:3])) + (1.0,)
    emit = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(diff.outputs[0], s2r.inputs[0])
    nt.links.new(s2r.outputs[0], ramp.inputs[0])
    nt.links.new(ramp.outputs[0], emit.inputs[0])
    nt.links.new(emit.outputs[0], out.inputs[0])
    m.diffuse_color = base
    return m


def outline_material():
    m = bpy.data.materials.get("acg_outline")
    if m:
        return m
    m = bpy.data.materials.new("acg_outline")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs[0].default_value = _rgb("#2a1e1e")
    nt.links.new(emit.outputs[0], out.inputs[0])
    m.use_backface_culling = True
    return m


def _outline_mask(h):
    """Weight 0 on the face front and ears: hull lines there draw every crease, anime faces only show the silhouette."""
    vg = h.vertex_groups.get("acg_outline") or h.vertex_groups.new(name="acg_outline")
    eye = h.vertex_groups["joint-l-eye"].index
    ears = h.vertex_groups["ears"].index
    ez = [v.co for v in h.data.vertices if any(g.group == eye for g in v.groups)]
    ec = sum(ez, Vector()) / len(ez)
    # The mask ends under the chin, fading in over 1.5 cm: an edge on the visible front of the
    # face leaves stray hull slivers, while on the underside it is hidden from front views.
    mz = sum((v.co.z for v in h.data.vertices if any(g.group == h.vertex_groups["joint-mouth"].index
                                                       for g in v.groups)), 0.0) / 8
    mid = [v.co for v in h.data.vertices if abs(v.co.x) < 0.004 and mz - 0.09 < v.co.z < mz - 0.01]
    tip_z = min(mid, key=lambda c: c.y).z
    for v in h.data.vertices:
        c = v.co
        w = 1.0
        if c.y < ec.y + 0.025 and c.z < ec.z + 0.07 and abs(c.x) < 0.075:
            t = min(1.0, max(0.0, (tip_z - 0.025 - c.z) / 0.02))
            w = t * t * (3 - 2 * t)
        if any(g.group == ears for g in v.groups):
            w = 0.0
        vg.add([v.index], w, "REPLACE")
    return vg.name


def add_outline(obj, width, mask=None):
    """Inverted-hull outline; Unity uses its own outline pass, so this is render-only."""
    if obj.type != "MESH":
        return
    ol = outline_material()
    if ol.name not in [s.material.name for s in obj.material_slots if s.material]:
        obj.data.materials.append(ol)
    mod = obj.modifiers.new("acg_outline", "SOLIDIFY")
    mod.thickness = -width
    mod.use_flip_normals = True
    mod.material_offset = len(obj.data.materials) - 1
    mod.use_rim = False
    mod.show_in_editmode = False
    if mask:
        mod.vertex_group = mask
        mod.thickness_vertex_group = 0.0


def spherical_face_normals(h, rig, mix=1.0):
    """Points head normals away from the skull centre so cel shading gives clean anime planes.

    Realistic MakeHuman facial normals produce blotchy shade islands under a two-tone ramp.
    """
    gi = h.vertex_groups["acg_head"].index
    pts = [v.co for v in h.data.vertices if any(g.group == gi and g.weight > 0.9 for g in v.groups)]
    centre = sum(pts, Vector()) / len(pts)
    centre.y += 0.08
    # Below eye level so the lower face faces forward and stays lit, as in cel-shaded art.
    eyes = [v.co for v in h.data.vertices if any(g.group == h.vertex_groups["joint-l-eye"].index for g in v.groups)]
    centre.z = sum(e.z for e in eyes) / len(eyes) - 0.1
    em = bpy.data.objects.new("acg_head_centre", None)
    bpy.context.scene.collection.objects.link(em)
    em.location = h.matrix_world @ centre
    em.parent = rig
    em.matrix_parent_inverse = rig.matrix_world.inverted()
    mod = h.modifiers.new("acg_face_normals", "NORMAL_EDIT")
    mod.mode = "RADIAL"
    mod.target = em
    mod.vertex_group = "acg_head"
    mod.mix_factor = mix
    mod.mix_mode = "COPY"
    return em


def set_material(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)


def iris_texture_material(name, iris_hex, iris_dark_hex, pupil_hex="#1a0f12", size=256):
    """Anime iris baked to an image (unit disk filling the UV square) so it survives FBX export.

    Side effects: creates an in-memory image <name>.png; export_fbx saves it next to the FBX.
    """
    import numpy as np
    s = np.linspace(-1.0, 1.0, size, endpoint=False) + 1.0 / size
    x, y = np.meshgrid(s, s)  # image rows run bottom-up in Blender pixel buffers
    r = np.sqrt(x * x + y * y)
    # Byte images hold display (sRGB) values directly, so use the hex colours unconverted.
    iris = np.array(_srgb(iris_hex), dtype=np.float32)
    dark = np.array(_srgb(iris_dark_hex), dtype=np.float32)
    pupil = np.array(_srgb(pupil_hex), dtype=np.float32)
    # Dark at the top under the lid, iris colour below, a lighter crescent near the bottom.
    k = np.clip((y + 0.1) / 0.9, 0.0, 1.0)[..., None]
    col = dark * k + iris * (1 - k)
    light = np.clip(iris * 1.35 + 0.08, 0.0, 1.0)
    crescent = (np.sqrt(x * x + (y + 0.15) ** 2) > 0.55) & (y < -0.35)
    col[crescent] = light
    col[r > 0.86] = dark
    col[(x / 0.42) ** 2 + (y / 0.5) ** 2 < 1.0] = pupil
    for hx, hy, hr in ((-0.32, 0.38, 0.24), (0.34, -0.42, 0.11)):
        col[np.sqrt((x - hx) ** 2 + (y - hy) ** 2) < hr] = (1, 1, 1, 1)
    col[..., 3] = 1.0
    img = bpy.data.images.get(name + ".png") or bpy.data.images.new(name + ".png", size, size, alpha=False)
    img.colorspace_settings.name = "sRGB"
    img.pixels.foreach_set(col.astype(np.float32).ravel())
    img.update()
    img.pack()
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    emit = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(tex.outputs[0], emit.inputs[0])
    nt.links.new(emit.outputs[0], out.inputs[0])
    m.diffuse_color = (1, 1, 1, 1)
    return m


def _children(rig, h):
    return [o for o in bpy.data.objects if o.type == "MESH" and o is not h and (o.parent in (h, rig))]


def smooth_joints(h, iterations=40, strength=0.7, spread=16):
    """Taubin-smooths knees and elbows: MakeHuman's kneecap, inner-knee notch and calf bulge leave
    a kinked contour that the inverted-hull outline turns into crack lines, so the leg reads as
    broken. Anime limbs are smooth tubes there. Taubin (shrink then inflate) keeps limb thickness,
    which plain Laplacian steps would eat; the joint zone is diffused "spread" rings outward so the
    smoothing fades in along the limb instead of leaving a step.

    Pre: h rigged (bone vertex groups present), rest pose. Side effects: edits h.data vertex positions.
    """
    names = {vg.index: vg.name for vg in h.vertex_groups}
    pairs = [("thigh_%s", "calf_%s"), ("upperarm_%s", "lowerarm_%s")]
    limb = ("thigh_l", "thigh_r", "calf_l", "calf_r", "upperarm_l", "upperarm_r", "lowerarm_l", "lowerarm_r")
    zone, on_limb = {}, set()
    for v in h.data.vertices:
        w = {names[g.group]: g.weight for g in v.groups}
        if "body" not in w:
            continue
        if sum(w.get(b, 0.0) for b in limb) > 0.9:
            on_limb.add(v.index)
        zone[v.index] = max(min(1.0, 8.0 * w.get(a % s, 0.0) * w.get(b % s, 0.0)) for a, b in pairs for s in "lr")
    nbr = {i: [] for i in zone}
    for e in h.data.edges:
        a, b = e.vertices
        if a in nbr and b in nbr:
            nbr[a].append(b)
            nbr[b].append(a)
    for _ in range(spread):
        zone = {i: max(z, 0.85 * max((zone[j] for j in nbr[i]), default=0.0)) if i in on_limb else z
                for i, z in zone.items()}
    region = {i: z * strength for i, z in zone.items() if z > 0.0 and nbr[i]}
    co = {i: h.data.vertices[i].co.copy() for i in zone}
    for _ in range(iterations):
        for k in (1.0, -1.06):
            new = {i: co[i].lerp(sum((co[j] for j in nbr[i]), Vector()) / len(nbr[i]), w * k)
                   for i, w in region.items()}
            co.update(new)
    for i in region:
        h.data.vertices[i].co = co[i]
    h.data.update()


def dress(h, rig, spec):
    """Applies colours and outlines; hair and outfit come from acg_hair / acg_outfit."""
    smooth_joints(h)
    look = spec["look"]
    skin = toon_material("skin_" + spec["id"], look["skin"], look.get("skin_shade"))
    set_material(h, skin)
    spherical_face_normals(h, rig)
    import acg_face
    acg_face.drawn_eyes(h, rig, spec, {
        "sclera": toon_material("sclera_" + spec["id"], look.get("sclera", "#f6f4f2")),
        "sclera_shade": toon_material("sclerashade_" + spec["id"], look.get("sclera_shade", "#c9c6d6")),
        "iris": iris_texture_material("eye_" + spec["id"], look["iris"], look["iris_dark"]),
        "lash": toon_material("lash_" + spec["id"], look.get("lash", "#2a1a18")),
        "brow": toon_material("brow_" + spec["id"], look["brow"]),
    })
    acg_face.mouth_line(h, rig, spec, toon_material("mouth_" + spec["id"], look.get("mouth", "#9a5a50")))
    acg_face.nose_accent(h, rig, toon_material("nose_" + spec["id"], look["skin_shade"]))
    import acg_hair
    import acg_outfit
    acg_hair.build(h, rig, spec)
    acg_outfit.build(h, rig, spec)
    import acg_props
    acg_props.build(h, rig, spec)
    width = 0.0025 * spec["height_cm"] / 165.0
    add_outline(h, width, _outline_mask(h))
    for o in _children(rig, h):
        if not any(k in o.name.lower() for k in ("sclera", "iris", "brow", "lash", "mouth", "nose")):
            add_outline(o, width)
