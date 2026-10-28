#!/usr/bin/env python3
"""G2 native city-map/UX checks in an owned isolated ordinary stage.
Author: Шелестов Максим Евгеньевич. Injected input is not device acceptance.
"""
import argparse,hashlib,json,os,shutil,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--godot',required=True);p.add_argument('--output',required=True,type=Path)
p.add_argument('--case',choices=['all','small','large','narrow','forms'],default='all');p.add_argument('--fixtures',type=Path);p.add_argument('--display');p.add_argument('--asset-cache',type=Path);p.add_argument('--reuse-stage',action='store_true');p.add_argument('--attempt',type=int,default=1)
a=p.parse_args();out=a.output.resolve();stage=out/'source';project=stage/'src/Client.Godot';marker=stage/'.delsk-city-ux-stage';prefix='a'+str(a.attempt)
if a.attempt<1:p.error('Use a positive attempt number')
if a.case=='forms' and a.fixtures is None:p.error('--case forms requires --fixtures')
if a.reuse_stage and not marker.is_file():p.error('Refusing an unowned stage')
if not a.reuse_stage and out.exists() and any(out.iterdir()):p.error('Use an empty output or its owned --reuse-stage')
if (out/'checks'/(prefix+'-restore.json')).exists():p.error('Use a new attempt number; previous diagnostics are retained')
out.mkdir(parents=True,exist_ok=True);case_root=out/('cases-'+prefix);case_root.mkdir()
def check(name,timeout,cmd,env=None):
    subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',prefix+'-'+name,'--timeout',str(timeout),'--log-directory',out/'checks','--',*map(str,cmd)],cwd=ROOT,env=env,check=True)
names=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z'],cwd=ROOT,text=True).split('\0');inputs={}
for name in sorted(set(names)):
    source=ROOT/name
    if source.is_file() and (name.startswith(('src/','config/','scenarios/','docs/vnext/')) or name in ['Directory.Build.props','Directory.Build.targets','global.json']):
        dest=stage/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,dest);
        if not name.startswith('docs/vnext/') or name=='docs/vnext/data/BALANCE_VNEXT.json':inputs[name]=hashlib.sha256(source.read_bytes()).hexdigest()
for name in ['tests/Client.Probes/CityExperienceProbe.cs','tests/Client.Probes/CityExperienceProbe.Forms.cs','tests/Client.Probes/ServiceRunProbe.cs','tools/AssemblyAudit/Program.cs','scripts/check_city_experience_ui.py']:
    if (ROOT/name).exists():inputs[name]=hashlib.sha256((ROOT/name).read_bytes()).hexdigest()
marker.write_text('Owned isolated G2 ordinary UI stage\n')
for name in ['CityExperienceProbe.cs','CityExperienceProbe.Forms.cs','CityExperienceProbe.tscn','ServiceRunProbe.cs','ServiceRunProbe.tscn']:
    (project/name).unlink(missing_ok=True)
# Existing measured import workaround belongs only to the disposable stage.
with (project/'project.godot').open('a') as f:f.write('\n[editor]\nimport/use_multiple_threads=false\n')
check('restore',180,['dotnet','restore',project/'Delsk.Client.csproj','--locked-mode','--disable-parallel','-m:1'])
check('ordinary-build',180,['dotnet','build',project/'Delsk.Client.csproj','--no-restore','--disable-build-servers','-m:1'])
check('ordinary-audit',120,['dotnet','run','--project',ROOT/'tools/AssemblyAudit','-c','Release','--',project/'.godot/mono/temp/bin/Debug'])
def probe(name):
    shutil.copyfile(ROOT/'tests/Client.Probes'/(name+'.cs'),project/(name+'.cs'))
    (project/(name+'.tscn')).write_text('[gd_scene load_steps=2 format=3]\n[ext_resource type="Script" path="res://'+name+'.cs" id="1"]\n[node name="'+name+'" type="Control"]\nlayout_mode=3\nanchors_preset=15\nanchor_right=1.0\nanchor_bottom=1.0\nscript=ExtResource("1")\n')
probe('CityExperienceProbe')
if a.fixtures:probe('ServiceRunProbe')
if (ROOT/'tests/Client.Probes/CityExperienceProbe.Forms.cs').exists():shutil.copyfile(ROOT/'tests/Client.Probes/CityExperienceProbe.Forms.cs',project/'CityExperienceProbe.Forms.cs')
check('probe-build',180,['dotnet','build',project/'Delsk.Client.csproj','--no-restore','--disable-build-servers','-m:1'])
if a.asset_cache:check('asset-cache',60,[sys.executable,ROOT/'scripts/reuse_godot_imports.py','--source',a.asset_cache,'--target',project,'--report',out/(prefix+'-asset-cache.json')])
check('import',120,[a.godot,'--headless','--path',project,'--editor','--import'])
env=os.environ.copy()
if a.display:env.update(DISPLAY=a.display,LIBGL_ALWAYS_SOFTWARE='1')
cases=[]
base_project=(project/'project.godot').read_text()
for map_key,count,language,width in [('small-river',6,'ru',640),('large-districts',10,'en',1100),('large-districts',10,'ru',360)]:
    tag='small' if map_key=='small-river' else 'large' if width>500 else 'narrow'
    if a.case not in ('all',tag):continue
    name=map_key+'-'+str(count)+'-'+language+'-'+str(width);folder=case_root/name;folder.mkdir()
    identity=hashlib.sha256(str(folder).encode()).hexdigest()[:16]
    (project/'project.godot').write_text(base_project.replace('[application]','[application]\nconfig/use_custom_user_dir=true\nconfig/custom_user_dir_name="Delsk-G2-'+identity+'"',1))
    check(name,180,[a.godot,'--path',project,'--rendering-method','gl_compatibility','--audio-driver','Dummy','res://CityExperienceProbe.tscn','--','--output',folder,'--map',map_key,'--count',count,'--language',language,'--width',width,'--windowed'],env|{'XDG_DATA_HOME':str(out/('user-'+prefix+'-'+name))})
    check(name+'-logs',15,[sys.executable,ROOT/'scripts/verify_platform_probe.py','--log-prefix',out/'checks'/(prefix+'-'+name)])
    result=json.loads((folder/'native-check.json').read_text());assert result['status']=='succeeded';cases.append(result)
if a.fixtures:
    fixtures=a.fixtures.resolve()
    for language in ['ru','en']:
        name='forms-'+language;folder=case_root/name;folder.mkdir();identity=hashlib.sha256(str(folder).encode()).hexdigest()[:16]
        (project/'project.godot').write_text(base_project.replace('[application]','[application]\nconfig/use_custom_user_dir=true\nconfig/custom_user_dir_name="Delsk-G2-'+identity+'"',1))
        check(name,180,[a.godot,'--path',project,'--rendering-method','gl_compatibility','--audio-driver','Dummy','res://CityExperienceProbe.tscn','--','--output',folder,'--map','prepared','--count',2,'--language',language,'--width',800,'--fixtures',fixtures,'--windowed'],env|{'XDG_DATA_HOME':str(out/('user-'+prefix+'-'+name))})
        check(name+'-logs',15,[sys.executable,ROOT/'scripts/verify_platform_probe.py','--log-prefix',out/'checks'/(prefix+'-'+name)])
        result=json.loads((folder/'native-check.json').read_text());assert result['status']=='succeeded' and result['prepared'];cases.append(result)
    folder=case_root/'service-runs';folder.mkdir()
    check('service-runs',180,[a.godot,'--path',project,'--rendering-method','gl_compatibility','--audio-driver','Dummy','res://ServiceRunProbe.tscn','--','--output',folder,'--fixtures',fixtures,'--windowed'],env|{'XDG_DATA_HOME':str(out/('user-'+prefix+'-service-runs'))})
    check('service-runs-logs',15,[sys.executable,ROOT/'scripts/verify_platform_probe.py','--log-prefix',out/'checks'/(prefix+'-service-runs')])
    cases.append(dict(status='succeeded',prepared=True,scope='Existing F2 native delivery execution/partial payment/cancellation/restore probe on the same final client source, RU/EN'))
assert all(hashlib.sha256((ROOT/name).read_bytes()).hexdigest()==digest for name,digest in inputs.items()),'Sources changed during the native gate'
(out/(prefix+'-inputs.json')).write_text(json.dumps(dict(inputs=inputs,stageOnlyChanges=['serial editor import','isolated forbidden probe','isolated per-case user data directory'],scope='Exact source digests; asset cache verified separately'),indent=2)+'\n')
(out/(prefix+'-gate.json')).write_text(json.dumps(dict(status='succeeded',requestedCases=a.case,cases=cases,matchedInputs=len(inputs),scope='Ordinary native window with synthetic keyboard/mouse/touch input; no physical Android/device assertion'),ensure_ascii=False,indent=2)+'\n')
print('G2 native city experience passed:',len(cases),'cases')
