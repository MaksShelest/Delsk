#!/usr/bin/env python3
"""Reproduce H3's fixed map design; no runtime generation or economy. Author: Шелестов Максим Евгеньевич."""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def design():
    def rect(x, z, w, h):
        return [[a, b] for b in range(z, z+h) for a in range(x, x+w)]

    centers = {(11, z) for z in range(26)}
    for z in (4, 10, 16, 22):
        centers.update((x, z) for x in range(11, 32))
    for z in (2, 7, 12, 17):
        centers.update((x, z) for x in range(9, 12))
    centers.update((x, 25) for x in range(12))
    corridor = {(x+dx, z+dz) for x, z in centers for dx, dz in
                ((0, 0), (1, 0), (-1, 0), (0, 1), (0, -1))
                if 0 <= x+dx < 32 and 0 <= z+dz < 27}
    lots = [dict(id=f'valley_lot_{r*6+c+1:02}', cells=rect(13+c*3, r*6, 2, 3),
                 entry=[13+c*3, r*6+3]) for r in range(4) for c in range(6)]
    resources = []
    for i, item in enumerate(('wood', 'grain', 'milk', 'beet', 'iron_ore', 'copper_ore', 'stone', 'sand')):
        x, z = (i % 2)*5, (i//2)*5
        resources.append(dict(id=f'valley_{item}', item=item, cells=rect(x, z, 3, 3),
                              public_cells=[[x+2, z+2]], profile=item))
    civic = []
    for kind, x, w, h in (('town_hall', 0, 3, 3), ('terminal', 3, 2, 3), ('dealership', 5, 5, 5)):
        site = dict(id=f'valley_{kind}', kind=kind, footprint=rect(x, 19, w, h),
                    building=rect(x, 19, 2, 2), entry=[x+(kind == 'town_hall'), 21])
        if kind == 'dealership':
            site['parking'] = rect(x, 22, 4, 1)
        civic.append(site)
    return dict(schema_version='delsk-map-design-2', id='compact_valley', name='Малая долина',
                width=32, height=27, max_participants=11,
                road_center_cells=sorted(centers), road_corridor_cells=sorted(corridor),
                initial_city_dirt_cells=sorted(centers), industrial_lots=lots,
                resource_districts=resources, civic_objects=civic,
                market_stalls=[dict(id=f'valley_stall_{i+1:02}', cell=[14+i, 24]) for i in range(11)],
                spawn_cells=[[11, z] for z in range(11)],
                turn_cells=[[11, z] for z in (4, 10, 16, 22, 25)],
                passing_bays='Protected side cells along the spine and branches; never private land.',
                assignment='startup-rotation-1; 24 independent full six-cell lots; no free ownership; eight public sources.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    path = ROOT/'docs/vnext/maps/compact_valley_h3.json'
    content = json.dumps(design(), ensure_ascii=False, indent=2)+'\n'
    if args.check:
        if path.read_text(encoding='utf-8') != content:
            parser.error('Committed compact map differs; regenerate explicitly')
    else:
        path.write_text(content, encoding='utf-8', newline='\n')


if __name__ == '__main__':
    main()
