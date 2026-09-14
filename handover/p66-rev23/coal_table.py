# -*- coding: utf-8 -*-
"""П66 — четыре спектра угля в каталогах прогона: χ²/ndf решателя, отчётный χ²п, невязка, матрица, и состав
(компонент: доля %, z, decay_s) — из `G1S24_spline_runs.csv` / `G1S24_spline_components.csv`.

    python handover/p66-rev23/coal_table.py <каталог> [<каталог> …]
"""
import csv, io, os, sys
sys.stdout.reconfigure(encoding='utf-8')
KEYS = ['G1S24_Rn222Coal_Mar_20m', 'G1S24_Rn222Coal_Mar_2h', 'G1S24_Rn222Coal_Mar_3h', 'G1S24_Rn222Coal_Mar_eq01']


def main():
    for d in sys.argv[1:]:
        runs = {r['spectrum']: r for r in csv.DictReader(io.open(os.path.join(d, 'G1S24_spline_runs.csv'), encoding='utf-8-sig', newline=''))}
        comps = {}
        for r in csv.DictReader(io.open(os.path.join(d, 'G1S24_spline_components.csv'), encoding='utf-8-sig', newline='')):
            comps.setdefault(r['spectrum'], []).append(r)
        print('== %s ==' % os.path.basename(d))
        for k in KEYS:
            r = runs.get(k)
            if r is None:
                print('  %-26s нет в каталоге' % k); continue
            print('  %-26s χ²/ndf %s  χ²п %s  нев %s %%  матрица %s (%s)  компонент %s' % (
                k, r['chi2ndf'], r['chi2ndf_pois'], r['model_residual_pct'], r['matrix_applied'], r['matrix_note'], r['components']))
            for c in sorted(comps.get(k, []), key=lambda c: -float(c['share_pct'] or 0)):
                if float(c['share_pct'] or 0) >= 0.05:
                    print('      %-14s %-7s доля %6s %%  z %7s  decay_s %s' % (c['component'], c['kind'], c['share_pct'], c['z'], c['decay_s']))
    return 0


if __name__ == '__main__':
    sys.exit(main())
