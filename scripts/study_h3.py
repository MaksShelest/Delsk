#!/usr/bin/env python3
"""H3's distinct Movement1 cohort using the retained H1/H2 bounded runner protocol.
Author: Шелестов Максим Евгеньевич. No Python simulation and no horizon override.
"""
import argparse
import json
import subprocess
import sys
from pathlib import Path
import study_postg2_h1 as protocol

ROOT = Path(__file__).resolve().parents[1]
PLAN = ROOT/'docs/research/h3/study-plan.json'
protocol.PLAN = PLAN
original_fingerprint = protocol.initial_fingerprint


def fingerprint(plan, engine):
    return original_fingerprint(plan, engine) | {'h3Worker': protocol.digest(__file__)}


def fresh_state(out, case, plan, frozen):
    inputs = out/'inputs'
    inputs.mkdir(exist_ok=True)
    for source, target in [(ROOT/f'scenarios/vnext/{case["map"]}.json', 'scenario.json'),
                           (ROOT/plan['profile'], 'economy.json'),
                           (ROOT/'config/vnext/balance.b1.json', 'balance.b1.json')]:
        (inputs/target).write_bytes(source.read_bytes())
    protocol.write(inputs/'ordinary.json', dict(npcCount=case['bots'], difficulty=case['difficulty'], seed=case['seed'], movementVersion=1))
    command = ['dotnet', out/'runtime/Research.Runner.dll', '--scenario', inputs/'scenario.json',
               '--economy', inputs/'economy.json', '--new-game', inputs/'ordinary.json',
               '--script', 'baseline', '--human-ballot', 'self', '--ticks', 1, '--output', out/'resolution']
    rc, _ = protocol.run_check('resolve-ordinary', 180, out, command)
    if rc:
        raise ValueError('Canonical C# ordinary resolution failed; attempt retained')
    resolved = protocol.read_metadata(out/'resolution/scenario.json')
    if resolved['maxTicks'] != 1000000 or resolved['economy']['movement'] != dict(version=1, speedDivisor=32):
        raise ValueError('Expected actual ordinary horizon and Movement1, without overrides')
    return dict(version=1, cohort=plan['id'], sourceCommit=subprocess.check_output(
        ['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True, timeout=30).strip(), case=case,
        status='ready', frozen=frozen, originalMaxTicks=resolved['maxTicks'], horizonOnlyOverride=False,
        targetTicks=plan[case['phase']]['targetTicks'], lastSavedTick=0, lastCheckpoint=None,
        segments=[], inputs={p.name: protocol.digest(p) for p in inputs.iterdir() if p.is_file()})


protocol.initial_fingerprint = fingerprint
protocol.fresh_state = fresh_state


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--case')
    parser.add_argument('--matrix', choices=['ordinary', 'extended', 'all'])
    parser.add_argument('--segments', type=int)
    parser.add_argument('--retry-failed', action='store_true')
    args = parser.parse_args()
    plan = protocol.read(PLAN)
    cases = protocol.cases(plan, 'ordinary')+protocol.cases(plan, 'extended')
    if args.matrix:
        print(json.dumps(dict(include=[c for c in cases if args.matrix == 'all' or c['phase'] == args.matrix])))
        return 0
    if not args.output or not args.case:
        parser.error('--output and --case required')
    case = next((c for c in cases if c['id'] == args.case), None)
    if case is None or args.segments is not None and args.segments < 0:
        parser.error('Use a preregistered case and nonnegative segment count')
    state = protocol.execute(args.output.resolve(), case, plan, args.segments, args.retry_failed)
    return 1 if state['status'] == 'blocked_audit' else 0


if __name__ == '__main__':
    sys.exit(main())
