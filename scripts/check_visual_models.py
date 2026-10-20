#!/usr/bin/env python3
"""Isolated Godot render fixture. Test class never enters the ordinary package. Шелестов Максим Евгеньевич."""
import argparse,os,socket,subprocess,sys,time
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--godot',required=True);p.add_argument('--xvfb',default='Xvfb');p.add_argument('--output',type=Path,required=True);a=p.parse_args()
root=Path(__file__).resolve().parents[1];out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
def check(name,timeout,command,env=None):
 subprocess.run([sys.executable,str(root/'scripts/run_check.py'),'--name',name,'--timeout',str(timeout),'--log-directory',str(out/'checks'),'--',*map(str,command)],cwd=root,env=env,check=True)
check('stage',300,[sys.executable,'scripts/build_developer_client.py','--output',out/'client'])
project=out/'client/source/src/Client.Godot'
(project/'VisualModelsProbe.cs').write_text((root/'tests/Client.Probes/VisualModelsProbe.cs').read_text())
(project/'VisualModelsProbe.tscn').write_text('[gd_scene load_steps=2 format=3]\n[ext_resource type="Script" path="res://VisualModelsProbe.cs" id="1"]\n[node name="Models" type="Control"]\nlayout_mode=3\nanchors_preset=15\nanchor_right=1.0\nanchor_bottom=1.0\nscript=ExtResource("1")\n')
check('build',180,['dotnet','build',project/'Delsk.Client.csproj','-c','Debug','-p:DelskDeveloper=true','-p:DelskDeveloperPackage=true','--no-restore','--disable-build-servers','-m:1'])
check('import',120,[a.godot,'--headless','--path',project,'--editor','--import'])
env=os.environ.copy();env.update(DISPLAY='127.0.0.1:94.0',LIBGL_ALWAYS_SOFTWARE='1',XDG_DATA_HOME=str(out/'data'))
with (out/'xvfb.log').open('w') as log:
 server=subprocess.Popen([a.xvfb,':94','-screen','0','1440x900x24','-nolisten','unix','-listen','tcp','-ac'],env=env,stdout=log,stderr=log)
 try:
  for _ in range(50):
   try:
    with socket.create_connection(('127.0.0.1',6094),timeout=.1):break
   except OSError:time.sleep(.1)
  check('render',70,[a.godot,'--path',project,'--audio-driver','Dummy','res://VisualModelsProbe.tscn','--','--output',out],env)
 finally:
  server.terminate()
  try:server.wait(timeout=5)
  except subprocess.TimeoutExpired:server.kill();server.wait(timeout=5)
