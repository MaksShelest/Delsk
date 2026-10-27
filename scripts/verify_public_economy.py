#!/usr/bin/env python3
"""Audit exported public accounting; does not execute gameplay or NPC policy."""
import json
from pathlib import Path
import sys

def verify(folder):
    folder = Path(folder)
    from billing_audit import capital
    claims,debts=capital(folder)
    city = json.loads((folder/'public-economy.json').read_text())
    state = json.loads((folder/'final-state.json').read_text())
    config = json.loads((folder/'scenario.json').read_text())
    physical = json.loads((folder/'physical-state.json').read_text())
    prices = {i['id']:i['basePriceMinor'] for i in config['items']}
    accounts = {a['id']['value']:a['balanceMinor'] for a in state['accounts']}
    assert city['valuationVersion'] == 'capital-reference-v1'
    assert city['population'] == city['employed'] + city['unemployed'] == state['population']
    assert city['employed'] == sum(g['headcount'] for g in state['employmentGroups'])
    assert city['labor']['employedRate'] == {'weightedMinor':sum(g['headcount']*g['wageMinor'] for g in state['employmentGroups']), 'weight':city['employed']}
    assert city['labor']['vacancyRate'] == {'weightedMinor':sum(o['vacancies']*o['attractionWageMinor'] for o in city['labor']['offers']), 'weight':sum(o['vacancies'] for o in city['labor']['offers'])}
    assert city['municipalMinor'] == sum(a['balanceMinor'] for a in state['accounts'] if a['kind']=='Treasury')+claims[0]
    assert city['capitalMinor'] == city['municipalMinor'] + sum(p['capitalMinor'] for p in city['participants'])
    assert len({p['actor']['value'] for p in city['participants']}) == len(config['participants'])
    types = {b['id']:b for b in config.get('buildingTypes') or []}
    machines = {e['id']:e for e in config.get('equipmentTypes') or []}
    recipes = {r['id']:r for r in config['recipes']}
    for participant in city['participants']:
        assert set(participant) == {'actor','capitalMinor','employed','preferenceItem'}
        actor = participant['actor']['value']
        cash = accounts[next(a['accountId']['value'] for a in state['participants'] if a['id']['value']==actor)]
        cash += sum(accounts[a['escrowAccount']['value']] for a in state['marketAllocations'] if a['buyer']['value']==actor)
        cash += sum(accounts[d['escrowAccount']['value']] for d in state['deliveryContracts'] if d['buyer'] and d['buyer']['value']==actor)
        cash += sum(accounts[d['bondAccount']['value']] for d in state['deliveryContracts'] if d['supplier']['value']==actor)
        if (folder/'services.json').exists():
            cash += sum(accounts[o['escrowAccount']] for o in json.loads((folder/'services.json').read_text())['orders'] if o['customer']['value']==actor and o['escrowAccount'])
        stock = sum(s['quantity']*prices[s['itemId']] for i in state['inventories'] if i['ownerId']['value']==actor for s in i['stock'])
        wip = sum(sum(i['quantity']*prices[i['itemId']] for i in e['batchInputs']) for e in state['enterprises'] if e['ownerId']['value']==actor and e['batchId'])
        land = sum(p['priceMinor'] for p in state['plots'] if p['ownerId'] and p['ownerId']['value']==actor)
        land += sum(r['purchasePriceMinor'] for r in physical['resources'] if r['owner'] and r['owner']['value']==actor)
        buildings = sum(l['priceMinor']+sum(m['quantity']*prices[m['itemId']] for m in l['materials']) for b in state['buildings'] if b['ownerId']['value']==actor for l in types[b['typeId']]['levels'] if l['level']<=b['level'])
        equipment = sum(l['priceMinor'] for e in state['equipment'] if e['ownerId']['value']==actor for l in machines[e['typeId']]['levels'] if l['level']<=e['level'])
        for system in state['logistics']:
            if system['ownerId']['value']!=actor or system.get('vehicleId',0): continue
            settings=config.get('logistics') or {'purchasePriceMinor':20000,'upgrade':None}
            equipment+=settings['purchasePriceMinor']
            if system['level']>1:
                upgrade=settings['upgrade'];equipment+=upgrade['priceMinor']+sum(m['quantity']*prices[m['itemId']] for m in upgrade['materials'])
        if (folder/'fleet.json').exists():
            fleet=json.loads((folder/'fleet.json').read_text());rules=config['economy']['fleet']
            models={m['id']:m for m in rules['models']}
            journal=[json.loads(line) for line in (folder/'events.jsonl').read_text().splitlines()]
            for vehicle in fleet['vehicles']:
                if vehicle['owner']['value']!=actor:continue
                equipment+=models[vehicle['modelId']]['priceMinor']
                jobs=[e for e in journal if e['type']=='VehicleKitStarted' and e['entityId']==vehicle['id']]
                commands={e['commandId'] for e in jobs}
                equipment+=sum(e['amountMinor'] for e in jobs)+sum(e['quantity']*prices[e['itemId']] for e in journal if e['type']=='ConstructionMaterialConsumed' and e.get('commandId') in commands)
            for platform in fleet['platforms']:
                if platform['group']['ownerId']['value']!=actor:continue
                if not any(b['id']['value']==platform['building'] for b in state['buildings']):continue
                target=platform.get('installation',{});target=target['level'] if target else 0
                buildings+=sum(u['priceMinor']+sum(m['quantity']*prices[m['itemId']] for m in u['materials']) for u in rules['platforms'] if u['level']<=platform['level'] or u['level']==target)
        if (folder/'roads.json').exists():
            roads=json.loads((folder/'roads.json').read_text()); stages={r['level']:r for r in config['economy']['roads']['stages']}
            def road_cost(level):
                r=stages[level];return r['priceMinor']+sum(m['quantity']*prices[m['itemId']] for m in r['materials'])
            buildings+=sum(road_cost(c['level']) for cell in roads['cells'] for c in cell['contributions'] if c['builder']['value']==actor)
            buildings+=sum(road_cost(w['batch']['level']) for w in roads['works'] if w['group']['ownerId']['value']==actor and w['batch'])
        assert participant['capitalMinor']==cash+stock+wip+land+buildings+equipment+claims[actor]-debts[actor], (folder,actor,'capital')
        assert participant['employed']==sum(g['headcount'] for g in state['employmentGroups'] if g['ownerId']['value']==actor)
    latest = [json.loads(line) for line in (folder/'events.jsonl').read_text().splitlines() if json.loads(line)['type']=='CityStageUpdated']
    assert city['stage']==(latest[-1]['entityId'] if latest else 0)
    print(str(folder)+': public capital/labor/consolidation/privacy shape verified')

if __name__=='__main__':
    for folder in sys.argv[1:]:verify(folder)
