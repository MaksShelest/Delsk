"""Derived metrics over existing private runner exports; never an economic simulator.
Author: Шелестов Максим Евгеньевич.
"""
import csv
import json
from pathlib import Path
from analyze_paired_series import model_counts, quantile


def measure(directory):
    folder = Path(directory)
    def load(name):
        return json.loads((folder/name).read_text(encoding='utf-8'))
    summary, manifest = load('summary.json'), load('manifest.json')
    if summary['status'] != 'succeeded' or manifest.get('developerBuild') or manifest.get('researchModified') or not manifest.get('cleanComparisonEligible', False):
        raise ValueError('Incomplete/Developer/intervened exports cannot enter clean comparisons')
    city, state, scenario = load('public-economy.json'), load('final-state.json'), load('scenario.json')
    prices = {i['id']: i['basePriceMinor'] for i in scenario['items']}
    with (folder/'labor.csv').open(encoding='utf-8', newline='') as stream:
        labor = list(csv.DictReader(stream))
    traces = model_counts(folder)
    result = dict(stateHash=summary['stateHash'], ticks=summary['tick'],
        capitalMinor=city['capitalMinor'], cashMinor=sum(a['balanceMinor'] for a in state['accounts'] if a['kind']=='Participant'),
        employment=city['employed'], maximumWageMinor=max((int(r['wage_minor']) for r in labor), default=0),
        idleWorkerTicks=sum(int(r['headcount']) for r in labor if r['ready'] != 'True'),
        payrollMinor=sum(a['balanceMinor'] for a in state['accounts'] if a['kind']=='WagesSink'),
        outputBaseMinor=sum(f['produced']*prices[f['itemId']] for f in state['flows']),
        harvestedUnits=sum(f['harvested'] for f in state['flows']),
        completedTrips=sum(l['completedTrips'] for l in state['logistics']),
        lateDeliveries=sum(c['status']=='Expired' for c in state['deliveryContracts']),
        latePickups=sum(c['releasedQuantity']>0 for c in state['marketAllocations']),
        elections=len(state['elections']['results']), winner=state['elections']['firstVictory'],
        negotiations=len(load('negotiations.json')), modelRequests=traces['requested'], fallback=traces['fallbackResolved'],
        modelLatencyP50Ms=quantile(traces['latencies'], .5) if traces['latencies'] else None,
        modelLatencyP95Ms=quantile(traces['latencies'], .95) if traces['latencies'] else None)
    # Realized operating cash result; capex/escrow deposits/returns and unsold stock are not revenue.
    participant_accounts = {p['accountId']['value'] for p in state['participants']}
    revenue = costs = 0
    with (folder/'ledger.jsonl').open(encoding='utf-8') as stream:
        for line in stream:
            row = json.loads(line)
            if row['reason'] in ('CapitalSettlement','DeliveryPayment','PickupPayment') and row['creditAccount']['value'] in participant_accounts:
                revenue += row['amountMinor']
            if row['reason'] in ('Wages','LogisticsWages','EquipmentTravelWages','Energy','ResourceLease') and row['debitAccount']['value'] in participant_accounts:
                costs += row['amountMinor']
    # Actual payments, not funding/holds. Internal payments cancel in the city aggregate.
    buyers = {p['id']['value'] for p in state['participants']}
    purchases = sum(a['paidMinor'] for a in state['marketAllocations'] if a['buyer']['value'] in buyers)
    purchases += sum(c['paidMinor'] for c in state['deliveryContracts'] if c['buyer']['value'] in buyers)
    fines = sum(c['fineMinor'] for c in state['deliveryContracts']) + sum(a['fineMinor'] for a in state['marketAllocations'])
    profit = revenue-purchases-costs-fines
    result.update(realizedRevenueMinor=revenue,operatingCashProfitMinor=profit,
                  operatingCashMargin=profit/revenue if revenue else None,
                  profitDefinition='Realized trade receipts minus actual input purchases/payroll/energy/lease/fines; excludes capex, unsold stock, depreciation and opportunity costs. Not accrual net profit.')
    if (folder/'profile.json').exists():
        profile=load('profile.json');result.update(profile)
        result['capitalChangeMinor']=profile['finalCapitalMinor']-profile['initialCapitalMinor']
    if (scenario.get('economyProfileId') or '').startswith('vnext-'):
        from full_economy_metrics import measure as full_measure
        result.update(full_measure(folder))
        full=result['fullEconomy'];a=full['actors'].values()
        revenue=sum(r.get('salesRevenueMinor',0)+r.get('serviceRevenueMinor',0)+r.get('roadRevenueMinor',0)+r.get('workRewardMinor',0)+r.get('compensationMinor',0) for r in a)
        result['realizedRevenueMinor']=revenue
        result['operatingCashProfitMinor']=sum(r['operatingReceiptResultMinor'] for r in a) if not full['unclassifiedActorCashMinor'] else None
        result['operatingCashMargin']=result['operatingCashProfitMinor']/revenue if revenue and result['operatingCashProfitMinor'] is not None else None
        result['profitDefinition']=full['definition']
    return result
