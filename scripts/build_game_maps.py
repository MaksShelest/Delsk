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
                road_corridor_cells=sorted(roads | set(spawns)), initial_city_dirt_cells=sorted(roads),
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
        district('valley_c2_east', 'grain', [(5,8),(6,8),(7,8),(5,9),(6,9),(6,10)], [(6,10)]),
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


def valley_i6c21():
    """Resources, fifteen lots, then the town. One-cell roads only."""
    roads = {(x, 12) for x in range(32)}
    roads |= {(10, z) for z in range(21)}
    roads |= {(23, z) for z in range(22)}
    def lot(n, x, z, entry):
        return dict(id=f'valley_c21_lot_{n:02}', cells=rect(x, z, 2, 3), entry=entry)
    lots = []
    for i, z in enumerate((0, 4, 8, 14, 18)):
        lots.append(lot(i * 3 + 1, 8, z, (10, z)))
        lots.append(lot(i * 3 + 2, 11, z, (10, z + 2)))
        lots.append(lot(i * 3 + 3, 21, z, (23, z + 1)))
    def cells(*points):
        return [list(p) for p in points]
    resources = [
        district('valley_c21', 'wood', cells((3,3),(3,2),(2,3),(2,4),(1,3),(2,2),(1,2),(0,2),(2,1),(1,1),(0,1)), [(2,4)]),
        district('valley_c21_east', 'wood', cells((4,0),(5,0),(6,0),(4,1),(5,1),(6,1),(5,2),(6,2)), [(6,2)]),
        district('valley_c21', 'grain', cells((1,6),(2,6),(3,6),(4,6),(0,7),(1,7),(2,7),(3,7),(4,7),(1,8),(2,8),(3,8),(4,8),(5,8),(2,9),(3,9)), [(5,8)]),
        district('valley_c21', 'milk', cells((0,14),(1,14),(2,14),(0,15),(1,15),(2,15),(1,16)), [(1,16)]),
        district('valley_c21', 'beet', cells((4,14),(5,14),(6,14),(5,15),(6,15),(6,16)), [(6,16)]),
        district('valley_c21', 'stone', cells((0,18),(1,18),(0,19),(1,19),(2,19)), [(2,19)]),
        district('valley_c21', 'sand', cells((4,18),(5,18),(3,19),(4,19)), [(3,19)]),
        district('valley_c21', 'iron_ore', cells((6,18),(6,19),(5,20)), [(5,20)]),
        district('valley_c21', 'copper_ore', cells((0,21),(1,21),(1,22)), [(1,22)]),
    ]
    # The first bays are the ones actually stocked. Keep them off the showroom exit.
    # Stocked extractors stand on different branches so neither blocks the other's only exit.
    parking = [(9, 17), (24, 18), (24, 21), (24, 15), (24, 13), (24, 10), (22, 17), (22, 13), (22, 3), (24, 16), (24, 17), (24, 19), (24, 20), (24, 14), (24, 11), (22, 7)]
    city = [civic(24, 1, 'town_hall', 'valley_c21'), civic(28, 1, 'terminal', 'valley_c21'), civic(24, 5, 'dealership', 'valley_c21')]
    stalls = [(30, 0), (31, 0), (30, 1), (31, 1), (30, 2), (31, 2), (30, 3), (31, 3), (27, 0), (26, 0), (31, 4)]
    # Startup rotation can place any participant on any spawn. Keep every spawn off the
    # one-cell roads the showroom and the two extractors use to reach the west branch.
    spawns = [(0, 12), (1, 12), (2, 12), (3, 12), (4, 12), (5, 12), (28, 12), (29, 12), (30, 12), (31, 12), (10, 20)]
    data = common('Малая долина', 'compact_valley_i6c21', 32, 25, 11, roads, lots, resources, city, stalls, spawns, parking)
    data['compact_exhibition'] = True
    data['assignment'] = 'startup-rotation-1; full six-cell industrial lots; eight public resources; no initial private ownership; compact-exhibition-1.'
    data['turn_cells'] = [(10, 12), (23, 12), (0, 12)]
    data['passing_bays'] = 'One central road and two single-cell branches. Showroom apron touches the city branch; equipment bays are a one-cell curb.'
    road = set(map(tuple, data['road_center_cells']))
    if not 60 <= len(road) <= 80 or any((x + 1, z) in road and (x, z + 1) in road and (x + 1, z + 1) in road for x, z in road):
        raise SystemExit(f'valley i6c21 road geometry {len(road)}')
    occupied = {}
    def take(points, label):
        for point in points:
            cell = tuple(point)
            if cell in occupied:
                raise SystemExit(f'overlap {cell} {occupied[cell]} / {label}')
            occupied[cell] = label
    for lot in lots:
        take(lot['cells'], lot['id'])
    for group in resources:
        take(group['cells'], group['id'])
        if any(tuple(cell) not in {tuple(item) for item in group['cells']} for cell in group['public_cells']):
            raise SystemExit('public cell outside '+group['id'])
    for site in city:
        take(site['footprint'], site['kind'])
    take(stalls, 'stall')
    take(parking, 'parking')
    for cell in road:
        if cell in occupied:
            raise SystemExit(f'road overlap {cell} {occupied[cell]}')
    if len(parking) != 16 or any(not any(abs(cell[0]-x)+abs(cell[1]-z)==1 for x, z in road) for cell in parking):
        raise SystemExit('equipment curb is not one cell from a road')
    return data


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
    bays = [(x,4) for x in (2,4)] + [(x,7) for x in (2,6,8)] + [(x,z) for z in (9,11) for x in (3,6)] + [(x,12) for x in (2,7,9)] + [(9,9)] + [(x,14) for x in (4,6,8)]
    return common('Лесной край', 'woodland_region_i6c2', 18, 15, 4, roads, lots, resources,
                  [civic(0,0,'town_hall','woodland'),civic(3,0,'terminal','woodland'),civic(5,0,'dealership','woodland')],
                  [(x,14) for x in range(4)], [(14,z) for z in (2,9,12,13)], bays)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--check', action='store_true')
    args = p.parse_args()
    for file, data in [('compact_valley', valley()), ('woodland_region', woodland()), ('compact_valley_i6c21', valley_i6c21())]:
        path = ROOT/'docs/vnext/maps'/f'{file}.json'
        content = json.dumps(data, ensure_ascii=False, indent=2)+'\n'
        if args.check:
            if path.read_text(encoding='utf-8') != content:
                p.error(f'{file}: regenerate fixed geography explicitly')
        else:
            path.write_text(content, encoding='utf-8', newline='\n')


if __name__ == '__main__':
    main()
