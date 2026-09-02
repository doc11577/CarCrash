"""Convert an ASCII FBX into a binary one Blender can actually open.

    blender --background --python tools/blender/fbx_ascii_to_binary.py -- \
        --input  ascii.fbx \
        --output binary.fbx

**Blender has never supported ASCII FBX and never will** — `import_scene.fbx` refuses with
"ASCII FBX files are not supported" and that is the end of it. Unity imports them happily, which
is why a model can arrive in this project looking fine and be unopenable by the whole Blender
pipeline: no inspection, no splitting, no previews.

The alternatives were worse. There is no FBX converter on this machine; Autodesk's has been
discontinued for years. Running a second Unity in batch mode to re-export would fight the project
lock whenever the Editor is open, and standing up a throwaway Unity project to dodge that costs
minutes per attempt.

Parsing it directly is viable because ASCII FBX is a plain, regular text format and the parts
that matter are four arrays per mesh. This handles the common Maya/FBX-SDK export shape:

    Geometry: <id>, "Geometry::", "Mesh" {
        Vertices: *N { a: x,y,z, ... }
        PolygonVertexIndex: *M { a: 0,1,-3, ... }     last index of each face is ~i
        LayerElementUV: 0 { UV: *K { a: u,v, ... }  UVIndex: *M { a: ... } }
        LayerElementNormal: 0 { Normals: *3M { a: ... } }
    }

It is NOT a general FBX reader. It reads meshes, their names, their local transforms and their
UVs, which is everything this project's pipeline needs. Anything else in the file — materials,
animation, cameras — is ignored on purpose; textures are reassigned in Unity anyway.
"""

import sys
import os
import math
import argparse

import bpy
from mathutils import Vector, Euler


def argv_after_dashes():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def parse_args():
    p = argparse.ArgumentParser(prog="fbx_ascii_to_binary")
    p.add_argument("--input", required=True)
    p.add_argument("--output", required=True)
    p.add_argument("--scale", type=float, default=1.0,
                   help="Uniform scale applied on the way in. ASCII FBX out of Maya is usually "
                        "in centimetres, so 0.01 is the common fix. The report prints the "
                        "resulting size -- a car should be 2-8 m long.")
    return p.parse_args(argv_after_dashes())


class Block:
    """One `Name: ... { ... }` node, with its raw body text and its children."""

    def __init__(self, header, body):
        self.header = header
        self.body = body


def split_blocks(text):
    """Yield (header, body) for every top-level `Header { body }` in text.

    Brace counting rather than a real parser, which is enough because ASCII FBX never puts a
    brace inside a string it cares about here. Quoted spans are skipped so a name containing a
    brace cannot throw the depth off.
    """
    out = []
    i = 0
    n = len(text)

    while i < n:
        # Find the next '{' that opens a block, remembering the line it is on as the header.
        brace = text.find("{", i)
        if brace < 0:
            break

        line_start = text.rfind("\n", 0, brace) + 1
        header = text[line_start:brace].strip()

        depth = 0
        j = brace
        in_string = False
        while j < n:
            c = text[j]
            if c == '"':
                in_string = not in_string
            elif not in_string:
                if c == "{":
                    depth += 1
                elif c == "}":
                    depth -= 1
                    if depth == 0:
                        break
            j += 1

        out.append(Block(header, text[brace + 1:j]))
        i = j + 1

    return out


def read_array(body, key):
    """Numbers from a `key: *N { a: 1,2,3 }` entry, or []."""
    at = body.find(key + ": *")
    if at < 0:
        return []

    brace = body.find("{", at)
    if brace < 0:
        return []

    depth = 0
    j = brace
    while j < len(body):
        if body[j] == "{":
            depth += 1
        elif body[j] == "}":
            depth -= 1
            if depth == 0:
                break
        j += 1

    chunk = body[brace + 1:j]
    a = chunk.find("a:")
    if a < 0:
        return []

    return [float(v) for v in chunk[a + 2:].replace(",", " ").split()]


def read_id_and_name(header):
    """`Geometry: 12345, "Geometry::Body", "Mesh"` -> (12345, 'Body')."""
    try:
        after = header.split(":", 1)[1]
    except IndexError:
        return None, ""

    parts = [p.strip() for p in after.split(",")]
    ident = None
    try:
        ident = int(parts[0])
    except (ValueError, IndexError):
        return None, ""

    name = ""
    if len(parts) > 1:
        raw = parts[1].strip().strip('"')
        # "Model::Body" -> "Body"
        name = raw.split("::")[-1]

    return ident, name


def read_property(body, name, count, default):
    """One `P: "name", ..., x, y, z` value out of a Properties70 block."""
    at = body.find('"%s"' % name)
    if at < 0:
        return list(default)

    line_end = body.find("\n", at)
    line = body[at:line_end if line_end > 0 else len(body)]

    nums = []
    for token in line.split(",")[1:]:
        token = token.strip()
        try:
            nums.append(float(token))
        except ValueError:
            continue

    return nums[-count:] if len(nums) >= count else list(default)


def build(args):
    with open(args.input, "r", encoding="utf-8", errors="replace") as handle:
        text = handle.read()

    blocks = split_blocks(text)

    objects_body = None
    connections_body = None
    for block in blocks:
        if block.header.startswith("Objects"):
            objects_body = block.body
        elif block.header.startswith("Connections"):
            connections_body = block.body

    if objects_body is None:
        raise SystemExit("no Objects section -- is this really an FBX?")

    geometries = {}
    models = {}
    materials = {}

    for block in split_blocks(objects_body):
        if block.header.startswith("Geometry:"):
            ident, _name = read_id_and_name(block.header)
            if ident is not None:
                geometries[ident] = block.body
        elif block.header.startswith("Model:"):
            ident, name = read_id_and_name(block.header)
            if ident is not None:
                models[ident] = (name, block.body)
        elif block.header.startswith("Material:"):
            ident, name = read_id_and_name(block.header)
            if ident is not None:
                materials[ident] = name

    # Geometry -> Model, so a mesh can take the model's NAME and local transform. Without the
    # connection table every mesh would come through called "Geometry::" and unusable to
    # split_car.py, which classifies by name.
    owner = {}
    model_materials = {}

    if connections_body:
        for line in connections_body.splitlines():
            line = line.strip()
            if not line.startswith("C:"):
                continue
            parts = [p.strip().strip('"') for p in line[2:].split(",")]
            if len(parts) < 3 or parts[0] != "OO":
                continue
            try:
                child, parent = int(parts[1]), int(parts[2])
            except ValueError:
                continue
            if child in geometries and parent in models:
                owner[child] = parent
            elif child in materials and parent in models:
                model_materials.setdefault(parent, []).append(materials[child])

    made = 0
    for geo_id, body in geometries.items():
        verts = read_array(body, "Vertices")
        indices = read_array(body, "PolygonVertexIndex")
        if not verts or not indices:
            continue

        points = [Vector((verts[i] * args.scale,
                          verts[i + 1] * args.scale,
                          verts[i + 2] * args.scale))
                  for i in range(0, len(verts) - 2, 3)]

        # A face ends at a NEGATIVE index, encoded as ~i. Missing this makes one enormous
        # n-gon out of the whole mesh.
        faces = []
        current = []
        for raw in indices:
            index = int(raw)
            if index < 0:
                current.append(~index)
                if len(current) >= 3:
                    faces.append(tuple(current))
                current = []
            else:
                current.append(index)

        model_id = owner.get(geo_id)
        name = models[model_id][0] if model_id in models else "Mesh%d" % geo_id

        mesh = bpy.data.meshes.new(name + "Mesh")
        mesh.from_pydata(points, [], faces)
        mesh.validate()

        apply_uvs(mesh, body, faces)

        # Materials are carried across by NAME only -- no colours, no textures, which get
        # reassigned in Unity anyway. The NAME is the part that matters: split_car.py decides
        # what counts as glass from the material name, and CarGlass empties submeshes by it.
        for material_name in model_materials.get(model_id, []):
            material = bpy.data.materials.get(material_name)
            if material is None:
                material = bpy.data.materials.new(material_name)
            mesh.materials.append(material)

        ob = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(ob)

        if model_id in models:
            apply_transform(ob, models[model_id][1], args.scale)

        made += 1

    return made


def apply_uvs(mesh, body, faces):
    """ByPolygonVertex / IndexToDirect UVs, which is what every exporter here produces."""
    uv = read_array(body, "UV")
    uv_index = read_array(body, "UVIndex")
    if not uv:
        return

    layer = mesh.uv_layers.new(name="UVMap")
    coords = [(uv[i], uv[i + 1]) for i in range(0, len(uv) - 1, 2)]

    loop = 0
    for face in faces:
        for _corner in face:
            if loop >= len(layer.data):
                return
            if loop < len(uv_index):
                at = int(uv_index[loop])
                if 0 <= at < len(coords):
                    layer.data[loop].uv = coords[at]
            loop += 1


def apply_transform(ob, body, scale):
    props = body
    at = body.find("Properties70")
    if at >= 0:
        props = body[at:]

    translation = read_property(props, "Lcl Translation", 3, (0.0, 0.0, 0.0))
    rotation = read_property(props, "Lcl Rotation", 3, (0.0, 0.0, 0.0))
    scaling = read_property(props, "Lcl Scaling", 3, (1.0, 1.0, 1.0))

    ob.location = Vector(translation) * scale
    ob.rotation_euler = Euler([math.radians(v) for v in rotation], "XYZ")
    ob.scale = Vector(scaling)


def report():
    print("\n=== CONVERTED ===")
    total = 0
    for ob in sorted(bpy.data.objects, key=lambda o: o.name):
        if ob.type != "MESH":
            continue
        mesh = ob.data
        mesh.calc_loop_triangles()
        tris = len(mesh.loop_triangles)
        total += tris
        d = ob.dimensions
        print("  %-24s %6d tris  %6.2f x %6.2f x %6.2f m  uv=%s"
              % (ob.name, tris, d.x, d.y, d.z, "yes" if mesh.uv_layers else "NO"))
        print("      at %6.2f, %6.2f, %6.2f   mats=%s"
              % (ob.location.x, ob.location.y, ob.location.z,
                 ",".join(m.name for m in mesh.materials) or "none"))
    print("  %-24s %6d tris" % ("TOTAL", total))

    longest = 0.0
    for ob in bpy.data.objects:
        if ob.type == "MESH":
            longest = max(longest, max(ob.dimensions))
    if not (2.0 <= longest <= 8.0):
        hint = "--scale 0.01" if longest > 20 else "--scale 100"
        print("  !! longest object is %.2f m, which is not a car. Try %s." % (longest, hint))


def main():
    args = parse_args()
    print("\n=== fbx_ascii_to_binary: %s ===" % args.input)

    bpy.ops.wm.read_factory_settings(use_empty=True)

    made = build(args)
    if made == 0:
        raise SystemExit("no meshes recovered -- the file may not be the shape this handles")

    report()

    os.makedirs(os.path.dirname(os.path.abspath(args.output)) or ".", exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=args.output,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        object_types={"MESH"},
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=False,
    )
    print("=== wrote %s ===" % args.output)


main()
