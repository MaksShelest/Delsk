#!/usr/bin/env python3
"""Independent arithmetic checks of runner exports. Author: Шелестов Максим Евгеньевич."""
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
    print(f'{folder}: money, item flows, capacity and settlements verified; tick={summary["tick"]}')

if len(sys.argv) < 2:
    raise SystemExit('Usage: verify_results.py RUN_DIRECTORY [RUN_DIRECTORY]')
for folder in sys.argv[1:]:
    verify(folder)
