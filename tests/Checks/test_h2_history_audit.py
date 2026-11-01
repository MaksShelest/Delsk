"""Historical ground storage privacy: construction may remove an inventory, never its ownership proof.
Author: Шелестов Максим Евгеньевич.
"""
import copy,json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from verify_h2_history import verify
class HistoricalInventoryAuditTests(unittest.TestCase):
 def fixture(self):
  ref=lambda n:dict(value=n)
  pos=dict(x=2,z=3)
  turn=dict(tick=10,actor=ref(1),observation=dict(tick=10,participant=dict(id=ref(1),accountId=ref(11),inventoryId=ref(12)),account=dict(id=ref(11)),inventories=[dict(id=ref(90),position=pos,capacity=70)],employmentGroups=[],enterprises=[],equipment=[],buildings=[]))
  return {'summary.json':dict(tick=20,controllerDecisionTurns=1),'scenario.json':dict(economy=dict(storage=dict(groundCapacity=70)),plots=[dict(id=3,ownerId=None)]),'final-state.json':dict(inventories=[dict(id=ref(12),ownerId=ref(1)),dict(id=ref(13),ownerId=ref(2))],participants=[dict(id=ref(1),accountId=ref(11),inventoryId=ref(12))],plots=[dict(id=ref(3),position=pos,ownerId=ref(1))]),'events.jsonl':[dict(type='PlotPurchased',tick=5,actorId=ref(1),entityId=3)],'decisions.jsonl':[turn]}
 def check(self,data):
  with tempfile.TemporaryDirectory() as path:
   root=Path(path)
   for name,value in data.items():
    (root/name).write_text('\n'.join(map(json.dumps,value))+'\n' if name.endswith('.jsonl') else json.dumps(value))
   return verify(root)
 def test_removed_ground_is_authenticated_by_historical_purchase(self):
  result=self.check(self.fixture());self.assertEqual(result['historicalGroundInventories'],1)
 def test_foreign_inventory_unpurchased_land_and_false_ground_are_rejected(self):
  for mutate in [lambda d:d['decisions.jsonl'][0]['observation']['inventories'][0].update(id=dict(value=13)),lambda d:d['events.jsonl'][0].update(tick=11),lambda d:d['events.jsonl'][0].update(actorId=dict(value=2)),lambda d:d['decisions.jsonl'][0]['observation']['inventories'][0].update(capacity=700),lambda d:d['decisions.jsonl'][0]['observation']['buildings'].append(dict(footprint=[dict(x=2,z=3)]))]:
   data=copy.deepcopy(self.fixture());mutate(data)
   with self.assertRaises(AssertionError):self.check(data)
