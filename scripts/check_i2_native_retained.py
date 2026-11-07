#!/usr/bin/env python3
"""Run more I2 fixtures on a retained, successful native build.
Author: Шелестов Максим Евгеньевич. Every source and managed binary is frozen.
"""
import argparse, hashlib, json, os, shutil, socket, subprocess, sys, time
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--prepared',type=Path,required=True)
p.add_argument('--fixtures',type=Path,required=True)
p.add_argument('--output',type=Path,required=True)
p.add_argument('--case',action='append',required=True)
p.add_argument('--godot',required=True)
p.add_argument('--xvfb',default='Xvfb')
p.add_argument('--display',type=int,default=97)
p.add_argument('--model',choices=['sputnik20','trakt_d1'],default='sputnik20',help='Explicit isolated model diagnostic; gameplay sources and runtime DLLs remain frozen')
a=p.parse_args();prepared=a.prepared.resolve();out=a.output.resolve()
if out.exists() and any(out.iterdir()):p.error('Use a new empty output; previous attempts are retained')
assert json.loads((prepared/'gate.json').read_text())['status']=='succeeded'
assert json.loads((prepared/'checks/ordinary-audit.json').read_text())['exit_code']==0
inputs=json.loads((prepared/'inputs.json').read_text())
original_project=prepared/'source/src/Client.Godot'
project=original_project
binary=project/'.godot/mono/temp/bin/Debug'
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
binaries={str(f.relative_to(binary)):digest(f) for f in binary.rglob('*') if f.is_file()}
original_binaries=binaries.copy();patched_probe=None
helper=digest(Path(__file__))
def verify():
    assert digest(Path(__file__))==helper,'Retained helper changed'
    for name,h in inputs.items():
        assert digest(ROOT/name)==h,'Current source changed: '+name
        staged=original_project/'I2ControlProbe.cs' if name=='tests/Client.Probes/I2ControlProbe.cs' else prepared/'source'/name
        if name.startswith(('src/','config/','scenarios/','docs/vnext/maps/')) or name=='tests/Client.Probes/I2ControlProbe.cs':
            assert digest(staged)==h,'Retained source changed: '+name
        if project!=original_project and name.startswith(('src/','config/','scenarios/','docs/vnext/maps/')):
            assert digest(out/'source'/name)==h,'Cloned gameplay source changed: '+name
    original_binary=original_project/'.godot/mono/temp/bin/Debug'
    assert original_binaries=={str(f.relative_to(original_binary)):digest(f) for f in original_binary.rglob('*') if f.is_file()},'Original prepared binaries changed'
    if patched_probe is not None:assert digest(project/'I2ControlProbe.cs')==patched_probe,'Tracked instrumentation changed'
    assert binaries=={str(f.relative_to(binary)):digest(f) for f in binary.rglob('*') if f.is_file()},'Retained binaries changed'
verify();out.mkdir(parents=True)
def check(name,timeout,command,env=None):
    subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout',str(timeout),'--log-directory',out/'checks','--',*map(str,command)],cwd=ROOT,env=env,check=True)
if a.model=='trakt_d1':
    # Copy the verified stage; change only test purchase and the lower-speed
    # intersection input interval; let the help layout settle before its screenshot.
    # Every real model cap and collision rule stays on.
    shutil.copytree(prepared/'source',out/'source');project=out/'source/src/Client.Godot';binary=project/'.godot/mono/temp/bin/Debug'
    probe=project/'I2ControlProbe.cs';source=probe.read_text()
    for before,after in [('v.ModelId=="sputnik20"','v.ModelId=="trakt_d1"'),('await Quanta(9);await KeyEvent(crossKey,false);','await Quanta(20);await KeyEvent(crossKey,false);'),('Call(_app,"OpenHelp","basics");await Frames(2);','Call(_app,"OpenHelp","basics");await Frames(8);')]:
        assert source.count(before)==1,'Tracked probe patch no longer matches'
        source=source.replace(before,after)
    marker='Check(text.Contains(language=='
    assert source.count(marker)==1
    layout='''if(language=="en")foreach(var label in Descendants(help).OfType<Label>().Where(l=>l.Text.StartsWith("1.")))
                    System.IO.File.AppendAllText(_output+"/help-layout.jsonl",JsonSerializer.Serialize(new{prefix=label.Text[..Math.Min(30,label.Text.Length)],x=label.GlobalPosition.X,y=label.GlobalPosition.Y,width=label.Size.X,height=label.Size.Y,alignment=label.HorizontalAlignment.ToString(),characters=Enumerable.Range(0,Math.Min(7,label.Text.Length)).Select(i=>new{index=i,bounds=label.GetCharacterBounds(i).ToString()})})+"\\n");
                '''
    source=source.replace(marker,layout+marker)
    marker='var ends=from c in road from outward in axes'
    assert source.count(marker)==1
    settle='''Check(_session.Engine.TravelOf(new(1)).HeadingX==0&&_session.Engine.TravelOf(new(1)).HeadingZ==0,"loaded input remains neutral");
            int settleBudget=40;
            while((Traffic.Progress>0||Traffic.ProgressFraction>0||Traffic.Steering is {TurnQuanta:>0})&&settleBudget-->0)await Quanta(1);
            Check(settleBudget>0,"saved committed edge safely settles before a new route");
            '''
    source=source.replace(marker,settle+marker)
    probe.write_text(source);patched_probe=digest(probe)
    check('tracked-probe-build',180,['dotnet','build',project/'Delsk.Client.csproj','--no-restore','--disable-build-servers','-m:1','-p:BuildProjectReferences=false'])
    for name,h in original_binaries.items():
        if name.endswith('.dll') and name!='Delsk.Client.dll':assert digest(binary/name)==h,'Tracked probe changed a gameplay reference: '+name
    binaries={str(f.relative_to(binary)):digest(f) for f in binary.rglob('*') if f.is_file()}
verify()
(out/'frozen.json').write_text(json.dumps(dict(prepared=str(prepared),inputs=inputs,originalBinaries=original_binaries,binaries=binaries,model=a.model,patchedProbeSha256=patched_probe,helperSha256=helper),indent=2)+'\n')
with (out/'xvfb.log').open('w') as log:
    server=subprocess.Popen([a.xvfb,':'+str(a.display),'-screen','0','1024x800x24','-nolisten','unix','-listen','tcp','-ac'],stdout=log,stderr=log)
    try:
        ready=False
        for _ in range(50):
            if server.poll() is not None:raise RuntimeError('Owned Xvfb exited')
            try:
                with socket.create_connection(('127.0.0.1',6000+a.display),timeout=.1):ready=True;break
            except OSError:time.sleep(.1)
        assert ready,'Owned Xvfb did not become ready'
        for name in a.case:
            assert name.startswith('native-') and all(c.isalnum() or c in '-_' for c in name)
            assert (a.fixtures/(name+'-plan.json')).is_file()
            for phase in ['road','focus','exit']:
                runtime=out/name/phase/'runtime'
                env=os.environ|{'DISPLAY':'127.0.0.1:'+str(a.display)+'.0','LIBGL_ALWAYS_SOFTWARE':'1','LP_NUM_THREADS':'2','XDG_DATA_HOME':str(runtime/'data'),'XDG_CONFIG_HOME':str(runtime/'config'),'XDG_CACHE_HOME':str(runtime/'cache')}
                source_fixture=a.fixtures.resolve()/name if phase=='road' else out/name/('road/after-road' if phase=='focus' else 'focus/after-focus')
                check(name+'-'+phase,120,[a.godot,'--path',project,'--rendering-method','gl_compatibility','--audio-driver','Dummy','--windowed','--resolution','1000x760','res://I2ControlProbe.tscn','--','--touch-controls','--phase',phase,'--fixture',source_fixture,'--output',out/name/phase],env)
                check(name+'-'+phase+'-log',15,[sys.executable,ROOT/'scripts/verify_platform_probe.py','--log-prefix',out/'checks'/(name+'-'+phase)])
                assert json.loads((out/name/phase/'native.json').read_text())['status']=='succeeded'

            native=json.loads((out/name/'exit/native.json').read_text());readback=out/name/'readback'
            check(name+'-readback',120,['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll','--load',out/name/'exit/final.save.json','--ticks',native['tick'],'--output',readback])
            assert json.loads((readback/'summary.json').read_text())['stateHash']==native['stateHash'],'Native/runner save readback differs'
            verify()
    finally:
        server.terminate()
        try:server.wait(timeout=5)
        except subprocess.TimeoutExpired:server.kill();server.wait(timeout=5)
verify()
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',cases=a.case,model=a.model,prepared=str(prepared),sources=len(inputs),binaries=len(binaries),scope='Verified native gameplay sources and references; explicit isolated model instrumentation, fresh user data for every fixture. Synthetic Linux input; no physical device or natural earning claim.'),indent=2)+'\n')
