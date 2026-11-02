"""Exercise publication races with real local Git remotes; no GitHub/network.
Author: Шелестов Максим Евгеньевич.
"""
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from publish_h3_measurements import BRANCH,FILES,publish

class H3PublicationTests(unittest.TestCase):
    def git(self,folder,*args):
        return subprocess.run(['git',*args],cwd=folder,text=True,capture_output=True,check=True).stdout.strip()

    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name);self.remote=self.root/'remote.git'
        self.git(self.root,'init','--bare',str(self.remote))
        self.worker=self.root/'worker';self.git(self.root,'clone',str(self.remote),str(self.worker))
        self.git(self.worker,'switch','-c',BRANCH)
        self.author(self.worker)
        (self.worker/'README.md').write_text('original\n')
        self.destination=Path('docs/verification/h3/economy')
        (self.worker/self.destination).mkdir(parents=True)
        (self.worker/self.destination/'summary.json').write_text('original measurement\n')
        self.git(self.worker,'add','.');self.git(self.worker,'commit','-m','original')
        self.git(self.worker,'push','origin',BRANCH)
        self.other=self.root/'other';self.git(self.root,'clone','--branch',BRANCH,str(self.remote),str(self.other));self.author(self.other)
        self.analysis=self.root/'analysis';self.analysis.mkdir()
        for name in FILES:(self.analysis/name).write_bytes(('verified '+name).encode())

    def author(self,folder):
        self.git(folder,'config','user.name','H3 test');self.git(folder,'config','user.email','h3-test@example.invalid')

    def advance(self,path,text):
        (self.other/path).write_text(text);self.git(self.other,'add','--',str(path));self.git(self.other,'commit','-m','concurrent work');self.git(self.other,'push','origin',BRANCH)

    def test_publish_from_old_checkout_keeps_unrelated_new_commit(self):
        self.advance(Path('README.md'),'concurrent owner change\n')
        newer=self.git(self.other,'rev-parse','HEAD')
        self.assertTrue(publish(self.worker,self.analysis,'economy'))
        head=self.git(self.root,'--git-dir='+str(self.remote),'rev-parse',BRANCH)
        self.assertEqual(self.git(self.worker,'rev-parse','HEAD^'),newer)
        self.assertEqual(self.git(self.root,'--git-dir='+str(self.remote),'show',head+':README.md'),'concurrent owner change')
        for name in FILES:self.assertEqual((self.worker/self.destination/name).read_bytes(),(self.analysis/name).read_bytes())

    def test_newer_measurements_are_preserved_without_local_or_remote_overwrite(self):
        before=self.git(self.worker,'rev-parse','HEAD')
        self.advance(self.destination/'summary.json','newer independent collection\n')
        remote=self.git(self.other,'rev-parse','HEAD')
        with self.assertRaisesRegex(ValueError,'Newer H3 measurements'):publish(self.worker,self.analysis,'economy')
        self.assertEqual(self.git(self.worker,'rev-parse','HEAD'),before)
        self.assertEqual(self.git(self.root,'--git-dir='+str(self.remote),'rev-parse',BRANCH),remote)
        self.assertEqual((self.worker/self.destination/'summary.json').read_text(),'original measurement\n')

    def test_dirty_tracked_work_is_not_rebased_or_published(self):
        (self.worker/'README.md').write_text('unsaved user edit\n')
        with self.assertRaisesRegex(ValueError,'Tracked work'):publish(self.worker,self.analysis,'economy')
        self.assertEqual((self.worker/'README.md').read_text(),'unsaved user edit\n')
