#!/usr/bin/env python3
"""Independent arithmetic checks of runner exports. Author: Шелестов Максим Евгеньевич."""
import csv
import json
from pathlib import Path
import sys

def verify(folder):
    folder = Path(folder)
    summary = json.loads((folder/'summary.json').read_text(encoding='utf-8'))
    if summary['status'] != 'succeeded':
        raise ValueError(f'{folder}: runner did not succeed')
    money = sum(account['balanceMinor'] for account in summary['accounts'])
    assert money == summary['initialMoneyMinor'] + summary['externalMoneyInMinor'] - summary['externalMoneyOutMinor']
    state = json.loads((folder/'final-state.json').read_text(encoding='utf-8'))
    groups = state['employmentGroups']
    assert state['population'] == state['unemployed'] + sum(g['headcount'] for g in groups)
    assert all(0 <= g['headcount'] <= g['capacity'] for g in groups)
    assert state['populationGrowth'] >= 0
    with (folder/'metrics.csv').open(newline='', encoding='utf-8') as f:
        samples = {int(r['tick']): r for r in csv.DictReader(f)}
    employed = {tick: 0 for tick in samples}
    with (folder/'labor.csv').open(newline='', encoding='utf-8') as f:
        seen = set()
        for row in csv.DictReader(f):
            tick, group, n, cap = map(int, (row['tick'], row['group_id'], row['headcount'], row['capacity']))
            assert (tick, group) not in seen and 0 <= n <= cap
            seen.add((tick, group)); employed[tick] += n
    for tick, sample in samples.items():
        assert int(sample['population']) == int(sample['unemployed']) + employed[tick]
    for flow in summary['flows']:
        physical = sum(stock['quantity'] for inventory in state['inventories'] for stock in inventory['stock'] if stock['itemId']==flow['itemId'])
        assert physical == flow['initial']+flow['harvested']+flow['produced']-flow['recipeInput']-flow['exported']-flow.get('constructionInput',0)
    for inv in state['inventories']:
        assert inv['occupiedVolume']+inv['reservedCapacity'] <= inv['capacity']
        assert all(0 <= item['reserved'] <= item['quantity'] for item in inv['stock'])
    tape = [json.loads(line) for line in (folder/'commands.jsonl').read_text(encoding='utf-8').splitlines()]
    assert tape, 'No real commands recorded'
    ledger = [json.loads(line) for line in (folder/'ledger.jsonl').read_text(encoding='utf-8').splitlines()]
    assert sum(entry['amountMinor'] for entry in ledger if entry['reason']=='CapitalSettlement') == sum(s['paidMinor'] for s in summary['settlements'])
    assert all(0 <= a['reservedMinor'] <= a['balanceMinor'] for a in state['accounts'])
    accounts = {a['id']['value']: a for a in state['accounts']}
    inventories = {i['id']['value']: i for i in state['inventories']}
    allocations = state.get('marketAllocations', [])
    assert len({a['id'] for a in allocations}) == len(allocations)
    assert len({a['escrowAccount']['value'] for a in allocations}) == len(allocations)
    for allocation in allocations:
        assert allocation['status'] == 'AwaitingPickup'
        assert allocation['seller'] != allocation['buyer'] and allocation['quantity'] > 0
        escrow = accounts[allocation['escrowAccount']['value']]
        assert escrow['kind'] == 'ContractEscrow'
        assert escrow['balanceMinor'] == allocation['quantity'] * allocation['unitPriceMinor']
        source = inventories[allocation['sourceInventory']['value']]
        assert source['ownerId'] == allocation['seller']
        assert sum(s['reserved'] for s in source['stock'] if s['itemId'] == allocation['itemId']) >= allocation['quantity']
    shops = {b['id']['value']: b['inputId']['value'] for b in state['buildings']}
    for inv in state['inventories']:
        for stock in inv['stock']:
            claims = sum(a['quantity'] for a in allocations if a['sourceInventory']['value'] == inv['id']['value'] and a['itemId'] == stock['itemId'])
            lots = sum(o['quantity'] for o in state.get('marketOffers', []) if o['status'] != 'Closed' and shops[o['shop']['value']] == inv['id']['value'] and o['itemId'] == stock['itemId'])
            assert claims + lots <= stock['reserved'] <= stock['quantity']
    assert sum(l['amountMinor'] for l in ledger if l['reason'] == 'MarketEscrow') == sum(a['quantity'] * a['unitPriceMinor'] for a in allocations)
    for offer in state.get('marketOffers', []):
        assert sum(a['quantity'] for a in allocations if a['offerId'] == offer['id']) == offer['allocatedQuantity'] <= offer['quantity']
    print(f'{folder}: money, item flows, capacity, population and settlements verified; tick={summary["tick"]}')

if len(sys.argv) < 2:
    raise SystemExit('Usage: verify_results.py RUN_DIRECTORY [RUN_DIRECTORY]')
for folder in sys.argv[1:]:
    verify(folder)
