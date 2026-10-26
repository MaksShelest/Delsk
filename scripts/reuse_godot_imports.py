#!/usr/bin/env python3
"""Reuse verified, identical asset imports in an isolated native probe, leaving its assemblies intact.
Author: Шелестов Максим Евгеньевич.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil


def reuse(source, target, *, verify_only=False):
    source, target = Path(source).resolve(), Path(target).resolve()
    if (source == target and not verify_only) or not (source / '.godot/imported').is_dir():
        raise ValueError('A separate previously imported source project is required')
    # An early editor quit must not look successful merely because some sidecars exist.
    importable = {'.svg', '.ttf', '.otf', '.wav', '.ogg', '.mp3', '.png', '.webp', '.jpg', '.jpeg', '.glb', '.gltf'}
    for asset in source.rglob('*'):
        if asset.is_file() and '.godot' not in asset.relative_to(source).parts and asset.suffix.lower() in importable and not Path(str(asset)+'.import').is_file():
            raise ValueError('Missing import metadata: '+str(asset.relative_to(source)))
    plans = []
    for sidecar in sorted(source.rglob('*.import')):
        relative = sidecar.relative_to(source)
        if '.godot' in relative.parts:
            continue
        asset = Path(str(relative)[:-7])
        original, staged = source / asset, target / asset
        if not original.is_file() or not staged.is_file() or original.read_bytes() != staged.read_bytes():
            raise ValueError('Imported source differs from staged asset: ' + str(asset))
        metadata = sidecar.read_text(encoding='utf-8')
        if 'source_file="res://' + asset.as_posix() + '"' not in metadata:
            raise ValueError('Invalid import source: ' + str(asset))
        cached = []
        for resource in sorted(set(re.findall(r'res://(\.godot/imported/[^"\r\n]+)', metadata))):
            cache = source / resource
            if not cache.resolve().is_relative_to(source / '.godot/imported') or not cache.is_file():
                raise ValueError('Missing or unsafe imported resource: ' + resource)
            cached.append((cache, target / resource))
        if not cached:
            raise ValueError('Import has no cached resources: ' + str(asset))
        # Godot records the source digest beside imported binaries. Matching the two
        # working trees alone would still accept a source changed after its import.
        digests=set()
        for cache, _ in cached:
            prefix=asset.name+'-'
            suffix=cache.name.removeprefix(prefix).split('.')[0]
            if not cache.name.startswith(prefix) or not re.fullmatch('[0-9a-f]{32}',suffix):
                raise ValueError('Unrecognized imported resource: '+str(cache))
            digest_path=cache.with_name(prefix+suffix+'.md5')
            if not digest_path.is_file() or 'source_md5="'+hashlib.md5(original.read_bytes(),usedforsecurity=False).hexdigest()+'"' not in digest_path.read_text(encoding='utf-8'):
                raise ValueError('Stale import source digest: '+str(asset))
            digests.add(digest_path)
        cached.extend((digest,target/digest.relative_to(source)) for digest in sorted(digests))
        plans.append((sidecar, target / relative, cached, asset, hashlib.sha256(original.read_bytes()).hexdigest()))
    if not plans:
        raise ValueError('No verified imported assets')
    if not verify_only:
        for sidecar, destination, cached, asset, digest in plans:
            for original, dest in [(sidecar, destination), *cached]:
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(original, dest)
    return dict(status='succeeded',assets=[dict(path=asset.as_posix(),sha256=digest,resources=len(cached)) for _,_,cached,asset,digest in plans],scope='All importable assets require metadata, cached resources and matching source digests; no editor state or managed assemblies copied. Native controls remain mandatory.',verifyOnly=verify_only)


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source',required=True,type=Path);p.add_argument('--target',type=Path);p.add_argument('--verify-only',action='store_true');p.add_argument('--report',required=True,type=Path);a=p.parse_args()
    if not a.verify_only and a.target is None:p.error('--target is required unless --verify-only')
    report=reuse(a.source,a.target or a.source,verify_only=a.verify_only);a.report.parent.mkdir(parents=True,exist_ok=True);a.report.write_text(json.dumps(report,indent=2)+'\n');print('Verified imported assets:',len(report['assets']))

if __name__=='__main__':main()
