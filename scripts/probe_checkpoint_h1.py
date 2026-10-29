#!/usr/bin/env python3
"""Adaptive H1 engineering diagnostic after bounded long-run failure; never a new economic replicate.
Same saved world and binaries, a shorter save interval, unchanged 64/512MiB limits.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,hashlib,json,sys
from pathlib import Path
from research_lab import ROOT,write,read
from research_workspace import binary_hashes
from study_economy_h1 import simulation_inputs
from run_research_series import run_checked
from collect_evidence_h1 import checkpoint_bytes

def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--trajectory',type=Path,action='append',required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();out=a.output.resolve()
 if out.exists() and any(out.iterdir()):raise ValueError('Retain prior diagnostic; use a new output')
 out.mkdir(parents=True,exist_ok=True);frozen=dict(simulationInputs=simulation_inputs(),binaries=binary_hashes());plans=[]
 for folder in a.trajectory:
  folder=folder.resolve();state=read(folder/'trajectory.json')
  if state['status']=='running':raise ValueError('Wait until the original trajectory process has stopped')
  if state['frozen']!=frozen:raise ValueError('Simulation inputs/binaries differ from the saved trajectory')
  checkpoint=Path(state['lastCheckpoint'])
  if digest(checkpoint)!=state['lastCheckpointSha256']:raise ValueError('Original checkpoint changed')
  target=min(state['researchMaxTicks'],state['lastSavedTick']+800)
  plans.append(dict(map=state['map'],checkpoint=str(checkpoint),checkpointSha256=digest(checkpoint),fromTick=state['lastSavedTick'],targetTick=target,timeoutSeconds=900))
 write(out/'plan.json',dict(version=1,scope=__doc__,adaptiveReason='Original 1800-tick segment exceeded900s; separate 800-tick checkpoint diagnostic. Original failure is retained. No parameter or reader bound is raised.',independentEconomicReplicate=False,frozen=frozen,plans=plans))
 results=[]
 for plan in plans:
  folder=out/plan['map'];checkpoint=out/(plan['map']+'.save.json');name='checkpoint-'+plan['map'];runner=['dotnet',str(ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll')]
  cmd=[*runner,'--load',plan['checkpoint'],'--ticks',str(plan['targetTick']),'--checkpoint',str(checkpoint),'--checkpoint-at',str(plan['targetTick']),'--progress','true','--output',str(folder)]
  rc,meta=run_checked(name,900,out/'checks',cmd);error='\n'.join(p.read_text(errors='replace') for p in [out/'checks'/(name+'.stderr.log'),out/'checks'/(name+'.stdout.log')] if p.exists())
  row=dict(plan,runner=meta,status='succeeded' if rc==0 else 'failed',checkpointTooLarge='CheckpointTooLarge' in error)
  if (folder/'run-progress.json').exists():row['lastHeartbeat']=read(folder/'run-progress.json')
  if rc==0:
   for tool,budget in [('verify_results.py',60),('verify_public_economy.py',30)]:
    code,audit=run_checked(name+'-'+tool.replace('.py',''),budget,out/'checks',[sys.executable,str(ROOT/'scripts'/tool),str(folder)]);row[tool]=audit
    if code:raise ValueError('Diagnostic audit failed; retained original outputs')
   row['producedCheckpoint']=dict(path=str(checkpoint),sha256=digest(checkpoint),**checkpoint_bytes(checkpoint))
  results.append(row);write(out/'diagnostic.json',dict(version=2,scope=__doc__,independentEconomicReplicate=False,results=results))
  print(json.dumps(dict(map=plan['map'],exitCode=rc,checkpointTooLarge=row['checkpointTooLarge'])),flush=True)
 return int(any(r['status']=='failed' and not r['checkpointTooLarge'] for r in results))
if __name__=='__main__':sys.exit(main())
