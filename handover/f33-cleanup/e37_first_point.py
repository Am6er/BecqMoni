# -*- coding: utf-8 -*-
"""E37: pervaja tochka krivoj effektivnosti u KAZHDOGO spektra korpusa.

Chitaet tolko na chtenie, nichego ne pishet. Zapusk:
    python e37_first_point.py [<katalog spektrov>]

Lovushka, na kotoruju nastupil pervyj zahod: tag <Efficiency> vstrechaetsja
DVAZHDY - kak uzel snimka krivoj i kak pole tochki vnutri <ROIEfficiencyData>.
Ne-zhadnyj regex '<Efficiency>(.*?)</Efficiency>' obryvaetsja na pervoj tochke i
daet 'tochek 1' u ljuboj krivoj. Poetomu blok beretsja po <Curve>...</Curve>.
"""
import collections
import glob
import io
import os
import re
import sys

DEFAULT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                       '..', '..', 'tools', 'CORPUS', 'corpus', 'spectra')
d = sys.argv[1] if len(sys.argv) > 1 else DEFAULT

rows = []
for f in sorted(glob.glob(os.path.join(d, '*.xml'))):
    t = io.open(f, encoding='utf-8-sig', errors='replace').read()
    name = os.path.basename(f)[:-4]
    m = re.search(r'<Curve>(.*?)</Curve>', t, re.S)
    if not m:
        rows.append((name, None, 0, None, None))
        continue
    pts = re.findall(r'<Energy>([-\d.eE+]+)</Energy>', m.group(1))
    head = t[:m.start()]
    cur = re.findall(r'<Name>(.*?)</Name>', head)
    upd = re.findall(r'<LastUpdated>(.*?)</LastUpdated>', head)
    e0 = min(float(x) for x in pts) if pts else None
    rows.append((name, cur[-1] if cur else '?', len(pts), e0,
                 upd[-1] if upd else '?'))

bad = [r for r in rows if r[3] is not None and r[3] >= 39.0]
nocurve = [r for r in rows if r[2] == 0]
print('spektrov vsego: %d  bez krivoj: %d  pervaja tochka >= 39 keV: %d'
      % (len(rows), len(nocurve), len(bad)))
for r in bad:
    print('  %-24s krivaya %-20s tochek %3d pervaya %.2f obnovlena %s'
          % (r[0], r[1], r[2], r[3], r[4]))
c = collections.Counter(round(r[3], 2) for r in rows if r[3] is not None)
print('raspredelenie pervoj tochki:', dict(sorted(c.items())))
n = collections.Counter(r[2] for r in rows if r[3] is not None and r[3] >= 39.0)
print('chislo tochek u etih krivyh:', dict(n))
