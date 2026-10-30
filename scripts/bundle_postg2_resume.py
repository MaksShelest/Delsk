#!/usr/bin/env python3
"""Keep late H1 checkpoints portable beyond the lifetime of CI artifacts.
Author: Шелестов Максим Евгеньевич. Copies evidence; never executes another game tick.
"""
import argparse,hashlib,json,zipfile
from pathlib import Path
from research_lab import read_metadata,write
from study_postg2_h1 import owned,valid_checkpoint,digest
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();source=a.source.resolve();out=a.output.resolve()
if out.exists() and any(out.iterdir()):p.error('Retain previous bundles; choose empty output')
out.mkdir(parents=True,exist_ok=True);state=read_metadata(source/'trajectory.json');valid_checkpoint(source,state)
selected={'trajectory.json',state['lastCheckpoint']}
for directory in ['inputs','runtime']:
 selected.update(str(p.relative_to(source)) for p in (source/directory).rglob('*') if p.is_file())
for f in source.rglob('*'):
 if f.is_file() and (f.name in {'h1-metrics.json','summary.json','run-progress.json','decisions-storage.json'} or f.parent.name=='checks' and f.suffix in {'.json','.log'} and f.stat().st_size<100000):selected.add(str(f.relative_to(source)))
archive=out/'resume.zip';files={}
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as z:
 for name in sorted(selected):
  path=owned(source,name);files[name]=dict(bytes=path.stat().st_size,sha256=digest(path));z.write(path,name)
parts=[]
with archive.open('rb') as stream:
 while block:=stream.read(24*1024*1024):
  path=out/('resume.zip.part'+str(len(parts)).zfill(2));path.write_bytes(block);parts.append(dict(name=path.name,bytes=len(block),sha256=digest(path)))
if len(parts)>3:raise ValueError('Bundle exceeds declared three artifacts; keep files and extend transport explicitly')
write(out/'manifest.json',dict(version=1,case=state['case'],sourceCommit=state['sourceCommit'],sourceRun=40921991720,lastSavedTick=state['lastSavedTick'],status=state['status'],archiveSha256=digest(archive),parts=parts,files=files,scope='Last committed checkpoint, original runtime/inputs/journal and compact prior exports/checks. Full historical raw exports remain in original h1-full artifact. No new simulation tick.'))
archive.unlink();print(json.dumps(dict(case=state['case']['id'],tick=state['lastSavedTick'],parts=len(parts))))
