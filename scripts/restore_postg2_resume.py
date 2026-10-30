#!/usr/bin/env python3
"""Restore and verify an H1 bundle in an empty workspace; executes no game tick.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,json,shutil,zipfile
from pathlib import Path
from research_lab import read,read_metadata,write
from study_postg2_h1 import PLAN,digest,owned,valid_checkpoint,initial_fingerprint
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--bundle',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();base=a.bundle.resolve();out=a.output.resolve()
if out.exists() and any(out.iterdir()):p.error('Use a new empty output; retain previous restore attempts')
out.mkdir(parents=True,exist_ok=True);m=read_metadata(base/'manifest.json');archive=out/'.restoring.zip'
with archive.open('wb') as stream:
 for part in m['parts']:
  source=owned(base,part['name'])
  if source.stat().st_size!=part['bytes'] or digest(source)!=part['sha256']:raise ValueError('Part changed: '+part['name'])
  with source.open('rb') as f:shutil.copyfileobj(f,stream)
if digest(archive)!=m['archiveSha256']:raise ValueError('Reassembled ZIP changed')
with zipfile.ZipFile(archive) as z:
 if len(z.namelist())!=len(m['files']) or set(z.namelist())!=set(m['files']):raise ValueError('ZIP contents differ from manifest')
 for info in z.infolist():
  owned(out,info.filename)
  if (info.external_attr>>16)&0o170000==0o120000:raise ValueError('Archive symlink forbidden')
 z.extractall(out)
for name,value in m['files'].items():
 path=owned(out,name)
 if path.stat().st_size!=value['bytes'] or digest(path)!=value['sha256']:raise ValueError('Restored file changed: '+name)
state=read_metadata(out/'trajectory.json');valid_checkpoint(out,state)
if state['frozen']!=initial_fingerprint(read(PLAN),out/'runtime'):raise ValueError('Current source/helpers/protocol do not match retained study; checkout original sourceCommit')
archive.unlink();result=dict(case=m['case'],lastSavedTick=state['lastSavedTick'],status=state['status'],verifiedFiles=len(m['files']),journalSha256=digest(out/'trajectory.json'),archiveSha256=m['archiveSha256'],scope='Exact bundle files and frozen source/inputs/runtime/checkpoint verified; no C# deserialize or game tick')
write(out/'restoration.json',result);print(json.dumps(result))
