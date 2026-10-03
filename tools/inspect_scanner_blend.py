"""Read-only Blender scene inventory; run with --background --disable-autoexec."""
import bpy, json, sys
from mathutils import Vector

out = sys.argv[sys.argv.index('--') + 1]
report = {'file': bpy.data.filepath, 'units': bpy.context.scene.unit_settings.system,
          'scale_length': bpy.context.scene.unit_settings.scale_length, 'objects': []}
for obj in bpy.data.objects:
    corners = [obj.matrix_world @ Vector(v) for v in obj.bound_box]
    report['objects'].append({'name': obj.name, 'type': obj.type,
        'location': list(obj.matrix_world.translation), 'dimensions': list(obj.dimensions),
        'rotation_euler': list(obj.rotation_euler), 'parent': obj.parent.name if obj.parent else None,
        'min': [min(v[i] for v in corners) for i in range(3)],
        'max': [max(v[i] for v in corners) for i in range(3)]})
with open(out, 'w', encoding='utf-8') as f:
    json.dump(report, f, indent=2, ensure_ascii=False)
print('Inventory:', out, 'objects:', len(report['objects']))
if '--render' in sys.argv:
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 1000
    scene.render.resolution_percentage = 100
    scene.render.filepath = sys.argv[sys.argv.index('--render') + 1]
    bpy.ops.render.render(write_still=True)
