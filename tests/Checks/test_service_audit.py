"""Corruption checks for independent F1 evidence. Шелестов Максим Евгеньевич."""
import contextlib,copy,io,json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from service_audit import verify
class ServiceAuditTests(unittest.TestCase):
    def fixture(self):
        rules=dict(responseTicks=10,minimumFeeMinor=500,fixedFeeMinor=200,blockFeeMinor=10,volumeBlock=10,distanceBlock=10,costMarkupBasisPoints=12000,driverWageMinor=20,minimumBufferTicks=600,bufferBasisPoints=5000)
        q=dict(terms=dict(vehicleId=9,source=dict(value=5),destination=dict(value=6),itemId='wood',quantity=150,purchaseId=0,contractId=0),volume=300,quantityPerTrip=35,trips=5,loadedDistance=1,loaded=[dict(x=0,z=0),dict(x=1,z=0)],etaTicks=100,travelTicks=40,handlingTicks=60,extraStaffTicks=0,roadUnits=0,bufferTicks=600,feeMinor=2900)
        order=dict(id=1001,quote=q,state='Accepted',createdTick=1,respondByTick=11,acceptedTick=10,deadlineTick=710,closedTick=0,escrowAccount=3,customer=dict(value=1),carrier=dict(value=2))
        return {'scenario.json':dict(items=[dict(id='wood',unitVolume=2)],economy=dict(services=rules,fleet=dict(models=[dict(id='muravey70',capacityVolume=70,materialsOnly=False)]))),
            'services.json':dict(listedVehicles=[9],orders=[order]),'fleet.json':dict(vehicles=[dict(id=9,modelId='muravey70')]),
            'final-state.json':dict(tick=10,participants=[dict(id=dict(value=1),accountId=dict(value=1)),dict(id=dict(value=2),accountId=dict(value=2))],accounts=[dict(id=dict(value=3),kind='DeliveryServiceEscrow',balanceMinor=2900)],inventories=[dict(id=dict(value=5),stock=[dict(itemId='wood',quantity=150,reserved=150)])]),
            'ledger.jsonl':[dict(debitAccount=dict(value=1),creditAccount=dict(value=3),amountMinor=2900,reason='ServiceFunding:1001')],
            'events.jsonl':[dict(type='ServiceRequested',entityId=1001),dict(type='ServiceAccepted',entityId=1001,tick=10,amountMinor=2900)],'public-events.jsonl':[]}
    def check(self,data):
        with tempfile.TemporaryDirectory() as folder:
            for name,value in data.items():(Path(folder)/name).write_text('\n'.join(json.dumps(v) for v in value) if name.endswith('jsonl') else json.dumps(value))
            with contextlib.redirect_stdout(io.StringIO()):verify(folder)
    def test_five_trip_frozen_quote_and_escrow(self):self.check(self.fixture())
    def test_price_capacity_duplicate_accept_lease_and_payout_corruption(self):
        mutations=[lambda d:d.pop('services.json'),lambda d:d['services.json']['orders'][0]['quote'].update(feeMinor=2901),lambda d:d['services.json']['orders'][0]['quote'].update(trips=4),lambda d:d['services.json']['orders'].append(copy.deepcopy(d['services.json']['orders'][0])),lambda d:d['ledger.jsonl'].append(copy.deepcopy(d['ledger.jsonl'][0])),lambda d:d['events.jsonl'].append(copy.deepcopy(d['events.jsonl'][-1])),lambda d:d['ledger.jsonl'].append(dict(debitAccount=dict(value=3),creditAccount=dict(value=2),amountMinor=1,reason='EarlyPayout')),lambda d:d['public-events.jsonl'].append(dict(type='ServiceRequested')),lambda d:d['final-state.json']['accounts'][0].update(balanceMinor=0)]
        for mutate in mutations:
            data=self.fixture();mutate(data)
            with self.subTest(mutation=mutate):
                with self.assertRaises((AssertionError,KeyError,FileNotFoundError)):self.check(data)

class ServiceExecutionAuditTests(ServiceAuditTests):
    def fixture(self):
        d=super().fixture();d['scenario.json']['economy']['serviceExecution']=dict(lateBasisPoints=2000)
        o=d['services.json']['orders'][0];o['quote']['returnInventory']=dict(value=5)
        r=dict(order=1001,returnInventory=dict(value=5),loaded=45,delivered=10,returned=0,released=0,grossSettledMinor=193,paidMinor=193,refundedMinor=0,lateGrossMinor=0,lateRefundMinor=0,receipts=[dict(tick=20,quantity=10,grossMinor=193,paidMinor=193,lateRefundMinor=0)])
        d['services.json']['execution']=dict(runs=[r],crews=[])
        v=d['fleet.json']['vehicles'][0];v.update(cargoInventory=dict(value=7),driver=2)
        state=d['final-state.json'];state['tick']=20;state['accounts'][0]['balanceMinor']=2707
        state['inventories'][0].update(ownerId=dict(value=1),stock=[dict(itemId='wood',quantity=105,reserved=105)])
        state['inventories'].append(dict(id=dict(value=7),ownerId=dict(value=2),stock=[dict(itemId='wood',quantity=35,reserved=35)]))
        d['events.jsonl'] += [dict(type='ServiceCargoLoaded',entityId=1001,tick=15,quantity=45),dict(type='ServicePartDelivered',entityId=1001,tick=20,quantity=10,amountMinor=193)]
        d['ledger.jsonl'].append(dict(debitAccount=dict(value=3),creditAccount=dict(value=2),amountMinor=193,reason='ServicePayment:1001'))
        return d
    def test_physical_partial_receipt_and_payment(self):self.check(self.fixture())
    def test_receipt_cargo_recipient_and_duplicate_payment_corruption(self):
        mutations=[lambda d:d['services.json']['execution']['runs'][0]['receipts'][0].update(paidMinor=194),lambda d:d['final-state.json']['inventories'][1]['stock'][0].update(quantity=36),lambda d:d['ledger.jsonl'][-1].update(creditAccount=dict(value=1)),lambda d:d['ledger.jsonl'].append(copy.deepcopy(d['ledger.jsonl'][-1]))]
        for mutate in mutations:
            d=self.fixture();mutate(d)
            with self.subTest(mutation=mutate):
                with self.assertRaises(AssertionError):self.check(d)
