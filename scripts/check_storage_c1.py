#!/usr/bin/env python3
"""C1 real runner/projection/save/replay gate. Prepared material sources, not D progression.
Author: Шелестов Максим Евгеньевич.
"""
import argparse, json, subprocess, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',required=True,type=Path);a=p.parse_args();out=a.output.resolve()
if out.exists() and any(out.iterdir()):p.error('Use a new/empty output')
out.mkdir(parents=True,exist_ok=True);runner=['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll'];records=[]
def check(name,cmd,timeout=60):
 subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout',str(timeout),'--log-directory',out/'checks','--',*map(str,cmd)],cwd=ROOT,check=True);records.append(name)
for i in range(2):
 target=out/f'profile-{i}';check(f'profile-{i}',[*runner,'--export-storage-profile',ROOT/'config/vnext/economy.b1-supported.json','--balance',ROOT/'config/vnext/balance.b1.json','--output',target]);assert (target/'economy.c1-storage.json').read_bytes()==(ROOT/'config/vnext/economy.c1-storage.json').read_bytes()
scenario=ROOT/'scenarios/vnext/storage-c1-fixture.json';common=[*runner,'--scenario',scenario,'--script','storage-c1'];base=out/'continuous';check('continuous',[*common,'--output',base])
events=[json.loads(line) for line in (base/'events.jsonl').read_text().splitlines()];tick=json.loads((base/'summary.json').read_text())['tick']
cuts=sorted({next(e['tick'] for e in events if e['type']==kind) for kind in ['StorageConstructionStarted','TransferStarted','OverflowCreated','OverflowWarning','OverflowTaken']} | {next(e['tick'] for e in events if e['type']=='OverflowLost')-1})
folders=[base]
for cut in cuts:
 save=out/f'{cut}.save.json';saved=out/f'saved-{cut}';loaded=out/f'loaded-{cut}'
 check(f'save-{cut}',[*common,'--output',saved,'--checkpoint',save,'--checkpoint-at',cut]);check(f'load-{cut}',[*runner,'--load',save,'--output',loaded]);check(f'equal-{cut}',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',base,saved,base,loaded]);folders.extend([saved,loaded])
replay=out/'replay';check('replay',[*common,'--replay',base/'commands.jsonl','--ticks',tick,'--output',replay]);check('replay-equal',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',base,replay]);folders.append(replay)
check('invariants',[sys.executable,ROOT/'scripts/verify_results.py',*folders]);state=json.loads((base/'final-state.json').read_text());assert sum(f.get('lost',0) for f in state['flows'])==7
public=[json.loads(line) for line in (base/'public-events.jsonl').read_text().splitlines()];assert not any(e['type'].startswith('Overflow') or e['type']=='TransferStarted' and e['reasonCode']=='Overflow' for e in public)
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',processChecks=len(records),checkpointCuts=cuts,finalTick=tick,scope='Real C# commands/transactions; prepared materials, no claim of D factories/import or target hardware'),indent=2)+'\n')
print('C1 profile, physical scenario, save/load/replay, conservation and privacy passed')
