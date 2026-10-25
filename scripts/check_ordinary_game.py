#!/usr/bin/env python3
"""Actual ordinary setup/roster/save/replay and bounded profiling; no alternative simulation.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True)
p.add_argument('--scenario',type=Path,default=ROOT/'scenarios/presentation-map-one-v3.json');p.add_argument('--economy',type=Path,default=ROOT/'config/economy.json');p.add_argument('--ticks',type=int,default=180);p.add_argument('--voting-check',action='store_true');a=p.parse_args();out=a.output.resolve()
if out.exists() and (not out.is_dir() or any(out.iterdir())):p.error('Use new/empty output')
out.mkdir(parents=True,exist_ok=True);runner=['dotnet',str(ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll')];checks=[]
def check(name,cmd,timeout=30,expected=0):
 r=subprocess.run([sys.executable,str(ROOT/'scripts/run_check.py'),'--name',name,'--timeout',str(timeout),'--log-directory',str(out/'checks'),'--',*map(str,cmd)],cwd=ROOT,check=False)
 assert r.returncode==expected,(name,r.returncode,expected);checks.append(dict(name=name,exitCode=r.returncode))
for count in [3,6,10]:
 options=out/f'options-{count}.json';options.write_text(json.dumps(dict(npcCount=count,difficulty='Medium',seed=42)))
 common=[*runner,'--scenario',a.scenario,'--economy',a.economy,'--new-game',options,'--script','baseline','--human-ballot','self','--ticks',str(a.ticks)]
 folder=out/f'npc-{count}';save=out/f'mid-{count}.save.json'
 check(f'run-{count}',[*common,'--output',folder],300)
 manifest=json.loads((folder/'manifest.json').read_text());state=json.loads((folder/'final-state.json').read_text());assert manifest['participantCount']==count+1 and manifest['npcCount']==count
 assert len(state['participants'])==count+1
 decisions=[json.loads(line) for line in (folder/'decisions.jsonl').read_text().splitlines()]
 assert {d['actor']['value'] if isinstance(d['actor'],dict) else d['actor'] for d in decisions} >= set(range(2,count+2))
 commands=[json.loads(line) for line in (folder/'commands.jsonl').read_text().splitlines()]
 assert {c['command']['actorId']['value'] if isinstance(c['command']['actorId'],dict) else c['command']['actorId'] for c in commands} >= set(range(2,count+2)),(count,'missing NPC actions')
 check(f'save-{count}',[*common,'--output',out/f'saved-{count}','--checkpoint',save,'--checkpoint-at','23'],300)
 check(f'load-{count}',[*runner,'--load',save,'--ticks',a.ticks,'--output',out/f'loaded-{count}'],300)
 check(f'equal-{count}',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',folder,out/f'saved-{count}',folder,out/f'loaded-{count}'])
 check(f'replay-{count}',[*runner,'--script','baseline','--scenario',folder/'scenario.json','--replay',folder/'commands.jsonl','--ticks',a.ticks,'--output',out/f'replayed-{count}'],300)
 check(f'replay-equal-{count}',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py','--world-only',folder,out/f'replayed-{count}'])
 check(f'balances-{count}',[sys.executable,ROOT/'scripts/verify_results.py',folder,out/f'loaded-{count}',out/f'replayed-{count}'])
 if count==10:
  check('profile-10',[*common,'--profile','true','--output',out/'profile-10'],300)
  profile=json.loads((out/'profile-10/profile.json').read_text());assert profile['checkpointHashMatches'] and profile['measuredTicks']==a.ticks
if a.voting_check:
 options=out/'options-10.json';common=[*runner,'--scenario',a.scenario,'--economy',a.economy,'--new-game',options,'--script','baseline','--human-ballot','self','--ticks','602']
 check('voting-save',[*common,'--checkpoint',out/'before-vote.save.json','--checkpoint-at','599','--output',out/'vote'],300)
 check('voting-load',[*runner,'--load',out/'before-vote.save.json','--ticks','602','--output',out/'vote-loaded'],300)
 check('voting-equal',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',out/'vote',out/'vote-loaded'])
# Real CLI rejection must happen before any result series starts.
for i,value in enumerate([2,11,3.5,None,'3',True]):
 options=out/f'bad-{i}.json';options.write_text(json.dumps(dict(npcCount=value)))
 target=out/f'bad-output-{i}';check(f'reject-{i}',[*runner,'--scenario',a.scenario,'--economy',a.economy,'--new-game',options,'--script','baseline','--output',target],expected=1);assert not target.exists()
for flag,value in [('--load','absent'),('--replay','absent'),('--seed','1'),('--research-setup','absent')]:
 check('mixed-'+flag[2:],[*runner,'--scenario',a.scenario,'--economy',a.economy,'--new-game',out/'options-10.json','--script','baseline',flag,value,'--output',out/('mixed-'+flag[2:])],expected=1)
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',scenario=str(a.scenario),ticks=a.ticks,counts=[3,6,10],processChecks=len(checks),checks=checks,profile=profile,votingChecked=a.voting_check,scope='Actual same C# ordinary setup/controllers/executor; no real model/device claim'),indent=2)+'\n')
print('Ordinary 3/6/10 real sessions, independent controllers, exact load/replay and profile passed')
