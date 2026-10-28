#!/usr/bin/env python3
"""Isolated D2 native development probe. No Developer API. Шелестов Максим Евгеньевич."""
import argparse,json,os,shutil,socket,subprocess,sys,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--probe',choices=['DevelopmentProbe','ProductionProbe','FleetProbe','RoadProbe','BillingProbe','ServiceProbe','ServiceRunProbe'],default='DevelopmentProbe');p.add_argument('--godot',required=True);p.add_argument('--fixtures',required=True,type=Path);p.add_argument('--xvfb',default='Xvfb');p.add_argument('--xorg',help='Existing Xorg with dummy driver as an alternative to Xvfb');p.add_argument('--output',type=Path,required=True);p.add_argument('--reuse-stage',action='store_true');p.add_argument('--maps',nargs='+',default=['storage']);p.add_argument('--counts',nargs='+',type=int,choices=[3,6,10],default=[3]);p.add_argument('--preview-only',action='store_true');a=p.parse_args();out=a.output.resolve();stage=out/'source';project=stage/'src/Client.Godot'
if not a.reuse_stage and out.exists() and any(out.iterdir()):p.error('Use a new/empty output or --reuse-stage for own stage')
out.mkdir(parents=True,exist_ok=True)
marker=stage/'.delsk-ordinary-probe'
if a.reuse_stage and not marker.is_file():p.error('Refusing to reuse a foreign stage')
def check(name,timeout,cmd,env=None):
 subprocess.run([sys.executable,str(ROOT/'scripts/run_check.py'),'--name',name,'--timeout',str(timeout),'--log-directory',str(out/'checks'),'--',*map(str,cmd)],cwd=ROOT,env=env,check=True)
if not a.reuse_stage:
 names=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z'],cwd=ROOT,text=True).split('\0')
 for name in names:
  source=ROOT/name
  if source.is_file() and (name.startswith(('src/','config/','scenarios/','docs/vnext/')) or name in ['Directory.Build.props','Directory.Build.targets','global.json']):
   dest=stage/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,dest)
 # Serial import is a measured Windows fix already used in the project; isolated probe only.
 with (project/'project.godot').open('a') as f:f.write('\n[editor]\nimport/use_multiple_threads=false\n')
 marker.write_text('Owned isolated ordinary probe stage\n')
else:
 names=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z'],cwd=ROOT,text=True).split('\0')
 for name in names:
  source=ROOT/name
  if source.is_file() and name.startswith(('src/','config/','scenarios/')):
   dest=stage/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,dest)
 with (project/'project.godot').open('a') as f:f.write('\n[editor]\nimport/use_multiple_threads=false\n')
shutil.copyfile(ROOT/('tests/Client.Probes/'+a.probe+'.cs'),project/(a.probe+'.cs'))
(project/(a.probe+'.tscn')).write_text('[gd_scene load_steps=2 format=3]\n[ext_resource type="Script" path="res://DevelopmentProbe.cs" id="1"]\n[node name="DevelopmentProbe" type="Control"]\nlayout_mode=3\nanchors_preset=15\nanchor_right=1.0\nanchor_bottom=1.0\nscript=ExtResource("1")\n'.replace("DevelopmentProbe",a.probe))
check('restore',180,['dotnet','restore',project/'Delsk.Client.csproj','--locked-mode','--disable-parallel','-m:1'])
check('build',180,['dotnet','build',project/'Delsk.Client.csproj','--no-restore','--disable-build-servers','-m:1'])
check('import',120,[a.godot,'--headless','--path',project,'--editor','--import'])
env=os.environ.copy();env.update(DISPLAY='127.0.0.1:94.0',LIBGL_ALWAYS_SOFTWARE='1')
server_command=[a.xvfb,':94','-screen','0','1440x1024x24','-nolisten','unix','-listen','tcp','-ac']
if a.xorg:
 config=out/'xorg.conf';config.write_text('''Section "ServerFlags"
 Option "AutoAddDevices" "false"
 Option "AutoEnableDevices" "false"
 Option "AllowMouseOpenFail" "true"
EndSection
Section "Device"
 Identifier "Dummy"
 Driver "dummy"
 VideoRam 256000
EndSection
Section "Monitor"
 Identifier "Monitor"
 HorizSync 5.0 - 1000.0
 VertRefresh 5.0 - 200.0
 Modeline "1440x1024" 120.00 1440 1480 1520 1600 1024 1027 1030 1100
EndSection
Section "Screen"
 Identifier "Screen"
 Device "Dummy"
 Monitor "Monitor"
 DefaultDepth 24
 SubSection "Display"
  Depth 24
  Modes "1440x1024"
 EndSubSection
EndSection
''')
 server_command=[a.xorg,':94','-config',str(config),'-logfile',str(out/'xorg.log'),'-nolisten','unix','-listen','tcp','-ac','-noreset']
with (out/'xvfb.log').open('w') as log:
 server=subprocess.Popen(server_command,stdout=log,stderr=log)
 try:
  for _ in range(50):
   try:
    with socket.create_connection(('127.0.0.1',6094),timeout=.1):break
   except OSError:time.sleep(.1)
  for key in a.maps:
   for count in a.counts:
    folder=out/f'{key}-{count}';folder.mkdir(exist_ok=True)
    runtime=folder/('runtime-'+str(time.time_ns()));runtime.mkdir()
    runenv=dict(env,XDG_DATA_HOME=str(runtime/'data'),XDG_CONFIG_HOME=str(runtime/'config'),XDG_CACHE_HOME=str(runtime/'cache'))
    # Full bilingual civic/multi-window lifecycle is longer than the separate 30s smoke under software GL.
    check(f'native-{key}-{count}',150,[a.godot,'--path',project,'--rendering-method','gl_compatibility','--audio-driver','Dummy','res://'+a.probe+'.tscn','--','--output',folder,'--fixtures',a.fixtures.resolve(),'--windowed',*(['--preview-only'] if a.preview_only else [])],runenv)
    check(f'logs-{key}-{count}',15,[sys.executable,ROOT/'scripts/verify_platform_probe.py','--log-prefix',out/f'checks/native-{key}-{count}'])
 finally:
  server.terminate()
  try:server.wait(timeout=5)
  except subprocess.TimeoutExpired:server.kill();server.wait(timeout=5)
print('Native development controls passed')
