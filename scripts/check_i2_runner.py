#!/usr/bin/env python3
"""I2 true runner load/replay/export and retained independent audits.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--fixtures',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
out=a.output.resolve();fixtures=a.fixtures.resolve()
if out.exists() and any(out.iterdir()):p.error('Use an empty output; retain previous attempts')
out.mkdir(parents=True);runner=['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll'];folders=[];checks=[];pairs=0
def check(name,cmd,timeout=120):
 subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout',str(timeout),'--log-directory',out/'checks','--',*map(str,cmd)],cwd=ROOT,check=True);checks.append(name)
for name in ['mid-turn','mid-exit','loaded-service','paid-service','opposing-shared','opposing-turn-wait']:
 boundary=json.loads((fixtures/(name+'-boundary.json')).read_text());tick=boundary['tick']+3
 direct=out/(name+'-replay');loaded=out/(name+'-loaded')
 check(name+'-replay',[*runner,'--scenario',fixtures/(name+'.scenario.json'),'--replay',fixtures/(name+'.commands.jsonl'),'--ticks',tick,'--output',direct])
 check(name+'-load',[*runner,'--load',fixtures/(name+'.save.json'),'--ticks',tick,'--output',loaded]);folders.extend([direct,loaded])
 check(name+'-equal',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py','--world-only',direct,loaded]);pairs+=1
for map_key,count in [('compact-valley',3),('small-river',6),('large-districts',10)]:
 options=out/(map_key+'-options.json');options.write_text(json.dumps(dict(npcCount=count,difficulty='Medium',seed=42)))
 direct=out/map_key;loaded=out/(map_key+'-loaded');replay=out/(map_key+'-replay');save=out/(map_key+'.save.json')
 check(map_key,[*runner,'--scenario',ROOT/'scenarios/vnext'/(map_key+'.json'),'--economy',ROOT/'config/vnext/economy.g1-npc.json','--new-game',options,'--script','baseline','--human-ballot','self','--ticks',80,'--checkpoint',save,'--checkpoint-at',40,'--output',direct],300)
 scenario=json.loads((direct/'scenario.json').read_text());assert scenario['economy']['movement']['version']==2 and scenario['economy']['vehicleControl']==dict(version=1,exitHoldQuanta=5,laneOffset=200)
 check(map_key+'-load',[*runner,'--load',save,'--ticks',80,'--output',loaded],300)
 check(map_key+'-equal',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',direct,loaded]);pairs+=1
 check(map_key+'-replay',[*runner,'--scenario',direct/'scenario.json','--replay',direct/'commands.jsonl','--ticks',80,'--output',replay],300)
 check(map_key+'-replay-equal',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py','--world-only',direct,replay]);pairs+=1;folders.extend([direct,loaded,replay])
check('accounting',[sys.executable,ROOT/'scripts/verify_results.py',*folders])
check('capital-privacy',[sys.executable,ROOT/'scripts/verify_public_economy.py',*folders])
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',processChecks=len(checks),exports=len(folders),exactPairs=pairs,scope='I2 real C# runner, saved manoeuvres and real paid service; three ordinary maps with NPC3/6/10 and independent audits. No physical device or long balance claim.'),indent=2)+'\n')
