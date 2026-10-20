"""Economic metric semantics: escrow is not income; missing evidence is not zero.
Author: Шелестов Максим Евгеньевич.
"""
import json
from pathlib import Path
import sys,tempfile,unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from research_lab import write
from research_metrics import measure

class MetricsTests(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup);self.root=Path(self.temp.name)
  self.state=dict(accounts=[dict(kind='Participant',balanceMinor=50),dict(kind='WagesSink',balanceMinor=10)],
   participants=[dict(id=dict(value=1),accountId=dict(value=1001))],flows=[],logistics=[],
   deliveryContracts=[dict(buyer=dict(value=1),paidMinor=50,status='Fulfilled',fineMinor=3)],
   marketAllocations=[dict(buyer=dict(value=1),paidMinor=15,releasedQuantity=0,fineMinor=0)],elections=dict(results=[],firstVictory=None))
  for name,value in {'summary.json':dict(status='succeeded',stateHash='h',tick=1),
   'manifest.json':dict(cleanComparisonEligible=True),'scenario.json':dict(items=[]),
   'public-economy.json':dict(capitalMinor=200,employed=1),'negotiations.json':[],'final-state.json':self.state}.items():write(self.root/name,value)
  (self.root/'labor.csv').write_text('headcount,wage_minor,ready\n1,10,False\n')
  rows=[dict(tick=1,debitAccount=dict(value=999),creditAccount=dict(value=1001),amountMinor=100,reason='CapitalSettlement'),
   dict(tick=1,debitAccount=dict(value=999),creditAccount=dict(value=1001),amountMinor=10000,reason='DeliveryRefund'),
   dict(tick=1,debitAccount=dict(value=1001),creditAccount=dict(value=999),amountMinor=5,reason='Energy'),
   dict(tick=1,debitAccount=dict(value=1001),creditAccount=dict(value=999),amountMinor=10,reason='Wages'),
   dict(tick=1,debitAccount=dict(value=1001),creditAccount=dict(value=999),amountMinor=9000,reason='MarketEscrow')]
  (self.root/'ledger.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in rows))
 def test_cash_profit_excludes_escrow_and_refunds_but_includes_actual_costs(self):
  m=measure(self.root);self.assertEqual(100,m['realizedRevenueMinor']);self.assertEqual(17,m['operatingCashProfitMinor'])
  self.assertAlmostEqual(.17,m['operatingCashMargin']);self.assertEqual(1,m['idleWorkerTicks'])
  self.assertIsNone(m['modelLatencyP95Ms']);self.assertEqual(0,m['modelRequests'])
 def test_zero_revenue_has_no_margin_or_invented_model_latency(self):
  (self.root/'ledger.jsonl').write_text('')
  m=measure(self.root);self.assertIsNone(m['operatingCashMargin']);self.assertIsNone(m['modelLatencyP50Ms'])
 def test_developer_or_modified_exports_are_rejected(self):
  for change in [dict(developerBuild=True),dict(researchModified=True),dict(cleanComparisonEligible=False)]:
   write(self.root/'manifest.json',dict(cleanComparisonEligible=True)|change)
   with self.assertRaises(ValueError):measure(self.root)
if __name__=='__main__':unittest.main()
