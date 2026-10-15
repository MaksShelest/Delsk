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
    market_rules = json.loads((folder/'scenario.json').read_text(encoding='utf-8')).get('market') or {}
    fine_rate = market_rules.get('pickupFineBasisPoints', 500)
    for allocation in allocations:
        q, c, r, x = (allocation[k] for k in ('quantity', 'collectedQuantity', 'remainingQuantity', 'releasedQuantity'))
        price = allocation['unitPriceMinor']
        assert q == c + r + x and min(c, r, x) >= 0
        assert allocation['paidMinor'] == c * price
        assert allocation['fineMinor'] == x * price * fine_rate // 10000
        assert allocation['refundedMinor'] == x * price - allocation['fineMinor']
        assert allocation['paidMinor'] + allocation['fineMinor'] + allocation['refundedMinor'] + r * price == q * price
        if allocation['closedTick'] is None:
            assert x == 0 and r > 0 and allocation['deadlineTick'] > state['tick']
            assert allocation['status'] == ('AwaitingPickup' if c == 0 else 'PartiallyCollected')
        else:
            assert r == 0
            assert allocation['status'] == ('Fulfilled' if x == 0 else 'Expired')
            assert allocation['createdTick'] < allocation['closedTick'] <= min(allocation['deadlineTick'], state['tick'])
            if x: assert allocation['closedTick'] == allocation['deadlineTick']
        assert allocation['seller'] != allocation['buyer'] and allocation['quantity'] > 0
        escrow = accounts[allocation['escrowAccount']['value']]
        assert escrow['kind'] == 'ContractEscrow'
        assert escrow['balanceMinor'] == r * price
        source = inventories[allocation['sourceInventory']['value']]
        assert source['ownerId'] == allocation['seller']
        assert sum(s['reserved'] for s in source['stock'] if s['itemId'] == allocation['itemId']) >= r
        entries = [l for l in ledger if l['debitAccount']['value'] == allocation['escrowAccount']['value']]
        for reason, field in [('PickupPayment', 'paidMinor'), ('PickupFine', 'fineMinor'), ('PickupRefund', 'refundedMinor')]:
            assert sum(l['amountMinor'] for l in entries if l['reason'] == reason) == allocation[field]
        assert sum(l['amountMinor'] for l in entries) == allocation['paidMinor'] + allocation['fineMinor'] + allocation['refundedMinor']
    shops = {b['id']['value']: b['inputId']['value'] for b in state['buildings']}
    for inv in state['inventories']:
        for stock in inv['stock']:
            claims = sum(a['remainingQuantity'] for a in allocations if a['sourceInventory']['value'] == inv['id']['value'] and a['itemId'] == stock['itemId'])
            lots = sum(o['quantity'] for o in state.get('marketOffers', []) if o['status'] != 'Closed' and shops[o['shop']['value']] == inv['id']['value'] and o['itemId'] == stock['itemId'])
            assert claims + lots <= stock['reserved'] <= stock['quantity']
    assert sum(l['amountMinor'] for l in ledger if l['reason'] == 'MarketEscrow') == sum(a['quantity'] * a['unitPriceMinor'] for a in allocations)
    for offer in state.get('marketOffers', []):
        assert sum(a['quantity'] for a in allocations if a['offerId'] == offer['id']) == offer['allocatedQuantity'] <= offer['quantity']
    config = json.loads((folder/'scenario.json').read_text(encoding='utf-8'))
    delivery_rules = config.get('delivery') or {}
    rate = delivery_rules.get('fineBasisPoints', 500)
    contracts = state.get('deliveryContracts', [])
    people = {p['id']['value']: p['accountId']['value'] for p in state['participants']}
    items = {i['id']: i['unitVolume'] for i in config['items']}
    assert len({d['id'] for d in contracts}) == len(contracts)
    financial_ids = [a['escrowAccount']['value'] for a in allocations] + [d[k]['value'] for d in contracts for k in ['escrowAccount','bondAccount']]
    assert len(set(financial_ids)) == len(financial_ids)
    for d in contracts:
        q, delivered, remaining, unfulfilled = (d[k] for k in ['quantity','deliveredQuantity','remainingQuantity','unfulfilledQuantity'])
        price = d['unitPriceMinor']; active = d['closedTick'] is None
        assert min(delivered, remaining, unfulfilled) >= 0 and q > 0 and q == delivered + remaining + unfulfilled
        assert min(d['preparedQuantity'],d['inTransitQuantity'],d['unprovidedQuantity']) >= 0
        assert remaining == d['preparedQuantity'] + d['inTransitQuantity'] + d['unprovidedQuantity']
        assert d['paidMinor'] == delivered * price and d['refundedMinor'] == unfulfilled * price
        assert d['bondMinor'] == q * price * rate // 10000 and d['fineMinor'] == unfulfilled * price * rate // 10000
        assert d['bondReturnedMinor'] == (0 if active else d['bondMinor'] - d['fineMinor'])
        assert d['deadlineTick'] == d['createdTick'] + delivery_rules.get('contractTtlTicks', 180)
        if active:
            assert remaining > 0 and unfulfilled == 0 and d['deadlineTick'] > state['tick']
            assert d['status'] == ('Active' if delivered == 0 else 'PartiallyDelivered')
        else:
            assert remaining == 0 and d['createdTick'] < d['closedTick'] <= min(state['tick'], d['deadlineTick'])
            assert d['status'] == ('Expired' if unfulfilled else 'Fulfilled')
            if unfulfilled: assert d['closedTick'] == d['deadlineTick']
        escrow, bond = accounts[d['escrowAccount']['value']], accounts[d['bondAccount']['value']]
        assert escrow['kind'] == 'ContractEscrow' and escrow['balanceMinor'] == remaining * price
        assert bond['kind'] == 'SupplierBond' and bond['balanceMinor'] == (d['bondMinor'] if active else 0)
        assert d['paidMinor'] + d['refundedMinor'] + escrow['balanceMinor'] == q * price
        assert d['fineMinor'] + d['bondReturnedMinor'] + bond['balanceMinor'] == d['bondMinor']
        if d['buyer'] is not None:
            assert d['buyer'] != d['supplier'] and d['buyerAccount']['value'] == people[d['buyer']['value']]
            destination = inventories[d['destinationInventory']['value']]
            assert destination['ownerId'] == d['buyer'] and destination['position'] == d['position']
            assert destination['reservedCapacity'] >= remaining * items[d['itemId']]
        else:
            assert d['destinationInventory'] is None and accounts[d['buyerAccount']['value']]['kind'] == 'CapitalBudget'
        for account, fields in [(d['escrowAccount']['value'], [('DeliveryPayment','paidMinor'),('DeliveryRefund','refundedMinor')]),
                                (d['bondAccount']['value'], [('DeliveryFine','fineMinor'),('DeliveryBondReturn','bondReturnedMinor')])]:
            entries = [l for l in ledger if l['debitAccount']['value'] == account]
            assert {l['reason'] for l in entries} <= {reason for reason, _ in fields}
            for reason, field in fields: assert sum(l['amountMinor'] for l in entries if l['reason'] == reason) == d[field]
    for inventory in state['inventories']:
        claims = sum(d['remainingQuantity'] * items[d['itemId']] for d in contracts if d['destinationInventory'] == inventory['id'])
        assert claims <= inventory['reservedCapacity']
    for offer in state.get('buyOffers', []):
        assert offer['quantity'] == offer['allocatedQuantity'] + offer['remainingQuantity'] + offer['releasedQuantity']
        assert offer['allocatedQuantity'] == sum(d['quantity'] for d in contracts if d['buyOfferId'] == offer['id'])
    capital_orders = {o['id']: o for o in config['capitalOffers']}
    for offer in state['capitalOffers']:
        assert capital_orders[offer['id']]['quantity'] == offer['remainingQuantity'] + sum(s['accepted'] for s in state['settlements'] if s['offerId'] == offer['id']) + sum(d['quantity'] for d in contracts if d['capitalOfferId'] == offer['id'])
    assert sum(l['amountMinor'] for l in ledger if l['reason'] == 'DeliveryFunding') == sum(d['quantity'] * d['unitPriceMinor'] for d in contracts)
    assert sum(l['amountMinor'] for l in ledger if l['reason'] == 'DeliveryBond') == sum(d['bondMinor'] for d in contracts)
    systems = state.get('logistics', [])
    assert len({l['enterpriseId']['value'] for l in systems}) == len(systems)
    for system in systems:
        cargo = inventories[system['cargoInventory']['value']]
        assert cargo['ownerId'] == system['ownerId']
        rules = config.get('logistics') or {'cargoCapacity':10, 'crewCapacity':2}
        level = system['level']; assert level in [1, 2]
        characteristics = rules if level == 1 else rules['upgrade']
        assert cargo['capacity'] == characteristics['cargoCapacity']
        group = next(g for g in state['employmentGroups'] if g['id'] == system['groupId'])
        assert group['capacity'] == characteristics['crewCapacity']
        if system['trip'] is None: assert not cargo['stock']
        else:
            trip = system['trip']; assert trip['position'] == cargo['position']
            q = trip['quantity'] if trip['state'] in ['Outbound','ReturningCargo'] else 0
            assert sum(s['quantity'] for s in cargo['stock']) == q
            assert sum(s['reserved'] for s in cargo['stock']) == q
            owner = accounts[people[system['ownerId']['value']]]
            assert owner['reservedMinor'] >= trip['wageBudgetMinor'] - trip['wagesPaidMinor'] >= 0
    events = [json.loads(line) for line in (folder/'events.jsonl').read_text(encoding='utf-8').splitlines()]
    assert sum(l['amountMinor'] for l in ledger if l['reason'] == 'LogisticsWages') == sum(e['amountMinor'] for e in events if e['type'] == 'LogisticsWagesPaid')
    for d in contracts:
        active_trips = [l['trip'] for l in systems if l['trip'] and l['trip']['contractId'] == d['id']]
        if d['closedTick'] is None:
            assert d['preparedQuantity'] == sum(t['quantity'] for t in active_trips if t['state'] == 'Loading')
            assert d['inTransitQuantity'] == sum(t['quantity'] for t in active_trips if t['state'] == 'Outbound')
    print(f'{folder}: money, item flows, capacity, population and settlements verified; tick={summary["tick"]}')

if len(sys.argv) < 2:
    raise SystemExit('Usage: verify_results.py RUN_DIRECTORY [RUN_DIRECTORY]')
for folder in sys.argv[1:]:
    verify(folder)
