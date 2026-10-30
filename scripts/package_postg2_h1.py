#!/usr/bin/env python3
"""Small, relocatable evidence bundle; full checkpoints/exports stay in the separate full artifact.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,json,shutil
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
if not (a.source/'trajectory.json').exists():raise SystemExit('No journal: infrastructure failure, not an economic observation')
# Preserve case directory names so merged thin artifacts never overwrite another world's records.
for f in a.source.rglob('*'):
 if not f.is_file() or f.name not in {'trajectory.json','h1-metrics.json','summary.json','run-progress.json','ordinary.json','decisions-storage.json'} and not (f.parent.name=='checks' and f.suffix in {'.json','.log'}):continue
 if f.name.endswith('.log') and f.stat().st_size>100000:continue
 dest=a.output/a.source.name/f.relative_to(a.source);dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(f,dest)
print('Packaged audit/metrics and journal; full raw results retained separately')
