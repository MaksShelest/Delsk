#!/usr/bin/env python3
"""Independent runner load/replay and retained accounting audits for I3.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--fixtures',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
out=a.output.resolve();fixtures=a.fixtures.resolve()
if out.exists() and any(out.iterdir()):p.error('Use an empty output; retain previous attempts')
out.mkdir(parents=True);folders=[];checks=[];runner=['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll']
def check(name,command,timeout=120):
    subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout',str(timeout),'--log-directory',out/'checks','--',*map(str,command)],cwd=ROOT,check=True);checks.append(name)
for name in ['prepared','positioning','working','completed','map-compact_valley','map-small_river','map-large_districts']:
    boundary=json.loads((fixtures/(name+'-boundary.json')).read_text());tick=boundary['tick']+3
    replay=out/(name+'-replay');loaded=out/(name+'-load')
    check(name+'-replay',[*runner,'--scenario',fixtures/(name+'.scenario.json'),'--replay',fixtures/(name+'.commands.jsonl'),'--ticks',tick,'--output',replay])
    check(name+'-load',[*runner,'--load',fixtures/(name+'.save.json'),'--ticks',tick,'--output',loaded])
    check(name+'-equal',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py','--world-only',replay,loaded]);folders.extend([replay,loaded])
check('accounting',[sys.executable,ROOT/'scripts/verify_results.py',*folders])
check('capital-privacy',[sys.executable,ROOT/'scripts/verify_public_economy.py',*folders])
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',processChecks=len(checks),exports=len(folders),exactPairs=7,scope='I3 exact runner continuation and replay, independent road/fleet/billing/accounting/privacy audits. Prepared fixtures; ordinary earning and native UI are separate gates.'),indent=2)+'\n')
