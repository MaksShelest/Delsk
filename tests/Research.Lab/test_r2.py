"""Проверяемая статистика и отчёт R2. Симулятор здесь не подменяется: числа заданы заранее.
Author: Шелестов Максим Евгеньевич.
"""
from decimal import Decimal
import json
from pathlib import Path
import tempfile
import unittest
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'scripts'))
from research_experiment import METRICS, analyze_snapshot, build_plan, open_folder
from research_lab import EXTRA_FACTORS, FACTORS, Lab, ROOT, expand
from research_report import summary_csv
from research_statistics import describe, mean_interval, paired, student_t_critical, student_t_two_sided_p


class StatisticsTests(unittest.TestCase):
    def test_mean_median_and_sample_deviation(self):
        summary = describe([1, 2, 3, 4, 5, None])
        self.assertEqual(5, summary['n'])
        self.assertEqual(1, summary['missing'])
        self.assertEqual(Decimal(3), summary['mean'])
        self.assertEqual(Decimal(3), summary['median'])
        self.assertEqual(Decimal(4), summary['range'])
        self.assertAlmostEqual(2.5, float(summary['standardDeviation'] ** 2), places=9)

    def test_even_median_and_negative_money(self):
        summary = describe([-400, -100])
        self.assertEqual(Decimal(-250), summary['mean'])
        self.assertEqual(Decimal(-250), summary['median'])
        self.assertLess(summary['minimum'], 0)

    def test_large_integers_keep_exact_mean(self):
        summary = describe([10**18, 10**18, 10**18 + 3])
        self.assertEqual(Decimal(10**18) + 1, summary['mean'])
        self.assertEqual(Decimal(10**18), summary['median'])

    def test_single_observation_has_no_deviation(self):
        summary = describe([7])
        self.assertIsNone(summary['standardDeviation'])
        self.assertIsNone(mean_interval(summary))

    def test_interval_uses_the_paired_student_quantile(self):
        self.assertAlmostEqual(0.5, student_t_two_sided_p(1, 1), places=6)
        known = {1: 12.706204736432095, 5: 2.5705818366147395, 9: 2.2621571627409915, 30: 2.042272456301237}
        for degrees, critical in known.items():
            self.assertAlmostEqual(critical, student_t_critical(degrees), places=6)
        result = paired({1: 10, 2: 20, 3: 30}, {1: 12, 2: 24, 3: 36})
        # Разности 2, 4, 6. Среднее 4, выборочное отклонение 2, df = 2.
        self.assertEqual([1, 2, 3], result['seeds'])
        self.assertEqual(Decimal(4), result['mean'])
        self.assertEqual(Decimal(2), result['standardDeviation'])
        margin = Decimal(str(student_t_critical(2))) * 2 / Decimal(3).sqrt()
        self.assertEqual([Decimal(4) - margin, Decimal(4) + margin], result['confidenceInterval'])

    def test_pairs_match_seeds_not_list_position(self):
        result = paired({2: 10, 1: 100}, {1: 130, 2: 10})
        self.assertEqual([1, 2], result['seeds'])
        self.assertEqual([Decimal(30), Decimal(0)], result['differences'])

    def test_relative_change_is_not_the_mean_of_ratios(self):
        # 10→20 это +100%, 30→30 это 0%. Среднее процентов было бы 50, а разность средних — 25%.
        result = paired({1: 10, 2: 30}, {1: 20, 2: 30})
        self.assertEqual(Decimal(25), result['relativeChangePercent'])

    def test_zero_base_and_zero_difference_do_not_invent_a_probability(self):
        zero_base = paired({1: 0, 2: 0}, {1: 5, 2: 5})
        self.assertIsNone(zero_base['relativeChangePercent'])
        self.assertIsNone(zero_base['pValue'])
        identical = paired({1: 5, 2: 5}, {1: 5, 2: 5})
        self.assertIsNone(identical['pValue'])
        self.assertIn('нулю', ' '.join(identical['warnings']))

    def test_missing_pair_is_not_zero(self):
        result = paired({1: 10, 2: None, 3: 4}, {1: None, 2: 7, 3: 9})
        self.assertEqual([3], result['seeds'])
        self.assertEqual(Decimal(5), result['mean'])
        self.assertIsNone(result['pValue'])

    def test_non_finite_values_are_rejected(self):
        with self.assertRaises(ValueError):
            describe([float('nan')])
        with self.assertRaises(ValueError):
            paired({1: float('inf')}, {1: 1})


class ExperimentTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def test_wage_template_changes_npc_target_not_the_world_schedule(self):
        self.assertNotIn('npc.baseWageMinor', FACTORS)
        self.assertIn('npc.baseWageMinor', EXTRA_FACTORS)
        built = build_plan(dict(template='wages', seedCount=2, ticks=120, groups=[
            dict(label='A', value=15), dict(label='B', value=25)]))
        plan = built['plan']
        self.assertEqual(4, built['estimate']['runs'])
        _, _, arms = expand(plan, self.root)
        self.assertEqual([15, 25], [arm['economy']['npc']['baseWageMinor'] for arm in arms])
        self.assertEqual(arms[0]['economy']['world']['rules']['fleet']['wageMinor'], arms[1]['economy']['world']['rules']['fleet']['wageMinor'])
        self.assertEqual(15, arms[0]['economy']['world']['rules']['fleet']['wageMinor'])
        self.assertEqual(2, arms[0]['economy']['world']['rules']['playerWageStepMinor'])
        self.assertEqual('vnext-i6c72-wages-1', arms[0]['economy']['id'])

    def test_other_templates_use_real_switches(self):
        count = build_plan(dict(template='npc-count', seedCount=1, ticks=50))['plan']
        _, _, arms = expand(count, self.root)
        self.assertEqual([3, 6], [arm['ordinary']['npcCount'] for arm in arms])
        talks = build_plan(dict(template='negotiations', seedCount=1, ticks=50))['plan']
        self.assertNotIn('ordinaryNpcCount', talks)
        self.assertEqual('scenarios/presentation-map-one-v3.json', talks['scenario'])
        self.assertIn('Малая долина', talks['science']['fixed'][0])
        with self.assertRaises(ValueError):
            build_plan(dict(template='wages', groups=[dict(label='A', value=15), dict(label='B', value=15)]))

    def test_report_round_trip_excludes_failures_and_replicas(self):
        series = self.series()
        first = analyze_snapshot(self.root, series)
        report = (self.root / series['id'] / 'analysis' / 'report.html').read_text(encoding='utf-8')
        markdown = (self.root / series['id'] / 'analysis' / 'report.md').read_text(encoding='utf-8')
        self.assertIn('Проверка отчёта', report)
        self.assertIn('Занятость вырастет', report)
        self.assertNotIn('научно доказан', report.lower())
        self.assertIn('не доказывают', markdown)
        self.assertIn('Delsk', report)
        table = summary_csv(first)
        self.assertTrue(table.startswith('\ufeff'))
        self.assertIn(';', table)
        self.assertIn(',', table)
        employed = first['metrics']['employed']
        self.assertEqual(2, employed['groups'][0]['n'])
        self.assertEqual(1, employed['groups'][1]['n'])
        self.assertEqual([42], employed['paired'][0]['seeds'])
        self.assertEqual(1, employed['paired'][0]['pairs'])
        self.assertIn('5000,00', table)
        failed = next(run for run in first['runs'] if run['status'] == 'failed')
        self.assertFalse(failed['included'])
        self.assertIsNone(failed['metrics']['employed'])
        replica = next(run for run in first['runs'] if run['repeat'] == 1)
        self.assertFalse(replica['included'])
        stamp = (self.root / series['id'] / 'analysis' / 'report.html').stat().st_mtime_ns
        again = analyze_snapshot(self.root, series)
        self.assertEqual(first['fingerprint'], again['fingerprint'])
        self.assertEqual(stamp, (self.root / series['id'] / 'analysis' / 'report.html').stat().st_mtime_ns)
        names = self.zip_names(series['id'])
        self.assertNotIn('runs/arm-0-s42-r0/1/decisions.jsonl', names)
        self.assertIn('analysis/omitted.txt', names)
        self.assertIn('analysis/report.html', names)
        self.assertTrue((self.root / series['id']).resolve().is_relative_to(self.root.resolve()))
        with self.assertRaises(ValueError):
            analyze_snapshot(self.root, series | dict(id='../outside'))

    def series(self):
        key = 'r2-fixture'
        folder = self.root / key
        def succeed(arm, seed, repeat, employed, treasury):
            run = folder / 'runs' / f'{arm}-s{seed}-r{repeat}' / '1'
            run.mkdir(parents=True)
            (run / 'summary.json').write_text(json.dumps(dict(
                status='succeeded', tick=120, elapsedMilliseconds=1000, controllerDecisionTurns=4, controllerRejections=1,
                accounts=[dict(kind='Treasury', balanceMinor=treasury), dict(kind='WagesSink', balanceMinor=0), dict(kind='Participant', balanceMinor=20000)],
                flows=[dict(produced=0, harvested=1)], deliveryContracts=[], marketAllocations=[], population=20, unemployed=20 - employed), ensure_ascii=False), encoding='utf-8')
            (run / 'public-economy.json').write_text(json.dumps(dict(
                capitalMinor=treasury + 100, municipalMinor=treasury, population=20, employed=employed, unemployed=20 - employed, activeProductions=0,
                grossOutputMinor=0, participants=[dict(actor=dict(value=2), capitalMinor=100)])), encoding='utf-8')
            (run / 'decisions.jsonl').write_bytes(b'{"secret":1}\n' * 20000)
            return f'{key}/runs/{arm}-s{seed}-r{repeat}/1'
        jobs = [
            dict(id='arm-0-s42-r0', arm='arm-0', seed=42, repeat=0, status='succeeded', attempt=1, result=succeed('arm-0', 42, 0, 5, 500000)),
            dict(id='arm-0-s42-r1', arm='arm-0', seed=42, repeat=1, status='succeeded', attempt=1, result=succeed('arm-0', 42, 1, 9, 1)),
            dict(id='arm-0-s43-r0', arm='arm-0', seed=43, repeat=0, status='succeeded', attempt=1, result=succeed('arm-0', 43, 0, 7, 500000)),
            dict(id='arm-1-s42-r0', arm='arm-1', seed=42, repeat=0, status='succeeded', attempt=1, result=succeed('arm-1', 42, 0, 8, 400000)),
            dict(id='arm-1-s43-r0', arm='arm-1', seed=43, repeat=0, status='failed', attempt=1, error='Runner exit 1'),
        ]
        plan = build_plan(dict(template='wages', title='Проверка отчёта', hypothesis='Занятость вырастет у второй ставки.', seedCount=2, ticks=120))['plan']
        return dict(id=key, plan=plan, jobs=jobs, commit=None, commitVerified=False, binaries={})

    def zip_names(self, key):
        import zipfile
        with zipfile.ZipFile(self.root / key / 'analysis' / 'results.zip') as archive:
            return set(archive.namelist())

    def test_folder_open_stays_inside_the_workspace(self):
        class Fake:
            workspace = self.root
        (self.root / 'known').mkdir()
        opened = open_folder(Fake(), 'known')
        self.assertEqual(str((self.root / 'known').resolve()), opened['path'])
        with self.assertRaises(ValueError):
            open_folder(Fake(), '../known')


class QueueTests(unittest.TestCase):
    def test_timeout_is_a_distinct_status_and_can_be_retried(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        calls = []

        def executor(name, timeout, logs, command):
            calls.append(name)
            if str(name).startswith('validate'):
                return 0, {}
            return 124, {}

        lab = Lab(root, executor)
        from research_lab import DEFAULT
        import copy
        lab.enqueue(copy.deepcopy(DEFAULT) | dict(seeds=[42], repetitions=1, recordWorld=False, factors=[]))
        lab.start()
        lab.thread.join(5)
        job = lab.series[0]['jobs'][0]
        self.assertEqual('timeout', job['status'])
        self.assertNotIn('metrics', job)
        before = len(calls)
        lab.retry_jobs(lab.series[0]['id'], job['id'])
        lab.thread.join(5)
        self.assertEqual('timeout', lab.series[0]['jobs'][0]['status'])
        self.assertGreater(len(calls), before)


if __name__ == '__main__':
    unittest.main()
