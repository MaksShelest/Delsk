#!/usr/bin/env python3
"""Bounded, relocatable H1 trajectories over the real C# ordinary game.
Author: Шелестов Максим Евгеньевич. No simulation or H2 optimization in Python.
"""
import argparse,contextlib,gzip,hashlib,json,os,signal,subprocess,sys,time
from pathlib import Path
from research_lab import ROOT,read,read_metadata,write
from study_economy_h1 import simulation_inputs,compress_turns
from run_research_series import run_checked
from full_economy_metrics import measure
PLAN=ROOT/'docs/research/h1-post-g2/study-plan.json'

def digest(path):
 h=hashlib.sha256()
 with Path(path).open('rb') as f:
  for data in iter(lambda:f.read(1024*1024),b''):h.update(data)
 return h.hexdigest()

def cases(plan,phase):
 s=plan[phase]
 return [dict(id=f'{phase}-{m}-{n}-{d}-{seed}',phase=phase,map=m,bots=n,difficulty=d,seed=seed) for m in plan['maps'] for n in s['bots'] for d in s['difficulties'] for seed in s['seedsByBots'][str(n)]]

def boundaries(plan,case):
 spec=plan[case['phase']]
 if 'boundaries' in spec:return spec['boundaries']
 return list(range(spec['segmentTicks'],spec['targetTicks'],spec['segmentTicks']))+[spec['targetTicks']]

@contextlib.contextmanager
def lock(path):
 with path.open('a+b') as stream:
  if os.name=='nt':
   import msvcrt
   stream.write(b'0');stream.flush();stream.seek(0);msvcrt.locking(stream.fileno(),msvcrt.LK_NBLCK,1)
  else:
   import fcntl
   fcntl.flock(stream,fcntl.LOCK_EX|fcntl.LOCK_NB)
  yield

def runtime(out):
 target=out/'runtime'
 if not (out/'trajectory.json').exists():
  target.mkdir(exist_ok=True)
  source=ROOT/'src/Research.Runner/bin/Release/net10.0'
  for p in source.iterdir():
   if p.suffix=='.dll' or p.name.endswith(('.deps.json','.runtimeconfig.json')):
    temp=target/(p.name+'.tmp');temp.write_bytes(p.read_bytes());os.replace(temp,target/p.name)
 if not (target/'Research.Runner.dll').is_file():raise ValueError('Frozen runtime missing; do not substitute another binary')
 return target

def initial_fingerprint(plan,engine):
 helpers=['research_lab.py','study_economy_h1.py','run_research_series.py','run_check.py','verify_results.py','verify_public_economy.py','full_economy_metrics.py']
 return dict(helpers={n:digest(ROOT/'scripts'/n) for n in helpers},simulationInputs=simulation_inputs(),binaries={p.name:digest(p) for p in sorted(engine.iterdir()) if p.is_file() and not p.name.endswith('.tmp')},worker=digest(__file__),metrics=digest(ROOT/'scripts/full_economy_metrics.py'),plan=digest(PLAN),effectivePlan=hashlib.sha256(json.dumps(plan,sort_keys=True).encode()).hexdigest())

def run_check(name,budget,out,command):
 return run_checked(name,budget,out/'checks',list(map(str,command)))

def fresh_state(out,case,plan,frozen):
 inputs=out/'inputs';inputs.mkdir(exist_ok=True)
 for source,target in [(ROOT/f'scenarios/vnext/{case["map"]}.json','scenario.json'),(ROOT/plan['profile'],'economy.json'),(ROOT/'config/vnext/balance.b1.json','balance.b1.json')]:
  (inputs/target).write_bytes(source.read_bytes())
 write(inputs/'ordinary.json',dict(npcCount=case['bots'],difficulty=case['difficulty'],seed=case['seed']))
 runner=['dotnet',out/'runtime/Research.Runner.dll']
 source=inputs/'scenario.json';profile=inputs/'economy.json';options=inputs/'ordinary.json'
 if case['phase']=='long':
  code,_=run_check('resolve-horizon',180,out,[*runner,'--scenario',source,'--economy',profile,'--new-game',options,'--script','baseline','--human-ballot','self','--ticks',1,'--output',out/'resolution'])
  if code:raise ValueError('Canonical C# ordinary resolution failed')
  scenario=read_metadata(out/'resolution/scenario.json');original=scenario['maxTicks'];scenario['maxTicks']=plan['long']['targetTicks'];write(inputs/'horizon.json',scenario)
  saved=read_metadata(out/'resolution/controllers.json');settings=[json.loads(c['stateJson'])['settings'] for c in saved if c['kind'].startswith('utility-')]
  if len(settings)!=case['bots'] or not all(c.get('coherentEconomy') for c in settings):raise ValueError('Not actual G1 ordinary policies')
  write(inputs/'controllers.json',dict(version=1,humanActorId=1,finalTick=scenario['maxTicks'],npcs=settings))
 else:
  original=read(profile)['world']['maxTicks']
  rc,_=run_check('validate-ordinary',180,out,[*runner,'--scenario',source,'--economy',profile,'--new-game',options,'--script','baseline','--human-ballot','self','--validate-only','true','--output',out/'validation'])
  if rc:raise ValueError('Canonical C# ordinary validation failed')
 return dict(version=1,sourceCommit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True,timeout=30).strip(),case=case,status='ready',frozen=frozen,originalMaxTicks=original,targetTicks=plan[case['phase']]['targetTicks'],lastSavedTick=0,lastCheckpoint=None,segments=[],inputs={p.name:digest(p) for p in inputs.iterdir() if p.is_file()})

def owned(out,name):
 path=out/name
 if Path(name).is_absolute() or not path.resolve().is_relative_to(out.resolve()) or any(p.is_symlink() for p in [path,*path.parents] if p!=out.parent):raise ValueError('Evidence path escapes owned workspace')
 return path

def valid_checkpoint(out,state):
 if state['lastCheckpoint'] is not None:
  p=owned(out,state['lastCheckpoint'])
  if not p.is_file() or digest(p)!=state['lastCheckpointSha256']:raise ValueError('Committed checkpoint missing or changed')
 for name,h in state['inputs'].items():
  if digest(owned(out,'inputs/'+name))!=h:raise ValueError('Resolved experiment input changed')

def classify_failure(code,stderr):
 if 'CheckpointTooLarge' in stderr:return 'checkpoint_limit'
 if code==124:return 'timeout'
 if code in (-9,137):return 'process_killed_or_oom_unconfirmed'
 return 'runner_failure'

def archive_completed(out,state):
 for record in state['segments']:
  if record.get('status')=='succeeded':compress_turns(out/record['result'])
 # Checkpoints stay byte-exact, and every successful segment remains independently auditable.

def execute(out,case,plan,segment_limit=None,retry=False):
 out.mkdir(parents=True,exist_ok=True);journal=out/'trajectory.json';runner=['dotnet',out/'runtime/Research.Runner.dll']
 with lock(out/'.worker.lock'):
  engine=runtime(out);frozen=initial_fingerprint(plan,engine)
  state=read_metadata(journal) if journal.exists() else fresh_state(out,case,plan,frozen)
  if state['case']!=case or state['frozen']!=frozen:raise ValueError('Case/source/DLL/protocol changed; use a different cohort')
  if state['status']=='running':
   state['segments'].append(dict(state.pop('activeAttempt'),status='interrupted',reason='Host interrupted; only prior checkpoint is committed'))
   state['status']='blocked_interrupted';write(journal,state)
  valid_checkpoint(out,state)
  if state['status'].startswith('blocked') and not retry:return state
  if state['status'].startswith('blocked'):state['status']='ready';write(journal,state)
  if state['status']=='completed':return state
  stop=[False]
  def pause(*_):stop[0]=True
  for sig in (signal.SIGINT,signal.SIGTERM):signal.signal(sig,pause)
  count=0;spec=plan[case['phase']]
  for end in boundaries(plan,case):
   if end<=state['lastSavedTick']:continue
   if stop[0] or segment_limit is not None and count>=segment_limit:break
   attempt=1+sum(r['targetTick']==end for r in state['segments']);tag=f'{end}-a{attempt}'
   run=out/'runs'/tag;save=out/'checkpoints'/(tag+'.save.json');save.parent.mkdir(exist_ok=True)
   if run.exists():raise ValueError('Attempt path already exists; evidence retained')
   command=[*runner,'--ticks',end,'--output',run,'--checkpoint',save,'--checkpoint-at',end,'--progress','true']
   if state['lastCheckpoint']:command+=['--load',out/state['lastCheckpoint']]
   elif case['phase']=='long':command+=['--scenario',out/'inputs/horizon.json','--controllers',out/'inputs/controllers.json','--script','baseline','--human-ballot','self']
   else:command+=['--scenario',out/'inputs/scenario.json','--economy',out/'inputs/economy.json','--new-game',out/'inputs/ordinary.json','--script','baseline','--human-ballot','self']
   record=dict(fromTick=state['lastSavedTick'],targetTick=end,attempt=attempt,result=str(run.relative_to(out)),checkpoint=str(save.relative_to(out)))
   state.update(status='running',activeAttempt=record);write(journal,state)
   measured=['/usr/bin/time','-v',*command] if Path('/usr/bin/time').exists() else command
   code,meta=run_check(tag,spec['timeoutSeconds'],out,measured);record=dict(record,runner=meta)
   if (run/'run-progress.json').exists():record['lastHeartbeat']=read_metadata(run/'run-progress.json')
   if code:
    stderr=(out/'checks'/(tag+'.stderr.log')).read_text(errors='replace');record.update(status=classify_failure(code,stderr),stderrTail=stderr[-6000:]);state['status']='blocked_'+record['status']
   else:
    summary=read_metadata(run/'summary.json')
    if summary['status']!='succeeded' or summary['tick']!=end or not save.is_file():raise ValueError('Runner exit0 without complete checkpoint/export')
    audits={}
    for tool,budget in [('verify_results.py',60),('verify_public_economy.py',30)]:
     rc,m=run_check(tag+'-'+tool.removesuffix('.py'),budget,out,[sys.executable,ROOT/'scripts'/tool,run]);audits[tool]=m
     if rc:
      record.update(status='audit_failure',audits=audits);state['status']='blocked_audit';break
    else:
     metrics=measure(run);write(run/'h1-metrics.json',metrics)
     record.update(status='succeeded',audits=audits,stateHash=summary['stateHash'],metrics='h1-metrics.json',checkpointBytes=save.stat().st_size,checkpointSha256=digest(save))
     state.update(lastSavedTick=end,lastCheckpoint=record['checkpoint'],lastCheckpointSha256=record['checkpointSha256'],status='completed' if end==state['targetTicks'] else 'ready')
   state['segments'].append(record);state.pop('activeAttempt');write(journal,state);archive_completed(out,state);count+=1
   print(json.dumps(dict(case=case['id'],status=state['status'],lastSavedTick=state['lastSavedTick'],target=end)),flush=True)
   if state['status'].startswith('blocked'):break
  write(journal,state);return state

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path);p.add_argument('--case');p.add_argument('--matrix',choices=['ordinary','long','all']);p.add_argument('--segments',type=int);p.add_argument('--retry-failed',action='store_true');p.add_argument('--smoke',action='store_true',help='Separate30/60tick real-engine persistence proof, never a study observation');a=p.parse_args();plan=read(PLAN)
 allcases=cases(plan,'ordinary')+cases(plan,'long')
 if a.matrix:
  selected=allcases if a.matrix=='all' else [c for c in allcases if c['phase']==a.matrix]
  print(json.dumps({'include':selected}));return 0
 if not a.output or not a.case:p.error('--output and --case are required')
 case=next((c for c in allcases if c['id']==a.case),None)
 if case is None:p.error('Unknown preregistered case')
 if a.segments is not None and a.segments<0:p.error('Nonnegative segment limit required')
 if a.smoke:
  if case['phase']!='ordinary':p.error('Smoke is a separate ordinary control')
  case=dict(case,id='smoke-'+case['id']);plan=dict(plan,ordinary=dict(plan['ordinary'],boundaries=[30,60],targetTicks=60))
 state=execute(a.output.resolve(),case,plan,a.segments,a.retry_failed)
 # A measured timeout/save limit is a retained research outcome, not a passing world.
 # Audit failure is an infrastructure/validity failure and keeps the workflow red.
 return 1 if state['status']=='blocked_audit' else 0
if __name__=='__main__':sys.exit(main())
