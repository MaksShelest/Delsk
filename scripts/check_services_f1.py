#!/usr/bin/env python3
"""Actual F1 runner command tapes, independent accrual/payment audit and exact continuations.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--fixtures',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
out=a.output.resolve();fixtures=a.fixtures.resolve()
if out.exists() and any(out.iterdir()):p.error('Use a new/empty output')
out.mkdir(parents=True,exist_ok=True);runner=['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll'];checks=[];folders=[];pairs=0;cases={}
def check(name,command,timeout=120):
    subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout',str(timeout),'--log-directory',out/'checks','--',*map(str,command)],cwd=ROOT,check=True);checks.append(name)
plans={'pending':'ServiceRequested','rejected':'ServiceClosed','accepted':'ServiceAccepted','refunded':'ServiceClosed','expired':'ServiceClosed','competition':'ServiceRequested','requote':'ServiceRequoted','reconfirmed':'ServiceAccepted','offline':'ServiceAccepted','busy':'ServiceClosed','five-trips':'ServiceRequested','two-customers':'ServiceRequested','paid-claim':'ServiceAccepted','claim-refund':'ServiceClosed','npc-carrier':'ServiceAccepted','paused-response':'ServiceAccepted','delivery-quota':'ServiceAccepted','quota-refund':'ServiceClosed'}
for name,event in plans.items():
    tick=int((fixtures/(name+'.tick')).read_text());common=[*runner,'--scenario',fixtures/(name+'.json'),'--replay',fixtures/(name+'.commands.jsonl'),'--ticks',tick]
    base=out/name;check(name,common+['--output',base]);folders.append(base)
    events=[json.loads(line) for line in (base/'events.jsonl').read_text().splitlines()]
    matching=[e['tick'] for e in events if e['type']==event]
    cut=max(1,matching[0]-1) if matching else max(1,tick//2)
    save=out/f'{name}-{cut}.save.json';saved=out/f'{name}-saved';loaded=out/f'{name}-loaded'
    check(name+'-save',common+['--output',saved,'--checkpoint',save,'--checkpoint-at',cut]);check(name+'-load',[*runner,'--load',save,'--output',loaded]);check(name+'-compare',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',base,saved,base,loaded]);pairs+=2;folders.extend([saved,loaded]);cases[name]=dict(finalTick=tick,cut=cut)
check('independent-accounting',[sys.executable,ROOT/'scripts/verify_results.py',*folders]);check('capital-and-privacy',[sys.executable,ROOT/'scripts/verify_public_economy.py',*folders])
for number in range(2):
    target=out/f'profile-{number}';check(f'profile-{number}',[*runner,'--export-service-profile',ROOT/'config/vnext/economy.b1-supported.json','--balance',ROOT/'config/vnext/balance.b1.json','--output',target]);assert (target/'economy.f1-services.json').read_bytes()==(ROOT/'config/vnext/economy.f1-services.json').read_bytes()
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',processChecks=len(checks),exports=len(folders),comparisonPairs=pairs,cases=cases,scope='Actual F1 ordinary commands, independent quotes/exclusivity/escrow/refunds, money/flows/capital/privacy, exact save/load/replay. Prepared fixtures; F2 trips and delivered-part settlement excluded.'),indent=2)+'\n')
print('F1 service runner gate passed')
