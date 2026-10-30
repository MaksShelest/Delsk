#!/usr/bin/env python3
"""Actual adjacent personal movement vs controller map time; no Python pathfinding.
Author: Шелестов Максим Евгеньевич. Prepared diagnostic, not an ordinary replicate.
"""
import argparse,json,subprocess,sys,hashlib
from pathlib import Path
from research_lab import ROOT,read,read_metadata,write
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--canonical-root',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();base=a.output.resolve()
if base.exists() and any(base.iterdir()):p.error('Use a new empty output; retain prior attempts')
base.mkdir(parents=True,exist_ok=True)
plan=read(ROOT/'docs/research/h1-post-g2/transport-diagnostic-plan.json');records=[]
runtime=ROOT/'src/Research.Runner/bin/Release/net10.0';frozen={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in runtime.glob('*.dll')}
for map_key in plan['maps']:
 source=a.canonical_root.resolve()/map_key/'canonical-resolution/scenario.json';s=read_metadata(source);spawn=s['participants'][0]['position'];blocked={(p['x'],p['z']) for p in s['blockedCells']}
 for index,(dx,dz) in enumerate(plan['directions']):
  name=f'{map_key}-{index}';target=dict(x=spawn['x']+dx,z=spawn['z']+dz);row=dict(map=map_key,direction=[dx,dz],spawn=spawn,target=target,scenarioSha256=hashlib.sha256(source.read_bytes()).hexdigest(),forecastMoveTicks=s['moveTicksPerCell'])
  if (target['x'],target['z']) in blocked or not (0<=target['x']<s['mapWidth'] and 0<=target['z']<s['mapHeight']):row['status']='unavailable';records.append(row);continue
  commands=base/(name+'.commands.jsonl');write_command=dict(submittedAtTick=0,authenticatedActor=dict(value=1),command=dict(commandId='h1-probe-1',actorId=dict(value=1),localSequence=1,issuedAtTick=0,notBeforeTick=1,expiresAtTick=11,action=dict(kind='MoveTo',target=target)))
  commands.write_text(json.dumps(write_command)+'\n');out=base/name
  command=[sys.executable,str(ROOT/'scripts/run_check.py'),'--name',name,'--timeout','60','--log-directory',str(base/'checks'),'--','dotnet',str(runtime/'Research.Runner.dll'),'--scenario',str(source),'--replay',str(commands),'--script','baseline','--ticks','10','--output',str(out)]
  r=subprocess.run(command);row['exitCode']=r.returncode
  if r.returncode:row['status']='failed'
  else:
   events=[json.loads(line) for line in (out/'events.jsonl').read_text().splitlines()];starts=[e for e in events if e['type']=='MovementStarted' and e['commandId']=='h1-probe-1'];ends=[e for e in events if e['type']=='MovementArrived' and e['commandId']=='h1-probe-1']
   row['status']='observed' if len(starts)==len(ends)==1 else 'unmatched';row['events']=starts+ends
   if row['status']=='observed':row['actualTravelTicks']=ends[0]['tick']-starts[0]['tick']
  records.append(row);write(base/'diagnostic.json',dict(plan=plan,runtimeSha256=frozen,records=records))
write(base/'diagnostic.json',dict(plan=plan,runtimeSha256=frozen,records=records));print([(r['map'],r['direction'],r['status'],r.get('actualTravelTicks')) for r in records])
