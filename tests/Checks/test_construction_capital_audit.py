"""Actual H3 construction receipt regression; unfinished investment remains capital.
Author: Шелестов Максим Евгеньевич.
"""
import copy,json,sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from verify_public_economy import pending_construction_values

class ConstructionCapitalAuditTests(unittest.TestCase):
    def data(self):return json.loads((Path(__file__).parent/'Fixtures/h3-construction-wip.json').read_text())
    def test_actual_unfinished_ordinary_factory_has39500_not_zero_capital(self):
        d=self.data();self.assertEqual(pending_construction_values(d['events'],d['prices']),{1:39500})
        self.assertEqual(d['pending'][0]['paidMinor'],17000)
    def test_completion_or_paid_cancellation_removes_only_that_pending_investment(self):
        for kind in ['StorageConstructionCompleted','StorageConstructionCancelled']:
            d=self.data();job=d['pending'][0]
            d['events'].append(dict(type=kind,entityId=job['id'],actorId={'value':job['owner']},tick=job['completesTick']))
            self.assertEqual(pending_construction_values(d['events'],d['prices']),{})
    def test_duplicate_and_wrong_owner_receipts_cannot_create_capital(self):
        for corruption in ['start','material','owner','closure']:
            d=self.data();start=next(e for e in d['events'] if e['type']=='StorageConstructionStarted' and e['entityId']==d['pending'][0]['id'])
            material=next(e for e in d['events'] if e['type']=='ConstructionMaterialConsumed' and e['commandId']==start['commandId'])
            if corruption=='start':d['events'].append(copy.deepcopy(start))
            elif corruption=='material':d['events'].append(copy.deepcopy(material))
            elif corruption=='owner':material['actorId']={'value':999}
            else:d['events'].append(dict(type='StorageConstructionCompleted',entityId=999999,actorId={'value':1},tick=4000))
            with self.assertRaises(AssertionError):pending_construction_values(d['events'],d['prices'])
