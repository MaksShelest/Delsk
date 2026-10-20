"""Queue/factor boundary tests without a second simulation. Шелестов Максим Евгеньевич."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import threading
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[2]/'scripts'))
from research_lab import DEFAULT, Lab, expand, values, write, command


class LabTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.plan = copy.deepcopy(DEFAULT)

    def test_factor_changes_only_negotiation_not_trade_or_politics(self):
        scenario, model, arms = expand(self.plan,self.root)
        a,b=arms
        self.assertEqual(a['economy'],b['economy'])
        for x,y in zip(a['controllers']['npcs'],b['controllers']['npcs']):
            self.assertTrue(x['tradeEnabled'] and y['tradeEnabled'])
            self.assertTrue(x['politicsEnabled'] and y['politicsEnabled'])
            self.assertTrue(x.get('futureContractsEnabled',True) and y.get('futureContractsEnabled',True))
            self.assertEqual({k:v for k,v in x.items() if k!='negotiationsEnabled'},
                             {k:v for k,v in y.items() if k!='negotiationsEnabled'})
            self.assertNotEqual(x['negotiationsEnabled'],y['negotiationsEnabled'])

    def test_inclusive_range_and_conflicting_or_wrong_values(self):
        self.assertEqual([20,30,40],values(dict(path='npc.maximumWageMinor',range=dict(start=20,stop=40,step=10))))
        for factor in [dict(path='npc.maximumWageMinor',values=[True]),dict(path='world.secret',values=[1]),
                       dict(path='npc.difficulty',values=['Impossible']),dict(path='npc.negotiationsEnabled',values=[False,False]),
                       dict(path='npc.maximumWageMinor',range=dict(start=0,stop=20,step=0))]:
            with self.subTest(factor=factor),self.assertRaises(ValueError):values(factor)

    def test_invalid_plan_and_composition_fail_before_any_execution(self):
        for change in [dict(seeds=[42,42]),dict(ticks=True),dict(repetitions=0),dict(modelConfig='x'),
                       dict(mode='hybrid'),dict(composition=[dict(actorId=2,preferredInput='wood')]),
                       dict(factors=self.plan['factors']*2),dict(seeds=[-1]),dict(unknown=0)]:
            with self.subTest(change=change),self.assertRaises((ValueError,TypeError)):
                expand(self.plan|change,self.root)
        with self.assertRaises(ValueError):expand(self.plan|dict(repetitions=500,seeds=list(range(11))),self.root)

    def test_command_matches_existing_runner_options(self):
        cmd=command(self.root,dict(arm='arm-0',mode='algorithmic',ticks=60,seed=42),self.root/'out')
        self.assertEqual('--scenario',cmd[2]);self.assertIn('--controllers',cmd)
        self.assertNotIn('run',cmd);self.assertEqual('baseline',cmd[cmd.index('--script')+1])

    def executor(self,name,timeout,logs,cmd):
        return 0,dict(exit_code=0)

    def test_rejection_of_later_arm_publishes_no_partial_series(self):
        calls=[]
        def reject(name,*args):
            calls.append(name);return (1 if len(calls)==2 else 0),{}
        lab=Lab(self.root,reject)
        with self.assertRaises(ValueError):lab.enqueue(self.plan)
        self.assertEqual(2,len(calls));self.assertEqual([],lab.series)
        self.assertFalse(any(p.name.startswith(self.plan['id']) for p in self.root.iterdir()))

    def test_validated_input_copies_are_independent_and_tampering_fails(self):
        lab=Lab(self.root,self.executor);key=lab.enqueue(self.plan)
        s=lab.series[0]
        self.assertEqual(6,len(s['jobs']))
        profile=self.root/key/'inputs/arm-0/economy.json'
        write(profile,{'changed':True})
        lab.start();self.wait(lab)
        self.assertEqual('failed',s['jobs'][0]['status']);self.assertIn('snapshot',s['jobs'][0]['error'])

    def wait(self,lab):
        thread=lab.thread
        if thread:thread.join(5)
        self.assertIsNone(lab.thread)

    def test_pause_boundary_retry_and_restart_do_not_duplicate_success(self):
        entered,release=threading.Event(),threading.Event()
        calls=[]
        def execute(name,timeout,logs,cmd):
            if name.startswith('validate'):return 0,{}
            calls.append(name);entered.set();release.wait(3)
            return 124,{'timed_out':True}
        lab=Lab(self.root,execute);lab.enqueue(self.plan|dict(seeds=[42]));lab.start()
        self.assertTrue(entered.wait(2));lab.pause();release.set();self.wait(lab)
        self.assertEqual(['failed','queued'],[j['status'] for j in lab.series[0]['jobs']])
        restored=Lab(self.root,self.executor)
        self.assertTrue(restored.paused)
        self.assertEqual(1,restored.series[0]['jobs'][0]['attempt'])
        # Crash during an admitted run: explicit retry, new attempt directory.
        restored.series[0]['jobs'][0]['status']='running';restored.save()
        recovered=Lab(self.root,self.executor)
        self.assertEqual('interrupted',recovered.series[0]['jobs'][0]['status'])
        self.assertEqual('queued',recovered.series[0]['jobs'][1]['status'])
        self.assertEqual(1,len(calls))

    def test_comparison_excludes_failed_cells_and_reports_sample_spread(self):
        lab=Lab(self.root,self.executor);lab.enqueue(self.plan)
        jobs=lab.series[0]['jobs']
        jobs[0].update(status='succeeded',metrics=dict(capitalMinor=100,modelLatencyP50Ms=None))
        jobs[1].update(status='succeeded',metrics=dict(capitalMinor=200))
        jobs[2].update(status='failed',metrics=dict(capitalMinor=999999))
        group=lab.compare([lab.series[0]['id']])['groups'][0]
        self.assertEqual(2,group['complete']);self.assertEqual(150,group['metrics']['capitalMinor']['median'])
        self.assertEqual(2,group['metrics']['capitalMinor']['n'])
        self.assertNotIn('modelLatencyP50Ms',group['metrics'])

    def test_explicit_retry_gets_new_attempt_and_does_not_reissue_completed_cells(self):
        from unittest.mock import patch
        calls=[]
        def execute(name,timeout,logs,cmd):
            if name.startswith('validate'):return 0,{}
            calls.append(name)
            Path(cmd[cmd.index('--output')+1]).mkdir(parents=True)
            return (124 if len(calls)==1 else 0),{}
        lab=Lab(self.root,execute);lab.enqueue(self.plan|dict(seeds=[42]))
        with patch('research_lab.measure',return_value=dict(capitalMinor=100)):
            lab.start();self.wait(lab)
            self.assertEqual(['failed','succeeded'],[j['status'] for j in lab.series[0]['jobs']])
            lab.start(retry=True);self.wait(lab)
        self.assertEqual([2,1],[j['attempt'] for j in lab.series[0]['jobs']])
        self.assertEqual(3,len(calls));self.assertIn('-a2',calls[-1])


if __name__=='__main__':unittest.main()
