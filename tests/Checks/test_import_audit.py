"""The independent audit must reject realistic corrupted import exports."""
import copy
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2]/'scripts'))
from import_audit import verify_imports


def example():
    ref = lambda value: {'value': value}
    position = {'x': 2, 'z': 3}
    config = {'economy': {'productionContent': {'imports': [dict(itemId='steel', package=12, refreshTicks=1200, unitPriceMinor=3125)]}}, 'capitalDemand': {'terminal': position}}
    state = {'tick': 1200, 'participants': [dict(id=ref(1), accountId=ref(11), inventoryId=ref(12))],
             'inventories': [dict(id=ref(20), ownerId=ref(1), position=position, stock=[dict(itemId='steel', quantity=2)])],
             'flows': [dict(itemId='steel', imported=3, produced=0)]}
    content = {'imports': [dict(itemId='steel', remaining=21, nextRefreshTick=2400)], 'pickups': [dict(owner=ref(1), inventory=ref(20))]}
    purchase = dict(type='ImportPurchased', tick=1, phase=1, actorId=ref(1), commandId='buy', entityId=20, itemId='steel', quantity=3, amountMinor=9375)
    events = [purchase, dict(type='StockTransferred', tick=2, phase=4, commandId='take', itemId='steel', quantity=1),
              dict(type='ImportRestocked', tick=1200, phase=0, itemId='steel', quantity=12, amountMinor=0)]
    ledger = [dict(tick=1, debitAccount=ref(11), creditAccount=ref(0), amountMinor=9375, reason='ImportPurchase')]
    tape = [dict(authenticatedActor=ref(1), command=dict(commandId='buy', actorId=ref(1), notBeforeTick=1, expiresAtTick=10, action=dict(kind='BuyImport', itemId='steel', quantity=3))),
            dict(authenticatedActor=ref(1), command=dict(commandId='take', actorId=ref(1), action=dict(kind='Transfer', source=ref(20), destination=ref(12), itemId='steel', quantity=1)))]
    return [config, state, content, events, ledger, tape, []]


class ImportAuditTests(unittest.TestCase):
    def test_paid_stock_refill_and_partial_physical_pickup(self):
        self.assertEqual(verify_imports(*example()), 9375)

    def test_bad_exports_are_rejected(self):
        def corrupt(which):
            values = example()
            config, state, content, events, ledger, tape, public = values
            if which == 'price': events[0]['amountMinor'] += 1
            elif which == 'double_payment': ledger.append(copy.deepcopy(ledger[0]))
            elif which == 'other_account': ledger[0]['debitAccount']['value'] = 999
            elif which == 'oversell': events[0]['quantity'] = tape[0]['command']['action']['quantity'] = 13
            elif which == 'duplicate_receipt': events.insert(1, copy.deepcopy(events[0]))
            elif which == 'wrong_owner': content['pickups'][0]['owner']['value'] = 2
            elif which == 'wrong_stock': content['imports'][0]['remaining'] = 22
            elif which == 'early_refill': events[-1]['tick'] = 1199
            elif which == 'uncapped_refill': events[-1]['quantity'] = 30
            elif which == 'missing_refill': events.pop()
            elif which == 'deadline_extension': content['imports'][0]['nextRefreshTick'] += 1200
            elif which == 'teleport': state['inventories'][0]['stock'][0]['quantity'] = 0
            elif which == 'duplicate_goods': state['inventories'][0]['stock'][0]['quantity'] = 3
            elif which == 'wrong_flow': state['flows'][0]['imported'] = 4
            elif which == 'privacy': public.append(copy.deepcopy(events[0]))
            return values
        for name in ['price', 'double_payment', 'other_account', 'oversell', 'duplicate_receipt', 'wrong_owner', 'wrong_stock',
                     'early_refill', 'uncapped_refill', 'missing_refill', 'deadline_extension', 'teleport', 'duplicate_goods', 'wrong_flow', 'privacy']:
            with self.subTest(case=name), self.assertRaises((AssertionError, KeyError)):
                verify_imports(*corrupt(name))

    def test_capped_zero_refill_still_requires_its_scheduled_event(self):
        values = example()
        values[1]['tick'] = 3600
        values[3].extend([dict(type='ImportRestocked', tick=2400, phase=0, itemId='steel', quantity=3, amountMinor=0),
                          dict(type='ImportRestocked', tick=3600, phase=0, itemId='steel', quantity=0, amountMinor=0)])
        values[2]['imports'][0].update(remaining=24, nextRefreshTick=4800)
        self.assertEqual(verify_imports(*values), 9375)
        values[3].pop()
        with self.assertRaises(AssertionError): verify_imports(*values)

    def test_legacy_profile_cannot_hide_an_import_flow(self):
        state = {'flows': [dict(itemId='steel')]}
        self.assertEqual(verify_imports({}, state, None, [], [], [], []), 0)
        state['flows'][0]['imported'] = 1
        with self.assertRaises(AssertionError): verify_imports({}, state, None, [], [], [], [])

    def test_asphalt_cutoff_uses_completed_plant_not_future_knowledge(self):
        values = example()
        # The same paid-pickup example with a bootstrap item and a real IV upgrade.
        def rename(node):
            if isinstance(node, dict):
                for key, value in node.items():
                    if key == 'itemId' and value == 'steel': node[key] = 'asphalt'
                    else: rename(value)
            elif isinstance(node, list):
                for value in node: rename(value)
        rename(values)
        values[1]['tick'] = 2400
        values[1]['buildings'] = [dict(id={'value': 8}, typeId='materials')]
        values[3].append(dict(type='BuildingUpgraded', tick=1800, phase=0, entityId=8, reasonCode='4'))
        values[3].append(dict(type='ImportRestocked', tick=2400, phase=0, itemId='asphalt', quantity=0, amountMinor=0))
        values[2]['imports'][0]['nextRefreshTick'] = 3600
        self.assertEqual(verify_imports(*values), 9375)  # The refill at1200 still happened.
        values[3][-1]['quantity'] = 3
        with self.assertRaises(AssertionError): verify_imports(*values)
