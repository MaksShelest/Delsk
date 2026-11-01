#!/usr/bin/env python3
"""Independent streaming check of complete G1 observations and immutable private inventory/account ownership.
Author: Шелестов Максим Евгеньевич. Runs no simulation or policy.
"""
import argparse,gzip,hashlib,json
from pathlib import Path
def value(v):return v['value'] if isinstance(v,dict) else v
def verify(folder):
 summary=json.loads((folder/'summary.json').read_text());world=json.loads((folder/'final-state.json').read_text())
 inventories={value(i['id']):value(i['ownerId']) for i in world['inventories']}
 accounts={value(p['id']):value(p['accountId']) for p in world['participants']}
 personal={value(p['id']):value(p['inventoryId']) for p in world['participants']}
 path=folder/'decisions.jsonl';opener=open
 if not path.exists():path=Path(str(path)+'.gz');opener=gzip.open
 count=0;previous=-1;digest=hashlib.sha256();by_actor={}
 with opener(path,'rb') as f:
  for raw in f:
   digest.update(raw);turn=json.loads(raw);actor=value(turn['actor']);o=turn['observation'];tick=turn['tick']
   assert previous<=tick<=summary['tick'] and tick==o['tick'];previous=tick
   assert value(o['participant']['id'])==actor and value(o['account']['id'])==accounts[actor]
   assert value(o['participant']['inventoryId'])==personal[actor]
   assert not {'scenario','config','seed','journal','ledger','stateHash','accounts'}&set(o)
   for inventory in o['inventories']:assert inventories[value(inventory['id'])]==actor
   for group in o['employmentGroups']:assert value(group['ownerId'])==actor
   for enterprise in o['enterprises']:assert value(enterprise['ownerId'])==actor
   for equipment in o['equipment']:assert value(equipment['ownerId'])==actor
   if o.get('fleet'):
    for vehicle in o['fleet']['vehicles']:assert value(vehicle['vehicle']['owner'])==actor
   count+=1;by_actor[actor]=by_actor.get(actor,0)+1
 assert count==summary['controllerDecisionTurns'],(count,summary['controllerDecisionTurns'])
 return dict(turns=count,byActor=by_actor,uncompressedSha256=digest.hexdigest(),allTurnsRetained=True,privateInventoryAndAccountOwnership=True,scope='Complete exported G1 history, monotone ticks and immutable inventory/account ownership; complements existing accounting and public-shape audits')
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('folder',type=Path);p.add_argument('--output',type=Path);a=p.parse_args();r=verify(a.folder)
 if a.output:a.output.write_text(json.dumps(r,indent=2)+'\n')
 print(json.dumps(r))
