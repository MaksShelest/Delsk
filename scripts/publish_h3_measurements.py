#!/usr/bin/env python3
"""Publish verified H3 measurements after unrelated branch advances, without force.
Author: Шелестов Максим Евгеньевич.
"""
import argparse
from pathlib import Path
import shutil
import subprocess

BRANCH='feat/vnext-h3-compact-acceptance'
FILES=('summary.json','observations.json.gz','collection-verification.json','source-artifacts.json','RESULTS.md')

def publish(root,analysis,kind):
    root=Path(root).resolve();analysis=Path(analysis).resolve()
    if kind not in ('economy','economy-intermediate'):raise ValueError('Unexpected H3 collection kind')
    def git(*args,check=True):
        return subprocess.run(['git',*args],cwd=root,text=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE,check=check)
    if git('diff','--quiet',check=False).returncode or git('diff','--cached','--quiet',check=False).returncode:
        raise ValueError('Tracked work must be clean before measurement publication')
    for name in FILES:
        if not (analysis/name).is_file():raise ValueError('Missing verified measurement: '+name)
    trigger=git('rev-parse','HEAD').stdout.strip()
    destination='docs/verification/h3/'+kind
    def remote_head():
        git('fetch','origin','refs/heads/'+BRANCH)
        remote=git('rev-parse','FETCH_HEAD').stdout.strip()
        if git('merge-base','--is-ancestor',trigger,remote,check=False).returncode:
            raise ValueError('H3 branch history changed; preserve the artifact for review')
        if git('diff','--name-only',trigger,remote,'--',destination).stdout.strip():
            raise ValueError('Newer H3 measurements exist; refuse to overwrite them')
        return remote
    git('switch','--detach',remote_head())
    target=root/destination;target.mkdir(parents=True,exist_ok=True)
    for name in FILES:shutil.copyfile(analysis/name,target/name)
    git('add','--',*[destination+'/'+name for name in FILES])
    if not git('diff','--cached','--quiet',check=False).returncode:return False
    git('commit','-m','H3: preserve audited world measurements without rerunning them')
    for attempt in range(3):
        remote=remote_head()
        git('rebase',remote)
        pushed=git('push','origin','HEAD:refs/heads/'+BRANCH,check=False)
        if pushed.returncode==0:return True
        # Retry only a concurrent fast-forward. Auth/network failures remain
        # failures; generated data and the uploaded artifact stay available.
        if not any(word in pushed.stderr for word in ('fetch first','non-fast-forward')):
            raise RuntimeError(pushed.stderr)
    raise RuntimeError('H3 branch kept advancing; measurements remain in the artifact')

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1])
    p.add_argument('--analysis',type=Path,required=True)
    p.add_argument('--kind',choices=['economy','economy-intermediate'],required=True)
    a=p.parse_args();print('Published' if publish(a.root,a.analysis,a.kind) else 'Already identical')
