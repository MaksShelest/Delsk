#!/usr/bin/env python3
"""Native window checks for the tablet redesign, isolated from shipped clients.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,os,socket,subprocess,sys,time,shutil
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--godot',required=True);p.add_argument('--xvfb',default='Xvfb');p.add_argument('--output',type=Path,required=True);p.add_argument('--reuse-stage',action='store_true');a=p.parse_args()
root=Path(__file__).resolve().parents[1];out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
def check(name,timeout,cmd,env=None):
 subprocess.run([sys.executable,str(root/'scripts/run_check.py'),'--name',name,'--timeout',str(timeout),'--log-directory',str(out/'checks'),'--',*map(str,cmd)],cwd=root,env=env,check=True)
if not a.reuse_stage:check('stage',300,[sys.executable,root/'scripts/build_developer_client.py','--output',out/'client'])
project=out/'client/source/src/Client.Godot'
if a.reuse_stage:
 for f in (root/'src/Client.Godot').rglob('*'):
  rel=f.relative_to(root/'src/Client.Godot')
  if f.is_file() and not any(c in ('.godot','bin','obj') for c in rel.parts) and f.name not in ('project.godot','export_presets.cfg'):
   (project/rel).parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(f,project/rel)
(project/'TabletDesignProbe.cs').write_text((root/'tests/Client.Probes/TabletDesignProbe.cs').read_text(encoding='utf-8'),encoding='utf-8')
(project/'TabletDesignProbe.tscn').write_text('[gd_scene load_steps=2 format=3]\n[ext_resource type="Script" path="res://TabletDesignProbe.cs" id="1"]\n[node name="Tablets" type="Control"]\nlayout_mode=3\nanchors_preset=15\nanchor_right=1.0\nanchor_bottom=1.0\nscript=ExtResource("1")\n',encoding='utf-8')
shutil.copyfile(root/'scenarios/food-chain.json',out/'food-chain.json')
check('build',180,['dotnet','build',project/'Delsk.Client.csproj','-c','Debug','-p:DelskDeveloper=true','-p:DelskDeveloperPackage=true','--no-restore','--disable-build-servers','-m:1'])
check('import',120,[a.godot,'--headless','--path',project,'--editor','--import'])
env=os.environ.copy();env.update(DISPLAY='127.0.0.1:96.0',LIBGL_ALWAYS_SOFTWARE='1',XDG_DATA_HOME=str(out/'data'))
with (out/'xvfb.log').open('w') as log:
 server=subprocess.Popen([a.xvfb,':96','-screen','0','1440x1024x24','-nolisten','unix','-listen','tcp','-ac'],env=env,stdout=log,stderr=log)
 try:
  for _ in range(50):
   try:
    with socket.create_connection(('127.0.0.1',6096),timeout=.1):break
   except OSError:time.sleep(.1)
  check('window',110,[a.godot,'--path',project,'--rendering-method','gl_compatibility','--audio-driver','Dummy','res://TabletDesignProbe.tscn','--','--output',out],env)
 finally:
  server.terminate()
  try:server.wait(timeout=5)
  except subprocess.TimeoutExpired:server.kill();server.wait(timeout=5)
check('logs',30,[sys.executable,root/'scripts/verify_platform_probe.py','--log-prefix',out/'checks/window'])
