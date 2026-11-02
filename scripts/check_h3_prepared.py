#!/usr/bin/env python3
"""H3 five-trip tapes on all three maps; full future-input playback through dangerous saves.
Author: Шелестов Максим Евгеньевич. Prepared states, never natural economic measurements.
"""
import argparse
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--fixtures', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
out = args.output.resolve()
fixtures = args.fixtures.resolve()
if out.exists() and any(out.iterdir()):
    parser.error('Use a new output; retain all previous attempts')
out.mkdir(parents=True, exist_ok=True)
runner = ['dotnet', ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll']
checks, exports, cases = [], [], []


def check(name, command, seconds=120):
    subprocess.run([sys.executable, ROOT/'scripts/run_check.py', '--name', name,
                    '--timeout', str(seconds), '--log-directory', out/'checks', '--', *map(str, command)], cwd=ROOT, check=True)
    checks.append(name)


for map_key in ['compact_valley', 'small_river', 'large_districts']:
    name = map_key+'-h3-five-trips'
    end = int((fixtures/(name+'.tick')).read_text())
    common = [*runner, '--scenario', fixtures/(name+'.json'), '--replay', fixtures/(name+'.commands.jsonl'), '--ticks', end]
    baseline = out/map_key
    check(map_key+'-continuous', [*common, '--output', baseline])
    exports.append(baseline)
    cuts = {}
    for phase in ['pending', 'escrow', 'loaded']:
        cut = int((fixtures/(map_key+'-h3-'+phase+'.tick')).read_text())
        saved, loaded = out/(map_key+'-'+phase+'-saved'), out/(map_key+'-'+phase+'-loaded')
        checkpoint = out/(map_key+'-'+phase+'.save.json')
        check(map_key+'-'+phase+'-save', [*common, '--output', saved, '--checkpoint', checkpoint, '--checkpoint-at', cut])
        check(map_key+'-'+phase+'-load', [*runner, '--load', checkpoint, '--output', loaded])
        check(map_key+'-'+phase+'-compare', [sys.executable, ROOT/'scripts/verify_postg2_continuation.py', baseline, loaded,
                                           '--output', out/(map_key+'-'+phase+'-comparison.json')], 30)
        check(map_key+'-'+phase+'-saved-compare', [sys.executable, ROOT/'scripts/verify_postg2_continuation.py', baseline, saved], 30)
        exports.extend([saved, loaded])
        cuts[phase] = cut
    cases.append(dict(map=map_key, finalTick=end, cuts=cuts))
check('independent-accounting', [sys.executable, ROOT/'scripts/verify_results.py', *exports], 60)
check('capital-and-public-privacy', [sys.executable, ROOT/'scripts/verify_public_economy.py', *exports], 60)
(out/'gate.json').write_text(json.dumps(dict(status='succeeded', processChecks=len(checks), exports=len(exports),
    exactComparisonPairs=18, cases=cases, scope='Prepared five physical trips and real payments; pending/escrow/loaded saves resume every future command through final settlement. All31 exports, independent accounting and public privacy. No natural progression or device claim.'), indent=2)+'\n')
