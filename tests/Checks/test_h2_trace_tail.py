"""Independent tail audit corruption checks. Author: Шелестов Максим Евгеньевич."""
import hashlib,json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'scripts'))
from verify_h2_trace_tail import verify
class TraceTailAuditTests(unittest.TestCase):
 def prepare(self,root):
  full=root/'full';probe=root/'probe';full.mkdir();probe.mkdir();rows=[];tail=[]
  for tick in [1,2,3]:
   observation=dict(tick=tick,participant=dict(id=dict(value=2)))
   raw=json.dumps(observation,separators=(',',':'));rows.append('{"tick":'+str(tick)+',"actor":{"value":2},"observation":'+raw+'}')
   tail.append(dict(tick=tick,actor=dict(value=2),observationSha256=hashlib.sha256(raw.encode()).hexdigest(),previewCharacters=len(raw)))
  (full/'decisions.jsonl').write_text('\n'.join(rows)+'\n');(full/'summary.json').write_text(json.dumps(dict(tick=3,stateHash='world',controllerStateHash='controller',controllerDecisionTurns=3)))
  (probe/'phases.json').write_text(json.dumps(dict(stage='succeeded',tick=3,finalWorldHash='world',finalControllerHash='controller')))
  (probe/'trace-tail.jsonl').write_text('\n'.join(map(json.dumps,tail[-2:]))+'\n');return full,probe
 def test_exact_suffix_is_accepted_without_dropping_full_history(self):
  with tempfile.TemporaryDirectory() as d:
   full,probe=self.prepare(Path(d));r=verify(full,probe,2);self.assertEqual((r['fullHistoryTurns'],r['tailTurns']),(3,2))
 def test_wrong_observation_or_missing_turn_is_rejected(self):
  for missing in [False,True]:
   with tempfile.TemporaryDirectory() as d:
    full,probe=self.prepare(Path(d));rows=[json.loads(x) for x in (probe/'trace-tail.jsonl').read_text().splitlines()]
    if missing:rows.pop()
    else:rows[0]['observationSha256']='bad'
    (probe/'trace-tail.jsonl').write_text('\n'.join(map(json.dumps,rows))+'\n')
    with self.assertRaises(AssertionError):verify(full,probe,2)
