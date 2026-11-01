#!/usr/bin/env python3
"""Independent streaming proof that bounded history reads retain the exact final observations.
Author: Шелестов Максим Евгеньевич. Does not execute simulation or materialize full history.
"""
import argparse,collections,gzip,hashlib,json
from pathlib import Path

def verify(full,probe,count=256):
 assert count>0
 summary=json.loads((full/'summary.json').read_text());phases=json.loads((probe/'phases.json').read_text())
 assert phases['stage']=='succeeded' and phases['tick']==summary['tick']
 assert phases['finalWorldHash']==summary['stateHash'] and phases['finalControllerHash']==summary['controllerStateHash']
 source=full/'decisions.jsonl';opener=open
 if not source.exists():source=Path(str(source)+'.gz');opener=gzip.open
 tail=collections.deque(maxlen=count);turns=0;raw_bytes=0
 with opener(source,'rt',encoding='utf-8') as f:
  for line in f:tail.append(line);turns+=1;raw_bytes+=len(line.encode())
 actual=[json.loads(line) for line in (probe/'trace-tail.jsonl').read_text().splitlines()]
 assert len(actual)==len(tail)==min(count,turns) and turns==summary['controllerDecisionTurns']
 decoder=json.JSONDecoder()
 for row,line in zip(actual,tail):
  start=line.index('"observation":')+len('"observation":');_,end=decoder.raw_decode(line,start);raw=line[start:end]
  turn=json.loads(line);assert (row['tick'],row['actor'])==(turn['tick'],turn['actor'])
  assert hashlib.sha256(raw.encode()).hexdigest()==row['observationSha256']
  assert row['previewCharacters']==min(len(raw.encode('utf-16-le'))//2,16384)
 return dict(status='succeeded',fullHistoryBytes=raw_bytes,fullHistoryTurns=turns,tailTurns=len(tail),tailObservationHashesMatchFullExport=True,
  scope='Streaming raw observation JSON SHA/tick/actor/count, preview length and final world/controller hashes. Full history retained; no simulation or policy executed.')
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('full',type=Path);p.add_argument('probe',type=Path);p.add_argument('--output',type=Path);a=p.parse_args();result=verify(a.full,a.probe)
 if a.output:a.output.write_text(json.dumps(result,indent=2)+'\n')
 print(json.dumps(result))
