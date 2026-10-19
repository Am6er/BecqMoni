import sys, math
import xml.etree.ElementTree as ET
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
root = ET.parse(sys.argv[1]).getroot()
pos = {d.get('name'): (float(d.get('x')), float(d.get('y')), float(d.get('z'))) for d in root.find('define') if d.tag == 'position'}
sol = {s.get('name'): s for s in root.find('solids')}
for name in sys.argv[2:]:
    s = sol[name]
    tris = [[pos[t.get(k)] for k in ('vertex1', 'vertex2', 'vertex3')] for t in s]
    # ray-cast along lines to get material thickness: radial ray at z=zq, and axial ray at r=rq
    def hits_axis(zq):  # radial ray from axis at height zq, direction +x (and avg several phi)
        res = []
        for phi in np.linspace(0, 2*math.pi, 13)[:-1]:
            d = np.array([math.cos(phi), math.sin(phi), 0.0]); o = np.array([0, 0, zq])
            ts = []
            for a, b, c in tris:
                a, b, c = map(np.array, (a, b, c)); e1 = b - a; e2 = c - a; p = np.cross(d, e2); det = e1 @ p
                if abs(det) < 1e-12: continue
                inv = 1 / det; tv = o - a; u = (tv @ p) * inv
                if u < 0 or u > 1: continue
                q = np.cross(tv, e1); v = (d @ q) * inv
                if v < 0 or u + v > 1: continue
                t = (e2 @ q) * inv
                if t > 0: ts.append(t)
            res.append(sorted(set(round(t, 3) for t in ts)))
        return res
    def hits_z(rq):
        o = np.array([rq, 0, -100.0]); d = np.array([0, 0, 1.0]); ts = []
        for a, b, c in tris:
            a, b, c = map(np.array, (a, b, c)); e1 = b - a; e2 = c - a; p = np.cross(d, e2); det = e1 @ p
            if abs(det) < 1e-12: continue
            inv = 1 / det; tv = o - a; u = (tv @ p) * inv
            if u < 0 or u > 1: continue
            q = np.cross(tv, e1); v = (d @ q) * inv
            if v < 0 or u + v > 1: continue
            t = (e2 @ q) * inv
            if t > 0: ts.append(round(t - 100.0, 3))
        return sorted(set(ts))
    print(name)
    for zq in (0.0, 20.0, 30.0, -10.0):
        h = hits_axis(zq); print('  radial z=%.0f' % zq, h[0], h[3])
    for rq in (0.0, 10.0, 20.0, 27.0):
        print('  axial r=%.0f' % rq, hits_z(rq))
