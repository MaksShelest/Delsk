#!/usr/bin/env python3
"""Isolated H3 test packages; pinned tools/templates must already be installed.
Author: Шелестов Максим Евгеньевич. No probes or private signing keys are shipped.
"""
import argparse,hashlib,json,os,shutil,subprocess,sys,urllib.parse,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--godot',required=True);p.add_argument('--platform',choices=['windows','android'],required=True)
p.add_argument('--output',type=Path,required=True);p.add_argument('--asset-cache',type=Path)
p.add_argument('--android-sdk',type=Path);p.add_argument('--java-sdk',type=Path)
a=p.parse_args();out=a.output.resolve();stage=out/'source';project=stage/'src/Client.Godot'
if out.exists() and any(out.iterdir()):p.error('Use a new output; retain failed attempts')
if a.platform=='android' and (not a.android_sdk or not a.java_sdk):p.error('Android requires explicit SDK and JDK paths')
out.mkdir(parents=True,exist_ok=True)
files=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z'],cwd=ROOT,text=True).split('\0')
inputs={}
for name in sorted(set(files)):
 source=ROOT/name
 if source.is_file() and (name.startswith(('src/','config/','scenarios/')) or name in ['Directory.Build.props','Directory.Build.targets','global.json']):
  target=stage/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,target);inputs[name]=hashlib.sha256(source.read_bytes()).hexdigest()
(out/'source-inputs.json').write_text(json.dumps(inputs,indent=2)+'\n')
env=os.environ.copy()
def check(name,limit,command):
 subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout',str(limit),'--log-directory',out/'checks','--',*map(str,command)],cwd=ROOT,env=env,check=True)
def logs(name):
 check(name+'-logs',15,[sys.executable,ROOT/'scripts/verify_platform_probe.py','--log-prefix',out/'checks'/name])
cache=a.asset_cache
if cache is None:
 cache=out/'assets';check('assets-stage',30,[sys.executable,ROOT/'scripts/prepare_godot_asset_import.py','--source',project,'--output',cache,'--report',out/'assets-stage.json'])
 check('assets-import',120,[a.godot,'--headless','--path',cache,'--editor','--import','--quit']);logs('assets-import')
check('assets-reuse',60,[sys.executable,ROOT/'scripts/reuse_godot_imports.py','--source',cache,'--target',project,'--report',out/'assets-reuse.json'])
check('assets-verify',30,[sys.executable,ROOT/'scripts/reuse_godot_imports.py','--source',project,'--verify-only','--report',out/'assets-verify.json'])
for config in ['Debug','ExportRelease']:
 check('restore-'+config,180,['dotnet','restore',project/'Delsk.Client.csproj','-p:Configuration='+config,'--locked-mode','--disable-parallel','-m:1'])
 check('build-'+config,180,['dotnet','build',project/'Delsk.Client.csproj','-c',config,'--no-restore','--disable-build-servers','-m:1'])
 check('audit-'+config,120,['dotnet','run','--project',ROOT/'tools/AssemblyAudit','-c','Release','--',project/'.godot/mono/temp/bin'/config,*(['--no-debug-ui'] if config=='ExportRelease' else [])])
presets=(project/'export_presets.cfg').read_text()
if a.platform=='android':
 sdk=a.android_sdk.resolve();jdk=a.java_sdk.resolve();env.update(JAVA_HOME=str(jdk),ANDROID_HOME=str(sdk),ANDROID_USER_HOME=str(out/'android-user'),GRADLE_USER_HOME=env.get('GRADLE_USER_HOME',str(out/'gradle')))
 gradle=Path(env['GRADLE_USER_HOME']);gradle.mkdir(parents=True,exist_ok=True)
 proxy=urllib.parse.urlsplit(env.get('HTTPS_PROXY',''))
 if proxy.hostname:
  text='org.gradle.daemon=false\norg.gradle.workers.max=2\norg.gradle.jvmargs=-Xmx1536m\n'
  for protocol in ['http','https']:
   text+=f'systemProp.{protocol}.proxyHost={proxy.hostname}\nsystemProp.{protocol}.proxyPort={proxy.port or 8080}\n'
   if proxy.username:text+=f'systemProp.{protocol}.proxyUser={urllib.parse.unquote(proxy.username)}\nsystemProp.{protocol}.proxyPassword={urllib.parse.unquote(proxy.password or '')}\n'
  (gradle/'gradle.properties').write_text(text)
 settings=out/'settings/godot';settings.mkdir(parents=True);env['XDG_CONFIG_HOME']=str(settings.parent)
 key=out/'test.keystore';keytool=jdk/'bin'/('keytool.exe' if os.name=='nt' else 'keytool')
 check('test-signing-key',30,[keytool,'-genkeypair','-keystore',key,'-alias','androiddebugkey','-storepass','android','-keypass','android','-dname','CN=Delsk H3 test, O=Local test, C=US','-keyalg','RSA','-keysize','2048','-validity','3650'])
 (settings/'editor_settings-4.7.tres').write_text('[gd_resource type="EditorSettings" format=3]\n[resource]\n'+''.join(f'export/android/{k}={json.dumps(str(v))}\n' for k,v in {'android_sdk_path':sdk,'java_sdk_path':jdk,'debug_keystore':key,'debug_keystore_user':'androiddebugkey','debug_keystore_pass':'android'}.items()))
 presets=presets.replace('package/signed=true','package/signed=true\nkeystore/release='+json.dumps(str(key))+'\nkeystore/release_user="androiddebugkey"\nkeystore/release_password="android"')
results=[]
for flavor,mode in [('developer','debug'),('release','release')]:
 folder=out/flavor;folder.mkdir();target=folder/('DelskProbe.exe' if a.platform=='windows' else 'DelskH3.apk')
 current=presets.replace('com.maksshelest.delsk.probe','com.maksshelest.delsk.h3.'+flavor).replace('Delsk Platform Probe','Delsk H3 '+flavor)
 (project/'export_presets.cfg').write_text(current)
 command=[a.godot,'--headless','--path',project,*(['--install-android-build-template'] if a.platform=='android' else []),'--export-'+mode,'Windows Probe' if a.platform=='windows' else 'Android Probe',target]
 check('export-'+flavor,240,command);logs('export-'+flavor)
 if not target.is_file() or not target.stat().st_size:raise ValueError('No target package')
 if a.platform=='windows':
  check('bundle-'+flavor,30,[sys.executable,ROOT/'scripts/verify_platform_probe.py','--windows-directory',folder])
  data=folder/'data_Delsk.Client_windows_x86_64'
  if flavor=='release':check('published-release-audit',120,['dotnet','run','--project',ROOT/'tools/AssemblyAudit','-c','Release','--',data,'--no-debug-ui'])
  elif not (data/'Simulation.Developer.dll').is_file():raise ValueError('Standard ExportDebug lost Developer')
 else:
  with zipfile.ZipFile(target) as z:
   if z.testzip() is not None:raise ValueError('Corrupt APK')
   names=z.namelist()
   if 'lib/arm64-v8a/libgodot_android.so' not in names:raise ValueError('Missing arm64 Godot runtime')
   if not any(n.endswith('Delsk.Client.dll') for n in names):raise ValueError('Missing managed client')
   if any(n.endswith('Simulation.Developer.dll') for n in names)!=(flavor=='developer'):raise ValueError('APK Developer composition mismatch')
 results.append(dict(flavor=flavor,package=target.relative_to(out).as_posix(),sha256=hashlib.sha256(target.read_bytes()).hexdigest(),bytes=target.stat().st_size))
(out/'packages.json').write_text(json.dumps(dict(status='succeeded',platform=a.platform,packages=results,sourceCommit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),tools={'dotnet':'10.0.401','godot':'4.7.2.stable.mono'},scope='Test-signed cross-platform packages; no hardware execution or real model inference implied. Code is an isolated copy; no test probes.'),indent=2)+'\n')
print('H3 target packages passed:',a.platform)
