"""R1 launch, city-plan and export-reading boundaries. No second economy.
Author: Шелестов Максим Евгеньевич.
"""
import copy
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from io import StringIO
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2]/'scripts'))
from research_city_report import report
from research_lab import DEFAULT, Lab, command, expand, main
from research_lab_launch import open_browser, prepare
from research_lab_presets import city_plan


class CityPlanTests(unittest.TestCase):
    def test_current_ordinary_horizon_is_not_capped_by_profile_max_ticks(self):
        scenario, _model, arms = expand(city_plan(ticks=8000), Path('.'))
        self.assertEqual('compact_valley_i6c21', scenario['id'])
        self.assertEqual('vnext-i6c72-wages-1', arms[0]['economy']['id'])
        self.assertEqual(dict(npcCount=3, difficulty='Medium'), arms[0]['ordinary'])
        self.assertEqual(5000, arms[0]['economy']['world']['maxTicks'])

    def test_limits_and_recording_conflicts_fail_before_a_run(self):
        with self.assertRaisesRegex(ValueError, '1000000'):
            expand(city_plan(ticks=1000001), Path('.'))
        plan = city_plan()
        plan['timeoutSeconds'] = 14401
        with self.assertRaisesRegex(ValueError, '14400'):
            expand(plan, Path('.'))
        plan = city_plan(recording='brief')
        plan['recordWorld'] = True
        with self.assertRaisesRegex(ValueError, 'recordWorld'):
            expand(plan, Path('.'))
        old = copy.deepcopy(DEFAULT)
        old['ticks'] = 10001
        with self.assertRaisesRegex(ValueError, '10000'):
            expand(old, Path('.'))

    def test_economy_sample_does_not_turn_on_world_frames(self):
        root = Path('.')
        cmd = command(root, dict(arm='arm-0', mode='algorithmic', ticks=120, seed=42, recordWorld=False, economySample=True), root/'out')
        self.assertIn('--economy-sample', cmd)
        self.assertNotIn('--research', cmd)
        self.assertEqual('true', cmd[cmd.index('--economy-sample')+1])


class LaunchTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def test_dirty_tree_is_not_stored_as_a_reproducible_commit(self):
        previous = os.environ.get('DELSK_COMMIT')
        sha = 'a'*40
        try:
            with patch('research_lab_launch.git_head', return_value=sha), patch('research_lab_launch.git_dirty', return_value=True):
                lab = Lab(self.root, lambda *args: (0, {}))
            self.assertEqual(sha, lab.commit)
            self.assertTrue(lab.commit_dirty)
            self.assertFalse(lab.commit_verified)
            self.assertEqual('unverified', os.environ['DELSK_COMMIT'])
        finally:
            if previous is None:
                os.environ.pop('DELSK_COMMIT', None)
            else:
                os.environ['DELSK_COMMIT'] = previous

    def test_missing_git_does_not_abort_the_laboratory(self):
        with patch('research_lab_launch.subprocess.check_output', side_effect=subprocess.CalledProcessError(1, 'git')):
            lab = Lab(self.root, lambda *args: (0, {}))
        self.assertIsNone(lab.commit)
        self.assertFalse(lab.commit_verified)
        with patch('research_lab_launch.shutil.which', return_value=None):
            again = Lab(self.root, lambda *args: (0, {}))
        self.assertIsNone(again.commit)

    def test_fresh_runner_skips_restore_and_build(self):
        with patch('research_lab_launch.fresh', return_value=True), patch('research_lab_launch.run_checked') as run:
            prepare(self.root)
        run.assert_not_called()
        self.assertEqual('skip', json.loads((self.root/'startup'/'environment.json').read_text(encoding='utf-8'))['action'])

    def test_cp1252_console_still_reads_utf8_json_and_prints_russian(self):
        env = os.environ.copy()
        env['PYTHONUTF8'] = '0'
        env['PYTHONIOENCODING'] = 'cp1252:strict'
        names = [
            'test_launch.StartupTests.test_preparation_uses_locked_restore_and_stops_on_failure',
            'test_launch.HttpTests.test_reopen_verifies_live_instance',
            'test_launch.HttpTests.test_failed_runner_logs_remain_downloadable_for_diagnosis',
            'test_full_metrics.FullMetricsTests.test_b1_import_is_paid_at_terminal_and_uncollected_stock_is_separate',
            'test_postg2_h1.PostG2StudyTests.test_checkpoint_path_escape_is_rejected',
            'test_r1.LaunchTests.test_fresh_runner_skips_restore_and_build',
        ]
        completed = subprocess.run([sys.executable, '-m', 'unittest', *names], cwd=Path(__file__).resolve().parent,
                                   env=env, capture_output=True, timeout=90)
        if completed.returncode:
            detail = (completed.stdout + completed.stderr).decode('utf-8', 'replace')
            self.fail(detail[-4000:])

    def test_browser_failure_prints_the_manual_address(self):
        with patch('research_lab_launch.webbrowser.open', side_effect=OSError('нет браузера')), patch('sys.stdout', new_callable=StringIO) as out:
            open_browser('http://127.0.0.1:9/')
        self.assertIn('http://127.0.0.1:9/', out.getvalue())

    def test_run_from_another_directory_uses_the_repository_root(self):
        other = self.root/'Папка с пробелом'
        other.mkdir()
        previous = os.getcwd()
        try:
            os.chdir(other)
            with patch('research_lab_launch.require_runner'):
                self.assertEqual(0, main(['--run', '--output', str(self.root/'lab')]))
        finally:
            os.chdir(previous)


class ReportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)

    def write(self, name, value):
        (self.folder/name).write_text(json.dumps(value), encoding='utf-8')

    def test_treasury_column_is_not_read_from_the_old_energy_header(self):
        header = 'tick,participant_cash_minor,wages_minor,energy_minor,harvested,produced,exported,state_hash,population,unemployed\n'
        (self.folder/'metrics.csv').write_text(header+'1,2,3,999,0,0,0,abc,20,10\n', encoding='utf-8')
        data = report(self.folder)
        self.assertEqual('energy_minor', data['metrics']['kind'])
        self.assertEqual([], data['metrics']['rows'])
        self.assertIn('не казна', data['metrics']['note'])

    def test_confirmed_treasury_matches_the_account_and_skips_a_broken_sample(self):
        header = 'tick,participant_cash_minor,wages_minor,treasury_minor,harvested,produced,exported,state_hash,population,unemployed\n'
        (self.folder/'metrics.csv').write_text(header+'120,20000,500,15000,4,7,0,abc,24,6\n', encoding='utf-8')
        self.write('metrics-format.json', dict(version=1, treasuryColumn='treasury_minor'))
        self.write('summary.json', dict(status='succeeded', tick=120, elapsedMilliseconds=1000))
        self.write('final-state.json', dict(
            accounts=[dict(id=dict(value=9), kind='Treasury', balanceMinor=15000), dict(id=dict(value=1), kind='Personal', balanceMinor=20000), dict(id=dict(value=8), kind='WagesSink', balanceMinor=500)],
            participants=[dict(id=dict(value=1), accountId=dict(value=1), busy=False)],
            buildings=[dict(ownerId=dict(value=2), kind='Production')],
            enterprises=[], flows=[dict(harvested=4, produced=7, exported=0)], deliveryContracts=[dict(status='Fulfilled'), dict(status='Expired')],
            capitalDemand=dict(periods=[dict(monetaryBudgetMinor=3000)])))
        self.write('public-economy.json', dict(municipalMinor=16000, population=24, employed=18, unemployed=6, activeProductions=1,
                                               participants=[dict(actor=dict(value=1), capitalMinor=40000)]))
        self.write('manifest.json', dict(scenarioId='compact_valley_i6c21', seed=42, economyProfileId='vnext-i6c72-wages-1', commit='not-a-sha'))
        (self.folder/'economy-samples.jsonl').write_text('{"tick":0}\n{broken\n{"tick":120,"treasuryBalanceMinor":15000}\n', encoding='utf-8')
        (self.folder/'events.jsonl').write_text('{"type":"BuildingConstructed","tick":10,"actorId":{"value":2},"reasonCode":"sawmill"}\n', encoding='utf-8')
        data = report(self.folder)
        self.assertTrue(data['complete'])
        self.assertTrue(data['metrics']['confirmed'])
        self.assertEqual(15000, data['metrics']['rows'][-1]['treasuryMinor'])
        self.assertEqual(15000, data['treasuryBalanceMinor'])
        self.assertEqual(16000, data['municipalMinor'])
        self.assertEqual(3000, data['orderBudgetMinor'])
        self.assertEqual(1, data['buildings']['constructed'])
        self.assertEqual(1, data['buildings']['existing'])
        self.assertEqual({'Fulfilled': 1, 'Expired': 1}, data['contracts'])
        self.assertIsNone(data['identity']['commit'])
        self.assertIn('не подтверждён', data['identity']['commitNote'])
        self.assertEqual(2, len(data['samples']))

    def test_large_decision_page_does_not_send_the_observation(self):
        lab = Lab(self.folder, lambda *args: (0, {}))
        lab.series.append(dict(id='demo', jobs=[dict(id='job', attempt=1, status='succeeded')]))
        folder = self.folder/'demo'/'runs'/'job'/'1'
        folder.mkdir(parents=True)
        row = dict(tick=3, actor=dict(value=2), observation=dict(huge='x'*9000),
                   decision=dict(goal='EarnConstructionReserve', reason='Medium', actions=[dict(kind='MoveTo')]))
        (folder/'decisions.jsonl').write_text(json.dumps(row)+'\n', encoding='utf-8')
        page = lab.views.journal('demo', 'job', '1', 'decisions')
        self.assertEqual('EarnConstructionReserve', page['rows'][0]['decision']['goal'])
        self.assertNotIn('observation', page['rows'][0])
        self.assertIn('MoveTo', page['rows'][0]['decision']['actions'])
