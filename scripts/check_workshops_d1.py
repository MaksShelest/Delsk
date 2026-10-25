#!/usr/bin/env python3
"""Real D1 profile/parallel production/save/replay gate; explicitly prepared physical places.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',required=True,type=Path);a=p.parse_args();out=a.output.resolve()
if out.exists() and any(out.iterdir()):p.error('Use a new/empty output')
out.mkdir(parents=True,exist_ok=True);runner=['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll'];checks=[]
def check(name,cmd):
 subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout','60','--log-directory',out/'checks','--',*map(str,cmd)],cwd=ROOT,check=True);checks.append(name)
common=[*runner,'--script','workshops-d1','--scenario',ROOT/'scenarios/vnext/workshops-d1-fixture.json'];base=out/'continuous';check('continuous',[*common,'--output',base]);events=[json.loads(line) for line in (base/'events.jsonl').read_text().splitlines()];tick=json.loads((base/'summary.json').read_text())['tick'];cuts=sorted({next(e['tick'] for e in events if e['type']==kind) for kind in ['WorkshopOutfitStarted','WorkshopBatchStarted','ProductionSettingChanged']}|{next(e['tick'] for e in events if e['type']=='WorkshopOutfitCompleted')-1,next(e['tick'] for e in events if e['type']=='ProductionCompleted')-1});folders=[base]
for cut in cuts:
 save=out/f'{cut}.save.json';saved=out/f'saved-{cut}';loaded=out/f'loaded-{cut}';check(f'save-{cut}',[*common,'--output',saved,'--checkpoint',save,'--checkpoint-at',cut]);check(f'load-{cut}',[*runner,'--load',save,'--output',loaded]);check(f'compare-{cut}',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',base,saved,base,loaded]);folders.extend([saved,loaded])
replay=out/'replay';check('replay',[*common,'--replay',base/'commands.jsonl','--ticks',tick,'--output',replay]);check('compare-replay',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',base,replay]);folders.append(replay);check('balances',[sys.executable,ROOT/'scripts/verify_results.py',*folders]);public=[json.loads(line) for line in (base/'public-events.jsonl').read_text().splitlines()];assert not any(e['type'].startswith('Workshop') for e in public)
state=json.loads((base/'final-state.json').read_text());flow={f['itemId']:f for f in state['flows']};assert flow['plank']['produced']==6 and flow['bread']['produced']==3
enterprise=state['enterprises'][0];assert len(enterprise['workshops'])==2 and all(w['completedBatches']==3 for w in enterprise['workshops']);assert sum(w['staff'] for w in enterprise['workshops'])==5
ledger=[json.loads(line) for line in (base/'ledger.jsonl').read_text().splitlines()];assert sum(l['amountMinor'] for l in ledger if l['reason']=='Energy')==240;assert sum(l['amountMinor'] for l in ledger if l['reason']=='WorkshopOutfit')==15000
for i in range(2):
 target=out/f'profile-{i}';check(f'profile-{i}',[*runner,'--export-workshop-profile',ROOT/'config/vnext/economy.b1-supported.json','--balance',ROOT/'config/vnext/balance.b1.json','--output',target]);assert (target/'economy.d1-workshops.json').read_bytes()==(ROOT/'config/vnext/economy.d1-workshops.json').read_bytes()
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',processChecks=len(checks),checkpointCuts=cuts,finalTick=tick,exports=len(folders),comparisonPairs=2*len(cuts)+1,scope='Real C# parallel production, outfit/payroll/shared stores, exact save/load/replay and profile; ready places prepared, D2 not started'),indent=2)+'\n');print('D1 parallel workshops, balance, privacy and checkpoint/replay passed')
