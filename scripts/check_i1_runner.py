#!/usr/bin/env python3
"""Real I1 runner exports, all loaded models, three ordinary maps and independent audits.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--unit',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--cargo-fixtures',type=Path,help='Optional supplementary loaded-model unit artifacts');a=p.parse_args();unit=a.unit.resolve();cargo=(a.cargo_fixtures or a.unit).resolve();out=a.output.resolve()
if out.exists() and any(out.iterdir()):p.error('Use a new empty output; preserve previous attempts')
out.mkdir(parents=True,exist_ok=True);runner=['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll'];checks=[];folders=[];pairs=0;rows=[]
def check(name,cmd,timeout=120):
 subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout',str(timeout),'--log-directory',out/'checks','--',*map(str,cmd)],cwd=ROOT,check=True);checks.append(name)
def state_hash(folder):return json.loads((folder/'summary.json').read_text())['stateHash']
records=[json.loads(s) for s in (cargo/'i1-fixtures/measurements.jsonl').read_text().splitlines()]
loaded={r['model']:r for r in records if r['kind']=='loadedReplay'}
assert set(loaded)=={'sputnik20','muravey70','eger70','vector200','trakt_d1'}
for model,r in loaded.items():
 name='i1-loaded-model-'+model;source=cargo/'e2-fixtures';direct=out/(model+'-replay');continued=out/(model+'-loaded')
 check(model+'-replay',[*runner,'--scenario',source/(name+'.json'),'--replay',source/(name+'.commands.jsonl'),'--ticks',r['tick'],'--output',direct]);folders.append(direct)
 check(model+'-loaded',[*runner,'--load',cargo/'i1-fixtures'/('active-model-'+model+'.save.json'),'--ticks',r['tick'],'--output',continued]);folders.append(continued)
 assert state_hash(direct)==state_hash(continued)==r['hash']
 check(model+'-equal',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',direct,continued]);pairs+=1;rows.append(dict(model=model,tick=r['tick'],hash=r['hash']))
for map_key,count in [('compact-valley',3),('small-river',6),('large-districts',10)]:
 options=out/(map_key+'-options.json');options.write_text(json.dumps(dict(npcCount=count,difficulty='Medium',seed=42)))
 direct=out/map_key;loaded_dir=out/(map_key+'-loaded');replayed=out/(map_key+'-replayed');save=out/(map_key+'.save.json')
 check(map_key,[*runner,'--scenario',ROOT/'scenarios/vnext'/(map_key+'.json'),'--economy',ROOT/'config/vnext/economy.g1-npc.json','--new-game',options,'--script','baseline','--human-ballot','self','--ticks','80','--checkpoint',save,'--checkpoint-at','40','--output',direct],300);folders.append(direct)
 scenario=json.loads((direct/'scenario.json').read_text());assert scenario['economy']['movement']==dict(version=2,speedDivisor=40,speedMultiplier=3,nominalSpeeds=dict(runningRoad=10,runningOffroad=8,vehicleOffroad=10))
 check(map_key+'-load',[*runner,'--load',save,'--ticks','80','--output',loaded_dir],300);folders.append(loaded_dir)
 check(map_key+'-equal',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',direct,loaded_dir]);pairs+=1
 check(map_key+'-replay',[*runner,'--scenario',direct/'scenario.json','--replay',direct/'commands.jsonl','--ticks','80','--output',replayed],300);folders.append(replayed)
 check(map_key+'-replay-equal',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py','--world-only',direct,replayed]);pairs+=1
 rows.append(dict(map=map_key,npcCount=count,ticks=80,hash=state_hash(direct)))
expected=json.loads((unit/'i1-fixtures/native-highway.expected.json').read_text());native=out/'native-highway'
check('native-highway',[*runner,'--load',unit/'i1-fixtures/native-highway.save.json','--ticks',expected['tick'],'--output',native]);folders.append(native);assert state_hash(native)==expected['hash']
check('independent-accounting',[sys.executable,ROOT/'scripts/verify_results.py',*folders]);check('independent-capital-and-privacy',[sys.executable,ROOT/'scripts/verify_public_economy.py',*folders])
(out/'gate.json').write_text(json.dumps(dict(author='Шелестов Максим Евгеньевич',status='succeeded',processChecks=len(checks),exports=len(folders),exactComparisonPairs=pairs,rows=rows,scope='Actual C# I1 runner, five loaded models restored mid-route, native midquantum save, new ordinary3/6/10NPC on three maps, full independent audits. Prepared cargo cases and80-tick ordinary exposure; not long economic balance/device acceptance.'),ensure_ascii=False,indent=2)+'\n')
