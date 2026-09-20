# -*- coding: utf-8 -*-
"""П93 (`S176`): лестница rev26 -> rev27 по спектрам — χ²/ndf, состав, кто сдвинулся, кто побитово.

    python handover/p93-s176/ladder.py out_rev26_full out_rev27_full [--part=known|unknown|all]

Сдвинулся = изменилось χ²/ndf (решателя или Пирсона), gain, состав (share_pct > 0.05 п.п.)
или любой столбец runs/components/limits/anchors кроме ms/cpu_ms. Печатает таблицу сдвинувшихся
с Δ % и составом, список побитовых, Σχ²/ndf по части (разобранные, chi2ndf числом).
"""
import csv, glob, io, os, sys
for s in (sys.stdout,): s.reconfigure(encoding='utf-8', errors='replace')
REPO = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'   # П103: скрипт лежит вне дерева
root = os.path.join(REPO, 'tools', 'pie')
TIME = {'ms', 'cpu_ms'}
A = sys.argv[1] if os.path.isabs(sys.argv[1]) else os.path.join(root, sys.argv[1])
B = sys.argv[2] if os.path.isabs(sys.argv[2]) else os.path.join(root, sys.argv[2])
part = 'all'
for a in sys.argv[3:]:
    if a.startswith('--part='): part = a[7:]

def table(d, t):
    out = {}
    for p in glob.glob(os.path.join(d, '*_spline_%s.csv' % t)):
        with io.open(p, encoding='utf-8-sig', newline='') as f:
            rows = list(csv.reader(f))
        if not rows: continue
        head = rows[0]
        for r in rows[1:]:
            dd = dict(zip(head, r))
            out.setdefault(dd.get('spectrum', ''), []).append(dd)
    return out

def diffcols(ra, rb):
    """Столбцы, разошедшиеся между списками строк одного спектра (по позиции после сортировки)."""
    ka = sorted(ra, key=lambda d: tuple(d.get(k, '') for k in ('component', 'line', 'energy', 'anchor', 'E', 'kev')))
    kb = sorted(rb, key=lambda d: tuple(d.get(k, '') for k in ('component', 'line', 'energy', 'anchor', 'E', 'kev')))
    if len(ka) != len(kb): return {'<число строк %d→%d>' % (len(ka), len(kb))}
    cols = set()
    for x, y in zip(ka, kb):
        for k in set(x) | set(y):
            if k in TIME: continue
            if x.get(k, '') != y.get(k, ''): cols.add(k)
    return cols

ta = {t: table(A, t) for t in ('runs', 'components', 'limits', 'anchors')}
tb = {t: table(B, t) for t in ('runs', 'components', 'limits', 'anchors')}
runs_a = {k: v[0] for k, v in ta['runs'].items()}; runs_b = {k: v[0] for k, v in tb['runs'].items()}
keys = sorted(set(runs_a) & set(runs_b))
moved = []; same = []; sum_a = sum_b = 0.0; n = 0
print('%-28s %-8s %10s %10s %8s  %s' % ('спектр', 'часть', 'χ²/ndf до', 'после', 'Δ %', 'что разошлось; состав (доля %, до → после, > 0.05 п.п.)'))
for k in keys:
    x, y = runs_a[k], runs_b[k]
    if part != 'all' and x.get('part') != part: continue
    cols = set()
    for t in ta:
        cols |= {t + '.' + c for c in diffcols(ta[t].get(k, []), tb[t].get(k, []))}
    ch = []
    ca = {d['component']: d for d in ta['components'].get(k, [])}
    cb = {d['component']: d for d in tb['components'].get(k, [])}
    for comp in sorted(set(ca) | set(cb)):
        try:
            sa = float(ca.get(comp, {}).get('share_pct', 0) or 0); sb = float(cb.get(comp, {}).get('share_pct', 0) or 0)
        except ValueError: continue
        if abs(sa - sb) > 0.05: ch.append('%s %.2f→%.2f' % (comp, sa, sb))
    try:
        fa, fb = float(x['chi2ndf']), float(y['chi2ndf']); sum_a += fa; sum_b += fb; n += 1
    except ValueError:
        fa = fb = float('nan')
    if not cols:
        same.append(k); continue
    moved.append(k)
    d = (fb / fa - 1) * 100 if fa == fa and fa else float('nan')
    print('%-28s %-8s %10.4f %10.4f %+8.3f  %s%s' % (k, x.get('part', ''), fa, fb, d,
          ', '.join(sorted(c.split('.', 1)[1] for c in cols if not c.startswith('components.') or 'share' not in c)[:6]),
          ('; ' + '; '.join(ch)) if ch else ''))
print('\n%s: разобранных %d, Σχ²/ndf %.4f -> %.4f (%+.4f %%)' % (part, n, sum_a, sum_b, (sum_b / sum_a - 1) * 100 if sum_a else 0))
print('сдвинулось %d, побитово %d' % (len(moved), len(same)))
print('побитово: ' + ', '.join(same))
