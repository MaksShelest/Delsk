#!/usr/bin/env python3
"""Real pre-iteration binary saves continued by the current binary; no economy study.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,hashlib,json,subprocess,sys,tarfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);p.add_argument('--baseline-ref',default='da213bf')
a=p.parse_args();out=a.output.resolve()
if out.exists() and any(out.iterdir()):p.error('Use a new/empty output')
out.mkdir(parents=True,exist_ok=True);stage=out/'baseline';checks=[]
def check(name,seconds,command):
 subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout',str(seconds),'--log-directory',out/'checks','--',*map(str,command)],cwd=ROOT,check=True);checks.append(name)
ref=subprocess.check_output(['git','rev-parse',a.baseline_ref+'^{commit}'],cwd=ROOT,text=True).strip()
folders=['src/Simulation.Core','src/Simulation.Hosting','src/Agents.Algorithmic','src/Agents.Llm','src/Research.Runner','config','scenarios','global.json','Directory.Build.props','Directory.Build.targets']
check('baseline-archive',30,['git','archive','--format=tar','-o',out/'baseline.tar',ref,*folders])
with tarfile.open(out/'baseline.tar') as archive:archive.extractall(stage,filter='data')
check('baseline-restore',180,['dotnet','restore',stage/'src/Research.Runner/Research.Runner.csproj','--locked-mode','--disable-parallel','-m:1'])
check('baseline-build',180,['dotnet','build',stage/'src/Research.Runner/Research.Runner.csproj','-c','Release','--no-restore','--disable-build-servers','-m:1'])
old=['dotnet',stage/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll'];current=['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll']
options=out/'options.json';options.write_text(json.dumps(dict(npcCount=10,difficulty='Medium',seed=42)))
records=[]
for key in ['small-river','large-districts']:
 save=out/(key+'-pre-iteration.save.json');original=out/(key+'-old');loaded=out/(key+'-current')
 check(key+'-old',120,[*old,'--scenario',stage/'scenarios/vnext'/(key+'.json'),'--economy',stage/'config/vnext/economy.g1-npc.json','--new-game',options,'--script','baseline','--human-ballot','self','--ticks',120,'--checkpoint',save,'--checkpoint-at',60,'--output',original])
 check(key+'-current',120,[*current,'--load',save,'--ticks',120,'--output',loaded])
 check(key+'-exact',30,[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',original,loaded])
 check(key+'-accounting',60,[sys.executable,ROOT/'scripts/verify_results.py',original,loaded])
 config=json.loads((loaded/'scenario.json').read_text());assert config['layout']['version']=='map-adapter-1'
 dealer=next(s for s in config['layout']['civicObjects'] if s['kind']=='dealership');assert len(dealer['body'])==15
 records.append(dict(map=key,checkpointTick=60,finalTick=120,participants=11,layout=config['layout']['version'],dealerBody=15,saveSha256=hashlib.sha256(save.read_bytes()).hexdigest(),exact=True))
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',baselineCommit=ref,cases=records,checks=checks,scope='Actual checkpoint produced by the pre-iteration C# binary with ordinary G1 NPCs; current binary must preserve all exact world/controller/export data. Short compatibility regression, not H1/H2 or balance evidence.'),indent=2)+'\n')
print('Actual pre-iteration saves: two maps, 11 participants, exact continuation and accounting passed')
