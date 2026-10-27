"""Independent E2 audit from actual runner exports, including road and vehicle histories.
Author: Шелестов Максим Евгеньевич.
"""
import json
from pathlib import Path


def verify_roads(folder):
    folder=Path(folder)
    config=json.loads((folder/'scenario.json').read_text());rules=(config.get('economy') or {}).get('roads')
    path=folder/'roads.json'
    if rules is None:
        assert not path.exists(),'Unexpected roads export'
        return
    state=json.loads((folder/'final-state.json').read_text());roads=json.loads(path.read_text());fleet=json.loads((folder/'fleet.json').read_text())
    events=[json.loads(s) for s in (folder/'events.jsonl').read_text().splitlines()]
    ledger=[json.loads(s) for s in (folder/'ledger.jsonl').read_text().splitlines()]
    def cell(c):return (c['x'],c['z'])
    def point(s):return tuple(map(int,s.split(',')))
    def owner(e):return (e.get('actorId') or {}).get('value',0)
    corridor={cell(c) for c in config['layout']['roadCorridorCells']}
    private={cell(p['position']) for p in config.get('plots') or []}
    protected={cell(c) for s in config['layout']['civicObjects'] for c in s['footprint']}
    legal=corridor-private-protected
    stages={s['level']:s for s in rules['stages']}
    assert list(stages)==[1,2,3] and [s['speed'] for s in stages.values()]==[20,40,60]
    initial={cell(c) for c in config['layout']['initialCityDirtCells']};built={c:[(1,0,0,0)] for c in initial}
    vehicles={v['id']:v for v in fleet['vehicles']};models={m['id']:m for m in config['economy']['fleet']['models']}
    inventory={i['id']['value']:i for i in state['inventories']};positions={};listings={};work={};mode={};starts=[];paid={}
    for e in events:
        kind=e['type'];identity=e['entityId'];tick=e['tick']
        if kind in ('DealerInitialStock','DealerSupply'):listings[identity]=point(e['reasonCode'])
        elif kind=='VehiclePurchased':positions[identity]=listings.pop(identity)
        elif kind=='RoadWorkConfigured':mode[identity]=e['reasonCode']
        elif kind=='RoadWorkStarted':
            assert identity in vehicles and models[vehicles[identity]['modelId']]['materialsOnly']
            assert identity not in work
            at=point(e['reasonCode']);level=e['quantity'];assert at in legal and positions[identity]==at
            assert len(built.get(at,[]))==level-1 and e['amountMinor']==stages[level]['priceMinor']
            command=f'road:{identity}:{tick}'
            actual={m['itemId']:m['quantity'] for m in events if m['type']=='ConstructionMaterialConsumed' and m.get('commandId')==command}
            assert actual=={m['itemId']:m['quantity'] for m in stages[level]['materials']}
            work[identity]=dict(cell=at,level=level,tick=tick,amount=0,builder=owner(e),mode=mode[identity]);starts.append(e)
        elif kind=='RoadWorkAdvanced':
            w=work[identity];assert tick>w['tick'] and e['quantity'] in [1,2]
            if w['mode']=='Personal':assert e['quantity']==1 and e['amountMinor']==0
            else:assert w['mode']=='Automatic' and e['amountMinor']>=0
            w['amount']+=e['quantity']
        elif kind=='RoadStageCompleted':
            w=work.pop(identity);at=point(e['reasonCode']);level=e['quantity']
            assert at==w['cell'] and level==w['level'] and owner(e)==w['builder']
            assert 2*stages[level]['ticks']<=w['amount']<=2*stages[level]['ticks']+1
            assert len(built.get(at,[]))==level-1
            built.setdefault(at,[]).append((level,owner(e),tick,identity))
        elif kind=='TrafficCellEntered':
            before,after=map(point,e['reasonCode'].split('>'))
            assert positions[identity]==before and sum(abs(a-b) for a,b in zip(before,after))==1
            assert not any(w['cell']==after for v,w in work.items() if v!=identity),'Vehicle crossed working paver'
            if after in corridor:assert not any(p==after for v,p in positions.items() if v!=identity),'Overlapping physical vehicles'
            positions[identity]=after
            a=len(built.get(before,[]));b=len(built.get(after,[]))
            cap=stages[min(a,b)]['speed'] if a and b else 8
            assert 0<e['quantity']<=min(cap,models[vehicles[identity]['modelId']]['speed'])
        elif kind=='RoadOrderPaid':
            assert identity not in paid;paid[identity]=(owner(e),e['amountMinor'])
    actual={cell(c['cell']):[(x['level'],x['builder']['value'],x['tick'],x['vehicleId']) for x in c['contributions']] for c in roads['cells']}
    assert actual==built,'Road contributions erased or invented'
    assert all(c['level']==len(c['contributions']) for c in roads['cells'])
    assert sum(e['amountMinor'] for e in starts)==sum(l['amountMinor'] for l in ledger if l['reason']=='RoadConstruction')
    assert sum(e['amountMinor'] for e in events if e['type']=='RoadWorkAdvanced')==sum(l['amountMinor'] for l in ledger if l['reason']=='RoadWages')
    for w in roads['works']:
        assert w['group']['id']['value']==w['vehicleId'] and w['group']['role']=='RoadWorks' and w['group']['capacity']==2
        assert w['group']['ownerId']==vehicles[w['vehicleId']]['owner']
        if w['batch']:
            expected=work.pop(w['vehicleId']);assert w['batch']['work']==expected['amount'] and cell(w['batch']['cell'])==expected['cell']
    assert not work
    for b in roads['bypasses']:
        p=list(map(cell,b['path']));assert len(p)>2 and set(p)<=legal and cell(b['workCell']) not in p
        assert all(sum(abs(a-b) for a,b in zip(x,y))==1 for x,y in zip(p,p[1:]))
    reserved=set()
    for t in roads['traffic']:
        p=list(map(cell,t['path']));i=t['index'];assert 0<=t['progress']<800 and 0<=t['advancedQuanta']<=10
        if p:assert positions[t['vehicleId']]==p[i]
        if t['progress']:
            assert i<len(p)-1 and p[i+1] not in reserved
            reserved.add(p[i+1]);assert not any(at==p[i+1] for v,at in positions.items() if v!=t['vehicleId'])
    assert all(positions[v]==cell(inventory[vehicles[v]['cargoInventory']['value']]['position']) for v in vehicles)
    pairs=set()
    for o in roads['orders']:
        key=(cell(o['cell']),o['level']);assert key not in pairs;pairs.add(key)
        if o['id'] in paid:assert paid[o['id']]==(o['assignee'],o['reward']) and o['paidTick']>0
    assert sum(v[1] for v in paid.values())==sum(l['amountMinor'] for l in ledger if l['reason']=='RoadOrderReward')
    public=[json.loads(s) for s in (folder/'public-events.jsonl').read_text().splitlines()]
    assert not any(e['type'].startswith('RoadWork') for e in public)
    assert not any(l['reason'] in ('RoadTax','RoadInvoice','RoadToll') for l in ledger),'E3 charges are not part of E2'
    print(str(folder)+': E2 sequential layers, actual materials/work/money, physical paths and privacy verified')


if __name__=='__main__':
    import sys
    for folder in sys.argv[1:]:verify_roads(folder)
