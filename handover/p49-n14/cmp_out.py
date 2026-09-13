# -*- coding: utf-8 -*-
"""Побитовое сравнение двух каталогов прогона (runs/components/limits/anchors)
без граф ms/cpu_ms. Печатает по файлам число строк и число разошедшихся."""
import io, os, sys, glob, csv
a, b = sys.argv[1], sys.argv[2]
TIME = {'ms', 'cpu_ms'}
def load(p):
    with io.open(p, encoding='utf-8-sig', newline='') as f:
        rows = list(csv.reader(f))
    if not rows:
        return []
    head = rows[0]
    keep = [i for i, h in enumerate(head) if h not in TIME]
    return [[r[i] for i in keep if i < len(r)] for r in rows]
total = 0; bad = 0
for fa in sorted(glob.glob(os.path.join(a, '*_spline_*.csv'))):
    name = os.path.basename(fa)
    fb = os.path.join(b, name)
    if not os.path.exists(fb):
        print('%-40s НЕТ во втором' % name); bad += 1; continue
    ra, rb = load(fa), load(fb)
    n = max(len(ra), len(rb))
    diff = sum(1 for i in range(n) if i >= len(ra) or i >= len(rb) or ra[i] != rb[i])
    total += n; bad += diff
    print('%-40s строк %4d  разошлось %d' % (name, n, diff))
    if diff:
        for i in range(n):
            if i >= len(ra) or i >= len(rb) or ra[i] != rb[i]:
                print('   A: %s' % (','.join(ra[i])[:200] if i < len(ra) else '-'))
                print('   B: %s' % (','.join(rb[i])[:200] if i < len(rb) else '-'))
                break
print('ИТОГО строк %d, разошлось %d — %s' % (total, bad, 'ПОБИТОВО' if bad == 0 else 'РАСХОЖДЕНИЕ'))
sys.exit(0 if bad == 0 else 1)
