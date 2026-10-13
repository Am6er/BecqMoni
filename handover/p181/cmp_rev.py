# -*- coding: utf-8 -*-
r"""П181 (29.09.2026): сравнение двух прогонов корпуса по chi2ndf, часть known/unknown (образец — П179 cmpruns_part.py).
  python cmp_rev.py <каталог A> <каталог B> [known|unknown] [--only=<mini.csv>]
Печать: n, изменилось, лучше/хуже/побитово, суммы, медианы, Δ, все строки по убыванию Δ.
Разделитель дробной части — точка.
"""
import csv
import glob
import io
import os
import statistics
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass


def load(d, part, only):
    out = {}
    for f in glob.glob(os.path.join(d, '*_runs.csv')):
        with io.open(f, encoding='utf-8-sig', newline='') as fh:
            for r in csv.DictReader(fh):
                if r.get('part') != part:
                    continue
                if only is not None and r['spectrum'] not in only:
                    continue
                try:
                    out[r['spectrum']] = float(r['chi2ndf'])
                except (ValueError, KeyError):
                    pass
    return out


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    only = None
    for a in sys.argv[1:]:
        if a.startswith('--only='):
            with io.open(a.split('=', 1)[1], encoding='utf-8-sig', newline='') as fh:
                rd = csv.reader(fh)
                rows = list(rd)
            head = rows[0]
            col = head.index('spectrum') if 'spectrum' in head else 0
            only = set(r[col] for r in rows[1:] if r)
    part = args[2] if len(args) > 2 else 'known'
    a = load(args[0], part, only)
    b = load(args[1], part, only)
    common = [s for s in a if s in b]
    ch = [s for s in common if abs(b[s] - a[s]) > 1e-9]
    better = sum(1 for s in ch if b[s] < a[s])
    worse = len(ch) - better
    sa = sum(a[s] for s in common)
    sb = sum(b[s] for s in common)
    print(u'part=%s nA=%d nB=%d общих=%d изменилось=%d лучше=%d хуже=%d побитово=%d' % (
        part, len(a), len(b), len(common), len(ch), better, worse, len(common) - len(ch)))
    if common:
        print(u'sumA=%.4f sumB=%.4f delta=%+.4f (%+.3f %%) медиана A %.2f B %.2f' % (
            sa, sb, sb - sa, 100.0 * (sb / sa - 1.0) if sa else 0.0,
            statistics.median(a[s] for s in common), statistics.median(b[s] for s in common)))
    only_a = sorted(set(a) - set(b))
    only_b = sorted(set(b) - set(a))
    if only_a or only_b:
        print(u'только в A: %s; только в B: %s' % (only_a, only_b))
    for s in sorted(ch, key=lambda s: -(b[s] - a[s])):
        print(u'%-28s %9.4f %9.4f %+8.4f %+7.2f%%' % (s, a[s], b[s], b[s] - a[s], 100 * (b[s] / a[s] - 1)))


if __name__ == '__main__':
    main()
