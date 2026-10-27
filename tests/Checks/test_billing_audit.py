"""Corruption checks for independent E3 arithmetic, not a gameplay implementation.
Author: Шелестов Максим Евгеньевич.
"""
import contextlib,copy,io,json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from billing_audit import verify,capital,SCALE

class BillingAuditTests(unittest.TestCase):
    def fixture(self):
        claim=lambda r,p:dict(recipient=dict(value=r),principalMinor=1,paidMinor=p)
        event=lambda kind,t,entity=1001,actor=1,q=0,amount=0,reason='':dict(type=kind,tick=t,entityId=entity,actorId=dict(value=actor),quantity=q,amountMinor=amount,reasonCode=reason)
        ledger=lambda debit,credit,reason:dict(debitAccount=dict(value=debit),creditAccount=dict(value=credit),reason=reason,amountMinor=1)
        events=[event('RoadStageCompleted',10,10,2,2,reason='0,0')]+[event('BillingDistance',100,9,q=80,reason='0,0') for _ in range(10)]+[
            event('BillingInvoiceCreated',120,amount=2),event('BillingPenaltyAccrued',1121),event('BillingInvoicePaid',1122,q=1,amount=1),event('BillingInvoicePaid',1123,q=1,amount=1)]
        i=dict(id=1001,payer=dict(value=1),createdTick=120,dueTick=1120,nextPenaltyTick=2121,claims=[claim(0,1),claim(2,1)],principalMinor=2,penaltyMinor=0,penaltyPaidMinor=0,penaltyRemainder=4000,viewed=False)
        return {'scenario.json':dict(layout=dict(initialCityDirtCells=[dict(x=0,z=0)]),economy=dict(billing=dict(classFactors=dict(car=10000),lightIncrementMinor=1,cityShareBasisPoints=1000,invoicePeriod=120,graceTicks=1000,penaltyPeriod=1000,penaltyBasisPoints=2000))),
            'billing.json':dict(clearingAccount=3,invoices=[i],accruals=[dict(payer=dict(value=1),recipient=dict(value=r),units=n) for r,n in [(0,SCALE//10),(2,-SCALE//10)]]),
            'fleet.json':dict(vehicles=[dict(id=9,owner=dict(value=1),modelId='car')]),
            'final-state.json':dict(equipment=[],participants=[dict(id=dict(value=n),accountId=dict(value=a)) for n,a in [(1,1),(2,4)]],accounts=[dict(id=dict(value=n),kind=k,balanceMinor=b) for n,k,b in [(1,'Participant',8),(2,'Treasury',1),(3,'RoadClearing',0),(4,'Participant',1)]]),
            'events.jsonl':events,'ledger.jsonl':[ledger(1,3,'RoadPrincipal:1001'),ledger(3,4,'RoadRevenue:1001'),ledger(1,3,'RoadPrincipal:1001'),ledger(3,2,'RoadRevenue:1001')],'public-events.jsonl':[]}
    def check(self,data):
        with tempfile.TemporaryDirectory() as folder:
            for name,value in data.items():(Path(folder)/name).write_text('\n'.join(json.dumps(v) for v in value) if name.endswith('jsonl') else json.dumps(value))
            with contextlib.redirect_stdout(io.StringIO()):verify(folder)
            claims,debt=capital(folder);self.assertEqual(sum(claims.values()),sum(debt.values()))
    def test_actual_split_signed_residue_partial_principal_and_zero_fractional_penalty(self):self.check(self.fixture())
    def test_distance_rights_interest_replay_payout_and_privacy_corruption_is_rejected(self):
        mutations=[lambda d:d['events.jsonl'][1].update(quantity=40),lambda d:d['events.jsonl'][0]['actorId'].update(value=3),lambda d:d['billing.json']['invoices'][0].update(penaltyMinor=1),lambda d:d['events.jsonl'][-3].update(tick=1120),lambda d:d['events.jsonl'].append(copy.deepcopy(d['events.jsonl'][-1])),lambda d:d['ledger.jsonl'].append(copy.deepcopy(d['ledger.jsonl'][-1])),lambda d:d['billing.json']['accruals'][0].update(units=0),lambda d:d['public-events.jsonl'].append(dict(type='BillingInvoiceCreated'))]
        for n,mutate in enumerate(mutations):
            with self.subTest(n=n):
                data=copy.deepcopy(self.fixture());mutate(data)
                with self.assertRaises((AssertionError,KeyError,ValueError)):self.check(data)
