"""Print the structure of a model file. Run headless:

blender --background --python tools/blender/inspect_model.py -- <path-to-model>

Reports one line per mesh object: name, verts, tris, local bounds and world
dimensions in metres, plus the material list. Used to decide where panel cuts
go before running split_car.py.
"""
import sys, os
import bpy


def argv_after_dashes():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


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
    elif ext == ".blend":
        bpy.ops.wm.open_mainfile(filepath=path)
    else:
        raise SystemExit("unsupported extension: " + ext)


def main():
    args = argv_after_dashes()
    if not args:
        raise SystemExit("usage: ... -- <model path>")
    path = args[0]
    wipe_scene()
    load(path)

    total_v = total_t = 0
    print("\n=== OBJECTS ===")
    for ob in sorted(bpy.data.objects, key=lambda o: o.name):
        parent = ob.parent.name if ob.parent else "-"
        if ob.type != "MESH":
            print(f"[{ob.type:>7}] {ob.name:<32} parent={parent}")
            continue
        me = ob.data
        tris = sum(len(p.vertices) - 2 for p in me.polygons)
        total_v += len(me.vertices)
        total_t += tris
        d = ob.dimensions
        mats = ",".join(m.name if m else "None" for m in me.materials) or "-"
        print(f"[  MESH ] {ob.name:<32} parent={parent}")
        print(f"          verts={len(me.vertices):>6} tris={tris:>6} "
              f"dims={d.x:.2f} x {d.y:.2f} x {d.z:.2f} m")
        print(f"          loc={tuple(round(c,3) for c in ob.location)} mats=[{mats}]")

    print("\n=== MATERIALS ===")
    for m in bpy.data.materials:
        print(f"  {m.name}")

    print("\n=== IMAGES ===")
    for im in bpy.data.images:
        if im.name == "Render Result":
            continue
        print(f"  {im.name}  size={tuple(im.size)}")

    print(f"\n=== TOTAL === verts={total_v} tris={total_t} "
          f"meshes={len([o for o in bpy.data.objects if o.type=='MESH'])}")


main()
