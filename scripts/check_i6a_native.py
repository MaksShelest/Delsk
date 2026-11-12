#!/usr/bin/env python3
"""One scoped native I6-A Revision 2 run: Small valley, 3 NPCs, RU/EN, 1100x720/960x440. No economy study.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,hashlib,json,os,shutil,socket,subprocess,sys,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);p.add_argument('--godot',required=True);p.add_argument('--xorg',default='/usr/lib/xorg/Xorg');p.add_argument('--prepared',type=Path,help='Reuse prior ordinary references and imported assets; only UI sources may differ');p.add_argument('--probe-only',action='store_true',help='Recompile only the isolated probe when all game sources match the prepared run');p.add_argument('--build-only',action='store_true');p.add_argument('--import-resources',action='store_true');p.add_argument('--probe-source',type=Path,default=ROOT/'tests/Client.Probes/I6ATabletProbe.cs');p.add_argument('--fixture',type=Path,help='Reuse one existing ordinary prepared world');p.add_argument('--case',default='all');a=p.parse_args()
if a.probe_source.name=='I6ATabletProbe.cs' and a.case not in ['all','layout','production','secondary','remaining','demolition','remote']:p.error('Unknown I6-A phase')
probe_name=a.probe_source.stem
if probe_name not in ['I6ATabletProbe','I6BUnifiedProbe','I6C11Probe','I6C2Probe','I6C21Probe','I6C22Probe','I6C23Probe','I6C24Probe','I6C25Probe','I6C3Probe','I6C4Probe','I6C5Probe','I6C6Probe','I6C7Probe','I6C73Probe','I6C74Probe','I6C75Probe','I6C8AProbe','I6C8BProbe','I6C8CProbe','CheckpointRecoveryProbe']:p.error('Unsupported isolated I6 probe')
out=a.output.resolve();stage=out/'source';project=stage/'src/Client.Godot'
if out.exists() and any(out.iterdir()):p.error('Use an empty output; retain earlier failures')
out.mkdir(parents=True);inputs={};client_changed=False
if a.probe_only and not a.prepared:p.error('--probe-only requires --prepared')
if a.prepared:
    shutil.copytree(a.prepared.resolve()/'source',stage)
    cache=a.prepared.resolve()/'runtime/cache'
    if cache.exists():shutil.copytree(cache,out/'runtime/cache')
    manifest=a.prepared.resolve()/'inputs.json'
    prior=json.loads(manifest.read_text()) if manifest.exists() else {'src/Client.Godot/'+f.relative_to(project).as_posix():None for f in project.rglob('*') if f.is_file() and not set(f.relative_to(project).parts)&{'.godot','obj','bin'}}
    for name in prior:
        if name.startswith('src/Client.Godot/') and not (ROOT/name).exists():(stage/name).unlink(missing_ok=True)
    for name in ['I5ContextProbe','I6ATabletProbe','I6BUnifiedProbe','I6C11Probe','I6C2Probe','I6C21Probe','I6C22Probe','I6C23Probe','I6C24Probe','I6C25Probe','I6C3Probe','I6C4Probe','I6C5Probe','I6C6Probe','I6C7Probe','I6C73Probe','I6C74Probe','I6C75Probe','I6C8AProbe','I6C8BProbe','I6C8CProbe','CheckpointRecoveryProbe']:
        for suffix in ['.cs','.tscn']:(project/(name+suffix)).unlink(missing_ok=True)
for name in subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z'],cwd=ROOT,text=True).split('\0'):
    source=ROOT/name
    if source.is_file() and (name.startswith(('src/','config/','scenarios/','docs/vnext/maps/')) or name in ['Directory.Build.props','Directory.Build.targets','global.json']):
        target=stage/name;target.parent.mkdir(parents=True,exist_ok=True)
        if a.prepared and (not target.is_file() or target.read_bytes()!=source.read_bytes()):
            assert name.startswith('src/Client.Godot/') or (probe_name=='I6C2Probe' and name in ['scenarios/vnext/compact-valley-i6c2.json','scenarios/vnext/woodland-region-i6c2.json']),'Prepared world/runtime inputs differ: '+name
            client_changed=True
        shutil.copyfile(source,target);inputs[name]=hashlib.sha256(source.read_bytes()).hexdigest()
(out/'inputs.json').write_text(json.dumps(inputs,indent=2)+'\n')
def check(name,timeout,command,env=None):
    subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout',str(timeout),'--log-directory',out/'checks','--',*map(str,command)],cwd=ROOT,env=env,check=True)
if not a.prepared:check('restore',180,['dotnet','restore',project/'Delsk.Client.csproj','--locked-mode','--disable-parallel','-m:1'])
if a.probe_only:assert not client_changed,'Probe-only run may not change game sources'
if not a.probe_only:check('ordinary-build',180,['dotnet','build',project/'Delsk.Client.csproj','--no-restore','--disable-build-servers','-m:1',*(['-p:BuildProjectReferences=false'] if a.prepared else [])])
ordinary=project/'.godot/mono/temp/bin/Debug'
if not a.probe_only:check('ordinary-audit',120,['dotnet','run','--project',ROOT/'tools/AssemblyAudit','-c','Release','--',ordinary])
if a.probe_only:
    receipt=a.prepared.resolve()/'checks/ordinary-audit.json'
    assert json.loads(receipt.read_text())['exit_code']==0,'Prepared ordinary audit must pass'
    (out/'checks/ordinary-audit.json').parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(receipt,out/'checks/ordinary-audit.json')
    (out/'checks/ordinary-reuse.json').write_text(json.dumps({'status':'reused','from':str(a.prepared.resolve()),'gameSourcesUnchanged':True},indent=2)+'\n')
references={f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in ordinary.glob('*.dll') if f.name!='Delsk.Client.dll'}
if a.build_only:
    assert all(hashlib.sha256((ROOT/name).read_bytes()).hexdigest()==h for name,h in inputs.items()),'Source changed during build'
    (out/'gate.json').write_text(json.dumps({'status':'succeeded','selection':'ordinary-build-only','scope':'Client build and ordinary assembly audit only; no native acceptance claimed'},indent=2)+'\n')
    sys.exit(0)
assets=out/'assets'
if not a.prepared or a.import_resources:check('assets',30,[sys.executable,ROOT/'scripts/prepare_godot_asset_import.py','--source',project,'--output',assets,'--report',out/'assets.json'])
if not a.prepared or a.import_resources:check('import',120,[a.godot,'--headless','--path',assets,'--editor','--import','--quit'])
if not a.prepared or a.import_resources:check('imports-reuse',60,[sys.executable,ROOT/'scripts/reuse_godot_imports.py','--source',assets,'--target',project,'--report',out/'import-reuse.json'])
probe=a.probe_source.resolve();shutil.copyfile(probe,project/probe.name);inputs[str(probe.relative_to(ROOT))]=hashlib.sha256(probe.read_bytes()).hexdigest()
(out/'inputs.json').write_text(json.dumps(inputs,indent=2)+'\n')
(project/(probe_name+'.tscn')).write_text('[gd_scene load_steps=2 format=3]\n[ext_resource type="Script" path="res://I6ATabletProbe.cs" id="1"]\n[node name="I6ATabletProbe" type="Control"]\nlayout_mode=3\nanchors_preset=15\nanchor_right=1.0\nanchor_bottom=1.0\nscript=ExtResource("1")\n'.replace('I6ATabletProbe',probe_name))
check('probe-build',180,['dotnet','build',project/'Delsk.Client.csproj','--no-restore','--disable-build-servers','-m:1','-p:BuildProjectReferences=false'])
assert all(hashlib.sha256((ordinary/name).read_bytes()).hexdigest()==h for name,h in references.items()),'Probe modified ordinary references'
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
            if server.poll() is not None:raise RuntimeError('Owned Xorg exited; see log')
            try:
                with socket.create_connection(('127.0.0.1',6094),timeout=.1):ready=True;break
            except OSError:time.sleep(.1)
        assert ready,'Owned display not ready'
        env=os.environ|{'DISPLAY':'127.0.0.1:94.0','LIBGL_ALWAYS_SOFTWARE':'1','LP_NUM_THREADS':'2','XDG_DATA_HOME':str(out/'runtime/data'),'XDG_CONFIG_HOME':str(out/'runtime/config'),'XDG_CACHE_HOME':str(out/'runtime/cache')}
        check('native',600 if probe_name in ('I6BUnifiedProbe','I6C7Probe','I6C73Probe','I6C74Probe','I6C75Probe','I6C8AProbe') else 300 if probe_name in ('I6C25Probe','I6C3Probe','I6C4Probe','I6C5Probe','I6C6Probe') else 180,[a.godot,'--path',project,'--rendering-method','gl_compatibility','--audio-driver','Dummy','--windowed','--resolution','1100x720','res://'+probe_name+'.tscn','--','--repository',ROOT,'--output',out/'native','--case',a.case,*(['--touch-controls'] if a.case=='hud' else []),*(['--fixture',a.fixture.resolve()] if a.fixture else [])],env)
        check('native-log',15,[sys.executable,ROOT/'scripts/verify_platform_probe.py','--log-prefix',out/'checks/native'])
        assert json.loads((out/'native/native.json').read_text())['status']=='succeeded'
    finally:
        server.terminate()
        try:server.wait(timeout=5)
        except subprocess.TimeoutExpired:server.kill();server.wait(timeout=5)
assert all(hashlib.sha256((ROOT/name).read_bytes()).hexdigest()==h for name,h in inputs.items()),'Source changed during run'
(out/'inputs.json').write_text(json.dumps(inputs,indent=2)+'\n')
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',selection=a.case,scope='Selected '+probe_name+' phase only: '+a.case+'; prepared Small valley / three ordinary NPCs. Software GL and synthetic input.'),indent=2)+'\n')
