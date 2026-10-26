#!/usr/bin/env python3
"""Native HUD and God-mode checks in an isolated Developer client. Шелестов Максим Евгеньевич."""
import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--godot", required=True)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--import-cache", type=Path, help="Already verified source project imports; asset bytes must match the isolated stage")
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
# Fresh Windows stages hit 0xC0000005 during resource import, before the probe ran.
# Use Godot's serial importer in this disposable test project to avoid concurrent imports.
# Keep nonzero exits fatal and run the complete native Debug/ExportDebug checks.
settings = project / "project.godot"
settings.write_text(settings.read_text(encoding="utf-8").replace(
    "[application]", "[editor]\nimport/use_multiple_threads=false\n\n[application]", 1), encoding="utf-8")
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
# Explicit quit also bounds the editor lifecycle after the synchronous import.
# Windows 4.7.2 can finish importing yet retain an editor process without it.
if args.import_cache:
    # Windows Mono editor can hang on shutdown after a successful import. Reuse the
    # ordinary project's already verified imports; never accept a timed-out process.
    check("hud-god-import", 30, [sys.executable, root / "scripts/reuse_godot_imports.py", "--source", args.import_cache,
          "--target", project, "--report", output / "import-cache.json"])
else:
    check("hud-god-import", 120, [args.godot, "--headless", "--path", project, "--editor", "--import", "--quit"])
check("hud-god-controls", 60, [args.godot, "--headless", "--audio-driver", "Dummy", "--path", project,
      "res://HudGodProbe.tscn", "--", "--output", output, "--windowed"])
report = json.loads((output / "hud-god-check.json").read_text(encoding="utf-8"))
if report.get("status") != "succeeded" or len(report.get("checks", [])) < 20:
    raise SystemExit("Incomplete HUD/God-mode report")
print("Native HUD and God-mode checks passed:", len(report["checks"]))

# Godot export invokes ExportDebug without DelskDeveloper command-line properties.
# Verify that exact build and execute its assemblies in a fresh engine process.
# Drop the packager's forced flags: test the original project's standard defaults.
shutil.copyfile(root / "Directory.Build.props", project.parents[1] / "Directory.Build.props")
check("hud-god-export-restore", 180, ["dotnet", "restore", project / "Delsk.Client.csproj",
      "-p:Configuration=ExportDebug", "--locked-mode", "--disable-parallel", "-m:1"])
check("hud-god-export-build", 180, ["dotnet", "build", project / "Delsk.Client.csproj", "-c", "ExportDebug",
      "--no-restore", "--disable-build-servers", "-m:1"])
export_bin = project / ".godot/mono/temp/bin/ExportDebug"
if not (export_bin / "Simulation.Developer.dll").is_file():
    raise SystemExit("ExportDebug silently lost Developer support")
shutil.rmtree(project / ".godot/mono/temp/bin/Debug")
shutil.copytree(export_bin, project / ".godot/mono/temp/bin/Debug")
export_output = output / "export-debug"
export_output.mkdir(exist_ok=True)
check("hud-god-export-controls", 60, [args.godot, "--headless", "--audio-driver", "Dummy", "--path", project,
      "res://HudGodProbe.tscn", "--", "--output", export_output, "--windowed"])
export_report = json.loads((export_output / "hud-god-check.json").read_text(encoding="utf-8"))
if export_report.get("status") != "succeeded" or len(export_report.get("checks", [])) < 20:
    raise SystemExit("Incomplete ExportDebug HUD/God-mode report")
check("hud-god-export-logs", 30, [sys.executable, root / "scripts/verify_platform_probe.py",
      "--log-prefix", output / "checks/hud-god-controls", "--log-prefix", output / "checks/hud-god-export-controls"])
print("Standard ExportDebug with original props and no CLI Developer flags passed all", len(export_report["checks"]), "native checks")
