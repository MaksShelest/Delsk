"""Protocol and failure guardrails; actual-engine continuation is a separate integration gate.
Author: Шелестов Максим Евгеньевич.
"""
import copy,gzip,json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from study_postg2_h1 import PLAN,cases,boundaries,valid_checkpoint,classify_failure,digest
from analyze_postg2_h1 import summarize,load_observations
class PostG2StudyTests(unittest.TestCase):
 def setUp(self):self.plan=json.loads(PLAN.read_text())
 def test_preregistered_independent_worlds_and_horizons(self):
  ordinary=cases(self.plan,'ordinary');long=cases(self.plan,'long')
  self.assertEqual(120,len(ordinary));self.assertEqual(8,len(long));self.assertEqual(128,len({c['id'] for c in ordinary+long}))
  self.assertEqual([1800,3600,5000],boundaries(self.plan,ordinary[0]));self.assertEqual(126000,boundaries(self.plan,long[0])[-1])
 def test_failures_and_missing_pairs_are_retained_without_zero_imputation(self):
  case=cases(self.plan,'ordinary')[0];row=dict(case=case,status='blocked_timeout',lastSavedTick=1800,segments=[],frozen={'test':1},sourceCommit='test')
  r=summarize([row],self.plan);self.assertEqual(127,len(r['missing']));self.assertEqual(1,len(r['blocked']));self.assertEqual(12,len(r['pairs']));self.assertTrue(all(p['completePairs']==0 and p['spread'] is None for p in r['pairs']))
 def test_duplicate_seed_or_mixed_binary_is_not_an_extra_replicate(self):
  a=dict(case=cases(self.plan,'ordinary')[0],status='ready',lastSavedTick=0,segments=[],frozen={'binary':'one'},sourceCommit='test');b=copy.deepcopy(a)
  with self.assertRaises(ValueError):summarize([a,b],self.plan)
  b['case']=cases(self.plan,'ordinary')[1];b['frozen']={'binary':'two'}
  with self.assertRaises(ValueError):summarize([a,b],self.plan)
 def test_relocated_checkpoint_is_verified_and_tampering_rejected(self):
  with tempfile.TemporaryDirectory() as d:
   out=Path(d);(out/'inputs').mkdir();(out/'checkpoints').mkdir();(out/'checkpoints/a').write_bytes(b'checkpoint fixture only, not a game pass');(out/'inputs/x').write_text('profile fixture')
   state=dict(lastCheckpoint='checkpoints/a',lastCheckpointSha256=digest(out/'checkpoints/a'),inputs={'x':digest(out/'inputs/x')});valid_checkpoint(out,state)
   (out/'checkpoints/a').write_bytes(b'changed')
   with self.assertRaises(ValueError):valid_checkpoint(out,state)
 def test_runtime_failure_classes_do_not_become_success_or_equivalence(self):
  self.assertEqual('checkpoint_limit',classify_failure(1,'CheckpointTooLarge'))
  self.assertEqual('timeout',classify_failure(124,''));self.assertEqual('process_killed_or_oom_unconfirmed',classify_failure(137,''))

 def test_checkpoint_path_escape_is_rejected(self):
  with tempfile.TemporaryDirectory() as d:
   out=Path(d)/'owned';out.mkdir();outside=Path(d)/'other';outside.write_bytes(b'foreign')
   state=dict(lastCheckpoint='../other',lastCheckpointSha256=digest(outside),inputs={})
   with self.assertRaises(ValueError):valid_checkpoint(out,state)

 def test_owned_compressed_export_restores_shared_fingerprint_and_rejects_other_plan(self):
  row=dict(case=cases(self.plan,'ordinary')[0],status='ready',lastSavedTick=0,segments=[],sourceCommit='test')
  with tempfile.TemporaryDirectory() as d:
   path=Path(d)/'observations.json.gz';path.write_bytes(gzip.compress(json.dumps(dict(formatVersion=2,plan=self.plan,frozen={'runtime':'fixture'},rows=[row])).encode()))
   restored=load_observations(path,self.plan);self.assertEqual({'runtime':'fixture'},restored[0]['frozen']);self.assertEqual(127,len(summarize(restored,self.plan)['missing']))
   wrong=copy.deepcopy(self.plan);wrong['ordinary']['targetTicks']=1800
   with self.assertRaises(ValueError):load_observations(path,wrong)
