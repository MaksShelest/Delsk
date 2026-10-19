#!/usr/bin/env python3
"""Stage an isolated Developer project; never overwrite the ordinary client cache. Шелестов Максим Евгеньевич."""
import argparse, shutil, subprocess, sys
from pathlib import Path

p=argparse.ArgumentParser()
p.add_argument('--output',type=Path,required=True)
p.add_argument('--dotnet',default='dotnet')
p.add_argument('--refresh-locks',action='store_true')
a=p.parse_args()
root=Path(__file__).resolve().parents[1];out=a.output.resolve();stage=out/'source'
if stage==root or root.is_relative_to(stage) or stage.is_relative_to(root/'src'):
    p.error('Use a separate artifact directory')
if stage.exists():
    if not (stage/'.delsk-developer-stage').is_file():p.error('Refusing to replace a directory not created by this packager')
    shutil.rmtree(stage)
stage.mkdir(parents=True)
(stage/'.delsk-developer-stage').write_text('Generated Developer staging directory. Edit repository sources, not this copy.\n')
names=set(subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard'],cwd=root,text=True).splitlines())
for name in sorted(names):
    src=root/name
    if not src.is_file() or not (name.startswith(('src/','config/','scenarios/')) or name in ('Directory.Build.props','Directory.Build.targets','global.json')):continue
    dest=stage/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(src,dest)
project=stage/'src/Client.Godot/Delsk.Client.csproj'
godot_project=project.parent/'project.godot'
godot_project.write_text(godot_project.read_text().replace('config/name="Delsk"','config/name="Delsk Developer"'))
for label,command in [('restore',[a.dotnet,'restore',str(project),'-p:DelskDeveloper=true','-p:DelskDeveloperPackage=true','--disable-parallel','-m:1',*([] if a.refresh_locks else ['--locked-mode'])]),
                      ('build',[a.dotnet,'build',str(project),'-c','Debug','-p:DelskDeveloper=true','-p:DelskDeveloperPackage=true','--no-restore','--disable-build-servers','-m:1'])]:
    r=subprocess.run([sys.executable,str(root/'scripts/run_check.py'),'--name','developer-client-'+label,'--timeout','180','--log-directory',str(out/'checks'),'--',*command],cwd=stage)
    if r.returncode:raise SystemExit(r.returncode)
print('Developer project:',project.parent)
