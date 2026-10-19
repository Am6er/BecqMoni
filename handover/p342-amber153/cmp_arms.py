# -*- coding: utf-8 -*-
u"""П342: плечи А/Б по мерке паспорта — разбор/паспорт у каждого спектра в каждом каталоге.

    python handover/p342-amber153/cmp_arms.py --only=<список.csv> --csv=<выход.csv> \
        A=tools/pie/out_p342_A B=tools/pie/out_p342_B ...

Первый каталог — контроль; печатается отношение к паспорту в каждом плече, сдвиг к контролю
(в %), χ²/ndf спектра (Пуассон, `chi2ndf_pois` из `*_runs.csv`) и итог по группам.
Отношение считается ТЕМ ЖЕ кодом, что мерка корпуса (`tools/pie/passport.py`: паспорт на
начало набора, масса по сосуду файла). Спектр, у которого в плече нет строки, — отказ словами.
"""
import argparse
import csv
import io
import os
import statistics
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
sys.path.insert(0, os.path.join(REPO, 'tools', 'pie'))
import passport  # noqa: E402
import score  # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def runs_chi(out_dir):
    res = {}
    for name in os.listdir(out_dir):
        if name.endswith('_spline_runs.csv'):
            for r in csv.DictReader(io.open(os.path.join(out_dir, name), encoding='utf-8-sig')):
                key = r.get('spectrum') or r.get('key')
                try:
                    res[key] = float(r.get('chi2ndf_pois') or 'nan')
                except ValueError:
                    res[key] = float('nan')
    return res


def arm_rows(out_dir, keys):
    results = score.load_results('spline', out_dir)[0]
    rows, refused, _ = passport.collect(results, keys & set(results), ruled=passport.ruled_in(out_dir))
    return {(r['key'], r['nuc']): r for r in rows}, refused, runs_chi(out_dir)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--only', required=True)
    ap.add_argument('--csv', default=None)
    ap.add_argument('arms', nargs='+', help='ИМЯ=каталог')
    a = ap.parse_args()
    keys = score.read_only(a.only)
    arms = []
    for spec in a.arms:
        name, _, path = spec.partition('=')
        if not os.path.isabs(path):
            path = os.path.join(REPO, path)
        rows, refused, chi = arm_rows(path, keys)
        arms.append((name, rows, refused, chi))
        for k, n, why in refused:
            print('⚠ %s: %s %s — %s' % (name, k, n, why))
    base = arms[0][1]
    allk = sorted(set().union(*[set(x[1]) for x in arms]),
                  key=lambda kn: (base.get(kn, {}).get('det', ''), base.get(kn, {}).get('group', ''), kn))
    names = [x[0] for x in arms]
    print('%-24s %-7s %-10s ' % ('спектр', 'нуклид', 'группа') +
          ' '.join('%7s' % n for n in names) + '  ' + ' '.join('Δ%-6s' % n for n in names[1:]) +
          '  ' + ' '.join('χ²%-5s' % n for n in names))
    out = []
    grp = defaultdict(lambda: defaultdict(list))
    for kn in allk:
        r0 = base.get(kn)
        vals = [x[1].get(kn, {}).get('ratio', float('nan')) for x in arms]
        chis = [x[3].get(kn[0], float('nan')) for x in arms]
        d = [100.0 * (v / vals[0] - 1.0) if vals[0] == vals[0] else float('nan') for v in vals[1:]]
        g = (r0 or {}).get('det', '?') + ' ' + (r0 or {}).get('group', '?')
        tol = (r0 or {}).get('tol', float('nan'))
        print('%-24s %-7s %-10s ' % (kn[0], kn[1], (r0 or {}).get('group', '?')) +
              ' '.join('%7.3f' % v for v in vals) + '  ' + ' '.join('%+6.2f%%' % x for x in d) +
              '  ' + ' '.join('%7.2f' % c for c in chis))
        for n, v in zip(names, vals):
            grp[g][n].append((v, abs(v - 1.0) > tol))
        out.append([kn[0], kn[1], g, '%.3f' % tol] + ['%.5f' % v for v in vals] + ['%.3f' % x for x in d] +
                   ['%.4f' % c for c in chis])
    print()
    print('по группам: медиана [вне порога]')
    for g in sorted(grp):
        print('  %-18s ' % g + '  '.join('%s %.3f [%d/%d]' % (n, statistics.median([v for v, _ in grp[g][n]]),
                                                            sum(1 for _, o in grp[g][n] if o), len(grp[g][n]))
                                        for n in names if grp[g][n]))
    tot = {n: sum(1 for g in grp for _, o in grp[g][n] if o) for n in names}
    print('вне порога всего: ' + ', '.join('%s %d' % (n, tot[n]) for n in names))
    if a.csv:
        with io.open(a.csv, 'w', encoding='utf-8-sig', newline='') as fh:
            w = csv.writer(fh)
            w.writerow(['spectrum', 'nuclide', 'group', 'tol'] + ['ratio_' + n for n in names] +
                       ['dpct_' + n for n in names[1:]] + ['chi2pois_' + n for n in names])
            w.writerows(out)


if __name__ == '__main__':
    main()
