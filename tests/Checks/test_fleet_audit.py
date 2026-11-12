"""Corruption checks for the independent export auditor; no game simulation is replaced here."""
import contextlib
import copy
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from fleet_audit import verify_fleet

class FleetAuditTests(unittest.TestCase):
    def fixture(self):
        ids=['sputnik20','muravey70','muravey70','eger70','vector200','trakt_d1']
        models=[dict(id=i,capacityVolume=20,priceMinor=100,materialsOnly=i=='trakt_d1') for i in dict.fromkeys(ids)]
        listings=[dict(id=1000+n,modelId=i,position=dict(x=n//2,z=0),deliveredTick=0) for n,i in enumerate(ids)]
        events=[dict(type='DealerInitialStock',entityId=v['id'],tick=0,itemId=v['modelId']) for v in listings]
        events.append(dict(type='VehiclePurchased',entityId=1000,tick=6,actorId=dict(value=1),amountMinor=100))
        return {
            'scenario.json':dict(economy=dict(fleet=dict(models=models,supplyTicks=500,minimumStockTicks=2500))),
            'fleet.json':dict(stock=listings[1:],vehicles=[dict(id=1000,owner=dict(value=1),modelId='sputnik20',cargoInventory=dict(value=2000),driver=0,installation=None)],purchases=[dict(vehicleId=1000)],platforms=[]),
            'final-state.json':dict(inventories=[dict(id=dict(value=2000),ownerId=dict(value=1),capacity=20,occupiedVolume=0,reservedCapacity=0,position=dict(x=0,z=0),stock=[])],participants=[dict(id=dict(value=1),position=dict(x=0,z=0))],logistics=[],employmentGroups=[],population=1,unemployed=1),
            'events.jsonl':events,'ledger.jsonl':[dict(reason='VehiclePurchase',amountMinor=100)],'public-events.jsonl':[]}

    def verify(self,data):
        with tempfile.TemporaryDirectory() as folder:
            for name,value in data.items():
                text='\n'.join(json.dumps(row) for row in value) if name.endswith('jsonl') else json.dumps(value)
                (Path(folder)/name).write_text(text)
            with contextlib.redirect_stdout(io.StringIO()):verify_fleet(folder)

    def test_real_shapes_and_balanced_acquisition_are_accepted(self):self.verify(self.fixture())

    def test_class_dealer_checks_catalog_receipts_and_upgraded_capacity(self):
        data = self.fixture()
        config = data['scenario.json']
        rules = config['economy']['fleet']
        rules['equipmentDealerVersion'] = 2
        equipment_models = [dict(id=f'extractor_{kind}_1', capacityVolume=20, priceMinor=100,
                                 materialsOnly=False, equipmentTypeId=f'extract_{kind}', equipmentLevel=1)
                            for kind in ('field', 'forest', 'dairy', 'mine')]
        rules['models'].extend(equipment_models)
        config['equipmentTypes'] = [dict(id='extract_dairy', itemId='milk', items=['milk', 'eggs'],
                                         levels=[dict(level=1, storageCapacity=20), dict(level=2, storageCapacity=40)])]
        initial = ['sputnik20', 'muravey70', 'extractor_field_1', 'extractor_forest_1']
        listings = [dict(id=1000 + n, modelId=model_id, deliveredTick=0) for n, model_id in enumerate(initial)]
        data['fleet.json']['stock'] = listings
        data['fleet.json']['vehicles'][0].update(id=1004, modelId='extractor_dairy_1')
        data['fleet.json']['purchases'][0]['vehicleId'] = 1004
        cargo = data['final-state.json']['inventories'][0]
        cargo.update(capacity=40, stock=[dict(itemId='eggs')])
        data['final-state.json']['equipment'] = [dict(id=dict(value=1004), typeId='extract_dairy', level=2,
                                                    storageId=cargo['id'], position=cargo['position'],
                                                    ownerId=cargo['ownerId'], deployed=True)]
        data['events.jsonl'] = [dict(type='DealerInitialStock', entityId=listing['id'], tick=0, itemId=listing['modelId'])
                                for listing in listings]
        data['events.jsonl'].extend([
            dict(type='VehicleCatalogRequested', entityId=0, tick=20, actorId=dict(value=1), itemId='extractor_dairy_1', commandId='order'),
            dict(type='VehiclePurchased', entityId=1004, tick=500, actorId=dict(value=1), itemId='extractor_dairy_1', commandId='order', amountMinor=100),
            dict(type='EquipmentUpgraded', entityId=1004, tick=550),
        ])
        self.verify(data)

        for name, mutate in (
            ('missing catalog request', lambda d: d['events.jsonl'].pop(4)),
            ('wrong winner', lambda d: d['events.jsonl'][4].update(actorId=dict(value=2))),
            ('wrong delivery tick', lambda d: d['events.jsonl'][5].update(tick=499)),
            ('missing upgrade', lambda d: d['events.jsonl'].pop()),
            ('wrong capacity', lambda d: d['final-state.json']['inventories'][0].update(capacity=20)),
        ):
            with self.subTest(name=name):
                invalid = copy.deepcopy(data)
                mutate(invalid)
                with self.assertRaises(AssertionError):
                    self.verify(invalid)

    def test_corrupt_stock_money_cargo_driver_and_privacy_are_rejected(self):
        original=self.fixture()
        mutations={
            'missing_listing':lambda d:d['fleet.json']['stock'].pop(),
            'duplicate_vehicle':lambda d:d['fleet.json']['vehicles'].append(copy.deepcopy(d['fleet.json']['vehicles'][0])),
            'wrong_capacity':lambda d:d['final-state.json']['inventories'][0].update(capacity=200),
            'over_capacity':lambda d:d['final-state.json']['inventories'][0].update(occupiedVolume=21),
            'wrong_owner':lambda d:d['fleet.json']['vehicles'][0].update(owner=dict(value=2)),
            'different_driver_position':lambda d:(d['fleet.json']['vehicles'][0].update(driver=1),d['final-state.json']['participants'][0].update(position=dict(x=1,z=0))),
            'unfunded_purchase':lambda d:d['ledger.jsonl'][0].update(amountMinor=99),
            'unregistered_purchase':lambda d:d['fleet.json']['purchases'].clear(),
            'early_replacement':lambda d:d['events.jsonl'].append(dict(type='DealerReplaced',entityId=1001,tick=500)),
            'private_project_leak':lambda d:d['public-events.jsonl'].append(dict(type='VehicleKitStarted')),
            'population_duplicated':lambda d:d['final-state.json'].update(population=2),
        }
        for name,mutate in mutations.items():
            with self.subTest(name=name):
                data=copy.deepcopy(original);mutate(data)
                with self.assertRaises((AssertionError,KeyError,StopIteration)):self.verify(data)
