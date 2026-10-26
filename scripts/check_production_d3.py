#!/usr/bin/env python3
"""D3 runner/save/load/replay over actual ordinary command tapes. Шелестов Максим Евгеньевич."""
import argparse,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--fixtures',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
out=a.output.resolve();fixtures=a.fixtures.resolve()
if out.exists() and any(out.iterdir()):p.error('Use a new/empty output')
out.mkdir(parents=True,exist_ok=True);runner=['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll'];checks=[];folders=[];pairs=0;cases={}
def check(name,cmd):
 subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout','120','--log-directory',out/'checks','--',*map(str,cmd)],cwd=ROOT,check=True);checks.append(name)
for name in ['saw_planks','make_glass','assemble_conveyor','modernization-2','modernization-3','modernized-demolition','import','ordinary-start']:
 tick=int((fixtures/(name+'.tick')).read_text());common=[*runner,'--scenario',fixtures/(name+'.json'),'--replay',fixtures/(name+'.commands.jsonl'),'--ticks',tick]
 base=out/name;check(name,common+['--output',base]);folders.append(base)
 events=[json.loads(line) for line in (base/'events.jsonl').read_text().splitlines()]
 selected={'import':['ImportPurchased','ImportRestocked'],'ordinary-start':['StorageConstructionStarted','ProductionBatchStarted'],'modernization-2':['WorkshopModernizationStarted','ProductionBatchStarted'],'modernization-3':['WorkshopModernizationStarted','ProductionBatchStarted']}.get(name,['ProductionBatchStarted'])
 cuts=sorted({max(1,next(e['tick'] for e in events if e['type']==kind)) for kind in selected})
 cases[name]=dict(finalTick=tick,cuts=cuts)
 for cut in cuts:
  save=out/f'{name}-{cut}.save.json';saved=out/f'{name}-saved-{cut}';loaded=out/f'{name}-loaded-{cut}'
  check(f'{name}-save-{cut}',common+['--output',saved,'--checkpoint',save,'--checkpoint-at',cut]);check(f'{name}-load-{cut}',[*runner,'--load',save,'--output',loaded]);check(f'{name}-compare-{cut}',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',base,saved,base,loaded]);pairs+=2;folders.extend([saved,loaded])
 public=[json.loads(line) for line in (base/'public-events.jsonl').read_text().splitlines()];assert not any(e['type'].startswith(('Industry','Workshop')) or e['type']=='ImportPurchased' for e in public)
check('balances',[sys.executable,ROOT/'scripts/verify_results.py',*folders]);check('public-economy',[sys.executable,ROOT/'scripts/verify_public_economy.py',*folders])
for n in range(2):
 target=out/f'profile-{n}';check(f'profile-{n}',[*runner,'--export-production-profile',ROOT/'config/vnext/economy.b1-supported.json','--balance',ROOT/'config/vnext/balance.b1.json','--output',target]);assert (target/'economy.d3-production.json').read_bytes()==(ROOT/'config/vnext/economy.d3-production.json').read_bytes()
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',processChecks=len(checks),exports=len(folders),comparisonPairs=pairs,cases=cases,scope='Actual runner commands and replay, exact save/load, full export equality, balances/privacy. Prepared sources/XP except explicitly compact ordinary-start; no designed-map endgame or balance claim.'),indent=2)+'\n')
print('D3 production runner gate passed')
