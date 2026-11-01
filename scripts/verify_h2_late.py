#!/usr/bin/env python3
"""Resumable late-world H2 continuation/export/replay and independent audits, old movement only.
Author: Шелестов Максим Евгеньевич. Each game process keeps the existing900s bound.
"""
import argparse,atexit,gzip,hashlib,json,os,shutil,subprocess,sys
from pathlib import Path
from verify_postg2_continuation import compare
ROOT=Path(__file__).resolve().parents[1]
def sha(p):
 h=hashlib.sha256()
 with p.open('rb') as f:
  for block in iter(lambda:f.read(1024*1024),b''):h.update(block)
 return h.hexdigest()
def write(path,data):
 temp=path.with_suffix('.tmp');temp.write_text(json.dumps(data,indent=2)+'\n');temp.replace(path)
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--checkpoint',type=Path,required=True);p.add_argument('--runtime',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--extension',type=int,default=1800);p.add_argument('--resume-ticks',type=int,default=30);a=p.parse_args()
 if not 1<=a.extension<=1800 or not 1<=a.resume_ticks<=100:p.error('Do not raise the declared1800/100-tick experiment bounds')
 out=a.output.resolve();out.mkdir(parents=True,exist_ok=True);lock=out/'writer.lock'
 if lock.exists():
  try:os.kill(int(lock.read_text()),0)
  except ProcessLookupError:lock.unlink()
  else:raise ValueError('Research writer is still active')
 with lock.open('x') as f:f.write(str(os.getpid()))
 atexit.register(lambda:lock.unlink(missing_ok=True))
 runtime=a.runtime.resolve();frozen=dict(checkpointSha256=sha(a.checkpoint),runtime={x.name:sha(x) for x in sorted(runtime.iterdir()) if x.suffix in ('.dll','.json')},helpers={x.name:sha(x) for x in sorted((ROOT/'scripts').glob('*.py'))},extension=a.extension,resumeTicks=a.resume_ticks)
 journal=out/'journal.json'
 if journal.exists():
  state=json.loads(journal.read_text());assert state['frozen']==frozen,'Inputs/binary/protocol changed; use a new cohort/output'
 else:
  shutil.copytree(runtime,out/'runtime');(out/'helpers').mkdir()
  for name in frozen['helpers']:shutil.copyfile(ROOT/'scripts'/name,out/'helpers'/name)
  state=dict(version=2,frozen=frozen,stages={},scope='H2 engineering continuation; original H1 resolved rules and policies; not new economic replicates');write(journal,state)
 assert {name:sha(out/'runtime'/name) for name in frozen['runtime']}==frozen['runtime'],'Retained H2 runtime changed'
 runner=['dotnet',str(out/'runtime/Research.Runner.dll')]
 def run(name,timeout,cmd):
  previous=state['stages'].get(name);metadata=out/'checks'/(name+'.json')
  if previous and previous.get('status')=='succeeded':return
  if metadata.exists():
   old=json.loads(metadata.read_text())
   if old.get('exit_code')==0:state['stages'][name]=dict(status='succeeded',check=old);write(journal,state);return
   raise ValueError('Retain failed attempt '+name+'; inspect it before starting a distinct output')
  if previous and previous.get('processId'):
   try:os.kill(previous['processId'],0)
   except ProcessLookupError:pass
   else:raise ValueError('Previous bounded stage is still running: '+name)
  if (out/name).is_dir():
   attempt=1
   while (out/(name+'.interrupted-'+str(attempt))).exists():attempt+=1
   (out/name).rename(out/(name+'.interrupted-'+str(attempt)))
   for log in (out/'checks').glob(name+'.*.log'):log.rename(log.with_name(log.name+'.interrupted-'+str(attempt)))
  state['stages'][name]=dict(status='running');write(journal,state)
  process=subprocess.Popen([sys.executable,str(ROOT/'scripts/run_check.py'),'--name',name,'--timeout',str(timeout),'--log-directory',str(out/'checks'),'--',*map(str,cmd)],cwd=ROOT)
  state['stages'][name]['processId']=process.pid;write(journal,state);rc=process.wait()
  state['stages'][name]=dict(status='succeeded' if rc==0 else 'failed',check=json.loads(metadata.read_text()));write(journal,state)
  if rc:raise ValueError('Bounded stage failed: '+name)
 # Initial tick is read by authoritative Core, not a Python save interpreter.
 run('identify',600,[*runner,'--measure-session',a.checkpoint.resolve(),'--operation','step','--output',out/'identify'])
 initial=json.loads((out/'identify/phases.json').read_text());assert initial['movementProfile']=='legacy-divisor-80';start=initial['tick'];end=start+a.extension;final=end+a.resume_ticks
 run('segment',900,[*runner,'--measure-session',a.checkpoint.resolve(),'--ticks',end,'--operation','save','--output',out/'segment'])
 run('resumed',900,[*runner,'--load',out/'segment/world.save.json','--ticks',final,'--output',out/'resumed','--progress','true'])
 run('continuous',900,[*runner,'--measure-session',a.checkpoint.resolve(),'--ticks',final,'--operation','export','--output',out/'continuous'])
 exact=compare(out/'continuous/exports',out/'resumed');write(out/'continuation-exact.json',exact)
 run('replay',900,[*runner,'--scenario',out/'resumed/scenario.json','--replay',out/'resumed/commands.jsonl','--ticks',final,'--output',out/'replay','--progress','true'])
 run('replay-exact',60,[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py','--world-only',out/'resumed',out/'replay'])
 for label,folder in [('resumed',out/'resumed'),('continuous',out/'continuous/exports'),('replay',out/'replay')]:
  run(label+'-accounting',60,[sys.executable,ROOT/'scripts/verify_results.py',folder])
  run(label+'-public',30,[sys.executable,ROOT/'scripts/verify_public_economy.py',folder])
  if label!='replay':run(label+'-history',120,[sys.executable,ROOT/'scripts/verify_h2_history.py',folder,'--output',out/(label+'-history.json')])
 archives=json.loads((out/'trace-archives.json').read_text()) if (out/'trace-archives.json').exists() else {}
 for folder in [out/'resumed',out/'continuous/exports',out/'replay']:
  path=folder/'decisions.jsonl';packed=Path(str(path)+'.gz')
  if path.exists():
   expected=sha(path)
   with path.open('rb') as source,gzip.open(packed,'wb',compresslevel=1) as target:shutil.copyfileobj(source,target)
   h=hashlib.sha256()
   with gzip.open(packed,'rb') as f:
    for block in iter(lambda:f.read(1024*1024),b''):h.update(block)
   assert h.hexdigest()==expected;archives[str(packed.relative_to(out))]=dict(uncompressedBytes=path.stat().st_size,uncompressedSha256=expected,gzipBytes=packed.stat().st_size,gzipSha256=sha(packed));path.unlink()
 write(out/'trace-archives.json',archives)
 final_state=json.loads((out/'resumed/summary.json').read_text());result=dict(status='succeeded',initialTick=start,checkpointTick=end,finalTick=final,stateHash=final_state['stateHash'],controllerStateHash=final_state['controllerStateHash'],exactExports=len(exact['files']),replayWorldExact=True,independentAccountingAudits=3,independentPublicAudits=3,independentFullHistoryAudits=2,limitsUnchanged=True,originalMovementRetained=True)
 write(out/'verification.json',result)
 evidence=dict(verification=result,phases={name:json.loads((out/name/'phases.json').read_text()) for name in ['identify','segment','continuous']},traceArchives=archives)
 write(out/'evidence-summary.json',evidence);print(json.dumps(evidence));return 0
if __name__=='__main__':sys.exit(main())
