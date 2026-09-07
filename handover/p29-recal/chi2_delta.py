# -*- coding: utf-8 -*-
u"""Разность χ²/ndf по спектрам между двумя прогонами — поимённо и с итогом.

    python handover/p29-recal/chi2_delta.py <каталог A> <каталог B>
"""
import csv
import io
import os
import sys


def load(d):
    out = {}
    for fn in sorted(os.listdir(d)):
        if not fn.endswith('_runs.csv'):
            continue
        with io.open(os.path.join(d, fn), encoding='utf-8-sig', newline='') as f:
            for r in csv.DictReader(f):
                try:
                    out[r['spectrum']] = (float(r['chi2ndf']), r['part'])
                except (TypeError, ValueError):
                    pass
    return out


a, b = load(sys.argv[1]), load(sys.argv[2])
rows = [(k, a[k][0], b[k][0], b[k][0] - a[k][0], a[k][1])
        for k in sorted(set(a) & set(b))]
rows.sort(key=lambda r: -abs(r[3]))
print(u'%-24s %10s %10s %10s %s' % (u'спектр', u'было', u'стало', u'Δ', u'часть'))
for k, x, y, d, p in rows:
    if abs(d) < 1e-9:
        continue
    print(u'%-24s %10.4f %10.4f %+10.4f %s' % (k, x, y, d, p))
for part in ('known', 'unknown'):
    sa = sum(r[1] for r in rows if r[4] == part)
    sb = sum(r[2] for r in rows if r[4] == part)
    n = len([r for r in rows if r[4] == part])
    print(u'ИТОГО %-8s спектров %3d   Σχ² %.4f -> %.4f   (Δ %+.4f)'
          % (part, n, sa, sb, sb - sa))
