#!/usr/bin/env python3
"""Prepared NPC action families: actual command replay, saved continuation, independent audits.
Author: Шелестов Максим Евгеньевич. These are not natural progression observations.
"""
import argparse,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--fixtures',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();out=a.output.resolve()
if out.exists() and any(out.iterdir()):p.error('Use new/empty output')
out.mkdir(parents=True,exist_ok=True);runner=['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll'];exports=[];checks=[];cases={}
def check(name,cmd,timeout=120):
    subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout',str(timeout),'--log-directory',out/'checks','--',*map(str,cmd)],cwd=ROOT,check=True);checks.append(name)
fixtures=a.fixtures.resolve();ticks=sorted(fixtures.glob('*.tick'));assert len(ticks)>=9
for index,file in enumerate(ticks):
    name=file.stem;tick=int(file.read_text());base=out/name;loaded=out/(name+'-loaded');again=out/(name+'-replay');save=out/(name+'.save.json');cut=max(1,tick//2)
    common=runner+['--scenario',fixtures/(name+'.json'),'--replay',fixtures/(name+'.commands.jsonl'),'--ticks',tick]
    check(str(index)+'-commands',common+['--checkpoint',save,'--checkpoint-at',cut,'--output',base]);check(str(index)+'-load',runner+['--load',save,'--output',loaded])
    check(str(index)+'-exact',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',base,loaded],15)
    exports.extend([base,loaded]);cases[name]=dict(tick=tick,checkpointTick=cut)
check('accounting',[sys.executable,ROOT/'scripts/verify_results.py',*exports],45)
check('privacy',[sys.executable,ROOT/'scripts/verify_public_economy.py',*exports],30)
for n in range(2):
    target=out/f'profile-{n}';check(f'profile-{n}',runner+['--export-npc-economy-profile',ROOT/'config/vnext/economy.b1-supported.json','--balance',ROOT/'config/vnext/balance.b1.json','--output',target]);assert (target/'economy.g1-npc.json').read_bytes()==(ROOT/'config/vnext/economy.g1-npc.json').read_bytes()
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',prepared=True,cases=cases,exports=len(exports),comparisonPairs=len(cases),processChecks=len(checks),scope='Policy-generated ordinary commands after explicit prepared staging; replay/checkpoint, finance/flow/privacy audits and frozen profile reproduction. Controller-memory continuations are covered separately by natural starts and unit tests.'),indent=2)+'\n')
print('Prepared G1 NPC actions passed')
