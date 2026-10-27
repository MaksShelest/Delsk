"""Independent E1 audit of listings, acquisitions, shared cargo and labour from real runner exports.
Author: Шелестов Максим Евгеньевич.
"""
import json
from collections import Counter
from pathlib import Path


def verify_fleet(folder):
    folder=Path(folder)
    config=json.loads((folder/'scenario.json').read_text())
    rules=(config.get('economy') or {}).get('fleet')
    path=folder/'fleet.json'
    if rules is None:
        assert not path.exists(), 'Unexpected fleet export'
        return
    fleet=json.loads(path.read_text())
    state=json.loads((folder/'final-state.json').read_text())
    events=[json.loads(line) for line in (folder/'events.jsonl').read_text().splitlines()]
    ledger=[json.loads(line) for line in (folder/'ledger.jsonl').read_text().splitlines()]
    models={m['id']:m for m in rules['models']}
    inventory={i['id']['value']:i for i in state['inventories']}
    actors={a['id']['value']:a for a in state['participants']}
    assert len(models)==5
    stock={};purchased={};initial=Counter()
    for event in events:
        kind=event['type'];identity=event['entityId'];tick=event['tick']
        if kind in ('DealerInitialStock','DealerSupply'):
            assert identity not in stock and identity not in purchased
            model=event['itemId'];assert model in models
            if kind=='DealerInitialStock':
                assert tick==0;initial[model]+=1
            else:
                assert tick>0 and tick%rules['supplyTicks']==0
            stock[identity]=(model,tick)
            assert len(stock)<=6
        elif kind=='DealerReplaced':
            assert identity in stock and identity not in purchased
            assert tick-stock[identity][1]>=rules['minimumStockTicks']
            del stock[identity]
        elif kind=='VehiclePurchased':
            assert identity in stock and identity not in purchased
            assert event['amountMinor']==models[stock[identity][0]]['priceMinor']
            purchased[identity]=(event['actorId']['value'],stock.pop(identity)[0],tick,event['amountMinor'])
    assert initial==Counter(sputnik20=1,muravey70=2,eger70=1,vector200=1,trakt_d1=1)
    assert stock=={s['id']:(s['modelId'],s['deliveredTick']) for s in fleet['stock']}
    assert len(purchased)==len(fleet['vehicles'])==len(fleet['purchases'])
    assert sum(p[3] for p in purchased.values())==sum(l['amountMinor'] for l in ledger if l['reason']=='VehiclePurchase')
    cargos=set();drivers=set();bound=set()
    for vehicle in fleet['vehicles']:
        identity=vehicle['id'];owner=vehicle['owner']['value'];model=models[vehicle['modelId']]
        assert purchased[identity][:2]==(owner,model['id'])
        cargo_id=vehicle['cargoInventory']['value'];assert cargo_id not in cargos;cargos.add(cargo_id)
        cargo=inventory[cargo_id]
        assert cargo['ownerId']['value']==owner and cargo['capacity']==model['capacityVolume']
        assert cargo['occupiedVolume']+cargo['reservedCapacity']<=model['capacityVolume']
        if model['materialsOnly']:assert all(s['itemId'] in ('gravel','asphalt','concrete') for s in cargo['stock'])
        driver=vehicle['driver']
        if driver:
            assert driver==owner and driver not in drivers;drivers.add(driver)
            assert cargo['position']==actors[driver]['position']
    for system in state['logistics']:
        identity=system.get('vehicleId',0)
        if not identity:continue
        assert identity not in bound;bound.add(identity)
        vehicle=next(v for v in fleet['vehicles'] if v['id']==identity)
        assert not models[vehicle['modelId']]['materialsOnly']
        assert system['cargoInventory']==vehicle['cargoInventory'] and system['ownerId']==vehicle['owner']
        if system['trip']:
            assert vehicle['driver']==0 and not vehicle['installation']
            assert system['trip']['position']==inventory[system['cargoInventory']['value']]['position']
        group=next(g for g in state['employmentGroups'] if g['id']==system['groupId'])
        assert group['role']=='Logistics' and group['capacity']==1
    groups={g['id']['value']:g for g in state['employmentGroups']}
    for platform in fleet['platforms']:
        assert groups[platform['id']]==platform['group']
        assert platform['group']['role']=='Handling' and platform['group']['capacity']==1
    assert state['population']==state['unemployed']+sum(g['headcount'] for g in groups.values())
    public=[json.loads(line) for line in (folder/'public-events.jsonl').read_text().splitlines()]
    assert not any(e['type'].startswith(('VehicleKit','LoadingPlatform')) for e in public)
    print(str(folder)+': E1 finite dealer, real purchases, one cargo/vehicle, occupation and numeric labour verified')


if __name__=='__main__':
    import sys
    for folder in sys.argv[1:]:verify_fleet(folder)
