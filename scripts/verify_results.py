#!/usr/bin/env python3
"""Independent arithmetic checks of runner exports. Author: Шелестов Максим Евгеньевич."""
import csv
import json
from pathlib import Path
import sys

def verify(folder):
    folder = Path(folder)
    summary = json.loads((folder/'summary.json').read_text(encoding='utf-8'))
    modifications = json.loads((folder/'research-modifications.json').read_text()) if (folder/'research-modifications.json').exists() else []
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
        # A valid research treatment may have no workplaces at all; its workforce snapshot is empty.
        labor_by_tick = {tick: {} for tick in samples}
        for row in csv.DictReader(f):
            tick, group, n, cap = map(int, (row['tick'], row['group_id'], row['headcount'], row['capacity']))
            assert (tick, group) not in seen and 0 <= n <= cap
            seen.add((tick, group)); employed[tick] += n
            labor_by_tick.setdefault(tick, {})[group] = (int(row["owner_id"]), n)
    for tick, sample in samples.items():
        assert int(sample['population']) == int(sample['unemployed']) + employed[tick]
    for flow in summary['flows']:
        physical = sum(stock['quantity'] for inventory in state['inventories'] for stock in inventory['stock'] if stock['itemId']==flow['itemId'])
        assert physical == flow['initial']+flow['harvested']+flow['produced']+sum(m['newValue']-m['oldValue'] for m in modifications if m['kind']=='Stock' and m['item']==flow['itemId'])-flow['recipeInput']-flow['exported']-flow.get('constructionInput',0)
    for inv in state['inventories']:
        assert inv['occupiedVolume']+inv['reservedCapacity'] <= inv['capacity']
        assert all(0 <= item['reserved'] <= item['quantity'] for item in inv['stock'])
    tape = [json.loads(line) for line in (folder/'commands.jsonl').read_text(encoding='utf-8').splitlines()]
    assert tape or json.loads((folder/'scenario.json').read_text(encoding='utf-8')).get('economyProfileId'), 'No real commands recorded'
    ledger = [json.loads(line) for line in (folder/'ledger.jsonl').read_text(encoding='utf-8').splitlines()]
    assert sum(entry['amountMinor'] for entry in ledger if entry['reason']=='CapitalSettlement') == sum(s['paidMinor'] for s in summary['settlements'])
    assert all(0 <= a['reservedMinor'] <= a['balanceMinor'] for a in state['accounts'])
    accounts = {a['id']['value']: a for a in state['accounts']}
    inventories = {i['id']['value']: i for i in state['inventories']}
    negotiations=json.loads((folder/'negotiations.json').read_text())
    talks={n['id']:n for n in negotiations}
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
            lots = sum(o['quantity'] for o in state.get('marketOffers', []) if o['status'] != 'Closed' and o['sourceInventory']['value'] == inv['id']['value'] and o['itemId'] == stock['itemId'])
            assert claims + lots <= stock['reserved'] <= stock['quantity']
    assert sum(l['amountMinor'] for l in ledger if l['reason'] == 'MarketEscrow') == sum(a['quantity'] * a['unitPriceMinor'] for a in allocations)
    for offer in state.get('marketOffers', []):
        assert sum(a['quantity'] for a in allocations if a['offerId'] == offer['id']) == offer['allocatedQuantity'] <= offer['quantity']
    config = json.loads((folder/'scenario.json').read_text(encoding='utf-8'))
    recipes = {r['id']:r for r in config['recipes']}
    def ingredients(r):
        return r.get('inputs', [{'itemId':r.get('inputItem'), 'quantity':r.get('inputQuantity')}])
    for e in state['enterprises']:
        if e['batchId']:
            assert e['batchRecipeId'] == e['recipeId']
            assert e['batchInputs'] == ingredients(recipes[e['batchRecipeId']])
        else:
            assert e['batchRecipeId'] == '' and e['batchInputs'] == []
    starts = [json.loads(line) for line in (folder/'events.jsonl').read_text().splitlines() if json.loads(line)['type']=='ProductionBatchStarted']
    for flow in summary['flows']:
        assert flow['recipeInput'] == sum(e['quantity'] for e in starts if e['itemId']==flow['itemId'])
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
        assert d['deadlineTick'] == d['createdTick'] + (talks[d['negotiationId']]['revisions'][-1]['terms']['fulfillmentTicks'] if d.get('negotiationId') else delivery_rules.get('contractTtlTicks', 180))
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
        original = capital_orders.get(offer['id'], {}).get('quantity', offer['originalQuantity'])
        assert original == offer['remainingQuantity'] + offer['releasedQuantity'] + sum(s['accepted'] for s in state['settlements'] if s['offerId'] == offer['id']) + sum(d['quantity'] for d in contracts if d['capitalOfferId'] == offer['id'])
    demand = state.get('capitalDemand')
    assert json.loads((folder/'capital-demand.json').read_text(encoding='utf-8')) == demand
    if demand is not None:
        rules = config['capitalDemand']; periods = demand['periods']; bases = {i['id']: i['basePriceMinor'] for i in config['items']}
        assert len(periods) == state['tick'] // rules['periodTicks'] + 1
        smooth = carry = 0
        raw = {n['itemId'] for n in config['resourceNodes']}
        for number, period in enumerate(periods):
            assert period['period'] == number and period['tick'] == number * rules['periodTicks']
            goods = period['goods']
            gross = sum((g['harvested'] + g['produced']) * bases[g['itemId']] for g in goods)
            consumed = sum(g['consumed'] * bases[g['itemId']] for g in goods)
            stock_delta = sum(g['stockChange'] * bases[g['itemId']] for g in goods)
            assert gross == period['grossOutputBaseMinor'] and consumed == period['internalConsumptionBaseMinor'] and stock_delta == period['stockChangeBaseMinor']
            assert all(g['stockChange'] == g['harvested'] + g['produced'] - g['consumed'] - g['exported'] for g in goods)
            smooth, carry = divmod(smooth + gross + carry, 2)
            assert smooth == period['smoothedOutputBaseMinor'] and carry == period['smoothingRemainder']
            assert period['targetBaseMinor'] == max(rules['bootstrapBaseBudgetMinor'], smooth * rules['demandNumerator'] // rules['demandDenominator'])
            ordered = sum(g['demandQuantity'] * bases[g['itemId']] for g in goods)
            assert ordered == period['orderedBaseMinor'] and 0 <= period['targetBaseMinor'] - ordered < min(bases.values())
            assert all(g['demandQuantity'] >= 1 for g in goods if g['itemId'] in raw)
            monetary = sum(g['demandQuantity'] * g['unitPriceMinor'] for g in goods)
            assert monetary == period['monetaryBudgetMinor'] and 0 <= period['externalFundingMinor'] <= monetary
            emissions = [l for l in ledger if l['reason'] == 'CapitalDemandBudget' and l['tick'] == period['tick']]
            assert len(emissions) <= 1 and sum(l['amountMinor'] for l in emissions) == period['externalFundingMinor']
            assert sum(o['originalQuantity'] * o['unitPriceMinor'] for o in state['capitalOffers'] if o['createdTick'] == period['tick']) == monetary
        assert sum(p['externalFundingMinor'] for p in periods) == state['externalMoneyInMinor']
        assert all(l['tick'] % rules['periodTicks'] == 0 for l in ledger if l['reason'] == 'CapitalDemandBudget')
        assert sum(o['releasedQuantity'] * o['unitPriceMinor'] for o in state['capitalOffers']) == state['externalMoneyOutMinor']
    assert sum(l['amountMinor'] for l in ledger if l['reason'] == 'DeliveryFunding') == sum(d['quantity'] * d['unitPriceMinor'] for d in contracts)
    assert sum(l['amountMinor'] for l in ledger if l['reason'] == 'DeliveryBond') == sum(d['bondMinor'] for d in contracts)
    systems = state.get('logistics', [])
    assert len({l['enterpriseId']['value'] for l in systems}) == len(systems)
    for system in systems:
        queue = system['queue']
        assert len(queue) <= 64 and len({j['key'] for j in queue}) == len(queue)
        assert 0 <= system['completedTrips'] <= system['dispatchCount'] <= state['tick']
        assert all(0 <= j['lastServed'] <= system['dispatchCount'] for j in queue)
        cargo = inventories[system['cargoInventory']['value']]
        assert cargo['ownerId'] == system['ownerId']
        rules = config.get('logistics') or {'cargoCapacity':10, 'crewCapacity':2}
        level = system['level']; assert level in [1, 2]
        characteristics = rules if level == 1 else rules['upgrade']
        assert cargo['capacity'] == characteristics['cargoCapacity']
        own = system['ownDestination']['value']
        if own:
            assert system['contractId'] == 0
            building = next(b for b in state['buildings'] if b['inputId']['value'] == own)
            assert building['ownerId'] == system['ownerId'] and building['kind'] in ['Warehouse', 'Shop', 'Production']
            assert building['id'] != system['enterpriseId']
        group = next(g for g in state['employmentGroups'] if g['id'] == system['groupId'])
        assert group['capacity'] == characteristics['crewCapacity']
        if system['trip'] is None: assert not cargo['stock']
        else:
            trip = system['trip']; assert trip['position'] == cargo['position']
            job = next(j for j in queue if j['key'] == trip['jobKey'])
            assert job['contractId'] == trip['contractId'] and job['lastServed'] == system['dispatchCount']
            f=trip['freight']; assert f is not None
            assert min(f['loaded'],f['received'],f['returned'],f['cancelled'])>=0
            assert f['loaded']+f['cancelled']<=trip['quantity']
            q=f['loaded']-f['received']-f['returned']; assert q>=0
            assert sum(s['quantity'] for s in cargo['stock'])==q
            assert sum(s['reserved'] for s in cargo['stock'])==q
            source=inventories[f['goodsSource']['value']]
            assert source['ownerId']==system['ownerId'] and source['reservedCapacity']>=q*items[trip['itemId']]
            if trip['contractId']==0:
                destination=inventories[f['destination']['value']]
                assert destination['ownerId']==system['ownerId']
                assert destination['reservedCapacity']>=(trip['quantity']-f['received']-f['cancelled'])*items[trip['itemId']]
            elif trip['state'] in ['ReturningCargo','UnloadingReturn'] or f['returned']:
                contract=next(d for d in contracts if d['id']==trip['contractId']); assert contract['status']=='Expired'
            assert 0<=f['workRemainder']<items[trip['itemId']]
            assert 0<=f['lastWorkedTick']<=state['tick'] and trip['preparedTick']<=f['stageStartedTick']<=state['tick']
            assert group['wageMinor'] == trip['committedWageMinor']
            if group['payEffectiveTick'] is not None:
                assert group['payEffectiveTick'] >= max(state['tick']+1,trip['endTick']+1)
            owner = accounts[people[system['ownerId']['value']]]
            assert owner['reservedMinor'] >= trip['wageBudgetMinor'] - trip['wagesPaidMinor'] >= 0
    events = [json.loads(line) for line in (folder/'events.jsonl').read_text(encoding='utf-8').splitlines()]
    assert sum(l['amountMinor'] for l in ledger if l['reason'] == 'LogisticsWages') == sum(e['amountMinor'] for e in events if e['type'] == 'LogisticsWagesPaid')
    for d in contracts:
        active_trips = [l['trip'] for l in systems if l['trip'] and l['trip']['contractId'] == d['id']]
        if d['closedTick'] is None:
            physical=json.loads((folder/'physical-state.json').read_text())
            assert d['preparedQuantity'] == sum(t['quantity']-t['freight']['loaded']-t['freight']['cancelled'] for t in active_trips)+sum(t['remaining'] for t in physical['transfers'] if t['kind']=='Delivery' and t['contract']==d['id'])
            assert d['inTransitQuantity'] == sum(t['freight']['loaded']-t['freight']['received']-t['freight']['returned'] for t in active_trips)
    physical=json.loads((folder/'physical-state.json').read_text())
    assert len({(r['position']['x'],r['position']['z'],r['itemId']) for r in physical['resources']})==len(physical['resources'])
    for site in physical['resources']:
        assert 0<=site['reserved']<=site['stock']<=site['capacity']
        coordinate=str(site['position']['x'])+','+str(site['position']['z'])
        consumed=sum(e['quantity'] for e in events if e['type']=='ResourceConsumed' and e['itemId']==site['itemId'] and e['reasonCode']==coordinate)
        regenerated=sum(e['quantity'] for e in events if e['type']=='ResourceRegenerated' and e['itemId']==site['itemId'] and e['reasonCode']==coordinate)
        assert site['stock']==site['capacity']+regenerated-consumed
        assert not (site['owner'] and site['tenant'])
        assert not site['public'] or not site['owner'] and not site['tenant']
        assert (site['tenant'] is None)==(site['leaseUntil'] is None)
        assert site['leaseUntil'] is None or site['leaseUntil']>state['tick']
    for process in physical['transfers']:
        assert 0<=process['completed']<process['quantity'] and process['remaining']==process['quantity']-process['completed']
        assert 0<=process['workRemainder']<items[process['itemId']]
        assert process['owner']['value'] in people
        if process['kind']!='Pickup':
            assert sum(s['reserved'] for s in inventories[process['source']]['stock'] if s['itemId']==process['itemId'])>=process['remaining']
        if process['destination'] and process['kind']!='Delivery':
            assert inventories[process['destination']]['reservedCapacity']>=process['remaining']*items[process['itemId']]
    election_rules = config.get('elections') or {}
    period, base = election_rules.get('periodTicks', 1200), election_rules.get('baseVote', 1)
    elections = state['elections']
    assert elections == json.loads((folder/'elections.json').read_text(encoding='utf-8'))
    actor_ids = sorted(people)
    winners = []
    for index, result in enumerate(elections['results'], 1):
        tick = result['votingTick']
        assert result['id']['value'] == index and tick == index * period <= state['tick']
        expected_groups = labor_by_tick[tick]
        assert len({g['group']['value'] for g in result['employment']}) == len(result['employment'])
        assert {g['group']['value']: (g['owner']['value'], g['headcount']) for g in result['employment']} == expected_groups
        assert result['population'] == int(samples[tick]['population'])
        assert result['unemployed'] == int(samples[tick]['unemployed'])
        assert [b['voter']['value'] for b in result['ballots']] == actor_ids
        tally = {}
        for ballot in result['ballots']:
            voter, candidate = ballot['voter']['value'], ballot['candidate']['value']
            assert candidate in people and ballot['baseWeight'] == base
            assert ballot['workerWeight'] == sum(n for owner, n in expected_groups.values() if owner == voter)
            assert ballot['weight'] == base + ballot['workerWeight']
            tally[candidate] = tally.get(candidate, 0) + ballot['weight']
        assert result['totalWeight'] == sum(tally.values()) == len(people)*base + result['population'] - result['unemployed']
        assert {t['candidate']['value']: t['weight'] for t in result['tally']} == tally
        winner = next((candidate for candidate, weight in tally.items() if 2 * weight > result['totalWeight']), None)
        assert (result['winner']['value'] if result['winner'] else None) == winner
        ballots = [e for e in events if e['type'] == 'ElectionBallot' and e['entityId'] == index]
        assert len(ballots) == len(people) and all(e['tick'] == tick and e['phase'] == (9 if election_rules.get('pauseForBallots') else 8) for e in ballots)
        assert [(e['actorId']['value'], int(e['reasonCode']), e['quantity']) for e in ballots] == [(b['voter']['value'], b['candidate']['value'], b['weight']) for b in result['ballots']]
        if winner is not None: winners.append((index, winner, tick))
    if winners:
        assert len(winners) == 1 and winners[0][0] == len(elections['results'])
        ident, winner, tick = winners[0]
        assert elections['firstVictory'] == {'election': {'value': ident}, 'winner': {'value': winner}, 'tick': tick}
        assert elections['nextElectionId'] is None and elections['nextVotingTick'] is None and not elections['choices']
        assert elections['mode'] in ('Victory', 'Sandbox')
        assert (elections['mode'] == 'Sandbox') == (elections['sandboxStartedAtTick'] is not None)
    else:
        assert elections['firstVictory'] is None and elections['mode'] == 'Competitive'
        assert len(elections['results']) == state['tick'] // period - bool(elections.get('voting'))
        assert elections['nextElectionId']['value'] == len(elections['results']) + 1
        assert elections['nextVotingTick'] == (len(elections['results']) + 1) * period
    support = json.loads((folder/'support.json').read_text(encoding='utf-8'))
    assert 'support' not in state, 'Private political state leaked into generic WorldView'
    proposals = {p['id']: p for p in support['proposals']}
    assert len(proposals) == len(support['proposals'])
    for candidate in people:
        owned = sorted(p['id'] for p in proposals.values() if p['candidate']['value'] == candidate)
        assert owned == [(candidate-1)*1_000_000_000+i for i in range(1,len(owned)+1)]
    sections = {x['owner']['value']: x['inventory']['value'] for x in support['sections']}
    assert len(sections) == len(set(sections.values()))
    if config.get('support'):
        assert sorted(sections) == actor_ids
        for owner, inv in sections.items():
            assert inventories[inv]['ownerId']['value'] == owner
            assert inventories[inv]['position'] == config['support']['tradingPoint']
    else: assert not sections and not proposals and not support['commitments']
    blocks = set()
    for c in support['commitments']:
        key = (c['seller']['value'],c['election']['value']); assert key not in blocks; blocks.add(key)
        p = proposals[c['proposalId']]
        assert p['status'] == 'Signed' and not p['reserved'] and p['closedTick'] == c['signedTick']
        for name in ['seller','candidate','election','source','destination','compensation']: assert p[name] == c[name]
        signed = [e for e in events if e['type']=='SupportSigned' and e.get('support',{}).get('proposal',{}).get('id')==p['id']]
        assert len(signed)==1 and signed[0]['phase']==3 and signed[0]['tick']==c['signedTick']
        receipt = signed[0]['support']['commitment']; assert receipt['compensation']==p['compensation']
        result = next((r for r in elections['results'] if r['id']==c['election']),None)
        assert (c['status']=='Executed') == (result is not None)
        if result:
            assert c['executedTick']==result['votingTick']
            assert next(b for b in result['ballots'] if b['voter']==c['seller'])['candidate']==c['candidate']
    for p in proposals.values():
        assert p['seller']!=p['candidate'] and p['source']['value']==sections[p['candidate']['value']] and p['destination']['value']==sections[p['seller']['value']]
        assert p['expiryTick']==p['election']['value']*period
        terms={m['itemId']:m['quantity'] for m in p['compensation']}; assert len(terms)==len(p['compensation']) and all(q>0 for q in terms.values())
        for r in p['reserved']:
            assert p['status']=='AcceptedAwaitingResources' and 0<r['quantity']<=terms[r['itemId']]
            assert next(x for x in inventories[p['source']['value']]['stock'] if x['itemId']==r['itemId'])['reserved']>=r['quantity']
        if p['status']=='Expired': assert p['closedTick']==p['expiryTick'] and not p['reserved']
        if p['status'] in ('Proposed','AcceptedAwaitingResources'): assert p['closedTick'] is None and p['expiryTick']>state['tick']
    public=[json.loads(line) for line in (folder/'public-events.jsonl').read_text(encoding='utf-8').splitlines()]
    assert [e['sequence'] for e in public]==list(range(1,len(public)+1))
    assert all(not e['type'].startswith('Support') and e['type']!='VoteChoiceChanged' and 'commandId' not in e and 'support' not in e for e in public)
    assert len(talks)==len(negotiations)
    nr=(config.get('economy') or {}).get('negotiations') or {}
    for n in negotiations:
        revisions=n['revisions']; t=revisions[-1]['terms']
        assert 0<len(revisions)<=nr.get('maxVersions',4)
        assert [r['version'] for r in revisions]==list(range(1,len(revisions)+1))
        assert n['createdTick']<n['expiresTick']<=n['createdTick']+nr.get('ttlTicks',60)
        assert all(r['terms']['seller']==t['seller'] and r['terms']['buyer']==t['buyer'] and r['terms']['kind']==t['kind'] for r in revisions)
        assert all(a['proposedBy']!=b['proposedBy'] for a,b in zip(revisions,revisions[1:]))
        if n['status']=='Proposed':assert n['closedTick'] is None and n['contractId']==0 and n['expiresTick']>state['tick']
        else: assert revisions[-1]['tick']<=n['closedTick']<=min(state['tick'],n['expiresTick'])
        if n['status']=='Expired':assert n['closedTick']==n['expiresTick']
        if n['status']=='Accepted':
            if t['kind']=='Support':
                p=proposals[n['contractId']];assert p['candidate']==t['buyer'] and p['seller']==t['seller'] and p['compensation']==t['compensation']
            else:
                contract=next(c for c in (allocations if t['kind']=='Sale' else contracts) if c['id']==n['contractId'])
                assert contract['negotiationId']==n['id'] and contract['quantity']==t['quantity'] and contract['unitPriceMinor']==t['unitPriceMinor']
                assert contract['deadlineTick']==contract['createdTick']+t['fulfillmentTicks']
                assert contract['createdTick']==n['closedTick']
        else: assert n['contractId']==0
    assert all('negotiation' not in e and e['type']!='TradeNegotiationChanged' for e in public)
    print(f'{folder}: money, item flows, capacity, population, settlements, elections and private support verified; tick={summary["tick"]}')

if len(sys.argv) < 2:
    raise SystemExit('Usage: verify_results.py RUN_DIRECTORY [RUN_DIRECTORY]')
for folder in sys.argv[1:]:
    verify(folder)
