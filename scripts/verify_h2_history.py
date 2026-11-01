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
 scenario=json.loads((folder/'scenario.json').read_text())
 ground_capacity=(scenario.get('economy') or {}).get('storage',{}).get('groundCapacity')
 plots={value(p['id']):p for p in world['plots']}
 initial_owners={value(p['id']):value(p['ownerId']) if p.get('ownerId') is not None else None for p in scenario['plots']}
 purchases={}
 with (folder/'events.jsonl').open() as events:
  for raw in events:
   event=json.loads(raw)
   if event['type']=='PlotPurchased':purchases[value(event['entityId'])]=(value(event['actorId']),event['tick'])
 historical_ground={};historical_checks=0
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
   assert value(o['participant']['accountId'])==accounts[actor]
   assert value(o['participant']['inventoryId'])==personal[actor]
   assert not {'scenario','config','seed','journal','ledger','stateHash','accounts'}&set(o)
   ground_by_plot={}
   for inventory in o['inventories']:
    identity=value(inventory['id'])
    if identity in inventories:assert inventories[identity]==actor;continue
    # Construction removes empty ground inventories. Their final-world absence is legal;
    # authenticate the historical ground against independently exported land purchase events.
    # IDs must remain bound to one owner/plot, and never alias a surviving foreign inventory.
    candidates=[p for p in plots.values() if p['position']==inventory['position']]
    assert len(candidates)==1,('Unknown historical inventory',identity,tick)
    plot=candidates[0];plot_id=value(plot['id'])
    assert value(plot['ownerId'])==actor
    assert initial_owners[plot_id]==actor or (plot_id in purchases and purchases[plot_id][0]==actor and purchases[plot_id][1]<=tick)
    assert ground_capacity is not None and inventory['capacity']==ground_capacity
    assert not any(inventory['position'] in b['footprint'] for b in o['buildings'])
    binding=(actor,plot_id)
    assert historical_ground.setdefault(identity,binding)==binding
    assert ground_by_plot.setdefault(binding,identity)==identity
    historical_checks+=1
   for group in o['employmentGroups']:assert value(group['ownerId'])==actor
   for enterprise in o['enterprises']:assert value(enterprise['ownerId'])==actor
   for equipment in o['equipment']:assert value(equipment['ownerId'])==actor
   if o.get('fleet'):
    for vehicle in o['fleet']['vehicles']:assert value(vehicle['vehicle']['owner'])==actor
   count+=1;by_actor[actor]=by_actor.get(actor,0)+1
 assert count==summary['controllerDecisionTurns'],(count,summary['controllerDecisionTurns'])
 return dict(turns=count,byActor=by_actor,uncompressedSha256=digest.hexdigest(),allTurnsRetained=True,privateInventoryAndAccountOwnership=True,historicalGroundInventories=len(historical_ground),historicalGroundChecks=historical_checks,scope='Complete exported G1 history: surviving inventories checked against authoritative final ownership; removed ground inventory owner/location/capacity checked against land purchase timeline, stable IDs and historical building footprints. Complements exact full-trace continuation and existing accounting/public-shape audits; does not reconstruct every historical inventory allocation ID independently.')
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('folder',type=Path);p.add_argument('--output',type=Path);a=p.parse_args();r=verify(a.folder)
 if a.output:a.output.write_text(json.dumps(r,indent=2)+'\n')
 print(json.dumps(r))
