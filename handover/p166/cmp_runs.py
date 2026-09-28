# -*- coding: utf-8 -*-
"""П166: сверка двух каталогов прогона (runs.csv и components.csv) без столбцов времени.
python cmp_runs.py <каталог А> <каталог Б>  — печатает число отличных строк и первые отличия."""
import csv
import glob
import os
import sys

a_dir, b_dir = sys.argv[1], sys.argv[2]
SKIP = {'ms', 'cpu_ms'}
total_rows = diff_rows = 0
examples = []
for pattern in ('*_runs.csv', '*_components.csv'):
    for fa in sorted(glob.glob(os.path.join(a_dir, pattern))):
        fb = os.path.join(b_dir, os.path.basename(fa))
        with open(fa, encoding='utf-8-sig') as h:
            ra = list(csv.DictReader(h))
        with open(fb, encoding='utf-8-sig') as h:
            rb = list(csv.DictReader(h))
        key = (lambda r: (r['spectrum'], r.get('component', '')))
        da = {key(r): r for r in ra}
        db = {key(r): r for r in rb}
        for k in sorted(set(da) | set(db)):
            total_rows += 1
            x, y = da.get(k), db.get(k)
            if x is None or y is None:
                diff_rows += 1
                examples.append((os.path.basename(fa), k, 'только в ' + ('Б' if x is None else 'А')))
                continue
            cols = [c for c in x if c not in SKIP and x.get(c) != y.get(c)]
            if cols:
                diff_rows += 1
                examples.append((os.path.basename(fa), k, ', '.join('%s: %s → %s' % (c, x[c][:40], y.get(c, '')[:40]) for c in cols[:4])))
print('строк %d, отличных %d' % (total_rows, diff_rows))
for e in examples[:int(os.environ.get('SHOW', '12'))]:
    print('  %s %s: %s' % e)
