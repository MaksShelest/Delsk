#!/usr/bin/env python3
"""Exact semantic exports, including losslessly archived controller traces.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,gzip,hashlib,json
from pathlib import Path
NAMES='development.json workshops.json demolition.json final-state.json ledger.jsonl events.jsonl commands.jsonl metrics.csv labor.csv controllers.json decisions.jsonl agents.csv market.json scenario.json delivery.json logistics.json capital-demand.json elections.json support.json public-events.jsonl model-decisions.jsonl public-economy.json research-modifications.json physical-state.json negotiations.json production-content.json fleet.json roads.json billing.json services.json personal-accounts.json'.split()
def digest(path):
 actual=path if path.exists() else Path(str(path)+'.gz');opener=actual.open if actual==path else lambda **kw:gzip.open(actual,**kw)
 h=hashlib.sha256()
 with opener(mode='rb') as f:
  for data in iter(lambda:f.read(1024*1024),b''):h.update(data)
 return h.hexdigest()
def compare(left,right):
 hashes={}
 for root in (left,right):
  for name in ['summary.json','final-state.json','ledger.jsonl','commands.jsonl','events.jsonl','controllers.json','scenario.json']:
   if not (root/name).is_file():raise ValueError('Required export missing: '+str(root/name))
 for name in NAMES:
  if not any((root/name).exists() or Path(str(root/name)+'.gz').exists() for root in (left,right)):continue
  a,b=digest(left/name),digest(right/name)
  if a!=b:raise ValueError('Continuation mismatch: '+name)
  hashes[name]=a
 a=json.loads((left/'summary.json').read_text());b=json.loads((right/'summary.json').read_text())
 if a['tick']!=b['tick'] or a['stateHash']!=b['stateHash']:raise ValueError('Final tick/hash mismatch')
 return dict(tick=a['tick'],stateHash=a['stateHash'],files=hashes)
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('left',type=Path);p.add_argument('right',type=Path);p.add_argument('--output',type=Path);a=p.parse_args();result=compare(a.left,a.right)
 if a.output:a.output.write_text(json.dumps(result,indent=2)+'\n')
 print('Exact exports including compressed observations matched:',len(result['files']))
