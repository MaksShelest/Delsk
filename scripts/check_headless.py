#!/usr/bin/env python3
"""Bounded regression of implemented economics without Godot. Author: Шелестов Максим Евгеньевич."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--dotnet', default='dotnet', help='SDK executable or absolute path')
parser.add_argument('--output', type=Path, default=Path('artifacts/headless-p22'))
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
os.environ['DELSK_RETURN_ARTIFACTS'] = str(out)
check('tests', 180, [args.dotnet, 'tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll',
    '-noColor', '-noLogo', '-parallelMode', 'none', '-result-trx', out/'tests.trx'])
import xml.etree.ElementTree as ET
trx=ET.parse(out/'tests.trx').getroot()
counters=next(e for e in trx.iter() if e.tag.endswith('Counters'))
if int(counters.attrib['passed']) < 640 or int(counters.attrib['failed']) != 0:
    raise SystemExit('Incomplete test suite')
runner = [args.dotnet, 'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll']
scenarios = {'cycle':'economic-cycle', 'recovery':'zero-cash-recovery', 'development':'land-and-content',
             'labor':'labor-dynamics', 'equipment':'equipment-cycle', 'autonomous':'autonomous-economy', 'market':'market-window', 'pickup':'pickup-lifecycle', 'delivery':'delivery-lifecycle', 'capital-delivery':'capital-delivery', 'logistics':'logistics-cycle', 'logistics-upgrade':'logistics-upgrade', 'own-storage':'own-storage-cycle', 'inbound':'inbound-cycle', 'own-production':'own-production-cycle', 'logistics-queue':'logistics-queue', 'capital-demand':'capital-demand-cycle', 'autonomous-demand':'autonomous-demand', 'elections':'elections', 'support':'support-cycle'}
for script, scenario in scenarios.items():
    check(script, 300 if script.startswith('autonomous') else 30, [*runner, '--scenario', f'scenarios/{scenario}.json', '--script', 'autonomous' if script == 'autonomous-demand' else script, '--output', out/script])
for script, tick in [('cycle', 55), ('equipment', 101), ('autonomous', 350), ('market', 44), ('pickup', 47), ('delivery', 37), ('capital-delivery', 3), ('logistics', 69), ('logistics-upgrade', 170), ('own-storage', 170), ('inbound', 78), ('own-production', 170), ('logistics-queue', 185), ('capital-demand', 85), ('autonomous-demand', 350), ('elections', 9), ('support', 18)]:
    checkpoint = out/f'{script}.save.json'
    check(f'{script}-save', 300 if script.startswith('autonomous') else 30, [*runner, '--scenario', f'scenarios/{scenarios[script]}.json', '--script', 'autonomous' if script == 'autonomous-demand' else script,
        '--output', out/f'{script}-saved', '--checkpoint', checkpoint, '--checkpoint-at', tick])
    check(f'{script}-load', 300 if script.startswith('autonomous') else 30, [*runner, '--load', checkpoint, '--output', out/f'{script}-loaded', *(['--ticks', '1000'] if script.startswith('autonomous') else [])])
    for name in ['final-state.json', 'ledger.jsonl', 'events.jsonl', 'commands.jsonl', 'metrics.csv', 'labor.csv', 'controllers.json', 'decisions.jsonl', 'agents.csv', 'market.json', 'scenario.json', 'delivery.json', 'logistics.json', 'capital-demand.json', 'elections.json', 'support.json', 'public-events.jsonl', 'model-decisions.jsonl']:
        expected = (out/script/name).read_bytes()
        if any((out/f'{script}-{mode}'/name).read_bytes() != expected for mode in ['saved','loaded']):
            raise SystemExit(f'{script}: checkpoint continuation differs in {name}')
for script in ['logistics-queue', 'capital-demand', 'elections', 'support']:
    final_tick = json.loads((out/script/'summary.json').read_text())['tick']
    check(f'{script}-replay', 30, [*runner, '--scenario', f'scenarios/{scenarios[script]}.json', '--script', script,
        '--replay', out/script/'commands.jsonl', '--ticks', final_tick, '--output', out/f'{script}-replayed'])
    check(f'{script}-replay-compare', 15, [sys.executable, 'scripts/verify_checkpoint_exports.py', out/script, out/f'{script}-replayed'])
baseline_cases = {'economy':(1200,350),'auction':(180,30),'politics':(160,19),'logistics':(360,80)}
for name,(horizon,cut) in baseline_cases.items():
    script='baseline-'+name
    config='baseline-controllers' if name=='economy' else script+'-controllers'
    common=[*runner,'--script','baseline','--scenario',f'scenarios/{script}.json','--controllers',f'scenarios/{config}.json']
    checkpoint=out/f'{script}.save.json'
    check(script,300,[*common,'--output',out/script])
    check(script+'-save',300,[*common,'--output',out/f'{script}-saved','--checkpoint',checkpoint,'--checkpoint-at',cut])
    check(script+'-load',300,[*runner,'--load',checkpoint,'--ticks',horizon,'--output',out/f'{script}-loaded'])
    for mode in ['saved','loaded']:
        check(script+'-'+mode+'-compare',15,[sys.executable,'scripts/verify_checkpoint_exports.py',out/script,out/f'{script}-{mode}'])
    check(script+'-replay',300,[*runner,'--script','baseline','--scenario',f'scenarios/{script}.json',
        '--replay',out/script/'commands.jsonl','--ticks',horizon,'--output',out/f'{script}-replayed'])
    check(script+'-replay-compare',15,[sys.executable,'scripts/verify_checkpoint_exports.py','--world-only',out/script,out/f'{script}-replayed'])
# Controlled asynchronous S7 records one valid choice, one forged actor and one late reply.
# Continue from this very checkpoint: live latency is evidence, not seed-deterministic input.
check('model-safety',30,[*runner,'--script','model-safety','--output',out/'model-safety',
    '--checkpoint',out/'model-safety.save.json','--checkpoint-at',6])
check('model-safety-load',30,[*runner,'--load',out/'model-safety.save.json','--ticks',60,'--output',out/'model-safety-loaded'])
check('model-safety-load-compare',15,[sys.executable,'scripts/verify_checkpoint_exports.py',out/'model-safety',out/'model-safety-loaded'])
check('model-safety-replay',30,[*runner,'--script','model-safety','--scenario','scenarios/model-safety.json',
    '--replay',out/'model-safety'/'commands.jsonl','--ticks',60,'--output',out/'model-safety-replayed'])
check('model-safety-replay-compare',15,[sys.executable,'scripts/verify_checkpoint_exports.py','--world-only',out/'model-safety',out/'model-safety-replayed'])
check('model-off',300,[*runner,'--script','hybrid','--model-mode','off','--output',out/'model-off'])
check('model-off-compare',15,[sys.executable,'scripts/verify_checkpoint_exports.py','--world-only',out/'baseline-economy',out/'model-off'])
disabled = json.loads((out/'model-off'/'manifest.json').read_text(encoding='utf-8'))
baseline = json.loads((out/'baseline-economy'/'manifest.json').read_text(encoding='utf-8'))
if disabled.get('timingPolicy') != 'headless' or baseline.get('timingPolicy') != 'headless':
    raise SystemExit(f"Disabled model changed the baseline clock: hybrid={disabled.get('timingPolicy')} baseline={baseline.get('timingPolicy')}")
if (disabled.get('model') or {}).get('modelDigest') not in (None,):
    raise SystemExit('Runner stored an unmeasured model digest')
check('model-privacy',15,[sys.executable,'scripts/verify_model_results.py',out/'model-safety',out/'model-safety-loaded',out/'model-off'])
check('late-return-load', 30, [*runner, '--load', out/'late-return.save.json', '--output', out/'late-return-loaded'])
check('late-return-compare', 15, [sys.executable, 'scripts/verify_checkpoint_exports.py', out/'late-return', out/'late-return-loaded'])
folders = [out/s for s in scenarios] + [out/f'{s}-{mode}' for s in ['cycle','equipment','autonomous','market','pickup','delivery','capital-delivery','logistics','logistics-upgrade','own-storage','inbound','own-production','logistics-queue','capital-demand','autonomous-demand','elections','support'] for mode in ['saved','loaded']]
folders += [out/'late-return-at-expiry', out/'late-return', out/'late-return-loaded']
folders += [out/'logistics-queue-replayed', out/'capital-demand-replayed', out/'elections-replayed', out/'support-replayed']
folders += [out/f'baseline-{name}{suffix}' for name in baseline_cases for suffix in ['', '-saved','-loaded','-replayed']]
folders += [out/'model-safety',out/'model-safety-loaded',out/'model-safety-replayed',out/'model-off']
check('balances', 30, [sys.executable, 'scripts/verify_results.py', *folders])
manifest = {'status':'succeeded', 'scenarios':list(scenarios), 'checkpoint_cases':{'cycle':55,'equipment':101,'autonomous':350,'market':44,'pickup':47,'delivery':37,'capital-delivery':3,'logistics':69,'logistics-upgrade':170,'own-storage':170,'inbound':78,'own-production':170,'logistics-queue':185,'capital-demand':85,'autonomous-demand':350,'elections':9,'support':18},
            'comparison':'every exported tick hash, state, ledger, event, command and labor row',
            'controlled_fixture':{'name':'late-return','origin':'test-only checkpoint fork at68; TTL180 to18; ordinary admission unchanged','checkpoint_tick':72,'final_tick':80},
            'baseline_cases':{name:{'final_tick':horizon,'checkpoint_tick':cut} for name,(horizon,cut) in baseline_cases.items()},
            'model_safety':{'checkpoint_tick':6,'final_tick':60,'valid':1,'forged_actor':1,'late_reply':1},
            'model_off':'exact world equality with baseline-economy and the same headless timing label',
            'exports':len(folders), 'replays':9, 'comparison_pairs':54,
            'scope':'P00-P26.6 play HUD and context actions; live Ollama series is separate evidence; windowed and device checks are separate',
            'author':'Шелестов Максим Евгеньевич'}
(out/'headless-check.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(f'Headless regression succeeded; results={out}')
