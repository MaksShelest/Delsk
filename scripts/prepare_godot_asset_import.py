#!/usr/bin/env python3
"""Stage identical importable assets without the Mono editor's C# project initialization.
Author: Шелестов Максим Евгеньевич.
"""
import argparse,hashlib,json,shutil
from pathlib import Path
IMPORTABLE={'.svg','.ttf','.otf','.wav','.ogg','.mp3','.png','.webp','.jpg','.jpeg','.glb','.gltf'}

def prepare(source,output):
    source=Path(source).resolve();output=Path(output).resolve()
    if not (source/'project.godot').is_file() or source==output or source.is_relative_to(output):raise ValueError('Use a separate source project and disposable output')
    if output.exists() and any(output.iterdir()):raise ValueError('Use a new/empty output')
    files=[]
    for asset in sorted(source.rglob('*')):
        relative=asset.relative_to(source)
        if not asset.is_file() or any(p in ('.godot','bin','obj') for p in relative.parts) or asset.suffix.lower() not in IMPORTABLE:continue
        target=output/relative;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(asset,target)
        sidecar=Path(str(asset)+'.import')
        if sidecar.is_file():shutil.copyfile(sidecar,Path(str(target)+'.import'))
        files.append(dict(path=relative.as_posix(),sha256=hashlib.sha256(asset.read_bytes()).hexdigest()))
    if not files:raise ValueError('No importable assets')
    text=(source/'project.godot').read_text(encoding='utf-8');rendering=text.split('[rendering]',1)[1].split('\n[',1)[0]
    (output/'project.godot').write_text('config_version=5\n[application]\nconfig/name="Delsk asset import"\nconfig/features=PackedStringArray("4.7", "GL Compatibility")\n[editor]\nimport/use_multiple_threads=false\n[rendering]'+rendering,encoding='utf-8')
    return dict(status='staged',assets=files,scope='Identical assets and import settings only. Required normal editor exit, independent verified cache reuse and ordinary Mono runtime replay follow. No assemblies, scripts, scenes or editor state copied.')

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--report',type=Path,required=True);a=p.parse_args()
    result=prepare(a.source,a.output);a.report.parent.mkdir(parents=True,exist_ok=True);a.report.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8');print('Staged actual assets:',len(result['assets']))
if __name__=='__main__':main()
