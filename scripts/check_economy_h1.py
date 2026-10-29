#!/usr/bin/env python3
"""Bounded real-engine H1 setup, observer invariance and F2 accounting checks.
Prepared settlements are separate from ordinary starts. Author: Шелестов Максим Евгеньевич.
"""
import argparse,json,sys
from pathlib import Path
from research_lab import ROOT,Lab,read,write
from study_economy_h1 import executor
from run_research_series import run_checked
from full_economy_metrics import measure

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);p.add_argument('--fixtures',type=Path,required=True);a=p.parse_args();out=a.output.resolve()
 if out.exists() and any(out.iterdir()):raise ValueError('Use a new output directory')
 out.mkdir(parents=True,exist_ok=True);lab=Lab(out/'lab',executor);cases=[];runner=['dotnet',str(ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll')]
 def check(name,cmd,timeout=180):
  rc,meta=run_checked(name,timeout,out/'checks',list(map(str,cmd)))
  if rc:raise ValueError('Failed '+name+'; original checks retained')
  return meta
 for count,map_key,difficulty,ticks in [(3,'small-river','Medium',120),(6,'small-river','Easy',60),(10,'large-districts','Hard',60)]:
  key=lab.enqueue(dict(version=1,id='h1-ordinary-'+str(count),scenario='scenarios/vnext/'+map_key+'.json',economy='config/vnext/economy.g1-npc.json',ordinaryNpcCount=count,ticks=ticks,seeds=[42],repetitions=1,mode='algorithmic',recordWorld=False,timeoutSeconds=180,factors=[dict(path='npc.difficulty',values=[difficulty])]))
  lab.start();thread=lab.thread
  if thread is not None:thread.join()
  series=next(s for s in lab.series if s['id']==key);job=series['jobs'][0]
  if job['status']!='succeeded':raise ValueError('Ordinary setup failed: '+str(job.get('error')))
  folder=out/'lab'/job['result'];controls=read(folder/'controllers.json');settings=[json.loads(c['stateJson'])['settings'] for c in controls if c['kind'].startswith('utility-')]
  assert len(settings)==count and all(c['coherentEconomy'] for c in settings)
  assert read(folder/'run-progress.json')['status']=='completed'
  assert job['metrics']['fullEconomy']['cashLedgerReconciled'] and not job['metrics']['fullEconomy']['unclassifiedActorCashMinor']
  cases.append(dict(ordinary=True,map=map_key,bots=count,difficulty=difficulty,ticks=ticks,stateHash=job['metrics']['stateHash'],modernControllers=len(settings)))
  if count==3:
   # The laboratory's progress-enabled run must equal the same session without progress,
   # and a real checkpoint resume. No extra inspection commands enter the simulation.
   cmd=list(job['command']);cmd[cmd.index('--output')+1]=str(out/'progress-off');i=cmd.index('--progress');del cmd[i:i+2]
   save=out/'ordinary.save.json';check('progress-off',cmd+['--checkpoint',save,'--checkpoint-at','60'])
   loaded=out/'loaded';check('loaded',[*runner,'--load',save,'--ticks',ticks,'--output',loaded,'--progress','true'])
   check('observer-and-continuation-exact',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',folder,out/'progress-off',folder,loaded],60)
   check('ordinary-accounting',[sys.executable,ROOT/'scripts/verify_results.py',folder,out/'progress-off',loaded],60)
   for other in [out/'progress-off',loaded]:assert (folder/'personal-accounts.json').read_bytes()==(other/'personal-accounts.json').read_bytes()
 for name,wages,road,net,debt in [('completed',0,7,4018,10),('numeric',3120,11,894,6)]:
  folder=out/('prepared-'+name);source=a.fixtures.resolve();tick=int((source/(name+'.tick')).read_text())
  check('prepared-'+name,[*runner,'--scenario',source/(name+'.json'),'--replay',source/(name+'.commands.jsonl'),'--script','baseline','--ticks',tick,'--output',folder])
  check('prepared-'+name+'-accounting',[sys.executable,ROOT/'scripts/verify_results.py',folder],60)
  full=measure(folder)['fullEconomy'];carrier=full['actors']['2'];customer=full['actors']['1']
  assert full['servicePaidMinor']==4025 and carrier['serviceRevenueMinor']==4025
  assert carrier.get('wagesMinor',0)==wages and carrier['roadPaidMinor']==road and carrier['operatingReceiptResultMinor']==net
  assert carrier['debtMinor']==0 and carrier['unbilledAccruedMinor']==debt and carrier['operatingAfterRoadDebtMinor']==net-debt
  assert customer['serviceCostMinor']==4025 and customer.get('salesRevenueMinor',0)==0
  assert carrier['cashCapitalSpendingMinor']==250000 and not full['unclassifiedActorCashMinor']
  cases.append(dict(prepared=True,name=name,servicePaidMinor=4025,carrier=carrier,customer=customer,roi=full['roi']))
 write(out/'gate.json',dict(status='succeeded',cases=cases,scope='Actual ordinary sessions and exact progress/load exports; two prepared service receipts are not a natural ROI study.'))
 print('H1 actual ordinary setup, exact observer/load and prepared settlement accounting passed');return 0
if __name__=='__main__':sys.exit(main())
