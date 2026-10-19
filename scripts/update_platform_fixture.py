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
parser.add_argument('--elections-directory', type=Path, help='Verified 40-tick P19 runner output')
parser.add_argument('--support-directory', type=Path, help='Verified 120-tick P20 runner output')
parser.add_argument('--stopped-directory', type=Path, help='Verified stopped-voting runner output')
parser.add_argument('--physical-directory', type=Path, help='Verified physical-automation runner output')
parser.add_argument('--trade-directory',type=Path,help='Verified participant-trade runner output')
parser.add_argument('--food-directory',type=Path,help='Verified physical food-chain output')
args = parser.parse_args()
summary = json.loads((args.run_directory / 'summary.json').read_text())
if summary['status'] != 'succeeded' or summary['tick'] != 62:
    parser.error('Expected the completed, unchanged 62-tick economic-cycle fixture')
root = Path(__file__).resolve().parents[1]
commands = (args.run_directory / 'commands.jsonl').read_bytes()
with (args.run_directory / 'metrics.csv').open(newline='') as source:
    rows = list(csv.DictReader(source))
if [int(r['tick']) for r in rows] != list(range(1, 63)) or rows[-1]['state_hash'] != summary['stateHash']:
    parser.error('Missing checkpoints or final hash mismatch')
(root / 'scenarios/platform-cycle.commands.jsonl').write_bytes(commands)
with (root / 'scenarios/platform-cycle.checkpoints.csv').open('w', newline='') as target:
    writer = csv.writer(target, lineterminator='\n')
    writer.writerow(['tick', 'state_hash'])
    writer.writerows((r['tick'], r['state_hash']) for r in rows)
if args.elections_directory:
    directory = args.elections_directory
    summary = json.loads((directory/'summary.json').read_text())
    state = json.loads((directory/'elections.json').read_text())
    if (summary['status'] != 'succeeded' or summary['tick'] != 40 or len(state['results']) != 2 or
            state['results'][0]['winner'] is not None or state['mode'] != 'Sandbox' or
            state['firstVictory'] != {'election': {'value': 2}, 'winner': {'value': 1}, 'tick': 20}):
        parser.error('Expected the verified election/majority/sandbox fixture')
    with (directory/'metrics.csv').open(newline='') as source:
        rows = list(csv.DictReader(source))
    if [int(r['tick']) for r in rows] != list(range(1,41)) or rows[-1]['state_hash'] != summary['stateHash']:
        parser.error('Missing election checkpoint hashes')
    (root/'scenarios/platform-elections.commands.jsonl').write_bytes((directory/'commands.jsonl').read_bytes())
    with (root/'scenarios/platform-elections.checkpoints.csv').open('w', newline='') as target:
        writer = csv.writer(target, lineterminator='\n')
        writer.writerow(['tick','state_hash'])
        writer.writerows((r['tick'], r['state_hash']) for r in rows)

if args.stopped_directory:
    directory=args.stopped_directory
    summary=json.loads((directory/'summary.json').read_text())
    if summary['status']!='succeeded' or summary['tick']!=10:
        parser.error('Expected the completed stopped-voting fixture')
    (root/'scenarios/platform-stopped.commands.jsonl').write_bytes((directory/'commands.jsonl').read_bytes())
    (root/'scenarios/platform-stopped.final.sha256').write_text(summary['stateHash']+'\n')

if args.physical_directory:
    directory=args.physical_directory
    summary=json.loads((directory/'summary.json').read_text())
    logistics=json.loads((directory/'logistics.json').read_text())['systems']
    if summary['status']!='succeeded' or not logistics or logistics[0]['completedTrips']<2 or logistics[0]['trip'] is not None:
        parser.error('Expected completed repeated physical deliveries')
    with (directory/'metrics.csv').open(newline='') as source:
        rows=list(csv.DictReader(source))
    if [int(r['tick']) for r in rows]!=list(range(1,summary['tick']+1)) or rows[-1]['state_hash']!=summary['stateHash']:
        parser.error('Missing physical checkpoint hashes')
    (root/'scenarios/platform-physical.commands.jsonl').write_bytes((directory/'commands.jsonl').read_bytes())
    with (root/'scenarios/platform-physical.checkpoints.csv').open('w',newline='') as target:
        writer=csv.writer(target,lineterminator='\n');writer.writerow(['tick','state_hash'])
        writer.writerows((r['tick'],r['state_hash']) for r in rows)

if args.support_directory:
    directory = args.support_directory
    summary = json.loads((directory/'summary.json').read_text())
    support = json.loads((directory/'support.json').read_text())
    if summary['status'] != 'succeeded' or summary['tick'] != 120 or len(support['commitments']) != 2 or any(c['status'] != 'Executed' for c in support['commitments']):
        parser.error('Expected the verified S5 support fixture')
    with (directory/'metrics.csv').open(newline='') as source:
        rows = list(csv.DictReader(source))
    if [int(r['tick']) for r in rows] != list(range(1,121)) or rows[-1]['state_hash'] != summary['stateHash']:
        parser.error('Missing support checkpoint hashes')
    (root/'scenarios/platform-support.commands.jsonl').write_bytes((directory/'commands.jsonl').read_bytes())
    with (root/'scenarios/platform-support.checkpoints.csv').open('w', newline='') as target:
        writer = csv.writer(target, lineterminator='\n')
        writer.writerow(['tick','state_hash'])
        writer.writerows((r['tick'], r['state_hash']) for r in rows)

if args.trade_directory:
    directory=args.trade_directory
    summary=json.loads((directory/'summary.json').read_text())
    talks=json.loads((directory/'negotiations.json').read_text())
    if summary['status']!='succeeded' or summary['tick']!=220 or len(talks)!=3 or any(n['status']!='Accepted' for n in talks):
        parser.error('Expected fully agreed and settled participant-trade fixture')
    with (directory/'metrics.csv').open(newline='') as source:rows=list(csv.DictReader(source))
    if [int(r['tick'])for r in rows]!=list(range(1,221)) or rows[-1]['state_hash']!=summary['stateHash']:
        parser.error('Missing trade checkpoint hashes')
    (root/'scenarios/platform-trade.commands.jsonl').write_bytes((directory/'commands.jsonl').read_bytes())
    with (root/'scenarios/platform-trade.checkpoints.csv').open('w',newline='')as target:
        writer=csv.writer(target,lineterminator='\n');writer.writerow(['tick','state_hash']);writer.writerows((r['tick'],r['state_hash'])for r in rows)

if args.food_directory:
    directory=args.food_directory
    summary=json.loads((directory/'summary.json').read_text())
    state=json.loads((directory/'final-state.json').read_text())
    if summary['status']!='succeeded' or next(f for f in state['flows'] if f['itemId']=='pastry')['exported']!=2:
        parser.error('Expected completed food-chain production/purchase/sale fixture')
    with (directory/'metrics.csv').open(newline='') as source: rows=list(csv.DictReader(source))
    if [int(row['tick']) for row in rows]!=list(range(1,summary['tick']+1)) or rows[-1]['state_hash']!=summary['stateHash']:
        parser.error('Missing food-chain checkpoint hashes')
    (root/'scenarios/platform-food.commands.jsonl').write_bytes((directory/'commands.jsonl').read_bytes())
    with (root/'scenarios/platform-food.checkpoints.csv').open('w',newline='') as target:
        writer=csv.writer(target,lineterminator='\n');writer.writerow(['tick','state_hash']);writer.writerows((row['tick'],row['state_hash']) for row in rows)
