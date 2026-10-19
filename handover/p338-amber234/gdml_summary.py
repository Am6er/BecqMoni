import sys, re, math
import xml.etree.ElementTree as ET
sys.stdout.reconfigure(encoding='utf-8')
p = sys.argv[1]
root = ET.parse(p).getroot()
pos = {}
for d in root.find('define'):
    if d.tag == 'position':
        pos[d.get('name')] = (float(d.get('x')), float(d.get('y')), float(d.get('z')))
print('== materials')
for m in root.find('materials'):
    if m.tag == 'material':
        D = m.find('D'); fr = [(f.get('ref'), f.get('n')) for f in m.findall('fraction')] + [(f.get('ref'), 'n=' + f.get('n')) for f in m.findall('composite')]
        print(m.get('name'), D.get('value') if D is not None else '', fr)
solids = {s.get('name'): s for s in root.find('solids')}
vols = {}
for v in root.find('structure'):
    if v.tag == 'volume':
        vols[v.get('name')] = (v.find('materialref').get('ref'), v.find('solidref').get('ref'), [(pv.find('volumeref').get('ref'), pv.find('position')) for pv in v.findall('physvol')])
print('== volumes')
for name, (mat, sol, kids) in vols.items():
    s = solids[sol]
    if s.tag == 'genericPolycone':
        pts = [(float(r.get('rzpoint') or 0) if False else float(r.get('r')), float(r.get('z'))) for r in s.findall('rzpoint')]
        rs = [a for a, b in pts]; zs = [b for a, b in pts]
        print(f'{name:40s} {mat:22s} genericPolycone n={len(pts)} r[{min(rs):.2f},{max(rs):.2f}] z[{min(zs):.2f},{max(zs):.2f}]')
        if len(pts) <= 40: print('    pts', [(round(a, 3), round(b, 3)) for a, b in pts])
    elif s.tag == 'tessellated':
        vs = set()
        for t in s:
            for k in ('vertex1', 'vertex2', 'vertex3'): vs.add(t.get(k))
        P = [pos[v] for v in vs]
        xs = [a[0] for a in P]; ys = [a[1] for a in P]; zs = [a[2] for a in P]
        rr = [math.hypot(a[0], a[1]) for a in P]
        # volume via signed tetra sum
        vol = 0.0
        for t in s:
            a = pos[t.get('vertex1')]; b = pos[t.get('vertex2')]; c = pos[t.get('vertex3')]
            vol += (a[0]*(b[1]*c[2]-b[2]*c[1]) - a[1]*(b[0]*c[2]-b[2]*c[0]) + a[2]*(b[0]*c[1]-b[1]*c[0]))/6
        print(f'{name:40s} {mat:22s} tess facets={len(s)} x[{min(xs):.1f},{max(xs):.1f}] y[{min(ys):.1f},{max(ys):.1f}] z[{min(zs):.1f},{max(zs):.1f}] rmax={max(rr):.1f} vol={vol/1000:.2f} cm3')
    else:
        print(f'{name:40s} {mat:22s} {s.tag} ' + ' '.join(f'{k}={v}' for k, v in s.attrib.items() if k != 'name'))
        for z in s.findall('zplane'): print('    zplane', z.attrib)
    for kref, ppos in kids:
        print('    child', kref, ppos.attrib if ppos is not None else '')
