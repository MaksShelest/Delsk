#!/usr/bin/env python3
"""Whole-tick long trajectories, persistent checkpoint segments, same ordinary G1 policies.
Only the resolved safety horizon is extended. Author: Шелестов Максим Евгеньевич.
"""
import argparse,datetime,hashlib,json,sys
from pathlib import Path
from research_lab import ROOT,read,write
from research_workspace import binary_hashes
from study_economy_h1 import simulation_inputs,executor,compress_turns
from run_research_series import run_checked
from full_economy_metrics import measure

def digest(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def check(name,seconds,out,cmd):
 code,meta=run_checked(name,seconds,out/'checks',list(map(str,cmd)))
 return code,meta

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);p.add_argument('--map',choices=['small-river','large-districts'],required=True);p.add_argument('--segments',type=int,default=1000);p.add_argument('--compare-first-two',action='store_true');p.add_argument('--retry-failed',action='store_true');p.add_argument('--adopt-tool-update',metavar='REASON');p.add_argument('--recover-completed',action='store_true');a=p.parse_args()
 if a.segments<0:raise ValueError('Segment count must be nonnegative')
 out=a.output.resolve();out.mkdir(parents=True,exist_ok=True);spec=read(ROOT/'docs/research/h1/study-plan.json')['long'];target=spec['targetTicks'];runner=['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll']
 frozen=dict(simulationInputs=simulation_inputs(),binaries=binary_hashes());journal=out/'trajectory.json'
 if journal.exists():
  state=read(journal)
  if state['frozen']!=frozen:raise ValueError('Trajectory code/binaries changed; original evidence is retained')
  if state['map']!=a.map or state['compareFirstTwo']!=a.compare_first_two:raise ValueError('Trajectory map/continuation proof must remain unchanged')
  if state['toolSha256']!=digest(__file__):
   if not a.adopt_tool_update:raise ValueError('Trajectory tool changed; retain evidence and explicitly --adopt-tool-update REASON')
   state.setdefault('toolUpdates',[]).append(dict(oldSha256=state['toolSha256'],newSha256=digest(__file__),reason=a.adopt_tool_update,atUtc=datetime.datetime.now(datetime.timezone.utc).isoformat()))
   state['toolSha256']=digest(__file__);write(journal,state)
  if a.recover_completed:
   if state['status']!='running' or 'activeAttempt' not in state:raise ValueError('No unfinished observer attempt to recover')
   record=dict(state['activeAttempt']);run=Path(record['result']);end=record['targetTick'];checkpoint=Path(record['checkpoint'])
   meta=read(out/'checks'/('segment-'+str(end)+'.json'))
   summary=json.loads((run/'summary.json').read_text(encoding='utf-8'))
   if meta['exit_code']!=0 or summary['status']!='succeeded' or summary['tick']!=end or not checkpoint.exists():raise ValueError('Runner/checkpoint did not complete; cannot recover analysis')
   for tool in ['verify_results.py','verify_public_economy.py']:
    audit=read(out/'checks'/(str(end)+'-'+tool.replace('.py','')+'.json'))
    if audit['exit_code']!=0:raise ValueError('Original audit did not pass; retain failed attempt')
    record[tool]=audit
   record.update(runner=meta,metrics=measure(run),stateHash=summary['stateHash'],checkpointBytes=checkpoint.stat().st_size,checkpointSha256=digest(checkpoint),lastHeartbeat=read(run/'run-progress.json'),observerRecovered=True)
   state['segments'].append(record);state.update(lastSavedTick=end,lastCheckpoint=str(checkpoint),lastCheckpointSha256=record['checkpointSha256'],status='ready');state.pop('activeAttempt');state.pop('activeSegment',None);write(journal,state);compress_turns(run)
  if state['status'] in ('failed','running'):
   if not a.retry_failed:raise ValueError('Failed/interrupted attempt retained; diagnose then explicitly --retry-failed')
   if state['status']=='running':
    active=state['activeAttempt'];run=Path(active['result']);active['failure']='Interrupted host; no completed checkpoint committed'
    if (run/'run-progress.json').exists():active['lastHeartbeat']=read(run/'run-progress.json')
    state['segments'].append(active)
   state['status']='ready';state.pop('activeAttempt',None);state.pop('activeSegment',None);write(journal,state)
 else:
  options=out/'new-game.json';write(options,dict(npcCount=spec['bots'],difficulty=spec['difficulty'],seed=spec['seed']))
  resolution=out/'resolution';code,_=check('resolve',180,out,[*runner,'--scenario',ROOT/f'scenarios/vnext/{a.map}.json','--economy',ROOT/'config/vnext/economy.g1-npc.json','--new-game',options,'--script','baseline','--human-ballot','self','--ticks',1,'--output',resolution])
  if code:return code
  original=read(resolution/'scenario.json');resolved=dict(original);resolved['maxTicks']=target
  source=out/'horizon-scenario.json';write(source,resolved)
  controls=read(resolution/'controllers.json');settings=[json.loads(c['stateJson'])['settings'] for c in controls if c['kind'].startswith('utility-')]
  if len(settings)!=spec['bots'] or not all(c.get('coherentEconomy') for c in settings):raise ValueError('Long trajectory is not the ordinary G1 policy')
  controller=out/'horizon-controllers.json';write(controller,dict(version=1,humanActorId=1,finalTick=target,npcs=settings))
  compare=dict(resolved);compare['maxTicks']=original['maxTicks'];assert compare==original
  state=dict(version=1,map=a.map,ordinaryProfile='vnext-g1-npc-economy-1',naturalStart=True,prepared=False,status='ready',originalMaxTicks=original['maxTicks'],researchMaxTicks=target,
   horizonOnlyOverride=['resolved scenario maxTicks','controller finalTick'],frozen=frozen,toolSha256=digest(__file__),compareFirstTwo=a.compare_first_two,inputs={str(f.relative_to(out)):digest(f) for f in [source,controller,options]},lastSavedTick=0,lastCheckpoint=None,segments=[])
  write(journal,state)
 def continuation():
  if not state['compareFirstTwo'] or state.get('firstTwoContinuationSucceeded') or state['lastSavedTick']!=2*spec['segmentTicks']:return 0
  run=Path(next(r['result'] for r in reversed(state['segments']) if r['targetTick']==state['lastSavedTick'] and 'stateHash' in r))
  attempt=len(state.get('continuationAttempts',[]))+1;continuous=out/('continuous-first-two-'+str(attempt));end=state['lastSavedTick']
  rc,m=check('continuous-first-two-'+str(attempt),spec['timeoutSeconds'],out,[*runner,'--scenario',out/'horizon-scenario.json','--controllers',out/'horizon-controllers.json','--script','baseline','--human-ballot','self','--ticks',end,'--output',continuous,'--progress','true'])
  record=dict(runner=m,result=str(continuous));state.setdefault('continuationAttempts',[]).append(record)
  if not rc:
   rc,m=check('continuation-first-two-'+str(attempt),60,out,[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',continuous,run]);record['comparison']=m
  if not rc and (continuous/'personal-accounts.json').read_bytes()!=(run/'personal-accounts.json').read_bytes():rc=1;record['failure']='Financial projection differs after continuation'
  if rc:state['status']='failed';write(journal,state);return rc
  record['personalAccountsExact']=True;state['firstTwoContinuationSucceeded']=True;write(journal,state);compress_turns(continuous)
  return 0
 code=continuation()
 if code:return code
 for _ in range(a.segments):
  if state['lastSavedTick']>=target:break
  if any(digest(out/f)!=h for f,h in state['inputs'].items()):raise ValueError('Trajectory input changed')
  end=min(target,state['lastSavedTick']+spec['segmentTicks']);attempt=1+sum(r['targetTick']==end for r in state['segments']);run=out/'runs'/str(end)/str(attempt);checkpoint=out/'checkpoints'/(str(end)+'-'+str(attempt)+'.save.json');checkpoint.parent.mkdir(exist_ok=True)
  if run.exists():raise ValueError('Segment attempt already exists; retained for diagnosis')
  cmd=[*runner,'--ticks',end,'--output',run,'--checkpoint',checkpoint,'--checkpoint-at',end,'--progress','true']
  if state['lastCheckpoint']:
   if digest(state['lastCheckpoint'])!=state['lastCheckpointSha256']:raise ValueError('Saved checkpoint bytes changed')
   cmd+=['--load',state['lastCheckpoint']]
  else:cmd+=['--scenario',out/'horizon-scenario.json','--controllers',out/'horizon-controllers.json','--script','baseline','--human-ballot','self']
  state['status']='running';state['activeSegment']=end;state['activeAttempt']=dict(fromTick=state['lastSavedTick'],targetTick=end,attempt=attempt,result=str(run),checkpoint=str(checkpoint));write(journal,state)
  code,meta=check('segment-'+str(end),spec['timeoutSeconds'],out,['/usr/bin/time','-v',*cmd] if Path('/usr/bin/time').exists() else cmd)
  record=dict(state['activeAttempt'],runner=meta,checkpointBytes=checkpoint.stat().st_size if checkpoint.exists() else None)
  progress=run/'run-progress.json'
  if progress.exists():record['lastHeartbeat']=read(progress)
  if code:
   state['status']='failed';record['failure']='Runner/serialization/resource limit; no synthetic outcome substituted';state['segments'].append(record);write(journal,state);return code
  # Physical accounting at every completed checkpoint boundary.
  for tool,budget in [('verify_results.py',60),('verify_public_economy.py',30)]:
   status,audit=check(str(end)+'-'+tool.replace('.py',''),budget,out,[sys.executable,ROOT/'scripts'/tool,run]);record[tool]=audit
   if status:state['status']='failed';record['failure']='Audit failed';state['segments'].append(record);write(journal,state);return status
  record['metrics']=measure(run);record['stateHash']=json.loads((run/'summary.json').read_text(encoding='utf-8'))['stateHash']
  record['checkpointSha256']=digest(checkpoint)
  state['segments'].append(record);state['lastSavedTick']=end;state['lastCheckpoint']=str(checkpoint);state['lastCheckpointSha256']=record['checkpointSha256'];state['status']='completed' if end==target else 'ready';state.pop('activeSegment',None);state.pop('activeAttempt',None);write(journal,state)
  print(json.dumps(dict(map=a.map,tick=end,target=target,checkpointBytes=record['checkpointBytes'])),flush=True)
  rc=continuation()
  if rc:return rc
  # Preserve all controller observations without retaining repeated plain JSON on disk.
  if not (a.compare_first_two and end==spec['segmentTicks']):compress_turns(run)
 write(journal,state);return 0
if __name__=='__main__':sys.exit(main())
