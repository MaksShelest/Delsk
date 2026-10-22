#!/usr/bin/env python3
"""Isolated native Godot appearance smoke test. Автор: Шелестов Максим Евгеньевич."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

p = argparse.ArgumentParser()
p.add_argument('--godot', required=True, help='Godot 4.7.2 Mono executable; a graphical display is required')
p.add_argument('--dotnet', default='dotnet')
p.add_argument('--output', type=Path, default=Path('artifacts/world-skins'))
a = p.parse_args()
root = Path(__file__).resolve().parents[1]
out = a.output.resolve()
out.mkdir(parents=True, exist_ok=True)
with tempfile.TemporaryDirectory(prefix='delsk-skins-qa-') as temporary:
    stage = Path(temporary)
    for folder in ['src', 'config', 'scenarios']:
        shutil.copytree(root / folder, stage / folder,
                        ignore=shutil.ignore_patterns('bin', 'obj', '.godot'))
    for name in ['global.json', 'Directory.Build.props', 'Directory.Build.targets', 'NuGet.Config', 'nuget.config']:
        if (root / name).exists():
            shutil.copy2(root / name, stage / name)
    client = stage / 'src/Client.Godot'
    shutil.copy2(root / 'tests/Client.Probes/WorldSkinsProbe.cs', client / 'WorldSkinsProbe.cs')
    (client / 'WorldSkinsProbe.tscn').write_text(
        '[gd_scene load_steps=2 format=3]\n'
        '[ext_resource type="Script" path="res://WorldSkinsProbe.cs" id="1"]\n'
        '[node name="WorldSkinsProbe" type="Control"]\nscript = ExtResource("1")\n')
    env = os.environ.copy()
    env.update(XDG_DATA_HOME=str(stage / 'userdata'), APPDATA=str(stage / 'userdata'),
               LOCALAPPDATA=str(stage / 'userdata'), GODOT_SILENCE_ROOT_WARNING='1')
    def check(name, seconds, command):
        result = subprocess.run([
            'python' if os.name == 'nt' else 'python3', str(root / 'scripts/run_check.py'),
            '--name', name, '--timeout', str(seconds), '--log-directory', str(out), '--', *map(str, command)
        ], cwd=stage, env=env, check=False)
        if result.returncode:
            raise SystemExit(result.returncode)
    check('appearance-build', 180, [a.dotnet, 'build', client / 'Delsk.Client.csproj',
          '--disable-build-servers', '-m:1', '-p:DelskDeveloper=false', '-v', 'minimal'])
    base = [a.godot, '--path', client, '--audio-driver', 'Dummy', '--rendering-method', 'gl_compatibility']
    check('appearance-import', 90, [*base, '--headless', '--editor', '--quit'])
    check('appearance-runtime', 60, [*base, 'res://WorldSkinsProbe.tscn', '--resolution', '1280x900',
          '--', '--output', out])
runtime = (out / 'appearance-runtime.stdout.log').read_text(errors='replace') + (out / 'appearance-runtime.stderr.log').read_text(errors='replace')
if 'WORLD_SKINS_PASS:' not in runtime or 'ERROR:' in runtime:
    raise SystemExit('Appearance runtime did not complete cleanly; inspect logs')
print('Appearance checks passed:', out)
