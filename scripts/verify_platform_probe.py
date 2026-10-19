#!/usr/bin/env python3
"""Check actual probe reports and Godot logs; a zero exporter exit alone is insufficient.
Author: Шелестов Максим Евгеньевич.
"""
import argparse
import csv
import json
from pathlib import Path
import struct

KNOWN_EDITOR_ERROR = 'ERROR: EditorSettings not instantiated yet when getting setting "export/android/shutdown_adb_on_exit".'
BUSY_PARENT = "remove_child() can't be called at this time"


def godot_problems(text):
    problems = []
    for line in text.splitlines():
        stripped = line.strip()
        if stripped == KNOWN_EDITOR_ERROR:
            continue
        if stripped.startswith('ERROR:') or stripped.startswith('SCRIPT ERROR:') or BUSY_PARENT in stripped or 'Parent node is busy adding/removing children' in stripped:
            problems.append(stripped)
    return problems


def self_test():
    if godot_problems(KNOWN_EDITOR_ERROR + '\nDELSK_PLATFORM_PASS ticks=62'):
        raise SystemExit('Known editor notice was treated as a probe failure')
    busy = "ERROR: Parent node is busy adding/removing children, remove_child() can't be called at this time."
    if not godot_problems(busy) or not godot_problems(BUSY_PARENT):
        raise SystemExit('Busy-parent scene change would pass unnoticed')
    if godot_problems('DELSK_PLATFORM_PASS ticks=62'):
        raise SystemExit('A passing probe line was treated as an error')


parser = argparse.ArgumentParser()
parser.add_argument('--report', type=Path)
parser.add_argument('--log-prefix', type=Path, action='append', default=[])
parser.add_argument('--windows-directory', type=Path)
parser.add_argument('--self-test-only', action='store_true')
args = parser.parse_args()
self_test()
if args.self_test_only:
    print('Platform probe log self-test passed')
    raise SystemExit(0)
if not (args.report or args.log_prefix or args.windows_directory):
    parser.error('Supply a report, log prefix or Windows export directory')
for prefix in args.log_prefix:
    metadata = json.loads(prefix.with_suffix('.json').read_text())
    if metadata['exit_code'] != 0 or metadata.get('timed_out'):
        raise SystemExit(f'Process failed: {prefix}')
    text = ''.join(Path(str(prefix) + suffix).read_text(errors='replace') for suffix in ('.stdout.log', '.stderr.log'))
    errors = godot_problems(text)
    if errors:
        raise SystemExit(f'Godot reported errors despite exit=0: {errors}')
if args.report:
    report = json.loads(args.report.read_text())
    root = Path(__file__).resolve().parents[1]
    with (root / 'scenarios/platform-cycle.checkpoints.csv').open(newline='') as source:
        expected = list(csv.DictReader(source))
    if (report['Status'] != 'passed' or report['Failure'] is not None or
            report['Tick'] != int(expected[-1]['tick']) or report['CheckedTicks'] != len(expected) or
            report['StateHash'] != expected[-1]['state_hash']):
        raise SystemExit(f'Probe did not complete the expected replay: {report}')
    if report.get('LifecycleAdapterChecked') is not True:
        raise SystemExit('Pause/resume adapter check was not executed')
    if report.get("CheckpointRestoreChecked") is not True or report.get("RestoredTicks") != [12, 34, 55, 60]:
        raise SystemExit("Full checkpoint/load check was not executed")
    with (root/'scenarios/platform-elections.checkpoints.csv').open(newline='') as source:
        elections = list(csv.DictReader(source))
    if (report.get('ElectionCheckedTicks') != len(elections) or
            report.get('ElectionStateHash') != elections[-1]['state_hash'] or
            report.get('ElectionRestoredTicks') != [9,19,20,21]):
        raise SystemExit('Election replay/checkpoint probe was not completed')
    print(f"{report['Platform']}: {report['CheckedTicks']} checkpoints match runner; save/load at12/34/55/60 verified")
    print(f"Election probe: {len(elections)} checkpoints match runner; save/load at9/19/20/21 verified")
    with (root/'scenarios/platform-support.checkpoints.csv').open(newline='') as source:
        support = list(csv.DictReader(source))
    if (report.get('SupportCheckedTicks') != len(support) or report.get('SupportStateHash') != support[-1]['state_hash'] or
            report.get('SupportRestoredTicks') != [17,18,19,27,99,100]):
        raise SystemExit('Support replay/checkpoint probe was not completed')
    print(f"Support probe: {len(support)} checkpoints match runner; Proposed/Accepted/Signed restore verified")
if args.report:
    if report.get('StoppedVotingRestoredTicks') != [3,6] or report.get('StoppedVotingFinalHash') != (root/'scenarios/platform-stopped.final.sha256').read_text().strip():
        raise SystemExit('Stopped voting save/load/final hash probe was not completed')
    print('Stopped voting: frozen boundaries3/6 restored, ballots consumed without economic ticks, final10 hash matches runner')
if args.windows_directory:
    folder = args.windows_directory
    executable = (folder / 'DelskProbe.exe').read_bytes()
    offset = struct.unpack_from('<I', executable, 0x3C)[0]
    if executable[:2] != b'MZ' or executable[offset:offset + 4] != b'PE\0\0' or struct.unpack_from('<H', executable, offset + 4)[0] != 0x8664:
        raise SystemExit('Expected an x86_64 Windows PE executable')
    data = folder / 'data_Delsk.Client_windows_x86_64'
    for name in ('Delsk.Client.dll', 'Simulation.Core.dll', 'Simulation.Hosting.dll', 'GodotSharp.dll', 'coreclr.dll', 'hostfxr.dll'):
        if not (data / name).is_file() or (data / name).stat().st_size == 0:
            raise SystemExit(f'Missing runtime/assembly: {name}')
    if not (folder / 'DelskProbe.pck').is_file():
        raise SystemExit('Missing Godot resource pack')
    runtime = json.loads((data / 'Delsk.Client.runtimeconfig.json').read_text())['runtimeOptions']
    if runtime['tfm'] != 'net10.0' or not runtime.get('includedFrameworks'):
        raise SystemExit('Windows bundle does not contain the expected .NET runtime')
    print('Windows x86_64 bundle and self-contained .NET runtime present; execution is a separate gate')
