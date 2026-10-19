#!/usr/bin/env python3
"""Запуск парной серии P24: baseline, hybrid и hybrid со структурированными переговорами.

Author: Шелестов Максим Евгеньевич.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import urllib.error
import urllib.request

POLITICAL_GOALS = {'BuySupport', 'AcceptSupport', 'RejectSupport', 'SignSupport', 'DeliverCompensation', 'SetVoteChoice'}


def sha256(path):
    digest = hashlib.sha256()
    digest.update(Path(path).read_bytes())
    return digest.hexdigest()


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def prepare_dotnet():
    home = Path.home() / '.dotnet'
    executable = home / ('dotnet.exe' if os.name == 'nt' else 'dotnet')
    if executable.exists():
        os.environ['DOTNET_ROOT'] = str(home)
        os.environ['PATH'] = str(home) + os.pathsep + os.environ.get('PATH', '')
    os.environ.setdefault('DOTNET_CLI_TELEMETRY_OPTOUT', '1')
    os.environ.setdefault('DOTNET_NOLOGO', '1')
    os.environ.setdefault('DOTNET_CLI_USE_MSBUILD_SERVER', '0')
    os.environ.setdefault('MSBUILDDISABLENODEREUSE', '1')


def commit():
    try:
        return subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True, timeout=30).strip()
    except (subprocess.SubprocessError, OSError):
        return None


def request_json(url, payload=None, timeout=60):
    data = None if payload is None else json.dumps(payload).encode('utf-8')
    request = urllib.request.Request(url, data=data, headers={'Content-Type': 'application/json'})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.load(response)


def measure_model(config, name):
    endpoint = config['ollama']['endpoint'].rstrip('/')
    tags = request_json(endpoint + '/api/tags', timeout=30)
    model = next((item for item in tags.get('models', []) if item.get('name') == name or item.get('model') == name), None)
    if model is None:
        raise RuntimeError(f'Model {name} is not installed in Ollama')
    show = request_json(endpoint + '/api/show', {'model': name}, timeout=60)
    details = show.get('details') or model.get('details') or {}
    raw_license = show.get('license') or ''
    license = 'Apache-2.0' if 'Apache License' in raw_license else (raw_license.strip()[:80] or None)
    info = show.get('model_info') or {}
    return {
        'endpoint': config['ollama']['endpoint'], 'name': name,
        'digest': model.get('digest') or show.get('digest'),
        'sizeBytes': model.get('size'), 'family': details.get('family'),
        'parameterSize': details.get('parameter_size'), 'quantization': details.get('quantization_level'),
        'format': details.get('format'), 'license': license,
        'contextLength': info.get('qwen35.context_length') or info.get('qwen3.context_length'),
        'digestSource': 'ollama /api/tags', 'gameRamBytes': None,
        'gameRamReason': 'not measured',
    }


def warmup(config, metadata):
    endpoint = config['ollama']['endpoint'].rstrip('/')
    body = {'model': metadata['name'], 'think': False, 'stream': False,
            'messages': [{'role': 'user', 'content': 'Reply with the word ok.'}],
            'options': {'num_predict': 8, 'temperature': 0}}
    import time
    started = time.monotonic()
    reply = request_json(endpoint + '/api/chat', body, timeout=120)
    metadata['warmup'] = {
        'elapsedSeconds': round(time.monotonic() - started, 3),
        'doneReason': reply.get('done_reason'), 'evalCount': reply.get('eval_count'),
        'contentEmpty': not ((reply.get('message') or {}).get('content') or '').strip(),
    }
    try:
        running = request_json(endpoint + '/api/ps', timeout=30)
        entry = next((item for item in running.get('models', []) if metadata['name'] in (item.get('name') or '')), None)
        metadata['vramBytes'] = None if entry is None else entry.get('size_vram')
        metadata['vramSource'] = None if entry is None else 'ollama /api/ps size_vram'
        if metadata['vramBytes'] is None:
            metadata['vramReason'] = 'ollama /api/ps did not report size_vram'
    except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as failure:
        metadata['vramBytes'] = None
        metadata['vramReason'] = f'not measured: {failure}'


def controllers_for(arm, source, directory):
    if 'politicsEnabled' not in arm:
        return source
    data = json.loads(Path(source).read_text(encoding='utf-8'))
    for npc in data['npcs']:
        npc['politicsEnabled'] = arm['politicsEnabled']
    path = directory / 'controllers' / f"{arm['id']}-politics-{'on' if arm['politicsEnabled'] else 'off'}.json"
    write_json(path, data)
    return str(path)


def run_checked(name, timeout, logs, command):
    completed = subprocess.run([sys.executable, 'scripts/run_check.py', '--name', name, '--timeout', str(timeout),
                                '--log-directory', str(logs), '--', *command], check=False)
    meta_path = Path(logs) / f'{name}.json'
    meta = json.loads(meta_path.read_text(encoding='utf-8')) if meta_path.exists() else {}
    return completed.returncode, meta


def runner_command(output, script, mode, ticks, seed, scenario, controllers, model, model_config):
    command = ['dotnet', 'run', '--project', 'src/Research.Runner', '-c', 'Release', '--no-build', '--',
               '--scenario', scenario, '--output', output, '--script', script, '--mode', mode,
               '--ticks', str(ticks), '--seed', str(seed), '--controllers', controllers]
    if script == 'hybrid':
        command += ['--model-mode', model]
        if model == 'ollama':
            command += ['--model-config', model_config]
    return command


def read_export(directory):
    folder = Path(directory)
    summary = folder / 'summary.json'
    manifest = folder / 'manifest.json'
    if not summary.exists() or not manifest.exists():
        return None
    metadata=json.loads(manifest.read_text(encoding='utf-8'))
    if metadata.get('developerBuild') or metadata.get('researchModified') or metadata.get('cleanComparisonEligible') is False:
        raise ValueError(f'{folder}: Developer/modified sessions are excluded from clean comparisons')
    return {'summary': json.loads(summary.read_text(encoding='utf-8')), 'manifest': metadata}


def cell_from_export(comparison, arm, seed, controllers, exported, replay_export, replay_dir):
    cell = {'comparison': comparison['id'], 'arm': arm['id'], 'seed': seed, 'script': arm['script'],
            'model': arm['model'], 'politicsEnabled': arm.get('politicsEnabled'),
            'timingGroup': comparison['timing'], 'ticks': comparison['ticks'],
            'scenario': comparison['scenario'], 'scenarioHash': sha256(comparison['scenario']),
            'controllers': str(controllers), 'controllersHash': sha256(controllers),
            'status': 'succeeded', 'exitCode': 0, 'check': {'resumed': True},
            'export': {'stateHash': exported['summary'].get('stateHash'),
                       'status': exported['summary'].get('status'),
                       'timingPolicy': exported['manifest'].get('timingPolicy'),
                       'configHash': exported['manifest'].get('configHash'),
                       'rulesVersion': exported['manifest'].get('rulesVersion'),
                       'schemaVersion': exported['manifest'].get('schemaVersion'),
                       'commit': exported['manifest'].get('commit'),
                       'promptVersion': (exported['manifest'].get('model') or {}).get('promptVersion'),
                       'modelDigest': (exported['manifest'].get('model') or {}).get('modelDigest')}}
    if replay_dir is not None:
        cell['replay'] = {'status': 'succeeded', 'exitCode': 0, 'check': {'resumed': True},
                          'directory': str(replay_dir),
                          'timingPolicy': None if replay_export is None else replay_export['manifest'].get('timingPolicy'),
                          'stateHash': None if replay_export is None else replay_export['summary'].get('stateHash')}
    return cell


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--protocol', default='scenarios/research-series.json')
    parser.add_argument('--output', default='artifacts/p24-series')
    parser.add_argument('--resume', action='store_true')
    args = parser.parse_args()
    prepare_dotnet()
    os.environ['DELSK_COMMIT'] = commit() or ''
    protocol = json.loads(Path(args.protocol).read_text(encoding='utf-8'))
    output = Path(args.output)
    logs = output / 'checks'
    model_config = json.loads(Path(protocol['modelConfig']).read_text(encoding='utf-8'))
    metadata = {'status': 'not-measured', 'name': protocol['modelName']}
    try:
        metadata = measure_model(model_config, protocol['modelName'])
        metadata['status'] = 'measured'
    except (urllib.error.URLError, TimeoutError, json.JSONDecodeError, RuntimeError, KeyError) as failure:
        metadata = {'status': 'unavailable', 'name': protocol['modelName'], 'error': str(failure),
                    'gameRamBytes': None, 'gameRamReason': 'not measured', 'vramBytes': None,
                    'vramReason': 'not measured because model metadata was unavailable'}
    write_json(output / 'model-metadata.json', metadata)

    build_code, build_meta = run_checked('p24-build', 180, logs, ['dotnet', 'build', 'src/Research.Runner', '-c', 'Release', '--nologo'])
    cells = []
    if build_code != 0:
        write_json(output / 'series-manifest.json', {'protocol': protocol['id'], 'commit': os.environ['DELSK_COMMIT'],
                                                      'build': build_meta, 'model': metadata, 'cells': [], 'status': 'build-failed'})
        print('P24 series build failed')
        return 1
    if metadata['status'] == 'measured':
        try:
            warmup(model_config, metadata)
            if metadata.get('warmup', {}).get('contentEmpty', True):
                metadata['status'] = 'warmup-empty'
                metadata['error'] = 'Warmup returned an empty reply'
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError, KeyError) as failure:
            metadata['status'] = 'unavailable'
            metadata['error'] = f'Warmup failed: {failure}'
        write_json(output / 'model-metadata.json', metadata)

    for comparison in protocol['comparisons']:
        needs_model = any(arm.get('model') == 'ollama' for arm in comparison['arms'])
        if needs_model and metadata.get('status') != 'measured':
            for seed in comparison['seeds']:
                for arm in comparison['arms']:
                    cells.append({'comparison': comparison['id'], 'arm': arm['id'], 'seed': seed,
                                  'status': 'blocked', 'failure': metadata.get('error', 'model metadata unavailable'),
                                  'timingGroup': comparison['timing']})
            continue
        for seed in comparison['seeds']:
            for arm in comparison['arms']:
                name = f"{comparison['id']}-s{seed}-{arm['id']}"
                run_dir = output / 'runs' / comparison['id'] / str(seed) / arm['id']
                replay_dir = output / 'replays' / comparison['id'] / str(seed) / arm['id']
                need_replay = arm['id'] in comparison.get('replay', [])
                controllers = controllers_for(arm, comparison['controllers'], output)
                if args.resume and read_export(run_dir) and read_export(run_dir)['summary'].get('status') == 'succeeded' and (not need_replay or (read_export(replay_dir) and read_export(replay_dir)['summary'].get('status') == 'succeeded')):
                    exported = read_export(run_dir)
                    replay_export = read_export(replay_dir) if need_replay else None
                    print(f'{name}: resume')
                    cells.append(cell_from_export(comparison, arm, seed, controllers, exported, replay_export, replay_dir if need_replay else None))
                    continue
                timeout = 240 if comparison['timing'] == 'paced' else 90
                code, meta = run_checked(name, timeout, logs, runner_command(
                    str(run_dir), arm['script'], comparison['timing'], comparison['ticks'], seed,
                    comparison['scenario'], controllers, arm['model'], protocol['modelConfig']))
                exported = read_export(run_dir)
                cells.append({'comparison': comparison['id'], 'arm': arm['id'], 'seed': seed, 'script': arm['script'],
                              'model': arm['model'], 'politicsEnabled': arm.get('politicsEnabled'),
                              'timingGroup': comparison['timing'], 'ticks': comparison['ticks'],
                              'scenario': comparison['scenario'], 'scenarioHash': sha256(comparison['scenario']),
                              'controllers': controllers, 'controllersHash': sha256(controllers),
                              'status': 'succeeded' if code == 0 and exported else 'failed', 'exitCode': code,
                              'check': meta, 'export': None if exported is None else {
                                  'stateHash': exported['summary'].get('stateHash'),
                                  'status': exported['summary'].get('status'),
                                  'timingPolicy': exported['manifest'].get('timingPolicy'),
                                  'configHash': exported['manifest'].get('configHash'),
                                  'rulesVersion': exported['manifest'].get('rulesVersion'),
                                  'schemaVersion': exported['manifest'].get('schemaVersion'),
                                  'commit': exported['manifest'].get('commit'),
                                  'promptVersion': (exported['manifest'].get('model') or {}).get('promptVersion'),
                                  'modelDigest': (exported['manifest'].get('model') or {}).get('modelDigest')}})
                if code == 0 and exported and need_replay:
                    replay_code, replay_meta = run_checked(name + '-replay', 90, logs, [
                        'dotnet', 'run', '--project', 'src/Research.Runner', '-c', 'Release', '--no-build', '--',
                        '--scenario', str(run_dir / 'scenario.json'), '--replay', str(run_dir / 'commands.jsonl'),
                        '--ticks', str(comparison['ticks']), '--output', str(replay_dir), '--mode', 'headless', '--script', 'hybrid'])
                    replay_export = read_export(replay_dir)
                    cells[-1]['replay'] = {'status': 'succeeded' if replay_code == 0 and replay_export else 'failed',
                                           'exitCode': replay_code, 'check': replay_meta,
                                           'directory': str(replay_dir),
                                           'timingPolicy': None if replay_export is None else replay_export['manifest'].get('timingPolicy'),
                                           'stateHash': None if replay_export is None else replay_export['summary'].get('stateHash')}

    series = {'protocol': protocol['id'], 'protocolFile': args.protocol, 'protocolHash': sha256(args.protocol),
              'commit': os.environ['DELSK_COMMIT'], 'modelConfigHash': sha256(protocol['modelConfig']),
              'build': build_meta, 'model': metadata, 'cells': cells,
              'status': 'completed' if all(cell['status'] == 'succeeded' and cell.get('replay', {}).get('status', 'succeeded') == 'succeeded' for cell in cells) else 'failed'}
    write_json(output / 'series-manifest.json', series)
    print(f"P24 series {series['status']}: {output / 'series-manifest.json'}")
    return 0 if series['status'] == 'completed' else 1


if __name__ == '__main__':
    sys.exit(main())
