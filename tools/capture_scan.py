"""Save scanner raw rays and status while the phone controls scanning.
Usage: python tools/capture_scan.py --seconds 65 --output captura
PC must be on ArScanner_Net. Does not start motors or change calibration.
"""
import argparse
import csv
import io
import json
import time
import urllib.request
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="192.168.4.1")
    parser.add_argument("--seconds", type=float, default=65)
    parser.add_argument("--output", type=Path, default=Path("captura"))
    args = parser.parse_args()
    base = f"http://{args.host}:8889"
    # Exclusive creation avoids accidentally replacing a previous hardware capture.
    with args.output.with_suffix('.csv').open('x', newline='') as rays, args.output.with_suffix('.jsonl').open('x') as statuses:
        writer = None
        try:
            with urllib.request.urlopen(base+'/geometry', timeout=2) as response:
                geometry = json.load(response)
            with args.output.with_suffix('.geometry.json').open('x') as output:
                json.dump(geometry, output, indent=2)
        except (OSError, ValueError) as error:
            statuses.write(json.dumps({'geometry_error':str(error)})+'\n')
        seen = set()
        deadline, next_status = time.monotonic()+args.seconds, 0
        count = 0
        while time.monotonic() < deadline:
            try:
                with urllib.request.urlopen(base+'/scan.csv', timeout=2) as response:
                    text = response.read(1000000).decode('ascii')
                reader = csv.DictReader(io.StringIO(text))
                if not reader.fieldnames or 'sequence' not in reader.fieldnames:
                    raise ValueError('Invalid CSV header')
                if writer is None:
                    writer = csv.DictWriter(rays, fieldnames=reader.fieldnames)
                    writer.writeheader()
                elif reader.fieldnames != writer.fieldnames:
                    raise ValueError('CSV schema changed during capture; start a new file')
                for row in reader:
                    key = (row['sequence'],row['sample_us'])
                    if key not in seen:
                        writer.writerow(row)
                        seen.add(key)
                        count += 1
                if time.monotonic() >= next_status:
                    with urllib.request.urlopen(base+'/status', timeout=2) as response:
                        status = json.load(response)
                    statuses.write(json.dumps({'pc_time':time.time(),'scanner':status})+'\n')
                    next_status = time.monotonic()+1
            except (OSError, ValueError, KeyError) as error:
                statuses.write(json.dumps({'pc_time':time.time(),'error':str(error)})+'\n')
            time.sleep(.25)
        print(f'{count} distinct samples saved. Gaps in sequence indicate missed snapshots; not a lossless recording.')


if __name__ == '__main__':
    main()
