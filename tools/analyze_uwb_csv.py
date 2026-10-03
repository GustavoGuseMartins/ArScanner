"""Summarize recorded UWB diagnostics without treating motion as sensor noise."""
import argparse
import csv
import json
import math
from collections import Counter
from pathlib import Path
from statistics import median


def summarize(rows):
    valid = [r for r in rows if int(r['radioMask']) == 7 and all(
        .08 < float(r[f'd{i}Raw']) <= 35 for i in range(1, 4))]
    pairs = [(1, 2, .1), (1, 3, .1), (2, 3, .175)]
    differences = {}
    for i, j, separation in pairs:
        values = [float(r[f'd{i}Raw'])-float(r[f'd{j}Raw']) for r in valid]
        differences[f'{i}-{j}'] = {
            'separation_m': separation,
            'median_signed_difference_m': median(values) if values else None,
            'outside_physical_bound': sum(abs(v) > separation for v in values),
            'outside_bound_plus_15cm': sum(abs(v) > separation+.15 for v in values),
        }
    impossible = gross = spheres = 0
    for r in valid:
        d = [float(r[f'd{i}Raw']) for i in range(1, 4)]
        impossible += any(abs(d[i-1]-d[j-1]) > s for i,j,s in pairs)
        gross += any(abs(d[i-1]-d[j-1]) > s+.15 for i,j,s in pairs)
        # Coordinates: left=(0,0,0), right=(.175,0,0), top=(.0875,h,0).
        h = math.sqrt(.1**2-.0875**2)
        x = (d[1]**2-d[2]**2+.175**2)/(.35)
        y = (d[1]**2-d[0]**2+.1**2-2*.0875*x)/(2*h)
        spheres += d[1]**2-x*x-y*y >= 0
    return {
        'rows': len(rows), 'first_utc': rows[0]['phoneUtc'],
        'last_utc': rows[-1]['phoneUtc'],
        'profile_saved_counts': dict(Counter(r['profileSaved'] for r in rows)),
        'profiles': sorted(set(tuple(r[k+str(i)] for k in ('scale','offset')
                                     for i in range(1,4)) for r in rows)),
        'radio_mask_counts': dict(Counter(r['radioMask'] for r in rows)),
        'base_state_counts': dict(Counter(r['state'] for r in rows)),
        'stage_triples': dict(Counter('/'.join(r[f'stage{i}'] for i in range(1,4)) for r in rows)),
        'complete_ranges': len(valid),
        'raw_ranges_min_median_max': [[min(v), median(v), max(v)] for v in
            [[float(r[f'd{i}Raw']) for r in valid] for i in range(1,4)]] if valid else [],
        'pair_differences': differences,
        'any_pair_outside_bound': impossible,
        'any_pair_outside_bound_plus_15cm': gross,
        'raw_exact_sphere_intersections': spheres,
    }


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    all_rows = []
    report = {}
    for path in sorted(args.directory.glob('*.csv')):
        with path.open(encoding='utf-8-sig', newline='') as f:
            rows = list(csv.DictReader(f))
        if rows:
            report[path.name] = summarize(rows)
            all_rows.extend(rows)
    if all_rows:
        report['all'] = summarize(all_rows)
    print(json.dumps(report, indent=2))
