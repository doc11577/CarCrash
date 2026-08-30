"""Dump each object's bounding box in the CAR's local space.

Derived, not guessed. Verified against two known points:
  WheelFL  blender (-0.909, -0.698, 0.300) -> car (-0.698, 0.300,  0.909)
  PartHood blender (-0.240,  0.012, 0.791) -> car ( 0.012, 0.791,  0.240)

so  car.x = blender.y,  car.y = blender.z,  car.z = -blender.x
"""
import sys
import bpy
from mathutils import Vector

path = sys.argv[sys.argv.index("--") + 1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)


def to_car(v):
    return Vector((v.y, v.z, -v.x))


print("\n=== BOUNDS IN CAR LOCAL SPACE (metres, ground y=0, +z forward) ===")
print(f"{'object':<16} {'x min':>7} {'x max':>7} {'y min':>7} {'y max':>7} {'z min':>7} {'z max':>7}")

for ob in sorted(bpy.context.scene.objects, key=lambda o: o.name):
    if ob.type != "MESH":
        continue
    pts = [to_car(ob.matrix_world @ Vector(c)) for c in ob.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    print(f"{ob.name:<16} {lo.x:7.3f} {hi.x:7.3f} {lo.y:7.3f} {hi.y:7.3f} {lo.z:7.3f} {hi.z:7.3f}")

# Why this exists: CarInteriorProps places boxes inside the bodywork in the CAR's local space,
# and there was no way to know where "inside" actually was. The figures written down by hand
# were wrong -- they had the body spanning z -2.41..1.45 when it really spans -2.578..1.586,
# and the wheel anchors at (+/-0.877, 0.61, 1.776/-1.345) when the scene really has
# (+/-0.719, 0.5, 0.909/-1.661) -- and the dash box ended up protruding through the bonnet.
#
# Run it after any re-export, and paste the table into CarInteriorProps' remarks.
