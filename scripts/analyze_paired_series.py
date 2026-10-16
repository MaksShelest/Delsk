#!/usr/bin/env python3
"""Парный отчёт P25 по сохранённым результатам серии. Модель повторно не вызывается.

Author: Шелестов Максим Евгеньевич.
"""
import argparse
import csv
import json
import math
import statistics
from pathlib import Path


def quantile(values, fraction):
    ordered = sorted(values)
    if not ordered:
        raise ValueError('empty sample')
    if len(ordered) == 1:
        return float(ordered[0])
    position = fraction * (len(ordered) - 1)
    low = math.floor(position)
    high = min(low + 1, len(ordered) - 1)
    weight = position - low
    return ordered[low] * (1 - weight) + ordered[high] * weight


def spread(values):
    if not values:
        return None
    return {'n': len(values), 'median': quantile(values, 0.5), 'q1': quantile(values, 0.25),
            'q3': quantile(values, 0.75), 'iqr': quantile(values, 0.75) - quantile(values, 0.25),
            'minimum': min(values), 'maximum': max(values),
            'mean': statistics.fmean(values)}


def sample_size_for_mean_ci(values):
    """Планировочная оценка: сколько пар нужно, чтобы 95% интервал среднего
    исключил 0, если пилотные среднее и SD сохранятся. Это не вывод о победителе."""
    if len(values) < 2:
        return {'pairs': None, 'reason': 'fewer than 2 complete pairs'}
    mean = statistics.fmean(values)
    deviation = statistics.stdev(values)
    if mean == 0:
        return {'pairs': None, 'standardDeviation': deviation, 'reason': 'pilot mean difference is 0'}
    if deviation == 0:
        return {'pairs': 1, 'standardDeviation': 0, 'reason': 'all observed paired differences are equal and nonzero'}
    estimate = math.ceil((1.96 * deviation / abs(mean)) ** 2)
    return {'pairs': estimate, 'mean': mean, 'standardDeviation': deviation,
            'reason': 'planning estimate if the pilot mean and SD persist; not a test result'}


def self_test():
    if quantile([1, 2, 3, 4], 0.5) != 2.5:
        raise SystemExit('median self-test failed')
    if quantile([1, 2, 3, 4], 0.25) != 1.75 or quantile([1, 2, 3, 4], 0.75) != 3.25:
        raise SystemExit('quartile self-test failed')
    sized = sample_size_for_mean_ci([1, 1, 1, 1])
    if sized['pairs'] != 1:
        raise SystemExit('sample-size self-test failed')
    if sample_size_for_mean_ci([1, -1])['reason'] != 'pilot mean difference is 0':
        raise SystemExit('zero-mean self-test failed')


def load(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))


def jsonl(path):
    file = Path(path)
    if not file.exists():
        return []
    return [json.loads(line) for line in file.read_text(encoding='utf-8').splitlines() if line.strip()]


def output_value(run_dir):
    summary = load(run_dir / 'summary.json')
    scenario = load(run_dir / 'scenario.json')
    prices = {item['id']: item['basePriceMinor'] for item in scenario['items']}
    total = 0
    for flow in summary.get('flows') or []:
        total += (flow.get('harvested') or 0) * prices[flow['itemId']]
        total += (flow.get('produced') or 0) * prices[flow['itemId']]
    return total


def support_counts(run_dir):
    support = load(run_dir / 'support.json')
    proposals = support.get('proposals') or []
    return {'supportProposed': len(proposals),
            'supportSigned': sum(1 for item in proposals if item.get('status') == 'Signed'),
            'supportCommitments': len(support.get('commitments') or [])}


def contract_counts(run_dir):
    delivery = load(run_dir / 'delivery.json')
    contracts = delivery.get('contracts') or []
    return {'contracts': len(contracts),
            'deliveredQuantity': sum(item.get('deliveredQuantity') or 0 for item in contracts),
            'fineMinor': sum(item.get('fineMinor') or 0 for item in contracts)}


def election_winner(run_dir):
    elections = load(run_dir / 'summary.json').get('elections') or {}
    results = elections.get('results') or []
    victory = elections.get('firstVictory')
    winner = None
    if victory and victory.get('winner') is not None:
        winner = victory['winner'].get('value', victory['winner'])
    elif results and results[-1].get('winner') is not None:
        winner = results[-1]['winner'].get('value', results[-1]['winner'])
    return {'electionWinner': winner, 'electionsHeld': len(results)}


def labor_idle(run_dir):
    path = run_dir / 'labor.csv'
    if not path.exists():
        return None
    idle = 0
    with path.open(encoding='utf-8', newline='') as handle:
        for row in csv.DictReader(handle):
            if int(row['headcount']) > 0 and row['reason'] != 'Ready':
                idle += 1
    return idle


def model_counts(run_dir):
    traces = [row['trace'] for row in jsonl(run_dir / 'model-decisions.jsonl')]
    latencies = [trace['latencyMilliseconds'] for trace in traces if trace.get('latencyMilliseconds') is not None]
    return {'requested': sum(1 for trace in traces if trace.get('resolution') == 'Requested'),
            'modelResolved': sum(1 for trace in traces if trace.get('resolution') == 'ModelResolved'),
            'fallbackResolved': sum(1 for trace in traces if trace.get('resolution') == 'FallbackResolved'),
            'staleResolved': sum(1 for trace in traces if trace.get('reasonCode') == 'StaleRelevantState'),
            'promptTokens': sum(trace.get('promptTokens') or 0 for trace in traces),
            'completionTokens': sum(trace.get('completionTokens') or 0 for trace in traces),
            'latencies': latencies}


def measure_run(run_dir):
    summary = load(run_dir / 'summary.json')
    measured = {'status': summary.get('status'), 'stateHash': summary.get('stateHash'),
                'tick': summary.get('tick'), 'unemployed': summary.get('unemployed'),
                'population': summary.get('population'),
                'controllerRejections': summary.get('controllerRejections'),
                'outputBaseValueMinor': output_value(run_dir),
                'laborIdleRows': labor_idle(run_dir)}
    measured.update(election_winner(run_dir))
    measured.update(support_counts(run_dir))
    measured.update(contract_counts(run_dir))
    measured.update(model_counts(run_dir))
    return measured


def pooled_latency(series_dir, live):
    pooled = {}
    for arm in [item['id'] for item in live['arms'] if item.get('model') == 'ollama']:
        values = []
        for seed in live['seeds']:
            values.extend(model_counts(Path(series_dir) / 'runs' / 'live-modes' / str(seed) / arm)['latencies'])
        pooled[arm] = None if not values else {'samples': len(values), 'p50Ms': quantile(values, 0.5),
                                                'p95Ms': quantile(values, 0.95), 'minimumMs': min(values),
                                                'maximumMs': max(values)}
    return pooled


def chart(path, title, rows):
    width, height, left, top = 760, 320, 48, 36
    values = [row['difference'] for row in rows]
    span = max(1, max(abs(value) for value in values)) if values else 1
    plot_width = width - left - 24
    plot_height = height - top - 36
    mid = top + plot_height / 2
    lines = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">',
             '<rect width="100%" height="100%" fill="#f7f4ee"/>',
             f'<text x="{left}" y="22" font-family="Segoe UI, sans-serif" font-size="16">{title}</text>',
             f'<line x1="{left}" y1="{mid}" x2="{width - 16}" y2="{mid}" stroke="#888"/>']
    if rows:
        bar = plot_width / len(rows)
        for index, row in enumerate(rows):
            bar_height = abs(row['difference']) / span * (plot_height / 2 - 8)
            x = left + index * bar + 6
            y = mid - bar_height if row['difference'] >= 0 else mid
            lines.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="{max(4, bar - 12):.1f}" height="{bar_height:.1f}" fill="#3d5a40"/>')
            lines.append(f'<text x="{x:.1f}" y="{height - 12}" font-family="Segoe UI, sans-serif" font-size="11">{row["seed"]}</text>')
    lines.append('</svg>')
    Path(path).write_text('\n'.join(lines) + '\n', encoding='utf-8')


def number(value):
    if value is None:
        return ''
    if isinstance(value, float):
        return f'{value:.4g}'
    return str(value)


def write_csv(path, rows):
    path.parent.mkdir(parents=True, exist_ok=True)
    if not rows:
        path.write_text('', encoding='utf-8')
        return
    with path.open('w', encoding='utf-8', newline='') as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)


def analyze(series_dir, evidence_dir):
    self_test()
    series_dir = Path(series_dir)
    evidence = Path(evidence_dir)
    evidence.mkdir(parents=True, exist_ok=True)
    series = load(series_dir / 'series-manifest.json')
    protocol = load(series['protocolFile'])
    if protocol.get('primaryMetric') != 'outputBaseValueMinor':
        raise SystemExit('Primary metric was not fixed as output base value before analysis')
    per_seed = []
    problems = []
    for cell in series['cells']:
        run_dir = series_dir / 'runs' / cell['comparison'] / str(cell['seed']) / cell['arm']
        row = {'comparison': cell['comparison'], 'arm': cell['arm'], 'seed': cell['seed'],
               'timingGroup': cell.get('timingGroup'), 'status': cell['status'],
               'failure': cell.get('failure') or ''}
        if cell['status'] != 'succeeded' or not (run_dir / 'summary.json').exists():
            problems.append(f"Kept failed cell {cell['comparison']}/{cell['seed']}/{cell['arm']}: {cell.get('failure') or cell['status']}")
            per_seed.append(row)
            continue
        measured = measure_run(run_dir)
        latencies = measured.pop('latencies')
        row.update(measured)
        row['latencyP50Ms'] = None if not latencies else quantile(latencies, 0.5)
        row['latencyP95Ms'] = None if not latencies else quantile(latencies, 0.95)
        per_seed.append(row)
    pairs = []
    pair_summaries = []
    live = next(item for item in protocol['comparisons'] if item['id'] == 'live-modes')
    metric_names = ['outputBaseValueMinor', 'unemployed', 'supportSigned', 'supportProposed',
                    'deliveredQuantity', 'fineMinor', 'laborIdleRows', 'controllerRejections',
                    'fallbackResolved', 'staleResolved']
    for pair in live['pairs']:
        differences = {name: [] for name in metric_names}
        complete = []
        for seed in live['seeds']:
            left = next((row for row in per_seed if row['comparison'] == 'live-modes' and row['seed'] == seed and row['arm'] == pair['left']), None)
            right = next((row for row in per_seed if row['comparison'] == 'live-modes' and row['seed'] == seed and row['arm'] == pair['right']), None)
            record = {'pair': pair['id'], 'factor': pair['factor'], 'seed': seed,
                      'left': pair['left'], 'right': pair['right'],
                      'leftStatus': None if left is None else left['status'],
                      'rightStatus': None if right is None else right['status']}
            if left is None or right is None or left['status'] != 'succeeded' or right['status'] != 'succeeded':
                record['status'] = 'incomplete'
                problems.append(f"Incomplete pair {pair['id']} seed {seed} stays in the table")
            else:
                record['status'] = 'complete'
                for name in metric_names:
                    record[name] = right[name] - left[name]
                    differences[name].append(record[name])
                record['leftWinner'] = left.get('electionWinner')
                record['rightWinner'] = right.get('electionWinner')
                complete.append(record)
            pairs.append(record)
        summary = {'pair': pair['id'], 'factor': pair['factor'], 'attempted': len(live['seeds']),
                   'complete': len(complete), 'metrics': {}}
        for name in metric_names:
            summary['metrics'][name] = {'spread': spread(differences[name]),
                                        'sampleSize': sample_size_for_mean_ci(differences[name]) if name == 'outputBaseValueMinor' else None}
        pair_summaries.append(summary)
        chart(evidence / f"paired-{pair['id']}.svg",
              f"{pair['id']}: output base value, right minus left",
              [{'seed': row['seed'], 'difference': row['outputBaseValueMinor']} for row in complete])
    winners = []
    for arm in ['baseline', 'hybrid', 'hybrid-communication']:
        rows = [row for row in per_seed if row['comparison'] == 'live-modes' and row['arm'] == arm and row['status'] == 'succeeded']
        counts = {}
        for row in rows:
            key = 'none' if row.get('electionWinner') in (None, '') else str(row['electionWinner'])
            counts[key] = counts.get(key, 0) + 1
        latencies = []
        for row in rows:
            if row.get('latencyP50Ms') is not None:
                latencies.append(row['latencyP50Ms'])
        winners.append({'arm': arm, 'games': len(rows), 'winners': counts,
                        'latencyP50OfRunMedians': None if not latencies else quantile(latencies, 0.5)})
    equality = []
    for comparison in protocol['comparisons']:
        expected = comparison.get('expectEqualWorld') or []
        if expected and isinstance(expected[0], str):
            expected = [expected]
        for left_id, right_id in expected:
            for seed in comparison['seeds']:
                left = next(row for row in per_seed if row['comparison'] == comparison['id'] and row['seed'] == seed and row['arm'] == left_id)
                right = next(row for row in per_seed if row['comparison'] == comparison['id'] and row['seed'] == seed and row['arm'] == right_id)
                same = left.get('stateHash') == right.get('stateHash') and left['status'] == 'succeeded' and right['status'] == 'succeeded'
                equality.append({'comparison': comparison['id'], 'seed': seed, 'equal': same,
                                 'leftHash': left.get('stateHash'), 'rightHash': right.get('stateHash')})
                if not same:
                    problems.append(f"Disabled model diverged at {comparison['id']} seed {seed}")
    flat_seed = []
    for row in per_seed:
        item = {key: value for key, value in row.items() if key != 'latencies'}
        flat_seed.append(item)
    result = {'stage': 'P25', 'protocol': protocol['id'], 'primaryMetric': protocol['primaryMetric'],
              'commit': series.get('commit'), 'model': {key: series.get('model', {}).get(key) for key in
              ('name', 'digest', 'quantization', 'family', 'parameterSize', 'license')},
              'attemptedLiveSeeds': live['seeds'], 'pairs': pair_summaries, 'winners': winners,
              'pooledLatency': pooled_latency(series_dir, live),
              'disabledModelEquality': equality, 'problems': problems,
              'limitations': ['Ten paired seeds are a pilot, not a settled ranking.',
                              'Victory counts are one metric and do not establish a general winner.',
                              'Latency is reported separately from economic differences.',
                              'The algorithmic baseline is native and is not budget-matched to the model.',
                              'Horizon is 120 ticks of the politics fixture.',
                              'A new model run with the same seed can differ; this report describes the saved runs.']}
    (evidence / 'summary.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    write_csv(evidence / 'per-seed.csv', flat_seed)
    write_csv(evidence / 'paired.csv', pairs)
    print(f'P25 analysis wrote {evidence}')
    return 1 if any(not row['equal'] for row in equality) else 0


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--series', default='artifacts/p25-series')
    parser.add_argument('--evidence', default='docs/verification/p25')
    parser.add_argument('--self-test-only', action='store_true')
    args = parser.parse_args()
    if args.self_test_only:
        self_test()
        print('P25 statistics self-test passed')
        return 0
    return analyze(args.series, args.evidence)


if __name__ == '__main__':
    raise SystemExit(main())
