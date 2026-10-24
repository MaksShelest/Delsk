#!/usr/bin/env python3
"""Mechanical B1 subset extraction, no economic validation. Шелестов Максим Евгеньевич."""
import argparse
import copy
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def unique(pairs):
    out = {}
    for key, value in pairs:
        if key in out:
            raise ValueError('Duplicate source field: '+key)
        out[key] = value
    return out


def extract(source):
    raw = source.read_bytes()
    data = json.loads(raw, object_pairs_hook=unique)
    industries = []
    for entry in data['industries']:
        if set(entry) != {'id', 'name', 'coins', 'materials'} or type(entry['coins']) is not int:
            raise ValueError('Unexpected source industry shape; update the explicit mapping')
        industries.append(dict(id=entry['id'], name=entry['name'], construction_price_minor=entry['coins']*100,
                               materials=copy.deepcopy(entry['materials'])))
    catalog = dict(format_version='delsk-vnext-catalog-1', id='vnext-b1', status='catalog_only',
                   source_sha256=hashlib.sha256(raw).hexdigest(), items=data['items'], recipes=data['recipes'], industries=industries)
    return (json.dumps(catalog, ensure_ascii=False, indent=2, allow_nan=False)+'\n').encode('utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=ROOT/'docs/vnext/data/BALANCE_VNEXT.json')
    parser.add_argument('--output', type=Path, default=ROOT/'config/vnext/catalog.b1.json')
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    expected = extract(args.source)
    if args.check:
        if not args.output.is_file() or args.output.read_bytes() != expected:
            parser.exit(2, 'Catalog is stale: source SHA or extracted values differ. Regenerate it.\n')
        print('B1 subset and provenance match exactly')
    else:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_bytes(expected)
        print(args.output)


if __name__ == '__main__':
    main()
