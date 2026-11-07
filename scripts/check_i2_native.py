#!/usr/bin/env python3
"""I2 native physics/scene/save agreement in an isolated ordinary project.
Author: Шелестов Максим Евгеньевич. Synthetic Linux checks are not device acceptance.
"""
import argparse,hashlib,json,os,shutil,socket,subprocess,sys,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--fixtures',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--godot',required=True);p.add_argument('--display',type=int,default=96);p.add_argument('--xvfb',default='Xvfb');p.add_argument('--case',help='Run one fixture basename during diagnosis');p.add_argument('--reuse-from',type=Path,help='Reuse a source/binary-verified ordinary build; probe is rebuilt and audit repeated');a=p.parse_args()
out=a.output.resolve();stage=out/'source';project=stage/'src/Client.Godot'
if out.exists() and any(out.iterdir()):p.error('Use a new empty output directory; previous attempts are retained')
out.mkdir(parents=True);inputs={}
previous=a.reuse_from.resolve() if a.reuse_from else None
if previous:shutil.copytree(previous/'source',stage)
def check(name,timeout,cmd,env=None):
 subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout',str(timeout),'--log-directory',out/'checks','--',*map(str,cmd)],cwd=ROOT,env=env,check=True)
names=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z'],cwd=ROOT,text=True).split('\0')
for name in sorted(set(names)):
 source=ROOT/name
 if source.is_file() and (name.startswith(('src/','config/','scenarios/','docs/vnext/maps/')) or name in ['Directory.Build.props','Directory.Build.targets','global.json']):
  dest=stage/name;dest.parent.mkdir(parents=True,exist_ok=True);digest=hashlib.sha256(source.read_bytes()).hexdigest()
  if not dest.exists() or hashlib.sha256(dest.read_bytes()).hexdigest()!=digest:shutil.copyfile(source,dest)
  inputs[name]=digest
for name in ['tests/Client.Probes/I2ControlProbe.cs','tools/AssemblyAudit/Program.cs','scripts/check_i2_native.py']:
 inputs[name]=hashlib.sha256((ROOT/name).read_bytes()).hexdigest()
(out/'inputs.json').write_text(json.dumps(inputs,indent=2)+'\n')
runtime_inputs={n:h for n,h in inputs.items() if n.startswith(('src/','config/','scenarios/','docs/vnext/maps/')) or n in ['Directory.Build.props','Directory.Build.targets','global.json']}
ordinary=project/'.godot/mono/temp/bin/Debug'
if previous:
 cached=json.loads((previous/'ordinary-cache.json').read_text())
 assert cached['sources']==runtime_inputs,'Ordinary source changed; use a fresh build'
 assert json.loads((previous/'checks/ordinary-audit.json').read_text())['exit_code']==0
 for name,digest in cached['binaries'].items():assert hashlib.sha256((previous/'ordinary-bin'/name).read_bytes()).hexdigest()==digest,'Changed cached binary'
 for name in ['I2ControlProbe.cs','I2ControlProbe.tscn']:(project/name).unlink(missing_ok=True)
 shutil.rmtree(ordinary);shutil.copytree(previous/'ordinary-bin',ordinary)
 (out/'ordinary-reuse.json').write_text(json.dumps(dict(status='source-and-binary-verified',previous=str(previous),binaries=cached['binaries']),indent=2)+'\n')
else:
 check('restore',180,['dotnet','restore',project/'Delsk.Client.csproj','--locked-mode','--disable-parallel','-m:1'])
 check('ordinary-build',180,['dotnet','build',project/'Delsk.Client.csproj','--no-restore','--disable-build-servers','-m:1'])
check('runner-build',180,['dotnet','build',ROOT/'src/Research.Runner/Research.Runner.csproj','-c','Release','--no-restore','--disable-build-servers','-m:1'])
check('ordinary-audit',120,['dotnet','run','--project',ROOT/'tools/AssemblyAudit','-c','Release','--',project/'.godot/mono/temp/bin/Debug'])
shutil.copytree(ordinary,out/'ordinary-bin')
(out/'ordinary-cache.json').write_text(json.dumps(dict(sources=runtime_inputs,binaries={str(f.relative_to(ordinary)):hashlib.sha256(f.read_bytes()).hexdigest() for f in ordinary.rglob('*') if f.is_file()}),indent=2)+'\n')
assets=out/'assets'
check('asset-stage',30,[sys.executable,ROOT/'scripts/prepare_godot_asset_import.py','--source',project,'--output',assets,'--report',out/'asset-stage.json'])
check('native-import',120,[a.godot,'--headless','--path',assets,'--editor','--import','--quit'])
check('verified-import-reuse',60,[sys.executable,ROOT/'scripts/reuse_godot_imports.py','--source',assets,'--target',project,'--report',out/'asset-reuse.json'])
check('import-completeness',30,[sys.executable,ROOT/'scripts/reuse_godot_imports.py','--source',project,'--verify-only','--report',out/'asset-completeness.json'])
runtime=out/'runtime';env=os.environ|{'XDG_DATA_HOME':str(runtime/'data'),'XDG_CONFIG_HOME':str(runtime/'config'),'XDG_CACHE_HOME':str(runtime/'cache')}
check('platform-smoke',30,[a.godot,'--headless','--path',project,'--audio-driver','Dummy','--','--smoke','--report',out/'platform.json'],env)
check('platform-log',15,[sys.executable,ROOT/'scripts/verify_platform_probe.py','--log-prefix',out/'checks/platform-smoke'])
shutil.copyfile(ROOT/'tests/Client.Probes/I2ControlProbe.cs',project/'I2ControlProbe.cs')
(project/'I2ControlProbe.tscn').write_text('[gd_scene load_steps=2 format=3]\n[ext_resource type="Script" path="res://I2ControlProbe.cs" id="1"]\n[node name="I2ControlProbe" type="Control"]\nlayout_mode=3\nanchors_preset=15\nanchor_right=1.0\nanchor_bottom=1.0\nscript=ExtResource("1")\n')
# Ordinary sources/binaries were built or verified above; only add the isolated probe.
references={name:digest for name,digest in json.loads((out/'ordinary-cache.json').read_text())['binaries'].items() if name.endswith('.dll') and name!='Delsk.Client.dll'}
# A prior isolated build can have re-emitted identical sources under a different
# source path. Restore its copy-local outputs to the verified ordinary references.
for folder in (stage/'src').glob('*/bin/Debug/net10.0'):
 for name in ['Simulation.Core.dll','Simulation.Hosting.dll','Agents.Algorithmic.dll','Agents.Llm.dll']:
  if (folder/name).is_file():shutil.copyfile(ordinary/name,folder/name)
check('isolated-probe-build',180,['dotnet','build',project/'Delsk.Client.csproj','--no-restore','--disable-build-servers','-m:1','-p:BuildProjectReferences=false'])
for name,digest in references.items():assert hashlib.sha256((ordinary/name).read_bytes()).hexdigest()==digest,'Probe changed a frozen runtime reference: '+name
assert all(hashlib.sha256((ROOT/name).read_bytes()).hexdigest()==digest for name,digest in inputs.items()),'Native inputs changed before window launch'
with (out/'xvfb.log').open('w') as log:
 server=subprocess.Popen([a.xvfb,':'+str(a.display),'-screen','0','1024x800x24','-nolisten','unix','-listen','tcp','-ac'],stdout=log,stderr=log)
 try:
  ready=False
  for _ in range(50):
   if server.poll() is not None:raise RuntimeError('Owned Xvfb exited')
   try:
    with socket.create_connection(('127.0.0.1',6000+a.display),timeout=.1):ready=True;break
   except OSError:time.sleep(.1)
  if not ready:raise RuntimeError('Owned Xvfb did not become ready')
  env|={'DISPLAY':'127.0.0.1:'+str(a.display)+'.0','LIBGL_ALWAYS_SOFTWARE':'1','LP_NUM_THREADS':'2'}
  cases=sorted(a.fixtures.resolve().glob('native-*-plan.json'))
  if a.case:cases=[f for f in cases if f.name==a.case+'-plan.json']
  assert cases,'No native fixtures'
  for plan in cases:
   name=plan.name.removesuffix('-plan.json');fixture=a.fixtures.resolve()/name
   # Preserve the complete chain across an exact saved boundary;120s per phase.
   # The retained ordinary platform smoke remains30s.
   for phase in ['road','focus','exit']:
    source_fixture=fixture if phase=='road' else out/name/('road/after-road' if phase=='focus' else 'focus/after-focus')
    target=out/name/phase
    check(name+'-'+phase,120,[a.godot,'--path',project,'--rendering-method','gl_compatibility','--audio-driver','Dummy','--windowed','--resolution','1000x760','res://I2ControlProbe.tscn','--','--touch-controls','--phase',phase,'--fixture',source_fixture,'--output',target],env)
    check(name+'-'+phase+'-log',15,[sys.executable,ROOT/'scripts/verify_platform_probe.py','--log-prefix',out/'checks'/(name+'-'+phase)])
    assert json.loads((target/'native.json').read_text())['status']=='succeeded'
   native=json.loads((out/name/'exit/native.json').read_text());readback=out/name/'readback'
   check(name+'-readback',120,['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll','--load',out/name/'exit/final.save.json','--ticks',native['tick'],'--output',readback])
   assert json.loads((readback/'summary.json').read_text())['stateHash']==native['stateHash'],'Native/runner save readback differs'


 finally:
  server.terminate()
  try:server.wait(timeout=5)
  except subprocess.TimeoutExpired:server.kill();server.wait(timeout=5)
assert all(hashlib.sha256((ROOT/name).read_bytes()).hexdigest()==digest for name,digest in inputs.items()),'Native sources changed during the gate'
(out/'inputs.json').write_text(json.dumps(inputs,indent=2)+'\n')
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',matchedInputs=len(inputs),cases=len(cases),scope='Linux Godot Mono native import, ordinary metadata audit and platform smoke; synthetic WASD/touch, physical Core coordinates, local turns, exit/reentry, focus, save/readback. Explicit prepared funds. No physical Windows/Android/FPS or ordinary earning claim'),indent=2)+'\n')
