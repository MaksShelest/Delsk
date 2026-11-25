#!/usr/bin/env python3
"""Run the rendered I9 motion probe in an isolated ordinary Godot project."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--godot', required=True)
parser.add_argument('--display', required=True)
parser.add_argument('--asset-cache', type=Path, required=True)
parser.add_argument('--scenario', type=Path, required=True)
parser.add_argument('--natural', type=Path)
parser.add_argument('--source', type=Path, default=ROOT)
parser.add_argument('--baseline', action='store_true')
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
out = args.output.resolve()
out.mkdir(parents=True, exist_ok=False)
source = args.source.resolve()
stage = out / 'source'
project = stage / 'src/Client.Godot'
runtime = project / '.godot/mono/temp/bin/Debug'
capture = out / 'capture'
capture.mkdir()

def check(name, limit, command, env=None):
    subprocess.run([sys.executable, ROOT / 'scripts/run_check.py', '--name', name,
                    '--timeout', str(limit), '--log-directory', out / 'checks',
                    '--', *map(str, command)], cwd=ROOT, env=env, check=True)

for directory in ('src', 'config', 'scenarios', 'docs/vnext/maps'):
    shutil.copytree(source / directory, stage / directory,
                    ignore=shutil.ignore_patterns('bin', 'obj', '.godot'))
for filename in ('global.json', 'Directory.Build.props', 'Directory.Build.targets'):
    shutil.copyfile(source / filename, stage / filename)

check('restore', 180, ['dotnet', 'restore', project / 'Delsk.Client.csproj',
                       '--locked-mode', '--disable-parallel', '-m:1'])
build = ['dotnet', 'build', project / 'Delsk.Client.csproj', '--no-restore', '--disable-build-servers', '-m:1']
check('ordinary-build', 180, build)
check('ordinary-audit', 120, ['dotnet', 'run', '--project', ROOT / 'tools/AssemblyAudit',
                              '-c', 'Release', '--', runtime])
probe = ROOT / 'tests/Client.Probes/I9MotionProbe.cs'
shutil.copyfile(probe, project / probe.name)
(project / 'I9MotionProbe.tscn').write_text(
    '[gd_scene load_steps=2 format=3]\n[ext_resource type="Script" path="res://I9MotionProbe.cs" id="1"]\n'
    '[node name="I9MotionProbe" type="Node"]\nscript=ExtResource("1")\n', encoding='utf-8')
check('probe-build', 180, [*build, '-p:BuildProjectReferences=false'])
reuse = [sys.executable, ROOT / 'scripts/reuse_godot_imports.py',
         '--source', args.asset_cache.resolve(), '--target', project]
check('reuse-imports', 60, [*reuse, '--report', out / 'import-reuse.json'])
check('verify-imports', 30, [*reuse, '--verify-only', '--report', out / 'import-integrity.json'])
env = os.environ | {'DISPLAY': args.display, 'LIBGL_ALWAYS_SOFTWARE': '1', 'LP_NUM_THREADS': '2',
                    'XDG_DATA_HOME': str(out / 'runtime')}
check('motion', 240, [args.godot, '--path', project, '--rendering-method', 'gl_compatibility',
                      '--audio-driver', 'Dummy', '--windowed', '--resolution', '960x540',
                      'res://I9MotionProbe.tscn', '--', '--scenario', args.scenario.resolve(),
                      '--output', capture, *(['--baseline'] if args.baseline else []),
                      *(['--natural', args.natural.resolve()] if args.natural else [])], env)
check('motion-logs', 15, [sys.executable, ROOT / 'scripts/verify_platform_probe.py',
                          '--log-prefix', out / 'checks/motion'])
for name in ('controlled', 'npc'):
    if (capture / name).exists():
        check(name + '-video', 60, ['ffmpeg', '-loglevel', 'error', '-framerate', '10', '-i',
              capture / name / '%03d.png', '-c:v', 'libx264', '-crf', '25', '-pix_fmt', 'yuv420p',
              '-movflags', '+faststart', capture / (name + '.mp4')])
(out / 'inputs.json').write_text(json.dumps({'scenarioSha256': hashlib.sha256(args.scenario.read_bytes()).hexdigest(),
    'probeSha256': hashlib.sha256(probe.read_bytes()).hexdigest(),
    'naturalSha256': hashlib.sha256(args.natural.read_bytes()).hexdigest() if args.natural else None},
    indent=2) + '\n', encoding='utf-8')
