#!/usr/bin/env python3
"""Read/verify completed lab exports and publish compact paired evidence; no simulation.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,json,hashlib,sys
from pathlib import Path
from research_lab import read,write,digest,ROOT
from run_research_series import run_checked
p=argparse.ArgumentParser();p.add_argument('--lab',type=Path,action='append',required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
a.output.mkdir(parents=True,exist_ok=True);report=[]
for workspace in a.lab:
 for series in read(workspace/'queue.json'):
  folder=workspace/series['id'];rows=[]
  for j in series['jobs']:
   if j['status']!='succeeded':raise SystemExit('Incomplete cell: '+j['id'])
   run=workspace/j['result']
   for tool in ['verify_results.py','verify_public_economy.py']:
    name=workspace.name+'-'+j['id']+'-'+tool.split('.')[0]
    code,_=run_checked(name,30,a.output/'checks',[sys.executable,str(ROOT/'scripts'/tool),str(run)])
    if code:raise SystemExit('Independent check failed: '+name)
   for path,h in series['inputs'].items():
    if digest(folder/path)!=h:raise SystemExit('Input snapshot changed')
   rows.append(dict(id=j['id'],factors=j['factors'],seed=j['seed'],repeat=j['repeat'],metrics=j['metrics'],
    command=j['command'],exportHashes={q.name:digest(q) for q in run.iterdir() if q.suffix in ('.json','.jsonl','.csv')}))
  report.append(dict(id=series['id'],plan=series['plan'],inputs=series['inputs'],commit=series.get('commit'),
                     rows=rows,aggregate=read(folder/'summary.json')))
write(a.output/'research-study.json',dict(status='succeeded',series=report,
 scope='Bounded cloud Linux pilot; actual runner and independent flow/capital checks. No real model/target hardware, no universal strategy or profitability claim.'))
print('Verified study:',a.output/'research-study.json')
