"""Compare physical range consistency before and after saved UWB profiles."""
import csv
from collections import Counter, defaultdict
from pathlib import Path
from statistics import median
import sys


def complete(row):
    return row['radioMask'] == '7' and all(
        .08 < float(row[f'd{i}Raw']) <= 35 for i in range(1, 4))


def violates(row, corrected):
    suffix = 'Corrected' if corrected else 'Raw'
    distances = [float(row[f'd{i}{suffix}']) for i in range(1, 4)]
    return any(abs(distances[i-1] - distances[j-1]) > separation
               for i, j, separation in ((1, 2, .1), (1, 3, .1), (2, 3, .175)))


if __name__ == '__main__':
    groups = defaultdict(list)
    for path in sorted(Path(sys.argv[1]).glob('Uwb_*.csv')):
        with path.open(encoding='utf-8-sig', newline='') as file:
            for row in csv.DictReader(file):
                key = (path.name, row['profileSaved'],
                       '/'.join(row['scale' + str(i)] + ':' + row['offset' + str(i)]
                                for i in range(1, 4)))
                groups[key].append(row)
    for (file, saved, profile), rows in groups.items():
        valid = [row for row in rows if complete(row)]
        sigmas = [float(row['baseSigma']) for row in valid
                  if row['state'] == '3' and float(row['baseSigma']) >= 0]
        print(file, 'saved=' + saved, 'rows=' + str(len(rows)),
              'from=' + rows[0]['phoneUtc'], 'to=' + rows[-1]['phoneUtc'])
        print('  profile:', profile)
        print('  states:', dict(Counter(row['state'] for row in rows)))
        print('  complete:', len(valid),
              'raw_pair_violations:', sum(violates(row, False) for row in valid),
              'corrected_pair_violations:', sum(violates(row, True) for row in valid))
        if sigmas:
            print('  state3_base_sigma_median:', round(median(sigmas), 3))
