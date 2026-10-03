import json

with open('docs/scanner_montado_inventory.json', encoding='utf-8') as f:
    d = json.load(f)

for o in d['objects']:
    name = o['name']
    if any(k in name.lower() for k in ['lidar', 'pan', 'pcb', 'suporte_motor', '08_', '10_']):
        print(f"Object: {name}")
        print(f"  Parent: {o['parent']}")
        print(f"  Loc: {o['location']}")
        print(f"  Rot: {o['rotation_euler']}")
        print(f"  Dim: {o['dimensions']}")
        print(f"  Min: {o['min']}")
        print(f"  Max: {o['max']}")
        print("-" * 40)
