#!/usr/bin/env python3
"""Bounded regression of implemented economics without Godot. Author: Шелестов Максим Евгеньевич."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--dotnet', default='dotnet', help='SDK executable or absolute path')
parser.add_argument('--output', type=Path, default=Path('artifacts/headless-inbound'))
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
out = args.output.resolve()

def check(name, timeout, command):
    result = subprocess.run([sys.executable, str(root/'scripts/run_check.py'), '--name', name,
        '--timeout', str(timeout), '--log-directory', str(out/'checks'), '--', *map(str, command)], cwd=root, check=False)
    if result.returncode:
        raise SystemExit(f'{name} failed (exit={result.returncode}); inspect {out/"checks"}')

check('restore', 180, [args.dotnet, 'restore', 'Delsk.sln', '--locked-mode', '--disable-parallel', '-m:1'])
check('build', 180, [args.dotnet, 'build', 'Delsk.sln', '-c', 'Release', '--no-restore', '--disable-build-servers', '-m:1'])
check('tests', 120, [args.dotnet, 'tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll',
    '-noColor', '-noLogo', '-parallelMode', 'none', '-result-trx', out/'tests.trx'])
runner = [args.dotnet, 'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll']
scenarios = {'cycle':'economic-cycle', 'recovery':'zero-cash-recovery', 'development':'land-and-content',
             'labor':'labor-dynamics', 'equipment':'equipment-cycle', 'autonomous':'autonomous-economy', 'market':'market-window', 'pickup':'pickup-lifecycle', 'delivery':'delivery-lifecycle', 'capital-delivery':'capital-delivery', 'logistics':'logistics-cycle', 'logistics-upgrade':'logistics-upgrade', 'own-storage':'own-storage-cycle', 'inbound':'inbound-cycle'}
for script, scenario in scenarios.items():
    check(script, 300 if script == 'autonomous' else 30, [*runner, '--scenario', f'scenarios/{scenario}.json', '--script', script, '--output', out/script])
for script, tick in [('cycle', 55), ('equipment', 101), ('autonomous', 350), ('market', 44), ('pickup', 47), ('delivery', 37), ('capital-delivery', 3), ('logistics', 69), ('logistics-upgrade', 170), ('own-storage', 170), ('inbound', 78)]:
    checkpoint = out/f'{script}.save.json'
    check(f'{script}-save', 300 if script == 'autonomous' else 30, [*runner, '--scenario', f'scenarios/{scenarios[script]}.json', '--script', script,
        '--output', out/f'{script}-saved', '--checkpoint', checkpoint, '--checkpoint-at', tick])
    check(f'{script}-load', 300 if script == 'autonomous' else 30, [*runner, '--load', checkpoint, '--output', out/f'{script}-loaded', *(['--ticks', '1000'] if script == 'autonomous' else [])])
    for name in ['final-state.json', 'ledger.jsonl', 'events.jsonl', 'commands.jsonl', 'metrics.csv', 'labor.csv', 'controllers.json', 'decisions.jsonl', 'agents.csv', 'market.json', 'scenario.json', 'delivery.json', 'logistics.json']:
        expected = (out/script/name).read_bytes()
        if any((out/f'{script}-{mode}'/name).read_bytes() != expected for mode in ['saved','loaded']):
            raise SystemExit(f'{script}: checkpoint continuation differs in {name}')
folders = [out/s for s in scenarios] + [out/f'{s}-{mode}' for s in ['cycle','equipment','autonomous','market','pickup','delivery','capital-delivery','logistics','logistics-upgrade','own-storage','inbound'] for mode in ['saved','loaded']]
check('balances', 15, [sys.executable, 'scripts/verify_results.py', *folders])
manifest = {'status':'succeeded', 'scenarios':list(scenarios), 'checkpoint_cases':{'cycle':55,'equipment':101,'autonomous':350,'market':44,'pickup':47,'delivery':37,'capital-delivery':3,'logistics':69,'logistics-upgrade':170,'own-storage':170,'inbound':78},
            'comparison':'every exported tick hash, state, ledger, event, command and labor row',
            'scope':'P00-P17 inbound: shared input/output transport with independent switches and saved empty approach/loaded return; general queues/full late-return gate/politics remain future tasks',
            'author':'Шелестов Максим Евгеньевич'}
(out/'headless-check.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(f'Headless regression succeeded; results={out}')
