#!/usr/bin/env python3
"""Reject incomplete second collection or any mutation of the frozen H1 measurements.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,json
from pathlib import Path
from study_postg2_h1 import cases

def verify(original,collected):
 old=json.loads((original/'observations.json').read_text());new=json.loads((collected/'observations.json').read_text());clean=json.loads(json.dumps(new));audits=0
 expected={c['id'] for phase in ['ordinary','long'] for c in cases(old['plan'],phase)}
 if {r['case']['id'] for r in new['rows']}!=expected or len(new['rows'])!=len(expected):raise ValueError('Incomplete/duplicate world collection')
 for row in new['rows']:
  for segment in row['segments']:
   if segment['status']=='succeeded':
    checks=segment.get('audits',{})
    if set(checks)!={'verify_results.py','verify_public_economy.py'} or any(x['exit_code']!=0 for x in checks.values()):raise ValueError('Successful segment without both original passing audits')
    audits+=1
 for row in clean['rows']:
  for segment in row['segments']:segment.pop('audits',None)
 if clean!=old:raise ValueError('Original measurements or protocol changed in collection')
 return dict(worlds=len(new['rows']),successfulSegments=audits,accountingAudits=audits,privacyAudits=audits,originalMeasurementsUnchanged=True)
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--original',type=Path,required=True);p.add_argument('--collected',type=Path,required=True);a=p.parse_args();result=verify(a.original,a.collected);(a.collected/'collection-verification.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result))
