#!/usr/bin/env python3
"""Resumable, serial H2 old-rule comparisons. No H1 economic replicates are added.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,atexit,hashlib,json,os,platform,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def digest(p):
 h=hashlib.sha256()
 with p.open('rb') as f:
  for block in iter(lambda:f.read(1024*1024),b''):h.update(block)
 return h.hexdigest()
def runtime(p):return {f.name:digest(f) for f in sorted(p.iterdir()) if f.suffix in ('.dll','.json')}
def write(p,data):
 temp=p.with_suffix('.tmp');temp.write_text(json.dumps(data,indent=2)+'\n');temp.replace(p)
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--h1-root',type=Path,required=True);p.add_argument('--before-runtime',type=Path,required=True);p.add_argument('--after-runtime',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--replicates',type=int,default=3);p.add_argument('--late',action='store_true');p.add_argument('--retry-failed',action='store_true');a=p.parse_args()
 if not 1<=a.replicates<=10:p.error('Use 1..10 timing replicates, not economic replicates')
 out=a.output.resolve();out.mkdir(parents=True,exist_ok=True);lock=out/'writer.lock'
 if lock.exists():
  owner=int(lock.read_text());
  try:os.kill(owner,0)
  except ProcessLookupError:lock.unlink()
  else:raise ValueError('Research writer is still active: '+str(owner))
 with lock.open('x') as f:f.write(str(os.getpid()))
 atexit.register(lambda:lock.unlink(missing_ok=True))
 journal=out/'journal.json';tasks=[];inputs={}
 cases=sorted(a.h1_root.resolve().glob('long-*'))
 for case in cases:
  saves=list((case/'world/checkpoints').glob('*.save.json'))
  if not saves:continue
  save=max(saves,key=lambda f:int(f.name.split('-')[0]));start=int(save.name.split('-')[0]);inputs[case.name]=digest(save)
  if case.name.endswith('-6-Medium-42'):
   for n in range(a.replicates):
    for label in ('before','after'):tasks.append(dict(name=f'{case.name}-{label}-{n+1}',case=case.name,start=start,end=start+100,runtime=label,timeout=600,operation='save'))
  if a.late:tasks.append(dict(name=case.name+'-late',case=case.name,start=start,end=start+1800,runtime='after',timeout=900,operation='save'))
 if not tasks:raise ValueError('No retained H1 checkpoints')
 frozen=dict(inputs=inputs,before=runtime(a.before_runtime),after=runtime(a.after_runtime),tasks=tasks)
 if journal.exists():
  state=json.loads(journal.read_text());assert state['frozen']==frozen,'Inputs, runtime or protocol changed: use a distinct cohort/output'
 else:
  state=dict(version=1,scope='H2 old movement rules, same saved controllers, all history; timing repeats are not new worlds',host=dict(platform=platform.platform(),cpuCount=os.cpu_count(),python=sys.version),frozen=frozen,attempts=[]);write(journal,state)
 for task in tasks:
  attempts=[r for r in state['attempts'] if r['task']==task['name']]
  if any(r['status']=='succeeded' for r in attempts):continue
  if attempts and attempts[-1]['status']=='failed' and not a.retry_failed:continue
  if attempts and attempts[-1]['status']=='running':
   last=attempts[-1];phases=out/last['directory']/'phases.json'
   if phases.exists() and json.loads(phases.read_text())['stage']=='succeeded':last['status']='succeeded';write(journal,state);continue
   # Preserve unfinished work and diagnostics in its own attempt, never overwrite it.
   last['status']='interrupted';write(journal,state)
  directory=task['name']+'-a'+str(len(attempts)+1);row=dict(task=task['name'],directory=directory,status='running');state['attempts'].append(row);write(journal,state)
  case=a.h1_root.resolve()/task['case'];save=next((case/'world/checkpoints').glob(str(task['start'])+'-*.save.json'));binary=(a.before_runtime if task['runtime']=='before' else a.after_runtime).resolve()/'Research.Runner.dll'
  cmd=['dotnet',str(binary),'--measure-session',str(save),'--ticks',str(task['end']),'--operation',task['operation'],'--output',str(out/directory)]
  rc=subprocess.run([sys.executable,str(ROOT/'scripts/run_check.py'),'--name',directory,'--timeout',str(task['timeout']),'--log-directory',str(out/'checks'),'--',*cmd],cwd=ROOT).returncode
  row.update(status='succeeded' if rc==0 else 'failed',exitCode=rc);phases=out/directory/'phases.json'
  if phases.exists():row['measurement']=json.loads(phases.read_text())
  write(journal,state)
 return int(any(r['status']!='succeeded' for r in state['attempts']))
if __name__=='__main__':sys.exit(main())
