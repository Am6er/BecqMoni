# -*- coding: utf-8 -*-
"""П343: суммы chi2ndf и chi2ndf_pois по части корпуса из *_spline_runs.csv.
python sums.py <каталог> [<каталог> ...]
"""
import csv
import glob
import io
import os
import statistics
import sys

for _s in (sys.stdout,):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except Exception:
        pass
for d in sys.argv[1:]:
    parts = {}
    for p in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        for r in csv.DictReader(io.open(p, encoding='utf-8-sig', newline='')):
            try:
                c = float(r['chi2ndf'])
                q = float(r['chi2ndf_pois'])
            except (ValueError, KeyError):
                continue
            parts.setdefault(r.get('part'), []).append((c, q))
    for part in sorted(parts, key=str):
        v = parts[part]
        print('%-60s %-8s n %3d  Σχ² %.4f  медиана %.2f  Σχ²(Пуассон) %.4f  медиана %.2f'
              % (d[-60:], part, len(v), sum(c for c, _ in v), statistics.median(c for c, _ in v),
                 sum(q for _, q in v), statistics.median(q for _, q in v)))
