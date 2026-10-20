#!/usr/bin/env python3
"""Native HUD and God-mode checks in an isolated Developer client. Шелестов Максим Евгеньевич."""
import argparse
import json
import subprocess
import sys
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--godot", required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")
root = Path(__file__).resolve().parents[1]
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)


def check(name, timeout, command):
    result = subprocess.run(
        [sys.executable, str(root / "scripts/run_check.py"), "--name", name,
         "--timeout", str(timeout), "--log-directory", str(output / "checks"), "--", *map(str, command)],
        cwd=root, check=False,
    )
    if result.returncode:
        logs = list((output / "checks").glob(f"{name}.*.log"))
        logs += list((output / "client/checks").glob("*.log"))
        for log in sorted(logs):
            print(f"--- {log.name} ---")
            print(log.read_text(encoding="utf-8", errors="replace")[-8000:])
        raise SystemExit(result.returncode)


check("hud-god-stage", 420, [sys.executable, root / "scripts/build_developer_client.py", "--output", output / "client"])
project = output / "client/source/src/Client.Godot"
(project / "HudGodProbe.cs").write_text((root / "tests/Client.Probes/HudGodProbe.cs").read_text(encoding="utf-8"), encoding="utf-8")
(project / "HudGodProbe.tscn").write_text(
    '[gd_scene load_steps=2 format=3]\n'
    '[ext_resource type="Script" path="res://HudGodProbe.cs" id="1"]\n'
    '[node name="HudGodProbe" type="Control"]\n'
    'layout_mode=3\nanchors_preset=15\nanchor_right=1.0\nanchor_bottom=1.0\nscript=ExtResource("1")\n',
    encoding="utf-8",
)
check("hud-god-build", 180, ["dotnet", "build", project / "Delsk.Client.csproj", "-c", "Debug",
      "-p:DelskDeveloper=true", "-p:DelskDeveloperPackage=true", "--no-restore", "--disable-build-servers", "-m:1"])
check("hud-god-import", 120, [args.godot, "--headless", "--path", project, "--editor", "--import"])
check("hud-god-controls", 60, [args.godot, "--headless", "--audio-driver", "Dummy", "--path", project,
      "res://HudGodProbe.tscn", "--", "--output", output, "--windowed"])
report = json.loads((output / "hud-god-check.json").read_text(encoding="utf-8"))
if report.get("status") != "succeeded" or len(report.get("checks", [])) < 17:
    raise SystemExit("Incomplete HUD/God-mode report")
print("Native HUD and God-mode checks passed:", len(report["checks"]))
