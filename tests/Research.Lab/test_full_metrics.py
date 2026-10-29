"""Settlement examples for F/G metrics. Toy export inputs test aggregation, not natural play.
Author: Шелестов Максим Евгеньевич.
"""
import json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from full_economy_metrics import measure
class FullMetricsTests(unittest.TestCase):
 def example(self,balances,ledger,services=None,allocations=None,billing=None):
  self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup);p=Path(self.tmp.name)
  def write(n,v):(p/n).write_text(json.dumps(v))
  write('scenario.json',dict(participants=[dict(id=1,cashMinor=1000),dict(id=2,cashMinor=100)],items=[],buildingTypes=[]))
  write('summary.json',dict(status='succeeded'))
  write('final-state.json',dict(tick=2,participants=[dict(id=i,accountId=i+100) for i in [1,2]],accounts=[dict(id=i+100,balanceMinor=balances[i-1]) for i in [1,2]],buildings=[],inventories=[],marketAllocations=allocations or [],deliveryContracts=[]))
  write('public-economy.json',dict(participants=[dict(actor=i,capitalMinor=balances[i-1]) for i in [1,2]]))
  if services is not None:write('services.json',services)
  if billing is not None:write('billing.json',billing)
  (p/'ledger.jsonl').write_text('\n'.join(json.dumps(dict(tick=t,debitAccount=d,creditAccount=c,amountMinor=a,reason=r)) for t,d,c,a,r in ledger)+'\n')
  (p/'events.jsonl').write_text('');(p/'agents.csv').write_text('tick,actor_id,busy,goal\n');(p/'labor.csv').write_text('tick,group_id,headcount,ready\n')
  return p
 def test_service_payment_is_not_escrow_funding_or_goods_payment(self):
  p=self.example([840,170],[(1,101,900,100,'ServiceFunding:7'),(2,900,102,40,'ServicePayment:7'),(2,900,101,20,'ServiceRefund:7'),(1,101,901,80,'MarketEscrow'),(2,901,102,30,'PickupPayment')],dict(orders=[dict(id=7,state='Completed',escrowAccount=900,customer=1,quote=dict(terms=dict(vehicleId=5)))],execution=dict(runs=[dict(paidMinor=40,delivered=5,lateRefundMinor=0)])),[dict(escrowAccount=901,buyer=1,seller=2)])
  r=measure(p)['fullEconomy'];self.assertEqual(40,r['actors']['2']['serviceRevenueMinor']);self.assertEqual(30,r['actors']['2']['salesRevenueMinor']);self.assertEqual(70,r['actors']['2']['operatingReceiptResultMinor']);self.assertEqual(-70,r['actors']['1']['operatingReceiptResultMinor']);self.assertEqual({},r['unclassifiedActorCashMinor'])
 def test_road_paid_principal_and_penalty_are_costs_unpaid_debt_is_not_cash(self):
  p=self.example([880,190],[(1,101,900,100,'RoadPrincipal:1'),(1,900,102,90,'RoadRevenue:1'),(1,900,0,10,'RoadRevenue:1'),(2,101,0,20,'RoadPenalty:1')],billing=dict(invoices=[dict(principalRemaining=200,penaltyRemaining=40)]))
  r=measure(p)['fullEconomy'];self.assertEqual(-120,r['actors']['1']['operatingReceiptResultMinor']);self.assertEqual(90,r['actors']['2']['roadRevenueMinor']);self.assertEqual(200,r['roadDebtPrincipalMinor'])
 def test_named_service_driver_wages_are_not_free(self):
  p=self.example([965,100],[(1,101,0,35,'ServiceDriverWages:8')]);r=measure(p)['fullEconomy'];self.assertEqual(-35,r['actors']['1']['operatingReceiptResultMinor']);self.assertEqual(35,r['actors']['1']['wagesMinor'])
 def test_unbilled_road_accrual_is_not_forgotten_or_counted_twice(self):
  p=self.example([1000,100],[])
  (p/'personal-accounts.json').write_text(json.dumps([dict(actor=2,personal=dict(debtMinor=20,accruedMinor=7,escrowMinor=0))]))
  r=measure(p)['fullEconomy']['actors']['2'];self.assertEqual(27,r['roadLiabilityMinor']);self.assertEqual(-27,r['operatingAfterRoadDebtMinor']);self.assertEqual(0,r['operatingReceiptResultMinor'])
 def test_missing_cash_flow_is_rejected_instead_of_fabricating_profit(self):
  p=self.example([999,100],[])
  with self.assertRaises(ValueError):measure(p)
 def test_unclassified_cash_is_reported_and_does_not_enter_profit_comparison(self):
  p=self.example([900,100],[(1,101,0,100,'NewUnclassifiedCost')]);r=measure(p);self.assertEqual({'NewUnclassifiedCost':100},r['fullEconomy']['unclassifiedActorCashMinor']);self.assertIsNone(r['npcOperatingReceiptResultMinor'])
 def test_handling_investment_is_cash_capex_not_repeated_operating_cost(self):
  p=self.example([700,100],[(1,101,0,100,'VehicleHandlingKit'),(2,101,0,200,'LoadingPlatformUpgrade')]);r=measure(p)['fullEconomy']
  self.assertEqual(300,r['actors']['1']['cashCapitalSpendingMinor']);self.assertEqual(0,r['actors']['1']['operatingReceiptResultMinor']);self.assertEqual({},r['unclassifiedActorCashMinor'])
