#!/usr/bin/env python3
"""Prepared zero-cash diagnostic. Resolve canonical G1 in C#, then change only initial cash and the declared research profile id.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,copy,hashlib,json,sys
from pathlib import Path
from research_lab import ROOT,read,write
from study_economy_h1 import freeze,compress_turns
from run_research_series import run_checked
from full_economy_metrics import measure

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=True);freeze(out)
 if (out/'diagnostic.json').exists():raise ValueError('Retain completed diagnostic and use a new output')
 spec=read(ROOT/'docs/research/h1/zero-cash-diagnostic.json');runner=['dotnet',str(ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll')];rows=[]
 def check(name,seconds,cmd):return run_checked(name,seconds,out/'checks',list(map(str,cmd)))
 for map_key in spec['maps']:
  base=out/map_key;base.mkdir(exist_ok=True);options=base/'options.json';write(options,dict(npcCount=spec['bots'],difficulty=spec['difficulty'],seed=spec['seed']))
  resolution=base/'canonical-resolution';rc,meta=check(map_key+'-resolve',180,[*runner,'--scenario',ROOT/f'scenarios/vnext/{map_key}.json','--economy',ROOT/'config/vnext/economy.g1-npc.json','--new-game',options,'--script','baseline','--human-ballot','self','--ticks',1,'--output',resolution])
  if rc:raise ValueError('Canonical C# resolution failed; original diagnostics retained')
  original=read(resolution/'scenario.json');scenario=copy.deepcopy(original)
  for participant in scenario['participants']:participant['cashMinor']=0
  scenario['economyProfileId']='h1-zero-cash-diagnostic-1'
  restored=copy.deepcopy(scenario)
  for left,right in zip(restored['participants'],original['participants']):left['cashMinor']=right['cashMinor']
  restored['economyProfileId']=original['economyProfileId'];assert restored==original
  source=base/'zero-cash-scenario.json';write(source,scenario)
  settings=[json.loads(c['stateJson'])['settings'] for c in read(resolution/'controllers.json') if c['kind'].startswith('utility-')]
  assert len(settings)==spec['bots'] and all(s['coherentEconomy'] for s in settings)
  controllers=base/'controllers.json';write(controllers,dict(version=1,humanActorId=1,finalTick=original['maxTicks'],npcs=settings))
  write(base/'condition.json',dict(version=1,prepared=True,changes=['participants[*].cashMinor=0','economyProfileId=h1-zero-cash-diagnostic-1'],allOtherResolvedValuesExact=True,canonicalProfileSha256=original['newGame']['economyProfileSha256'],scenarioSha256=hashlib.sha256(source.read_bytes()).hexdigest(),controllersSha256=hashlib.sha256(controllers.read_bytes()).hexdigest()))
  folder=base/'run';rc,meta=check(map_key+'-zero',spec['timeoutSeconds'],[*runner,'--scenario',source,'--controllers',controllers,'--script','baseline','--human-ballot','self','--ticks',spec['ticks'],'--progress','true','--output',folder])
  row=dict(map=map_key,status='succeeded' if rc==0 else 'failed',prepared=True,result=str(folder),check=meta,condition=read(base/'condition.json'))
  if not rc:
   for tool,budget in [('verify_results.py',60),('verify_public_economy.py',30)]:
    code,audit=check(map_key+'-'+tool.replace('.py',''),budget,[sys.executable,ROOT/'scripts'/tool,folder]);row[tool]=audit
    if code:raise ValueError('Zero-cash audit failed; original outputs retained')
   assert all(p['cashMinor']==0 for p in read(folder/'scenario.json')['participants'])
   row['metrics']=measure(folder);row['npcPositiveFinalCash']=sum(v.get('cashMinor',0)>0 for k,v in row['metrics']['fullEconomy']['actors'].items() if k!='1');compress_turns(folder)
  rows.append(row);write(out/'diagnostic.json',dict(version=2,prepared=True,protocol=spec,rows=rows));print(json.dumps(dict(map=map_key,status=row['status'],npcPositiveCash=row.get('npcPositiveFinalCash'))),flush=True)
 return int(any(r['status']!='succeeded' for r in rows))
if __name__=='__main__':sys.exit(main())
