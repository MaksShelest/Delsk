"""Independent audit of finite import stock, receipts and physical pickup.
Author: Шелестов Максим Евгеньевич.
"""
from collections import Counter


def verify_imports(config, state, content, events, ledger, tape, public, modifications=()):
    rules = (config.get('economy') or {}).get('productionContent')
    imported = [e for e in events if e['type'] == 'ImportPurchased']
    restocked = [e for e in events if e['type'] == 'ImportRestocked']
    payments = [l for l in ledger if l['reason'] == 'ImportPurchase']
    if rules is None:
        assert content is None and not imported and not restocked and not payments, 'Unexpected import state'
        assert all(f.get('imported', 0) == 0 for f in state['flows']), 'Unexpected import flow'
        return 0

    assert content is not None, 'Missing import export'
    supplies = {r['itemId']: r for r in rules['imports']}
    assert len(supplies) == len(rules['imports']) and supplies, 'Duplicate/empty supply rules'
    remaining = {item: r['package'] for item, r in supplies.items()}
    next_tick = {item: r['refreshTicks'] for item, r in supplies.items()}
    assert all(r['package'] > 0 and r['refreshTicks'] > 0 and r['unitPriceMinor'] > 0 for r in supplies.values())
    people = {p['id']['value']: p for p in state['participants']}
    inventories = {i['id']['value']: i for i in state['inventories']}
    pickups = {p['owner']['value']: p['inventory']['value'] for p in content['pickups']}
    assert len(pickups) == len(content['pickups']) == len(set(pickups.values())), 'Duplicate pickup section'
    commands = {r['command']['commandId']: r for r in tape}
    terminal = config['capitalDemand']['terminal']
    building_types = {b['id']: b['typeId'] for b in config.get('buildings') or []}
    building_types.update({b['id']['value']: b['typeId'] for b in state.get('buildings', [])})
    for e in events:
        if e['type'] == 'BuildingConstructed': building_types[e['entityId']] = e['reasonCode']
    asphalt_cutoff = 0 if any(b['typeId'] == 'materials' and b['level'] >= 4 for b in config.get('buildings') or []) else None
    for e in events:
        if e['type'] == 'BuildingUpgraded' and e['reasonCode'] in ('4', '5') and building_types.get(e['entityId']) == 'materials':
            asphalt_cutoff = min(asphalt_cutoff, e['tick']) if asphalt_cutoff is not None else e['tick']
    for owner, inventory in pickups.items():
        assert owner in people and inventory != people[owner]['inventoryId']['value'], 'Invalid pickup owner'
        section = inventories[inventory]
        assert section['ownerId']['value'] == owner and section['position'] == terminal, 'Invalid pickup location/rights'

    totals, owned, paid = Counter(), Counter(), Counter()
    seen = set()
    for e in events:
        if e['type'] not in ('ImportPurchased', 'ImportRestocked'):
            continue
        item = e['itemId']
        assert item in supplies, 'Unknown imported item'
        r = supplies[item]
        if e['type'] == 'ImportRestocked':
            assert e['tick'] == next_tick[item] and e['phase'] == 0, 'Missing/duplicate/early refill'
            addition = 0 if item == 'asphalt' and asphalt_cutoff is not None and e['tick'] >= asphalt_cutoff else min(r['package'], 2 * r['package'] - remaining[item])
            assert e['quantity'] == addition and e['amountMinor'] == 0, 'Unbounded refill'
            remaining[item] += addition
            next_tick[item] += r['refreshTicks']
            continue
        owner, command_id = e['actorId']['value'], e['commandId']
        assert command_id not in seen, 'Duplicate import receipt'
        seen.add(command_id)
        record = commands[command_id]
        command, action = record['command'], record['command']['action']
        assert record['authenticatedActor']['value'] == command['actorId']['value'] == owner, 'Wrong importer'
        assert action['kind'] == 'BuyImport' and action['itemId'] == item and action['quantity'] == e['quantity'], 'Receipt differs from command'
        assert command['notBeforeTick'] <= e['tick'] <= command['expiresAtTick'], 'Stale import receipt'
        assert e['phase'] == 1 and e['tick'] < next_tick[item], 'Invalid purchase phase/refill order'
        assert 0 < e['quantity'] <= remaining[item], 'Import oversold'
        assert owner in pickups and e['entityId'] == pickups[owner], 'Wrong pickup section'
        amount = e['quantity'] * r['unitPriceMinor']
        assert e['amountMinor'] == amount, 'Wrong import price'
        paid[(e['tick'], people[owner]['accountId']['value'])] += amount
        totals[item] += e['quantity']
        owned[(pickups[owner], item)] += e['quantity']
        remaining[item] -= e['quantity']

    exported = {s['itemId']: s for s in content['imports']}
    assert len(exported) == len(content['imports']) == len(supplies), 'Invalid stock export'
    for item in supplies:
        assert exported[item]['remaining'] == remaining[item] and exported[item]['nextRefreshTick'] == next_tick[item], 'Incorrect remaining stock/deadline'
        assert 0 <= remaining[item] <= 2 * supplies[item]['package'] and next_tick[item] > state['tick'], 'Missing refill or exceeded cap'
    assert set(pickups) == {e['actorId']['value'] for e in imported}, 'Unpaid pickup section'
    assert all(f.get('imported', 0) == totals[f['itemId']] for f in state['flows']), 'Import flow differs from receipts'
    actual_paid = Counter()
    for l in payments:
        assert l['creditAccount']['value'] == 0 and l['amountMinor'] > 0, 'Import must pay the external supplier'
        actual_paid[(l['tick'], l['debitAccount']['value'])] += l['amountMinor']
    assert paid == actual_paid, 'Import payment missing, doubled or charged to another account'

    # Paid goods remain at the terminal until physical transfers actually finish.
    sections = set(pickups.values())
    for e in events:
        if e['type'] != 'StockTransferred':
            continue
        command = commands[e['commandId']]['command']
        a = command['action']
        if a['source']['value'] in sections:
            section = a['source']['value']
            owner = inventories[section]['ownerId']['value']
            assert command['actorId']['value'] == owner and a['destination'] == people[owner]['inventoryId']
            assert a['itemId'] == e['itemId'] and e['quantity'] > 0, 'Invalid physical import pickup'
            owned[(section, e['itemId'])] -= e['quantity']
    # Service events refer to frozen authenticated order terms, not a second stock source.
    if (config.get('economy') or {}).get('serviceExecution'):
        orders={}
        for record in tape:
            action=record['command']['action']
            if action['kind']=='RequestDeliveryService':orders[record['command']['commandId']]=action
        service_sources={e['entityId']:orders[e['commandId']]['source']['value'] for e in events if e['type']=='ServiceRequested'}
        for e in events:
            source=service_sources.get(e['entityId'])
            if source in sections and e['type'] in ('ServiceCargoLoaded','ServiceCargoReturned'):
                owned[(source,e['itemId'])]+=e['quantity']*(-1 if e['type']=='ServiceCargoLoaded' else 1)
    for m in modifications:
        if m['kind'] == 'Stock' and m['inventory']['value'] in sections:
            owned[(m['inventory']['value'], m['item'])] += m['newValue'] - m['oldValue']
    for section in sections:
        actual = {s['itemId']: s['quantity'] for s in inventories[section]['stock']}
        for item in set(actual) | {item for inventory, item in owned if inventory == section}:
            assert actual.get(item, 0) == owned[(section, item)] >= 0, 'Paid import goods lost, duplicated or teleported'
    assert not any(e['type'] == 'ImportPurchased' for e in public), 'Private import receipt in public events'
    return sum(paid.values())
