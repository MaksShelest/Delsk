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
