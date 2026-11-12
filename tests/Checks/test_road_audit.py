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
    def test_catalog_vehicle_starts_at_an_eligible_bay(self):
        data = self.fixture()
        config = data['scenario.json']
        config['economy']['fleet']['equipmentDealerVersion'] = 2
        start, end = config['layout']['roadCorridorCells']
        dealer = dict(kind='dealership', footprint=[], parking=[start, dict(x=0, z=1), end, dict(x=1, z=1)])
        config['layout']['civicObjects'].append(dealer)
        events = data['events.jsonl']
        events[0] = dict(type='VehicleCatalogRequested', entityId=0, tick=1, commandId='order', actorId=dict(value=1), itemId='car')
        events[1].update(commandId='order', actorId=dict(value=1), itemId='car')
        self.verify(data)

        stationary = copy.deepcopy(data)
        stationary['events.jsonl'].pop()
        stationary['roads.json']['traffic'].clear()
        stationary['final-state.json']['inventories'][0]['position'] = start
        self.verify(stationary)

        dealer['parking'] = [end, dict(x=0, z=1), start, dict(x=1, z=1)]
        with self.assertRaisesRegex(AssertionError, 'eligible dealer bay'):
            self.verify(data)

    def i2_fixture(self,shared_target=False):
        d=self.fixture();a,b=dict(x=0,z=0),dict(x=1,z=0);c=dict(x=2,z=0)
        d['scenario.json']['economy'].update(movement=dict(version=2,speedDivisor=40,speedMultiplier=3,nominalSpeeds=dict(runningRoad=10,runningOffroad=8,vehicleOffroad=10)),vehicleControl=dict(version=1,exitHoldQuanta=5,laneOffset=200))
        d['fleet.json']['vehicles'][0].update(facingX=1,facingZ=0)
        d['fleet.json']['vehicles'].append(dict(id=3,owner=dict(value=2),modelId='car',cargoInventory=dict(value=4),facingX=-1,facingZ=0))
        d['final-state.json']['inventories'].append(dict(id=dict(value=4),position=c if shared_target else b))
        state=lambda key:dict(laneX=0,laneZ=200 if key==2 else -200,bearing=4000 if key==2 else 12000,turnQuanta=0)
        d['roads.json']['traffic'][0]['steering']=state(2)
        d['roads.json']['traffic'].append(dict(vehicleId=3,path=[],index=0,progress=0,advancedQuanta=10,steering=state(4)))
        initial=d['events.jsonl'][:2];entry=d['events.jsonl'][-1]
        d['events.jsonl']=initial+[dict(type='DealerInitialStock',entityId=3,tick=0,reasonCode='2,0' if shared_target else '1,0'),dict(type='VehiclePurchased',entityId=3,tick=6),
            dict(type='TrafficLaneState',entityId=1,actorId=dict(value=1),tick=7,quantity=2,reasonCode='0,0'),dict(type='TrafficLaneState',entityId=3,actorId=dict(value=2),tick=7,quantity=4,reasonCode='2,0' if shared_target else '1,0')]
        if shared_target:
            d['scenario.json']['layout']['roadCorridorCells'].append(c);d['scenario.json']['layout']['initialCityDirtCells'].append(c)
            d['roads.json']['cells'].append(dict(cell=c,level=1,contributions=copy.deepcopy(d['roads.json']['cells'][0]['contributions'])))
            d['final-state.json']['inventories'][0]['position']=a
            d['roads.json']['traffic'][0].update(index=0,progress=400)
            d['roads.json']['traffic'][1].update(path=[c,b],index=0,progress=400)
        else:d['events.jsonl'].append(entry)
        return d
    def test_i2_opposite_lanes_allow_real_cell_and_edge_sharing_only_with_witnesses(self):
        self.verify(self.i2_fixture());self.verify(self.i2_fixture(True))
    def test_i2_lane_forgery_missing_witness_wrong_side_and_old_profile_are_rejected(self):
        mutations=[lambda d:d['events.jsonl'].pop(-2),lambda d:d['events.jsonl'][-2].update(quantity=2),
            lambda d:d['roads.json']['traffic'][1]['steering'].update(laneZ=-199),
            lambda d:d['events.jsonl'][-2].update(reasonCode='0,0'),lambda d:d['events.jsonl'][-2].update(amountMinor=1),
            lambda d:d['roads.json']['traffic'][1]['steering'].update(turnQuanta=4),lambda d:d['scenario.json']['economy'].pop('vehicleControl')]
        for mutate in mutations:
            d=self.i2_fixture();mutate(d)
            with self.assertRaises((AssertionError,KeyError)):self.verify(d)
    def test_i1_offroad_nominal_ten_is_valid_but_old_profile_and_eleven_are_rejected(self):
        d=self.fixture();d['scenario.json']['economy']['movement']=dict(version=2,speedDivisor=40,speedMultiplier=3,nominalSpeeds=dict(runningRoad=10,runningOffroad=8,vehicleOffroad=10))
        d['scenario.json']['layout']['initialCityDirtCells']=d['scenario.json']['layout']['initialCityDirtCells'][:1];d['roads.json']['cells']=d['roads.json']['cells'][:1];d['events.jsonl'][-1]['quantity']=10
        self.verify(d)
        bad=copy.deepcopy(d);bad['scenario.json']['economy'].pop('movement')
        with self.assertRaises(AssertionError):self.verify(bad)
        bad=copy.deepcopy(d);bad['events.jsonl'][-1]['quantity']=11
        with self.assertRaises(AssertionError):self.verify(bad)
    def test_corrupt_contribution_speed_position_money_private_and_e3_rejected(self):
        mutations=[lambda d:d['roads.json']['cells'][0]['contributions'].clear(),lambda d:d['events.jsonl'][-1].update(quantity=40),lambda d:d['events.jsonl'][-1].update(reasonCode='0,0>2,0'),lambda d:d['final-state.json']['inventories'][0].update(position=dict(x=0,z=0)),lambda d:d['ledger.jsonl'].append(dict(reason='RoadWages',amountMinor=1)),lambda d:d['public-events.jsonl'].append(dict(type='RoadWorkStarted')),lambda d:d['ledger.jsonl'].append(dict(reason='RoadInvoice',amountMinor=1))]
        for n,mutate in enumerate(mutations):
            with self.subTest(n=n):
                data=copy.deepcopy(self.fixture());mutate(data)
                with self.assertRaises((AssertionError,KeyError)):self.verify(data)
