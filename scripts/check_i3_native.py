#!/usr/bin/env python3
"""Frozen ordinary build, asset import, native I3 controls and screenshots.
Author: Шелестов Максим Евгеньевич. Linux software GL is not device acceptance.
"""
import argparse,hashlib,json,os,shutil,socket,subprocess,sys,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--fixtures',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--godot',required=True);p.add_argument('--xorg',default='Xorg');p.add_argument('--prepared',type=Path,help='Reuse verified upstream code; rebuild the ordinary client when its UI changes');a=p.parse_args()
out=a.output.resolve();stage=out/'source';project=stage/'src/Client.Godot'
if out.exists() and any(out.iterdir()):p.error('Use an empty output; previous attempts are retained')
out.mkdir(parents=True);inputs={}
client_changed=False
if a.prepared:
    shutil.copytree(a.prepared.resolve()/'source',stage)
    for name in ['I3RoadWorkProbe.cs','I3RoadWorkProbe.tscn']:(project/name).unlink(missing_ok=True)
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
for name in subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z'],cwd=ROOT,text=True).split('\0'):
    source=ROOT/name
    if source.is_file() and (name.startswith(('src/','config/','scenarios/','docs/vnext/maps/')) or name in ['Directory.Build.props','Directory.Build.targets','global.json']):
        destination=stage/name;destination.parent.mkdir(parents=True,exist_ok=True);
        if a.prepared:
            if digest(destination)!=digest(source):
                assert name.startswith('src/Client.Godot/'),'Prepared upstream source differs: '+name
                shutil.copyfile(source,destination);client_changed=True
        else:shutil.copyfile(source,destination)
        inputs[name]=digest(source)
def check(name,timeout,command,env=None):
    subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout',str(timeout),'--log-directory',out/'checks','--',*map(str,command)],cwd=ROOT,env=env,check=True)
if not a.prepared:check('restore',180,['dotnet','restore',project/'Delsk.Client.csproj','--locked-mode','--disable-parallel','-m:1'])
if not a.prepared:check('ordinary-build',180,['dotnet','build',project/'Delsk.Client.csproj','--no-restore','--disable-build-servers','-m:1'])
ordinary=project/'.godot/mono/temp/bin/Debug'
if a.prepared:
    assert json.loads((a.prepared.resolve()/'checks/ordinary-audit.json').read_text())['exit_code']==0
    frozen={f.name:digest(f) for f in ordinary.glob('*.dll') if f.name!='Delsk.Client.dll'}
    if client_changed:check('ordinary-client-build',180,['dotnet','build',project/'Delsk.Client.csproj','--no-restore','--disable-build-servers','-m:1','-p:BuildProjectReferences=false'])
    assert all(digest(ordinary/name)==h for name,h in frozen.items()),'UI build changed frozen upstream binaries'
(out/'inputs.json').write_text(json.dumps(inputs,indent=2)+'\n')
check('ordinary-audit',120,['dotnet','run','--project',ROOT/'tools/AssemblyAudit','-c','Release','--',ordinary])
references={f.name:digest(f) for f in ordinary.glob('*.dll') if f.name!='Delsk.Client.dll'}
assets=out/'assets'
check('assets',30,[sys.executable,ROOT/'scripts/prepare_godot_asset_import.py','--source',project,'--output',assets,'--report',out/'assets.json'])
check('import',120,[a.godot,'--headless','--path',assets,'--editor','--import','--quit'])
check('imports-reuse',60,[sys.executable,ROOT/'scripts/reuse_godot_imports.py','--source',assets,'--target',project,'--report',out/'import-reuse.json'])
env=os.environ|{'XDG_DATA_HOME':str(out/'runtime/data'),'XDG_CONFIG_HOME':str(out/'runtime/config'),'XDG_CACHE_HOME':str(out/'runtime/cache')}
check('platform-smoke',30,[a.godot,'--headless','--path',project,'--audio-driver','Dummy','--','--smoke','--report',out/'platform.json'],env)
check('platform-log',15,[sys.executable,ROOT/'scripts/verify_platform_probe.py','--log-prefix',out/'checks/platform-smoke'])
shutil.copyfile(ROOT/'tests/Client.Probes/I3RoadWorkProbe.cs',project/'I3RoadWorkProbe.cs');inputs['tests/Client.Probes/I3RoadWorkProbe.cs']=digest(ROOT/'tests/Client.Probes/I3RoadWorkProbe.cs')
(project/'I3RoadWorkProbe.tscn').write_text('[gd_scene load_steps=2 format=3]\n[ext_resource type="Script" path="res://I3RoadWorkProbe.cs" id="1"]\n[node name="I3RoadWorkProbe" type="Control"]\nlayout_mode=3\nanchors_preset=15\nanchor_right=1.0\nanchor_bottom=1.0\nscript=ExtResource("1")\n')
check('probe-build',180,['dotnet','build',project/'Delsk.Client.csproj','--no-restore','--disable-build-servers','-m:1','-p:BuildProjectReferences=false'])
assert all(digest(ordinary/name)==h for name,h in references.items()),'Probe changed ordinary runtime references'
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
with (out/'xorg.stdout.log').open('w') as log:
    server=subprocess.Popen([a.xorg,':94','-config',str(config),'-logfile',str(out/'xorg.log'),'-nolisten','unix','-listen','tcp','-ac','-noreset'],stdout=log,stderr=log)
    try:
        ready=False
        for _ in range(50):
            if server.poll() is not None:raise RuntimeError('Owned Xorg exited')
            try:
                with socket.create_connection(('127.0.0.1',6094),timeout=.1):ready=True;break
            except OSError:time.sleep(.1)
        assert ready,'Owned Xorg did not become ready'
        env|={'DISPLAY':'127.0.0.1:94.0','LIBGL_ALWAYS_SOFTWARE':'1','LP_NUM_THREADS':'2'}
        for case in ['ru-800','en-1000','en-360']:
            target=out/case
            check(case,120,[a.godot,'--path',project,'--rendering-method','gl_compatibility','--audio-driver','Dummy','--windowed','--resolution','1000x760','res://I3RoadWorkProbe.tscn','--','--touch-controls','--fixture',a.fixtures.resolve()/'prepared.save.json','--case',case,'--output',target],env)
            check(case+'-log',15,[sys.executable,ROOT/'scripts/verify_platform_probe.py','--log-prefix',out/'checks'/case]);assert json.loads((target/'native.json').read_text())['status']=='succeeded'
    finally:
        server.terminate()
        try:server.wait(timeout=5)
        except subprocess.TimeoutExpired:server.kill();server.wait(timeout=5)
assert all(digest(ROOT/name)==h for name,h in inputs.items()),'Native source changed during the gate'
(out/'inputs.json').write_text(json.dumps(inputs,indent=2)+'\n')
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',cases=3,scope='Linux Godot Mono, software GL, actual context/fleet card, mouse/touch, position/work/pause/focus/save/completion. Prepared funds/materials. No ordinary earning or physical device claim.'),indent=2)+'\n')
