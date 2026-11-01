#!/usr/bin/env python3
"""Create the read-only-rule H2 profiling baseline from the exact pre-H2 commit.
Author: Шелестов Максим Евгеньевич. Only measurement hooks/runner entry point are patched.
"""
import argparse,io,json,subprocess,tarfile,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
BASE='29cb97d8f8517930d07fe7422139efca0799a009'
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);a=p.parse_args();out=a.output.resolve()
 if out==ROOT or ROOT.is_relative_to(out) or out.exists() and any(out.iterdir()):p.error('Use a distinct empty directory')
 out.mkdir(parents=True,exist_ok=True)
 archive=subprocess.check_output(['git','archive',BASE],cwd=ROOT)
 with tarfile.open(fileobj=io.BytesIO(archive)) as tar:tar.extractall(out,filter='data')
 patch=ROOT/'docs/research/h2/baseline-instrumentation.patch'
 subprocess.run(['git','apply','--check',str(patch)],cwd=out,check=True)
 subprocess.run(['git','apply',str(patch)],cwd=out,check=True)
 data=dict(base=BASE,patchSha256=hashlib.sha256(patch.read_bytes()).hexdigest(),source={str(x.relative_to(out)):hashlib.sha256(x.read_bytes()).hexdigest() for x in out.rglob('*.cs')})
 (out/'h2-baseline-source.json').write_text(json.dumps(data,indent=2)+'\n')
 print('Prepared original rules with host-only measurement hooks:',out)
if __name__=='__main__':main()
