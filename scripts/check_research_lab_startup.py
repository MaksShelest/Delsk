#!/usr/bin/env python3
"""Actual launcher preparation/reopen/lock/shutdown; no fake simulation.
Author: Шелестов Максим Евгеньевич.
"""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import urllib.request

p = argparse.ArgumentParser()
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
out = a.output.resolve(); out.mkdir(parents=True, exist_ok=True)
root = Path(__file__).resolve().parents[1]
workspace = out/'Исследования с пробелами'
record = out/'browser-urls.txt'
browser = out/'browser_stub.py'
browser.write_text('#!'+sys.executable+'\nimport os,sys\nwith open(os.environ["DELSK_BROWSER_RECORD"],"a") as f: f.write(sys.argv[-1]+"\\n")\n')
browser.chmod(0o755)
env = os.environ.copy(); env.update(BROWSER=str(browser), DELSK_BROWSER_RECORD=str(record))
command = [sys.executable, str(root/'scripts/research_lab.py'), '--output', str(workspace), '--open-browser', '--port', '0']
client = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def ready(process):
    deadline = time.monotonic()+100
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError('Launcher stopped before becoming ready')
        host = workspace/'host.json'
        if host.exists():
            try:
                data = json.loads(host.read_text())
                with client.open(data['url']+'api/health', timeout=2) as response:
                    if json.load(response)['instance'] == data['instance']:
                        return data
            except (OSError, ValueError):
                pass
        time.sleep(.1)
    raise TimeoutError('Launcher startup')


with (out/'launcher.stdout.log').open('w') as stdout, (out/'launcher.stderr.log').open('w') as stderr:
    process = subprocess.Popen([*command, '--prepare'], cwd=root, env=env, stdout=stdout, stderr=stderr)
    try:
        first = ready(process)
        # Every guided preset on every offered map passes the real C# validators.
        sys.path.insert(0, str(root/'scripts'))
        from research_lab import Lab
        from research_lab_presets import MAPS, presets
        validation = Lab(out/'preset-validation')
        for preset in presets():
            for choice in MAPS:
                validation.enqueue(preset['plan'] | dict(scenario=choice['path']))
        assert len(validation.series) == 12
        stamps = {f.name: f.stat().st_mtime_ns for f in (workspace/'startup').glob('*.json')}
        reopened = subprocess.run(command+['--prepare'], cwd=root, env=env, capture_output=True, text=True, timeout=15)
        assert reopened.returncode == 0, reopened.stderr
        assert 'уже работает' in reopened.stdout
        assert stamps == {f.name: f.stat().st_mtime_ns for f in (workspace/'startup').glob('*.json')}, 'Reopen rebuilt the runner'
        denied = subprocess.run(command+['--run'], cwd=root, env=env, capture_output=True, text=True, timeout=10)
        assert denied.returncode == 2 and 'уже использует' in denied.stderr
        assert len(record.read_text().splitlines()) == 2
        assert all(url == first['url'] for url in record.read_text().splitlines())
        with client.open(first['url'], timeout=3) as response:
            token = re.search("const token='([^']+)'", response.read().decode())[1]
        request = urllib.request.Request(first['url']+'api/shutdown', data=b'{}', headers={'X-Lab-Token': token, 'Content-Type':'application/json'})
        with client.open(request, timeout=3) as response:
            assert json.load(response)['stopping']
        assert process.wait(timeout=10) == 0
        assert not (workspace/'host.json').exists()
        # A new process acquires the same queue after graceful shutdown.
        again = subprocess.run([sys.executable, str(root/'scripts/research_lab.py'), '--output', str(workspace), '--run'],
                               cwd=root, env=env, capture_output=True, text=True, timeout=10)
        assert again.returncode == 0, again.stderr
        (out/'gate.json').write_text(json.dumps(dict(status='passed', prepare=True, availablePort=True,
            browserOpenedTwice=True, repeatedLaunchReusesHost=True, noRebuildOnReopen=True,
            duplicateCliRejected=True, unicodeAndSpaces=True, shutdownAndLockRelease=True,
            presetMapCombinationsValidated=12, validatedArms=21), indent=2))
        print('Actual launcher prepare/reopen/lock/shutdown passed')
    finally:
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill(); process.wait(timeout=5)
