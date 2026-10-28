#!/usr/bin/env python3
"""Bounded regression of implemented economics without Godot. Author: Шелестов Максим Евгеньевич."""
import argparse
import json
import os
import shutil
from pathlib import Path
import subprocess
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--dotnet', default='dotnet', help='SDK executable or absolute path')
parser.add_argument('--output', type=Path, default=Path('artifacts/headless-p22'))
parser.add_argument('--suite',choices=['all','previous','roads','billing','services','runs'],default='all',help='Explicit disjoint suites preserve pre-E2 aggregate deadlines')
parser.add_argument('--phase',choices=['all','tests','runs'],default='all',help='Separate exhaustive unit and runner gates without changing their deadlines')
parser.add_argument('--unit-artifacts',type=Path,help='Completed unit gate fixtures, required by --phase runs')
args = parser.parse_args()
if args.phase=='runs' and args.unit_artifacts is None:parser.error('--phase runs requires --unit-artifacts')
if args.phase!='runs' and args.unit_artifacts is not None:parser.error('--unit-artifacts belongs to --phase runs')
root = Path(__file__).resolve().parents[1]
out = args.output.resolve()

comparison_pairs=0
replays=0
def check(name, timeout, command):
    global comparison_pairs,replays
    values=list(map(str,command))
    compare=next((i for i,v in enumerate(values) if Path(v).name=="verify_checkpoint_exports.py"),None)
    if compare is not None:
        comparison_pairs+=len([v for v in values[compare+1:] if v!="--world-only"])//2
    if "--replay" in values:replays+=1
    result = subprocess.run([sys.executable, str(root/'scripts/run_check.py'), '--name', name,
        '--timeout', str(timeout), '--log-directory', str(out/'checks'), '--', *map(str, command)], cwd=root, check=False)
    if result.returncode:
        for suffix in ('stdout','stderr'):
            log=out/'checks'/f'{name}.{suffix}.log'
            if log.exists():print(log.read_text(errors='replace')[-18000:],file=sys.stderr)
        raise SystemExit(f'{name} failed (exit={result.returncode}); inspect {out/"checks"}')

check('restore', 180, [args.dotnet, 'restore', 'Delsk.sln', '--locked-mode', '--disable-parallel', '-m:1'])
check('build', 180, [args.dotnet, 'build', 'Delsk.sln', '-c', 'Release', '--no-restore', '--disable-build-servers', '-m:1'])
partitions=[];passed=0
if args.phase!='runs':
    os.environ['DELSK_RETURN_ARTIFACTS'] = str(out)
    os.environ['DELSK_D2_EVIDENCE'] = str(out/'d2-fixtures')
    os.environ['DELSK_D3_EVIDENCE'] = str(out/'d3-fixtures')
    os.environ['DELSK_E1_EVIDENCE'] = str(out/'e1-fixtures')
    os.environ['DELSK_E2_EVIDENCE'] = str(out/'e2-fixtures')
    os.environ['DELSK_E3_EVIDENCE'] = str(out/'e3-fixtures')
    os.environ['DELSK_F2_EVIDENCE']=str(out/'f2-fixtures')
    os.environ['DELSK_F1_EVIDENCE']=str(out/'f1-fixtures')
    from test_partitions import run_suite
    partitions=run_suite(check,args.dotnet,'tests/Simulation.Tests/bin/Release/net10.0/Simulation.Tests.dll',out,suite=args.suite)
    passed=sum(p['passed'] for p in partitions)
    if passed<(27 if args.suite=='runs' else 27 if args.suite=='services' else 22 if args.suite=='billing' else 25 if args.suite=='roads' else 1157):raise SystemExit('Incomplete test suite')
    unit={'suite':args.suite,'status':'succeeded','testsPassed':passed,'testPartitions':partitions,'scope':'Exhaustive ordinary unit phase; runner checks are a separate required gate'}
    (out/'unit-check.json').write_text(json.dumps(unit,indent=2)+'\n')
    if args.phase=='tests':
        print(f'Exhaustive unit gate succeeded; results={out}');raise SystemExit(0)
else:
    source=args.unit_artifacts.resolve();unit=json.loads((source/'unit-check.json').read_text())
    from test_partitions import selected_partitions
    assert unit['status']=='succeeded' and unit['testsPassed']>=1157
    assert {p['file'] for p in unit['testPartitions']}=={'tests'+p.name+'.trx' for p in selected_partitions(unit.get('suite','all'))}
    assert sum(p['passed'] for p in unit['testPartitions'])==unit['testsPassed']
    for name in ['late-return-at-expiry','late-return']:
        shutil.copytree(source/name,out/name)
    for name in ['late-return.save.json','late-return-fault.json']:
        shutil.copyfile(source/name,out/name)
runner = [args.dotnet, 'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll']
scenarios = {'cycle':'economic-cycle', 'recovery':'zero-cash-recovery', 'development':'land-and-content',
             'labor':'labor-dynamics', 'equipment':'equipment-cycle', 'autonomous':'autonomous-economy', 'market':'market-window', 'pickup':'pickup-lifecycle', 'delivery':'delivery-lifecycle', 'capital-delivery':'capital-delivery', 'logistics':'logistics-cycle', 'logistics-upgrade':'logistics-upgrade', 'own-storage':'own-storage-cycle', 'inbound':'inbound-cycle', 'own-production':'own-production-cycle', 'logistics-queue':'logistics-queue', 'capital-demand':'capital-demand-cycle', 'autonomous-demand':'autonomous-demand', 'elections':'elections', 'support':'support-cycle','stopped-elections':'stopped-elections','physical-automation':'physical-automation','participant-trade':'participant-trade','food-chain':'food-chain'}
for script, scenario in scenarios.items():
    check(script, 300 if script.startswith('autonomous') else 30, [*runner, '--scenario', f'scenarios/{scenario}.json', '--script', 'autonomous' if script == 'autonomous-demand' else script, '--output', out/script])
for script, tick in [('cycle', 55), ('equipment', 101), ('autonomous', 350), ('market', 44), ('pickup', 47), ('delivery', 37), ('capital-delivery', 3), ('logistics', 69), ('logistics-upgrade', 170), ('own-storage', 170), ('inbound', 78), ('own-production', 170), ('logistics-queue', 185), ('capital-demand', 85), ('autonomous-demand', 350), ('elections', 9), ('support', 18), ('stopped-elections',3),('physical-automation',20),('participant-trade',33),('food-chain',300)]:
    checkpoint = out/f'{script}.save.json'
    check(f'{script}-save', 300 if script.startswith('autonomous') else 30, [*runner, '--scenario', f'scenarios/{scenarios[script]}.json', '--script', 'autonomous' if script == 'autonomous-demand' else script,
        '--output', out/f'{script}-saved', '--checkpoint', checkpoint, '--checkpoint-at', tick])
    check(f'{script}-load', 300 if script.startswith('autonomous') else 30, [*runner, '--load', checkpoint, '--output', out/f'{script}-loaded', *(['--ticks', '1000'] if script.startswith('autonomous') else [])])
    for name in ['final-state.json', 'ledger.jsonl', 'events.jsonl', 'commands.jsonl', 'metrics.csv', 'labor.csv', 'controllers.json', 'decisions.jsonl', 'agents.csv', 'market.json', 'scenario.json', 'delivery.json', 'logistics.json', 'capital-demand.json', 'elections.json', 'support.json', 'public-events.jsonl', 'model-decisions.jsonl', 'public-economy.json', 'research-modifications.json', 'physical-state.json', 'negotiations.json']:
        expected = (out/script/name).read_bytes()
        if any((out/f'{script}-{mode}'/name).read_bytes() != expected for mode in ['saved','loaded']):
            raise SystemExit(f'{script}: checkpoint continuation differs in {name}')
    comparison_pairs+=2
for script in ['logistics-queue', 'capital-demand', 'elections', 'support','stopped-elections','physical-automation','participant-trade','food-chain']:
    final_tick = json.loads((out/script/'summary.json').read_text())['tick']
    check(f'{script}-replay', 30, [*runner, '--scenario', f'scenarios/{scenarios[script]}.json', '--script', script,
        '--replay', out/script/'commands.jsonl', '--ticks', final_tick, '--output', out/f'{script}-replayed'])
    check(f'{script}-replay-compare', 15, [sys.executable, 'scripts/verify_checkpoint_exports.py', out/script, out/f'{script}-replayed'])
baseline_cases = {'economy':(1200,350),'auction':(180,30),'politics':(160,19),'logistics':(360,80),'food':(900,85)}
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
folders = [out/s for s in scenarios] + [out/f'{s}-{mode}' for s in ['cycle','equipment','autonomous','market','pickup','delivery','capital-delivery','logistics','logistics-upgrade','own-storage','inbound','own-production','logistics-queue','capital-demand','autonomous-demand','elections','support','stopped-elections','physical-automation','participant-trade','food-chain'] for mode in ['saved','loaded']]
folders += [out/'late-return-at-expiry', out/'late-return', out/'late-return-loaded']
folders += [out/'logistics-queue-replayed', out/'capital-demand-replayed', out/'elections-replayed', out/'support-replayed',out/'stopped-elections-replayed',out/'physical-automation-replayed',out/'participant-trade-replayed',out/'food-chain-replayed']
folders += [out/f'baseline-{name}{suffix}' for name in baseline_cases for suffix in ['', '-saved','-loaded','-replayed']]
folders += [out/'model-safety',out/'model-safety-loaded',out/'model-safety-replayed',out/'model-off']
check('balances', 30, [sys.executable, 'scripts/verify_results.py', *folders])
# Explicit profile/CLI path: an unprofitable research treatment is a valid run, not a failed baseline fixture.
profile = json.loads((root/'config/economy.json').read_text(encoding='utf-8'))
profile['id'] = 'gate-unprofitable-land'
profile['world']['plotPriceMinor'] = 1000000000
profile['world']['shopSitePriceMinor'] = 1000000000
profile_path = out/'profile-input.json'
profile_path.write_text(json.dumps(profile), encoding='utf-8')
check('economy-profile-validation',30,[*runner,'--script','baseline','--scenario','scenarios/baseline-economy.json',
    '--economy',profile_path,'--validate-only','true'])
check('economy-profile-run',300,[*runner,'--script','baseline','--scenario','scenarios/baseline-economy.json',
    '--economy',profile_path,'--ticks','1200','--human-ballot','self','--profile','true','--output',out/'economy-profile'])
profile_state=json.loads((out/'economy-profile/final-state.json').read_text())
profile_measure=json.loads((out/'economy-profile/profile.json').read_text())
if profile_state['enterprises'] or not profile_measure['checkpointHashMatches'] or profile_measure['measuredTicks'] != 1200:
    raise SystemExit('Research profile/measurement path did not apply its independent economic treatment')
folders.append(out/'economy-profile')
check('economy-profile-balances',30,[sys.executable,'scripts/verify_results.py',out/'economy-profile'])
public_exports=sorted(p.parent for p in out.rglob("public-economy.json"))
check("public-economy-audit",30,[sys.executable,"scripts/verify_public_economy.py",*public_exports])
manifest = {'status':'succeeded', 'scenarios':list(scenarios), 'checkpoint_cases':{'cycle':55,'equipment':101,'autonomous':350,'market':44,'pickup':47,'delivery':37,'capital-delivery':3,'logistics':69,'logistics-upgrade':170,'own-storage':170,'inbound':78,'own-production':170,'logistics-queue':185,'capital-demand':85,'autonomous-demand':350,'elections':9,'support':18,'stopped-elections':3,'physical-automation':20,'participant-trade':33,'food-chain':300},
            'comparison':'every exported tick hash, state, ledger, event, command and labor row',
            'controlled_fixture':{'name':'late-return','origin':'test-only checkpoint fork at68; TTL180 to18; ordinary admission unchanged','checkpoint_tick':72,'final_tick':80},
            'baseline_cases':{name:{'final_tick':horizon,'checkpoint_tick':cut} for name,(horizon,cut) in baseline_cases.items()},
            'model_safety':{'checkpoint_tick':6,'final_tick':60,'valid':1,'forged_actor':1,'late_reply':1},
            'model_off':'exact world equality with baseline-economy and the same headless timing label',
            'testsPassed':passed if args.phase=='all' else None, 'testPartitions':partitions, 'phase':args.phase, 'unitArtifacts':str(args.unit_artifacts) if args.unit_artifacts else None, 'exports':len(folders), 'replays':replays, 'comparison_pairs':comparison_pairs,
            'scope':'Package8 software and full previous economic/controller regression; laboratory/study/window/platform gates separate',
            'author':'Шелестов Максим Евгеньевич'}
(out/'headless-check.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(f'Headless regression succeeded; results={out}')
