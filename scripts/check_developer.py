#!/usr/bin/env python3
"""Bounded Developer variant and negative Release gate. Шелестов Максим Евгеньевич."""
import argparse,json,subprocess,sys
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--dotnet',default='dotnet');p.add_argument('--output',type=Path,default=Path('artifacts/developer-gate'));a=p.parse_args()
root=Path(__file__).resolve().parents[1];out=a.output.resolve()
def check(name,timeout,cmd,expected=0):
 r=subprocess.run([sys.executable,str(root/'scripts/run_check.py'),'--name',name,'--timeout',str(timeout),'--log-directory',str(out/'checks'),'--',*cmd],cwd=root)
 if r.returncode!=expected:raise SystemExit(name+' returned '+str(r.returncode))
 return r.returncode
check('developer-restore',180,[a.dotnet,'restore','Delsk.sln','-p:DelskDeveloper=true','--locked-mode','--disable-parallel','-m:1'])
check('developer-build',180,[a.dotnet,'build','Delsk.sln','-c','Debug','-p:DelskDeveloper=true','--no-restore','--disable-build-servers','-m:1'])
check('developer-tests',180,[a.dotnet,'tests/Simulation.Tests/bin/developer/Debug/net10.0/Simulation.Tests.dll','-noColor','-noLogo','-parallelMode','none','-result-trx',str(out/'tests.trx')])
import xml.etree.ElementTree as ET
counters=next(e for e in ET.parse(out/'tests.trx').iter() if e.tag.endswith('Counters')).attrib
assert int(counters['passed'])>=886 and int(counters['failed'])==0 and int(counters.get('notExecuted',0))==0
check('forbidden-developer-release',60,[a.dotnet,'build','src/Simulation.Core/Simulation.Core.csproj','-c','Release','-p:DelskDeveloper=true','--no-restore','--disable-build-servers','-m:1'],1)
assert 'forbidden in Release' in (out/'checks/forbidden-developer-release.stdout.log').read_text()
(out/'developer-check.json').write_text(json.dumps({'status':'succeeded','release_negative_build':'expected rejection','scope':'Developer variant; ordinary release/client metadata checked separately'},indent=2)+'\n')
