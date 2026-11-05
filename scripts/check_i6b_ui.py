#!/usr/bin/env python3
"""I6-B uses the existing frozen I6 native driver; only the isolated probe and phase differ.
Автор: Шелестов Максим Евгеньевич.
"""
import argparse,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--output',required=True);p.add_argument('--godot',required=True);p.add_argument('--prepared');p.add_argument('--fixture');p.add_argument('--build-only',action='store_true');p.add_argument('--probe-only',action='store_true');p.add_argument('--case',choices=['all','regression','prepare','production','equipment','fleet','stock','trade','logistics','roads','city','landscape','demolition',*['from-'+k for k in ['navigation','production','equipment','fleet','stock','trade','logistics','roads','city','landscape','demolition']]],default='all')
a=p.parse_args();command=[sys.executable,str(ROOT/'scripts/check_i6a_native.py'),'--output',a.output,'--godot',a.godot,'--probe-source',str(ROOT/'tests/Client.Probes/I6BUnifiedProbe.cs'),'--case',a.case]
for key in ['prepared','fixture']:
 if getattr(a,key):command+=['--'+key,getattr(a,key)]
if a.build_only:command+=['--build-only']
elif not a.probe_only:command+=['--import-resources']
if a.probe_only:command+=['--probe-only']
raise SystemExit(subprocess.run(command,cwd=ROOT).returncode)
