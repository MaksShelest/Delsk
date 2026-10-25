#!/usr/bin/env python3
"""Real C2 runner/quotes/expiry/save/replay gate; prepared materials, not D progression.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',required=True,type=Path);a=p.parse_args();out=a.output.resolve()
if out.exists() and any(out.iterdir()):p.error('Use a new/empty output')
out.mkdir(parents=True,exist_ok=True);runner=['dotnet',ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll'];checks=[]
def check(name,cmd):
 subprocess.run([sys.executable,ROOT/'scripts/run_check.py','--name',name,'--timeout','60','--log-directory',out/'checks','--',*map(str,cmd)],cwd=ROOT,check=True);checks.append(name)
common=[*runner,'--script','demolition-c2','--scenario',ROOT/'scenarios/vnext/demolition-c2-fixture.json'];base=out/'continuous';check('continuous',[*common,'--output',base]);events=[json.loads(line) for line in (base/'events.jsonl').read_text().splitlines()];tick=json.loads((base/'summary.json').read_text())['tick'];cuts=sorted({next(e['tick'] for e in events if e['type']==kind) for kind in ['DemolitionPrepared','BuildingDemolished','OverflowTaken']}|{next(e['tick'] for e in events if e['type']=='OverflowLost')-1});folders=[base]
for cut in cuts:
 save=out/f'{cut}.save.json';saved=out/f'saved-{cut}';loaded=out/f'loaded-{cut}';check(f'save-{cut}',[*common,'--output',saved,'--checkpoint',save,'--checkpoint-at',cut]);check(f'load-{cut}',[*runner,'--load',save,'--output',loaded]);check(f'compare-{cut}',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',base,saved,base,loaded]);folders.extend([saved,loaded])
replay=out/'replay';check('replay',[*common,'--replay',base/'commands.jsonl','--ticks',tick,'--output',replay]);check('compare-replay',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',base,replay]);folders.append(replay);check('balances',[sys.executable,ROOT/'scripts/verify_results.py',*folders]);public=[json.loads(line) for line in (base/'public-events.jsonl').read_text().splitlines()];assert not any(e['type'].startswith('DemolitionMaterial') for e in public)
receipt=json.loads((base/'demolition.json').read_text())['receipts'][0];assert receipt['priceMinor']==9000 and {m['itemId']:m['quantity'] for m in receipt['recovery']}=={'steel':1,'wood':18}
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',processChecks=len(checks),checkpointCuts=cuts,finalTick=tick,exports=len(folders),comparisonPairs=2*len(cuts)+1,scope='Real C# effects, private researcher demolition export, prepared material sources; no D1'),indent=2)+'\n');print('C2 physical conservation, preparation/save/load/replay and privacy passed')
