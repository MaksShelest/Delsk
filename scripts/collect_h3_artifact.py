#!/usr/bin/env python3
"""Extract only owned compact evidence from one retained H3 ZIP. Author: Шелестов Максим Евгеньевич."""
import argparse
import collections
import io
import json
import re
import sys
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

class RemoteZip(io.RawIOBase):
    """Read selected ZIP entries by HTTPS ranges, never download the large raw archive.

    URL comes directly from GitHub's authenticated artifact redirect. Keep it out
    of logs/manifests. ZIP CRC validates each selected original entry; this does
    not claim an independent digest of the entire unrequested archive.
    """
    def __init__(self, url):
        parsed=urllib.parse.urlsplit(url)
        if parsed.scheme!='https' or not parsed.hostname or not parsed.hostname.endswith(('.blob.core.windows.net','.actions.githubusercontent.com','.githubusercontent.com')):
            raise ValueError('Expected the HTTPS GitHub artifact storage redirect')
        self.url=url;self.position=0;self.cache=collections.OrderedDict()
        with urllib.request.urlopen(urllib.request.Request(url,method='HEAD'),timeout=30) as response:
            self.size=int(response.headers['Content-Length'])
        if self.size<=0:raise ValueError('Empty artifact')
    def seekable(self):return True
    def readable(self):return True
    def tell(self):return self.position
    def seek(self, offset, whence=0):
        self.position=offset+(self.position if whence==1 else self.size if whence==2 else 0)
        if self.position<0:raise ValueError('Negative archive offset')
        return self.position
    def read(self, size=-1):
        end=self.size if size<0 else min(self.size,self.position+size)
        result=[]
        while self.position<end:
            start=self.position//65536*65536
            if start not in self.cache:
                stop=min(self.size-1,start+65535)
                request=urllib.request.Request(self.url,headers={'Range':f'bytes={start}-{stop}'})
                with urllib.request.urlopen(request,timeout=30) as response:
                    if response.status!=206 or response.headers.get('Content-Range')!=f'bytes {start}-{stop}/{self.size}':
                        raise ValueError('Artifact server did not honor the bounded range')
                    data=response.read(65537)
                if len(data)!=stop-start+1:raise ValueError('Truncated archive range')
                self.cache[start]=data
                if len(self.cache)>8:self.cache.popitem(last=False)
            data=self.cache[start];offset=self.position-start;length=min(end-self.position,len(data)-offset)
            result.append(data[offset:offset+length]);self.position+=length
        return b''.join(result)

parser = argparse.ArgumentParser(description=__doc__)
source=parser.add_mutually_exclusive_group(required=True)
source.add_argument('--archive', type=Path)
source.add_argument('--url-stdin', action='store_true', help='GitHub HTTPS artifact redirect as private stdin JSON')
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
with zipfile.ZipFile(RemoteZip(json.load(sys.stdin)['url']) if args.url_stdin else args.archive) as archive:
    journals = [n for n in archive.namelist() if n.endswith('/trajectory.json') or n == 'trajectory.json']
    if len(journals) != 1:
        raise ValueError('Expected exactly one original world journal')
    if archive.getinfo(journals[0]).file_size>2*1024*1024:raise ValueError('Oversized original world journal')
    journal = json.loads(archive.read(journals[0]))
    case = journal['case']['id']
    if not re.fullmatch(r'(ordinary|extended)-(compact-valley|small-river|large-districts)-(3|6|10)-(Easy|Medium|Hard)-\d+', case):
        raise ValueError('Unexpected case identifier')
    folder = args.output.resolve()/case
    if folder.exists():
        raise ValueError('Duplicate world; do not overwrite an earlier collection')
    prefix = journals[0].removesuffix('trajectory.json')
    for member in archive.infolist():
        if member.is_dir() or not member.filename.startswith(prefix):
            continue
        relative = Path(member.filename[len(prefix):])
        if relative.name not in {'trajectory.json', 'h1-metrics.json', 'run-progress.json', 'ordinary.json', 'decisions-storage.json', 'audit-erratum.json', 'trajectory.before-audit-erratum-v1.json', 'h1-metrics.audit-v1.json'} and not (relative.parent.name == 'checks' and relative.suffix in {'.json', '.log'}):
            continue
        if relative.suffix == '.log' and member.file_size > 100000:
            continue
        if member.file_size>2*1024*1024:raise ValueError('Unexpected oversized compact evidence entry')
        target = folder/relative
        if not target.resolve().is_relative_to(folder):
            raise ValueError('Archive path escape')
        if target.exists():raise ValueError('Duplicate selected archive entry; do not overwrite original evidence')
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(archive.read(member))
print(json.dumps(dict(case=case, status=journal['status'], lastSavedTick=journal['lastSavedTick'])))
