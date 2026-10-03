import json

with open('docs/scanner_montado_inventory.json', encoding='utf-8') as f:
    data = json.load(f)

for o in data['objects']:
    name = o['name'].lower()
    if any(k in name for k in ['lidar', 'pcb', '180', '10t', 'pinhao', 'ponte', 'base', 'suporte']):
        print(f"Name: {o['name']}")
        print(f"  Parent: {o['parent']}")
        print(f"  Location: {o['location']}")
        print(f"  Rotation: {o['rotation_euler']}")
        print(f"  Dimensions: {o['dimensions']}")
        print(f"  Min: {o['min']}")
        print(f"  Max: {o['max']}")
        print("-" * 50)
