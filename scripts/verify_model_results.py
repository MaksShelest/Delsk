#!/usr/bin/env python3
"""Independent P22 identity, exactly-once, timing and prompt privacy checks.
Author: Шелестов Максим Евгеньевич.
"""
import json
from pathlib import Path
import sys

def verify(folder):
    folder = Path(folder)
    rows = [json.loads(line) for line in (folder/'model-decisions.jsonl').read_text().splitlines()]
    public = (folder/'public-events.jsonl').read_text()
    assert not any(token in public for token in ['requestJson', 'responseJson', 'reasonSummary', 'ModelResolved'])
    if folder.name == 'model-off':
        assert not rows
        return
    proposed = {}; resolved = {}
    for row in rows:
        trace = row['trace']; request = json.loads(trace['requestJson']); actor = row['actor']['value']
        assert actor == request['actorId']
        assert trace['sessionEpoch'] == request['sessionEpoch'] > 0
        assert request['requestedTick'] == request['observationVersion'] == trace['requestedTick']
        assert request['applyTick'] == trace['applyTick'] <= request['expiresTick']
        assert len(request['prompt'].encode()) <= 16384
        assert 'seed' not in request['prompt'].lower() and 'randomstate' not in request['prompt'].lower()
        assert 1 <= len(request['options']) <= 5
        assert len({p['id'] for p in request['options']}) == len(request['options'])
        context = json.loads(request['prompt'].split('\n', 1)[1])['context']
        for p in context['support']:
            assert actor in [p['candidate']['value'], p['seller']['value']]
        assert 'worldState' not in context and 'marketRandom' not in context
        target = proposed if trace['resolution'] == 'Requested' else resolved
        assert trace['decisionId'] not in target, 'Duplicate decision resolution'
        target[trace['decisionId']] = trace
        if trace['resolution'] != 'Requested':
            assert row['tick'] + 1 == trace['applyTick']
            if trace['resolution'] == 'ModelResolved':
                answer = json.loads(trace['responseJson'])
                assert answer['actorId'] == actor
                assert answer['decisionId'] == trace['decisionId']
                assert answer['observationVersion'] == trace['requestedTick']
                assert answer['sessionEpoch'] == trace['sessionEpoch']
                assert answer['optionId'] in {p['id'] for p in request['options']}
    assert proposed.keys() == resolved.keys() and len(resolved) == 3
    assert sum(t['resolution'] == 'ModelResolved' for t in resolved.values()) == 1
    assert sorted(t['reasonCode'] for t in resolved.values()) == ['Accepted', 'DecisionDeadline', 'DecisionIdentityMismatch']
    print(f'{folder}: S7 identity, apply barrier, exactly-once resolution and prompt/public privacy passed')

for folder in sys.argv[1:]:
    verify(folder)
