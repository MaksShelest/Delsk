#!/usr/bin/env python3
"""Preregistered post-G2 world-level analysis. Failures and censoring are data, not zeros.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,collections,gzip,json
from pathlib import Path
from research_lab import read,read_metadata,write
from study_postg2_h1 import PLAN,cases
from analyze_paired_series import spread
from analyze_economy_h1 import paired_stats

def observe(folder):
 journal=read_metadata(folder/'trajectory.json');case=journal['case'];segments=[]
 for segment in journal['segments']:
  row=dict(segment)
  if segment.get('status')=='succeeded':row['metrics']=read_metadata(folder/segment['result']/segment['metrics'])
  segments.append(row)
 return dict(case=case,status=journal['status'],lastSavedTick=journal['lastSavedTick'],sourceCommit=journal['sourceCommit'],frozen=journal['frozen'],segments=segments)

def latest(row):
 good=[s for s in row['segments'] if s.get('status')=='succeeded']
 return good[-1] if good else None

def summarize(rows,plan):
 expected=cases(plan,'ordinary')+cases(plan,'long');expected_ids={c['id'] for c in expected};byid={}
 for row in rows:
  key=row['case']['id']
  if key not in expected_ids or key in byid:raise ValueError('Unknown/duplicate world: '+key)
  byid[key]=row
 # Matrix workers must share source/protocol, not merely a common display label.
 fingerprints={json.dumps(r['frozen'],sort_keys=True) for r in rows}
 if len(fingerprints)>1:raise ValueError('Mixed source/DLL/protocol cohorts')
 groups=[];pairs=[]
 for map_key in plan['maps']:
  for bots in plan['ordinary']['bots']:
   seeds=plan['ordinary']['seedsByBots'][str(bots)]
   for difficulty in plan['ordinary']['difficulties']:
    selected=[byid.get(f'ordinary-{map_key}-{bots}-{difficulty}-{seed}') for seed in seeds]
    complete=[r for r in selected if r and r['status']=='completed' and r['lastSavedTick']==plan['ordinary']['targetTicks']]
    full=[latest(r)['metrics']['fullEconomy'] for r in complete]
    distributions={key:spread([latest(r)['metrics'][key] for r in complete if latest(r)['metrics'].get(key) is not None]) for key in ['npcMedianCapitalChangeMinor','npcOperatingReceiptResultMinor','npcOperatingAfterRoadDebtMinor','employment','hostedMillisecondsPerActiveTick']}
    milestones=[m for f in full for actor,m in f['milestones'].items() if actor!='1'];xp=collections.Counter()
    for f in full:
     for actor_industry,grants in f['experience'].items():
      if not actor_industry.startswith('1:'):xp.update(grants)
    groups.append(dict(map=map_key,bots=bots,difficulty=difficulty,plannedWorlds=len(seeds),completeWorlds=len(complete),missingWorlds=sum(r is None for r in selected),states=dict(collections.Counter(r['status'] if r else 'missing' for r in selected)),distributions=distributions,
      firstFactoriesObserved=sum(m['firstProductionTick'] is not None for m in milestones),npcMilestones=len(milestones),censoredNpcMilestones=sum(m['firstProductionTick'] is None for m in milestones),
      worldsWithFactory=sum(any(m['firstProductionTick'] is not None for actor,m in f['milestones'].items() if actor!='1') for f in full),
      levelsObserved={str(level):sum(m['physicalLevels'][str(level)] is not None for m in milestones) for level in range(1,6)},experience=dict(xp),questXpShare=xp.get('Quest',0)/sum(xp.values()) if sum(xp.values()) else None,
      batches=sum(f['events'].get('ProductionCompleted',0) for f in full),vehicles=sum(f['vehicleCount'] for f in full),privateRoadStages=sum(f['completedRoadStages'] for f in full),serviceOrders=sum(f['serviceOrders'] for f in full),
      independentUnit='World/seed; NPC milestone rows are correlated',scope='Complete ordinary5000 endpoints only; partial/censored trajectories retained separately'))
   for left in ['Hard','Easy']:
    differences=[];paired=[];incomplete=[]
    for seed in seeds:
     a=byid.get(f'ordinary-{map_key}-{bots}-{left}-{seed}');b=byid.get(f'ordinary-{map_key}-{bots}-Medium-{seed}')
     if not a or not b or any(r['status']!='completed' or r['lastSavedTick']!=plan['ordinary']['targetTicks'] for r in (a,b)):
      incomplete.append(seed);continue
     x=latest(a)['metrics'].get('npcMedianCapitalChangeMinor');y=latest(b)['metrics'].get('npcMedianCapitalChangeMinor')
     if x is None or y is None:incomplete.append(seed);continue
     paired.append(seed);differences.append(x-y)
    pairs.append(dict(map=map_key,bots=bots,left=left,right='Medium',plannedPairs=len(seeds),completePairs=len(paired),seeds=paired,incompleteSeeds=incomplete,primary=bots==6,**paired_stats(differences)))
 prior=0
 for rank,(_,row) in enumerate(sorted(enumerate(pairs),key=lambda t:t[1]['signTestTwoSided'])):
  prior=max(prior,min(1,row['signTestTwoSided']*(len(pairs)-rank)));row['signTestHolmAdjusted']=prior
 return dict(version=1,groups=groups,pairs=pairs,expectedWorlds=len(expected),recordedWorlds=len(rows),missing=[c for c in expected if c['id'] not in byid],
   long=[r for r in rows if r['case']['phase']=='long'],blocked=[dict(case=r['case'],status=r['status'],lastSavedTick=r['lastSavedTick'],lastAttempt=r['segments'][-1] if r['segments'] else None) for r in rows if r['status'].startswith('blocked')],
   definition='No missing outcome imputed. Different planning horizons never pooled. n5 cells exploratory; n10 primary. Industrial Hard superiority remains unproved if late milestones are censored.')

def load_observations(path,plan):
 # Explicit owned measurement export, not a user-supplied simulation plan.
 opener=gzip.open if path.suffix=='.gz' else open
 with opener(path,'rt',encoding='utf-8') as stream:data=json.load(stream)
 if data['plan']!=plan:raise ValueError('Measurement plan does not match registered current protocol')
 rows=data['rows']
 if data.get('formatVersion')==2:
  rows=[dict(r,frozen=data['frozen']) for r in rows]
 return rows

def main():
 p=argparse.ArgumentParser(description=__doc__);source=p.add_mutually_exclusive_group(required=True);source.add_argument('--study',type=Path);source.add_argument('--observations',type=Path,help='Owned exported observations, optionally gzip/common fingerprint format2');p.add_argument('--output',type=Path,required=True);a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
 plan=read(PLAN);rows=load_observations(a.observations,plan) if a.observations else [observe(p.parent) for p in sorted(a.study.rglob('trajectory.json'))];result=summarize(rows,plan)
 write(a.output/'observations.json',dict(rows=rows,plan=plan));write(a.output/'summary.json',result)
 print(json.dumps(dict(recorded=result['recordedWorlds'],missing=len(result['missing']),blocked=len(result['blocked']),complete=sum(r['status']=='completed' for r in rows))))
if __name__=='__main__':main()
