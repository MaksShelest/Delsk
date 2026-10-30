#!/usr/bin/env python3
"""Export observed H1 distributions, censoring and bounded-runtime plots.
Author: Шелестов Максим Евгеньевич. No simulated/extrapolated outcomes.
"""
import argparse,csv,json,os
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--analysis',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();a.output.mkdir(parents=True,exist_ok=True)
os.environ.setdefault('MPLCONFIGDIR',str(a.output/'.mpl-cache'));os.environ.setdefault('XDG_CACHE_HOME',str(a.output/'.cache'))
import matplotlib
matplotlib.use('Agg');matplotlib.rcParams['svg.hashsalt']='h1-post-g2-v1'
import matplotlib.pyplot as plt
data=json.loads((a.analysis/'observations.json').read_text());rows=data['rows'];ordinary=data['plan']['ordinary'];planned=len(data['plan']['maps'])*len(ordinary['difficulties'])*sum(len(ordinary['seedsByBots'][str(n)]) for n in ordinary['bots']);complete=sum(r['case']['phase']=='ordinary' and r['status']=='completed' and r['lastSavedTick']==5000 for r in rows)
with (a.output/'world-endpoints.csv').open('w',newline='') as f:
 w=csv.writer(f);w.writerow(['case','phase','map','bots','difficulty','seed','status','last_audited_tick','median_npc_capital_change_minor','npc_receipts_result_minor','first_factory_tick','censored_at_tick'])
 for row in rows:
  c=row['case'];good=[s for s in row['segments'] if s.get('status')=='succeeded'];m=good[-1]['metrics'] if good else {}
  first=m.get('firstNpcProductionTick');w.writerow([c['id'],c['phase'],c['map'],c['bots'],c['difficulty'],c['seed'],row['status'],row['lastSavedTick'],m.get('npcMedianCapitalChangeMinor'),m.get('npcOperatingReceiptResultMinor'),first,None if first is not None else row['lastSavedTick']])
colors={'Easy':'#4b9376','Medium':'#477ec0','Hard':'#b5667e'}
fig,axes=plt.subplots(1,2,figsize=(12,4.8),sharey=True)
for ax,map_key in zip(axes,['small-river','large-districts']):
 labels=[];x=0
 for bots in [3,6,10]:
  for diff in colors:
   values=[]
   for row in rows:
    c=row['case']
    if c['phase']=='ordinary' and c['map']==map_key and c['bots']==bots and c['difficulty']==diff and row['status']=='completed' and row['lastSavedTick']==5000:
     m=[s for s in row['segments'] if s.get('status')=='succeeded'][-1]['metrics'];v=m.get('npcMedianCapitalChangeMinor')
     if v is not None:values.append(v/100)
   if values:
    ax.boxplot(values,positions=[x],widths=.6,showfliers=False,medianprops={'color':'black'})
    ax.scatter([x+(i-(len(values)-1)/2)*.025 for i in range(len(values))],values,s=14,color=colors[diff],zorder=3)
   labels.append(f'{bots}\n{diff}');x+=1
 ax.set_xticks(range(len(labels)),labels,fontsize=8);ax.set_title(map_key);ax.grid(axis='y',alpha=.25)
axes[0].set_ylabel('Change in world median NPC capital, coins')
fig.suptitle(f'Ordinary tick5000: {complete}/{planned} worlds complete; one dot per independent world')
fig.tight_layout();fig.savefig(a.output/'ordinary-capital.svg',metadata={'Date':None});plt.close(fig)
fig,axes=plt.subplots(1,2,figsize=(12,4.8))
for row in rows:
 c=row['case']
 if c['phase']!='long':continue
 good=[s for s in row['segments'] if s.get('status')=='succeeded'];label=f"{c['map']} {c['difficulty']} s{c['seed']}"
 for ax,key,scale in [(axes[0],'elapsed_seconds',1),(axes[1],'checkpointBytes',1024*1024)]:
  x=[s['targetTick']/3600 for s in good];y=[s['runner'].get(key,0)/scale if key=='elapsed_seconds' else s.get(key,0)/scale for s in good]
  ax.plot(x,y,marker='o',markersize=3,label=label)
 for failure in [s for s in row['segments'] if s.get('status')!='succeeded' and s.get('runner')]:
  axes[0].scatter([failure['targetTick']/3600],[failure['runner']['elapsed_seconds']],marker='x',color='#b33d43',s=45,zorder=5)
axes[0].axhline(900,color='#b33d43',linestyle=':',label='900s budget');axes[0].set_ylabel('Wall seconds / attempt (x = failed)');axes[1].set_ylabel('Checkpoint envelope MiB');axes[1].axhline(64,color='red',linestyle=':',label='64MiB reader limit')
for ax in axes:ax.set_xlabel('Audited active game hours');ax.grid(alpha=.25)
axes[0].set_xlabel('Target active game hours');axes[1].legend(fontsize=7,bbox_to_anchor=(1.02,1),loc='upper left');fig.suptitle('Long trajectories: audited segments and measured failures; no extrapolation')
fig.tight_layout();fig.savefig(a.output/'long-runtime.svg',metadata={'Date':None});plt.close(fig)
print('Exported CSV and two observed-only plots')
