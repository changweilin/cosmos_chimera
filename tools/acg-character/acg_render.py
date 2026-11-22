"""Turnaround renders and FBX export for ACG characters (ADR-0013)."""
import math
import os

import bpy
from mathutils import Matrix, Quaternion, Vector

# Matches the turnaround sheets: front, profile facing image-left, back, slight 3/4.
VIEWS = (("front", 0.0), ("side", -90.0), ("back", 180.0), ("q34", 25.0))


def t_pose(rig, spec):
    """Raises the arms to the sheets' T-pose; MakeHuman's bind pose is an A-pose."""
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    for side, sign in (("l", 1), ("r", -1)):
        pb = rig.pose.bones["upperarm_" + side]
        pb.rotation_mode = "XYZ"
        arm = (pb.tail - pb.head)
        # Rotate in armature space so the upper arm points straight out along X.
        want = Vector((sign, 0, 0))
        q = arm.normalized().rotation_difference(want)
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = (pb.bone.matrix_local.to_quaternion().inverted() @ q @ pb.bone.matrix_local.to_quaternion())
        for name in ("lowerarm_" + side, "hand_" + side):
            rig.pose.bones[name].rotation_mode = "QUATERNION"
    bpy.ops.object.mode_set(mode="OBJECT")
    # The A-pose forearm also bends ~45 deg forward; straighten forearm and hand along the upper arm,
    # or sleeve tubes and cuffs authored along X stand off the forearm and the hand leaves the sleeve.
    for side, sign in (("l", 1), ("r", -1)):
        art_pose(rig, {"aim": [["lowerarm_" + side, [sign, 0, 0]], ["hand_" + side, [sign, 0, 0]]]})
    bpy.context.view_layer.update()


def art_pose(rig, pose):
    """Poses the rig from the spec's "idle_pose".

    "aim": [[bone, [x, y, z]], ...] turns each bone (parents first) so head->tail points along that
    body-space direction (character faces -Y, +X is its left); "level": [bone, ...] restores a
    bone's rest orientation in body space (feet stay flat after the legs spread).
    Pre: object mode, bones in QUATERNION rotation mode. Side effects: edits pose bones.
    """
    for name, d in pose.get("aim", []):
        bpy.context.view_layer.update()
        pb = rig.pose.bones[name]
        m = pb.matrix.copy()
        q = (pb.tail - pb.head).normalized().rotation_difference(Vector(d).normalized())
        head = m.translation.copy()
        pb.matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ m
    # "curl": [[bone, degrees], ...] bends a finger joint toward the palm (bone-local +X rotation
    # swings the tail toward local +Z, the palm side on this rig).
    for name, deg in pose.get("curl", []):
        pb = rig.pose.bones[name]
        pb.rotation_quaternion = pb.rotation_quaternion @ Quaternion((1, 0, 0), math.radians(deg))
    for name in pose.get("level", []):
        bpy.context.view_layer.update()
        pb = rig.pose.bones[name]
        m = pb.bone.matrix_local.copy()
        m.translation = pb.matrix.translation
        pb.matrix = m
    bpy.context.view_layer.update()


def idle_action(rig, spec):
    """Keys the illustration stance as a one-pose action "Idle_Art" (characters.md: T-pose bind,
    stance as idle clip). Returns the action, or None when the spec has no "idle_pose".

    Side effects: assigns the action to the rig, so the rig evaluates to the stance afterwards.
    """
    pose = spec.get("idle_pose")
    if not pose:
        return None
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
    art_pose(rig, pose)
    import acg_swing
    acg_swing.settle(rig)
    rig.animation_data_create()
    for frame in (0, 1):
        for pb in rig.pose.bones:
            pb.keyframe_insert("rotation_quaternion", frame=frame, group=pb.name)
            pb.keyframe_insert("location", frame=frame, group=pb.name)
    act = rig.animation_data.action
    act.name = "Idle_Art"
    sc = bpy.context.scene
    sc.frame_start, sc.frame_end = 0, 1
    sc.frame_set(0)
    return act


def _bounds(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    lo = Vector((1e9, 1e9, 1e9))
    hi = -lo
    for o in objs:
        if o.type != "MESH" or o.hide_render:
            continue
        e = o.evaluated_get(dg)
        for c in e.bound_box:
            w = e.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi


def _setup_scene(spec):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x = 900
    sc.render.resolution_y = 1400
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"
    w = bpy.data.worlds.get("acg") or bpy.data.worlds.new("acg")
    w.use_nodes = True
    nt = w.node_tree
    bg = next((n for n in nt.nodes if n.type == "BACKGROUND"), None)
    if bg is None:
        nt.nodes.clear()
        bg = nt.nodes.new("ShaderNodeBackground")
        nt.links.new(bg.outputs[0], nt.nodes.new("ShaderNodeOutputWorld").inputs[0])
    bg.inputs[0].default_value = (0.21, 0.21, 0.22, 1)
    bg.inputs[1].default_value = 1.0
    sc.world = w
    if "acg_sun" not in bpy.data.objects:
        ld = bpy.data.lights.new("acg_sun", "SUN")
        ld.energy = 3.0
        ld.angle = math.radians(5)
        sun = bpy.data.objects.new("acg_sun", ld)
        sc.collection.objects.link(sun)
    return sc


def render_views(rig, spec, out_dir, views=VIEWS, faces=True, prefix=""):
    """Renders the sheet views as <out_dir>/<id>_<prefix><view>.png (plus face close-ups)."""
    os.makedirs(out_dir, exist_ok=True)
    sc = _setup_scene(spec)
    objs = [o for o in bpy.data.objects if o.type == "MESH"]
    lo, hi = _bounds(objs)
    height = hi.z - lo.z
    cam_d = bpy.data.cameras.get("acg_cam") or bpy.data.cameras.new("acg_cam")
    cam_d.type = "ORTHO"
    cam_d.ortho_scale = max(height * 1.08, (hi.x - lo.x) * 1.08 * 1400 / 900)
    cam = bpy.data.objects.get("acg_cam") or bpy.data.objects.new("acg_cam", cam_d)
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    sc.camera = cam
    center = (lo + hi) / 2
    sun = bpy.data.objects["acg_sun"]
    paths = []
    for name, yaw in views:
        a = math.radians(yaw)
        # Character faces -Y; the camera orbits it so yaw 90 shows the profile facing image-left.
        d = Vector((-math.sin(a), -math.cos(a), 0))
        cam.location = center + d * 10
        cam.rotation_euler = (math.radians(90), 0, math.atan2(d.x, -d.y))
        sun.rotation_euler = (math.radians(50), 0, math.atan2(d.x, -d.y) + math.radians(30))
        sc.render.filepath = os.path.join(out_dir, "%s_%s%s.png" % (spec["id"], prefix, name))
        bpy.ops.render.render(write_still=True)
        paths.append(sc.render.filepath)
    if not faces:
        sc.render.resolution_x, sc.render.resolution_y = 900, 1400
        print("RENDER_OK", height, paths)
        return paths
    # Face close-ups: front and 3/4, framed on the head joint.
    head = rig.matrix_world @ rig.pose.bones["head"].head
    top = hi.z
    fc = Vector((center.x, center.y, (head.z + top) / 2))
    cam_d.ortho_scale = (top - head.z) * 1.9
    sc.render.resolution_x = sc.render.resolution_y = 900
    for name, yaw in (("face", 0.0), ("face34", 30.0), ("faceside", -90.0)):
        a = math.radians(yaw)
        d = Vector((-math.sin(a), -math.cos(a), 0))
        cam.location = fc + d * 10
        cam.rotation_euler = (math.radians(90), 0, math.atan2(d.x, -d.y))
        sun.rotation_euler = (math.radians(50), 0, math.atan2(d.x, -d.y) + math.radians(30))
        sc.render.filepath = os.path.join(out_dir, "%s_%s.png" % (spec["id"], name))
        bpy.ops.render.render(write_still=True)
        paths.append(sc.render.filepath)
    print("RENDER_OK", height, paths)
    return paths


def _to_srgb_hex(c):
    def enc(x):
        x = max(0.0, min(1.0, x))
        return 12.92 * x if x <= 0.0031308 else 1.055 * x ** (1 / 2.4) - 0.055
    return "#%02x%02x%02x" % tuple(round(enc(x) * 255) for x in c[:3])


def _fbx_materials():
    """Routes each cel material's lit colour through a Principled BSDF: the FBX exporter reads
    only that node. Returns {material: {base, shade, bands, texture}} (sRGB hex) for the Unity
    importer, which rebuilds the cel look on Cosmimera/ToonOutlined: FBX carries no shade colour,
    and a lone base colour under URP Lit washes light skin out to the white of the clothes."""
    table = {}
    for m in bpy.data.materials:
        if not m.use_nodes or m.name == "acg_outline":
            continue
        nt = m.node_tree
        out = next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"), None)
        if out is None:
            continue
        ramp = next((n for n in nt.nodes if n.type == "VALTORGB"), None)
        img = next((n.image for n in nt.nodes if n.type == "TEX_IMAGE" and n.image), None)
        entry = {"base": _to_srgb_hex(m.diffuse_color), "shade": None, "bands": 2,
                 "texture": img.name if img else None}
        if ramp is not None:
            els = ramp.color_ramp.elements
            entry["shade"] = _to_srgb_hex(els[0].color)
            entry["bands"] = 3 if len(els) > 2 else 2
            if img is None:
                entry["base"] = _to_srgb_hex(els[-1].color)
        table[m.name] = entry
        p = nt.nodes.new("ShaderNodeBsdfPrincipled")
        p.inputs["Base Color"].default_value = m.diffuse_color
        p.inputs["Roughness"].default_value = 1.0
        tex = next((n for n in nt.nodes if n.type == "TEX_IMAGE" and n.image), None)
        if tex is not None:
            nt.links.new(tex.outputs[0], p.inputs["Base Color"])
        nt.links.new(p.outputs[0], out.inputs[0])
    return table


def _limit_weights(ob, bones, limit):
    """Keeps each vertex's `limit` strongest bone weights, renormalised to sum 1 (non-bone groups untouched)."""
    groups = {vg.index: vg for vg in ob.vertex_groups if vg.name in bones}
    for v in ob.data.vertices:
        ws = sorted(((g.weight, g.group) for g in v.groups if g.group in groups and g.weight > 0), reverse=True)
        if not ws:
            continue
        keep = ws[:limit]
        tot = sum(w for w, _ in keep)
        for w, gi in ws[limit:]:
            groups[gi].remove([v.index])
        for w, gi in keep:
            groups[gi].add([v.index], w / tot, "REPLACE")


def export_fbx(rig, path):
    """Exports armature and meshes in bind pose (T-pose) for Unity Humanoid import. When the rig has
    an action (Idle_Art), it goes to a separate armature-only file "<name>@<action>.fbx" (Unity's
    model@clip convention).

    Separate files because the exporter writes node transforms that follow a baked take, and Unity
    builds the Humanoid avatar from those transforms, so they must stay in the T-pose.
    Side effects: unassigns the action (kept with a fake user) during the model export, resets the
    pose to rest, disables render-only outline modifiers and rewires materials in this session.
    Also writes "<name>_materials.json" (cel colours per material, see _fbx_materials).
    Returns the list of written paths.
    """
    act = rig.animation_data.action if rig.animation_data else None
    if act is not None:
        act.use_fake_user = True
        rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
    bpy.context.view_layer.update()
    for o in bpy.data.objects:
        for mod in getattr(o, "modifiers", []):
            if mod.name == "acg_outline":
                mod.show_viewport = False
                mod.show_render = False
        if o.type == "MESH":
            # The outline slot is unused once the hull is gone; drop it so Unity gets no stray material.
            slots = [i for i, s in enumerate(o.material_slots) if s.material and s.material.name == "acg_outline"]
            for i in reversed(slots):
                o.data.materials.pop(index=i)
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    # Generated images live only in memory; save them beside the FBX so the importer finds them.
    for img in bpy.data.images:
        if img.source == "GENERATED" or (not img.filepath and img.has_data):
            img.filepath_raw = os.path.join(os.path.dirname(os.path.abspath(path)), img.name)
            img.file_format = "PNG"
            img.save()
    import json
    base = os.path.splitext(os.path.abspath(path))[0]
    with open(base + "_materials.json", "w", encoding="utf-8", newline="\n") as f:
        # A list, not a map: Unity's JsonUtility reads arrays of records but not dictionaries.
        rows = [dict(name=k, **v) for k, v in sorted(_fbx_materials().items())]
        json.dump({"materials": rows}, f, indent=1, sort_keys=True)
        f.write("\n")
    common = dict(use_selection=True, add_leaf_bones=False, apply_unit_scale=True,
                  apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y")
    # Export from a collection, not the selection: hidden props (hide_viewport) cannot be selected
    # but must still ship so Unity can swap them in.
    coll = bpy.data.collections.new("acg_export")
    coll.objects.link(rig)
    for o in rig.children_recursive:
        if o.type == "MESH":
            coll.objects.link(o)
    # Unity skins at most 4 bones per vertex and drops the rest unnormalised; MakeHuman knees and
    # elbows carry more, so posed knees kinked in Unity while Blender looked right. Trim here so
    # both agree.
    bones = set(rig.data.bones.keys())
    for o in coll.objects:
        if o.type == "MESH" and o.vertex_groups:
            _limit_weights(o, bones, 4)
    model = dict(common, use_selection=False, use_custom_props=True)
    bpy.ops.export_scene.fbx(filepath=os.path.abspath(path), collection=coll.name, object_types={"ARMATURE", "MESH"},
                             mesh_smooth_type="FACE", use_mesh_modifiers=True, bake_anim=False,
                             path_mode="RELATIVE", embed_textures=False, **model)
    bpy.data.collections.remove(coll)
    out = [path]
    if act is not None:
        anim_path = os.path.splitext(path)[0] + "@" + act.name + ".fbx"
        rig.animation_data.action = act
        # Unity collapses a lone top-level node into the file root; a second top-level node keeps
        # "<rig>/Root" so the copied Humanoid avatar finds the same hierarchy as in the model file.
        keep = bpy.data.objects.new("acg_keep_root", None)
        bpy.context.scene.collection.objects.link(keep)
        bpy.ops.object.select_all(action="DESELECT")
        rig.select_set(True)
        keep.select_set(True)
        bpy.ops.export_scene.fbx(filepath=os.path.abspath(anim_path), object_types={"ARMATURE", "EMPTY"}, bake_anim=True,
                                 # The scene-take path drops a pose that never changes; per-action keeps it.
                                 bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                                 bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, **common)
        bpy.data.objects.remove(keep)
        out.append(anim_path)
    return out
