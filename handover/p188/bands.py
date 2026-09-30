# -*- coding: utf-8 -*-
r"""П188 (30.09.2026): Σ(n−m)²/m по полосам энергии, rev35 против rev36, для названных спектров
(образец — П179 44-worsened-by-energy-band). n — столбец fit, m — model кривых разбора (--dump-curves).
  python bands.py <кривые A> <кривые B> спектр [спектр ...]
Разделитель дробной части — точка.
"""
import csv
import io
import os
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

BANDS = [(0, 30), (30, 40), (40, 100), (100, 300), (300, 1000), (1000, 1e9)]


def stats(path):
    out = [0.0] * len(BANDS)
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        for r in csv.DictReader(fh):
            e = float(r['keV'])
            a = float(r['fit'])
            b = float(r['model'])
            for i, (lo, hi) in enumerate(BANDS):
                if lo <= e < hi:
                    out[i] += (a - b) ** 2 / max(b, 1.0)
    return out


A, B = sys.argv[1], sys.argv[2]
print(u'%-18s  %s' % (u'спектр', '  '.join(u'%13s' % (u'%g–%g' % (lo, hi) if hi < 1e8 else u'>%g' % lo) for lo, hi in BANDS)))
for s in sys.argv[3:]:
    pa = os.path.join(A, s + '_curves.csv')
    pb = os.path.join(B, s + '_curves.csv')
    if not (os.path.exists(pa) and os.path.exists(pb)):
        print(u'%-18s  нет кривых' % s)
        continue
    a = stats(pa)
    b = stats(pb)
    print(u'%-18s  %s' % (s, '  '.join(u'%6.0f→%-6.0f' % (x, y) for x, y in zip(a, b))))
