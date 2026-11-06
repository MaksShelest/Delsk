#!/usr/bin/env python3
"""Combined native smoke for the two I6-C2 maps and concrete car purchases.
Автор: Шелестов Максим Евгеньевич.
"""
import argparse
import subprocess
import sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--output',required=True)
p.add_argument('--godot',required=True)
p.add_argument('--prepared',help='Reuse frozen runtime references/imports; changed map snapshots recompile the ordinary client')
a=p.parse_args()
raise SystemExit(subprocess.run([sys.executable,str(ROOT/'scripts/check_i6a_native.py'),
    '--output',a.output,'--godot',a.godot,'--probe-source',str(ROOT/'tests/Client.Probes/I6C2Probe.cs'),
    '--case','all',*(['--prepared',a.prepared] if a.prepared else [])],cwd=ROOT).returncode)
