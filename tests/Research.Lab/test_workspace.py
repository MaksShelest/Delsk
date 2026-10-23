"""Research isolation, history and control boundaries; never a simulated economy.
Author: Шелестов Максим Евгеньевич.
"""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from research_lab import Lab,DEFAULT,expand,read,write
from research_workspace import validate_recording,model_tags


class WorkspaceTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        self.lab=Lab(self.root,lambda *args:(0,{}))
        self.plan=copy.deepcopy(DEFAULT)|dict(ticks=30,seeds=[42],factors=[],recordWorld=False)
        self.key=self.lab.enqueue(self.plan);self.row=self.lab.series[0]
        self.folder=self.root/self.key

    def test_participant_settings_copy_without_changing_source_or_economy(self):
        base,_,arms=expand(self.plan,self.root)
        members=[dict(id=i+1,cashMinor=3725,inventoryCapacity=31,position=p['position'],preferredInput='wood',difficulty='Hard') for i,p in enumerate(base['participants'][:3])]
        scenario,_,resolved=expand(self.plan|dict(participants=members),self.root)
        self.assertEqual(base,scenario)
        self.assertEqual(2,len(resolved[0]['controllers']['npcs']))
        self.assertEqual(3725,resolved[0]['setup']['participants'][0]['cashMinor'])
        self.assertEqual(arms[0]['economy'],resolved[0]['economy'])

    def test_invalid_start_count_cash_and_duplicate_positions_fail(self):
        base,_,_=expand(self.plan,self.root)
        members=[dict(id=i+1,cashMinor=10,inventoryCapacity=20,position=p['position']) for i,p in enumerate(base['participants'])]
        for change in ('cash','count','duplicate','boolean'):
            value=copy.deepcopy(members)
            if change=='cash':value[0]['cashMinor']=1.2
            if change=='count':value=value[:1]
            if change=='duplicate':value[1]['position']=value[0]['position']
            if change=='boolean':value[0]['id']=True
            with self.subTest(change=change),self.assertRaises(ValueError):expand(self.plan|dict(participants=value),self.root)

    def test_economy_copy_never_changes_source(self):
        _,_,arms=expand(self.plan,self.root)
        economy=copy.deepcopy(arms[0]['economy']);economy['world']['population']=230
        _,_,other=expand(self.plan|dict(economyData=economy),self.root)
        other[0]['economy']['world']['population']=999
        self.assertEqual(230,economy['world']['population'])
        self.assertNotEqual(230,arms[0]['economy']['world']['population'])

    def test_archived_queue_is_not_executed_and_restore_is_explicit(self):
        self.lab.views.manage(dict(action='archive',id=self.key))
        self.lab.start()
        thread=self.lab.thread
        if thread:thread.join(2)
        self.assertEqual('queued',self.row['jobs'][0]['status'])
        self.lab.views.manage(dict(action='restore',id=self.key))
        self.assertFalse(self.row['archived'])

    def test_running_series_cannot_be_deleted_or_archived(self):
        self.row['jobs'][0]['status']='running'
        for action in ('delete','archive'):
            with self.assertRaises(ValueError):self.lab.views.manage(dict(action=action,id=self.key))
        self.assertTrue(self.folder.exists())

    def test_delete_removes_only_the_known_series(self):
        other=self.root/'unrelated.txt';other.write_text('keep')
        self.lab.views.manage(dict(action='delete',id=self.key))
        self.assertFalse(self.folder.exists());self.assertTrue(other.exists());self.assertFalse(self.lab.series)

    def test_preview_rejects_traversal_links_and_pages_large_utf8(self):
        with self.assertRaises(ValueError):self.lab.views.preview(self.key,'../../queue.json')
        path=self.folder/'large.jsonl';path.write_text('я'*40000,encoding='utf-8')
        result=self.lab.views.preview(self.key,'large.jsonl')
        self.assertTrue(result['hasMore']);self.assertLessEqual(len(result['text'].encode()),65536)
        target=self.root/'private.json';write(target,dict(secret=True))
        link=self.folder/'link.json';link.symlink_to(target)
        with self.assertRaises(ValueError):self.lab.views.preview(self.key,'link.json')

    def test_cancel_writes_control_and_pauses_the_queue(self):
        job=self.row['jobs'][0];job.update(status='running',attempt=1)
        self.lab.views.control(self.key,'cancelled')
        self.assertTrue(self.lab.paused)
        self.assertEqual('cancelled',read(self.folder/'runs'/job['id']/'1/control.json')['action'])

    def test_binary_replacement_fails_before_runner_execution(self):
        self.row['binaries']={'different.dll':'changed'}
        self.lab.start()
        thread=self.lab.thread
        if thread:thread.join(2)
        self.assertEqual('failed',self.row['jobs'][0]['status'])
        self.assertIn('binaries',self.row['jobs'][0]['error'])

    def test_incomplete_or_wrong_final_frame_cannot_enter_statistics(self):
        out=self.folder/'frames';out.mkdir()
        write(out/'summary.json',dict(tick=1,stateHash='final'))
        data=(json.dumps(dict(tick=0,stateHash='zero'))+'\n').encode()
        (out/'world-frames.jsonl').write_bytes(data)
        (out/'world-index.jsonl').write_text(json.dumps(dict(tick=0,offset=0,length=len(data)))+'\n')
        with self.assertRaises(ValueError):validate_recording(out)
        write(out/'summary.json',dict(tick=0,stateHash='different'))
        with self.assertRaises(ValueError):validate_recording(out)
        write(out/'summary.json',dict(tick=0,stateHash='zero'))
        validate_recording(out)

    def test_different_undeclared_economy_or_composition_is_not_paired(self):
        _,_,arms=expand(self.plan,self.root)
        economy=arms[0]['economy'];economy['world']['population']=230
        key=self.lab.enqueue(self.plan|dict(economyData=economy))
        for s in self.lab.series:s['jobs'][0].update(status='succeeded',metrics=dict(capitalMinor=100))
        self.assertEqual([],self.lab.compare([self.key,key])['pairedRightMinusLeft'])

    def test_model_discovery_does_not_visit_remote_urls(self):
        for url in ('https://example.com/','http://localhost:11434/redirect','http://user@127.0.0.1:11434/'):
            with self.assertRaises(ValueError):model_tags(url)

    def test_chart_downsampling_keeps_every_actor_and_final_sample(self):
        job=self.row['jobs'][0];job['attempt']=1
        folder=self.folder/'runs'/job['id']/'1';folder.mkdir(parents=True)
        with (folder/'agents.csv').open('w') as stream:
            stream.write('tick,actor_id,cash_minor,stock_base_value_minor,headcount\n')
            for tick in range(1,601):
                for actor in (2,3,4):stream.write(f'{tick},{actor},100,200,1\n')
        chart=self.lab.views.chart(self.key,job['id'],1)
        self.assertEqual({'2','3','4'},{r['actor_id'] for r in chart['rows']})
        self.assertEqual(3,sum(r['tick']=='600' for r in chart['rows']))
        self.assertLess(len(chart['rows']),510)

    def test_model_contrast_is_declared_and_different_timing_is_not_paired(self):
        key=self.lab.enqueue(self.plan|dict(mode='hybrid',modelData=dict(bindings={'2':None,'3':None,'4':None})))
        for s in self.lab.series:s['jobs'][0].update(status='succeeded',metrics=dict(capitalMinor=100))
        paired=self.lab.compare([self.key,key])['pairedRightMinusLeft']
        self.assertEqual(1,len(paired))
        self.assertEqual(['algorithmic','hybrid'],paired[0]['declaredContrasts']['mode'])
        self.lab.series[1]['jobs'][0]['modelWait']=False
        self.assertEqual([],self.lab.compare([self.key,key])['pairedRightMinusLeft'])


if __name__=='__main__':unittest.main()
