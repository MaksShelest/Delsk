#!/usr/bin/env python3
"""H1 zero-cash diagnostic via actual ordinary resolver, separate prepared conditions.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,json,sys
from pathlib import Path
from research_lab import ROOT,Lab,read,write
from study_economy_h1 import freeze,executor,compress_turns

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=True);freeze(out)
 spec=read(ROOT/'docs/research/h1/zero-cash-diagnostic.json');lab=Lab(out/'lab',executor)
 for map_key in spec['maps']:
  name='h1-zero-cash-'+map_key
  if any(s['plan']['id']==name for s in lab.series):continue
  lab.enqueue(dict(version=1,id=name,scenario='scenarios/vnext/'+map_key+'.json',economy='config/vnext/economy.g1-npc.json',ordinaryNpcCount=spec['bots'],ticks=spec['ticks'],seeds=[spec['seed']],repetitions=1,mode='algorithmic',recordWorld=False,timeoutSeconds=spec['timeoutSeconds'],factors=[dict(path='npc.difficulty',values=[spec['difficulty']]),dict(path='world.startingCashMinor',values=[0])]))
 lab.start();thread=lab.thread
 if thread is not None:thread.join()
 rows=[]
 for series in lab.series:
  for job in series['jobs']:
   row=dict(map=series['plan']['scenario'],status=job['status'],prepared=True,result=job.get('result'),check=job.get('check'),error=job.get('error'))
   if job['status']=='succeeded':
    folder=out/'lab'/job['result'];scenario=read(folder/'scenario.json');assert all(p['cashMinor']==0 for p in scenario['participants'])
    row['metrics']=job['metrics'];row['npcPositiveFinalCash']=sum(v.get('cashMinor',0)>0 for k,v in job['metrics']['fullEconomy']['actors'].items() if k!='1');compress_turns(folder)
   rows.append(row)
 write(out/'diagnostic.json',dict(version=1,prepared=True,protocol=spec,rows=rows));print(json.dumps(dict(completed=sum(r['status']=='succeeded' for r in rows),total=len(rows),npcPositiveCash=[r.get('npcPositiveFinalCash') for r in rows])))
 return int(any(r['status']!='succeeded' for r in rows))
if __name__=='__main__':sys.exit(main())
