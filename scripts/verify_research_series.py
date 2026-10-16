#!/usr/bin/env python3
"""Проверка серии P24: один фактор, timing-policy, baseline без модели, replay и сырые наблюдения.

Author: Шелестов Максим Евгеньевич.
"""
import argparse
import csv
import json
from pathlib import Path
import sys

POLITICAL_GOALS = {'BuySupport', 'AcceptSupport', 'RejectSupport', 'SignSupport', 'DeliverCompensation', 'SetVoteChoice'}
PACED = {'paced', 'Realtime'}
WORLD_FILES = ['final-state.json', 'ledger.jsonl', 'events.jsonl', 'commands.jsonl', 'metrics.csv', 'labor.csv',
               'market.json', 'scenario.json', 'delivery.json', 'logistics.json', 'capital-demand.json',
               'elections.json', 'support.json', 'public-events.jsonl']
PRIVATE_MARKERS = ('requestJson', 'responseJson', 'reasonSummary', 'ModelResolved', 'promptHash')


def load(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))


def percentile(values, fraction):
    if not values:
        return None
    ordered = sorted(values)
    index = min(len(ordered) - 1, max(0, int(round(fraction * (len(ordered) - 1)))))
    return ordered[index]


def jsonl(path):
    file = Path(path)
    if not file.exists():
        return []
    return [json.loads(line) for line in file.read_text(encoding='utf-8').splitlines() if line.strip()]


def controller_body(path):
    data = load(path)
    bodies = []
    politics = []
    for npc in data['npcs']:
        item = dict(npc)
        politics.append(item.pop('politicsEnabled', True))
        bodies.append(item)
    fixed = {'version': data['version'], 'humanActorId': data['humanActorId'], 'finalTick': data['finalTick'], 'npcs': bodies}
    return fixed, politics


def same_pacer(left, right):
    return left == right or (left in PACED and right in PACED)


def option_goals(run_dir):
    goals = set()
    for row in jsonl(Path(run_dir) / 'model-decisions.jsonl'):
        request = json.loads(row['trace']['requestJson'])
        for option in request.get('options', []):
            goals.add(option.get('goal'))
    return goals


def metrics_for(cell, run_dir):
    traces = [row['trace'] for row in jsonl(Path(run_dir) / 'model-decisions.jsonl')]
    decisions = jsonl(Path(run_dir) / 'decisions.jsonl')
    events = Path(run_dir, 'events.jsonl').read_text(encoding='utf-8') if Path(run_dir, 'events.jsonl').exists() else ''
    accepted = set()
    for line in events.splitlines():
        if '"CommandAccepted"' in line:
            try:
                payload = json.loads(line)
            except json.JSONDecodeError:
                continue
            command = payload.get('commandId') or (payload.get('payload') or {}).get('commandId')
            if command:
                accepted.add(command)
    model_commands = []
    for row in decisions:
        trace = ((row.get('decision') or {}).get('modelTrace') or {})
        if trace.get('resolution') == 'ModelResolved':
            model_commands.extend(row.get('commandIds') or [])
    latencies = [trace['latencyMilliseconds'] for trace in traces if trace.get('latencyMilliseconds') is not None]
    return {
        'requested': sum(1 for trace in traces if trace.get('resolution') == 'Requested'),
        'modelResolved': sum(1 for trace in traces if trace.get('resolution') == 'ModelResolved'),
        'fallbackResolved': sum(1 for trace in traces if trace.get('resolution') == 'FallbackResolved'),
        'modelCommands': len(model_commands),
        'modelCommandsAccepted': sum(1 for command in model_commands if command in accepted),
        'latencyP50Ms': percentile(latencies, 0.50), 'latencyP95Ms': percentile(latencies, 0.95),
        'promptTokens': sum(trace.get('promptTokens') or 0 for trace in traces),
        'completionTokens': sum(trace.get('completionTokens') or 0 for trace in traces),
        'supportProposed': events.count('"SupportProposed"'),
        'supportSigned': events.count('"SupportSigned"'),
        'observations': sum(1 for row in decisions if 'observation' in row),
    }


def world_equal(left, right):
    for name in WORLD_FILES:
        if Path(left, name).read_bytes() != Path(right, name).read_bytes():
            return False, name
    return True, None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', default='artifacts/p24-series')
    parser.add_argument('--evidence', default='docs/verification/p24/gate.json')
    args = parser.parse_args()
    output = Path(args.output)
    series = load(output / 'series-manifest.json')
    protocol = load(series['protocolFile'])
    problems = []
    cells = series['cells']
    by_key = {(cell['comparison'], cell['seed'], cell['arm']): cell for cell in cells}

    if series.get('model', {}).get('digest') in (None, '', protocol['modelName']):
        problems.append('Measured model digest is missing or equal to the tag name')
    if series.get('model', {}).get('gameRamBytes') not in (None,):
        problems.append('Unmeasured game RAM must stay null')
    digest_in_runner = [cell for cell in cells if (cell.get('export') or {}).get('modelDigest')]
    if digest_in_runner:
        problems.append('Runner manifest stored a model digest; the series must measure it externally')

    comparisons = {item['id']: item for item in protocol['comparisons']}
    rows = []
    for cell in cells:
        comparison = comparisons[cell['comparison']]
        run_dir = output / 'runs' / cell['comparison'] / str(cell['seed']) / cell['arm']
        if cell['status'] != 'succeeded':
            logs = cell.get('check') or {}
            if not run_dir.exists() and not logs:
                problems.append(f"Failed cell {cell['comparison']}/{cell['seed']}/{cell['arm']} kept neither output nor logs")
            rows.append({'comparison': cell['comparison'], 'arm': cell['arm'], 'seed': cell['seed'], 'status': cell['status']})
            continue
        manifest = load(run_dir / 'manifest.json')
        if manifest.get('timingPolicy') == 'LiveHeadlessStress' or comparison['timing'] == 'headless' and manifest.get('timingPolicy') in PACED:
            problems.append(f"Timing policy mixed for {cell['comparison']}/{cell['arm']}")
        if comparison['timing'] == 'paced' and manifest.get('timingPolicy') not in PACED:
            problems.append(f"Paced cell {cell['arm']} exported timing {manifest.get('timingPolicy')}")
        if comparison['timing'] == 'headless' and manifest.get('timingPolicy') != 'headless':
            problems.append(f"Headless cell {cell['arm']} exported timing {manifest.get('timingPolicy')}")
        decisions = run_dir / 'decisions.jsonl'
        if not decisions.exists() or b'"observation"' not in decisions.read_bytes():
            problems.append(f'Raw observations missing in {decisions}')
        public = (run_dir / 'public-events.jsonl').read_text(encoding='utf-8')
        if any(marker in public for marker in PRIVATE_MARKERS):
            problems.append(f'Private model fields leaked into {run_dir / "public-events.jsonl"}')
        counts = metrics_for(cell, run_dir)
        cell['metrics'] = counts
        if cell['model'] == 'ollama' and counts['requested'] < 1:
            problems.append(f"Live model was not requested in {cell['comparison']}/{cell['arm']}")
        rows.append({'comparison': cell['comparison'], 'arm': cell['arm'], 'seed': cell['seed'],
                     'timingGroup': cell['timingGroup'], 'runnerTiming': manifest.get('timingPolicy'),
                     'stateHash': cell['export']['stateHash'], 'status': cell['status'], **counts})

    for comparison in protocol['comparisons']:
        for seed in comparison['seeds']:
            present = [by_key.get((comparison['id'], seed, arm['id'])) for arm in comparison['arms']]
            if any(cell is None or cell['status'] != 'succeeded' for cell in present):
                problems.append(f"Comparison {comparison['id']} seed {seed} is incomplete")
                continue
            hashes = {cell['export']['configHash'] for cell in present}
            scenarios = {cell['scenarioHash'] for cell in present}
            if len(hashes) != 1 or len(scenarios) != 1:
                problems.append(f"Seed/config diverged inside {comparison['id']} seed {seed}")
            timings = {load(output / 'runs' / comparison['id'] / str(seed) / cell['arm'] / 'manifest.json')['timingPolicy'] for cell in present}
            if not all(same_pacer(left, right) for left in timings for right in timings):
                problems.append(f"Timing policies mixed inside {comparison['id']} seed {seed}: {sorted(timings)}")
            if comparison['timing'] == 'headless' and timings != {'headless'}:
                problems.append(f"Headless comparison {comparison['id']} contains {sorted(timings)}")
        expected = comparison.get('expectEqualWorld', [])
        if expected and isinstance(expected[0], str):
            expected = [expected]
        for left_id, right_id in expected:
            for seed in comparison['seeds']:
                left = output / 'runs' / comparison['id'] / str(seed) / left_id
                right = output / 'runs' / comparison['id'] / str(seed) / right_id
                if not left.exists() or not right.exists():
                    continue
                equal, name = world_equal(left, right)
                if not equal:
                    problems.append(f'Disabled model diverged from baseline at {comparison["id"]} seed {seed}: {name}')
                if by_key[(comparison['id'], seed, left_id)]['controllersHash'] != by_key[(comparison['id'], seed, right_id)]['controllersHash']:
                    problems.append(f'Baseline equality changed controller config at {comparison["id"]} seed {seed}')
        for pair in comparison.get('pairs', []):
            for seed in comparison['seeds']:
                left = by_key.get((comparison['id'], seed, pair['left']))
                right = by_key.get((comparison['id'], seed, pair['right']))
                if left is None or right is None or left['status'] != 'succeeded' or right['status'] != 'succeeded':
                    problems.append(f"Pair {pair['id']} seed {seed} did not complete")
                    continue
                left_fixed, left_politics = controller_body(left['controllers'])
                right_fixed, right_politics = controller_body(right['controllers'])
                if left['timingGroup'] != right['timingGroup'] or left['timingGroup'] != comparison['timing']:
                    problems.append(f"Pair {pair['id']} mixes timing groups")
                if pair['factor'] == 'politicsEnabled':
                    if left_fixed != right_fixed or left_politics == right_politics:
                        problems.append(f"Communication pair {pair['id']} does not change only politicsEnabled")
                    if left['model'] != 'ollama' or right['model'] != 'ollama':
                        problems.append(f"Communication pair {pair['id']} is not hybrid on both sides")
                    goals = option_goals(output / 'runs' / comparison['id'] / str(seed) / pair['right'])
                    if not goals & POLITICAL_GOALS:
                        problems.append(f"Structured communication offered no political plan at seed {seed}")
                elif pair['factor'] == 'model':
                    if left_fixed != right_fixed or left_politics != right_politics:
                        problems.append(f"Model pair {pair['id']} changed controller settings")
                    if left['model'] == right['model']:
                        problems.append(f"Model pair {pair['id']} did not change the controller mode")
                for npc in left_fixed['npcs'] + right_fixed['npcs']:
                    if npc.get('harvestCoefficient') != 1 or npc.get('productionCoefficient') != 1:
                        problems.append(f"Unexpected economic coefficient in {pair['id']}")
        for arm in comparison.get('replay', []):
            for seed in comparison['seeds']:
                cell = by_key.get((comparison['id'], seed, arm))
                replay = None if cell is None else cell.get('replay')
                if replay is None or replay.get('status') != 'succeeded':
                    problems.append(f'Recorded replay missing for {comparison["id"]} seed {seed} {arm}')
                    continue
                if not str(replay.get('timingPolicy', '')).startswith('recorded-'):
                    problems.append(f"Replay timing was not recorded for {arm}")
                if same_pacer(replay.get('timingPolicy'), 'Realtime'):
                    problems.append(f'Replay was grouped with a live timing policy for {arm}')
                equal, name = world_equal(output / 'runs' / comparison['id'] / str(seed) / arm, replay['directory'])
                if not equal:
                    problems.append(f'Recorded replay diverged for {arm} seed {seed}: {name}')

    evidence = {'stage': 'P24', 'protocol': protocol['id'], 'commit': series.get('commit'),
                'model': {key: series.get('model', {}).get(key) for key in
                          ('status', 'name', 'digest', 'family', 'parameterSize', 'quantization', 'license',
                           'sizeBytes', 'vramBytes', 'vramSource', 'vramReason', 'gameRamBytes', 'gameRamReason', 'warmup')},
                'modelConfigHash': series.get('modelConfigHash'), 'cells': rows, 'problems': problems,
                'passed': not problems and series.get('status') == 'completed'}
    evidence_path = Path(args.evidence)
    evidence_path.parent.mkdir(parents=True, exist_ok=True)
    evidence_path.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    metric_path = output / 'metrics.csv'
    if rows and 'requested' in rows[0]:
        with metric_path.open('w', encoding='utf-8', newline='') as handle:
            writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
            writer.writeheader()
            writer.writerows(rows)
    print(f"P24 verification {'passed' if evidence['passed'] else 'failed'}: {evidence_path}")
    if problems:
        print('\n'.join(problems))
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
