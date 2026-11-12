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
    movement=config['economy'].get('movement') or {}
    i1=movement.get('version')==2
    i2=config['economy'].get('vehicleControl')
    if i2:assert i1 and i2==dict(version=1,exitHoldQuanta=5,laneOffset=200)
    if i1:assert movement['speedDivisor']==40 and movement['speedMultiplier']==3 and movement['nominalSpeeds']==dict(runningRoad=10,runningOffroad=8,vehicleOffroad=10)
    elif movement:assert movement['version']==1 and movement['speedDivisor']==32 and movement.get('speedMultiplier',0)==0 and movement.get('nominalSpeeds') is None
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
    vehicles={v['id']:v for v in fleet['vehicles']};owned=set(vehicles);models={m['id']:m for m in config['economy']['fleet']['models']}
    lanes={v:0 for v in vehicles}
    def opposite(a,b):return a and b and (a-b)%4==2
    def share(a,b,at):
        bypass_cells={cell(c) for bypass in roads['bypasses'] for c in bypass['path']}
        return i2 and a in owned and b in owned and vehicles[a]['modelId']!='trakt_d1' and vehicles[b]['modelId']!='trakt_d1' and not models[vehicles[a]['modelId']].get('equipmentTypeId') and not models[vehicles[b]['modelId']].get('equipmentTypeId') and opposite(lanes[a],lanes[b]) and at in built and positions[a] in built and positions[b] in built and not {at,positions[a],positions[b]}&bypass_cells
    prices={i['id']:i['basePriceMinor'] for i in config['items']}
    inventory={i['id']['value']:i for i in state['inventories']};positions={};listings={};equipment_stock={};work={};mode={};starts=[];paid={}
    dealer_version = config['economy']['fleet'].get('equipmentDealerVersion', 0)
    if dealer_version == 2:
        dealer = next(s for s in config['layout']['civicObjects'] if s['kind'] == 'dealership')
        catalog_requests = {}
        first_traffic = {}
        for event in events:
            identity = event['entityId']
            if event['type'] == 'TrafficCellEntered' and identity not in first_traffic:
                first_traffic[identity] = point(event['reasonCode'].split('>')[0])
    equipment_initial={e['entityId']:point(e['reasonCode']) for e in events if e['type']=='EquipmentTrafficRegistered'}
    for machine in state['equipment']:
        identity=machine['id']['value']
        if identity in owned:continue # I4 shares the purchased Fleet body and cargo, not a second traffic body.
        model='extractor:'+machine['typeId']
        models[model]=dict(speed=max(1,80//config['moveTicksPerCell']),materialsOnly=False)
        vehicles[identity]=dict(id=identity,owner=machine['ownerId'],modelId=model,cargoInventory=machine['storageId'])
        positions[identity]=equipment_initial.get(identity,cell(machine['position']))
    for e in events:
        kind=e['type'];identity=e['entityId'];tick=e['tick']
        if kind in ('DealerInitialStock','DealerSupply'):
            listings[identity]=point(e['reasonCode'])
            if dealer_version in (1, 2) and models[e['itemId']].get('equipmentTypeId'):
                equipment_stock[identity] = listings[identity]
        elif kind=='DealerReplaced':
            listings.pop(identity,None);equipment_stock.pop(identity,None)
        elif kind == 'VehicleCatalogRequested':
            assert dealer_version == 2 and e['commandId'] not in catalog_requests
            catalog_requests[e['commandId']] = e
        elif kind == 'VehiclePurchased':
            if identity in listings:
                positions[identity] = listings.pop(identity)
                equipment_stock.pop(identity, None)
            else:
                assert dealer_version == 2 and e.get('commandId') in catalog_requests
                request = catalog_requests.pop(e['commandId'])
                assert owner(request) == owner(e) and request['itemId'] == e['itemId'] and request['tick'] < tick
                vehicle = vehicles[identity]
                model = models[vehicle['modelId']]
                assert model['id'] == e['itemId']
                # Catalog receipts omit the bay. The first movement origin or stationary cargo witnesses it.
                position = first_traffic.get(identity, cell(inventory[vehicle['cargoInventory']['value']]['position']))
                if model.get('equipmentTypeId'):
                    bays = dealer['equipmentParking']
                else:
                    parking = dealer['parking']
                    bays = parking[-1:] if model['materialsOnly'] else parking[:2]
                assert position in map(cell, bays), 'Catalog vehicle did not start in an eligible dealer bay'
                positions[identity] = position
        elif kind=='TrafficLaneState':
            assert i2 and identity in owned and vehicles[identity]['modelId']!='trakt_d1'
            assert owner(e)==vehicles[identity]['owner']['value'] and not e.get('amountMinor') and not e.get('commandId') and not e.get('itemId')
            key=e['quantity'];assert 0<=key<=4 and lanes[identity]!=key
            assert positions[identity]==point(e['reasonCode']) and (not key or positions[identity] in built)
            lanes[identity]=key
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
            assert after not in equipment_stock.values(), "Crossed unsold extraction equipment"
            assert not any(w['cell']==after for v,w in work.items() if v!=identity),'Vehicle crossed working paver'
            if after in corridor:assert all(p!=after or share(identity,v,after) for v,p in positions.items() if v!=identity),'Overlapping physical vehicles'
            if i2 and lanes.get(identity):assert {1:(0,-1),2:(1,0),3:(0,1),4:(-1,0)}[lanes[identity]]==(after[0]-before[0],after[1]-before[1])
            positions[identity]=after
            a=len(built.get(before,[]));b=len(built.get(after,[]))
            cap=stages[min(a,b)]['speed'] if a and b else 10 if i1 else 8
            assert 0<e['quantity']<=min(cap,models[vehicles[identity]['modelId']]['speed'])
        elif kind=='RoadOrderPaid':
            assert identity not in paid;paid[identity]=(owner(e),e['amountMinor'])
    actual={cell(c['cell']):[(x['level'],x['builder']['value'],x['tick'],x['vehicleId']) for x in c['contributions']] for c in roads['cells']}
    assert actual==built,'Road contributions erased or invented'
    assert all(c['level']==len(c['contributions']) for c in roads['cells'])
    assert sum(e['amountMinor'] for e in starts)==sum(l['amountMinor'] for l in ledger if l['reason']=='RoadConstruction')
    assert sum(e['amountMinor'] for e in events if e['type'] in ('RoadWorkAdvanced','RoadWorkTravelled'))==sum(l['amountMinor'] for l in ledger if l['reason']=='RoadWages')
    for w in roads['works']:
        assert w['group']['id']['value']==w['vehicleId'] and w['group']['role']=='RoadWorks' and w['group']['capacity']==2
        assert w['group']['ownerId']==vehicles[w['vehicleId']]['owner']
        if w['batch']:
            expected=work.pop(w['vehicleId']);assert w['batch']['work']==expected['amount'] and cell(w['batch']['cell'])==expected['cell']
    assert not work
    for b in roads['bypasses']:
        p=list(map(cell,b['path']));assert len(p)>2 and set(p)<=legal and cell(b['workCell']) not in p
        assert all(sum(abs(a-b) for a,b in zip(x,y))==1 for x,y in zip(p,p[1:]))
        assert len(b['users'])==len(set(b['users'])) and all(v in vehicles for v in b['users'])
        if b['closing']:assert b['users']
    reserved={}
    for t in roads['traffic']:
        p=list(map(cell,t['path']));i=t['index'];assert 0<=t['progress']<800 and 0<=t['advancedQuanta']<=10
        if p:assert positions[t['vehicleId']]==p[i]
        fraction=t.get('progressFraction',0);assert 0<=fraction<(596700 if i1 else 1_000_000) and (config['economy'].get('movement') or fraction==0)
        if i2:
            identity=t['vehicleId'];s=t.get('steering');key=0
            if identity in owned and vehicles[identity]['modelId']!='trakt_d1' and s and not s['turnQuanta']:
                facing=(vehicles[identity]['facingX'],vehicles[identity]['facingZ']);bearing={(0,-1):0,(1,0):4000,(0,1):8000,(-1,0):12000}[facing]
                if s['bearing']==bearing and (s['laneX'],s['laneZ'])==(-facing[1]*200,facing[0]*200):key=bearing//4000+1
            assert lanes.get(identity,0)==key,'Lane witness does not match saved physical state'
        if t['progress'] or fraction:
            assert i<len(p)-1
            identity=t['vehicleId'];assert all(share(identity,other,p[i+1]) for other in reserved.get(p[i+1],[]))
            reserved.setdefault(p[i+1],[]).append(identity)
            assert all(at!=p[i+1] or share(identity,v,p[i+1]) for v,at in positions.items() if v!=identity)
    assert all(positions[v]==cell(inventory[vehicles[v]['cargoInventory']['value']]['position']) for v in vehicles)
    pairs=set()
    for o in roads['orders']:
        key=(cell(o['cell']),o['level']);assert key not in pairs;pairs.add(key)
        rule=stages[o['level']];expected=(sum(m['quantity']*prices[m['itemId']] for m in rule['materials'])+2*rule['ticks']*rules['wageMinor'])*120//100
        assert o['reward']==expected
        if o['id'] in paid:assert paid[o['id']]==(o['assignee'],o['reward']) and o['paidTick']>0
    assert sum(v[1] for v in paid.values())==sum(l['amountMinor'] for l in ledger if l['reason']=='RoadOrderReward')
    public=[json.loads(s) for s in (folder/'public-events.jsonl').read_text().splitlines()]
    assert not any(e['type'].startswith('RoadWork') or e['type'] in ('RoadCrewCreated','FreightBudgetExtended') for e in public)
    assert not any(l['reason'] in ('RoadTax','RoadInvoice','RoadToll') for l in ledger),'E3 charges are not part of E2'
    print(str(folder)+': E2 sequential layers, actual materials/work/money, physical paths and privacy verified')


if __name__=='__main__':
    import sys
    for folder in sys.argv[1:]:verify_roads(folder)
