# -*- coding: utf-8 -*-
"""П34 12.09.2026, `A309` — построчная разность двух каталогов прогона по `*_spline_runs.csv` (χ²/ndf решателя,
пуассоновский χ²/ndf, невязка) и по составу (`*_spline_components.csv`: доля/z).

    python handover/p34-fit-floor/per_spectrum.py tools/pie/out_p34_full_off tools/pie/out_p34_full_thr
"""
import csv, glob, io, os, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
parts = {r['spectrum']: r['part'] for r in csv.DictReader(io.open(os.path.join(ROOT, 'tools/CORPUS/corpus/parts.csv'), encoding='utf-8-sig', newline=''))}
def runs(d):
    out = {}
    for p in glob.glob(os.path.join(d, '*_spline_runs.csv')):
        for r in csv.DictReader(io.open(p, encoding='utf-8-sig', newline='')):
            out[r['spectrum']] = r
    return out
def comps(d):
    out = {}
    for p in glob.glob(os.path.join(d, '*_spline_components.csv')):
        for r in csv.DictReader(io.open(p, encoding='utf-8-sig', newline='')):
            out[(r['spectrum'], r['component'])] = r
    return out
A, B = sys.argv[1], sys.argv[2]
a, b = runs(A), runs(B); ca, cb = comps(A), comps(B)
print('спектры, у которых B отличается от A (маска ms/cpu_ms): A=%s B=%s' % (os.path.basename(A), os.path.basename(B)))
print('%-24s %-7s %9s %8s %8s | %8s %8s | %6s %6s' % ('спектр', 'часть', 'χ²ndf A', 'B', 'Δ', 'χ²p A', 'B', 'нев A', 'B'))
sums = {}
for k in sorted(a):
    if k not in b: print(k, 'нет в B'); continue
    ra, rb = a[k], b[k]
    if all(ra[c] == rb[c] for c in ra if c not in ('ms', 'cpu_ms')): continue
    part = parts.get(k, '?')
    d = float(rb['chi2ndf']) - float(ra['chi2ndf'])
    sums.setdefault(part, [0, 0.0, 0, 0])
    sums[part][0] += 1; sums[part][1] += d; sums[part][2] += d > 0.005; sums[part][3] += d < -0.005
    print('%-24s %-7s %9.3f %8.3f %+8.3f | %8.1f %8.1f | %6.1f %6.1f' % (k, part, float(ra['chi2ndf']), float(rb['chi2ndf']), d, float(ra['chi2ndf_pois']), float(rb['chi2ndf_pois']), float(ra['model_residual_pct']), float(rb['model_residual_pct'])))
for part, (n, d, w, bt) in sums.items():
    print('  часть %s: изменилось %d, Σ Δχ²ndf %+.3f, хуже %d, лучше %d' % (part, n, d, w, bt))
print()
print('состав: компоненты, у которых доля или z изменились (A → B):')
for key in sorted(set(ca) | set(cb)):
    x, y = ca.get(key), cb.get(key)
    if x is None: print('  %-22s %-10s ТОЛЬКО В B: %s/%s' % (key[0], key[1], y['share_pct'], y['z'])); continue
    if y is None: print('  %-22s %-10s ТОЛЬКО В A: %s/%s' % (key[0], key[1], x['share_pct'], x['z'])); continue
    if x['share_pct'] != y['share_pct'] or x['z'] != y['z']:
        print('  %-22s %-10s %s/%s → %s/%s' % (key[0], key[1], x['share_pct'], x['z'], y['share_pct'], y['z']))
