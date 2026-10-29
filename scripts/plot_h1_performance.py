#!/usr/bin/env python3
"""Measured H1 host cost/checkpoint size; no long-horizon extrapolation.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,json
from pathlib import Path

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--evidence',type=Path,required=True);a=p.parse_args()
 import matplotlib
 matplotlib.use('Agg')
 import matplotlib.pyplot as plt
 data=json.loads((a.evidence/'long-trajectories.json').read_text());fig,axes=plt.subplots(1,2,figsize=(11,4))
 for name,value in data.items():
  completed=value['finalAnalysis'];x=[r['tick']/3600 for r in completed];y=[r['checkpoint']['decodedBytes']/1024**2 for r in completed]
  line,=axes[0].plot(x,y,'o-',label=value['map']);colour=line.get_color();ok=[r for r in value['segments'] if 'stateHash' in r]
  axes[1].plot([r['targetTick']/3600 for r in ok],[r['runner']['elapsed_seconds'] for r in ok],'o-',color=colour,label=value['map'])
  failed=[r for r in value['segments'] if r.get('runner',{}).get('timed_out')]
  for r in failed:axes[1].plot(r['targetTick']/3600,r['runner']['elapsed_seconds'],'x',color=colour,markersize=10)
 axes[0].axhline(512,color='#a44',linestyle='--',label='Existing decoded checkpoint limit')
 axes[0].set_ylabel('Measured decoded checkpoint, MiB');axes[1].set_ylabel('Runner wall time per segment, seconds')
 axes[1].axhline(900,color='#a44',linestyle='--',label='Original segment budget')
 for ax in axes:ax.set_xlabel('Segment target, active game hours');ax.grid(alpha=.2);ax.legend(fontsize=8)
 fig.suptitle('H1 measured long runs; crosses are timed-out attempts, not reached milestones')
 fig.tight_layout();fig.savefig(a.evidence/'long-run-performance.svg');plt.close(fig)
if __name__=='__main__':main()
