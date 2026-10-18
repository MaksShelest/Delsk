#!/usr/bin/env python3
"""Bounded paired-seed economy study. Simulation remains in C#.
Author: Шелестов Максим Евгеньевич.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--economy', type=Path, action='append', help='Repeat for A/B profiles; default config/economy.json')
parser.add_argument('--scenario', type=Path, default=Path('scenarios/baseline-economy.json'))
parser.add_argument('--controllers', type=Path, default=Path('scenarios/baseline-controllers.json'))
parser.add_argument('--seeds', default='42,43,44')
parser.add_argument('--ticks', type=int, default=1200)
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
seeds = [int(s) for s in args.seeds.split(',')]
if not seeds or len(seeds) != len(set(seeds)) or any(s < 0 or s >= 2**64 for s in seeds) or not 1 <= args.ticks <= 1000000:
    parser.error('Use distinct uint64 seeds and ticks 1..1000000')
out = args.output.resolve()
if out.exists():
    parser.error('Output already exists; use a new folder to preserve previous measurements')
out.mkdir(parents=True)
inputs = out / 'inputs'
inputs.mkdir()
for name, source in [('scenario.json', args.scenario), ('controllers.json', args.controllers)]:
    shutil.copyfile(source.resolve(), inputs / name)
profiles = []
for index, source in enumerate(args.economy or [Path('config/economy.json')]):
    target = inputs / f'economy-{index}.json'
    shutil.copyfile(source.resolve(), target)
    profiles.append((index, target, hashlib.sha256(target.read_bytes()).hexdigest()))
rows = []
for index, profile, digest in profiles:
    for seed in seeds:
        case = f'profile-{index}-seed-{seed}'
        run = out / case
        command = [args.dotnet, str(root/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll'),
                   '--script', 'baseline', '--scenario', str(inputs/'scenario.json'), '--controllers', str(inputs/'controllers.json'),
                   '--economy', str(profile), '--seed', str(seed), '--ticks', str(args.ticks), '--profile', 'true', '--output', str(run)]
        result = subprocess.run([sys.executable, str(root/'scripts/run_check.py'), '--name', case,
                                 '--timeout', '300', '--log-directory', str(out/'checks'), '--', *command], cwd=root, timeout=320)
        if result.returncode:
            raise SystemExit(f'{case} failed; prior results and exact inputs are preserved at {out}')
        summary = json.loads((run/'summary.json').read_text())
        measured = json.loads((run/'profile.json').read_text())
        scenario = json.loads((run/'scenario.json').read_text())
        prices = {i['id']: i['basePriceMinor'] for i in scenario['items']}
        rows.append({'case': case, 'economy_id': scenario['economyProfileId'], 'input_sha256': digest,
                     'seed': seed, 'ticks': summary['tick'], 'produced_base_minor': sum(f['produced']*prices[f['itemId']] for f in summary['flows']),
                     'capital_paid_minor': sum(s['paidMinor'] for s in summary['settlements']),
                     'market_paid_minor': sum(s['paidMinor'] for s in summary['marketAllocations']),
                     'p95_tick_ms': measured['p95TickMs'], 'save_bytes': measured['saveBytes'], 'state_hash': summary['stateHash']})
with (out/'comparison.csv').open('w', encoding='utf-8-sig', newline='') as stream:
    writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
    writer.writeheader()
    writer.writerows(rows)
(out/'study.json').write_text(json.dumps({'status':'succeeded', 'scope':'algorithmic economy sensitivity; no LLM or device claim',
    'seeds':seeds, 'ticks':args.ticks, 'cases':rows, 'author':'Шелестов Максим Евгеньевич'}, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(f'Economy study succeeded: {out/"comparison.csv"}')
