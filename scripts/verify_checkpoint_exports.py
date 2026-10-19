#!/usr/bin/env python3
"""Compare continuous/loaded evidence, excluding wall-clock timings. Author: Шелестов Максим Евгеньевич."""
from pathlib import Path
import sys

world_only='--world-only' in sys.argv
if world_only: sys.argv.remove('--world-only')
if len(sys.argv) < 3 or len(sys.argv[1:]) % 2:
    raise SystemExit('Usage: verify_checkpoint_exports.py CONTINUOUS LOADED [CONTINUOUS LOADED]')
for original, loaded in zip(sys.argv[1::2], sys.argv[2::2]):
    for name in ['final-state.json', 'ledger.jsonl', 'events.jsonl', 'commands.jsonl', 'metrics.csv', 'labor.csv', 'controllers.json', 'decisions.jsonl', 'agents.csv', 'market.json', 'scenario.json', 'delivery.json', 'logistics.json', 'capital-demand.json', 'elections.json', 'support.json', 'public-events.jsonl', 'model-decisions.jsonl', 'public-economy.json', 'research-modifications.json', 'physical-state.json', 'negotiations.json']:
        if world_only and name in ['controllers.json','decisions.jsonl','agents.csv','model-decisions.jsonl']: continue
        if (Path(original)/name).read_bytes() != (Path(loaded)/name).read_bytes():
            raise SystemExit(f'Continuation mismatch: {original} vs {loaded}: {name}')
    print(f'{original} == {loaded}: states, all tick hashes, flows, events and commands match')
