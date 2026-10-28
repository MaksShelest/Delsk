#!/usr/bin/env python3
"""NPC/P76 natural-start smoke, controller continuation and independent accounting.
Author: Шелестов Максим Евгеньевич. Prepared action fixtures are a separate gate.
"""
import argparse,json,subprocess,sys
from pathlib import Path
from collections import Counter
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--map',choices=['small-river','large-districts'],required=True)
p.add_argument('--bots',type=int,choices=[3,6,10],required=True)
p.add_argument('--difficulty',choices=['Easy','Medium','Hard'],default='Medium')
p.add_argument('--ticks',type=int,default=1800)
p.add_argument('--output',type=Path,required=True)
a=p.parse_args();out=a.output.resolve()
if out.exists() and any(out.iterdir()):p.error('Use a new/empty output')
out.mkdir(parents=True,exist_ok=True)
runner=['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll'];checks=[]
def check(name,cmd,timeout=600):
    subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout',str(timeout),'--log-directory',out/'checks','--',*map(str,cmd)],cwd=ROOT,check=True);checks.append(name)
options=out/'options.json';options.write_text(json.dumps(dict(npcCount=a.bots,difficulty=a.difficulty,seed=42)))
source=ROOT/f'scenarios/vnext/{a.map}.json';definition=json.loads(source.read_text());original_limit=definition['maxTicks']
if a.ticks>original_limit:
    definition['maxTicks']=a.ticks;source=out/'map-horizon-only.json';source.write_text(json.dumps(definition))
cut=a.ticks//2;save=out/'npc.save.json';base=out/'start';loaded=out/'loaded';replay=out/'replay'
check('start',runner+['--scenario',source,'--economy',ROOT/'config/vnext/economy.g1-npc.json','--new-game',options,'--script','baseline','--human-ballot','self','--ticks',a.ticks,'--checkpoint',save,'--checkpoint-at',cut,'--output',base])
check('load',runner+['--load',save,'--ticks',a.ticks,'--output',loaded])
check('continuation',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',base,loaded],30)
check('replay',runner+['--scenario',base/'scenario.json','--replay',base/'commands.jsonl','--script','baseline','--ticks',a.ticks,'--output',replay])
check('replay-comparison',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py','--world-only',base,replay],30)
check('accounting',[sys.executable,ROOT/'scripts/verify_results.py',base,loaded,replay],60)
check('public-privacy',[sys.executable,ROOT/'scripts/verify_public_economy.py',base,loaded,replay],30)
state=json.loads((base/'final-state.json').read_text());decisions=[json.loads(r) for r in (base/'decisions.jsonl').read_text().splitlines()];commands=[json.loads(r) for r in (base/'commands.jsonl').read_text().splitlines()];events=[json.loads(r) for r in (base/'events.jsonl').read_text().splitlines()]
actors={str(r['actor']) for r in decisions}
assert len(actors)==a.bots,(actors,a.bots)
assert not any(r['type']=='InvariantViolation' for r in events)
summary=dict(status='succeeded',map=a.map,bots=a.bots,difficulty=a.difficulty,seed=42,ticks=a.ticks,checkpointTick=cut,processChecks=len(checks),exports=3,comparisonPairs=2,naturalStart=True,originalMapTickLimit=original_limit,horizonOnlyOverride=a.ticks>original_limit,
 goals=dict(Counter(r['decision']['goal'] for r in decisions)),actions=dict(Counter(r['command']['action']['kind'] for r in commands)),events=dict(Counter(r['type'] for r in events)),
 scope='One seed, natural start; exact controller/world continuation and ordinary-command replay. Not balance calibration or proof of difficulty superiority.')
summary['finalStateHash']=json.loads((base/'summary.json').read_text()).get('stateHash')
(out/'gate.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(summary,ensure_ascii=False))
