# surface.py area.json [label] (an area dump of the water audit, WA_AREA): per area, the water surface's faces between neighbouring water columns and the top sources' labels
#  surface of a column = its top fluid: a source y + 7/8, flowing or falling level L y + L/8
#  faces: 4-neighbour pairs of water columns whose surfaces differ by more than 0.3 / 0.55 block (a ramp step is 1/8; a
#  layout 9 step edge to the next pool 2/8; the layout 8 chute faces 5/8-6/8)
import json, sys

def parse(code):
    c = code.split(':')[-1]
    p = c.split('-')
    return p[0], p[1], int(p[2]) if len(p) > 2 else 7

areas = json.load(open(sys.argv[1]))
label = sys.argv[2] if len(sys.argv) > 2 else ''
for i, a in enumerate(areas):
    surf, top = {}, {}
    for x, z, solid, fluids in a['cols']:
        if not fluids: continue
        y, code = fluids[0]
        name, flow, lvl = parse(code)
        if name not in ('water', 'rapidwater'): continue
        # river or lake water only: a source in the column's top 3 fluid cells (not a hillside rivulet)
        if not any(parse(c)[2] == 7 for _, c in fluids[:3]): continue
        s = y + lvl / 8.0
        surf[(x, z)] = (s, flow)
        top[(x, z)] = (name, flow, lvl)
    f30 = f55 = 0; mx = 0.0
    for (x, z), (s, fl) in surf.items():
        for dx, dz in ((1, 0), (0, 1)):
            o = surf.get((x + dx, z + dz))
            if o is None: continue
            d = abs(s - o[0]); mx = max(mx, d)
            if d > 0.3: f30 += 1
            if d > 0.55: f55 += 1
    src = [t for t in top.values() if t[2] == 7 and t[1] != 'd']
    rapid_still = sum(1 for t in src if t[0] == 'rapidwater' and t[1] == 'still')
    rapid_flow = sum(1 for t in src if t[0] == 'rapidwater' and t[1] != 'still')
    water_src = sum(1 for t in src if t[0] == 'water')
    flowing = sum(1 for t in top.values() if t[2] < 7 and t[1] != 'd')
    falling = sum(1 for t in top.values() if t[1] == 'd')
    print(f"{label:10s} {str(a['x']) + ',' + str(a['z']):22s} water cols {len(surf):5d}  faces >0.3: {f30:4d}  >0.55: {f55:4d}  max {mx:.2f} | "
          f"top sources: rapid still {rapid_still:5d}, rapid with flow {rapid_flow:5d}, ordinary {water_src:5d}; flowing tops {flowing:4d}, falling {falling:3d}")
