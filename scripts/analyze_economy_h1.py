#!/usr/bin/env python3
"""H1 seed-level distributions and paired comparisons. No missing outcome is replaced with zero.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,collections,json,math,random,statistics
from pathlib import Path
from analyze_paired_series import spread,quantile
from research_lab import read,read_metadata,write

def paired_stats(values):
 nonzero=[v for v in values if v];n=len(nonzero);positive=sum(v>0 for v in nonzero)
 p=min(1,2*sum(math.comb(n,k) for k in range(min(positive,n-positive)+1))/2**n) if n else 1
 rng=random.Random(20261029)
 boots=[statistics.median(rng.choices(values,k=len(values))) for _ in range(5000)] if values else []
 return dict(differences=values,spread=spread(values),signTestTwoSided=p,nonzeroPairs=n,medianBootstrap95=[quantile(boots,.025),quantile(boots,.975)] if boots else None)

def summarize(rows):
 groups=collections.defaultdict(list)
 for row in rows:groups[(Path(row['map']).stem,row['bots'],row['difficulty'])].append(row)
 summaries=[];pairs=[]
 for (map_key,bots,difficulty),rs in sorted(groups.items()):
  good=[r for r in rs if r['status']=='succeeded'];metrics=[r['metrics'] for r in good]
  values={k:spread([m[k] for m in metrics if m.get(k) is not None]) for k in ['npcMedianCapitalChangeMinor','npcOperatingReceiptResultMinor','npcOperatingAfterRoadDebtMinor','employment','hostedMillisecondsPerActiveTick']}
  npc_milestones=[m for r in good for actor,m in r['metrics']['fullEconomy']['milestones'].items() if actor!='1']
  observed=[m['firstProductionTick'] for m in npc_milestones if m['firstProductionTick'] is not None]
  xp=collections.Counter()
  for r in good:
   for owner,grants in r['metrics']['fullEconomy']['experience'].items():
    if not owner.startswith('1:'):xp.update(grants)
  values['firstProductionAmongObservedOnly']=spread(observed)
  summaries.append(dict(map=map_key,bots=bots,difficulty=difficulty,states=dict(collections.Counter(r['status'] for r in rs)),independentCompletedSeeds=len({r['seed'] for r in good}),metrics=values,npcMilestoneObservations=len(npc_milestones),firstProductionObserved=len(observed),rightCensoredNpcMilestones=len(npc_milestones)-len(observed),xp=dict(xp),questXpShare=xp.get('Quest',0)/sum(xp.values()) if sum(xp.values()) else None,
   completedBatches=sum(r['metrics']['fullEconomy']['events'].get('ProductionCompleted',0) for r in good),importsCollected=sum(sum(r['metrics']['fullEconomy']['importsCollectedByItem'].values()) for r in good),importsPurchased=sum(sum(r['metrics']['fullEconomy']['importsPurchasedByItem'].values()) for r in good),importsNetRemoved=sum(sum(r['metrics']['fullEconomy']['importsNetRemovedFromPickupsByItem'].values()) for r in good),vehicles=sum(r['metrics']['fullEconomy']['vehicleCount'] for r in good),roadStages=sum(r['metrics']['fullEconomy']['completedRoadStages'] for r in good),serviceOrders=sum(r['metrics']['fullEconomy']['serviceOrders'] for r in good),unknownCash=[r['metrics']['fullEconomy']['unclassifiedActorCashMinor'] for r in good if r['metrics']['fullEconomy']['unclassifiedActorCashMinor']]))
 for map_key in ['small-river','large-districts']:
  for left,right in [('Hard','Medium'),('Easy','Medium')]:
   def indexed(difficulty):
    rs=[r for r in groups[(map_key,6,difficulty)] if r['status']=='succeeded']
    if len({r['seed'] for r in rs})!=len(rs):raise ValueError('Duplicate world/seed is not an independent replicate')
    return {r['seed']:r for r in rs}
   a=indexed(left);b=indexed(right)
   seeds=sorted(a.keys()&b.keys());complete=[seed for seed in seeds if a[seed]['metrics'].get('npcMedianCapitalChangeMinor') is not None and b[seed]['metrics'].get('npcMedianCapitalChangeMinor') is not None]
   diffs=[a[s]['metrics']['npcMedianCapitalChangeMinor']-b[s]['metrics']['npcMedianCapitalChangeMinor'] for s in complete]
   pairs.append(dict(map=map_key,bots=6,left=left,right=right,seeds=complete,plannedIndependentPairs=10,completeIndependentPairs=len(complete),primary='Per-world median NPC game-defined capital change at1800; not long-term superiority',**paired_stats(diffs)))
 ranked=sorted(enumerate(pairs),key=lambda p:p[1]['signTestTwoSided']);prior=0
 for rank,(_,pair) in enumerate(ranked):prior=max(prior,min(1,pair['signTestTwoSided']*(len(pairs)-rank)));pair['signTestHolmAdjusted']=prior
 return dict(version=1,unitOfIndependence='world/seed; NPC observations inside a world are coupled',groups=summaries,pairs=pairs,failures=[r for r in rows if r['status'] in ('failed','interrupted','cancelled')],pending=sum(r['status'] in ('queued','running') for r in rows))

def plots(rows,out):
 import matplotlib
 matplotlib.use('Agg')
 import matplotlib.pyplot as plt
 colours={'Easy':'#5c9470','Medium':'#3275a8','Hard':'#c76a45'}
 maps=sorted({Path(r['map']).stem for r in rows});fig,axes=plt.subplots(1,len(maps),figsize=(12,5),sharey=True)
 for ax,map_key in zip(axes,maps):
  for i,bots in enumerate([3,6,10]):
   for j,difficulty in enumerate(['Easy','Medium','Hard']):
    values=[r['metrics']['npcMedianCapitalChangeMinor']/100 for r in rows if Path(r['map']).stem==map_key and r['bots']==bots and r['difficulty']==difficulty and r['status']=='succeeded' and r['metrics'].get('npcMedianCapitalChangeMinor') is not None]
    if values:
     b=ax.boxplot([values],positions=[i+(j-1)*.23],widths=.18,patch_artist=True,manage_ticks=False)
     for patch in b['boxes']:patch.set_facecolor(colours[difficulty])
  ax.set_xticks(range(3),[3,6,10]);ax.set_title(map_key);ax.set_xlabel('NPC count');ax.axhline(0,color='#888',lw=.7);ax.grid(axis='y',alpha=.2)
 axes[0].set_ylabel('Per-world median NPC capital change, coins')
 from matplotlib.patches import Patch
 fig.legend(handles=[Patch(color=c,label=k) for k,c in colours.items()],loc='upper center',ncol=3)
 fig.suptitle('H1: ordinary games, 1800 active ticks',y=1.03);fig.tight_layout();fig.savefig(out/'capital-distribution.svg',bbox_inches='tight');plt.close(fig)

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--study',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--plots',action='store_true');a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
 rows=read_metadata(a.study/'observations.json')['rows'];result=summarize(rows);write(a.output/'study-summary.json',result)
 if a.plots:plots(rows,a.output)
 print(json.dumps(dict(groups=len(result['groups']),pending=result['pending'],failures=len(result['failures']))))
if __name__=='__main__':main()
