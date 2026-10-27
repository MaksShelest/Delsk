"""Mutation checks for the independent road export auditor.
Author: Шелестов Максим Евгеньевич.
"""
import contextlib,copy,io,json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from road_audit import verify_roads

class RoadAuditTests(unittest.TestCase):
    def fixture(self):
        point=lambda x,z:dict(x=x,z=z)
        a,b=point(0,0),point(1,0)
        contributions=[dict(level=1,builder=dict(value=0),tick=0,vehicleId=0)]
        return {'scenario.json':dict(items=[],economy=dict(roads=dict(stages=[dict(level=n,speed=s,priceMinor=n*200,ticks=n*8,materials=[]) for n,s in enumerate([20,40,60],1)]),fleet=dict(models=[dict(id='car',speed=60,materialsOnly=False)])),layout=dict(roadCorridorCells=[a,b],initialCityDirtCells=[a,b],civicObjects=[]),plots=[]),
            'roads.json':dict(cells=[dict(cell=c,level=1,contributions=contributions) for c in [a,b]],works=[],bypasses=[],orders=[],traffic=[dict(vehicleId=1,path=[a,b],index=1,progress=0,advancedQuanta=10)]),
            'fleet.json':dict(vehicles=[dict(id=1,owner=dict(value=1),modelId='car',cargoInventory=dict(value=2))]),
            'final-state.json':dict(equipment=[],inventories=[dict(id=dict(value=2),position=b)]),
            'events.jsonl':[dict(type='DealerInitialStock',entityId=1,tick=0,reasonCode='0,0'),dict(type='VehiclePurchased',entityId=1,tick=6),dict(type='TrafficCellEntered',entityId=1,tick=10,reasonCode='0,0>1,0',quantity=20)],'ledger.jsonl':[],'public-events.jsonl':[]}
    def verify(self,data):
        with tempfile.TemporaryDirectory() as folder:
            for name,value in data.items():
                text='\n'.join(json.dumps(row) for row in value) if name.endswith('jsonl') else json.dumps(value)
                (Path(folder)/name).write_text(text)
            with contextlib.redirect_stdout(io.StringIO()):verify_roads(folder)
    def test_valid_independent_physical_history(self):self.verify(self.fixture())
    def test_corrupt_contribution_speed_position_money_private_and_e3_rejected(self):
        mutations=[lambda d:d['roads.json']['cells'][0]['contributions'].clear(),lambda d:d['events.jsonl'][-1].update(quantity=40),lambda d:d['events.jsonl'][-1].update(reasonCode='0,0>2,0'),lambda d:d['final-state.json']['inventories'][0].update(position=dict(x=0,z=0)),lambda d:d['ledger.jsonl'].append(dict(reason='RoadWages',amountMinor=1)),lambda d:d['public-events.jsonl'].append(dict(type='RoadWorkStarted')),lambda d:d['ledger.jsonl'].append(dict(reason='RoadInvoice',amountMinor=1))]
        for n,mutate in enumerate(mutations):
            with self.subTest(n=n):
                data=copy.deepcopy(self.fixture());mutate(data)
                with self.assertRaises((AssertionError,KeyError)):self.verify(data)
