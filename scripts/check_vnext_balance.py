#!/usr/bin/env python3
"""Actual semantic CLI exits, projection, new executor WIP and replay. Шелестов Максим Евгеньевич."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);a=p.parse_args();out=a.output.resolve()
if out.exists() and (not out.is_dir() or any(out.iterdir())):p.error('Use a new/empty dedicated output')
out.mkdir(parents=True,exist_ok=True)
runner=['dotnet',str(ROOT/'src/Research.Runner/bin/Release/net10.0/Research.Runner.dll')]
checks=[]
def check(name,cmd,expected=0,timeout=30):
    result=subprocess.run([sys.executable,str(ROOT/'scripts/run_check.py'),'--name',name,'--timeout',str(timeout),'--log-directory',str(out/'checks'),'--',*map(str,cmd)],cwd=ROOT,check=False)
    assert result.returncode==expected,(name,result.returncode,expected)
    checks.append(dict(name=name,exitCode=result.returncode))
def write(name,value):
    path=out/'inputs'/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(value,ensure_ascii=False),encoding='utf-8');return path
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def validate(name,path,expected=0,code=None,extra=()):
    destination=out/name;check(name,[*runner,'--validate-vnext-balance',path,'--output',destination,*extra],expected)
    report=json.loads((destination/'balance-validation.json').read_text())
    assert report['scope']=='semantic' and report['balanced'] is None
    assert report['status']==('succeeded' if expected==0 else 'failed')
    if code:assert any(e['code']==code for e in report['errors']),report
    if Path(path).is_file() and Path(path).stat().st_size<=1024*1024:assert report['sha256']==sha(Path(path))
    return report
full=ROOT/'config/vnext/balance.b1.json';source=ROOT/'docs/vnext/data/BALANCE_VNEXT.json';catalog=ROOT/'config/vnext/catalog.b1.json';profile=ROOT/'config/vnext/economy.b1-supported.json';baseline=ROOT/'config/economy.json'
protected=[baseline,source,catalog,full,profile,*sorted((ROOT/'scenarios').rglob('*.json'))]
before={str(f):sha(f) for f in protected}
check('full-extract-check',[sys.executable,ROOT/'scripts/extract_vnext_balance.py','--check'])
for name in ['generated1','generated2']:check(name,[sys.executable,ROOT/'scripts/extract_vnext_balance.py','--output',out/(name+'.json')])
assert (out/'generated1.json').read_bytes()==(out/'generated2.json').read_bytes()==full.read_bytes()
stale=out/'stale-source.json';stale.write_bytes(source.read_bytes()+b' ')
check('full-stale-check',[sys.executable,ROOT/'scripts/extract_vnext_balance.py','--source',stale,'--output',full,'--check'],2)
provenance=['--source',source,'--catalog',catalog]
report=validate('full',full,extra=[*provenance,'--baseline-economy',baseline])
assert report['provenanceVerified'] and report['counts']==dict(items=34,recipes=23,industries=6,sections=15)
assert len(report['coverage'])==15 and len(report['batches'])==69
assert [v['materialVolume'] for v in report['construction']]==[63,66,64,63,54,46]
assert report['bootstrap']['allRequiredReachable'] and len(report['bootstrap']['witness'])==30
assert report['startup']['steelUnitPriceMinor']==3125 and report['startup']['glassUnitPriceMinor']==4250
assert report['startup']['cashWithManuallyHarvestedWoodMinor']==36375 and report['startup']['additionalCashMinor']==16375 and report['startup']['woodSpotUnitsAtReferencePrice']==33
assert (out/'full/economy.b1-supported.json').read_bytes()==profile.read_bytes()
assert report['profileSha256']==sha(profile)
plain=validate('standalone',full);assert plain['provenanceVerified'] is False
validate('stale-source',full,2,'StaleSource',['--source',stale,'--catalog',catalog])
original=json.loads(full.read_text())
small=copy.deepcopy(original)
small['items']=[i for i in small['items'] if i['id'] in ('wood','plank','steel','glass','asphalt')]
for item in small['items']:
    if item['id'] not in ('wood','plank'):item.update(source='import',xp_per_unit=0)
small['industries']=small['industries'][:1];small['recipes']=small['recipes'][:1]
small['resources']={'wood':small['resources']['wood']};small['additional_workshop_outfits']={'woodworking':small['additional_workshop_outfits']['woodworking']}
for section in ['enterprise_tiers','warehouses','workshop_recipe_modernization','loading_platforms','vehicle_handling_kits','road_projects']:
    for entry in small[section]:
        if entry.get('materials'):entry['materials']={'wood':1}
assert validate('small',write('small.json',small))['counts']==dict(items=5,recipes=1,industries=1,sections=15)
broken=copy.deepcopy(original);broken['format_version']='future'
validate('unknown-format',write('future.json',broken),2,'UnsupportedVersion')
broken=copy.deepcopy(original);broken['rules']['typo']=1
validate('strict-rules',write('rules.json',broken),2,'InvalidJson')
broken=copy.deepcopy(original);broken['recipes'][0]['output']='missing' 
validate('reference',write('reference.json',broken),2,'UnknownReference')
broken=copy.deepcopy(original);broken['economy_globals']['import_refresh_ticks']=777
validate('metadata-conflict',write('metadata.json',broken),2,'SourceConflict',provenance)
broken=copy.deepcopy(original);broken['items'][0]['reference_price_minor']=999
validate('subset-conflict',write('subset.json',broken),2,'SubsetConflict',provenance)
broken=copy.deepcopy(original);broken['economy_globals']['bootstrap_steel']=5
validate('finite-seed',write('finite.json',broken),2,'UnreachableDependencies')
broken=copy.deepcopy(original);broken['recipe_versions'][1]['output_multiplier']=1000
validate('batch-capacity',write('capacity.json',broken),2,'BatchCapacity')
validate('missing',out/'absent.json',2,'IoError')
large=out/'large.json'
with large.open('wb') as f:f.truncate(100*1024*1024)
assert validate('oversized',large,2,'TooLarge')['sha256'] is None
# No world, adapter, model request, or foreign series may be created by any mixed invocation.
flags=['--scenario','--economy','--load','--replay','--ticks','--controllers','--model-mode','--model-config','--script','--seed','--checkpoint','--checkpoint-at','--human-ballot','--validate-only','--research','--research-setup','--model-wait','--validate-vnext-catalog']
for i,flag in enumerate(flags):
    dest=out/('mixed-'+str(i));check('mixed-'+str(i),[*runner,'--validate-vnext-balance',full,'--output',dest,flag,'absent'],2);assert not dest.exists()
for name,args in [('missing-value',['--validate-vnext-balance']),('duplicate',['--validate-vnext-balance',full,'--validate-vnext-balance',full,'--output',out/'dup']),('source-unpaired',['--validate-vnext-balance',full,'--source',source,'--output',out/'unpaired']),('unverified-export',['--validate-vnext-balance',full,'--baseline-economy',baseline,'--output',out/'unsafe'])]:
    check(name,[*runner,*args],2)
existing=out/'existing-series';existing.mkdir();marker=existing/'series.json';marker.write_text('keep exactly')
check('protect-series',[*runner,'--validate-vnext-balance',full,'--output',existing],2);assert marker.read_text()=='keep exactly' and list(existing.iterdir())==[marker]
occupied=out/'file-output';occupied.write_bytes(b'keep');check('protect-file',[*runner,'--validate-vnext-balance',full,'--output',occupied],2);assert occupied.read_bytes()==b'keep'
prior=(out/'full/balance-validation.json').read_bytes();check('protect-report',[*runner,'--validate-vnext-balance',full,'--output',out/'full'],2);assert prior==(out/'full/balance-validation.json').read_bytes()
# Explicit prepared factory, zero stock. Ordinary harvest/transfer/production commands, no Developer, model or Godot.
common=[*runner,'--script','vnext-supported-fixture','--scenario',ROOT/'scenarios/vnext/supported-production-fixture.json','--economy',profile]
check('production',[*common,'--output',out/'production'])
check('production-save',[*common,'--output',out/'production-saved','--checkpoint',out/'wip.save.json','--checkpoint-at','18'])
check('production-load',[*runner,'--load',out/'wip.save.json','--output',out/'production-loaded'])
check('production-load-equal',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',out/'production',out/'production-saved',out/'production',out/'production-loaded'])
check('production-replay',[*runner,'--script','vnext-supported-fixture','--scenario',out/'production/scenario.json','--replay',out/'production/commands.jsonl','--ticks','50','--output',out/'production-replayed'])
check('production-replay-equal',[sys.executable,ROOT/'scripts/verify_checkpoint_exports.py',out/'production',out/'production-replayed'])
check('production-balances',[sys.executable,ROOT/'scripts/verify_results.py',out/'production',out/'production-saved',out/'production-loaded',out/'production-replayed'])
summary=json.loads((out/'production/summary.json').read_text());assert summary['tick']==50
manifest=json.loads((out/'production/manifest.json').read_text());assert manifest['economyProfileId']=='vnext-b1-supported-1'
prov=manifest['economyProvenance'];assert prov['profileFileSha256']==sha(profile) and prov['metadata']['fullBalanceSha256']==sha(full) and prov['metadata']['sourceSha256']==sha(source)
assert prov['resolvedConfigHash']==manifest['configHash'] and manifest['configHash']!=sha(catalog)
ledger=[json.loads(line) for line in (out/'production/ledger.jsonl').read_text().splitlines()]
assert sum(l['amountMinor'] for l in ledger if l['reason']=='Wages')==2000
assert sum(l['amountMinor'] for l in ledger if l['reason']=='Energy')==40
assert before=={str(f):sha(f) for f in protected}
(out/'gate.json').write_text(json.dumps(dict(status='succeeded',scope='A2',sourceSha256=sha(source),fullSha256=sha(full),profileSha256=sha(profile),checks=checks,processChecks=len(checks),exports=4,comparisonPairs=3,preparedFixture=True,exactWipContinuation=True,sourceAndA1Verified=True,inputsPreserved=True),indent=2)+'\n')
print(f'A2 passed {len(checks)} real bounded process checks; prepared WIP/load/replay exact')
