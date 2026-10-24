#!/usr/bin/env python3
"""Actual A1 process exits, extraction provenance and legacy save/replay. No alternative validator.
Author: Шелестов Максим Евгеньевич.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
out = a.output.resolve()
if out.exists() and (not out.is_dir() or any(out.iterdir())):
    p.error('Use a new or empty directory for gate artifacts')
out.mkdir(parents=True, exist_ok=True)
runner = ['dotnet', str(ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll')]
checks = []


def check(name, command, expected=0, timeout=30):
    result = subprocess.run([sys.executable, str(ROOT/'scripts/run_check.py'), '--name', name, '--timeout', str(timeout),
                             '--log-directory', str(out/'checks'), '--', *map(str, command)], cwd=ROOT, check=False)
    assert result.returncode == expected, (name, result.returncode, expected)
    checks.append(dict(name=name, exitCode=result.returncode))


def write(name, value):
    path = out/name; path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False), encoding='utf-8'); return path


def validate(name, source, expected=0, code=None):
    destination = out/name
    check(name, [*runner, '--validate-vnext-catalog', source, '--output', destination], expected)
    assert [f.name for f in destination.iterdir()] == ['catalog-validation.json']
    report = json.loads((destination/'catalog-validation.json').read_text())
    assert report['scope'] == 'structure' and report['status'] == ('succeeded' if expected == 0 else 'failed')
    if code:
        assert report['diagnostics'][0]['code'] == code
    if Path(source).is_file() and Path(source).stat().st_size <= 1024*1024:
        assert report['sha256'] == hashlib.sha256(Path(source).read_bytes()).hexdigest()
    return report


source = ROOT/'docs/vnext/data/BALANCE_VNEXT.json'
catalog = ROOT/'config/vnext/catalog.b1.json'
protected = [ROOT/'config/economy.json', source, catalog, *sorted((ROOT/'scenarios').glob('*'))]
before = {str(f): hashlib.sha256(f.read_bytes()).hexdigest() for f in protected if f.is_file()}
check('extract-check', [sys.executable, ROOT/'scripts/extract_vnext_catalog.py', '--check'])
for name in ['generated-1.json', 'generated-2.json']:
    check(name[:-5], [sys.executable, ROOT/'scripts/extract_vnext_catalog.py', '--output', out/name])
assert (out/'generated-1.json').read_bytes() == (out/'generated-2.json').read_bytes() == catalog.read_bytes()
altered = out/'source-changed.json'; altered.write_bytes(source.read_bytes()+b' ')
check('stale-source', [sys.executable, ROOT/'scripts/extract_vnext_catalog.py', '--source', altered, '--output', catalog, '--check'], 2)
report = validate('valid-b1', catalog)
assert report['counts'] == dict(items=34, recipes=23, industries=6)
original = json.loads(catalog.read_text())
small = copy.deepcopy(original)
small['items'] = [i for i in small['items'] if i['id'] in ('wood', 'plank', 'steel')]
small['recipes'] = small['recipes'][:1]; small['industries'] = small['industries'][:1]
assert validate('small', write('inputs/small.json', small))['counts'] == dict(items=3, recipes=1, industries=1)
broken = copy.deepcopy(original); broken['recipes'][0]['output'] = 'missing'
validate('bad-reference', write('inputs/bad.json', broken), 2, 'UnknownReference')
validate('missing-file', out/'absent.json', 2, 'IoError')
too_large = out/'inputs/large.json'
with too_large.open('wb') as stream:
    stream.truncate(100*1024*1024)  # Sparse: no huge in-memory buffer or file read needed.
assert validate('large', too_large, 2, 'TooLarge')['sha256'] is None

# Every simulation/research/model option is rejected before opening its nonexistent input.
extra = ['--scenario', '--economy', '--load', '--replay', '--ticks', '--controllers', '--model-mode', '--model-config',
         '--script', '--mode', '--seed', '--checkpoint', '--checkpoint-at', '--recover-backup', '--profile',
         '--human-ballot', '--validate-only', '--research', '--research-setup', '--model-wait', '--unknown']
for index, flag in enumerate(extra):
    destination = out/('mixed-'+str(index))
    check('mixed-'+str(index), [*runner, '--validate-vnext-catalog', catalog, '--output', destination, flag, 'absent'], 2)
    assert not destination.exists()
for name, args in [
    ('missing-value', ['--validate-vnext-catalog']),
    ('missing-output', ['--validate-vnext-catalog', catalog]),
    ('duplicate', ['--validate-vnext-catalog', catalog, '--validate-vnext-catalog', catalog, '--output', out/'duplicate']),
    ('duplicate-output', ['--output', out/'dup-a', '--output', out/'dup-b', '--validate-vnext-catalog', catalog]),
]:
    check(name, [*runner, *args], 2)

existing = out/'existing-series'; existing.mkdir(); marker = existing/'series.json'; marker.write_text('keep exactly')
check('preserve-output', [*runner, '--validate-vnext-catalog', catalog, '--output', existing], 2)
assert list(existing.iterdir()) == [marker] and marker.read_text() == 'keep exactly'
existing_file = out/'occupied-file'; existing_file.write_text('keep')
check('file-output', [*runner, '--validate-vnext-catalog', catalog, '--output', existing_file], 2)
assert existing_file.read_text() == 'keep'
prior = (out/'valid-b1/catalog-validation.json').read_bytes()
check('preserve-report', [*runner, '--validate-vnext-catalog', catalog, '--output', out/'valid-b1'], 2)
assert (out/'valid-b1/catalog-validation.json').read_bytes() == prior

# Existing execution branches, original profile + controller memory + direct load + replay.
common = [*runner, '--script', 'baseline', '--scenario', ROOT/'scenarios/presentation-map-one-v3.json',
          '--economy', ROOT/'config/economy.json', '--controllers', ROOT/'scenarios/baseline-controllers.json',
          '--ticks', '120', '--seed', '42', '--human-ballot', 'self']
check('legacy-profile', [*common, '--output', out/'legacy'])
check('legacy-save', [*common, '--output', out/'legacy-saved', '--checkpoint', out/'mid.save.json', '--checkpoint-at', '59'])
check('legacy-load', [*runner, '--load', out/'mid.save.json', '--ticks', '120', '--output', out/'legacy-loaded'])
check('legacy-continuation', [sys.executable, ROOT/'scripts/verify_checkpoint_exports.py', out/'legacy', out/'legacy-saved', out/'legacy', out/'legacy-loaded'])
check('legacy-replay', [*runner, '--script', 'baseline', '--scenario', out/'legacy/scenario.json', '--replay', out/'legacy/commands.jsonl',
                        '--ticks', '120', '--output', out/'legacy-replayed'])
check('legacy-replay-equal', [sys.executable, ROOT/'scripts/verify_checkpoint_exports.py', '--world-only', out/'legacy', out/'legacy-replayed'])
assert before == {str(f): hashlib.sha256(f.read_bytes()).hexdigest() for f in protected if f.is_file()}
(out/'gate.json').write_text(json.dumps(dict(status='succeeded', scope='structure', counts=report['counts'], catalogSha256=report['sha256'],
    sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(), checks=checks, exactExtraction=True, staleSourceRejected=True,
    preservedInputsAndOutputs=True, legacySaveLoadReplay=True, simulationFlagsRejected=len(extra)), indent=2)+'\n')
print('A1 CLI/extraction and existing profile/save/load/replay passed')
