"""Render a colour-coded preview of a split car, to eyeball the panel cuts.

    blender --background --python tools/blender/preview_split.py -- \
        --input <car_split.fbx> --output <preview.png> [--size 900]

Each Part* object gets its own flat colour, everything else stays grey. Uses
the Workbench engine, so a render is well under a second and needs no GPU.
Reading the triangle report is not enough -- a region can be numerically
plausible and still cut a door in half.
"""

import sys
import os
import argparse
import math

import bpy
from mathutils import Vector


PART_COLORS = {
    "PartHood":    (0.95, 0.30, 0.25, 1.0),
    "PartTrunk":   (0.20, 0.55, 0.95, 1.0),
    "PartDoorL":   (0.35, 0.85, 0.40, 1.0),
    "PartDoorR":   (0.95, 0.80, 0.20, 1.0),
    "PartBumperF": (0.85, 0.40, 0.90, 1.0),
    "PartBumperR": (0.30, 0.85, 0.85, 1.0),
    "PartMirrorL": (1.00, 0.55, 0.10, 1.0),
    "PartMirrorR": (0.60, 0.35, 0.95, 1.0),
    # Box-van rear doors, kept from the source model rather than carved.
    "PartBoxDoorL": (0.20, 0.85, 0.55, 1.0),
    "PartBoxDoorR": (0.95, 0.45, 0.55, 1.0),
    "InteriorShell": (0.05, 0.05, 0.06, 1.0),
    "Body":        (0.75, 0.75, 0.78, 1.0),
}
DEFAULT_COLOR = (0.45, 0.45, 0.48, 1.0)


def argv_after_dashes():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def parse_args():
    p = argparse.ArgumentParser(prog="preview_split")
    p.add_argument("--input", required=True)
    p.add_argument("--output", required=True)
    p.add_argument("--size", type=int, default=900)
    return p.parse_args(argv_after_dashes())


def scene_bounds():
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for ob in bpy.data.objects:
        if ob.type != "MESH":
            continue
        for c in ob.bound_box:
            w = ob.matrix_world @ Vector(c)
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])
    return lo, hi


def add_camera(centre, radius, angle_deg, elev_deg):
    a = math.radians(angle_deg)
    e = math.radians(elev_deg)
    d = radius * 2.6
    pos = Vector((
        centre.x + d * math.cos(a) * math.cos(e),
        centre.y + d * math.sin(a) * math.cos(e),
        centre.z + d * math.sin(e),
    ))
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    bpy.context.collection.objects.link(cam)
    cam.location = pos
    direction = centre - pos
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = cam
    return cam


def main():
    args = parse_args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=args.input)

    for ob in bpy.data.objects:
        if ob.type != "MESH":
            continue
        ob.color = PART_COLORS.get(ob.name, DEFAULT_COLOR)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "OBJECT"
    shading.show_cavity = True
    scene.render.film_transparent = False
    scene.render.resolution_x = args.size
    scene.render.resolution_y = args.size
    scene.render.image_settings.file_format = "PNG"

    lo, hi = scene_bounds()
    centre = (lo + hi) * 0.5
    radius = max((hi - lo).length * 0.5, 0.001)

    # Three-quarter front-left and three-quarter rear-right: between them
    # every carved panel is visible in at least one frame.
    views = [(35.0, 18.0, "_a"), (215.0, 18.0, "_b")]
    base, ext = os.path.splitext(args.output)
    out_dir = os.path.dirname(os.path.abspath(args.output))
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)

    for angle, elev, suffix in views:
        cam = add_camera(centre, radius, angle, elev)
        scene.render.filepath = base + suffix + (ext or ".png")
        bpy.ops.render.render(write_still=True)
        print("wrote %s" % scene.render.filepath)
        bpy.data.objects.remove(cam, do_unlink=True)


main()
