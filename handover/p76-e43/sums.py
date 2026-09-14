# -*- coding: utf-8 -*-
"""П66 — сводные числа понятной/непонятной части по каталогам прогона: n, Σχ²/ndf решателя, медиана,
Σ chi2ndf_pois (отчётный), медиана невязки модели, матрица найдена/применена — из `*_spline_runs.csv`
(часть — графа `part` самой строки прогона; так каталог плеча A (131) и B/C (135) судятся своими списками).

    python handover/p66-rev23/sums.py <каталог> [<каталог> …] [--part=known|unknown] [--only=<mini.csv>]
"""
import csv, glob, io, os, statistics, sys
sys.stdout.reconfigure(encoding='utf-8')


def runs(d):
    out = {}
    for p in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        for r in csv.DictReader(io.open(p, encoding='utf-8-sig', newline='')):
            out[r['spectrum']] = r
    return out


def main():
    dirs, part, only = [], 'known', None
    for a in sys.argv[1:]:
        if a.startswith('--part='):
            part = a[7:]
        elif a.startswith('--only='):
            only = set((r.get('spectrum') or r.get('key') or list(r.values())[0]) for r in csv.DictReader(io.open(a[7:], encoding='utf-8-sig', newline='')))
        else:
            dirs.append(a)
    print(u'часть %s: n, Σχ²/ndf решателя, медиана, Σ chi2ndf_pois, медиана невязки модели, матрица найдена/применена' % part)
    for d in dirs:
        rs = [r for r in runs(d).values() if r['part'] == part and (only is None or r['spectrum'] in only)]
        if not rs:
            print('%-24s пусто' % os.path.basename(d)); continue
        def num(x):
            try:
                return float(x)
            except (TypeError, ValueError):
                return None
        chi = [v for v in (num(r['chi2ndf']) for r in rs) if v is not None]
        chp = [v for v in (num(r.get('chi2ndf_pois')) for r in rs) if v is not None]
        res = [v for v in (num(r.get('model_residual_pct')) for r in rs) if v is not None]
        skipped = len(rs) - len(chi)
        found = sum(1 for r in rs if r.get('matrix_found') == '1')
        applied = sum(1 for r in rs if r.get('matrix_applied') == '1')
        print(u'%-24s n=%-3d (разобраны %d) Σχ²ndf %8.1f  медиана %6.2f  Σχ²p %8.1f  нев.мед %5.1f %%  матрица %d / %d'
              % (os.path.basename(d), len(rs), len(chi), sum(chi), statistics.median(chi) if chi else 0.0, sum(chp) if chp else 0.0,
                 statistics.median(res) if res else 0.0, found, applied))
    return 0


if __name__ == '__main__':
    sys.exit(main())
