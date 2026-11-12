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
    version = rules.get('equipmentDealerVersion', 0)
    assert version in (0, 1, 2), 'Unknown dealer version'
    assert len(models) == len(rules['models']) == (5, 21, 9)[version], 'Wrong vehicle catalog for dealer version'
    compact = 'compact-exhibition-1' in (config.get('layout') or {}).get('assignment', '')
    if version == 2:
        expected_initial = Counter(('sputnik20', 'muravey70', 'extractor_field_1', 'extractor_forest_1'))
    elif compact:
        expected_initial = Counter(('sputnik20', 'muravey70', 'eger70', 'trakt_d1', 'extractor_extract_wood_1', 'extractor_extract_grain_1'))
    else:
        expected_initial = Counter(sputnik20=1, muravey70=2, eger70=1, vector200=1, trakt_d1=1)
        if version == 1:
            expected_initial.update(m['id'] for m in models.values() if m.get('equipmentTypeId'))
    stock, purchased, catalog_requests = {}, {}, {}
    initial, upgrades = Counter(), Counter()
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
            assert len(stock) <= (6, 22, 5)[version]
            if version == 2:
                listed = [models[model_id] for model_id, _ in stock.values()]
                extractors = sum(bool(model.get('equipmentTypeId')) for model in listed)
                pavers = sum(model['materialsOnly'] for model in listed)
                assert extractors <= 2 and pavers <= 1 and len(listed) - extractors - pavers <= 2
        elif kind=='DealerReplaced':
            assert identity in stock and identity not in purchased
            assert tick-stock[identity][1]>=rules['minimumStockTicks']
            del stock[identity]
        elif kind == 'VehicleCatalogRequested':
            assert version == 2 and event['commandId'] not in catalog_requests
            assert event['itemId'] in models and all(model_id != event['itemId'] for model_id, _ in stock.values())
            catalog_requests[event['commandId']] = event
        elif kind == 'VehiclePurchased':
            assert identity not in purchased
            if identity in stock:
                model_id, _ = stock.pop(identity)
            else:
                assert version == 2 and event.get('commandId') in catalog_requests, 'Purchase without a listing or catalog request'
                request = catalog_requests.pop(event['commandId'])
                assert request['actorId'] == event['actorId'] and request['tick'] < tick and tick % rules['supplyTicks'] == 0
                model_id = request['itemId']
            assert event.get('itemId', model_id) == model_id and event['amountMinor'] == models[model_id]['priceMinor']
            purchased[identity] = (event['actorId']['value'], model_id, tick, event['amountMinor'])
        elif kind == 'EquipmentUpgraded' and identity in purchased:
            assert version == 2 and models[purchased[identity][1]].get('equipmentTypeId')
            upgrades[identity] += 1
    assert initial==expected_initial
    assert stock=={s['id']:(s['modelId'],s['deliveredTick']) for s in fleet['stock']}
    assert len(purchased)==len(fleet['vehicles'])==len(fleet['purchases'])
    assert sum(p[3] for p in purchased.values())==sum(l['amountMinor'] for l in ledger if l['reason']=='VehiclePurchase')
    cargos=set();drivers=set();bound=set()
    for vehicle in fleet['vehicles']:
        identity=vehicle['id'];owner=vehicle['owner']['value'];model=models[vehicle['modelId']]
        assert purchased[identity][:2]==(owner,model['id'])
        cargo_id=vehicle['cargoInventory']['value'];assert cargo_id not in cargos;cargos.add(cargo_id)
        cargo=inventory[cargo_id]
        capacity = model['capacityVolume']
        if model['materialsOnly']:assert all(s['itemId'] in ('gravel','asphalt','concrete') for s in cargo['stock'])
        if model.get('equipmentTypeId'):
            machine = next(e for e in state['equipment'] if e['id']['value'] == identity)
            type_def = next(t for t in config['equipmentTypes'] if t['id'] == model['equipmentTypeId'])
            level = model['equipmentLevel'] + upgrades[identity]
            assert machine['typeId'] == type_def['id'] and machine['level'] == level
            if version == 2:
                capacity = next(l['storageCapacity'] for l in type_def['levels'] if l['level'] == level)
            assert machine['storageId']==vehicle['cargoInventory'] and machine['position']==cargo['position']
            assert machine['ownerId']==vehicle['owner'] and isinstance(machine['deployed'],bool)
            compatible = type_def.get('items') or [type_def['itemId']]
            assert all(s['itemId'] in compatible for s in cargo['stock'])
        assert cargo['ownerId']['value'] == owner and cargo['capacity'] == capacity
        assert cargo['occupiedVolume'] + cargo['reservedCapacity'] <= capacity
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
