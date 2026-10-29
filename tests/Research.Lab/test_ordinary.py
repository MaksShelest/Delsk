"""Ordinary resolver inputs cannot silently fall back to legacy policies. Шелестов Максим Евгеньевич."""
import copy,json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from research_lab import Lab,expand,command,write,read
from research_workspace import compatible
class OrdinaryTests(unittest.TestCase):
 def setUp(self):
  self.tmp=tempfile.TemporaryDirectory();self.root=Path(self.tmp.name)
  self.plan=dict(version=1,id='ordinary',scenario='scenarios/vnext/small-river.json',economy='config/vnext/economy.g1-npc.json',ordinaryNpcCount=6,ticks=60,seeds=[42],repetitions=1,mode='algorithmic',recordWorld=False,factors=[dict(path='npc.difficulty',values=['Easy','Medium','Hard'])])
 def tearDown(self):self.tmp.cleanup()
 def test_modern_policy_source_is_the_frozen_profile(self):
  for n in [3,6,10]:
   _,_,arms=expand(self.plan|dict(ordinaryNpcCount=n),self.root)
   self.assertEqual(['Easy','Medium','Hard'],[a['ordinary']['difficulty'] for a in arms]);self.assertTrue(all(a['economy']['npc']['coherentEconomy'] for a in arms));self.assertTrue(all(a['ordinary']['npcCount']==n for a in arms))
 def test_cli_uses_new_game_without_legacy_seed_or_controllers(self):
  lab=Lab(self.root,lambda *a:(0,{}));key=lab.enqueue(self.plan);s=lab.series[0];j=s['jobs'][2]
  self.assertEqual((Path(__file__).resolve().parents[2]/'config/vnext/balance.b1.json').read_bytes(),(self.root/key/'inputs/arm-0/balance.b1.json').read_bytes())
  cmd=command(self.root/key,j,self.root/'out');self.assertNotIn('--controllers',cmd);self.assertNotIn('--seed',cmd);self.assertNotIn('--model-wait',cmd)
  options=json.loads(Path(cmd[cmd.index('--new-game')+1]).read_text());self.assertEqual(dict(npcCount=6,difficulty='Hard',seed=42),options)
 def test_mixed_overrides_and_ignored_factors_are_rejected(self):
  for change in [dict(controllers='scenarios/baseline-controllers.json'),dict(participants=[]),dict(composition=[]),dict(ordinaryNpcCount=True),dict(ordinaryNpcCount=1),dict(ordinaryNpcCount=2),dict(ordinaryNpcCount=11),dict(factors=[dict(path='npc.negotiationsEnabled',values=[True])])]:
   with self.subTest(change=change),self.assertRaises(ValueError):expand(self.plan|change,self.root)
 def test_pairing_keeps_roster_and_declared_difficulty_distinct(self):
  lab=Lab(self.root,lambda *a:(0,{}));a=lab.enqueue(self.plan);b=lab.enqueue(self.plan|dict(ordinaryNpcCount=10))
  left,right=lab.series;self.assertTrue(compatible(lab,left,left,'arm-0','arm-2'));self.assertFalse(compatible(lab,left,right,'arm-0','arm-0'))
 def test_no_mutation_of_caller_plan(self):
  before=copy.deepcopy(self.plan);expand(self.plan,self.root);self.assertEqual(before,self.plan)
 def test_large_persistent_queue_reopens_without_relaxing_plan_input_limit(self):
  lab=Lab(self.root,lambda *a:(0,{}));lab.enqueue(self.plan);lab.series[0]['jobs'][0]['metrics']=dict(retainedDiagnostic='x'*(2*1024*1024));lab.save()
  with self.assertRaises(ValueError):read(self.root/'queue.json')
  reopened=Lab(self.root,lambda *a:(0,{}));self.assertEqual(lab.series,reopened.series)
