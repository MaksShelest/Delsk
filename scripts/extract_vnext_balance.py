#!/usr/bin/env python3
"""Mechanical full B1 extraction. No simulation or semantic rules. Шелестов Максим Евгеньевич."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
from extract_vnext_catalog import unique
ROOT = Path(__file__).resolve().parents[1]
SECTIONS = 'rules items recipes vehicles industries enterprise_tiers warehouses recipe_versions resources additional_workshop_outfits workshop_recipe_modernization loading_platforms vehicle_handling_kits road_projects economy_globals'.split()


def extract(source):
    raw = source.read_bytes()
    data = json.loads(raw, object_pairs_hook=unique)
    if set(data) != set(SECTIONS + ['schema_version', 'status', 'date']):
        raise ValueError('Source sections changed: update the explicit full mapping')
    full = dict(format_version='delsk-vnext-balance-1', id='vnext-b1', status='metadata_with_supported_projection',
                source_sha256=hashlib.sha256(raw).hexdigest(),
                source_metadata={k: data[k] for k in ['schema_version', 'status', 'date']})
    for section in SECTIONS:
        full[section] = copy.deepcopy(data[section])
    def coins(entry, target='price_minor'):
        if 'coins' in entry:
            value = entry.pop('coins')
            if type(value) is not int or not 0 <= value <= (2**63-1)//100:
                raise ValueError('Coins -> minor exceeds Int64')
            entry[target] = value*100
    for industry in full['industries']:
        coins(industry, 'construction_price_minor')
    for section in ['enterprise_tiers', 'warehouses', 'workshop_recipe_modernization', 'loading_platforms', 'vehicle_handling_kits', 'road_projects']:
        for entry in full[section]:
            coins(entry)
    for entry in full['additional_workshop_outfits'].values():
        coins(entry)
    # Absent source fields are explicit base-tier identities; never hidden reader defaults.
    for entry in full['enterprise_tiers']:
        if entry['level'] == 1:
            entry.update(price_minor=0, materials={}, ticks=0)
    for entry in full['recipe_versions']:
        if entry['version'] == 1:
            entry['unlock_level'] = 1
    for entry in full['loading_platforms']:
        entry.setdefault('service_minor_per_active_tick', 0)
        if entry['level'] == 1:
            entry.update(price_minor=0, materials={}, ticks=0)
    for entry in full['vehicles']:
        entry.setdefault('delivery_service_allowed', True)
    for old, new in [('resource_cell_coins', 'resource_cell_price_minor'), ('resource_lease_coins', 'resource_lease_price_minor')]:
        value = full['economy_globals'].pop(old)
        if type(value) is not int or not 0 <= value <= (2**63-1)//100:
            raise ValueError('Coins -> minor exceeds Int64')
        full['economy_globals'][new] = value*100
    return (json.dumps(full, ensure_ascii=False, indent=2, allow_nan=False)+'\n').encode('utf-8')


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source', type=Path, default=ROOT/'docs/vnext/data/BALANCE_VNEXT.json')
    p.add_argument('--output', type=Path, default=ROOT/'config/vnext/balance.b1.json')
    p.add_argument('--check', action='store_true')
    a = p.parse_args(); expected = extract(a.source)
    if a.check:
        if not a.output.is_file() or a.output.read_bytes() != expected:
            p.exit(2, 'Full B1 is stale: exact source SHA or mapped values differ.\n')
        print('Full B1 extraction/provenance match')
    else:
        a.output.parent.mkdir(parents=True, exist_ok=True); a.output.write_bytes(expected); print(a.output)

if __name__ == '__main__':
    main()
