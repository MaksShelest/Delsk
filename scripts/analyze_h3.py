#!/usr/bin/env python3
"""World-level H3 analysis, reusing H1 statistics and actual H2-era engine metrics.
Author: Шелестов Максим Евгеньевич.
"""
import argparse
import gzip
import hashlib
import json
from pathlib import Path
import analyze_postg2_h1 as analysis
from study_postg2_h1 import cases

ROOT = Path(__file__).resolve().parents[1]
PLAN = ROOT/'docs/research/h3/study-plan.json'


def summarize(rows, plan):
    previous = analysis.cases
    analysis.cases = lambda p, phase: cases(p, 'extended' if phase == 'long' else phase)
    try:
        result = analysis.summarize(rows, plan)
    finally:
        analysis.cases = previous
    result.pop('long')
    result['extended'] = [r for r in rows if r['case']['phase'] == 'extended']
    result['definition'] = ('H3 Movement1, actual ordinary horizon1000000, no overrides. '
        'Separate maps; world/seed is the independent unit. Continuations remain one world. '
        'Missing and censored milestones never become zero-time milestones. '
        'Extended142/143 are distinct from ordinary42-51. No industrial Hard claim without mature exposure.')
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument('--study', type=Path)
    source.add_argument('--observations', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--require-complete-collection', action='store_true')
    args = parser.parse_args()
    plan = json.loads(PLAN.read_text(encoding='utf-8'))
    if args.observations:
        opener = gzip.open if args.observations.suffix == '.gz' else open
        with opener(args.observations, 'rt', encoding='utf-8') as stream:
            data = json.load(stream)
        if data['plan'] != plan:
            raise ValueError('Different preregistered plan')
        rows = data['rows']
        if data.get('formatVersion') == 2:
            rows = [dict(row, frozen=data['frozen']) for row in rows]
    else:
        rows = []
        for path in sorted(args.study.rglob('trajectory.json')):
            row=analysis.observe(path.parent);journal=analysis.read_metadata(path)
            if journal.get('auditErrata'):row['auditErrata']=journal['auditErrata']
            rows.append(row)
    registry_path=ROOT/'docs/research/h3/audit-erratum.json'
    registry=json.loads(registry_path.read_text()) if registry_path.exists() else None
    for row in rows:
        for revision in row.get('auditErrata',[]):
            if not registry or revision['correctedAuditorSha256']!=registry['correctedAuditorSha256'] or revision['originalAuditorSha256']!=registry['originalAuditorSha256']:
                raise ValueError('Unregistered audit erratum')
            frozen=hashlib.sha256(json.dumps(row['frozen'],sort_keys=True).encode()).hexdigest()
            if revision['originalFrozenSha256']!=frozen or revision['simulationBinariesAndInputsUnchanged'] is not True:
                raise ValueError('Audit erratum cannot authorize a changed simulator/input')
    result = summarize(rows, plan)
    result['retainedOriginalAuditFailures']=sum(s['status']=='audit_failure' for r in rows for s in r['segments'])
    result['derivedReauditedSegments']=sum(bool(s.get('derivedAuditOnly')) for r in rows for s in r['segments'])
    result['auditErrata']=[dict(case=r['case']['id'],revisions=r['auditErrata']) for r in rows if r.get('auditErrata')]
    if args.require_complete_collection and result['missing']:
        raise ValueError('Incomplete collection; preserve missing cases explicitly')
    audits = 0
    for row in rows:
        for segment in row['segments']:
            if segment['status'] != 'succeeded':
                continue
            recorded = segment.get('audits', {})
            if set(recorded) != {'verify_results.py', 'verify_public_economy.py'} or any(v['exit_code'] for v in recorded.values()):
                raise ValueError('Successful segment without both original passing audits')
            audits += 1
    args.output.mkdir(parents=True, exist_ok=True)
    common = rows[0]['frozen'] if rows else None
    with gzip.open(args.output/'observations.json.gz', 'wt', encoding='utf-8') as stream:
        json.dump(dict(formatVersion=2, plan=plan, frozen=common,
                       rows=[{k:v for k,v in row.items() if k != 'frozen'} for row in rows]), stream, ensure_ascii=False)
    analysis.write(args.output/'summary.json', result)
    analysis.write(args.output/'collection-verification.json', dict(worlds=len(rows), expected=result['expectedWorlds'],
        missing=len(result['missing']), successfulSegments=audits, accountingAudits=audits, publicAudits=audits,
        allOriginalSegmentAuditsVerified=True, scope='Collection and statistical analysis; no new simulation replicates'))
    print(json.dumps(dict(worlds=len(rows), expected=result['expectedWorlds'], missing=len(result['missing']), audits=audits)))


if __name__ == '__main__':
    main()
