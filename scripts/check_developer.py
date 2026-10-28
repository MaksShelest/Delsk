#!/usr/bin/env python3
"""Bounded Developer variant and negative Release gate. Шелестов Максим Евгеньевич."""
import argparse,json,subprocess,sys
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--dotnet',default='dotnet');p.add_argument('--output',type=Path,default=Path('artifacts/developer-gate'));p.add_argument('--suite',choices=['all','previous','roads','billing','services','runs','npc'],default='all');a=p.parse_args()
root=Path(__file__).resolve().parents[1];out=a.output.resolve()
def check(name,timeout,cmd,expected=0):
 r=subprocess.run([sys.executable,str(root/'scripts/run_check.py'),'--name',name,'--timeout',str(timeout),'--log-directory',str(out/'checks'),'--',*cmd],cwd=root)
 if r.returncode!=expected:
  for suffix in ('stdout','stderr'):
   log=out/'checks'/f'{name}.{suffix}.log'
   if log.exists():print(log.read_text(errors='replace')[-18000:],file=sys.stderr)
  raise SystemExit(name+' returned '+str(r.returncode))
 return r.returncode
check('developer-restore',180,[a.dotnet,'restore','Delsk.sln','-p:DelskDeveloper=true','--locked-mode','--disable-parallel','-m:1'])
check('developer-build',180,[a.dotnet,'build','Delsk.sln','-c','Debug','-p:DelskDeveloper=true','--no-restore','--disable-build-servers','-m:1'])
import os
os.environ['DELSK_G1_EVIDENCE']=str(out/'g1-fixtures')
os.environ['DELSK_F2_EVIDENCE']=str(out/'f2-fixtures')
os.environ['DELSK_F1_EVIDENCE']=str(out/'f1-fixtures')
from test_partitions import run_suite
partitions=run_suite(check,a.dotnet,'tests/Simulation.Tests/bin/developer/Debug/net10.0/Simulation.Tests.dll',out,prefix='developer-tests',timeout=180,suite=a.suite)
assert sum(p['passed'] for p in partitions)>=(16 if a.suite=='npc' else 30 if a.suite=='runs' else 27 if a.suite=='services' else 22 if a.suite=='billing' else 25 if a.suite=='roads' else 1164)
check('forbidden-developer-release',60,[a.dotnet,'build','src/Simulation.Core/Simulation.Core.csproj','-c','Release','-p:DelskDeveloper=true','--no-restore','--disable-build-servers','-m:1'],1)
assert 'forbidden in Release' in (out/'checks/forbidden-developer-release.stdout.log').read_text()
(out/'developer-check.json').write_text(json.dumps({'suite':a.suite,'status':'succeeded','testsPassed':sum(p['passed'] for p in partitions),'testPartitions':partitions,'release_negative_build':'expected rejection','scope':'Developer variant; ordinary release/client metadata checked separately'},indent=2)+'\n')
