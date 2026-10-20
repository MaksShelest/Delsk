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
    labor = list(csv.DictReader((folder/'labor.csv').open(encoding='utf-8', newline='')))
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
    if (folder/'profile.json').exists():
        result.update(load('profile.json'))
    return result
