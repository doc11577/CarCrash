"""Cut a welded car mesh into separately detachable panels for CarCrash.

    blender --background --python tools/blender/split_car.py -- \
        --input  <model.glb|fbx|obj> \
        --output <out.fbx> \
        [--tris 11000] [--length-axis auto] [--no-shell]

Why this exists: no free car pack ships doors, hood, trunk and mirrors as
separate objects (checked Kenney, Quaternius, RgsDev, Sketchfab CC0 tags --
they all separate wheels only). So we cut them ourselves.

Pipeline:
  1. Import, and join every exterior mesh into one body.
  2. Decimate to the triangle budget. Realistic proportions survive
     decimation; that is the whole trick behind "realistic but low poly".
  3. Carve panels out of the body by region test -- a face belongs to a panel
     if its median point falls inside that panel's normalised bounding box.
  4. Give each panel an origin at its hinge, so it rotates correctly before
     it detaches and tumbles correctly after.
  5. Build a dark interior shell so a missing door does not show a hollow car.
  6. Export FBX with -Z forward / +Y up, which is what Unity wants.

Panel regions are expressed as fractions of the body bounding box, so the same
config works on any car that is roughly car-shaped. Tune REGIONS, re-run, look
at the report. Nothing here is destructive -- the source file is never written.
"""

import sys
import os
import re
import argparse

import bpy
import bmesh
from mathutils import Vector


# Panel regions as (min, max) fractions of the body bounding box, in
# (length, width, height) space where length runs nose->tail, width runs
# left->right, and height runs floor->roof. A face is claimed by the FIRST
# region that contains it, so order matters.
REGIONS = [
    # name           length        width         height        hinge
    # Mirrors first: they sit inside the door region and must claim their
    # faces before the door test runs.
    # Width range must be very tight. Mirrors are the widest thing on the car
    # but only by a few percent, so a loose range grabs a slab of front wing
    # instead of the mirror.
    ("PartMirrorL", (0.30, 0.46), (0.00, 0.06), (0.50, 0.74), "inner", False),
    ("PartMirrorR", (0.30, 0.46), (0.94, 1.00), (0.50, 0.74), "inner", False),
    # Bumpers claim the nose and tail before hood and trunk. Height is capped
    # at 0.45 -- an uncapped rear bumper region swallows the whole boot lid.
    ("PartBumperF", (0.00, 0.09), (0.00, 1.00), (0.00, 0.45), "none",  False),
    ("PartBumperR", (0.91, 1.00), (0.00, 1.00), (0.00, 0.45), "none",  False),
    ("PartHood",    (0.09, 0.32), (0.06, 0.94), (0.42, 1.00), "rear",  False),
    ("PartTrunk",   (0.76, 0.93), (0.06, 0.94), (0.42, 1.00), "front", False),
    # Doors run up past the beltline, and are the ONLY region allowed to take glass, so
    # the side window leaves with the door -- which is what the reference footage does.
    ("PartDoorL",   (0.34, 0.64), (0.00, 0.34), (0.16, 0.72), "front", True),
    ("PartDoorR",   (0.34, 0.64), (0.66, 1.00), (0.16, 0.72), "front", True),
]

# Material names containing any of these are glass. Only regions with allow_glass may
# claim such a face. Without this the trunk region takes the rear WINDSCREEN instead of
# the boot lid -- both sit high and just behind the rear axle, so position alone cannot
# tell them apart, but a boot lid is never made of glass.
GLASS_HINTS = ("glass", "window", "windscreen", "windshield")

# Objects below this many triangles are left alone by the group decimator.
# There is nothing to reclaim from a 56-triangle wheel centre cap, and the
# shared ratio was reducing them to 2 triangles of noise.
DECIMATE_FLOOR = 250

# Carved BEFORE the interior shell is built. The shell is a shrunken copy of the body, so
# anything that sticks out past the bodywork -- mirrors -- leaves a stub on the shell that
# pokes through the gap and reads as a dark blob. Removing them first keeps the shell inside
# the car's core volume. Everything else is carved after, because the shell must still have
# the door and hood surfaces it exists to hide.
PRE_SHELL_PARTS = {"PartMirrorL", "PartMirrorR"}

# How far the interior shell is scaled in toward the body centre. 0.93 gives
# roughly 8 cm of clearance on a 4.5 m car -- enough that a decimated shell
# stays hidden, close enough that a missing door shows dark interior rather
# than a visible gap to a small floating shape.
SHELL_SCALE = 0.93

# Classification is by whole NAME TOKEN, never substring. Substring matching
# put "trim" in the wheel bucket because it contains "rim", which silently
# skipped decimation on 13k triangles. Names are split on non-letters and
# trailing digits are stripped, so "rim_fl", "wheel.003" and "tire" all match.
WHEEL_TOKENS = {"wheel", "wheels", "rim", "rims", "tire", "tires", "tyre",
                "tyres", "brake", "hub", "nut", "nuts", "centre", "center"}

# Dropped entirely. Interior detail is the biggest waste of triangles in a
# chase-cam game -- the camera never resolves a dashboard. Checked BEFORE
# wheels, so "steering_centre" drops rather than being kept as a wheel part.
DROP_TOKENS = {"steering", "carpet", "leather", "interior", "seat", "seats",
               "dash", "dashboard", "wiper", "wipers", "brakes", "pedal",
               "pedals", "gauge", "gauges"}


def argv_after_dashes():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def parse_args():
    p = argparse.ArgumentParser(prog="split_car")
    p.add_argument("--input", required=True)
    p.add_argument("--output", required=True)
    p.add_argument("--tris", type=int, default=11000,
                   help="triangle budget for the whole car after decimation")
    p.add_argument("--length-axis", default="auto", choices=["auto", "x", "y", "z"])
    p.add_argument("--scale", type=float, default=1.0,
                   help="uniform scale applied on import. Use 0.001 for a model authored "
                        "in millimetres, which most 3ds Max OBJ exports are.")
    p.add_argument("--no-shell", action="store_true",
                   help="skip the dark interior shell")
    p.add_argument("--keep-interior", action="store_true",
                   help="do not drop interior meshes")
    return p.parse_args(argv_after_dashes())


def wipe_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def load(path):
    ext = os.path.splitext(path)[1].lower()
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=path)
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=path)
    elif ext == ".obj":
        bpy.ops.wm.obj_import(filepath=path)
    else:
        raise SystemExit("unsupported input extension: " + ext)


def mesh_objects():
    return [o for o in bpy.data.objects if o.type == "MESH"]


def scale_scene(factor):
    """Uniformly rescale everything at the root. Needed for millimetre-authored models."""
    if abs(factor - 1.0) < 1e-9:
        return
    for ob in bpy.data.objects:
        if ob.parent is None:
            ob.scale = [s * factor for s in ob.scale]
            ob.location = [c * factor for c in ob.location]
    print("  scaled scene by %g" % factor)


def sanity_check_size(body):
    """Shout if the car is not car-sized. Wrong units silently ruin every derived number."""
    length = max(body.dimensions)
    if 2.0 <= length <= 8.0:
        return
    hint = "--scale 0.001" if length > 100 else ("--scale 0.01" if length > 20 else "--scale 100")
    print("  !! WARNING: car is %.2f m long, which is not a car. Try %s." % (length, hint))


def name_tokens(ob):
    """Split an object name into lowercase word tokens, digits stripped.

    'rim_fl' -> {rim, fl}; 'wheel.003' -> {wheel}; 'LowTire001' -> {low, tire}.
    CamelCase is split too, because 3ds Max exports name things like 'LowTire001' and
    without the split that reads as one token 'lowtire', misses the 'tire' hint, and the
    wheels get classified as bodywork. Token equality is what stops 'trim' matching 'rim'.
    """
    spaced = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", ob.name)
    raw = re.split(r"[^A-Za-z0-9]+", spaced.lower())
    return {re.sub(r"\d+$", "", t) for t in raw if t}


def name_matches(ob, tokens):
    return bool(name_tokens(ob) & tokens)


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def select_only(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0] if objs else None


def apply_all_transforms(objs):
    if not objs:
        return
    select_only(objs)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def join_body(bodies):
    """Weld every exterior mesh into a single object named Body."""
    select_only(bodies)
    if len(bodies) > 1:
        bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    body.name = "Body"
    body.data.name = "BodyMesh"
    return body


def decimate_to(ob, target_tris):
    current = tri_count(ob)
    if current <= target_tris:
        print("  decimate: %d tris already within budget %d" % (current, target_tris))
        return
    ratio = max(0.005, target_tris / float(current))
    mod = ob.modifiers.new("Decimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = ob
    select_only([ob])
    bpy.ops.object.modifier_apply(modifier=mod.name)
    print("  decimate: %d -> %d tris (ratio %.4f)" % (current, tri_count(ob), ratio))


def decimate_group(objs, target_tris, label):
    """Decimate a set of objects to a shared budget, proportionally.

    Splitting the budget evenly across objects would crush a tyre to the same
    triangle count as a wheel nut. One shared ratio keeps their relative
    detail intact.
    """
    objs = [o for o in objs if tri_count(o) > 0]
    if not objs:
        return
    current = sum(tri_count(o) for o in objs)
    if current <= target_tris:
        print("  decimate %s: %d tris already within budget %d"
              % (label, current, target_tris))
        return

    # Tiny objects are exempt, but their triangles still count against the
    # budget, so the big objects absorb the shortfall.
    small = [o for o in objs if tri_count(o) < DECIMATE_FLOOR]
    big = [o for o in objs if tri_count(o) >= DECIMATE_FLOOR]
    if not big:
        print("  decimate %s: all %d objects under floor, left alone"
              % (label, len(objs)))
        return
    reserved = sum(tri_count(o) for o in small)
    big_current = sum(tri_count(o) for o in big)
    ratio = max(0.005, (target_tris - reserved) / float(big_current))
    for ob in big:
        mod = ob.modifiers.new("Decimate", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = ratio
        mod.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = ob
        select_only([ob])
        bpy.ops.object.modifier_apply(modifier=mod.name)
    after = sum(tri_count(o) for o in objs)
    print("  decimate %s: %d -> %d tris across %d objects (ratio %.4f)"
          % (label, current, after, len(objs), ratio))


def world_bounds(ob):
    cs = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    lo = Vector((min(c.x for c in cs), min(c.y for c in cs), min(c.z for c in cs)))
    hi = Vector((max(c.x for c in cs), max(c.y for c in cs), max(c.z for c in cs)))
    return lo, hi


def world_centre(ob):
    lo, hi = world_bounds(ob)
    return (lo + hi) * 0.5


def corner_name(centre, body_centre, axes):
    """WheelFL / FR / RL / RR from position. Front is the low end of the length axis."""
    la, wa, _ = axes
    front = "F" if centre[la] < body_centre[la] else "R"
    side = "L" if centre[wa] < body_centre[wa] else "R"
    return "Wheel" + front + side


def split_loose(objs):
    """Break each object into its disconnected islands.

    Packs routinely ship all four wheels as one or two objects -- the E30 has both front
    wheels welded into 'LowTire' and both rears into 'LowTire001'. Corner grouping cannot
    separate what is a single object, so split first and regroup after.
    """
    out = []
    for ob in list(objs):
        before = set(bpy.data.objects)
        select_only([ob])
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.separate(type="LOOSE")
        bpy.ops.object.mode_set(mode="OBJECT")
        out.append(ob)
        out.extend(o for o in (set(bpy.data.objects) - before) if o.type == "MESH")
    return out


def find_wheels_by_shape(objs, min_diameter=0.30, max_diameter=1.30):
    """Recover wheels from a welded car with no wheel-shaped NAMES, by looking at geometry.

    Most free car models are one welded mesh with one material -- the De Tomaso P72 arrives as
    a single object called `s_0070`, so every name and material test finds nothing and the car
    ends up with no wheels at all. The parts are still THERE, just anonymous.

    Splitting into disconnected islands recovers them, because a wheel is never welded to the
    bodywork even when it shares an object with it. A wheel is then identifiable by shape
    alone: a disc, meaning two similar large dimensions and one much smaller, at a plausible
    road-wheel size.

    Deliberately shape-based and not position-based. Corners are only meaningful once the
    model's axes are known, and the axes are derived from the body -- which is what this runs
    before. Shape needs no frame of reference.

    Returns (wheels, remaining), both lists of objects. `remaining` is every island that is
    not part of a wheel and still has to be rejoined into the body.
    """
    islands = split_loose(objs)

    # Straight after mesh.separate, ob.dimensions is STALE until the depsgraph re-evaluates,
    # so every island reports the size of the object it was cut from and no shape test can
    # work. Measuring the bounding box in world space avoids depending on that entirely.
    def world_dims(ob):
        pts = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
        lo = Vector((min(p[i] for p in pts) for i in range(3)))
        hi = Vector((max(p[i] for p in pts) for i in range(3)))
        return hi - lo

    discs = []
    for ob in islands:
        d = sorted(world_dims(ob))
        thin, mid, wide = d[0], d[1], d[2]

        if wide < min_diameter or wide > max_diameter:
            continue
        if thin <= 1e-6 or wide / thin < 1.8:
            continue          # a box, not a disc
        if mid / wide < 0.82:
            continue          # an oval or a fin, not a wheel

        discs.append((wide, ob))

    if len(discs) < 3:
        return [], islands

    # Pick the largest MATCHED SET, not the largest disc.
    #
    # Picking the biggest disc does not work, and the P72 shows exactly why: a body panel
    # measuring 0.31 x 0.78 x 0.85 passes the disc test and is larger than the 0.68 wheels, so
    # it becomes the reference and excludes them. Size alone cannot tell a wheel from a curved
    # panel that happens to be disc-shaped.
    #
    # What IS distinctive is repetition. Wheels are the only thing on a car that appears four
    # times at an identical size. Grouping by diameter and requiring at least three members
    # rejects every one-off panel without needing to know what a wheel looks like.
    discs.sort(key=lambda pair: -pair[0])

    groups = []
    for wide, ob in discs:
        for group in groups:
            if abs(group[0][0] - wide) <= group[0][0] * 0.08:
                group.append((wide, ob))
                break
        else:
            groups.append([(wide, ob)])

    matched = [g for g in groups if len(g) >= 3]
    if not matched:
        return [], islands

    # Largest diameter among the sets that repeat: brake discs and hub caps also come in
    # fours, and they are always smaller than the wheel they sit inside.
    matched.sort(key=lambda g: -g[0][0])
    biggest = matched[0][0][0]
    wheels = [ob for wide, ob in matched[0]]

    # Absorb whatever sits inside each wheel: brake disc, caliper, hub. These are separate
    # islands at the same centre, and a wheel that leaves its own brake disc behind on the
    # car looks like a bug the moment it detaches.
    centres = [(world_centre(w), biggest * 0.55) for w in wheels]
    claimed = set(w.name for w in wheels)

    for ob in islands:
        if ob.name in claimed:
            continue
        centre = world_centre(ob)
        for wheel_centre, reach in centres:
            if (centre - wheel_centre).length <= reach:
                wheels.append(ob)
                claimed.add(ob.name)
                break

    remaining = [ob for ob in islands if ob.name not in claimed]

    print("  wheels found by SHAPE: %d island(s), %.2f m diameter "
          "(the model had no wheel-shaped names)" % (len(wheels), biggest))

    return wheels, remaining


def group_wheels(wheels, body, axes):
    """Join each corner's parts into one object, origin at the wheel centre, named by corner.

    Two things make this mandatory rather than tidy-up:

      1. Packs split a wheel across several objects (tyre, rim, brake, nuts). CarController
         drives ONE transform per corner, so they have to be joined.
      2. Every wheel arrives with its origin at the model origin, not at the wheel. A
         transform whose origin is elsewhere cannot be positioned or spun -- assigning it
         to Wheel.visual flings the mesh across the car. Both "wheels don't spin" and
         "wheels sink through the road under suspension compression" are this one defect:
         with no usable visual the wheels stay welded into the body and cannot travel.
    """
    if not wheels:
        return []

    wheels = split_loose(wheels)

    bc = world_centre(body)
    groups = {}
    for ob in wheels:
        groups.setdefault(corner_name(world_centre(ob), bc, axes), []).append(ob)

    result = []
    for name in sorted(groups):
        parts = groups[name]
        select_only(parts)
        if len(parts) > 1:
            bpy.ops.object.join()
        wheel = bpy.context.view_layer.objects.active
        wheel.name = name
        wheel.data.name = name + "Mesh"

        # Origin to the wheel's own centre, so rotating the transform spins the tyre
        # about its axle rather than swinging it around the car.
        select_only([wheel])
        bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")

        print("  %-8s %d object(s) -> %5d tris, origin at wheel centre"
              % (name, len(parts), tri_count(wheel)))
        result.append(wheel)

    return result


def ground_the_model(objects, wheels, axes):
    """Drop everything so the lowest point of the wheels sits exactly on zero.

    Models are not reliably authored with the tyres on the origin plane. A few millimetres
    of error reads as the car sunk into the road, and it silently corrupts every ride-height
    number derived from the mesh.
    """
    if not wheels:
        return 0.0

    ha = axes[2]
    bottom = min(world_bounds(w)[0][ha] for w in wheels)
    if abs(bottom) < 1e-6:
        return 0.0

    for ob in objects:
        ob.location[ha] -= bottom

    print("  grounded: moved everything %+.4f m so the tyres rest on zero" % -bottom)
    return bottom


def shell_height_between(objects, axes, to_unity, z_lo, z_hi, fallback):
    """Tallest point of the bodywork within a slice of the car, in Unity height.

    The nose box must be as tall as the BONNET, not as tall as the roof. Sizing it from the
    car's global maximum makes an invisible slab above the bonnet that collides with
    overhangs the car should duck under.
    """
    best = None
    for ob in objects:
        for v in ob.data.vertices:
            u = to_unity(ob.matrix_world @ v.co)
            if z_lo <= u[2] <= z_hi and (best is None or u[1] > best):
                best = u[1]
    return best if best is not None else fallback


def report_unity_setup(body, wheels, axes, travel=0.30, panels=None):
    """Print the exact numbers to type into the Unity Inspector.

    Blender is Z-up and this exports with -Z forward / Y up, so Unity sees
    (x, y, z)_unity = (x, z, -y)_blender. Deriving these by hand every time is how the
    ride height ends up wrong.
    """
    la, wa, ha = axes

    def to_unity(v):
        out = [0.0, 0.0, 0.0]
        out[0] = v[wa]
        out[1] = v[ha]
        out[2] = -v[la]
        return out

    print("\n=== UNITY SETUP ===")

    radius = 0.0
    if wheels:
        radius = max(world_bounds(w)[1][ha] - world_bounds(w)[0][ha] for w in wheels) * 0.5
        anchor_y = radius + (2.0 / 3.0) * travel
        print("  CarController:  wheelRadius = %.3f   suspensionTravel = %.2f" % (radius, travel))
        print("  Wheel anchors (local, anchorY = radius + 2/3 x travel = %.3f):" % anchor_y)

        # Anchors are forced symmetric. Models are rarely perfectly mirrored -- the E30's
        # wheel centres came out at -0.698 and +0.740 -- and asymmetric suspension anchors
        # make the car pull to one side for no visible reason. Half-track is the mean of
        # the magnitudes; front and rear keep their own Z, which is a real difference.
        centres = {w.name: to_unity(world_centre(w)) for w in wheels}
        half_track = sum(abs(c[0]) for c in centres.values()) / len(centres)
        z_front = max(c[2] for c in centres.values())
        z_rear = min(c[2] for c in centres.values())

        for name in sorted(centres):
            x = -half_track if name.endswith("L") else half_track
            z = z_front if name[5] == "F" else z_rear
            print("    %-8s (%7.3f, %6.3f, %7.3f)" % (name, x, anchor_y, z))

        spread = max(abs(abs(c[0]) - half_track) for c in centres.values())
        if spread > 0.01:
            print("    (model is asymmetric by %.3f m across; anchors symmetrised)" % spread)

    # Bounds must span every panel, not just Body -- the bumpers and hood have already been
    # carved out of Body by this point, so measuring Body alone loses the ends of the car.
    shell = panels if panels else [body]
    x0 = y0 = z0 = 1e9
    x1 = y1 = z1 = -1e9
    for ob in shell:
        lo, hi = world_bounds(ob)
        for corner in (to_unity(lo), to_unity(hi)):
            x0, x1 = min(x0, corner[0]), max(x1, corner[0])
            y0, y1 = min(y0, corner[1]), max(y1, corner[1])
            z0, z1 = min(z0, corner[2]), max(z1, corner[2])
    print("  Body bounds (Unity): x %.2f..%.2f  y %.2f..%.2f  z %.2f..%.2f"
          % (x0, x1, y0, y1, z0, z1))

    if not wheels:
        return

    zf = max(to_unity(world_centre(w))[2] for w in wheels)
    zr = min(to_unity(world_centre(w))[2] for w in wheels)
    width = x1 - x0
    clear = max(0.30, y0 + 0.06)

    import math

    # Measured per region so the nose box is bonnet-high and the tail box is boot-high.
    nose_top = shell_height_between(shell, axes, to_unity, zf, z1, y1)
    tail_top = shell_height_between(shell, axes, to_unity, z0, zr, y1)
    lift = 0.55

    boxes = [
        ("Core", (0.0, (clear + y1) * 0.5, (zr + zf) * 0.5),
                 (width, y1 - clear, zf - zr)),
        ("Nose", (0.0, (lift + nose_top) * 0.5, (zf + z1) * 0.5),
                 (width * 0.96, max(0.1, nose_top - lift), z1 - zf)),
        ("Tail", (0.0, (lift + tail_top) * 0.5, (z0 + zr) * 0.5),
                 (width * 0.96, max(0.1, tail_top - lift), zr - z0)),
    ]
    print("  Box colliders on the Car GameObject:")
    for name, c, s in boxes:
        print("    %-5s centre (%.2f, %.2f, %.3f)  size (%.2f, %.2f, %.2f)"
              % (name, c[0], c[1], c[2], s[0], s[1], s[2]))

    approach = math.degrees(math.atan2(0.55, max(0.01, z1 - zf)))
    departure = math.degrees(math.atan2(0.55, max(0.01, zr - z0)))
    breakover = 2.0 * math.degrees(math.atan2(clear, max(0.01, (zf - zr) * 0.5)))
    print("  Angles: approach %.1f deg  departure %.1f deg  breakover %.1f deg"
          % (approach, departure, breakover))

    print("  Materials on the body (assign a transparent one to any glass slot):")
    for slot in body.data.materials:
        print("    %s" % (slot.name if slot else "None"))


def axis_order(ob, forced):
    """Return (length, width, height) as axis indices 0/1/2.

    Cars are longer than they are wide, and wider than they are tall. That
    ordering is stable enough to detect automatically, which matters because
    glTF and FBX disagree about which way is up.
    """
    d = list(ob.dimensions)
    order = sorted(range(3), key=lambda i: d[i], reverse=True)
    length, width, height = order[0], order[1], order[2]
    if forced != "auto":
        length = "xyz".index(forced)
        rest = [i for i in range(3) if i != length]
        rest.sort(key=lambda i: d[i], reverse=True)
        width, height = rest[0], rest[1]
    names = "xyz"
    print("  axes: length=%s width=%s height=%s  dims=%.2f x %.2f x %.2f" % (
        names[length], names[width], names[height], d[0], d[1], d[2]))
    return length, width, height


def local_bounds(ob):
    cs = [Vector(c) for c in ob.bound_box]
    lo = Vector((min(c.x for c in cs), min(c.y for c in cs), min(c.z for c in cs)))
    hi = Vector((max(c.x for c in cs), max(c.y for c in cs), max(c.z for c in cs)))
    return lo, hi


def normalise(p, lo, hi):
    out = []
    for i in range(3):
        span = hi[i] - lo[i]
        out.append((p[i] - lo[i]) / span if span > 1e-9 else 0.5)
    return out


def carve(body, region, axes, lo, hi):
    """Separate the faces inside region into their own object."""
    name, lr, wr, hr, hinge, allow_glass = region
    la, wa, ha = axes

    # Which material slots on this mesh are glass, resolved once per region.
    glass_slots = set()
    for i, mat in enumerate(body.data.materials):
        if mat is not None and any(h in mat.name.lower() for h in GLASS_HINTS):
            glass_slots.add(i)

    bpy.context.view_layer.objects.active = body
    select_only([body])
    bpy.ops.object.mode_set(mode="EDIT")
    bm = bmesh.from_edit_mesh(body.data)
    bm.faces.ensure_lookup_table()

    hit = 0
    for f in bm.faces:
        f.select = False
    for f in bm.faces:
        if not allow_glass and f.material_index in glass_slots:
            continue
        n = normalise(f.calc_center_median(), lo, hi)
        if (lr[0] <= n[la] <= lr[1] and
                wr[0] <= n[wa] <= wr[1] and
                hr[0] <= n[ha] <= hr[1]):
            f.select = True
            hit += 1
    bmesh.update_edit_mesh(body.data)

    if hit == 0:
        bpy.ops.object.mode_set(mode="OBJECT")
        print("  %-12s SKIPPED - region matched no faces" % name)
        return None

    before = set(bpy.data.objects)
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")
    new = list(set(bpy.data.objects) - before)
    if not new:
        print("  %-12s SKIPPED - separate produced nothing" % name)
        return None

    panel = new[0]
    panel.name = name
    panel.data.name = name + "Mesh"
    set_hinge_origin(panel, hinge, axes)
    print("  %-12s %5d faces -> %5d tris, hinge=%s" % (
        name, hit, tri_count(panel), hinge))
    return panel


def set_hinge_origin(ob, hinge, axes):
    """Put the object origin where the part physically pivots.

    A door spinning about its centroid looks like a thrown plate. A door
    spinning about its hinge edge looks like a door.
    """
    la, wa, ha = axes
    bpy.context.view_layer.objects.active = ob
    select_only([ob])

    if hinge == "none":
        bpy.ops.object.origin_set(type="ORIGIN_GEOMETRY", center="BOUNDS")
        return

    lo, hi = local_bounds(ob)
    centre = (lo + hi) * 0.5
    point = Vector(centre)
    if hinge == "front":
        point[la] = lo[la]
    elif hinge == "rear":
        point[la] = hi[la]
    elif hinge == "inner":
        # mirrors hinge against the body, i.e. toward the car centreline
        point[wa] = hi[wa] if centre[wa] < 0 else lo[wa]

    world = ob.matrix_world @ point
    prev = tuple(bpy.context.scene.cursor.location)
    bpy.context.scene.cursor.location = world
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.context.scene.cursor.location = prev


def build_interior_shell(body, budget):
    """A dark inward-facing copy of the body, so holes read as an interior.

    Must be built from the INTACT body, before any panel is carved out --
    a shell copied from an already-carved body has the same holes it is
    meant to hide.

    Cheap on purpose: decimate hard, shrink slightly, flip the normals. No
    solidify -- a single-sided inward-facing shell renders correctly from
    outside a hole and costs exactly the triangles it has. Solidify was
    costing 16k tris for something the camera reads as "dark in there".
    """
    shell = body.copy()
    shell.data = body.data.copy()
    shell.name = "InteriorShell"
    shell.data.name = "InteriorShellMesh"
    bpy.context.collection.objects.link(shell)

    decimate_to(shell, budget)

    # Uniform scale about the body centre, NOT a normal offset. Offsetting
    # along normals fails on a heavily decimated shell: it deviates from the
    # body by more than the offset distance and erupts through the paint.
    # A car is roughly convex, so scaling toward its centre stays inside.
    lo, hi = local_bounds(body)
    centre = (lo + hi) * 0.5
    for v in shell.data.vertices:
        v.co = centre + (v.co - centre) * SHELL_SCALE

    bpy.context.view_layer.objects.active = shell
    select_only([shell])
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.flip_normals()
    bpy.ops.object.mode_set(mode="OBJECT")

    mat = bpy.data.materials.new("CarInterior")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (0.02, 0.02, 0.025, 1.0)
        if "Roughness" in bsdf.inputs:
            bsdf.inputs["Roughness"].default_value = 0.9
    shell.data.materials.clear()
    shell.data.materials.append(mat)
    print("  InteriorShell %5d tris" % tri_count(shell))
    return shell


def export_fbx(path):
    out_dir = os.path.dirname(os.path.abspath(path))
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        axis_forward="-Z",
        axis_up="Y",
        object_types={"MESH", "EMPTY"},
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=False,
    )


def main():
    args = parse_args()
    print("\n=== split_car: %s ===" % args.input)
    wipe_scene()
    load(args.input)
    scale_scene(args.scale)

    meshes = mesh_objects()
    if not meshes:
        raise SystemExit("no mesh objects in input")

    wheels, bodies, dropped = [], [], []
    for ob in meshes:
        # Drop is checked first: "steering_centre" is dashboard trim, not a
        # wheel centre cap, and the wheel test would otherwise claim it.
        if not args.keep_interior and name_matches(ob, DROP_TOKENS):
            dropped.append(ob)
        elif name_matches(ob, WHEEL_TOKENS):
            wheels.append(ob)
        else:
            bodies.append(ob)

    # Nothing matched by name. Either the model has no wheels, or -- far more likely, since
    # most free car models are one welded mesh -- it has them under a name that says nothing.
    if not wheels and bodies:
        wheels, bodies = find_wheels_by_shape(bodies)
        if not wheels:
            print("  !! no wheels by name OR by shape. The car will have no wheels: check "
                  "WHEEL_TOKENS, or whether this model has separate wheel geometry at all.")

    print("  classified: %d body, %d wheel, %d dropped" % (
        len(bodies), len(wheels), len(dropped)))
    print("    body  : %s" % ", ".join(sorted(o.name for o in bodies)))
    print("    wheel : %s" % ", ".join(sorted(o.name for o in wheels)))
    if not bodies:
        raise SystemExit("no body meshes found -- check DROP_TOKENS/WHEEL_TOKENS")

    for ob in dropped:
        bpy.data.objects.remove(ob, do_unlink=True)

    apply_all_transforms(bodies + wheels)
    body = join_body(bodies)

    # The body is what the camera sees, so it gets most of the budget. Four
    # wheels share the rest -- they are small on screen and mostly spinning.
    body_budget = int(args.tris * 0.7)
    decimate_to(body, body_budget)
    decimate_group(wheels, int(args.tris * 0.3), "wheels")

    axes = axis_order(body, args.length_axis)
    sanity_check_size(body)

    print("  --- wheels ---")
    wheels = group_wheels(wheels, body, axes)

    lo, hi = local_bounds(body)

    # Bounds are captured once, from the intact body, and reused for every region. Letting
    # them shift as faces are removed would move every later region's frame of reference.
    print("  --- carving protruding parts ---")
    for region in REGIONS:
        if region[0] in PRE_SHELL_PARTS:
            carve(body, region, axes, lo, hi)

    shell = None
    if not args.no_shell:
        shell = build_interior_shell(body, max(400, int(args.tris * 0.08)))

    print("  --- carving panels ---")
    for region in REGIONS:
        if region[0] not in PRE_SHELL_PARTS:
            carve(body, region, axes, lo, hi)

    ground_the_model(mesh_objects(), wheels, axes)

    print("  --- result ---")
    total = 0
    for ob in sorted(mesh_objects(), key=lambda o: o.name):
        t = tri_count(ob)
        total += t
        print("    %-20s %6d tris" % (ob.name, t))
    print("    %-20s %6d tris" % ("TOTAL", total))

    # Mirrors are excluded from the collision bounds on purpose: they are fragile
    # protrusions, and letting them set the box width makes the car 0.37 m wider than its
    # bodywork for collision, so it clips kerbs and doorways it should clear.
    panels = [o for o in mesh_objects()
              if o not in wheels
              and o.name != "InteriorShell"
              and "Mirror" not in o.name]
    report_unity_setup(body, wheels, axes, panels=panels)

    export_fbx(args.output)
    print("=== wrote %s ===" % args.output)


main()
