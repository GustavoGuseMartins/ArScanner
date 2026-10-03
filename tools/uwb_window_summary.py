"""Show short windows of corrected ranges so static periods can be inspected."""
import csv
import sys
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path
from statistics import median


if __name__ == '__main__':
    path = Path(sys.argv[1])
    with path.open(encoding='utf-8-sig', newline='') as file:
        rows = list(csv.DictReader(file))
    begin = datetime.fromisoformat(rows[0]['phoneUtc'].replace('Z', '+00:00'))
    windows = defaultdict(list)
    for row in rows:
        instant = datetime.fromisoformat(row['phoneUtc'].replace('Z', '+00:00'))
        windows[int((instant - begin).total_seconds() // 5)].append(row)
    for index, chunk in sorted(windows.items()):
        valid = [row for row in chunk if row['radioMask'] == '7' and
                 all(row[f'd{i}Corrected'] and .08 < float(row[f'd{i}Corrected']) <= 35
                     for i in (1, 2, 3))]
        if not valid:
            continue
        distances = [[float(row[f'd{i}Corrected']) for row in valid] for i in (1, 2, 3)]
        pairs = ((0, 1, .1), (0, 2, .1), (1, 2, .175))
        invalid = sum(any(abs(float(row[f'd{i+1}Corrected']) -
                              float(row[f'd{j+1}Corrected'])) > separation
                          for i, j, separation in pairs) for row in valid)
        print(f"{chunk[0]['phoneUtc'][11:19]}-{chunk[-1]['phoneUtc'][11:19]}",
              f'n={len(valid)}',
              'medians=' + '/'.join(f'{median(v):.3f}' for v in distances),
              'ranges=' + '/'.join(f'{min(v):.2f}..{max(v):.2f}' for v in distances),
              f'pair_violations={invalid}',
              'states=' + str(dict(Counter(row['state'] for row in chunk))))
