# -*- coding: utf-8 -*-
r"""П97 — положительный контроль цепочки корпуса: плечо `--eltr=0` НОВЫМ кодом (матрицы RC103_point0 и
AS80_point0 с ключом ВЫКЛ, спектры HEAD) против `out_rev27_full` по четырём таблицам у трёх спектров —
ожидание ПОБИТОВО (без граф ms/cpu_ms).

  python handover/p97-physics19/cmp_off_rev27.py [out_rev27_full] [out_p97_off]
"""
import csv
import glob
import io
import os
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'tools', 'pie'))
ONLY = {'RC103_Cs137_0cm', 'AS80_Cs137_0cm', 'AS80_Am241'}
TIME = {'ms', 'cpu_ms'}


def table(d, t):
    out = {}
    for p in glob.glob(os.path.join(d, '*_spline_%s.csv' % t)):
        rows = list(csv.reader(io.open(p, encoding='utf-8-sig', newline='')))
        if not rows:
            continue
        head = rows[0]
        for r in rows[1:]:
            dd = dict(zip(head, r))
            if dd.get('spectrum') in ONLY:
                out.setdefault(dd['spectrum'], []).append(dd)
    return out


def main(argv):
    a = os.path.join(ROOT, argv[0] if argv else 'out_rev27_full')
    b = os.path.join(ROOT, argv[1] if len(argv) > 1 else 'out_p97_off')
    key = lambda d: tuple(d.get(k, '') for k in ('component', 'line', 'energy', 'anchor', 'E', 'kev'))
    tot = 0
    for t in ('runs', 'components', 'limits', 'anchors'):
        ta, tb = table(a, t), table(b, t)
        for s in sorted(ONLY):
            ra, rb = sorted(ta.get(s, []), key=key), sorted(tb.get(s, []), key=key)
            bad = set()
            if len(ra) != len(rb):
                bad.add('<rows %d->%d>' % (len(ra), len(rb)))
            else:
                for x, y in zip(ra, rb):
                    for k in set(x) | set(y):
                        if k not in TIME and x.get(k, '') != y.get(k, ''):
                            bad.add(k)
            tot += len(bad)
            print('%-11s %-16s строк %2d/%2d  %s' % (t, s, len(ra), len(rb), 'ПОБИТОВО' if not bad else 'РАЗОШЛОСЬ: ' + ', '.join(sorted(bad))))
    print('итого разошедшихся столбцов: %d' % tot)
    return 0 if tot == 0 else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
