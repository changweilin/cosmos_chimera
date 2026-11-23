"""Renders NPC race bases side by side at one common scale, to compare silhouettes and heads by eye.

Usage: blender -b --python tools/acg-character/npc_lineup.py -- --out <dir> <spec.json>...
Post: <dir>/<id>_front.png, _side.png (2.4 m frame, feet on the bottom edge) and _face.png, _faceside.png
(head frame of the tallest head in the set). Side effects: overwrites those files.
"""
import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import acg_render  # noqa: E402
import build  # noqa: E402

FRAME_M = 2.4


def _shot(sc, cam, target, yaw, scale, path):
    a = math.radians(yaw)
    d = Vector((-math.sin(a), -math.cos(a), 0))
    cam.data.ortho_scale = scale
    cam.location = target + d * 10
    cam.rotation_euler = (math.radians(90), 0, math.atan2(d.x, -d.y))
    bpy.data.objects["acg_sun"].rotation_euler = (math.radians(50), 0, math.atan2(d.x, -d.y) + math.radians(30))
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
    p.add_argument("specs", nargs="+")
    a = p.parse_args(argv)
    a.out = os.path.abspath(a.out)
    os.makedirs(a.out, exist_ok=True)
    for path in a.specs:
        spec = json.load(open(path, encoding="utf-8"))
        spec["hair"], spec["outfit"] = None, []
        h, rig = build.build(spec)
        for o in list(bpy.data.objects):
            if o.name.startswith("prop_"):
                bpy.data.objects.remove(o)
        sc = acg_render._setup_scene(spec)
        cam = bpy.data.objects.get("acg_cam")
        if cam is None:
            cam = bpy.data.objects.new("acg_cam", bpy.data.cameras.new("acg_cam"))
            sc.collection.objects.link(cam)
        cam.data.type = "ORTHO"
        sc.camera = cam
        sc.render.resolution_x, sc.render.resolution_y = 500, 1000
        body = Vector((0, 0, FRAME_M / 2))
        _shot(sc, cam, body, 0.0, FRAME_M, os.path.join(a.out, spec["id"] + "_front.png"))
        _shot(sc, cam, body, -90.0, FRAME_M, os.path.join(a.out, spec["id"] + "_side.png"))
        head = rig.matrix_world @ rig.pose.bones["head"].head
        top = max((h.matrix_world @ v.co).z for v in h.data.vertices)
        sc.render.resolution_x = sc.render.resolution_y = 600
        fc = Vector((0, head.y, (head.z + top) / 2 - 0.02))
        span = (top - head.z) * 2.0
        _shot(sc, cam, fc, 0.0, span, os.path.join(a.out, spec["id"] + "_face.png"))
        _shot(sc, cam, fc, -90.0, span, os.path.join(a.out, spec["id"] + "_faceside.png"))
        print("LINEUP_OK", spec["id"])


if __name__ == "__main__":
    main()
