#!/usr/bin/env python3
"""I1 native physics/scene/save agreement in an isolated ordinary project.
Author: Шелестов Максим Евгеньевич. Synthetic Linux checks are not device acceptance.
"""
import argparse,hashlib,json,os,shutil,socket,subprocess,sys,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--fixtures',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--godot',required=True);p.add_argument('--display',type=int,default=96);a=p.parse_args()
out=a.output.resolve();stage=out/'source';project=stage/'src/Client.Godot'
if out.exists() and any(out.iterdir()):p.error('Use a new empty output directory; previous attempts are retained')
out.mkdir(parents=True);inputs={}
def check(name,timeout,cmd,env=None):
 subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout',str(timeout),'--log-directory',out/'checks','--',*map(str,cmd)],cwd=ROOT,env=env,check=True)
names=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z'],cwd=ROOT,text=True).split('\0')
for name in sorted(set(names)):
 source=ROOT/name
 if source.is_file() and (name.startswith(('src/','config/','scenarios/','docs/vnext/maps/')) or name in ['Directory.Build.props','Directory.Build.targets','global.json']):
  dest=stage/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,dest);inputs[name]=hashlib.sha256(source.read_bytes()).hexdigest()
for name in ['tests/Client.Probes/I1MovementProbe.cs','tools/AssemblyAudit/Program.cs','scripts/check_i1_native.py']:
 inputs[name]=hashlib.sha256((ROOT/name).read_bytes()).hexdigest()
check('restore',180,['dotnet','restore',project/'Delsk.Client.csproj','--locked-mode','--disable-parallel','-m:1'])
check('ordinary-build',180,['dotnet','build',project/'Delsk.Client.csproj','--no-restore','--disable-build-servers','-m:1'])
check('ordinary-audit',120,['dotnet','run','--project',ROOT/'tools/AssemblyAudit','-c','Release','--',project/'.godot/mono/temp/bin/Debug'])
assets=out/'assets'
check('asset-stage',30,[sys.executable,ROOT/'scripts/prepare_godot_asset_import.py','--source',project,'--output',assets,'--report',out/'asset-stage.json'])
check('native-import',120,[a.godot,'--headless','--path',assets,'--editor','--import','--quit'])
check('verified-import-reuse',60,[sys.executable,ROOT/'scripts/reuse_godot_imports.py','--source',assets,'--target',project,'--report',out/'asset-reuse.json'])
check('import-completeness',30,[sys.executable,ROOT/'scripts/reuse_godot_imports.py','--source',project,'--verify-only','--report',out/'asset-completeness.json'])
runtime=out/'runtime';env=os.environ|{'XDG_DATA_HOME':str(runtime/'data'),'XDG_CONFIG_HOME':str(runtime/'config'),'XDG_CACHE_HOME':str(runtime/'cache')}
check('platform-smoke',30,[a.godot,'--headless','--path',project,'--audio-driver','Dummy','--','--smoke','--report',out/'platform.json'],env)
check('platform-log',15,[sys.executable,ROOT/'scripts/verify_platform_probe.py','--log-prefix',out/'checks/platform-smoke'])
shutil.copyfile(ROOT/'tests/Client.Probes/I1MovementProbe.cs',project/'I1MovementProbe.cs')
(project/'I1MovementProbe.tscn').write_text('[gd_scene load_steps=2 format=3]\n[ext_resource type="Script" path="res://I1MovementProbe.cs" id="1"]\n[node name="I1MovementProbe" type="Control"]\nlayout_mode=3\nanchors_preset=15\nanchor_right=1.0\nanchor_bottom=1.0\nscript=ExtResource("1")\n')
check('isolated-probe-build',180,['dotnet','build',project/'Delsk.Client.csproj','--no-restore','--disable-build-servers','-m:1'])
with (out/'xvfb.log').open('w') as log:
 server=subprocess.Popen(['Xvfb',':'+str(a.display),'-screen','0','1024x800x24','-nolisten','unix','-listen','tcp','-ac'],stdout=log,stderr=log)
 try:
  ready=False
  for _ in range(50):
   if server.poll() is not None:raise RuntimeError('Owned Xvfb exited')
   try:
    with socket.create_connection(('127.0.0.1',6000+a.display),timeout=.1):ready=True;break
   except OSError:time.sleep(.1)
  if not ready:raise RuntimeError('Owned Xvfb did not become ready')
  env|={'DISPLAY':'127.0.0.1:'+str(a.display)+'.0','LIBGL_ALWAYS_SOFTWARE':'1'}
  check('i1-native',30,[a.godot,'--path',project,'--rendering-method','gl_compatibility','--audio-driver','Dummy','--windowed','--resolution','800x720','res://I1MovementProbe.tscn','--','--fixtures',a.fixtures.resolve(),'--output',out/'native'],env)
  check('i1-native-log',15,[sys.executable,ROOT/'scripts/verify_platform_probe.py','--log-prefix',out/'checks/i1-native'])
 finally:
  server.terminate()
  try:server.wait(timeout=5)
  except subprocess.TimeoutExpired:server.kill();server.wait(timeout=5)
assert json.loads((out/'native/native-check.json').read_text())['status']=='succeeded'
assert all(hashlib.sha256((ROOT/name).read_bytes()).hexdigest()==digest for name,digest in inputs.items()),'Native sources changed during the gate'
(out/'inputs.json').write_text(json.dumps(inputs,indent=2)+'\n')
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',matchedInputs=len(inputs),scope='Linux Godot native import, ordinary metadata audit, retained platform smoke, I1 actual scene4.5cells/s, midquantum restore, RU/EN help, save/readback. Software GL/synthetic fixture; no physical Windows/Android/FPS claim'),indent=2)+'\n')
