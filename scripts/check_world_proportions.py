#!/usr/bin/env python3
"""Native world proportions capture in an isolated ordinary Playfield. Author: Шелестов Максим Евгеньевич."""
import argparse,hashlib,json,os,shutil,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--godot',required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--verify',action='store_true');p.add_argument('--asset-cache',type=Path);p.add_argument('--legacy-save-dir',type=Path);p.add_argument('--fleet-save-dir',type=Path)
p.add_argument('--maps',nargs='+',choices=['small-river','large-districts','compact-valley','first'],default=['small-river','large-districts','first'])
p.add_argument('--motion',action='store_true',help='Capture six authoritative active seconds on the compact map,180 real viewport frames at30fps')
p.add_argument('--save',type=Path,help='One existing saved world on the single selected map')
p.add_argument('--natural-save',action='store_true',help='Label --save as an actual natural ordinary trajectory, not a funded fixture')
a=p.parse_args();out=a.output.resolve()
if a.save and (len(a.maps)!=1 or a.legacy_save_dir or a.fleet_save_dir or a.motion):p.error('--save requires one map and no other snapshot/motion options')
if a.natural_save and not a.save:p.error('--natural-save requires --save')
if out.exists() and any(out.iterdir()):p.error('Use a new/empty output')
out.mkdir(parents=True,exist_ok=True);stage=out/'source';project=stage/'src/Client.Godot'
for folder in ['src','config','scenarios','docs/vnext/maps']:
 shutil.copytree(ROOT/folder,stage/folder,ignore=shutil.ignore_patterns('bin','obj','.godot'))
for name in ['global.json','Directory.Build.props','Directory.Build.targets']:
 shutil.copyfile(ROOT/name,stage/name)
inputs={p.relative_to(stage).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in stage.rglob('*') if p.is_file()}
for name in ['scripts/check_world_proportions.py','tests/Client.Probes/WorldProportionsProbe.cs','tools/AssemblyAudit/Program.cs']:
 inputs[name]=hashlib.sha256((ROOT/name).read_bytes()).hexdigest()
def check(name,seconds,cmd,env=None):
 subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout',str(seconds),'--log-directory',out/'checks','--',*map(str,cmd)],cwd=ROOT,env=env,check=True)
check('ordinary-restore',180,['dotnet','restore',project/'Delsk.Client.csproj','--locked-mode','--disable-parallel','-m:1'])
check('ordinary-build',180,['dotnet','build',project/'Delsk.Client.csproj','--no-restore','--disable-build-servers','-m:1'])
check('ordinary-audit',120,['dotnet','run','--project',ROOT/'tools/AssemblyAudit','-c','Release','--',project/'.godot/mono/temp/bin/Debug'])
name='WorldProportionsProbe';shutil.copyfile(ROOT/'tests/Client.Probes'/(name+'.cs'),project/(name+'.cs'))
(project/(name+'.tscn')).write_text('[gd_scene load_steps=2 format=3]\n[ext_resource type="Script" path="res://'+name+'.cs" id="1"]\n[node name="'+name+'" type="Control"]\nlayout_mode=3\nanchors_preset=15\nanchor_right=1.0\nanchor_bottom=1.0\nscript=ExtResource("1")\n',encoding='utf-8')
check('probe-build',180,['dotnet','build',project/'Delsk.Client.csproj','--no-restore','--disable-build-servers','-m:1'])
cache=a.asset_cache
if cache is None:
 cache=out/'assets';check('asset-stage',30,[sys.executable,ROOT/'scripts/prepare_godot_asset_import.py','--source',project,'--output',cache,'--report',out/'asset-stage.json'])
 check('asset-import',120,[a.godot,'--headless','--path',cache,'--editor','--import','--quit'])
check('asset-reuse',60,[sys.executable,ROOT/'scripts/reuse_godot_imports.py','--source',cache,'--target',project,'--report',out/'asset-reuse.json'])
check('asset-verify',30,[sys.executable,ROOT/'scripts/reuse_godot_imports.py','--source',project,'--verify-only','--report',out/'asset-verify.json'])
base=(project/'project.godot').read_text(encoding='utf-8')
cases=[(m,None,'') for m in a.maps]
if a.save:cases=[(a.maps[0],a.save.resolve(),'-saved')]
if a.legacy_save_dir:cases += [(m,a.legacy_save_dir.resolve()/(m+'-adapter1.save.json'),'-adapter1') for m in ['small-river','large-districts']]
if a.fleet_save_dir:cases += [(m,a.fleet_save_dir.resolve()/(m.replace('-','_')+'-purchased.save.json'),'-purchased') for m in ['small-river','large-districts']]
for map_key,save,suffix in cases:
 tag=map_key+suffix;folder=out/tag;folder.mkdir();(project/'project.godot').write_text(base.replace('[application]','[application]\nconfig/use_custom_user_dir=true\nconfig/custom_user_dir_name="Delsk-proportions-'+out.name+'-'+tag+'"',1),encoding='utf-8')
 check(tag,150,[a.godot,'--path',project,'--rendering-method','gl_compatibility','--audio-driver','Dummy','res://WorldProportionsProbe.tscn','--','--map',map_key,'--output',folder,*(['--verify'] if a.verify else []),*(['--motion'] if a.motion and map_key=='compact-valley' else []),*(['--natural-save'] if a.natural_save else []),*(['--load',save] if save else [])],os.environ|{'XDG_DATA_HOME':str(out/'userdata')})
 check(tag+'-logs',15,[sys.executable,ROOT/'scripts/verify_platform_probe.py','--log-prefix',out/'checks'/tag])
 assert json.loads((folder/'native-check.json').read_text(encoding='utf-8'))['status']=='succeeded'
assert all(hashlib.sha256((ROOT/name).read_bytes()).hexdigest()==digest for name,digest in inputs.items()),'Source changed during native capture'
(out/'source-inputs.json').write_text(json.dumps(inputs,indent=2)+'\n',encoding='utf-8')
print('Native proportions/captures passed:',', '.join(a.maps),'; both skins')
