#!/usr/bin/env python3
"""Explicit audit-only H3 erratum over the ORIGINAL frozen simulation/runtime.
Author: Шелестов Максим Евгеньевич. No changed DLL, seed, config or game command.
"""
import argparse,hashlib,json,os,sys
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--root',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
p.add_argument('--auditor',type=Path,required=True);p.add_argument('--audit-commit',required=True)
p.add_argument('--case',required=True);p.add_argument('--segments',type=int,default=1)
p.add_argument('--resume-interrupted',action='store_true',help='Retain a host-interrupted attempt and continue only its last audited checkpoint')
a=p.parse_args();root=a.root.resolve();out=a.output.resolve();auditor=a.auditor.resolve()
if not 0<=a.segments<=8:p.error('Use bounded0-8 segments')
sys.path.insert(0,str(root/'scripts'))
import study_h3 as original
q=original.protocol
plan=q.read(original.PLAN)
case=next((c for phase in ['ordinary','extended'] for c in q.cases(plan,phase) if c['id']==a.case),None)
if case is None or not (out/'trajectory.json').is_file():raise ValueError('Only an existing preregistered world may continue')
os.environ['PYTHONPATH']=str(root/'scripts')+os.pathsep+os.environ.get('PYTHONPATH','')
auditor_sha=q.digest(auditor);tool_sha=q.digest(__file__)
base_check=q.run_check

def corrected_check(name,budget,directory,command):
    values=list(map(str,command));changed=False
    for i,value in enumerate(values):
        if Path(value)==root/'scripts/verify_public_economy.py':values[i]=str(auditor);changed=True
    code,meta=base_check(name,budget,directory,values)
    if changed:meta=dict(meta,auditErratumSha256=auditor_sha)
    return code,meta

with q.lock(out/'.worker.lock'):
    state=q.read_metadata(out/'trajectory.json')
    if state['case']!=case or q.initial_fingerprint(plan,q.runtime(out))!=state['frozen']:
        raise ValueError('Original simulation/DLL/input/protocol fingerprint changed; audit erratum cannot authorize it')
    q.valid_checkpoint(out,state)
    original_bytes=(out/'trajectory.json').read_bytes();backup=out/'trajectory.before-audit-erratum-v1.json'
    if not backup.exists():backup.write_bytes(original_bytes)
    proof=dict(version=1,reason='Original public auditor omitted paid unfinished storage/development construction WIP',
        correctedAuditorSha256=auditor_sha,continuationToolSha256=tool_sha,auditSourceCommit=a.audit_commit,
        originalAuditorSha256=state['frozen']['helpers']['verify_public_economy.py'],
        originalFrozenSha256=hashlib.sha256(json.dumps(state['frozen'],sort_keys=True).encode()).hexdigest(),
        originalJournalSha256=q.digest(backup),simulationBinariesAndInputsUnchanged=True,
        scope='Original frozen fingerprint remains provenance of the original worker. Actual additional audit tool is explicitly recorded here; no original failure is overwritten and no extra independent world is created.')
    prior=state.setdefault('auditErrata',[])
    if not any(x['correctedAuditorSha256']==auditor_sha and x['continuationToolSha256']==tool_sha for x in prior):prior.append(proof)
    q.write(out/'audit-erratum.json',proof);q.write(out/'trajectory.json',state)
    if a.resume_interrupted and state['status'] in ['running','blocked_interrupted']:
        if state['status']=='running':
            attempt=dict(state.pop('activeAttempt'),status='interrupted',reason='Original host job ended; only the last audited checkpoint is accepted')
            state['segments'].append(attempt)
        state.setdefault('interruptionRecoveries',[]).append(dict(lastSavedTick=state['lastSavedTick'],
            lastCheckpointSha256=state.get('lastCheckpointSha256'),sourceCommit=a.audit_commit,
            scope='Explicit continuation of the SAME world from the prior audited checkpoint; original interrupted attempt and its files remain retained.'))
        state['status']='ready';q.write(out/'trajectory.json',state)
    if state['status']=='blocked_audit':
        index=len(state['segments'])-1;failed=state['segments'][index]
        if failed['status']!='audit_failure' or failed['runner']['exit_code']!=0:
            raise ValueError('Only completed original engine output may be re-audited without simulation')
        folder=q.owned(out,failed['result']);summary=q.read_metadata(folder/'summary.json')
        if summary['status']!='succeeded' or summary['tick']!=failed['targetTick']:raise ValueError('Incomplete original engine output')
        audits={}
        for tool,budget,path in [('verify_results.py',60,root/'scripts/verify_results.py'),('verify_public_economy.py',30,auditor)]:
            tag=f"reaudit-{failed['targetTick']}-{auditor_sha[:12]}-{tool.removesuffix('.py')}"
            if (out/'checks'/(tag+'.json')).exists():raise ValueError('Re-audit attempt already exists; preserve its outcome')
            code,meta=base_check(tag,budget,out,[sys.executable,path,folder]);audits[tool]=dict(meta,auditErratumSha256=auditor_sha)
            if code:raise ValueError('Corrected audit still rejects the original output; failure preserved')
        name='h1-metrics.audit-v1.json';metrics=q.measure(folder)
        if (folder/name).exists():raise ValueError('Derived metrics already exist; do not overwrite')
        q.write(folder/name,metrics)
        checkpoint=q.owned(out,failed['checkpoint'])
        record=dict(failed,status='succeeded',audits=audits,stateHash=summary['stateHash'],metrics=name,
            checkpointBytes=checkpoint.stat().st_size,checkpointSha256=q.digest(checkpoint),
            derivedAuditOnly=True,originalSegmentIndex=index,originalAuditFailurePreserved=True)
        state['segments'].append(record)
        state.update(lastSavedTick=failed['targetTick'],lastCheckpoint=failed['checkpoint'],lastCheckpointSha256=record['checkpointSha256'],
            status='completed' if failed['targetTick']==state['targetTicks'] else 'ready')
        q.write(out/'trajectory.json',state);q.archive_completed(out,state)
    # A host may have stopped during a large raw export. Retain its exact bytes
    # with the original lossless archival helper before another segment writes.
    # This does not promote partial output to an audited simulation result.
    successful={r['result'] for r in state['segments'] if r.get('status')=='succeeded'}
    if any((q.owned(out,name)/'decisions.jsonl').is_file() for name in successful):
        code,meta=base_check('retain-completed-'+str(len(state['segments'])),180,out,[sys.executable,'-c',
            'from pathlib import Path; import study_h3,sys; p=Path(sys.argv[1]); q=study_h3.protocol; q.archive_completed(p,q.read_metadata(p/"trajectory.json"))',out])
        if code:raise ValueError('Completed trace archival interrupted; original evidence retained, no new segment started')
        state.setdefault('completedTraceArchives',[]).append(meta);q.write(out/'trajectory.json',state)
    for record in state['segments']:
        if record['result'] in successful:continue
        folder=q.owned(out,record['result'])
        if not (folder/'decisions.jsonl').is_file():continue
        tag='retain-partial-'+Path(record['result']).name
        code,meta=base_check(tag,180,out,[sys.executable,'-c',
            'from pathlib import Path; from study_economy_h1 import compress_turns; import sys; compress_turns(Path(sys.argv[1]))',folder])
        if code:raise ValueError('Partial trace archive did not finish; original raw evidence remains retained')
        state.setdefault('partialTraceArchives',[]).append(dict(result=record['result'],check=meta,
            storage=q.read_metadata(folder/'decisions-storage.json'),scope='Lossless bytes of an incomplete/failed export; NOT a complete private history or successful segment.'))
        q.write(out/'trajectory.json',state)
q.run_check=corrected_check
state=q.execute(out,case,plan,a.segments,False)
print(json.dumps(dict(case=a.case,status=state['status'],lastSavedTick=state['lastSavedTick'],auditOnlyErratum=True,simulationBinariesUnchanged=True)))
raise SystemExit(1 if state['status']=='blocked_audit' else 0)
