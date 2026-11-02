"""H3 evidence collection must reject corruption, path escapes and incomplete cohorts.
Author: Шелестов Максим Евгеньевич.
"""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT=Path(__file__).resolve().parents[2]


class H3CollectionTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name);self.archive=self.root/'world.zip';self.output=self.root/'collected'

    def make(self, extra=None):
        case='ordinary-compact-valley-3-Easy-42'
        with zipfile.ZipFile(self.archive,'w',compression=zipfile.ZIP_STORED) as z:
            z.writestr('world/trajectory.json',json.dumps(dict(case=dict(id=case),status='completed',lastSavedTick=5000)))
            z.writestr('world/runs/5000-a1/h1-metrics.json','{"actual":5000}')
            z.writestr('world/runs/5000-a1/decisions.jsonl','large private raw archive is retained remotely')
            if extra:z.writestr(*extra)
        return self.output/case

    def collect(self):
        return subprocess.run([sys.executable,ROOT/'scripts/collect_h3_artifact.py','--archive',self.archive,'--output',self.output],capture_output=True,text=True,timeout=10)

    def test_selected_original_metadata_and_duplicate_world_protection(self):
        target=self.make();self.assertEqual(self.collect().returncode,0)
        self.assertEqual(json.loads((target/'runs/5000-a1/h1-metrics.json').read_text()),dict(actual=5000))
        self.assertFalse((target/'runs/5000-a1/decisions.jsonl').exists())
        before=(target/'trajectory.json').read_bytes();self.assertNotEqual(self.collect().returncode,0)
        self.assertEqual(before,(target/'trajectory.json').read_bytes())

    def test_archive_path_escape_rejected_without_external_write(self):
        self.make(('world/../../escape/h1-metrics.json','{}'))
        self.assertNotEqual(self.collect().returncode,0)
        self.assertFalse((self.root/'escape/h1-metrics.json').exists())

    def test_duplicate_selected_entry_cannot_replace_an_original_metric(self):
        import warnings
        with warnings.catch_warnings():
            warnings.simplefilter('ignore',UserWarning)
            target=self.make(('world/runs/5000-a1/h1-metrics.json','{"actual":0}'))
        result=self.collect();self.assertNotEqual(result.returncode,0)
        self.assertIn('Duplicate selected archive entry',result.stderr)
        self.assertEqual(json.loads((target/'runs/5000-a1/h1-metrics.json').read_text()),dict(actual=5000))

    def test_selected_crc_corruption_is_never_a_successful_measurement(self):
        self.make()
        with zipfile.ZipFile(self.archive) as z:
            info=z.getinfo('world/runs/5000-a1/h1-metrics.json')
            at=info.header_offset+30+len(info.filename.encode())+len(info.extra)
        raw=bytearray(self.archive.read_bytes());raw[at]^=1;self.archive.write_bytes(raw)
        result=self.collect();self.assertNotEqual(result.returncode,0);self.assertIn('CRC',result.stderr)

    def test_missing_worlds_remain_missing_and_complete_collection_cannot_pass(self):
        empty=self.root/'empty';empty.mkdir()
        command=[sys.executable,ROOT/'scripts/analyze_h3.py','--study',empty,'--output',self.root/'analysis']
        result=subprocess.run(command,capture_output=True,text=True,timeout=10);self.assertEqual(result.returncode,0,result.stderr)
        summary=json.loads((self.root/'analysis/summary.json').read_text())
        self.assertEqual(summary['recordedWorlds'],0);self.assertEqual(len(summary['missing']),192)
        self.assertEqual(len(summary['pairs']),18);self.assertTrue(all(g['completeWorlds']==0 for g in summary['groups']))
        result=subprocess.run(command+['--require-complete-collection'],capture_output=True,text=True,timeout=10)
        self.assertNotEqual(result.returncode,0)


if __name__=='__main__':unittest.main()
