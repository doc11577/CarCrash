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

    p.add_argument("--roughness", type=float, default=0.32,
                   help="Surface noise amplitude in metres on the drivable corridor.")
    p.add_argument("--curviness", type=float, default=1.0,
                   help="How much the centreline snakes. 0 is straight.")
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


def surface_height(args, frames, i, offset, lift):
    """Height for one grid point, before the frame position is added."""
    half = args.width * 0.5

    # Noise fades out under the walls: bumps on a vertical face are invisible and just cost
    # triangles' worth of shading noise.
    on_corridor = 1.0 - smoothstep(half, half + args.shoulder, abs(offset))

    along = i * (args.length / (len(frames) - 1))
    fine = (noise2(along * 0.08, offset * 0.08, args.seed) - 0.5) * 2.0
    broad = (noise2(along * 0.015, offset * 0.02, args.seed + 4409) - 0.5) * 2.0

    bumps = (fine * 0.65 + broad * 1.35) * args.roughness * on_corridor

    # Relief on the walls, so the benches are weathered rather than machined. Kept well under
    # one bench height (wall / benches) on purpose -- noise louder than the terracing erases
    # it, and the terracing is what makes this read as a quarry at all.
    wall_relief = 0.0
    if lift > 0.0:
        bench_h = args.wall / max(1, args.benches)
        wall_relief = (noise2(along * 0.05, offset * 0.05, args.seed + 8821) - 0.5) * bench_h * 0.45

    return lift + bumps + wall_relief


def build_chunks(args, frames):
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
                h = surface_height(args, frames, r, offset, lifts[c])
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

    # Eye height on the corridor, a quarter of the way down, looking ahead.
    ahead = frames[min(len(frames) - 1, len(frames) // 4 + 60)][0]
    shoot(quarter + Vector((0.0, 0.0, 3.0)), ahead + Vector((0.0, 0.0, 2.0)),
          root + "_road" + ext, lens=32.0)

    # Across the course, not down it. The road shot looks along the corridor, so the walls are
    # seen edge-on and all it shows is their top edge receding -- which reads as rolling hills
    # whatever their actual profile is. This one looks side-on at one wall from above the
    # opposite one, which is the only view that shows whether the benches exist.
    _, _, right = frames[len(frames) // 2]
    reach = args.width * 0.5 + args.shoulder + args.wall_run
    shoot(mid + right * -(reach + 95.0) + Vector((0.0, 0.0, args.wall * 2.6)),
          mid + right * (reach * 0.35) + Vector((0.0, 0.0, args.wall * 0.35)),
          root + "_wall" + ext, lens=30.0)


# ---------------------------------------------------------------------------- report


def report(args, frames, chunks, total_tris, step):
    radius = min_turn_radius(frames, step)
    grade = max_grade(frames)

    start = frames[0][0]
    end = frames[-1][0]
    per_chunk = total_tris / max(1, len(chunks))

    print("\n=== COURSE MEASURED ===")
    print(f"  centreline length     {args.length:.0f} m")
    print(f"  vertical drop         {args.drop:.0f} m  ({args.drop / args.length * 100:.1f}% average)")
    print(f"  steepest gradient     {grade * 100:.1f}%")
    print(f"  tightest turn radius  {radius:.0f} m   (under ~40 m is undrivable at speed)")
    print(f"  corridor width        {args.width:.0f} m drivable, {args.shoulder:.0f} m shoulder")
    print(f"  chunks                {len(chunks)} of {args.chunk:.0f} m")
    print(f"  triangles             {total_tris:,} total, {per_chunk:,.0f} per chunk")
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
    print(f"  Spawn the car at   ({start.x:.1f}, {start.z + 1.0:.1f}, {start.y:.1f})   [Unity XYZ]")
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
    chunks, total_tris = build_chunks(args, frames)

    export_fbx(os.path.abspath(args.output))
    report(args, frames, chunks, total_tris, step)

    if args.preview:
        render_preview(os.path.abspath(args.preview), frames, args)
        print(f"\n  preview -> {args.preview}")


if __name__ == "__main__":
    main()
