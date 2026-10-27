#!/usr/bin/env python3
"""E1 real runner commands, independent audits and exact save/load at in-use boundaries.
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
plans={
    'manual-cargo':('manual-cargo',['VehiclePurchaseRequested','TransferStarted']),
    'freight':('freight',['LogisticsTripPrepared','LogisticsCargoLoaded']),
    'platforms':('platforms',['LoadingPlatformStarted']),
    'kit':('kit',['VehicleKitStarted']),
    'handling75':('LightKitAndIndependentMannedPlatformAddPowerAndChargeTheirActualInputs',['LoadingPlatformStarted']),
    'dealer':('DeliveryWaitsForMinimumAgeAndNeverReplacesPurchasedOrOverfillsParking',['DealerReplaced']),
}
for name,(fixture,selected) in plans.items():
    tick=int((fixtures/(fixture+'.tick')).read_text());common=[*runner,'--scenario',fixtures/(fixture+'.json'),'--replay',fixtures/(fixture+'.commands.jsonl'),'--ticks',tick]
    base=out/name;check(name,common+['--output',base]);folders.append(base)
    events=[json.loads(line) for line in (base/'events.jsonl').read_text().splitlines()]
    cuts=sorted({max(1,next(e['tick'] for e in events if e['type']==kind)) for kind in selected});cases[name]=dict(finalTick=tick,cuts=cuts)
    for cut in cuts:
        save=out/f'{name}-{cut}.save.json';saved=out/f'{name}-saved-{cut}';loaded=out/f'{name}-loaded-{cut}'
        check(f'{name}-save-{cut}',common+['--output',saved,'--checkpoint',save,'--checkpoint-at',cut]);check(f'{name}-load-{cut}',[*runner,'--load',save,'--output',loaded]);check(f'{name}-compare-{cut}',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',base,saved,base,loaded]);pairs+=2;folders.extend([saved,loaded])
check('balances-and-fleet',[sys.executable,ROOT/'scripts/verify_results.py',*folders]);check('public-economy',[sys.executable,ROOT/'scripts/verify_public_economy.py',*folders])
for number in range(2):
    target=out/f'profile-{number}';check(f'profile-{number}',[*runner,'--export-fleet-profile',ROOT/'config/vnext/economy.b1-supported.json','--balance',ROOT/'config/vnext/balance.b1.json','--output',target]);assert (target/'economy.e1-fleet.json').read_bytes()==(ROOT/'config/vnext/economy.e1-fleet.json').read_bytes()
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',processChecks=len(checks),exports=len(folders),comparisonPairs=pairs,cases=cases,scope='E1 real command tapes, exact whole exports including fleet.json, independent finite-stock/purchase/cargo/labour/capital/flow/privacy audits. Prepared materials/geography and cash; not natural progression, device or E2 acceptance.'),indent=2)+'\n')
print('E1 fleet runner gate passed')
