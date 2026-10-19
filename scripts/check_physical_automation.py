#!/usr/bin/env python3
"""Bounded ordinary-map integration/profile gate for package3. Шелестов Максим Евгеньевич."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

p=argparse.ArgumentParser()
p.add_argument('--dotnet',default='dotnet')
p.add_argument('--output',type=Path,default=Path('artifacts/package3-maps'))
a=p.parse_args()
root=Path(__file__).resolve().parents[1]
out=a.output.resolve()
runner=[a.dotnet,'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll']
def check(name,timeout,command):
    subprocess.run([sys.executable,str(root/'scripts/run_check.py'),'--name',name,'--timeout',str(timeout),
        '--log-directory',str(out/'checks'),'--',*map(str,command)],cwd=root,check=True)

for name in ['one-v2','two-v2','three','one-v3','two-v3','three-v2']:
    check('map-'+name,30,[*runner,'--scenario',f'scenarios/presentation-map-{name}.json',
        '--economy','config/economy.json','--validate-only','true','--output',out/('validation-'+name)])
common=[*runner,'--script','baseline','--scenario','scenarios/presentation-map-three-v2.json',
    '--controllers','scenarios/baseline-controllers.json','--economy','config/economy.json','--human-ballot','self','--ticks',900]
check('third-map',300,[*common,'--profile','true','--output',out/'continuous'])
check('third-map-save',300,[*common,'--checkpoint',out/'third-map.save.json','--checkpoint-at',350,'--output',out/'saved'])
check('third-map-load',300,[*runner,'--load',out/'third-map.save.json','--ticks',900,'--output',out/'loaded'])
check('third-map-replay',300,[*runner,'--script','baseline','--scenario',out/'continuous/scenario.json',
    '--replay',out/'continuous/commands.jsonl','--ticks',900,'--output',out/'replayed'])
check('direct-compare',30,[sys.executable,'scripts/verify_checkpoint_exports.py',out/'continuous',out/'saved',out/'continuous',out/'loaded'])
check('replay-compare',30,[sys.executable,'scripts/verify_checkpoint_exports.py','--world-only',out/'continuous',out/'replayed'])
for verifier in ['verify_results.py','verify_public_economy.py']:
    check(Path(verifier).stem.replace('_','-'),30,[sys.executable,'scripts/'+verifier,*[out/name for name in ['continuous','saved','loaded','replayed']]])
events=[json.loads(line) for line in (out/'continuous/events.jsonl').read_text().splitlines()]
profile=json.loads((out/'continuous/profile.json').read_text())
summary=json.loads((out/'continuous/summary.json').read_text())
assert profile['checkpointHashMatches'] is True and summary['tick']==900
(out/'physical-check.json').write_text(json.dumps({
    'status':'succeeded','mapsValidated':6,'exports':4,'comparisonPairs':3,'checkpointTick':350,'finalTick':900,
    'stateHash':summary['stateHash'],'profile':profile,
    'acceptedCommands':sum(e['type']=='CommandAccepted' for e in events),
    'rejectedCommands':summary['controllerRejections'],
    'scope':'Ordinary third map; three baseline NPCs; explicit human self ballot; no live model or rendering claim'
},indent=2)+'\n')
