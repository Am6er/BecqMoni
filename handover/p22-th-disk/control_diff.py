# -*- coding: utf-8 -*-
"""Положительный контроль П22: прогон `out_p22_disk` против базы `out_rev18_mini`
СТРОКА В СТРОКУ по `*_spline_runs.csv` и `*_spline_components.csv`, без столбцов
времени (`ms`, `cpu_ms`). Печатает расхождения поимённо; диск (`AS80_Th232Medal`)
ожидаемо расходится (был ERROR без геометрии) и печатается отдельно.

    python handover/p22-th-disk/control_diff.py [база] [плечо]
"""
import csv
import glob
import os
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BASE = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'tools', 'pie', 'out_rev18_mini')
ARM = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, 'tools', 'pie', 'out_p22_disk')
SKIP = {'ms', 'cpu_ms'}
NEW = {'AS80_Th232Medal'}


def rows(d, suffix):
    out = {}
    for p in sorted(glob.glob(os.path.join(d, '*_spline_%s.csv' % suffix))):
        for r in csv.DictReader(open(p, encoding='utf-8-sig')):
            key = (r['spectrum'], r.get('component', ''), r.get('kind', ''))
            out.setdefault(key, []).append(r)
    return out


for suffix in ('runs', 'components'):
    a, b = rows(BASE, suffix), rows(ARM, suffix)
    same = diff = 0
    only_a = sorted(set(a) - set(b))
    only_b = sorted(set(b) - set(a))
    for k in sorted(set(a) & set(b)):
        ra, rb = a[k], b[k]
        cols = [c for c in ra[0] if c not in SKIP]
        eq = len(ra) == len(rb) and all(x[c] == y[c] for x, y in zip(ra, rb) for c in cols)
        if eq:
            same += 1
        else:
            diff += 1
            bad = [c for c in cols if any(x[c] != y[c] for x, y in zip(ra, rb))]
            tag = '(диск, ожидаемо)' if k[0] in NEW else '⛔ КОНТРОЛЬ РАЗОШЁЛСЯ'
            print('  %s %s: %s' % (tag, k[0] + (' ' + k[1] if k[1] else ''), ', '.join(bad[:8])))
    print('%s: совпало %d, разошлось %d, только в базе %d, только в плече %d'
          % (suffix, same, diff, len(only_a), len(only_b)))
    for k in only_b:
        print('  только в плече: %s %s %s %s' % (k[0], k[1], k[2], '(диск, ожидаемо)' if k[0] in NEW else '⛔'))
    for k in only_a:
        print('  только в базе: %s %s %s ⛔' % k)
