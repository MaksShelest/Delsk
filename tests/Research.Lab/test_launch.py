"""Startup and HTTP handoff tests; the runner's simulation is not reimplemented.
Author: Шелестов Максим Евгеньевич.
"""
import copy
import io
import json
from pathlib import Path
import re
import sys
import tempfile
import threading
import unittest
import urllib.error
import urllib.request
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2]/'scripts'))
from research_lab import DEFAULT, Lab, main, write
from research_lab_launch import prepare, reopen
from research_lab_presets import MAPS, presets
from research_lab_server import make_server


class StartupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def test_preparation_uses_locked_restore_and_stops_on_failure(self):
        version = json.loads((Path(__file__).resolve().parents[2]/'global.json').read_text())['sdk']['version']
        with patch('research_lab_launch.prepare_dotnet'), patch('research_lab_launch.shutil.which', return_value='dotnet'), \
             patch('research_lab_launch.subprocess.run') as sdk, patch('research_lab_launch.run_checked') as run:
            sdk.return_value.returncode = 0
            sdk.return_value.stdout = version+'\n'
            run.return_value = (124, {})
            with self.assertRaisesRegex(ValueError, '124'):
                prepare(self.root)
            self.assertEqual(1, run.call_count)
            self.assertIn('--locked-mode', run.call_args.args[3])
            run.reset_mock(); run.return_value = (0, {})
            prepare(self.root)
            self.assertEqual(2, run.call_count)
            self.assertIn('--no-restore', run.call_args.args[3])

    def test_missing_or_wrong_sdk_has_actionable_message(self):
        with patch('research_lab_launch.prepare_dotnet'), patch('research_lab_launch.shutil.which', return_value=None):
            with self.assertRaisesRegex(ValueError, '.NET SDK'):
                prepare(self.root)
        with patch('research_lab_launch.prepare_dotnet'), patch('research_lab_launch.shutil.which', return_value='dotnet'), \
             patch('research_lab_launch.subprocess.run') as sdk, patch('research_lab_launch.run_checked') as run:
            sdk.return_value.returncode = 1; sdk.return_value.stderr = 'SDK missing'
            with self.assertRaisesRegex(ValueError, 'SDK missing'):
                prepare(self.root)
            run.assert_not_called()

    def test_startup_failure_releases_queue_lock_for_next_attempt(self):
        with patch('research_lab_launch.prepare', side_effect=ValueError('restore failed')):
            self.assertEqual(1, main(['--prepare', '--output', str(self.root)]))
        with patch('research_lab_launch.require_runner'):
            self.assertEqual(0, main(['--run', '--output', str(self.root)]))

    def test_reopen_never_visits_arbitrary_or_stale_urls(self):
        for url in ['https://example.com/', 'http://localhost:1234/', 'file:///tmp/a',
                    'http://127.0.0.1:1234/x', 'http://user@127.0.0.1:1234/']:
            write(self.root/'host.json', dict(url=url, instance='id'))
            with patch('research_lab_launch.urllib.request.build_opener') as client, patch('research_lab_launch.open_browser') as browser:
                self.assertFalse(reopen(self.root)); client.assert_not_called(); browser.assert_not_called()
        write(self.root/'host.json', dict(url='http://127.0.0.1:1234/', instance='expected'))
        with patch('research_lab_launch.urllib.request.build_opener') as client, patch('research_lab_launch.open_browser') as browser:
            client.return_value.open.return_value.__enter__.return_value.read.return_value = b'{"application":"Delsk Research Lab","instance":"stale"}'
            self.assertFalse(reopen(self.root)); browser.assert_not_called()


class HttpTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.lab = Lab(Path(self.temp.name), lambda *args: (0, {}))
        self.server = make_server(self.lab, 0)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True); self.thread.start()
        self.url = f'http://127.0.0.1:{self.server.server_port}/'
        with urllib.request.urlopen(self.url, timeout=3) as response:
            self.token = re.search("const token='([^']+)'", response.read().decode())[1]

    def tearDown(self):
        self.server.shutdown(); self.server.server_close(); self.thread.join(3)

    def get(self, path, authenticated=True):
        headers = {'X-Lab-Token': self.token} if authenticated else {}
        with urllib.request.urlopen(urllib.request.Request(self.url+path, headers=headers), timeout=3) as response:
            return response.read()

    def test_presets_expand_on_actual_catalog_and_keep_source_default_unchanged(self):
        from research_lab import expand
        original = copy.deepcopy(DEFAULT)
        catalog = json.loads(self.get('api/catalog'))
        self.assertEqual(4, len(catalog['presets']))
        for preset in presets():
            for choice in MAPS:
                _, _, arms = expand(preset['plan'] | dict(scenario=choice['path']), self.lab.workspace)
                self.assertGreaterEqual(len(arms), 1)
        self.assertEqual(original, DEFAULT)
        self.assertEqual(1, len(presets()[0]['plan']['seeds']))
        self.assertEqual([], presets()[0]['plan']['factors'])

    def test_reopen_verifies_live_instance(self):
        write(self.lab.workspace/'host.json', dict(url=self.url, instance=self.server.instance_id))
        with patch('research_lab_launch.open_browser') as browser:
            self.assertTrue(reopen(self.lab.workspace)); browser.assert_called_once_with(self.url)

    def test_archive_download_preserves_files_and_requires_token(self):
        key = self.lab.enqueue(copy.deepcopy(DEFAULT) | dict(seeds=[42]))
        series = self.lab.series[0]; series['jobs'][0]['status'] = 'succeeded'
        folder = self.lab.workspace/key
        write(folder/'sample.json', dict(exact=42))
        for path in ['archives/'+key+'.zip', 'files/'+key+'/plan.json']:
            with self.assertRaises(urllib.error.HTTPError) as error:
                self.get(path, authenticated=False)
            self.assertEqual(403, error.exception.code)
        body = self.get('archives/'+key+'.zip')
        with zipfile.ZipFile(io.BytesIO(body)) as archive:
            self.assertIsNone(archive.testzip())
            self.assertEqual((folder/'plan.json').read_bytes(), archive.read(key+'/plan.json'))
            self.assertEqual(dict(exact=42), json.loads(archive.read(key+'/sample.json')))
            self.assertTrue(all(not Path(name).is_absolute() and '..' not in Path(name).parts for name in archive.namelist()))

    def test_archive_rejects_unknown_series_and_active_job(self):
        for path in ['archives/unknown.zip', 'archives/..%2Foutside.zip']:
            with self.assertRaises(urllib.error.HTTPError) as error:
                self.get(path)
            self.assertEqual(400, error.exception.code)
        key = self.lab.enqueue(copy.deepcopy(DEFAULT) | dict(seeds=[42]))
        self.lab.series[0]['jobs'][0]['status'] = 'running'
        with self.assertRaises(urllib.error.HTTPError) as error:
            self.get('archives/'+key+'.zip')
        self.assertEqual(400, error.exception.code)

    def test_failed_runner_logs_remain_downloadable_for_diagnosis(self):
        def executor(name, timeout, logs, command):
            if name.startswith('validate'):
                return 0, {}
            logs.mkdir(parents=True, exist_ok=True)
            (logs/(name+'.stderr.log')).write_text('deliberate runner failure')
            return 124, dict(timed_out=True)
        self.lab.executor = executor
        self.lab.enqueue(copy.deepcopy(DEFAULT) | dict(seeds=[42]))
        self.lab.start(); worker = self.lab.thread
        if worker:
            worker.join(5)
        job = self.lab.series[0]['jobs'][0]
        self.assertEqual('failed', job['status'])
        self.assertEqual(1, len(job['logs']))
        self.assertEqual(b'deliberate runner failure', self.get('files/'+job['logs'][0]))

    def test_shutdown_requires_token_and_foreign_origin_is_rejected(self):
        for headers in [{}, {'X-Lab-Token':self.token, 'Origin':'https://outside.invalid'}]:
            request = urllib.request.Request(self.url+'api/shutdown', data=b'{}', headers=headers)
            with self.assertRaises(urllib.error.HTTPError) as error:
                urllib.request.urlopen(request, timeout=3)
            self.assertEqual(403, error.exception.code)
        self.assertEqual('Delsk Research Lab', json.loads(self.get('api/health', authenticated=False))['application'])

    def test_stopping_host_does_not_admit_another_start_or_queue_request(self):
        self.server.stopping.set()
        for path in ['api/start', 'api/resume', 'api/retry', 'api/queue']:
            request = urllib.request.Request(self.url+path, data=b'{}', headers={'X-Lab-Token':self.token})
            with self.assertRaises(urllib.error.HTTPError) as error:
                urllib.request.urlopen(request, timeout=3)
            self.assertEqual(409, error.exception.code)
        self.assertTrue(self.lab.paused)
        self.assertEqual([], self.lab.series)


if __name__ == '__main__':
    unittest.main()
