#!/usr/bin/env python3
"""D2 real runner/save/replay/profile gate over command tapes produced by ordinary Core tests.
Fixtures explicitly prepare material sources and accelerate XP only for physical geometry.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];p=argparse.ArgumentParser(description=__doc__);p.add_argument('--fixtures',required=True,type=Path);p.add_argument('--output',required=True,type=Path);a=p.parse_args();out=a.output.resolve();fixtures=a.fixtures.resolve()
if out.exists() and any(out.iterdir()):p.error('Use a new/empty output')
out.mkdir(parents=True,exist_ok=True);runner=['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll'];checks=[];pairs=0;folders=[];cases={}
def check(name,cmd):
 subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout','60','--log-directory',out/'checks','--',*map(str,cmd)],cwd=ROOT,check=True);checks.append(name)
for name in ['physical','quests']:
 final=int((fixtures/(name+'.tick')).read_text());common=[*runner,'--scenario',fixtures/(name+'.json'),'--replay',fixtures/(name+'.commands.jsonl'),'--ticks',final];base=out/name;check(name,common+['--output',base]);folders.append(base)
 events=[json.loads(l) for l in (base/'events.jsonl').read_text().splitlines()];kinds=['StorageConstructionStarted','IndustryExperienceEarned','StorageConstructionCompleted'] if name=='physical' else ['IndustryQuestAccepted','IndustryQuestCompleted']
 cuts=sorted({max(1,next(e['tick'] for e in events if e['type']==kind)-1) for kind in kinds}|{next(e['tick'] for e in reversed(events) if e['type']==kinds[-1])-1});cases[name]=dict(finalTick=final,cuts=cuts)
 for cut in cuts:
  save=out/f'{name}-{cut}.save.json';saved=out/f'{name}-saved-{cut}';loaded=out/f'{name}-loaded-{cut}';check(f'{name}-save-{cut}',common+['--output',saved,'--checkpoint',save,'--checkpoint-at',cut]);check(f'{name}-load-{cut}',[*runner,'--load',save,'--output',loaded]);check(f'{name}-compare-{cut}',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',base,saved,base,loaded]);pairs+=2;folders.extend([saved,loaded])
 replay=out/f'{name}-replay';check(name+'-replay',common+['--output',replay]);check(name+'-replay-compare',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',base,replay]);pairs+=1;folders.append(replay)
 public=[json.loads(l) for l in (base/'public-events.jsonl').read_text().splitlines()];assert not any(e['type'].startswith('Industry') for e in public)
 d=json.loads((base/'development.json').read_text());assert len({(g['source'],g['sourceId']) for g in d['grants']})==len(d['grants'])
 if name=='physical':assert len(d['buildings'][0]['cells'])==6 and all(c['level']==5 for c in d['buildings'][0]['cells'])
 else:assert len(d['quests'])==12 and all(q['status']=='Completed' for q in d['quests']) and sum(g['xp'] for g in d['grants'])==600
check('balances',[sys.executable,ROOT/'scripts/verify_results.py',*folders]);check('public-economy',[sys.executable,ROOT/'scripts/verify_public_economy.py',*folders])
for n in range(2):
 target=out/f'profile-{n}';check(f'profile-{n}',[*runner,'--export-development-profile',ROOT/'config/vnext/economy.b1-supported.json','--balance',ROOT/'config/vnext/balance.b1.json','--output',target]);assert (target/'economy.d2-development.json').read_bytes()==(ROOT/'config/vnext/economy.d2-development.json').read_bytes()
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',processChecks=len(checks),cases=cases,exports=len(folders),comparisonPairs=pairs,scope='Real commands/runner/paid projects and funded quests, exact save/load/replay/privacy/balances. Prepared sources and accelerated geometry XP; no natural D3 balance claim.'),indent=2)+'\n');print('D2 development runner gate passed')
