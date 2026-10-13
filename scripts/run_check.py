#!/usr/bin/env python3
"""Run one project check with a hard deadline and separate logs. Author: Шелестов Максим Евгеньевич."""
import argparse
import datetime
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time

parser = argparse.ArgumentParser()
parser.add_argument('--name', required=True)
parser.add_argument('--timeout', type=int, default=180)
parser.add_argument('--log-directory', default='artifacts/checks')
parser.add_argument('command', nargs=argparse.REMAINDER)
args = parser.parse_args()
command = args.command[1:] if args.command[:1] == ['--'] else args.command
if not re.fullmatch(r'[a-zA-Z0-9_-]+', args.name) or not command or args.timeout <= 0:
    parser.error('Use a simple check name, positive timeout and command after --')
folder = Path(args.log_directory)
folder.mkdir(parents=True, exist_ok=True)
metadata = {'command': command, 'timeout_seconds': args.timeout,
            'started_at': datetime.datetime.now(datetime.timezone.utc).isoformat()}
started = time.monotonic()
env = os.environ.copy()
env.update(DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1', DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1')
code = 1
with (folder / f'{args.name}.stdout.log').open('wb') as out, (folder / f'{args.name}.stderr.log').open('wb') as err:
    try:
        process = subprocess.Popen(command, stdout=out, stderr=err, env=env, start_new_session=os.name != 'nt')
        try:
            code = process.wait(timeout=args.timeout)
        except subprocess.TimeoutExpired:
            metadata['timed_out'] = True
            if os.name == 'nt':
                subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'], stdout=err, stderr=err, timeout=10, check=False)
            else:
                os.killpg(process.pid, signal.SIGTERM)
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=10)
            code = 124
    except OSError as failure:
        err.write(str(failure).encode('utf-8'))
        metadata['launch_error'] = str(failure)
metadata.update(exit_code=code, elapsed_seconds=round(time.monotonic()-started, 3),
                finished_at=datetime.datetime.now(datetime.timezone.utc).isoformat())
(folder / f'{args.name}.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'{args.name}: exit={code}; logs={folder}')
sys.exit(code if code >= 0 else 1)
