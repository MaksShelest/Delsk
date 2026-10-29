#!/usr/bin/env python3
"""Versioned analysis of existing H1 exports. Never rewrites the queue or original metrics.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,hashlib,json,sys
from pathlib import Path
from research_lab import ROOT,read,write
from full_economy_metrics import measure
from analyze_economy_h1 import summarize,plots

def digest(path):
 h=hashlib.sha256()
 with Path(path).open('rb') as stream:
  for block in iter(lambda:stream.read(1024*1024),b''):h.update(block)
 return h.hexdigest()

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--study',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--plots',action='store_true');a=p.parse_args()
 if a.output.exists():raise ValueError('Analysis directory already exists; preserve it and choose a new version')
 q=a.study/'lab/queue.json';snapshot=q.read_bytes();queue=json.loads(snapshot);rows=[];artifacts={}
 for series in queue:
  for job in series['jobs']:
   row=dict(series=series['plan']['id'],job=job['id'],map=series['plan']['scenario'],bots=series['plan'].get('ordinaryNpcCount'),difficulty=job['factors'].get('npc.difficulty'),seed=job['seed'],ticks=job['ticks'],status=job['status'],attempt=job['attempt'],result=job.get('result'),error=job.get('error'),runnerCheck=job.get('check'))
   if job['status']=='succeeded':
    folder=a.study/'lab'/job['result'];m=dict(job['metrics']);m.update(measure(folder));row['metrics']=m
    # Byte-level source proof for all original exports, including losslessly archived turns.
    artifacts[job['result']]={f.name:dict(bytes=f.stat().st_size,sha256=digest(f)) for f in sorted(folder.iterdir()) if f.is_file()}
   rows.append(row)
 if q.read_bytes()!=snapshot:raise ValueError('Queue changed during analysis; analyze a stable completed or paused queue')
 a.output.mkdir(parents=True)
 write(a.output/'observations.json',dict(version=1,rows=rows,unitOfIndependence='world/seed'))
 write(a.output/'study-summary.json',summarize(rows));write(a.output/'source-artifacts.json',artifacts)
 scripts=['scripts/reanalyze_economy_h1.py','scripts/full_economy_metrics.py','scripts/analyze_economy_h1.py','scripts/analyze_paired_series.py','docs/research/h1/study-plan.json','docs/research/h1/analysis-plan.json']
 write(a.output/'analysis-inputs.json',dict(version=1,queueSha256=hashlib.sha256(snapshot).hexdigest(),queueBytes=len(snapshot),analysisInputs={f:digest(ROOT/f) for f in scripts},simulationInputs=read(a.study/'study-inputs.json'),registeredProtocol=read(a.study/'study-protocol.json')))
 if a.plots:plots(rows,a.output)
 print(json.dumps(dict(rows=len(rows),completed=sum(r['status']=='succeeded' for r in rows),failures=sum(r['status'] in ('failed','interrupted') for r in rows))))
 return 0
if __name__=='__main__':sys.exit(main())
