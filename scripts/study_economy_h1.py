#!/usr/bin/env python3
"""H1 reproducible ordinary-game matrix using the existing persistent laboratory queue.
Author: Шелестов Максим Евгеньевич. No financial rules are implemented here.
"""
import argparse,hashlib,json,os,signal,sys,time
from pathlib import Path
from research_lab import Lab,write,read,ROOT
from research_workspace import binary_hashes
from run_research_series import run_checked

def simulation_inputs():
 import subprocess
 names=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z'],cwd=ROOT,text=True).split('\0')
 return {p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in names if p and ((p.startswith(('src/','config/','scenarios/'))) or p in ('Directory.Build.props','Directory.Build.targets','global.json','docs/vnext/data/BALANCE_VNEXT.json'))}

def executor(name,timeout,logs,cmd):
 measured=Path('/usr/bin/time').is_file() and '--validate-only' not in cmd
 actual=['/usr/bin/time','-v',*map(str,cmd)] if measured else cmd
 code,meta=run_checked(name,timeout,logs,actual)
 if code or '--validate-only' in cmd:return code,meta
 out=Path(cmd[cmd.index('--output')+1])
 for kind,tool in [('accounting','verify_results.py'),('privacy','verify_public_economy.py')]:
  result,audit=run_checked(name+'-'+kind,60,logs,[sys.executable,str(ROOT/'scripts'/tool),str(out)])
  if result:raise ValueError(kind+' audit failed after runner exit0; attempt retained')
 return code,meta

def freeze(out):
 data=dict(version=1,baseG2='cf0784be360024b2b6874753a8d231da8b4a8f59',simulationInputs=simulation_inputs(),binaries=binary_hashes())
 path=out/'study-inputs.json'
 if path.exists():
  old=read(path)
  if old['simulationInputs']!=data['simulationInputs'] or old['binaries']!=data['binaries']:raise ValueError('Simulation inputs/binaries changed; use a separate study workspace')
 else:write(path,data)
 return data

def initialize(lab,phase,plan):
 spec=plan['pilot' if phase=='pilot' else 'pairedExtension']
 for map_key in plan['maps']:
  for bots in spec['bots']:
   name='h1-'+phase+'-'+map_key+'-'+str(bots)
   if any(s['plan']['id']==name for s in lab.series):continue
   p=dict(version=1,id=name,scenario='scenarios/vnext/'+map_key+'.json',economy=plan['profile'],ordinaryNpcCount=bots,
          ticks=spec['ticks'],seeds=spec['seeds'],repetitions=1,mode='algorithmic',recordWorld=False,
          timeoutSeconds=spec['timeoutSeconds'],factors=[dict(path='npc.difficulty',values=spec['difficulties'])])
   lab.enqueue(p)

def compress_turns(out):
 # Preserve exact original bytes; this opt-in study storage is not the default lab viewer format.
 import gzip,os
 path=out/'decisions.jsonl'
 if not path.exists():return
 digest=hashlib.sha256();size=0
 with path.open('rb') as src,gzip.open(str(path)+'.gz.tmp','wb',compresslevel=6) as dest:
  for block in iter(lambda:src.read(1024*1024),b''):digest.update(block);size+=len(block);dest.write(block)
 verify=hashlib.sha256()
 with gzip.open(str(path)+'.gz.tmp','rb') as src:
  for block in iter(lambda:src.read(1024*1024),b''):verify.update(block)
 if verify.digest()!=digest.digest():raise ValueError('Turn archive byte mismatch')
 os.replace(str(path)+'.gz.tmp',str(path)+'.gz')
 write(out/'decisions-storage.json',dict(version=1,format='gzip',uncompressedBytes=size,sha256=digest.hexdigest(),compressedBytes=Path(str(path)+'.gz').stat().st_size))
 path.unlink()

def summarize(lab,out):
 rows=[]
 for s in lab.series:
  for j in s['jobs']:
   row=dict(series=s['plan']['id'],job=j['id'],map=s['plan']['scenario'],bots=s['plan'].get('ordinaryNpcCount'),difficulty=j['factors'].get('npc.difficulty'),seed=j['seed'],ticks=j['ticks'],status=j['status'],attempt=j['attempt'],result=j.get('result'),error=j.get('error'),runnerCheck=j.get('check'))
   if j.get('metrics'):row['metrics']=j['metrics']
   rows.append(row)
 write(out/'observations.json',dict(version=1,rows=rows,unitOfIndependence='world/seed; not individual NPC'))
 return rows

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);p.add_argument('--phase',choices=['pilot','pairs','summary'],default='pilot');p.add_argument('--init-only',action='store_true');p.add_argument('--retry-failed',action='store_true');p.add_argument('--compress-turns',action='store_true');a=p.parse_args()
 out=a.output.resolve();out.mkdir(parents=True,exist_ok=True);freeze(out)
 protocol=out/'study-protocol.json'
 if not protocol.exists():
  inputs=['scripts/research_lab.py','scripts/research_workspace.py','docs/research/h1/study-plan.json']
  write(protocol,dict(inputs={f:hashlib.sha256((ROOT/f).read_bytes()).hexdigest() for f in inputs},registeredPlan=read(ROOT/'docs/research/h1/study-plan.json')))
 if protocol.exists():
  registered=read(protocol)
  if any(hashlib.sha256((ROOT/f).read_bytes()).hexdigest()!=h for f,h in registered['inputs'].items()):raise ValueError('Registered setup/protocol changed; use a separate study')
 lab=Lab(out/'lab',executor);plan=read(ROOT/'docs/research/h1/study-plan.json')
 if a.phase!='summary':initialize(lab,a.phase,plan)
 if a.init_only or a.phase=='summary':summarize(lab,out);return 0
 # Measurement completes before compression. Its time is outside the runner timing.
 if a.compress_turns:
  import research_lab
  ordinary_measure=research_lab.measure
  def compressed_measure(folder):
   result=ordinary_measure(folder);compress_turns(Path(folder));return result
  research_lab.measure=compressed_measure
 def stop(*_):lab.pause()
 signal.signal(signal.SIGINT,stop);signal.signal(signal.SIGTERM,stop)
 lab.start(retry=a.retry_failed);last=-1
 while lab.thread is not None:
  thread=lab.thread
  if thread is not None:thread.join(1)
  done=sum(j['status'] in ('succeeded','failed','cancelled','interrupted') for s in lab.series for j in s['jobs'])
  if done!=last:
   print(json.dumps(dict(completed=done,total=sum(len(s['jobs']) for s in lab.series),failed=sum(j['status']=='failed' for s in lab.series for j in s['jobs'])),ensure_ascii=False),flush=True);last=done;summarize(lab,out)
 summarize(lab,out)
 return int(any(j['status']=='failed' for s in lab.series for j in s['jobs']))
if __name__=='__main__':sys.exit(main())
