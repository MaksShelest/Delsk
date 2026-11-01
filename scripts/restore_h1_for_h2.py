#!/usr/bin/env python3
"""Verify an original retained H1 bundle before an explicitly separate H2 experiment.
Unlike H1 continuation, this accepts a new declared runtime; it never rewrites the old journal.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,hashlib,json,shutil,zipfile
from pathlib import Path
def sha(p):
 h=hashlib.sha256()
 with p.open('rb') as f:
  for block in iter(lambda:f.read(1024*1024),b''):h.update(block)
 return h.hexdigest()
def owned(root,name):
 p=(root/name).resolve()
 if not p.is_relative_to(root.resolve()) or p==root.resolve():raise ValueError('Unsafe bundle path')
 return p
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--bundle',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
root=a.output.resolve()
if root.exists() and any(root.iterdir()):p.error('Use an empty output; preserve prior attempts')
root.mkdir(parents=True,exist_ok=True);m=json.loads((a.bundle/'manifest.json').read_text());archive=root/'.restoring.zip'
with archive.open('wb') as target:
 for part in m['parts']:
  source=owned(a.bundle,part['name']);assert source.stat().st_size==part['bytes'] and sha(source)==part['sha256']
  with source.open('rb') as f:shutil.copyfileobj(f,target)
assert sha(archive)==m['archiveSha256']
with zipfile.ZipFile(archive) as z:
 assert len(z.namelist())==len(m['files']) and set(z.namelist())==set(m['files'])
 for item in z.infolist():
  owned(root,item.filename);assert (item.external_attr>>16)&0o170000!=0o120000
 z.extractall(root)
for name,expected in m['files'].items():
 file=owned(root,name);assert file.stat().st_size==expected['bytes'] and sha(file)==expected['sha256']
state=json.loads((root/'trajectory.json').read_text());checkpoint=owned(root,state['lastCheckpoint']);assert sha(checkpoint)==state['lastCheckpointSha256']
assert state['case']==m['case'] and state['lastSavedTick']==m['lastSavedTick']
assert {name:sha(root/'runtime'/name) for name in state['frozen']['binaries']}==state['frozen']['binaries']
archive.unlink();result=dict(sourceCommit=state['sourceCommit'],checkpoint=str(checkpoint.relative_to(root)),checkpointSha256=sha(checkpoint),tick=state['lastSavedTick'],verifiedFiles=len(m['files']),originalJournalSha256=sha(root/'trajectory.json'),scope='All original bundle bytes/runtime/inputs/journal checked; H2 is a new declared binary, not a mutation of the H1 study')
(root/'h2-source-verification.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result))
