#!/usr/bin/env python3
"""Print actual gate counters and failed bounded process logs for CI diagnosis.
Author: Шелестов Максим Евгеньевич.
"""
import json,sys
from pathlib import Path
for argument in sys.argv[1:]:
    root=Path(argument)
    for name in ('unit-check.json','developer-check.json','gate.json'):
        path=root/name
        if path.is_file():print(str(path)+':\n'+path.read_text(encoding='utf-8'))
    for path in sorted(root.glob('checks/*.json')):
        data=json.loads(path.read_text(encoding='utf-8'))
        print(path.stem+': '+json.dumps({k:data.get(k) for k in ('exit_code','elapsed_seconds','timeout_seconds')}))
        if data.get('exit_code',0):
            for suffix in ('stdout','stderr'):
                log=path.with_name(path.stem+'.'+suffix+'.log')
                if log.is_file():print(str(log)+':\n'+log.read_text(encoding='utf-8',errors='replace')[-12000:])
