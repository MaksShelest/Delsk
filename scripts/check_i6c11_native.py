#!/usr/bin/env python3
"""One bounded I6-C1.1 native smoke, isolated prepared Small valley / three NPCs.
Автор: Шелестов Максим Евгеньевич.
"""
import argparse,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--output',required=True);p.add_argument('--godot',required=True)
a=p.parse_args()
raise SystemExit(subprocess.run([sys.executable,str(ROOT/'scripts/check_i6a_native.py'),'--output',a.output,'--godot',a.godot,'--probe-source',str(ROOT/'tests/Client.Probes/I6C11Probe.cs'),'--case','all'],cwd=ROOT).returncode)
