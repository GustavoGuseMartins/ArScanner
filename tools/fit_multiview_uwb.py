"""Offline experiment: fit a stationary scanner from UWB ranges and AR phone poses.

This does not place anything in AR. It diagnoses whether a moving-phone baseline
can overcome the small triangle on the PCB. Requires the trajectory CSV columns.
"""
import argparse
import csv
from collections import defaultdict
from pathlib import Path
from statistics import median

import numpy as np


def rotated(q, v):
    xyz = q[:3]
    return v + 2 * (q[3] * np.cross(xyz, v) + np.cross(xyz, np.cross(xyz, v)))


def observations(path):
    clusters = defaultdict(list)
    with path.open(encoding='utf-8-sig', newline='') as handle:
        for row in csv.DictReader(handle):
            if row.get('arTracking') != '1' or row['profileSaved'] != '1':
                continue
            try:
                ranges = np.array([float(row[f'd{i}Corrected']) for i in (1, 2, 3)])
                camera = np.array([float(row[k]) for k in ('camX', 'camY', 'camZ')])
                q = np.array([float(row[k]) for k in ('camQx', 'camQy', 'camQz', 'camQw')])
            except (ValueError, TypeError):
                continue
            if not np.all(np.isfinite(ranges)) or not np.all(np.isfinite(camera)) or not np.all(np.isfinite(q)):
                continue
            if np.any((ranges <= .08) | (ranges > 35)) or abs(np.linalg.norm(q) - 1) > .05:
                continue
            # Moving the phone during one sequential three-radio cycle can break
            # exact pair bounds. Keep only cycles without a gross discrepancy.
            if (abs(ranges[0]-ranges[1]) > .25 or
                    abs(ranges[0]-ranges[2]) > .25 or
                    abs(ranges[1]-ranges[2]) > .325):
                continue
            q = q / np.linalg.norm(q)
            board_center = camera + rotated(q, np.array([0., -.05 + .0484123 / 3, .02]))
            range_median = float(np.median(ranges))
            key = tuple(np.floor(board_center / .15).astype(int))
            clusters[key].append((board_center, range_median))
    centers, ranges = [], []
    for chunk in clusters.values():
        if len(chunk) < 3:
            continue
        centers.append(np.median(np.stack([item[0] for item in chunk]), axis=0))
        ranges.append(median(item[1] for item in chunk))
    return np.array(centers), np.array(ranges), sum(len(v) for v in clusters.values())


def solve(centers, ranges):
    if len(centers) < 6:
        raise ValueError('fewer than six occupied AR position cells')
    span = np.ptp(centers, axis=0)
    pair_baseline = max(np.linalg.norm(a - b) for a in centers for b in centers)
    if pair_baseline < .4:
        raise ValueError(f'phone AR baseline only {pair_baseline:.2f} m; need separated views')
    # Linearized sphere differences provide a seed. The nonlinear fit uses
    # robust weights so a few bad radio readings cannot dominate the result.
    reference = centers[0]
    a = 2 * (centers[1:] - reference)
    b = ranges[0]**2 - ranges[1:]**2 + np.sum(centers[1:]**2, axis=1) - np.dot(reference, reference)
    seed, _, _, _ = np.linalg.lstsq(a, b, rcond=None)
    starts = [seed, np.median(centers, axis=0) + np.array([0, 0, median(ranges)]),
              np.median(centers, axis=0) - np.array([0, 0, median(ranges)])]
    solutions = []
    for start in starts:
        x = np.array(start, dtype=float)
        for _ in range(40):
            delta = x - centers
            length = np.maximum(np.linalg.norm(delta, axis=1), 1e-5)
            residual = length - ranges
            jacobian = delta / length[:, None]
            weight = np.minimum(1., .15 / np.maximum(np.abs(residual), 1e-5))
            lhs = jacobian.T @ (weight[:, None] * jacobian) + np.eye(3) * 1e-5
            rhs = -jacobian.T @ (weight * residual)
            step = np.linalg.solve(lhs, rhs)
            step *= min(1., .5 / max(np.linalg.norm(step), 1e-5))
            x += step
            if np.linalg.norm(step) < 1e-5:
                break
        residual = np.linalg.norm(x - centers, axis=1) - ranges
        cost = np.sum(np.where(np.abs(residual) <= .15, residual**2 / 2,
                               .15 * (np.abs(residual) - .075)))
        solutions.append((cost, x, residual))
    cost, estimate, residual = min(solutions, key=lambda item: item[0])
    inlier = np.abs(residual) <= .20
    if np.count_nonzero(inlier) >= 4:
        directions = estimate - centers[inlier]
        directions /= np.linalg.norm(directions, axis=1)[:, None]
        singular = np.linalg.svd(directions, compute_uv=False)
        geometry = float(singular[-1] / singular[0])
    else:
        geometry = 0.
    return {
        'camera_cells': len(centers), 'camera_span_xyz_m': np.round(span, 3).tolist(),
        'max_phone_baseline_m': round(float(pair_baseline), 3),
        'scanner_ar_xyz_m': np.round(estimate, 3).tolist(),
        'median_absolute_range_residual_m': round(float(np.median(np.abs(residual))), 3),
        'inlier_cells_within_20cm': int(np.count_nonzero(inlier)),
        'geometry_smallest_over_largest_singular': round(geometry, 4),
        'robust_cost': round(float(cost), 3),
    }


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('csv', type=Path)
    args = parser.parse_args()
    positions, measurements, raw_count = observations(args.csv)
    print('accepted_cycles_before_cell_aggregation:', raw_count)
    print(solve(positions, measurements))
