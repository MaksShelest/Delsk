#!/usr/bin/env python3
"""Fixed I6-C2 geography. No random generation or economic overrides.
Автор: Шелестов Максим Евгеньевич.
"""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def rect(x, z, w, h):
    return [(a, b) for b in range(z, z+h) for a in range(x, x+w)]


def civic(x, z, kind, prefix):
    w, h = (3, 3) if kind == 'town_hall' else (2, 3) if kind == 'terminal' else (5, 5)
    result = dict(id=prefix+'_'+kind, kind=kind, footprint=rect(x, z, w, h),
                  building=rect(x, z, 2, 2), entry=(x+(kind == 'town_hall'), z+2))
    if kind == 'dealership':
        result['parking'] = rect(x, z+3, 4, 1)
    return result


def district(prefix, item, cells, public=None):
    return dict(id=prefix+'_'+item, item=item, cells=cells,
                public_cells=public or [cells[-1]], profile=item)


def common(name, id, width, height, maximum, roads, lots, resources, city, stalls, spawns, bays):
    city[-1]['equipment_parking'] = bays
    return dict(schema_version='delsk-map-design-2', id=id, name=name, width=width, height=height,
                max_participants=maximum, road_center_cells=sorted(roads),
                road_corridor_cells=sorted(roads), initial_city_dirt_cells=sorted(roads),
                industrial_lots=lots, resource_districts=resources, civic_objects=city,
                market_stalls=[dict(id=f'{id}_stall_{n+1:02}', cell=p) for n, p in enumerate(stalls)],
                spawn_cells=spawns, turn_cells=[spawns[0]],
                passing_bays='Connected two-cell streets and public verges; distributed delivery bays face existing streets.',
                assignment='startup-rotation-1; full six-cell industrial lots; eight public resources; no initial private ownership.')


def valley():
    roads = set(rect(11, 0, 3, 25)+rect(28, 0, 2, 25))
    for z in (3, 8, 13, 18, 23):
        roads.update(rect(11, z, 19, 2))
    # The civic aprons themselves stay public; no road underneath the market.
    roads.update(rect(10, 19, 1, 5))
    lots = [dict(id=f'valley_c2_lot_{r*3+c+1:02}', cells=rect(15+c*5, r*5, 2, 3),
                 entry=(15+c*5, r*5+3)) for r in range(5) for c in range(3)]
    resources = [
        district('valley_c2', 'wood', [(0,1),(1,1),(2,1),(0,2),(1,2),(2,2),(3,2),(1,3),(2,3),(2,4)], [(2,4)]),
        district('valley_c2_east', 'wood', [(6,0),(7,0),(8,0),(5,1),(6,1),(7,1),(6,2),(7,2)], [(6,2)]),
        district('valley_c2', 'grain', [(0,7),(1,7),(2,7),(0,8),(1,8),(2,8),(3,8),(1,9),(2,9),(3,9)], [(3,9)]),
        district('valley_c2_east', 'grain', [(6,6),(7,6),(8,6),(6,7),(7,7),(7,8)], [(7,8)]),
        district('valley_c2', 'milk', [(0,12),(1,12),(0,13),(1,13),(2,13),(1,14),(2,14)]),
        district('valley_c2', 'beet', [(6,11),(7,11),(5,12),(6,12),(7,12),(6,13)]),
        district('valley_c2', 'iron_ore', [(0,16),(1,16),(1,17)]),
        district('valley_c2', 'copper_ore', [(5,16),(6,16),(5,17)]),
        district('valley_c2', 'stone', [(30,5),(31,5),(30,6),(31,6),(31,7)]),
        district('valley_c2', 'sand', [(30,15),(31,15),(30,16),(31,16),(30,17)]),
    ]
    bays = [(x,z) for z in (2,7,12,17,22) for x in (18,23)] + [(30,z) for z in (1,4,9,12,19,22)]
    return common('Малая долина', 'compact_valley_i6c2', 32, 25, 11, roads, lots, resources,
                  [civic(0,19,'town_hall','valley_c2'), civic(3,19,'terminal','valley_c2'), civic(5,19,'dealership','valley_c2')],
                  [(x,24) for x in range(11)], [(12,z) for z in range(0,22,2)], bays)


def woodland():
    roads = set(rect(10,0,2,15)+rect(17,3,1,12)+rect(0,5,18,2)+rect(10,3,8,2)
                +rect(10,10,8,1)+rect(10,14,8,1)+rect(4,6,2,8)+rect(0,13,12,1))
    lots = [dict(id=f'woodland_lot_{r*2+c+1:02}', cells=rect(x,z,2,3), entry=(x,z+3))
            for r,z in enumerate((0,7,11)) for c,x in enumerate((12,15))]
    resources = [
        district('woodland', 'wood', [(0,3),(1,3),(2,3),(3,3),(4,3)]),
        district('woodland_south', 'wood', [(0,8),(1,8),(0,9),(1,9)]),
        district('woodland', 'grain', [(7,8),(8,8),(7,9),(8,9)]),
        district('woodland', 'milk', [(0,10),(1,10),(0,11)]),
        district('woodland', 'beet', [(7,10),(8,10),(7,11)]),
        district('woodland', 'stone', [(2,9),(2,10),(2,11)]),
        district('woodland', 'sand', [(8,11),(9,11)]),
        district('woodland', 'iron_ore', [(14,0),(14,1)]),
        district('woodland', 'copper_ore', [(14,7),(14,8)]),
    ]
    bays = [(x,4) for x in (0,2,4)] + [(x,7) for x in (0,2,6,8)] + [(x,z) for z in (9,11) for x in (3,6)] + [(x,12) for x in (0,2,7,9)] + [(9,9)]
    return common('Лесной край', 'woodland_region_i6c2', 18, 15, 4, roads, lots, resources,
                  [civic(0,0,'town_hall','woodland'),civic(3,0,'terminal','woodland'),civic(5,0,'dealership','woodland')],
                  [(x,14) for x in range(4)], [(x,13) for x in (4,6,8,10)], bays)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--check', action='store_true')
    args = p.parse_args()
    for file, data in [('compact_valley', valley()), ('woodland_region', woodland())]:
        path = ROOT/'docs/vnext/maps'/f'{file}.json'
        content = json.dumps(data, ensure_ascii=False, indent=2)+'\n'
        if args.check:
            if path.read_text(encoding='utf-8') != content:
                p.error(f'{file}: regenerate fixed geography explicitly')
        else:
            path.write_text(content, encoding='utf-8', newline='\n')


if __name__ == '__main__':
    main()
