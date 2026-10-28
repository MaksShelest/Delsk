"""CI boundaries: preserve exhaustive tests and refuse stale or incomplete import caches.
Author: Шелестов Максим Евгеньевич.
"""
from pathlib import Path
import sys
import hashlib
import tempfile
import unittest
import xml.etree.ElementTree as ET

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from reuse_godot_imports import reuse
from test_partitions import PARTITIONS, INTEGRATION, DEVELOPMENT, PHYSICAL, MODELS, FLEET, FLEET_STOCK, ROADS, TRAFFIC, ROAD_FREIGHT, ROAD_HISTORY, RUNS, SERVICES, BILLING, BILLING_DISTRIBUTION, BILLING_TIMING, selected_partitions, run_suite

class PartitionsTests(unittest.TestCase):
    def test_every_class_including_future_and_developer_has_exactly_one_partition(self):
        for name in (*INTEGRATION,*DEVELOPMENT,*PHYSICAL,*MODELS,*FLEET,*FLEET_STOCK,*ROADS,*TRAFFIC,*ROAD_FREIGHT,*ROAD_HISTORY,*SERVICES,*RUNS,*BILLING,*BILLING_DISTRIBUTION,*BILLING_TIMING,'Simulation.Tests.FutureNewTests','Simulation.Tests.Developer.DeveloperToolsTests'):
            self.assertEqual(sum(p.matches(name) for p in PARTITIONS),1,name)

    def test_disjoint_jobs_preserve_every_partition_and_deadlines(self):
        old=set(selected_partitions('previous'));roads=set(selected_partitions('roads'));billing=set(selected_partitions('billing'));services=set(selected_partitions('services'));runs=set(selected_partitions('runs'));new=roads|billing|services|runs
        self.assertTrue(runs);self.assertFalse(runs&(roads|billing|services));self.assertFalse(roads&billing);self.assertTrue(billing)
        self.assertTrue(services);self.assertFalse(services&(roads|billing))
        self.assertFalse(old&new);self.assertEqual(old|new,set(PARTITIONS))
        self.assertTrue(new);self.assertEqual(len(old),9)

    def test_all_actual_theory_rows_count_and_bad_result_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            def write(name,timeout,cmd):
                target=Path(cmd[-1]); part=next(p for p in PARTITIONS if name=='tests'+p.name)
                cls=part.include[0] if part.include else 'Simulation.Tests.FutureNewTests'
                root=ET.Element('TestRun');defs=ET.SubElement(root,'TestDefinitions');results=ET.SubElement(root,'Results')
                for n in range(2):
                    identity=cls+str(n);unit=ET.SubElement(defs,'UnitTest',id=identity);ET.SubElement(unit,'TestMethod',className=cls)
                    ET.SubElement(results,'UnitTestResult',testId=identity)
                ET.SubElement(ET.SubElement(root,'ResultSummary'),'Counters',passed='2',failed='0',notExecuted='0')
                ET.ElementTree(root).write(target)
            reports=run_suite(write,'dotnet','assembly',folder)
            self.assertEqual(sum(p['passed'] for p in reports),2*len(PARTITIONS))
            def wrong(name,timeout,cmd):
                write(name,timeout,cmd);p=Path(cmd[-1]);root=ET.parse(p);next(e for e in root.iter() if e.tag=='Counters').set('notExecuted','1');root.write(p)
            with self.assertRaisesRegex(SystemExit,'Incomplete'):run_suite(wrong,'dotnet','assembly',folder)
            def duplicate(name,timeout,cmd):
                write(name,timeout,cmd)
                if name=='tests':return
                path=Path(cmd[-1]);root=ET.parse(path);key='Simulation.Tests.FutureNewTests0'
                next(e for e in root.iter() if e.tag=='UnitTest').set('id',key)
                next(e for e in root.iter() if e.tag=='UnitTestResult').set('testId',key);root.write(path)
            with self.assertRaisesRegex(SystemExit,'overlapping'):run_suite(duplicate,'dotnet','assembly',folder)

class ImportCacheTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup);self.root=Path(self.temp.name);self.source=self.root/'source';self.target=self.root/'target'
        for path in [self.source,self.target]:
            (path/'art').mkdir(parents=True);(path/'art/icon.svg').write_text('<svg/>')
        self.cache=self.source/'.godot/imported/icon.svg-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.ctex';self.cache.parent.mkdir(parents=True);self.cache.write_bytes(b'imported texture')
        self.cache.with_suffix('.md5').write_text('source_md5="'+hashlib.md5(b'<svg/>',usedforsecurity=False).hexdigest()+'"\n')
        (self.source/'art/icon.svg.import').write_text('[remap]\npath="res://.godot/imported/icon.svg-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.ctex"\n[deps]\nsource_file="res://art/icon.svg"\n')

    def test_identical_assets_reuse_only_resources_and_leave_developer_assemblies_intact(self):
        binary=self.target/'.godot/mono/temp/bin/Debug/Simulation.Developer.dll';binary.parent.mkdir(parents=True);binary.write_bytes(b'developer')
        report=reuse(self.source,self.target)
        self.assertEqual(len(report['assets']),1);self.assertEqual(binary.read_bytes(),b'developer')
        self.assertEqual((self.target/'.godot/imported/icon.svg-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.ctex').read_bytes(),b'imported texture')
        self.assertTrue((self.target/'art/icon.svg.import').is_file())

    def test_verification_is_read_only_and_rejects_unimported_assets(self):
        before={p.relative_to(self.source):p.read_bytes() for p in self.source.rglob('*') if p.is_file()}
        report=reuse(self.source,self.source,verify_only=True)
        self.assertTrue(report['verifyOnly']);self.assertEqual(len(report['assets']),1)
        self.assertEqual(before,{p.relative_to(self.source):p.read_bytes() for p in self.source.rglob('*') if p.is_file()})
        (self.source/'art/not-imported.wav').write_bytes(b'raw audio')
        with self.assertRaisesRegex(ValueError,'Missing import metadata'):reuse(self.source,self.source,verify_only=True)
        with self.assertRaisesRegex(ValueError,'Missing import metadata'):reuse(self.source,self.target)
        self.assertFalse((self.target/'art/icon.svg.import').exists())

    def test_changed_source_refuses_cache_before_copying_anything(self):
        (self.target/'art/icon.svg').write_text('changed')
        with self.assertRaisesRegex(ValueError,'differs'):reuse(self.source,self.target)
        self.assertFalse((self.target/'.godot/imported').exists())

    def test_source_changed_after_import_is_rejected_even_if_stage_matches(self):
        for base in (self.source,self.target):(base/'art/icon.svg').write_text('<svg>new</svg>')
        with self.assertRaisesRegex(ValueError,'Stale import'):reuse(self.source,self.target)

    def test_missing_cached_binary_is_fatal(self):
        self.cache.unlink()
        with self.assertRaisesRegex(ValueError,'Missing'):reuse(self.source,self.target)

    def test_wrong_source_metadata_and_empty_cache_are_fatal(self):
        (self.source/'art/icon.svg.import').write_text('source_file="res://another.svg"')
        with self.assertRaisesRegex(ValueError,'Invalid import source'):reuse(self.source,self.target)
        (self.source/'art/icon.svg.import').unlink()
        with self.assertRaisesRegex(ValueError,'Missing import metadata'):reuse(self.source,self.target)

if __name__=='__main__':unittest.main()
