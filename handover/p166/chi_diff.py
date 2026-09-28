# -*- coding: utf-8 -*-
"""П166: χ²/ndf понятной части по спектрам — два каталога; печать изменившихся (|Δ| > 1e-4)."""
import csv
import glob
import os
import sys

def load(d):
    out = {}
    for f in glob.glob(os.path.join(d, '*_runs.csv')):
        with open(f, encoding='utf-8-sig') as h:
            for r in csv.DictReader(h):
                if r['part'] == 'known' and r['chi2ndf']:
                    out[r['spectrum']] = float(r['chi2ndf'])
    return out

a, b = load(sys.argv[1]), load(sys.argv[2])
rows = []
for s in sorted(a):
    if s in b and abs(b[s] - a[s]) > 1e-4:
        rows.append((b[s] / a[s] - 1.0, s, a[s], b[s]))
rows.sort()
better = sum(1 for r in rows if r[0] < 0)
print('Σχ²/ndf %.2f → %.2f; изменились %d из %d (лучше %d, хуже %d)' % (sum(a.values()), sum(b.values()), len(rows), len(a), better, len(rows) - better))
for d, s, x, y in rows:
    print('  %-24s %9.4f → %9.4f  (%+.2f %%)' % (s, x, y, 100 * d))
