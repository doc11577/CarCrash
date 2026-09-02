"""Generate the Bullseye map: a descending run-up, a kicker, and a giant dartboard to land on.

    blender --background --python tools/blender/build_dartboard.py -- \
        --output Assets/Art/Tracks/Bullseye/bullseye.fbx \
        --preview /tmp/bullseye.png

Why generated rather than downloaded: every dartboard model online is a 45 cm wall prop, built
to be looked at from two metres with a texture doing all the work. This needs to be 180 m
across, dished so a car settles into it, cheap enough for a Chromebook -- and above all the
GAME HAS TO KNOW THE RINGS. Scoring by where the car lands means the code needs ring radii and
segment angles as numbers, and a downloaded mesh only gives triangles. (The licences are also
wrong: Sketchfab "free" is usually CC-BY, and TurboSquid/CGTrader forbid redistribution, which
is fatal for a public repo.)

Layout, along +Y with +Z up -- the same convention as build_course.py:

    apron -> descent -> runout -> kicker -> GAP -> board

Everything about the flight is arithmetic, and the report prints it: the range for a given exit
speed decides where the bullseye goes, not the other way round.
"""

import sys
import os
import math
import argparse

import bpy
import bmesh
from mathutils import Vector

TAU = math.pi * 2.0

# Reuse the other maps' material names so the SAME Unity materials light this one -- the
# textures are already imported and assigned for Quarry.
MAT_GROUND = "CourseGround"
MAT_ROCK = "CourseRock"

# Board colours. Separate objects per colour rather than a texture: the board is flat and
# enormous, so a texture would need to be vast to hold a crisp ring edge, while a colour split
# is exact at any size and costs five draw calls for the whole thing.
BOARD_MATERIALS = {
    "gold":  ("BoardGold",  (0.95, 0.78, 0.10, 1.0)),
    "red":   ("BoardRed",   (0.78, 0.10, 0.11, 1.0)),
    "blue":  ("BoardBlue",  (0.20, 0.55, 0.85, 1.0)),
    "black": ("BoardBlack", (0.08, 0.08, 0.09, 1.0)),
    "white": ("BoardWhite", (0.92, 0.92, 0.90, 1.0)),
    "line":  ("BoardLine",  (0.05, 0.05, 0.06, 1.0)),
    "wire":  ("BoardWire",  (0.45, 0.45, 0.48, 1.0)),
}

# A standard 5-colour, 10-ring ARCHERY target face: ten equal-width concentric bands, gold in
# the middle, no radial divisions at all.
#
# This replaced a real dartboard, and it is better for the game as well as simpler to look at.
# A dartboard's single band covers most of the disc, so scoring needed 20 numbered segments on
# top of the rings just to spread the results out. An archery face is already ten equal graded
# bands scoring 10 down to 1, so the ring alone answers "how good was that landing" -- which is
# the only question this map asks.
#
# THESE MUST MATCH DartboardScore.cs. They are duplicated rather than shared because one is
# Python run at build time and the other is C# run at play time, and there is no sane way to
# share a constant across that gap -- so the generator PRINTS them and the component's defaults
# are set from that print. If a landing scores the wrong ring, this table is the first place to
# look.
RINGS = [
    # (outer fraction, score, colour)
    (0.1, 10, "gold"),
    (0.2,  9, "gold"),
    (0.3,  8, "red"),
    (0.4,  7, "red"),
    (0.5,  6, "blue"),
    (0.6,  5, "blue"),
    (0.7,  4, "black"),
    (0.8,  3, "black"),
    (0.9,  2, "white"),
    (1.0,  1, "white"),
]

# Thin dividing line at each ring boundary, as a fraction of the radius. Without it rings 10
# and 9 are one gold disc and 8 and 7 are one red one -- the pairs share a colour, which is
# exactly why a real target prints lines between them.
LINE_WIDTH = 0.006

# Real targets use black lines, and WHITE lines inside the black rings, because a black line on
# black is not a line.
LINE_ON_BLACK = "white"


def argv_after_dashes():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def parse_args():
    p = argparse.ArgumentParser(prog="build_dartboard")
    p.add_argument("--output", required=True, help="FBX path to write.")
    p.add_argument("--preview", default="", help="Optional PNG to render.")

    p.add_argument("--apron", type=float, default=80.0,
                   help="Flat start pad before the descent, in metres.")
    p.add_argument("--descent", type=float, default=430.0,
                   help="Length of the descending section.")
    p.add_argument("--drop", type=float, default=230.0,
                   help="How far the descent falls. Sets the speed at the lip.")
    p.add_argument("--runout", type=float, default=55.0,
                   help="Flat metres between the bottom of the descent and the kicker, so the "
                        "suspension settles before the launch.")
    p.add_argument("--kicker", type=float, default=55.0,
                   help="Length of the launch ramp.")
    p.add_argument("--kicker-angle", type=float, default=19.0,
                   help="Exit angle at the lip, in degrees. The single biggest control over "
                        "where the car lands.")
    p.add_argument("--width", type=float, default=30.0,
                   help="Drivable width of the run-up.")
    p.add_argument("--berm", type=float, default=7.0,
                   help="Height of the raised edge either side of the run-up.")

    p.add_argument("--gap", type=float, default=178.0,
                   help="Metres from the lip to the CENTRE of the board. Set this from the "
                        "predicted range the report prints, not by eye.")
    p.add_argument("--radius", type=float, default=98.0,
                   help="Board radius. 98 gives a 196 m board, sized so every speed from ~26 m/s up to the fastest the ramp can produce lands somewhere on it.")
    p.add_argument("--below", type=float, default=35.0,
                   help="How far the board sits below the flat runout. Together with the kicker "
                        "rise this is the height the car falls.")
    p.add_argument("--dish", type=float, default=7.0,
                   help="How much the board dips toward the middle, so a landing settles into "
                        "it instead of skating off. The 'floor to catch'.")
    p.add_argument("--rim", type=float, default=11.0,
                   help="Height of the lip around the board.")

    p.add_argument("--rings", type=int, default=44,
                   help="Radial subdivisions of the board. Controls how smooth the dish is.")
    p.add_argument("--arc", type=int, default=120,
                   help="Angular steps around the board. This is what stops a 200 m circle "
                        "looking like a polygon; there are no radial divisions to align to.")
    p.add_argument("--skirt", type=float, default=150.0,
                   help="How far the sides drop below the surface. NOT decoration: a heightfield "
                        "has a top and no underside, so without this the board and the ramp are "
                        "back-face-culled ribbons floating in the sky the moment you see them "
                        "from below or beyond. Already cost this project two rounds on Quarry.")
    p.add_argument("--cell", type=float, default=3.0,
                   help="Grid resolution of the run-up.")
    p.add_argument("--chunk", type=float, default=100.0,
                   help="Run-up chunk length. Same frustum-culling argument as build_course.")
    return p.parse_args(argv_after_dashes())


# ---------------------------------------------------------------------------- geometry maths

def smoothstep(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3.0 - 2.0 * x)


def kicker_rise(args):
    """Rise of the launch ramp implied by its length and exit angle.

    The kicker is a parabola, z = rise * u^2, so its slope is zero where it meets the flat
    runout -- no crease to unsettle the car -- and steepest exactly at the lip. Slope at the
    lip is 2*rise/len, so the rise follows from the angle rather than being guessed.
    """
    return math.tan(math.radians(args.kicker_angle)) * args.kicker * 0.5


def runup_length(args):
    return args.apron + args.descent + args.runout + args.kicker


def runup_height(args, y):
    """Height of the run-up centreline at distance y from the start."""
    rise = kicker_rise(args)

    if y <= args.apron:
        return 0.0

    y -= args.apron
    if y <= args.descent:
        # Smoothstep, so the top of the descent is not a cliff edge and the bottom is not a
        # crease that bottoms the suspension out just before the launch.
        return -args.drop * smoothstep(y / args.descent)

    y -= args.descent
    if y <= args.runout:
        return -args.drop

    y -= args.runout
    u = min(1.0, y / args.kicker)
    return -args.drop + rise * u * u


def lip(args):
    """(y, z) of the launch lip."""
    y = runup_length(args)
    return y, runup_height(args, y)


def board_centre(args):
    ly, lz = lip(args)
    return Vector((0.0, ly + args.gap, -args.drop - args.below))


def flight_range(args, speed):
    """Where a car leaving the lip at `speed` lands, in metres past the lip.

    Plain projectile motion. It ignores drag and the car's own rotation, which is exactly the
    right amount of physics for placing a target -- the point is to size the gap so the middle
    of the board is reachable at a sensible speed, not to predict a landing to the metre.
    """
    theta = math.radians(args.kicker_angle)
    vx = speed * math.cos(theta)
    vy = speed * math.sin(theta)

    _, lz = lip(args)
    h = lz - (-args.drop - args.below)          # fall from lip to board plane
    if h < 0.0:
        h = 0.0

    t = (vy + math.sqrt(vy * vy + 2.0 * 9.81 * h)) / 9.81
    return vx * t


def board_height(args, r):
    """Board surface height relative to its centre plane, at radius r."""
    R = args.radius
    if r <= R:
        # Dish: deepest in the middle, flush with the plane at the rim. Quadratic rather than
        # conical so the middle is genuinely flat-ish and a car settles rather than sliding.
        u = r / R
        return -args.dish * (1.0 - u * u)

    # Rim wall, to catch a car that would otherwise skate off the far side.
    u = min(1.0, (r - R) / max(0.001, R * 0.07))
    return args.rim * smoothstep(u)


def ring_at(args, r):
    """(index, score, colour) of the ring containing radius r."""
    u = r / args.radius
    for i, (outer, score, colour) in enumerate(RINGS):
        if u <= outer:
            return i, score, colour
    return len(RINGS) - 1, RINGS[-1][1], RINGS[-1][2]


def colour_at(args, r):
    """Face colour at radius r, including the thin boundary lines."""
    u = r / args.radius
    if u > 1.0:
        return "wire"

    _i, _score, colour = ring_at(args, r)

    # Inside a boundary line? Checked against every ring edge except the outermost, which is
    # where the rim takes over anyway.
    for outer, _score, ring_colour in RINGS[:-1]:
        if abs(u - outer) <= LINE_WIDTH * 0.5:
            return LINE_ON_BLACK if ring_colour == "black" else "line"

    return colour


# ---------------------------------------------------------------------------- scene helpers

def wipe_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def ensure_material(name, colour):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
    if not mat.use_nodes:
        mat.use_nodes = True
    mat.diffuse_color = colour
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = colour
        if "Roughness" in bsdf.inputs:
            bsdf.inputs["Roughness"].default_value = 0.9
        if "Metallic" in bsdf.inputs:
            bsdf.inputs["Metallic"].default_value = 0.0
    return mat


def mesh_object(name, verts, faces, material, flat=True):
    """Build one mesh object. Returns (object, triangle count)."""
    mesh = bpy.data.meshes.new(name + "Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.validate()

    if flat:
        for poly in mesh.polygons:
            poly.use_smooth = False

    mesh.materials.append(material)

    ob = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(ob)

    tris = sum(max(0, len(p.vertices) - 2) for p in mesh.polygons)
    return ob, tris


# ---------------------------------------------------------------------------- the run-up

def build_runup(args, ground, rock):
    """Chunked ramp with a raised berm either side."""
    total = runup_length(args)
    half = args.width * 0.5
    cols = max(2, int(round(args.width / args.cell)) + 1)

    # Cross-section: drivable width, then a berm shoulder on each side.
    offsets = []
    for c in range(cols):
        offsets.append(-half + args.width * c / (cols - 1))
    offsets = [-half - 10.0] + offsets + [half + 10.0]

    chunks = []
    tris = 0
    n = max(1, int(math.ceil(total / args.chunk)))

    for k in range(n):
        y0 = k * args.chunk
        y1 = min(total, (k + 1) * args.chunk)
        rows = max(2, int(round((y1 - y0) / args.cell)) + 1)

        verts = []
        for r in range(rows):
            y = y0 + (y1 - y0) * r / (rows - 1)
            z = runup_height(args, y)
            for x in offsets:
                lift = args.berm if abs(x) > half + 0.001 else 0.0
                verts.append(Vector((x, y, z + lift)))

        faces = []
        stride = len(offsets)
        for r in range(rows - 1):
            for c in range(stride - 1):
                a = r * stride + c
                faces.append((a, a + 1, a + stride + 1, a + stride))

        # Skirts down both outer edges, so the ramp is a solid-looking wall from the side
        # rather than a ribbon you can see straight through. Its own vertex per row, never a
        # shared pair -- reusing two corners for the whole run is what built a giant slab
        # beside Quarry's start bay.
        left_top, right_top = 0, stride - 1
        for edge, top, flip in ((0, left_top, False), (1, right_top, True)):
            base = len(verts)
            for r in range(rows):
                v = verts[r * stride + top]
                verts.append(Vector((v.x, v.y, v.z - args.skirt)))
            for r in range(rows - 1):
                a = r * stride + top
                b = (r + 1) * stride + top
                lo_a, lo_b = base + r, base + r + 1
                faces.append((a, lo_a, lo_b, b) if flip else (a, b, lo_b, lo_a))

        ob, t = mesh_object(f"RunUp{k:02d}", verts, faces, ground)
        chunks.append(ob)
        tris += t

    return chunks, tris


# ---------------------------------------------------------------------------- the board

def build_board(args, materials):
    """The dartboard: one mesh per colour, plus the rim.

    Faces are grouped by COLOUR rather than built as one mesh, because a ring boundary has to
    be exact. A texture on a 180 m disc would need to be enormous to keep the double ring crisp,
    and would still blur under a camera that gets within a few metres of it on landing.
    """
    centre = board_centre(args)
    R = args.radius
    outer = R * 1.07

    ang_total = max(24, args.arc)

    # Radial stations: every ring boundary AND both edges of every boundary line is an exact
    # station, so no colour ever bleeds across a scoring edge. Extra stations are spread between
    # them purely to make the dish smooth.
    edges = set()
    prev = 0.0
    for outer_frac, _score, _colour in RINGS:
        edges.add(round(outer_frac, 6))
        edges.add(round(max(0.0, outer_frac - LINE_WIDTH * 0.5), 6))
        edges.add(round(min(1.0, outer_frac + LINE_WIDTH * 0.5), 6))

        span = outer_frac - prev
        steps = max(1, int(round(args.rings * span)))
        for s in range(1, steps):
            edges.add(round(prev + span * s / steps, 6))
        prev = outer_frac

    stations = [0.0] + [f * R for f in sorted(edges) if f > 1e-9]
    stations.append(outer)

    groups = {key: ([], []) for key in materials}

    def add_quad(key, p0, p1, p2, p3):
        verts, faces = groups[key]
        base = len(verts)
        verts.extend([p0, p1, p2, p3])
        faces.append((base, base + 1, base + 2, base + 3))

    for a in range(ang_total):
        a0 = (a / ang_total) * TAU
        a1 = ((a + 1) / ang_total) * TAU

        for i in range(len(stations) - 1):
            r0, r1 = stations[i], stations[i + 1]
            mid = (r0 + r1) * 0.5

            key = colour_at(args, mid)

            z0 = board_height(args, r0)
            z1 = board_height(args, r1)

            p0 = centre + Vector((math.sin(a0) * r0, math.cos(a0) * r0, z0))
            p1 = centre + Vector((math.sin(a1) * r0, math.cos(a1) * r0, z0))
            p2 = centre + Vector((math.sin(a1) * r1, math.cos(a1) * r1, z1))
            p3 = centre + Vector((math.sin(a0) * r1, math.cos(a0) * r1, z1))

            # r0 == 0 collapses to a point: emit a triangle rather than a degenerate quad,
            # which would otherwise leave a pinhole at the exact centre of the bullseye.
            if r0 <= 1e-6:
                verts, faces = groups[key]
                base = len(verts)
                verts.extend([centre + Vector((0.0, 0.0, z0)), p2, p3])
                faces.append((base, base + 1, base + 2))
            else:
                add_quad(key, p0, p1, p2, p3)

    # Skirt and bottom cap, so the board is a solid drum rather than a floating disc. It is
    # seen from below and from the side for the whole flight, which is exactly the view a
    # heightfield has nothing to show.
    skirt_verts, skirt_faces = groups["wire"]
    top_z = board_height(args, outer)
    bottom_z = top_z - args.skirt

    rim_ring = []
    for a in range(ang_total + 1):
        ang = (a / ang_total) * TAU
        rim_ring.append(centre + Vector((math.sin(ang) * outer, math.cos(ang) * outer, 0.0)))

    for a in range(ang_total):
        p0 = rim_ring[a] + Vector((0.0, 0.0, top_z))
        p1 = rim_ring[a + 1] + Vector((0.0, 0.0, top_z))
        base = len(skirt_verts)
        skirt_verts.extend([p0, p1,
                            Vector((p1.x, p1.y, centre.z + bottom_z)),
                            Vector((p0.x, p0.y, centre.z + bottom_z))])
        skirt_faces.append((base, base + 1, base + 2, base + 3))

    # Bottom cap, wound so it faces DOWN. An unwound cap is invisible from underneath, which
    # defeats the point of adding one at all.
    cap_centre = len(skirt_verts)
    skirt_verts.append(Vector((centre.x, centre.y, centre.z + bottom_z)))
    for a in range(ang_total):
        p0 = rim_ring[a]
        p1 = rim_ring[a + 1]
        base = len(skirt_verts)
        skirt_verts.extend([Vector((p0.x, p0.y, centre.z + bottom_z)),
                            Vector((p1.x, p1.y, centre.z + bottom_z))])
        skirt_faces.append((cap_centre, base, base + 1))

    objects = []
    tris = 0
    for key, (verts, faces) in groups.items():
        if not faces:
            continue
        name, _colour = BOARD_MATERIALS[key]
        ob, t = mesh_object("Board" + key.capitalize(), verts, faces, materials[key])
        objects.append(ob)
        tris += t

    return objects, tris


# ---------------------------------------------------------------------------- export & report

def build_markers(args):
    """Empties at the two places Unity needs to know about.

    Exported rather than printed as coordinates, because the Blender -> Unity axis conversion is
    exactly the kind of transcription that goes wrong silently: a spawn 40 m out is a car in a
    wall, and a board centre 40 m out scores every landing in the wrong ring. Dragging a marker
    into an Inspector slot cannot be off by a sign.
    """
    spawn_y = args.apron * 0.35
    spawn = bpy.data.objects.new("SpawnPoint", None)
    spawn.empty_display_size = 6.0
    spawn.location = Vector((0.0, spawn_y, runup_height(args, spawn_y) + 2.0))
    bpy.context.collection.objects.link(spawn)

    centre = bpy.data.objects.new("BoardCentre", None)
    centre.empty_display_size = 12.0
    centre.location = board_centre(args) + Vector((0.0, 0.0, board_height(args, 0.0)))
    bpy.context.collection.objects.link(centre)

    return spawn, centre


def export_fbx(path):
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        axis_forward="-Z",
        axis_up="Y",
        # Same reason as build_course: without it every chunk arrives in Unity carrying a
        # (-90, 0, 0) node rotation.
        bake_space_transform=True,
        object_types={"MESH", "EMPTY"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        path_mode="COPY",
    )


def report(args, tris):
    ly, lz = lip(args)
    c = board_centre(args)
    fall = lz - c.z

    print("\n=== BULLSEYE ===")
    print(f"  run-up      {runup_length(args):.0f} m, dropping {args.drop:.0f} m")
    print(f"  kicker      {args.kicker:.0f} m at {args.kicker_angle:.0f} deg, "
          f"rise {kicker_rise(args):.1f} m")
    print(f"  lip         y {ly:.0f}, z {lz:.1f}")
    print(f"  board       centre y {c.y:.0f}, z {c.z:.1f}, radius {args.radius:.0f} m "
          f"({args.radius * 2:.0f} m across)")
    print(f"  fall to board {fall:.1f} m")
    print(f"  triangles   {tris}")

    print("\n  --- where a car lands, by exit speed ---")
    print("  (gap to the board CENTRE is %.0f m; near edge %.0f m, far edge %.0f m)"
          % (args.gap, args.gap - args.radius, args.gap + args.radius))
    for speed in (18.0, 22.0, 26.0, 30.0, 32.0, 36.0, 44.0):
        rng = flight_range(args, speed)
        off = rng - args.gap
        if abs(off) > args.radius:
            where = "MISSES the board" + (" (long)" if off > 0 else " (short)")
        else:
            _i, score, colour = ring_at(args, abs(off))
            where = f"scores {score}  ({colour})"
        print(f"    {speed:5.1f} m/s ({speed * 3.6:5.1f} km/h) -> {rng:6.1f} m  "
              f"{off:+7.1f} from centre   {where}")

    print("\n  --- ring table, COPY THESE INTO DartboardScore ---")
    prev = 0.0
    for outer, score, colour in RINGS:
        print(f"    {prev:5.3f} .. {outer:5.3f} R  ({prev * args.radius:6.1f} .. "
              f"{outer * args.radius:6.1f} m)  scores {score:2d}  {colour}")
        prev = outer

    print("\n  Unity: put the board's PlayerSpawn-equivalent target at "
          f"({c.x:.1f}, {c.z:.1f}, {c.y:.1f}) in Unity axes (x, y, z).")
    print("  Spawn the player on the apron, around y 20 in Blender = z -20 in Unity.")


def render_preview(path, args):
    """Two views: the side profile that matches the sketch, and the board from above."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 800
    scene.render.film_transparent = False

    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "MATERIAL"

    c = board_centre(args)
    total = runup_length(args)

    cam_data = bpy.data.cameras.new("Cam")
    # The course is ~600 m long; Blender's default 100 m clip renders an empty frame.
    cam_data.clip_end = 5000.0
    cam = bpy.data.objects.new("Cam", cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam

    sun_data = bpy.data.lights.new("Sun", type="SUN")
    sun = bpy.data.objects.new("Sun", sun_data)
    bpy.context.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50), 0.0, math.radians(35))

    base, ext = os.path.splitext(path)

    # Side elevation, looking across the course -- the view the sketch is drawn in.
    mid_y = c.y * 0.55
    cam.location = Vector((-1500.0, mid_y, -args.drop * 0.4))
    cam.rotation_euler = (math.radians(88), 0.0, math.radians(-90))
    cam_data.lens = 42.0
    scene.render.filepath = base + "_side" + (ext or ".png")
    bpy.ops.render.render(write_still=True)
    print("  wrote", scene.render.filepath)

    # Board from above and behind, so the rings read.
    cam.location = c + Vector((0.0, -args.radius * 2.1, args.radius * 1.7))
    cam.rotation_euler = (math.radians(52), 0.0, 0.0)
    cam_data.lens = 42.0
    scene.render.filepath = base + "_board" + (ext or ".png")
    bpy.ops.render.render(write_still=True)
    print("  wrote", scene.render.filepath)

    # Down the run-up from behind the start, so the ramp and the target are in one frame.
    cam.location = Vector((0.0, -70.0, 70.0))
    cam.rotation_euler = (math.radians(74), 0.0, 0.0)
    cam_data.lens = 32.0
    scene.render.filepath = base + "_run" + (ext or ".png")
    bpy.ops.render.render(write_still=True)
    print("  wrote", scene.render.filepath)


def main():
    args = parse_args()
    wipe_scene()

    ground = ensure_material(MAT_GROUND, (0.42, 0.38, 0.33, 1.0))
    rock = ensure_material(MAT_ROCK, (0.34, 0.30, 0.27, 1.0))
    board_mats = {key: ensure_material(name, colour)
                  for key, (name, colour) in BOARD_MATERIALS.items()}

    _chunks, run_tris = build_runup(args, ground, rock)
    _board, board_tris = build_board(args, board_mats)
    build_markers(args)

    report(args, run_tris + board_tris)

    if args.preview:
        render_preview(args.preview, args)

    export_fbx(args.output)
    print(f"\n=== wrote {args.output} ===")


main()
