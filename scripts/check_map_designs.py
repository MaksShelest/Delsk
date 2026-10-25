#!/usr/bin/env python3
"""Reproducible C# map adaptation and real CLI compatibility. No Python geometry/pathfinder.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,hashlib,json,subprocess,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);a=p.parse_args();out=a.output.resolve()
if out.exists() and any(out.iterdir()):p.error('Use a new/empty output')
out.mkdir(parents=True,exist_ok=True);runner=['dotnet',str(ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll')]
def check(name,args,expected=0):
 r=subprocess.run([sys.executable,str(ROOT/'scripts/run_check.py'),'--name',name,'--timeout','30','--log-directory',str(out/'checks'),'--',*map(str,args)],cwd=ROOT)
 assert r.returncode==expected,(name,r.returncode)
records=[]
for source,key in [('small_river','small-river'),('large_districts','large-districts')]:
 design=ROOT/'docs/vnext/maps'/f'{source}.json';profile=ROOT/'config/vnext/economy.b1-supported.json';snapshot=ROOT/'scenarios/vnext'/f'{key}.json'
 for i in range(2):
  folder=out/f'{key}-{i}';check(f'export-{key}-{i}',[*runner,'--export-map-design',design,'--economy',profile,'--output',folder]);assert (folder/'scenario.json').read_bytes()==snapshot.read_bytes()
 doc=json.loads(snapshot.read_text());assert doc['layout']['sourceSha256']==hashlib.sha256(design.read_bytes()).hexdigest()
 check('reject-profile-'+key,[*runner,'--scenario',snapshot,'--economy',ROOT/'config/economy.json','--script','baseline','--validate-only','true','--output',out/f'wrong-{key}'],1)
 assert not (out/f'wrong-{key}').exists()
 records.append(dict(map=source,sourceSha256=doc['layout']['sourceSha256'],scenarioSha256=hashlib.sha256(snapshot.read_bytes()).hexdigest(),profileSha256=hashlib.sha256(profile.read_bytes()).hexdigest()))
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',maps=records,scope='Exact source bytes and C# adapter snapshots; not runtime balance'),indent=2)+'\n')
print('Exact deterministic map snapshots and incompatible-profile rejection passed')
