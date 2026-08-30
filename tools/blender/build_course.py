"""
Generate a downhill crash course: a descending corridor cut through rising walls.

Run headless:

    BL="/c/Program Files (x86)/Steam/steamapps/common/Blender/blender.exe"
    "$BL" --background --python tools/blender/build_course.py -- \
      --output Assets/Art/Tracks/Quarry01/quarry01.fbx \
      --length 1800 --drop 270 --theme quarry

Why this is generated rather than downloaded: no CC0 or CC-BY asset exists that is a
drivable downhill destruction course. The good-looking terrain models are photogrammetry
scale (229k tris for one canyon, against an 11.6k-tri car), the CC0 modular kits are toy
low-poly, and the open-source racing games ship GPL or ShareAlike track data. What the
BeamNG reference maps actually are is a sculpted hillside plus placed obstacles -- the
realism lives in the texture and the silhouette, not the triangle count, which is the same
trade already made on the E30.

The layout is also the part no asset could supply. Where the pitches steepen, where the
run-out is, how tight the walls close in -- that is the game design.

WHAT IT MAKES

A centreline that descends and snakes, swept into a cross-section: a drivable corridor in
the middle, shoulders rising into walls either side. Surface noise makes it bumpy rather
than paved. Output is one FBX of CHUNK objects, two materials, plus a printed
=== UNITY SETUP === block with the numbers to type in.

Everything is measured and printed rather than asserted: real length, real drop, max
gradient, minimum turn radius, triangles per chunk. Read those before tuning.
"""

import argparse
import math
import os
import sys

import bpy
import bmesh
from mathutils import Vector

TAU = math.pi * 2.0

# Chunking is the whole performance story, so the default is deliberate rather than
# incidental. One mesh would be a single draw call with NO frustum culling -- every triangle
# in the course submitted every frame. At 100 m a chunk, four to six are ever visible.
DEFAULT_CHUNK = 100.0

# Grid resolution along and across the course. 2 m is coarse enough to keep the triangle
# count sane over nearly two kilometres and fine enough that surface noise reads as bumps a
# 0.41 m wheel can feel.
DEFAULT_CELL = 2.0

MAT_GROUND = "CourseGround"
MAT_ROCK = "CourseRock"

# Viewport colours only -- real textures are assigned in Unity, the same way the car's are.
THEMES = {
    "quarry": {
        "ground": (0.42, 0.38, 0.33, 1.0),
        "rock": (0.34, 0.30, 0.27, 1.0),
    },
    "jungle": {
        "ground": (0.35, 0.31, 0.22, 1.0),
        "rock": (0.24, 0.30, 0.20, 1.0),
    },
    "desert": {
        "ground": (0.60, 0.48, 0.33, 1.0),
        "rock": (0.52, 0.38, 0.26, 1.0),
    },
}


# ---------------------------------------------------------------------------- args


def argv_after_dashes():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def parse_args():
    p = argparse.ArgumentParser(prog="build_course")
    p.add_argument("--output", required=True, help="FBX path to write.")
    p.add_argument("--preview", default="", help="Optional PNG to render.")

    p.add_argument("--length", type=float, default=1800.0,
                   help="Centreline length in metres. 1800 is about 90 s at the 20 m/s a "
                        "crash course actually averages, against topSpeed 32.")
    p.add_argument("--drop", type=float, default=270.0,
                   help="Total vertical descent in metres.")

    p.add_argument("--width", type=float, default=26.0,
                   help="Drivable corridor width in metres. Wide enough for lines to diverge "
                        "the way the reference map's eight lanes do.")
    p.add_argument("--shoulder", type=float, default=5.0,
                   help="Metres of rising shoulder between corridor and wall.")
    p.add_argument("--wall", type=float, default=22.0,
                   help="Wall height in metres above the corridor.")
    p.add_argument("--wall-run", type=float, default=16.0,
                   help="Horizontal metres the wall face occupies. Needs room for benches; "
                        "too small and they collapse into a vertical cliff.")
    p.add_argument("--benches", type=int, default=3,
                   help="Quarry benches cut into each wall. 0 gives a smooth slope, which "
                        "reads as a sand dune rather than excavated rock.")

    p.add_argument("--chunk", type=float, default=DEFAULT_CHUNK,
                   help="Chunk length in metres. Drives frustum culling. See module docstring.")
    p.add_argument("--cell", type=float, default=DEFAULT_CELL,
                   help="Grid cell size in metres.")

    p.add_argument("--roughness", type=float, default=0.55,
                   help="Surface noise amplitude in metres on the drivable corridor. Geometry "
                        "cannot go finer than the --cell can represent, so detail below about "
                        "4 m belongs in the texture and in scattered boulders, not here.")
    p.add_argument("--curviness", type=float, default=1.8,
                   help="How much the centreline snakes. 0 is straight.")
    p.add_argument("--rollers", type=float, default=9.0,
                   help="Amplitude in metres of zero-mean vertical undulation on top of the "
                        "descent -- crests and compressions rather than a constant ramp. Does "
                        "not change the total drop.")

    p.add_argument("--skirt", type=float, default=9.0,
                   help="Metres BELOW the corridor floor that the outer wall is carried down. "
                        "The terrain is a surface, not a solid, so without a skirt there is "
                        "nothing facing you from outside the course and you see straight "
                        "through it. 0 disables, which looks broken from any exterior view.")

    p.add_argument("--bowl-radius", type=float, default=55.0,
                   help="Radius of the half-circle stopping area at the bottom. 0 omits it.")
    p.add_argument("--start-radius", type=float, default=42.0,
                   help="Radius of the half-circle starting bay the player spawns in. 0 omits "
                        "it. Smaller than the finish bowl: you only need room to line up, not "
                        "room to shed 30 m/s.")

    p.add_argument("--obstacles", type=float, default=1.0,
                   help="Obstacle density multiplier. 0 generates a clean course.")
    p.add_argument("--seed", type=int, default=7)
    p.add_argument("--theme", default="quarry", choices=sorted(THEMES))

    return p.parse_args(argv_after_dashes())


# ---------------------------------------------------------------------------- maths


def smoothstep(a, b, x):
    if b <= a:
        return 0.0
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def hash01(i, seed):
    """Deterministic pseudo-random in 0..1. Hashed, never random.Random, so a rebuild with
    the same seed produces the same course -- otherwise tuning one parameter reshuffles the
    whole map and you cannot tell what your change did."""
    n = (i * 374761393 + seed * 668265263) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((n ^ (n >> 16)) & 0xFFFFFFFF) / 4294967295.0


def value_noise(x, seed):
    """Smoothed 1D value noise. Cheap, and enough for terrain relief at this scale."""
    i = math.floor(x)
    f = x - i
    a = hash01(int(i), seed)
    b = hash01(int(i) + 1, seed)
    t = f * f * (3.0 - 2.0 * f)
    return a + (b - a) * t


def fbm(x, seed, octaves=4):
    total = 0.0
    amp = 1.0
    norm = 0.0
    freq = 1.0
    for o in range(octaves):
        total += value_noise(x * freq, seed + o * 977) * amp
        norm += amp
        amp *= 0.5
        freq *= 2.0
    return total / norm if norm else 0.0


def noise2(x, y, seed):
    """Separable 2D noise. Not a true 2D lattice, but the diagonal banding it produces is
    invisible under a rock texture and it costs a fraction of the real thing."""
    return (fbm(x, seed) + fbm(y, seed + 5171)) * 0.5


# ---------------------------------------------------------------------------- centreline


def grade_profile(t):
    """Relative steepness along the course, 0..1 in t.

    Shaped rather than constant, because a constant grade is the one thing that reads as
    obviously generated: a flat spawn apron, pitches that steepen and ease, and a run-out at
    the bottom so the car has somewhere to stop -- the reference map was criticised for not
    having one.
    """
    ramp_in = smoothstep(0.0, 0.05, t)
    run_out = 1.0 - smoothstep(0.90, 1.0, t)
    pitches = 1.0 + 0.38 * math.sin(t * TAU * 2.5) + 0.16 * math.sin(t * TAU * 6.3)
    return max(0.0, ramp_in * run_out * pitches)


def build_centreline(args, samples):
    """Return a list of (position, tangent, right) frames down the course."""
    step = args.length / samples

    # Integrate the grade profile, then normalise so the total descent is exactly --drop.
    # Normalising after the fact means the profile shape and the drop are independent dials.
    raw = []
    acc = 0.0
    for i in range(samples + 1):
        t = i / samples
        acc += grade_profile(t) * step
        raw.append(acc)

    total = raw[-1] if raw[-1] > 1e-6 else 1.0
    heights = [-(r / total) * args.drop for r in raw]

    # Rollers: zero-mean undulation on top of the descent, so the course has crests to launch
    # off and compressions to bottom out in rather than being one constant ramp. Faded to
    # nothing at both ends, which also means the total drop is untouched -- the spawn apron
    # and the run-out stay exactly where the descent put them.
    if args.rollers > 0.0:
        for i in range(len(heights)):
            t = i / samples
            fade = smoothstep(0.0, 0.07, t) * (1.0 - smoothstep(0.88, 1.0, t))
            heights[i] += (fbm(t * 5.0, args.seed + 177) - 0.5) * 2.0 * args.rollers * fade

    # Lateral snake. Low frequencies only: tight turns are not the point of a crash course,
    # and a radius under about 40 m is undrivable at the speeds this thing reaches.
    amp = 90.0 * args.curviness
    lateral = []
    for i in range(samples + 1):
        t = i / samples
        x = (math.sin(t * TAU * 0.75 + args.seed) * 1.0
             + math.sin(t * TAU * 1.6 + args.seed * 2.3) * 0.45
             + (fbm(t * 3.0, args.seed + 31) - 0.5) * 1.2)
        lateral.append(x * amp)

    points = [Vector((lateral[i], i * step, heights[i])) for i in range(samples + 1)]

    frames = []
    for i, p in enumerate(points):
        nxt = points[min(i + 1, samples)]
        prv = points[max(i - 1, 0)]
        tangent = (nxt - prv)
        if tangent.length < 1e-6:
            tangent = Vector((0.0, 1.0, 0.0))
        tangent.normalize()

        # Right vector kept horizontal. Banking the corridor with the terrain normal would
        # look better but makes the walls lean, and a leaning quarry wall reads as a bug.
        right = Vector((tangent.y, -tangent.x, 0.0))
        if right.length < 1e-6:
            right = Vector((1.0, 0.0, 0.0))
        right.normalize()

        frames.append((p, tangent, right))

    return frames


def min_turn_radius(frames, step):
    """Measured, not assumed. Printed so an undrivable corner is caught here rather than in
    play. Radius = step / angle between consecutive tangents."""
    worst = float("inf")
    for i in range(1, len(frames) - 1):
        a = frames[i][1]
        b = frames[i + 1][1]
        dot = max(-1.0, min(1.0, a.dot(b)))
        angle = math.acos(dot)
        if angle > 1e-6:
            worst = min(worst, step / angle)
    return worst


def max_grade(frames):
    worst = 0.0
    for _, tangent, _ in frames:
        horizontal = math.hypot(tangent.x, tangent.y)
        if horizontal > 1e-6:
            worst = max(worst, abs(tangent.z) / horizontal)
    return worst


# ---------------------------------------------------------------------------- cross-section


def terrace(height, step_h, flat=0.74):
    """Quantise a height into benches: a flat tread, then a quick riser.

    This is the whole quarry look. A wall that rises as one smooth ramp reads as a sand dune
    however much noise is on it -- which is exactly what the first render came out as. Cut
    horizontal benches into it and the same geometry reads as excavated rock, because benches
    are the one silhouette that only ever occurs where something dug.

    `flat` is the fraction of each step spent on the tread rather than the riser.
    """
    if step_h <= 1e-6:
        return height
    k = height / step_h
    i = math.floor(k)
    return (i + smoothstep(flat, 1.0, k - i)) * step_h


def wall_lift(args, d, half, edge):
    if d <= half:
        return 0.0

    if d <= edge:
        # Shoulder: smooth rise, so there is no lip at the corridor edge. Deliberately gentle
        # -- a corridor meeting a hard vertical face launches anything that clips it, and the
        # shoulder is what lets you ride up and come back down instead.
        return smoothstep(half, edge, d) * args.wall * 0.18

    base = args.wall * 0.18
    over = min(1.0, (d - edge) / max(args.wall_run, 1e-6))
    above = over * (args.wall - base)

    # Terrace the height ABOVE the shoulder, never the absolute height. Quantising the
    # absolute height let the first bench tread land BELOW the shoulder top -- measured at
    # 3.96 m of shoulder dropping to 2.72 m two metres further out -- which is a ditch running
    # the whole length of both corridor edges, and precisely the sort of thing that snatches a
    # wheel at speed. Terracing only the part above the shoulder makes the profile monotonic
    # by construction.
    if args.benches > 0:
        above = terrace(above, (args.wall - base) / args.benches)

    return base + above


def cross_section(args):
    """Offsets across the corridor, and the height each is raised by.

    Sampling is deliberately UNEVEN: the corridor and shoulder get full resolution because
    that is what the wheels touch, and the wall face gets half, because nobody drives on a
    quarry bench and doubling its triangle count buys nothing. Sampling the wall at the
    corridor's resolution roughly doubles the whole course.
    """
    half = args.width * 0.5
    edge = half + args.shoulder

    offsets = []
    x = -edge
    while x <= edge - 1e-6:
        offsets.append(x)
        x += args.cell
    offsets.append(edge)

    # Full resolution on the wall too. Half resolution was the cheaper choice and it did not
    # work: benches need several COLUMNS each to show a tread and a riser, and at cell*2 over
    # a 16 m run there are four columns for three benches, so the terracing averaged out into
    # the smooth slope it exists to replace. The corridor is what the wheels touch; the wall
    # is what the player looks at, and it turns out to need the vertices more.
    wall_cell = args.cell
    steps = max(1, int(round(args.wall_run / wall_cell)))
    left = [-edge - wall_cell * (i + 1) for i in range(steps)]
    right = [edge + wall_cell * (i + 1) for i in range(steps)]

    offsets = sorted(left) + offsets + right
    lifts = [wall_lift(args, abs(x), half, edge) for x in offsets]

    return offsets, lifts


# ---------------------------------------------------------------------------- build


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
            bsdf.inputs["Roughness"].default_value = 0.92
        # No metallic, no normal map -- same call as the car. A rock face read by a chase
        # camera on integrated graphics does not repay a normal map.
        if "Metallic" in bsdf.inputs:
            bsdf.inputs["Metallic"].default_value = 0.0
    return mat


def wipe_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def surface_height(args, frames, i, offset, lift, features=None, step=1.0):
    """Height for one grid point, before the frame position is added."""
    half = args.width * 0.5

    # Noise fades out under the walls: bumps on a vertical face are invisible and just cost
    # triangles' worth of shading noise.
    on_corridor = 1.0 - smoothstep(half, half + args.shoulder, abs(offset))

    along = i * (args.length / (len(frames) - 1))

    # Three scales, because one is the thing that reads as a smooth dune. Broad rolls carry
    # most of the amplitude, fine breaks them up, and chatter is the shortest wavelength the
    # grid can actually represent -- at a 2 m cell nothing under ~4 m survives sampling, so
    # pushing this higher aliases rather than adding detail.
    broad = (noise2(along * 0.015, offset * 0.02, args.seed + 4409) - 0.5) * 2.0
    fine = (noise2(along * 0.08, offset * 0.08, args.seed) - 0.5) * 2.0
    chatter = (noise2(along * 0.15, offset * 0.15, args.seed + 911) - 0.5) * 2.0

    bumps = (broad * 1.25 + fine * 0.70 + chatter * 0.38) * args.roughness * on_corridor

    # Relief on the walls, so the benches are weathered rather than machined. Kept well under
    # one bench height (wall / benches) on purpose -- noise louder than the terracing erases
    # it, and the terracing is what makes this read as a quarry at all.
    wall_relief = 0.0
    if lift > 0.0:
        bench_h = args.wall / max(1, args.benches)
        wall_relief = (noise2(along * 0.05, offset * 0.05, args.seed + 8821) - 0.5) * bench_h * 0.45

    # Kickers, humps and rock outcrops are part of the GROUND, not objects standing on it.
    # The MAX of the overlapping features, never the sum: summing two that overlap builds a
    # spike at the intersection, which is both ugly and a launch ramp nobody designed.
    feature = 0.0
    if features:
        for feat in features.get(i, ()):
            feature = max(feature, feature_lift(feat, (i - feat["i"]) * step,
                                                offset - feat["offset"]))

    return lift + bumps + wall_relief + feature * on_corridor


def build_chunks(args, frames, features=None, step=1.0):
    offsets, lifts = cross_section(args)
    samples = len(frames) - 1
    step = args.length / samples

    rows_per_chunk = max(2, int(round(args.chunk / step)))
    ground = ensure_material(MAT_GROUND, THEMES[args.theme]["ground"])
    rock = ensure_material(MAT_ROCK, THEMES[args.theme]["rock"])

    half = args.width * 0.5
    created = []
    total_tris = 0

    start_row = 0
    index = 0
    while start_row < samples:
        end_row = min(start_row + rows_per_chunk, samples)

        verts = []
        faces = []
        mat_ids = []
        uvs = []

        rows = list(range(start_row, end_row + 1))
        cols = len(offsets)

        for r in rows:
            origin, _tangent, right = frames[r]
            for c, offset in enumerate(offsets):
                h = surface_height(args, frames, r, offset, lifts[c], features, step)
                p = origin + right * offset + Vector((0.0, 0.0, h))
                verts.append((p.x, p.y, p.z))

                # World-scale UVs: one texture tile every 8 m, so tiling is consistent
                # between chunks and independent of how the course curves.
                uvs.append((offset / 8.0, (r * step) / 8.0))

        for ri in range(len(rows) - 1):
            for c in range(cols - 1):
                a = ri * cols + c
                b = a + 1
                d = (ri + 1) * cols + c
                e = d + 1
                faces.append((a, b, e, d))

                # Slot by position: corridor and shoulder are ground, everything past the
                # shoulder edge is rock. Two materials over the whole course, so it stays two
                # draw calls however many chunks there are.
                on_wall = min(abs(offsets[c]), abs(offsets[c + 1])) >= half + args.shoulder - 1e-6
                mat_ids.append(1 if on_wall else 0)

        # ---- skirt -------------------------------------------------------------------
        #
        # THE TERRAIN IS A SURFACE, NOT A SOLID, AND FROM OUTSIDE IT IS INVISIBLE. Seen from
        # beyond the wall there is nothing facing you, so the whole course renders as a set of
        # floating ribbons -- the bench treads -- with sky between them. It is the same defect
        # that made the ramps see-through, one scale up.
        #
        # A skirt closes it: drop a wall from the outermost column straight down past the
        # corridor floor and the course reads as a cut through solid rock from every angle.
        # Costs two quads per row per side, about 200 triangles a chunk against 3,400.
        skirt_base = len(verts)
        for r in rows:
            origin, _tangent, right = frames[r]
            floor = origin.z - args.skirt
            for c in (0, cols - 1):
                p = origin + right * offsets[c] + Vector((0.0, 0.0, floor))
                verts.append((p.x, p.y, p.z))
                uvs.append((offsets[c] / 8.0, floor / 8.0))

        for ri in range(len(rows) - 1):
            top_l, top_r = ri * cols, ri * cols + cols - 1
            nxt_l, nxt_r = (ri + 1) * cols, (ri + 1) * cols + cols - 1
            bot_l, bot_r = skirt_base + ri * 2, skirt_base + ri * 2 + 1
            nbot_l, nbot_r = skirt_base + (ri + 1) * 2, skirt_base + (ri + 1) * 2 + 1

            # Winding is opposite on the two sides so both normals face OUTWARD. Get this
            # backwards and the skirt is invisible from outside, which is the bug it exists
            # to fix.
            faces.append((top_l, nxt_l, nbot_l, bot_l))
            mat_ids.append(1)
            faces.append((top_r, bot_r, nbot_r, nxt_r))
            mat_ids.append(1)

        # Cap the very first and very last cross-section, or the course is an open-ended tube.
        #
        # ONLY where there is no bay. A bay's disc already covers that opening and brings its
        # own skirt, and capping it as well builds a wall straight across the mouth the player
        # has to drive through.
        #
        # The cap needs its OWN bottom vertex per column. An earlier version reused the two
        # outer skirt corners for every quad in the row, so each 2 m segment of the profile
        # was stretched down to the same pair of points 68 m apart -- a fan of overlapping
        # slabs that read as a giant wall beside the start bay.
        caps = []
        if start_row == 0 and args.start_radius <= 0.0:
            caps.append((0, True))
        if end_row == samples and args.bowl_radius <= 0.0:
            caps.append((len(rows) - 1, False))

        for edge_ri, is_start in caps:
            origin, _tangent, right = frames[rows[edge_ri]]
            floor = origin.z - args.skirt

            cap_base = len(verts)
            for offset in offsets:
                p = origin + right * offset + Vector((0.0, 0.0, floor))
                verts.append((p.x, p.y, p.z))
                uvs.append((offset / 8.0, floor / 8.0))

            for c in range(cols - 1):
                a = edge_ri * cols + c
                b = a + 1
                lo_a = cap_base + c
                lo_b = cap_base + c + 1
                faces.append((a, lo_a, lo_b, b) if is_start else (a, b, lo_b, lo_a))
                mat_ids.append(1)

        name = f"CourseChunk{index:03d}"
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(verts, [], faces)
        mesh.validate()

        mesh.materials.append(ground)
        mesh.materials.append(rock)
        for poly, slot in zip(mesh.polygons, mat_ids):
            poly.material_index = slot

        uv_layer = mesh.uv_layers.new(name="UVMap")
        for loop in mesh.loops:
            uv_layer.data[loop.index].uv = uvs[loop.vertex_index]

        # Corridor smooth, rock FLAT. This is the single biggest look change for zero cost:
        # smooth-shading a faceted wall averages its normals and turns rock into draped
        # fabric, which is what the first render came out as. Flat shading lets each bench
        # face catch the light on its own. The corridor stays smooth so the surface the car
        # drives on does not read as a series of ramps.
        mesh.polygons.foreach_set("use_smooth", [slot == 0 for slot in mat_ids])
        mesh.update()

        ob = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(ob)
        created.append(ob)

        total_tris += sum(len(p.vertices) - 2 for p in mesh.polygons)
        start_row = end_row
        index += 1

    return created, total_tris


def course_point(args, frames, i, offset, features=None, step=1.0):
    """A point on the finished terrain surface, using exactly the mesh's own maths.

    Boulders are placed with this rather than with their own approximation, so a rock cannot
    end up buried or hovering when the surface noise or its own outcrop changes.
    """
    origin, _tangent, right = frames[i]
    half = args.width * 0.5
    edge = half + args.shoulder
    lift = wall_lift(args, abs(offset), half, edge)
    height = surface_height(args, frames, i, offset, lift, features, step)
    return origin + right * offset + Vector((0.0, 0.0, height))


# ---------------------------------------------------------------------------- bowl


def build_bowl(args, frames, at_start=False, features=None, step=1.0):
    """A half-circle bay: the stopping area at the bottom, or the spawn bay at the top.

    A true half disc rather than a full one: the flat diameter edge is where the corridor
    meets it, so nothing overlaps the end chunk and there is no coplanar z-fighting at the
    join. A rim wall runs round the curved edge and along the parts of the straight edge
    outside the corridor mouth, so the bay holds a car instead of letting it run out the
    sides.

    The start bay is the same shape with `forward` flipped, so it sits BEHIND the first
    station and opens down the course. That gives the player somewhere to spawn that is
    visibly a place rather than an arbitrary point on a slope, and a moment to line up before
    the descent starts.
    """
    radius = args.start_radius if at_start else args.bowl_radius
    if radius <= 0.0:
        return None, 0

    origin, tangent, right = frames[0] if at_start else frames[-1]
    forward = Vector((tangent.x, tangent.y, 0.0))
    if forward.length < 1e-6:
        forward = Vector((0.0, 1.0, 0.0))
    forward.normalize()

    # Flipping forward mirrors the whole bay through its own mouth, which is exactly the
    # start bay: half a disc pointing back up the hill, opening onto the first chunk.
    if at_start:
        forward = -forward

    station = 0 if at_start else len(frames) - 1
    half = args.width * 0.5
    edge = half + args.shoulder
    mouth = edge

    # How far out the corridor's own cross-section reaches. Past this the bay is wider than the
    # course and has to close itself; inside it, the chunk already does.
    outer = edge + args.wall_run

    # SNAP THE RADIUS TO A WHOLE NUMBER OF CELLS. The seam blend above only gives a watertight
    # join because the bay's diameter samples land on the same coordinates as the chunk's
    # columns, and they only do that if the radial step is exactly --cell.
    #
    # Unsnapped this silently half-works: 42 / 2 gives rings 21 and a step of exactly 2, so the
    # start bay lines up, while 55 / 2 rounds to 28 rings and a step of 1.964, so every vertex
    # along the finish bowl's mouth sits between two chunk columns -- T-junctions, and a hairline
    # crack at each one. Snapping 55 to 56 costs a metre of radius nobody will notice.
    rings = max(4, int(round(radius / args.cell)))
    radius = rings * args.cell
    arcs = 48

    verts = []
    faces = []
    mat_ids = []

    for ri in range(rings + 1):
        r = radius * ri / rings
        for ai in range(arcs + 1):
            # -90..+90 about the forward axis: the half plane ahead of the course end.
            theta = math.radians(-90.0 + 180.0 * ai / arcs)
            ahead = math.cos(theta) * r
            across = math.sin(theta) * r

            lift = args.wall * smoothstep(radius * 0.80, radius, r)

            # Wall the straight edge too, except across the corridor mouth, or the bay simply
            # spills back out either side of where you came in.
            #
            # This TAPERS into the bay rather than stopping after one cell. Gated on
            # `ahead < cell * 1.5` it was a wall three metres deep standing in open ground --
            # two thin fins either side of the entrance, which is what it looked like. Decaying
            # it over most of the radius makes the corridor walls open out into the bay the way
            # a quarry actually does.
            if abs(across) > mouth:
                flank = (smoothstep(mouth, mouth + 10.0, abs(across))
                         * (1.0 - smoothstep(0.0, radius * 0.55, max(0.0, ahead))))
                lift = max(lift, args.wall * flank)

            relief = (noise2(ahead * 0.05, across * 0.05, args.seed + 6101) - 0.5)
            lift += relief * (args.wall * 0.10 if lift > 0.1 else 0.0)
            floor_noise = relief * args.roughness * (1.0 - smoothstep(radius * 0.7, radius, r))
            height = lift + floor_noise

            # BLEND TO THE CORRIDOR CROSS-SECTION AT THE MOUTH, or the two meshes meet at the
            # same plane with heights from two different formulas and leave a ragged gap you
            # can see the horizon through.
            #
            # At ahead = 0 this evaluates the chunk's OWN height function at the same station,
            # so the shared edge matches exactly rather than approximately. The vertices line
            # up too: the chunk's columns and the bay's diameter points are both even
            # multiples of --cell, so there are no T-junctions to crack open either.
            blend = smoothstep(0.0, radius * 0.55, ahead)
            if blend < 1.0:
                seam = surface_height(args, frames, station, across,
                                      wall_lift(args, abs(across), half, edge),
                                      features, step)
                height = seam + (height - seam) * blend

            p = origin + forward * ahead + right * across + Vector((0.0, 0.0, height))
            verts.append((p.x, p.y, p.z))

    stride = arcs + 1
    for ri in range(rings):
        for ai in range(arcs):
            a = ri * stride + ai
            b = a + 1
            c = (ri + 1) * stride + ai
            d = c + 1

            # Flipping `forward` mirrors the parameterisation, which reverses the handedness
            # of every quad built from it. Without reversing the winding to match, the whole
            # start bay renders face-down and is invisible from above -- the same class of
            # mistake as the inside-out ramps.
            faces.append((a, c, d, b) if at_start else (a, b, d, c))
            mat_ids.append(1 if (radius * (ri + 1) / rings) > radius * 0.80 else 0)

    # ---- skirt ----------------------------------------------------------------------------
    # Same reason as the chunks: a bare disc has no outside, so from beyond the rim you see
    # straight through it.
    floor = origin.z - args.skirt
    outer_base = len(verts)
    for ai in range(arcs + 1):
        t = verts[rings * stride + ai]
        verts.append((t[0], t[1], floor))

    edge_lo_base = len(verts)
    for ri in range(rings + 1):
        t = verts[ri * stride]
        verts.append((t[0], t[1], floor))

    edge_hi_base = len(verts)
    for ri in range(rings + 1):
        t = verts[ri * stride + arcs]
        verts.append((t[0], t[1], floor))

    for ai in range(arcs):
        t0, t1 = rings * stride + ai, rings * stride + ai + 1
        b0, b1 = outer_base + ai, outer_base + ai + 1
        faces.append((t0, b0, b1, t1) if at_start else (t0, t1, b1, b0))
        mat_ids.append(1)

    # The straight diameter edge, but only where the bay is WIDER than the corridor's own
    # cross-section. Inside that the chunk's geometry and its skirt already close things off,
    # and a skirt here would stand as a wall across the mouth you drive through.
    for ri in range(rings):
        if radius * (ri + 1) / rings <= outer:
            continue

        lo0, lo1 = ri * stride, (ri + 1) * stride
        lb0, lb1 = edge_lo_base + ri, edge_lo_base + ri + 1
        faces.append((lo0, lb0, lb1, lo1) if at_start else (lo0, lo1, lb1, lb0))
        mat_ids.append(1)

        hi0, hi1 = ri * stride + arcs, (ri + 1) * stride + arcs
        hb0, hb1 = edge_hi_base + ri, edge_hi_base + ri + 1
        faces.append((hi0, hi1, hb1, hb0) if at_start else (hi0, hb0, hb1, hi1))
        mat_ids.append(1)

    name = "CourseStartBay" if at_start else "CourseBowl"
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.validate()
    mesh.materials.append(bpy.data.materials[MAT_GROUND])
    mesh.materials.append(bpy.data.materials[MAT_ROCK])
    for poly, slot in zip(mesh.polygons, mat_ids):
        poly.material_index = slot

    uv_layer = mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        v = mesh.vertices[loop.vertex_index].co
        uv_layer.data[loop.index].uv = (v.x / 8.0, v.y / 8.0)

    mesh.polygons.foreach_set("use_smooth", [slot == 0 for slot in mat_ids])
    mesh.update()

    ob = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(ob)

    return ob, sum(len(p.vertices) - 2 for p in mesh.polygons)


# ---------------------------------------------------------------------------- features


def feature_lift(feat, along, lateral):
    """How much one terrain feature raises the ground at a point, in metres.

    `along` and `lateral` are metres from the feature centre, down the course and across it.
    """
    half_w = feat["width"] * 0.5
    if abs(lateral) >= half_w:
        return 0.0

    # Fade to nothing at the edges over a wide band, so a feature blends into the corridor
    # instead of standing on it behind a near-vertical rim. See the lip note below -- the
    # sides are the same problem and want the same answer.
    fade = 1.0 - smoothstep(half_w * 0.35, half_w, abs(lateral))
    half_l = feat["length"] * 0.5

    if feat["kind"] == "kicker":
        # Rises along the course, then falls away. That back face is the lip, and it is what
        # launches the car.
        #
        # It used to drop over a flat 2.5 m, which on a 3.4 m kicker is about 54 degrees. THE
        # TERRAIN IS A HEIGHTFIELD -- one surface with a top and no underside -- so a face
        # that steep is somewhere the car can get beneath, and beneath it there is no geometry
        # and no backface: you see straight through the ramp and pass into it. Scaling the lip
        # with the height keeps every face near 34 degrees, which is solid from both sides.
        #
        # It still launches. At 20 m/s the ground falling away at 34 degrees is plenty of air;
        # what makes a kicker throw a car is the speed and the rise, not a cliff at the end.
        lip = max(4.0, feat["height"] * 1.5)
        if along < -half_l or along > half_l + lip:
            return 0.0
        if along <= half_l:
            profile = smoothstep(-half_l, half_l, along)
        else:
            profile = 1.0 - smoothstep(half_l, half_l + lip, along)
        return feat["height"] * profile * fade

    # hump and outcrop: a rounded swell, symmetric along the course.
    if abs(along) >= half_l:
        return 0.0
    return feat["height"] * (1.0 - smoothstep(0.0, half_l, abs(along))) * fade


def plan_features(args, frames):
    """Decide where the terrain rises into kickers, humps and rock outcrops.

    Obstacles used to be separate wedge and box meshes dropped onto the surface. That was
    wrong twice over. They read as objects lying on the track rather than as part of the
    valley -- popcorn on the ground -- and the hand-written face winding on the wedge was
    backwards, so Unity culled the outside and the ramps rendered inside out.

    Making them part of the terrain removes both failure modes at once: the same mesh cannot
    be inverted relative to itself, cannot z-fight against itself, and cannot look placed.

    Placement is hashed from the seed, never random, so the same seed always gives the same
    course. Nothing lands in the first stretch -- the spawn apron has to be clean or the run
    is over before it starts -- or the last, where the run-out and the bowl are.

    Returns (buckets keyed by station, flat list) so the height function can look up only the
    handful of features near a given station instead of testing all of them per vertex.
    """
    if args.obstacles <= 0.0:
        return {}, []

    samples = len(frames) - 1
    step = args.length / samples
    first = int(samples * 0.08)
    last = int(samples * 0.90)
    spacing = max(5, int(round(30.0 / args.obstacles / step)))

    buckets = {}
    listing = []

    for i in range(first, last, spacing):
        roll = hash01(i * 7 + args.seed, args.seed + 61)
        side = (hash01(i * 11 + args.seed, args.seed + 89) - 0.5) * 2.0
        offset = side * args.width * 0.5 * 0.70

        if roll < 0.36:
            feat = {
                "kind": "kicker",
                "length": 9.0 + 6.0 * hash01(i * 19, args.seed),
                "width": 7.0 + 5.0 * hash01(i * 17, args.seed),
                "height": 1.8 + 1.6 * hash01(i * 23, args.seed),
            }
        elif roll < 0.60:
            feat = {
                "kind": "hump",
                "length": 8.0 + 7.0 * hash01(i * 29, args.seed),
                "width": 10.0 + 8.0 * hash01(i * 31, args.seed),
                "height": 0.9 + 1.2 * hash01(i * 37, args.seed),
            }
        else:
            radius = 1.4 + 2.1 * hash01(i * 41, args.seed)
            feat = {
                "kind": "outcrop",
                "length": radius * 3.2,
                "width": radius * 3.2,
                "height": radius * 0.5,
                "radius": radius,
            }

        feat["i"] = i
        feat["offset"] = offset
        listing.append(feat)

        reach = int(math.ceil((max(feat["length"], feat["width"]) * 0.5 + 4.0) / step))
        for k in range(i - reach, i + reach + 1):
            buckets.setdefault(k, []).append(feat)

    return buckets, listing


# ---------------------------------------------------------------------------- boulders


def add_boulder(name, centre, radius, seed, material):
    """A low-poly rock, sunk into the swell of ground that `plan_features` raised under it.

    Kept as real geometry rather than made out of terrain, because a terrain dome is smooth
    and reads as another dune -- the crisp faceted silhouette is the whole point of a rock.
    What stops it looking dropped on is the outcrop feature beneath it and the fact that its
    centre sits BELOW the surface.
    """
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=radius)

    for k, v in enumerate(bm.verts):
        jitter = 0.62 + 0.76 * hash01(k * 13 + seed, seed + 4523)
        v.co *= jitter

    # Normals recalculated rather than trusted. This is exactly what the old hand-written ramp
    # got wrong, and a rock rendered inside out looks like a hole in the world.
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    verts = [(centre.x + v.co.x, centre.y + v.co.y, centre.z + v.co.z) for v in bm.verts]
    faces = [tuple(v.index for v in f.verts) for f in bm.faces]
    bm.free()

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.validate()
    mesh.materials.append(material)
    mesh.polygons.foreach_set("use_smooth", [False] * len(mesh.polygons))
    mesh.update()

    ob = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(ob)
    return ob, sum(len(p.vertices) - 2 for p in mesh.polygons)


def build_boulders(args, frames, features, listing, step):
    """Place a rock in every outcrop swell.

    Sunk by a chunk of its own radius so the ground cuts through it and it reads as bedrock
    breaking the surface rather than a pebble resting on top. The surface height comes from
    the terrain's own function INCLUDING the outcrop, so the rock and its mound cannot
    disagree however the noise changes.
    """
    rock = ensure_material(MAT_ROCK, THEMES[args.theme]["rock"])

    created = []
    tris = 0

    for n, feat in enumerate(listing):
        if feat["kind"] != "outcrop":
            continue

        radius = feat["radius"]
        base = course_point(args, frames, feat["i"], feat["offset"], features, step)

        # Centre BELOW the surface, not above it. Sitting the centre on the ground leaves the
        # whole lower hemisphere visible and the rock reads as dropped on top of the track.
        # Sunk, the ground line cuts across it and it reads as bedrock breaking through.
        centre = base - Vector((0.0, 0.0, radius * 0.28))

        ob, t = add_boulder(f"CourseRock{n:03d}", centre, radius, feat["i"], rock)
        created.append(ob)
        tris += t

    return created, tris


# ---------------------------------------------------------------------------- export


def export_fbx(path):
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")

    # bake_space_transform=True is the fix split_car.py never applied. Without it Blender
    # leaves the axis conversion in the NODE transforms and every object arrives in Unity
    # carrying rotation (-90, 0, 0). That is invisible for anything Unity merely renders,
    # but it is exactly what made the car's wheels unusable, and terrain chunks have no
    # business carrying a rotation at all.
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        object_types={"MESH"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        path_mode="COPY",
    )


def scene_bounds():
    """World-space bounding box of every mesh in the scene."""
    lo = Vector((1e12, 1e12, 1e12))
    hi = Vector((-1e12, -1e12, -1e12))

    for ob in bpy.context.scene.objects:
        if ob.type != "MESH":
            continue
        for corner in ob.bound_box:
            p = ob.matrix_world @ Vector(corner)
            lo = Vector((min(lo[i], p[i]) for i in range(3)))
            hi = Vector((max(hi[i], p[i]) for i in range(3)))

    return lo, hi


def render_preview(path, frames, args):
    """Two Workbench renders: an overview, and a low shot from the corridor itself.

    Workbench for the same reason preview_split.py uses it -- sub-second, no GPU, no
    lighting setup to get wrong. With cavity shading on it reads terrain SHAPE better than
    an unlit EEVEE render does, and shape is the only thing worth judging before there are
    textures.

    The low shot is the one that matters. An overview flatters any terrain; the question is
    whether the thing is drivable, and that is only visible from about eye height.
    """
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.image_settings.file_format = "PNG"

    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "MATERIAL"
    shading.show_cavity = True
    shading.show_shadows = True

    # Workbench has no world, so anything not covered by geometry renders pure black -- which
    # is indistinguishable from a night sky and made the first road render look like it had
    # one. A flat sky colour makes it obvious what is terrain and what is nothing.
    shading.background_type = "VIEWPORT"
    shading.background_color = (0.45, 0.55, 0.68)

    cam_data = bpy.data.cameras.new("PreviewCam")

    # Blender's default far clip is 100 m. This course is 1,800 m long, so at the default
    # everything past the first hundred metres is behind the far plane -- which renders as an
    # empty frame from the overview and as a black band across the road shot that reads
    # convincingly like sky. Set it from the actual scene size.
    lo, hi = scene_bounds()
    cam_data.clip_start = 0.5
    cam_data.clip_end = max(1000.0, (hi - lo).length * 3.0)

    cam = bpy.data.objects.new("PreviewCam", cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam

    def shoot(location, target, out_path, lens=35.0):
        cam_data.lens = lens
        cam.location = location
        cam.rotation_euler = (target - location).normalized().to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = out_path
        bpy.ops.render.render(write_still=True)

    mid = frames[len(frames) // 2][0]
    quarter = frames[len(frames) // 4][0]

    root, ext = os.path.splitext(path)

    # Overview framed from the real scene bounds, not a fixed offset. A hand-tuned offset
    # frames exactly one --length and misses at every other, which is how the first render
    # came out as a thumbnail in the middle of an empty frame.
    centre = (lo + hi) * 0.5
    radius = (hi - lo).length * 0.5

    lens = 32.0
    half_fov = math.atan(18.0 / lens)          # 36 mm sensor
    distance = (radius / math.tan(half_fov)) * 1.05

    # Three-quarter view: off to one side and above, so both the descent and the snake read.
    view_dir = Vector((0.62, -0.60, 0.50)).normalized()
    shoot(centre + view_dir * distance, centre, root + ext, lens=lens)

    # Roughly where ChaseCamera sits: behind and above the corridor, looking down it. At eye
    # height on the centreline the camera ends up inside whichever obstacle happens to be
    # there, which shows a grey slab and nothing else.
    q = len(frames) // 4
    _, q_tangent, _ = frames[q]
    q_forward = Vector((q_tangent.x, q_tangent.y, 0.0)).normalized()
    ahead = frames[min(len(frames) - 1, q + 70)][0]
    shoot(quarter - q_forward * 26.0 + Vector((0.0, 0.0, 9.0)),
          ahead + Vector((0.0, 0.0, 2.0)),
          root + "_road" + ext, lens=32.0)

    # Across the course, not down it. The road shot looks along the corridor, so the walls are
    # seen edge-on and all it shows is their top edge receding -- which reads as rolling hills
    # whatever their actual profile is. This one looks side-on at one wall from above the
    # opposite one, which is the only view that shows whether the benches exist.
    # The start bay, from above and behind it looking down the course. Added after a broken end
    # cap shipped twice without being seen -- neither the overview nor the mid-course cameras
    # look anywhere near either bay, so nothing caught it.
    start = frames[0][0]
    _, t0, _ = frames[0]
    fwd0 = Vector((t0.x, t0.y, 0.0)).normalized()
    shoot(start - fwd0 * (args.start_radius + 105.0) + Vector((0.0, 0.0, args.wall * 3.4)),
          frames[min(len(frames) - 1, 40)][0],
          root + "_start" + ext, lens=26.0)

    _, _, right = frames[len(frames) // 2]
    reach = args.width * 0.5 + args.shoulder + args.wall_run
    shoot(mid + right * -(reach + 95.0) + Vector((0.0, 0.0, args.wall * 2.6)),
          mid + right * (reach * 0.35) + Vector((0.0, 0.0, args.wall * 0.35)),
          root + "_wall" + ext, lens=30.0)


# ---------------------------------------------------------------------------- report


def uphill_check(frames):
    """Worst UPHILL gradient anywhere on the course.

    Rollers add zero-mean undulation on top of the descent, and if their amplitude beats the
    local descent the course starts climbing -- which on a downhill run means arriving at a
    hill with no throttle and stopping. Measured rather than assumed, because it depends on
    --rollers and --drop together and neither one alone tells you.
    """
    worst = 0.0
    where = 0.0
    for i in range(len(frames) - 1):
        a = frames[i][0]
        b = frames[i + 1][0]
        run = math.hypot(b.x - a.x, b.y - a.y)
        if run < 1e-6:
            continue
        climb = (b.z - a.z) / run
        if climb > worst:
            worst = climb
            where = i / (len(frames) - 1)
    return worst, where


def steepest_feature_face(args, listing, step):
    """Steepest face any terrain feature creates, sampled at the real grid resolution.

    Worth measuring rather than reasoning about, because it is the number that decides whether
    a kicker is solid. The terrain is a heightfield: one surface, no underside. Past about 60
    degrees the car can get beneath a face, and under a heightfield there is nothing to see and
    nothing to hit -- you look straight through the ramp and drive into it.
    """
    worst = 0.0
    culprit = "none"

    for feat in listing:
        span = max(feat["length"], feat["width"]) * 0.5 + 8.0

        for axis, spacing in (("along", step), ("across", args.cell)):
            n = int(span / spacing) + 2
            previous = None
            for k in range(-n, n + 1):
                d = k * spacing
                h = feature_lift(feat, d, 0.0) if axis == "along" else feature_lift(feat, 0.0, d)
                if previous is not None:
                    slope = abs(h - previous) / spacing
                    if slope > worst:
                        worst = slope
                        culprit = f"{feat['kind']} {axis}"
                previous = h

    return math.degrees(math.atan(worst)), culprit


def report(args, frames, chunks, total_tris, step,
           bowl=None, bowl_tris=0, obstacles=None, obstacle_tris=0, listing=None,
           bay=None, bay_tris=0):
    radius = min_turn_radius(frames, step)
    grade = max_grade(frames)
    climb, climb_at = uphill_check(frames)

    obstacles = obstacles or []
    start = frames[0][0]
    end = frames[-1][0]
    per_chunk = total_tris / max(1, len(chunks))
    everything = total_tris + bowl_tris + bay_tris + obstacle_tris

    print("\n=== COURSE MEASURED ===")
    print(f"  centreline length     {args.length:.0f} m")
    print(f"  vertical drop         {args.drop:.0f} m  ({args.drop / args.length * 100:.1f}% average)")
    print(f"  steepest descent      {grade * 100:.1f}%")
    print(f"  steepest CLIMB        {climb * 100:.1f}%  at {climb_at * 100:.0f}% along"
          f"   {'(fine)' if climb < 0.08 else '(<-- may stall a slow car)'}")
    print(f"  tightest turn radius  {radius:.0f} m   (under ~40 m is undrivable at speed)")
    print(f"  corridor width        {args.width:.0f} m drivable, {args.shoulder:.0f} m shoulder")
    print(f"  rollers               {args.rollers:.0f} m amplitude")
    print(f"  chunks                {len(chunks)} of {args.chunk:.0f} m")
    def snapped(r):
        return max(4, int(round(r / args.cell))) * args.cell

    print(f"  start bay             {f'radius {snapped(args.start_radius):.0f} m (asked {args.start_radius:.0f}, snapped to a whole cell), {bay_tris:,} tris' if bay else 'none'}")
    print(f"  stopping bowl         {f'radius {snapped(args.bowl_radius):.0f} m (asked {args.bowl_radius:.0f}, snapped to a whole cell), {bowl_tris:,} tris' if bowl else 'none'}")
    print(f"  outer skirt           {args.skirt:.0f} m below the corridor floor")
    listing = listing or []
    kinds = {}
    for feat in listing:
        kinds[feat["kind"]] = kinds.get(feat["kind"], 0) + 1
    shaped = ", ".join(f"{v} {k}" for k, v in sorted(kinds.items())) or "none"

    face_deg, culprit = steepest_feature_face(args, listing, step) if listing else (0.0, "none")
    face_note = "solid from both sides" if face_deg < 60.0 else "<-- CAR CAN GET UNDER THIS"

    print(f"  terrain features      {len(listing)} ({shaped}) -- folded into the ground, no meshes")
    print(f"  steepest feature face {face_deg:.0f} deg ({culprit})   {face_note}")
    print(f"  boulder meshes        {len(obstacles)}, {obstacle_tris:,} tris")
    print(f"  triangles             {everything:,} total, {per_chunk:,.0f} per terrain chunk")
    print(f"  drive time at 20 m/s  {args.length / 20.0:.0f} s")

    # The cross-section printed as numbers, because a render looking down the corridor cannot
    # show the wall profile and it is not worth guessing from a picture whether the terracing
    # actually fired.
    offsets, lifts = cross_section(args)
    print("\n=== CROSS SECTION (half, metres from centreline) ===")

    previous = None
    dips = 0
    for x, lift in zip(offsets, lifts):
        if x < -1e-6:
            continue
        # A profile that ever falls as it goes outward is a ditch along the corridor edge,
        # which catches wheels. Flagged rather than trusted.
        dip = previous is not None and lift < previous - 1e-6
        dips += 1 if dip else 0
        bar = "#" * int(round(lift * 1.2))
        print(f"  {x:6.1f} m  ->  {lift:6.2f} m  {bar}{'   <-- DIP' if dip else ''}")
        previous = lift

    print(f"  profile is {'MONOTONIC, good' if dips == 0 else f'NOT monotonic -- {dips} dip(s)'}")

    print("\n=== UNITY SETUP ===")
    print(f"  Import the FBX at Scale Factor 1.0. It is authored in metres and")
    print(f"  bake_space_transform is on, so every chunk arrives with an identity transform.")
    print(f"  Place the root at world origin.")
    print()
    # Spawn INSIDE the start bay, back from its mouth, rather than on the first metre of the
    # descent. The bay exists so the run begins somewhere that reads as a place.
    _o, t0, _r = frames[0]
    back = Vector((t0.x, t0.y, 0.0))
    back = back.normalized() if back.length > 1e-6 else Vector((0.0, 1.0, 0.0))
    spawn = start - back * min(args.start_radius * 0.55, 24.0)

    print(f"  Spawn the car at   ({spawn.x:.1f}, {spawn.z + 1.0:.1f}, {spawn.y:.1f})   [Unity XYZ]")
    print(f"  Facing             (0, 0, 0) -- the course runs along +Z")
    print(f"  Finish is near     ({end.x:.1f}, {end.z + 1.0:.1f}, {end.y:.1f})")
    print()
    print(f"  Every chunk: Static ticked, layer Default, one MeshCollider, Convex OFF.")
    print(f"  Materials: {MAT_GROUND} and {MAT_ROCK}. Two draw calls for the whole course.")
    print(f"  ~{per_chunk:,.0f} tris a chunk means 4-6 chunks in frustum is ~{per_chunk * 5:,.0f}")
    print(f"  rendered, against {total_tris:,} if this were one mesh.")
    print()
    print(f"  CarController groundMask and CarDamage damagingLayers must include Default.")


# ---------------------------------------------------------------------------- main


def main():
    args = parse_args()

    if args.theme not in THEMES:
        raise SystemExit(f"unknown theme {args.theme}")

    wipe_scene()

    samples = max(8, int(round(args.length / args.cell)))
    step = args.length / samples

    frames = build_centreline(args, samples)

    # Features are planned BEFORE the terrain, because they are part of it: the height
    # function folds them in, so a kicker is a fold in the ground rather than a wedge sitting
    # on it.
    features, listing = plan_features(args, frames)

    chunks, total_tris = build_chunks(args, frames, features, step)
    bowl, bowl_tris = build_bowl(args, frames, False, features, step)
    bay, bay_tris = build_bowl(args, frames, True, features, step)
    boulders, boulder_tris = build_boulders(args, frames, features, listing, step)

    export_fbx(os.path.abspath(args.output))
    report(args, frames, chunks, total_tris, step,
           bowl, bowl_tris, boulders, boulder_tris, listing, bay, bay_tris)

    if args.preview:
        render_preview(os.path.abspath(args.preview), frames, args)
        print(f"\n  preview -> {args.preview}")


if __name__ == "__main__":
    main()
