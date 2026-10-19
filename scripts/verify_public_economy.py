#!/usr/bin/env python3
"""Audit exported public accounting; does not execute gameplay or NPC policy."""
import json
from pathlib import Path
import sys

def verify(folder):
    folder = Path(folder)
    city = json.loads((folder/'public-economy.json').read_text())
    state = json.loads((folder/'final-state.json').read_text())
    config = json.loads((folder/'scenario.json').read_text())
    prices = {i['id']:i['basePriceMinor'] for i in config['items']}
    accounts = {a['id']['value']:a['balanceMinor'] for a in state['accounts']}
    assert city['valuationVersion'] == 'capital-reference-v1'
    assert city['population'] == city['employed'] + city['unemployed'] == state['population']
    assert city['employed'] == sum(g['headcount'] for g in state['employmentGroups'])
    assert city['labor']['employedRate'] == {'weightedMinor':sum(g['headcount']*g['wageMinor'] for g in state['employmentGroups']), 'weight':city['employed']}
    assert city['labor']['vacancyRate'] == {'weightedMinor':sum(o['vacancies']*o['attractionWageMinor'] for o in city['labor']['offers']), 'weight':sum(o['vacancies'] for o in city['labor']['offers'])}
    assert city['municipalMinor'] == sum(a['balanceMinor'] for a in state['accounts'] if a['kind']=='Treasury')
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
        stock = sum(s['quantity']*prices[s['itemId']] for i in state['inventories'] if i['ownerId']['value']==actor for s in i['stock'])
        wip = sum(recipes[e['recipeId']]['inputQuantity']*prices[recipes[e['recipeId']]['inputItem']] for e in state['enterprises'] if e['ownerId']['value']==actor and e['batchId'])
        land = sum(p['priceMinor'] for p in state['plots'] if p['ownerId'] and p['ownerId']['value']==actor)
        buildings = sum(l['priceMinor']+sum(m['quantity']*prices[m['itemId']] for m in l['materials']) for b in state['buildings'] if b['ownerId']['value']==actor for l in types[b['typeId']]['levels'] if l['level']<=b['level'])
        equipment = sum(l['priceMinor'] for e in state['equipment'] if e['ownerId']['value']==actor for l in machines[e['typeId']]['levels'] if l['level']<=e['level'])
        for system in state['logistics']:
            if system['ownerId']['value']!=actor: continue
            settings=config.get('logistics') or {'purchasePriceMinor':20000,'upgrade':None}
            equipment+=settings['purchasePriceMinor']
            if system['level']>1:
                upgrade=settings['upgrade'];equipment+=upgrade['priceMinor']+sum(m['quantity']*prices[m['itemId']] for m in upgrade['materials'])
        assert participant['capitalMinor']==cash+stock+wip+land+buildings+equipment, (folder,actor,'capital')
        assert participant['employed']==sum(g['headcount'] for g in state['employmentGroups'] if g['ownerId']['value']==actor)
    latest = [json.loads(line) for line in (folder/'events.jsonl').read_text().splitlines() if json.loads(line)['type']=='CityStageUpdated']
    assert city['stage']==(latest[-1]['entityId'] if latest else 0)
    print(str(folder)+': public capital/labor/consolidation/privacy shape verified')

if __name__=='__main__':
    for folder in sys.argv[1:]:verify(folder)
