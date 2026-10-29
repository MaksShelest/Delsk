#!/usr/bin/env python3
"""Compact publishable H1 evidence; full raw exports stay in the study workspace.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,base64,collections,gzip,hashlib,io,json,shutil
from pathlib import Path
from research_lab import ROOT,read,write
from full_economy_metrics import measure

def checkpoint_bytes(path):
 envelope=read(path);size=0
 if envelope['formatVersion']!=2:return dict(fileBytes=path.stat().st_size,decodedBytes=None)
 with gzip.GzipFile(fileobj=io.BytesIO(base64.b64decode(envelope['payloadJson']))) as stream:
  for block in iter(lambda:stream.read(1024*1024),b''):size+=len(block)
 return dict(fileBytes=path.stat().st_size,decodedBytes=size)

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--workspace',type=Path,required=True);p.add_argument('--analysis',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();root=a.workspace.resolve();out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
 for name in ['observations.json','study-summary.json','source-artifacts.json','analysis-inputs.json','capital-distribution.svg']:
  src=a.analysis/name
  if src.exists():shutil.copyfile(src,out/name)
 for name in ['study-inputs.json','study-protocol.json']:
  shutil.copyfile(root/'study'/name,out/name)
 trajectories={}
 for name in ['long-small','long-large']:
  path=root/name/'trajectory.json'
  if path.exists():
   value=read(path);reanalysis=[]
   for record in value['segments']:
    if 'stateHash' not in record:continue
    folder=Path(record['result']);checkpoint=Path(record['checkpoint'])
    reanalysis.append(dict(tick=record['targetTick'],metrics=measure(folder),checkpoint=checkpoint_bytes(checkpoint)))
   value['finalAnalysis']=reanalysis;value['analysisSha256']=hashlib.sha256((ROOT/'scripts/full_economy_metrics.py').read_bytes()).hexdigest();trajectories[name]=value
 write(out/'long-trajectories.json',trajectories)
 # Retain every bounded process result, with its original stdout/stderr on nonzero exit.
 # Do not reinterpret a failed attempt as a pass or omit earlier setup failures.
 processes=[]
 for path in sorted(root.rglob('*.json')):
  if path.parent.name!='checks':continue
  try:value=read(path)
  except (ValueError,OSError):continue
  if not isinstance(value,dict) or 'exit_code' not in value or 'command' not in value:continue
  record=dict(file=str(path.relative_to(root)),**value)
  if value['exit_code']:
   for suffix in ['stdout.log','stderr.log']:
    log=path.with_suffix('.'+suffix)
    if log.exists():record[suffix]=log.read_text(encoding='utf-8',errors='replace')
  processes.append(record)
 write(out/'process-checks.json',dict(version=1,processCount=len(processes),exitCodes=dict(collections.Counter(p['exit_code'] for p in processes)),processes=processes))
 # All monetary inputs protected by G2, including resolved old profiles, remain byte checked.
 financial=read(ROOT/'docs/verification/city-g2/financial-source-comparison.json')['files'];changed=[]
 for f,h in financial.items():
  if hashlib.sha256((ROOT/f).read_bytes()).hexdigest()!=h:changed.append(f)
 write(out/'financial-source-comparison.json',dict(baseG2='cf0784be360024b2b6874753a8d231da8b4a8f59',unchangedCount=len(financial)-len(changed),changed=changed,files=financial))
 if (root/'runtime-gate1/gate.json').exists():shutil.copyfile(root/'runtime-gate1/gate.json',out/'runtime-gate.json')
 print(json.dumps(dict(processes=len(processes),nonzero=sum(p['exit_code']!=0 for p in processes),changedFinancial=changed)))
if __name__=='__main__':main()
