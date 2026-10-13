#!/usr/bin/env python3
"""Explicitly regenerate platform expectations from a verified runner output.
Author: Шелестов Максим Евгеньевич.
"""
import argparse
import csv
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('run_directory', type=Path)
args = parser.parse_args()
summary = json.loads((args.run_directory / 'summary.json').read_text())
if summary['status'] != 'succeeded' or summary['tick'] != 62:
    parser.error('Expected the completed, unchanged 62-tick economic-cycle fixture')
root = Path(__file__).resolve().parents[1]
(root / 'scenarios/platform-cycle.commands.jsonl').write_bytes((args.run_directory / 'commands.jsonl').read_bytes())
with (args.run_directory / 'metrics.csv').open(newline='') as source:
    rows = list(csv.DictReader(source))
if [int(r['tick']) for r in rows] != list(range(1, 63)) or rows[-1]['state_hash'] != summary['stateHash']:
    parser.error('Missing checkpoints or final hash mismatch')
with (root / 'scenarios/platform-cycle.checkpoints.csv').open('w', newline='') as target:
    writer = csv.writer(target, lineterminator='\n')
    writer.writerow(['tick', 'state_hash'])
    writer.writerows((r['tick'], r['state_hash']) for r in rows)
